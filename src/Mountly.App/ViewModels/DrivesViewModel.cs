using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mountly.Core.Mounting;
using Mountly.Core.Storage;
using Mountly.Windows;

namespace Mountly.App.ViewModels;

/// <summary>
/// The Drives page: the user's virtual drives (slots) and every mounted image. Windows has no device for
/// an empty drive, so a slot is Mountly's record of a drive (its letter and last image); loading an image
/// mounts it, ejecting unmounts it and keeps the slot.
/// </summary>
public sealed partial class DrivesViewModel : ObservableObject
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _poll;
    private bool _refreshing;

    public DrivesViewModel(AppServices services)
    {
        Services = services;
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += async (_, _) => await RefreshAsync();
    }

    public AppServices Services { get; }

    public ObservableCollection<DriveViewModel> Drives { get; } = [];

    /// <summary>Raised after every refresh, so the tray menu can follow.</summary>
    public event EventHandler? Changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(HasMounted))]
    public partial int MountedCount { get; private set; }

    public string Summary => MountedCount switch
    {
        0 => "No images mounted",
        1 => "1 image mounted",
        var n => $"{n} images mounted",
    };

    public bool HasMounted => MountedCount > 0;

    public bool IsEmpty => Drives.Count == 0;

    /// <summary>Keeps the list in step with Windows (images ejected in Explorer, for example) while the window is visible.</summary>
    public void SetPolling(bool active)
    {
        if (active) _poll.Start();
        else _poll.Stop();
    }

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            IReadOnlyList<MountedImage> mounted;
            try
            {
                mounted = await Services.Mounts.ListAsync();
            }
            catch (MountException ex)
            {
                Services.Notices.Error("Couldn't read the mounted images", ex.Message);
                return;
            }

            var slots = Services.Slots.Load();
            var wanted = new List<(DriveSlot? Slot, MountedImage? Mounted)>();
            var used = new HashSet<MountedImage>();
            foreach (var slot in slots)
            {
                var match = slot.ImagePath is { } path ? Services.Mounts.Find(mounted.Except(used), path) : null;
                if (match is not null) used.Add(match);
                wanted.Add((slot, match));
            }

            foreach (var image in mounted.Where(m => !used.Contains(m)).OrderBy(m => m.FirstLetter)) wanted.Add((null, image));
            Reconcile(wanted);
            MountedCount = mounted.Count;
            OnPropertyChanged(nameof(IsEmpty));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshing = false;
        }
    }

    // Updates cards in place (so busy states and focus survive a refresh), adding and removing as needed.
    private void Reconcile(List<(DriveSlot? Slot, MountedImage? Mounted)> wanted)
    {
        var byKey = Drives.ToDictionary(d => d.Key);
        var next = new List<DriveViewModel>();
        foreach (var (slot, image) in wanted)
        {
            var key = slot is not null ? slot.Id.ToString() : "ext:" + image?.ImagePath;
            if (byKey.TryGetValue(key, out var existing)) existing.Update(slot, image);
            else existing = new DriveViewModel(this, slot, image);
            next.Add(existing);
        }

        for (var i = Drives.Count - 1; i >= 0; i--)
            if (!next.Contains(Drives[i])) Drives.RemoveAt(i);
        for (var i = 0; i < next.Count; i++)
        {
            var at = Drives.IndexOf(next[i]);
            if (at < 0) Drives.Insert(i, next[i]);
            else if (at != i) Drives.Move(at, i);
        }
    }

    /// <summary>Mounts images into free drives, adding drives when all are in use.</summary>
    public async Task MountImagesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!Services.Mounts.CanMount(path))
            {
                Services.Notices.Error($"Mountly can't mount '{Path.GetFileName(path)}'",
                    "Supported: ISO, VHD, VHDX, BIN/CUE, NRG, MDS/MDF, CloneCD (CCD/IMG) and raw BIN/IMG data images.");
                continue;
            }

            await RefreshAsync();
            if (Services.Mounts.Find(Drives.Where(d => d.Mounted is not null).Select(d => d.Mounted!), path) is not null)
            {
                Services.Notices.Show(NoticeKind.Info, $"'{Path.GetFileName(path)}' is already mounted");
                continue;
            }

            var slot = Drives.FirstOrDefault(d => d.Slot is not null && !d.IsMounted && !d.IsBusy && !d.Slot.Loaded)?.Slot;
            var slotId = slot?.Id ?? AddSlot().Id;
            await MountIntoAsync(slotId, path);
        }
    }

    public async Task LoadIntoAsync(DriveViewModel drive)
    {
        var picked = await Services.Dialogs.PickImagesAsync(multiple: false);
        if (picked.Count == 0) return;
        if (drive.IsMounted) await EjectAsync(drive);
        if (drive.Slot is { } slot) await MountIntoAsync(slot.Id, picked[0]);
    }

    public Task ReloadAsync(DriveViewModel drive) =>
        drive.Slot is { ImagePath: { } path } slot ? MountIntoAsync(slot.Id, path) : Task.CompletedTask;

    /// <summary>Mounts the slots that held an image when Mountly last ran (Windows forgets mounts on restart).</summary>
    public async Task RemountSavedAsync()
    {
        await RefreshAsync();
        foreach (var drive in Drives.Where(d => d.Slot is { Loaded: true, ImagePath: not null } && !d.IsMounted).ToList())
        {
            if (File.Exists(drive.Slot!.ImagePath)) await MountIntoAsync(drive.Slot.Id, drive.Slot.ImagePath!, quiet: true);
        }
    }

    private async Task MountIntoAsync(Guid slotId, string path, bool quiet = false)
    {
        var slots = Services.Slots.Update(list =>
        {
            if (list.FirstOrDefault(s => s.Id == slotId) is { } s)
            {
                s.ImagePath = Path.GetFullPath(path);
                s.Loaded = true;
            }
        });
        var slot = slots.First(s => s.Id == slotId);
        await RefreshAsync();
        var card = Drives.FirstOrDefault(d => d.Slot?.Id == slotId);
        if (card is not null)
        {
            card.IsBusy = true;
            card.BusyText = "Mounting…";
            card.HasProgress = false;
        }

        try
        {
            var progress = new Progress<MountProgress>(p =>
            {
                if (card is null) return;
                card.BusyText = p.Total > 0 ? $"{p.Stage} {p.Fraction:P0}" : p.Stage + "…";
                card.HasProgress = p.Total > 0;
                card.Progress = p.Fraction;
            });
            var mounted = await Services.Mounts.MountAsync(path, new MountOptions { PreferredLetter = slot.PreferredLetter }, progress);
            Services.Catalog.Touch(path);
            if (slot.PreferredLetter is { } wanted && mounted.FirstLetter is { } got && char.ToUpperInvariant(got[0]) != wanted)
                Services.Notices.Warning($"Mounted as {got} instead of {wanted}:", "Choosing a drive letter needs administrator rights.",
                    new NoticeAction($"Move to {wanted}: as administrator", new AsyncRelayCommand(() => MoveLetterElevatedAsync(got[0], wanted))));
            else if (!quiet)
                Services.Notices.Success($"Mounted {Path.GetFileName(path)}",
                    mounted.FirstLetter is { } l ? $"It's in {l}" + (mounted.IsConverted ? " (converted to ISO first)." : ".") : null);
        }
        catch (MountException ex) when (ex.Error == MountError.NeedsElevation)
        {
            Services.Notices.Warning($"'{Path.GetFileName(path)}' needs administrator rights", ex.Message,
                new NoticeAction("Mount as administrator", new AsyncRelayCommand(() => MountElevatedAsync(slotId, path))));
        }
        catch (MountException ex) when (ex.Error == MountError.AlreadyMounted)
        {
            Services.Notices.Show(NoticeKind.Info, ex.Message);
        }
        catch (Exception ex) when (ex is MountException or IOException or UnauthorizedAccessException)
        {
            SetLoaded(slotId, false);
            Services.Notices.Error($"Couldn't mount {Path.GetFileName(path)}", ex.Message);
        }
        finally
        {
            if (card is not null) card.IsBusy = false;
            await RefreshAsync();
        }
    }

    private async Task MountElevatedAsync(Guid slotId, string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var slot = Services.Slots.Load().FirstOrDefault(s => s.Id == slotId);
        var args = new List<string> { "mount", path };
        if (slot?.PreferredLetter is { } l)
        {
            args.Add("--letter");
            args.Add(l.ToString());
        }

        var result = await ElevatedCli.RunAsync(args);
        if (result is null) return; // UAC declined
        if (result.Ok)
        {
            Services.Catalog.Touch(path);
            Services.Notices.Success($"Mounted {Path.GetFileName(path)}", result.Images.FirstOrDefault()?.FirstLetter is { } x ? $"It's in {x}." : null);
        }
        else
        {
            SetLoaded(slotId, false);
            Services.Notices.Error($"Couldn't mount {Path.GetFileName(path)}", result.Message);
        }

        await RefreshAsync();
    }

    public async Task EjectAsync(DriveViewModel drive)
    {
        if (drive.Mounted is not { } image) return;
        drive.IsBusy = true;
        drive.BusyText = "Ejecting…";
        try
        {
            await Services.Mounts.UnmountAsync(image.ImagePath);
            if (drive.Slot is { } slot) SetLoaded(slot.Id, false);
        }
        catch (MountException ex) when (ex.Error == MountError.NotMounted)
        {
            if (drive.Slot is { } slot) SetLoaded(slot.Id, false);
        }
        catch (MountException ex)
        {
            Services.Notices.Error($"Couldn't eject {drive.Letter}", ex.Message +
                (ex.Error == MountError.FileInUse ? " Close the programs using it and try again." : ""));
        }
        finally
        {
            drive.IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task EjectAll()
    {
        foreach (var drive in Drives.Where(d => d.IsMounted).ToList()) await EjectAsync(drive);
    }

    [RelayCommand]
    private async Task MountImage()
    {
        var picked = await Services.Dialogs.PickImagesAsync();
        if (picked.Count > 0) await MountImagesAsync(picked);
    }

    [RelayCommand]
    private async Task AddDrive()
    {
        AddSlot();
        await RefreshAsync();
    }

    private DriveSlot AddSlot()
    {
        var slot = new DriveSlot();
        Services.Slots.Update(list => list.Add(slot));
        return slot;
    }

    public async Task RemoveDriveAsync(DriveViewModel drive)
    {
        if (drive.IsMounted) await EjectAsync(drive);
        if (drive.Slot is { } slot) Services.Slots.Update(list => list.RemoveAll(s => s.Id == slot.Id));
        await RefreshAsync();
    }

    /// <summary>Turns an image mounted elsewhere into one of Mountly's drives (remounted at start, for example).</summary>
    public void Adopt(DriveViewModel drive)
    {
        if (drive.Mounted is not { } image) return;
        Services.Slots.Update(list => list.Add(new DriveSlot { ImagePath = image.ImagePath, Loaded = true }));
        _ = RefreshAsync();
    }

    public const string AutoLetter = "Next free letter";

    public IReadOnlyList<string> LetterChoices(DriveViewModel drive)
    {
        var free = OperatingSystem.IsWindows() ? DriveLetters.Free() : [];
        var letters = new SortedSet<char>(free);
        if (drive.Mounted?.FirstLetter is { } current) letters.Add(char.ToUpperInvariant(current[0]));
        if (drive.Slot?.PreferredLetter is { } preferred) letters.Add(preferred);
        return [AutoLetter, .. letters.Select(c => c + ":")];
    }

    public async Task SetLetterAsync(DriveViewModel drive, string? choice)
    {
        if (drive.Slot is not { } slot) return;
        char? letter = choice is { Length: >= 1 } c && char.IsAsciiLetter(c[0]) && c.Length <= 2 ? char.ToUpperInvariant(c[0]) : null;
        Services.Slots.Update(list =>
        {
            if (list.FirstOrDefault(s => s.Id == slot.Id) is { } s) s.PreferredLetter = letter;
        });

        if (letter is { } to && drive.Mounted?.FirstLetter is { } current && char.ToUpperInvariant(current[0]) != to && OperatingSystem.IsWindows())
        {
            if (DriveLetters.TryMove(current[0], to, out var error))
                Services.Notices.Success($"Moved {current} to {to}:");
            else if (!DriveLetters.IsElevated)
                await MoveLetterElevatedAsync(current[0], to);
            else
                Services.Notices.Error($"Couldn't move {current} to {to}:", error);
        }

        await RefreshAsync();
    }

    private async Task MoveLetterElevatedAsync(char from, char to)
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await ElevatedCli.RunAsync(["letter", from + ":", to + ":"]);
        if (result is null) return;
        if (result.Ok) Services.Notices.Success($"Moved {from}: to {to}:");
        else Services.Notices.Error($"Couldn't move {from}: to {to}:", result.Message);
        await RefreshAsync();
    }

    private void SetLoaded(Guid slotId, bool loaded) => Services.Slots.Update(list =>
    {
        if (list.FirstOrDefault(s => s.Id == slotId) is { } s) s.Loaded = loaded;
    });
}
