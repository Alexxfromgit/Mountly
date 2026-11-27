using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mountly.App.Services;
using Mountly.Core.Images;
using Mountly.Core.Mounting;
using Mountly.Core.Storage;
using Mountly.Core.Util;

namespace Mountly.App.ViewModels;

/// <summary>
/// One card on the Drives page: a virtual drive the user created (a slot), loaded or empty, or an image
/// mounted outside Mountly (by Explorer, for example).
/// </summary>
public sealed partial class DriveViewModel : ObservableObject
{
    private readonly DrivesViewModel _owner;

    public DriveViewModel(DrivesViewModel owner, DriveSlot? slot, MountedImage? mounted)
    {
        _owner = owner;
        Update(slot, mounted);
    }

    /// <summary>The slot id, or the image path for images mounted outside Mountly.</summary>
    public string Key => Slot is { } s ? s.Id.ToString() : "ext:" + Mounted?.ImagePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExternal), nameof(ImagePath), nameof(ImageName), nameof(HasImage), nameof(PreferredLetterText))]
    public partial DriveSlot? Slot { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMounted), nameof(Letter), nameof(Title), nameof(ImagePath), nameof(ImageName), nameof(Details),
        nameof(Status), nameof(HasImage), nameof(CanOpen))]
    public partial MountedImage? Mounted { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial string? BusyText { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool HasProgress { get; set; }

    public bool IsIdle => !IsBusy;
    public bool IsMounted => Mounted is not null;
    public bool IsExternal => Slot is null;
    public bool CanOpen => Mounted?.FirstLetter is not null;
    public string? ImagePath => Mounted?.ImagePath ?? Slot?.ImagePath;
    public bool HasImage => ImagePath is not null;
    public string ImageName => ImagePath is { } p ? Path.GetFileName(p) : "No image";

    public string Letter => Mounted?.FirstLetter ?? (Slot?.PreferredLetter is { } l ? l + ":" : "—");

    public string Title => Mounted?.VolumeLabel is { Length: > 0 } label ? label
        : ImagePath is { } p ? Path.GetFileNameWithoutExtension(p)
        : Slot?.Name ?? "Empty drive";

    public string Details => Mounted is { } m
        ? $"{m.Format.DisplayName()} · {ByteSize.Format(m.SizeBytes)}{(m.IsConverted ? " · converted" : "")}"
        : ImagePath is { } p ? ImageFormats.Detect(p).DisplayName() : "Drop an image here or load one";

    public string Status => IsBusy ? BusyText ?? "Working…"
        : Mounted is not null ? (IsExternal ? "Mounted by Windows" : "Mounted")
        : HasImage ? "Ejected"
        : "Empty";

    public string PreferredLetterText => Slot?.PreferredLetter is { } l ? $"Always {l}:" : "Next free letter";

    /// <summary>The drive-letter submenu: "Next free letter" and the letters that are free now.</summary>
    public IReadOnlyList<LetterChoice> LetterMenu
    {
        get
        {
            var current = Slot?.PreferredLetter is { } l ? l + ":" : DrivesViewModel.AutoLetter;
            return _owner.LetterChoices(this).Select(t => new LetterChoice(t == current ? "✓  " + t : "     " + t, t, SetLetterCommand)).ToList();
        }
    }

    public void Update(DriveSlot? slot, MountedImage? mounted)
    {
        OnPropertyChanged(nameof(LetterMenu));
        Slot = slot;
        Mounted = mounted;
        OnPropertyChanged(nameof(Letter));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(Status));
    }

    [RelayCommand]
    private Task Eject() => _owner.EjectAsync(this);

    [RelayCommand]
    private Task Load() => _owner.LoadIntoAsync(this);

    [RelayCommand]
    private Task Reload() => _owner.ReloadAsync(this);

    [RelayCommand]
    private void Open()
    {
        if (Mounted?.FirstLetter is { } letter) DesktopIntegration.Open(letter + "\\");
    }

    [RelayCommand]
    private void Reveal()
    {
        if (ImagePath is { } p && File.Exists(p)) DesktopIntegration.Reveal(p);
    }

    [RelayCommand]
    private Task CopyPath() => ImagePath is { } p ? _owner.Services.Dialogs.SetClipboardTextAsync(p) : Task.CompletedTask;

    [RelayCommand]
    private Task Remove() => _owner.RemoveDriveAsync(this);

    [RelayCommand]
    private void Keep() => _owner.Adopt(this);

    [RelayCommand]
    private Task SetLetter(string? choice) => _owner.SetLetterAsync(this, choice);
}

public sealed record LetterChoice(string Header, string Value, System.Windows.Input.ICommand Command);
