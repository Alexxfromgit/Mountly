using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mountly.Core.Images;
using Mountly.Core.Util;
using Mountly.Formats;
using Mountly.Tools;
using AppIcons = Mountly.App.Controls.Icons;

namespace Mountly.App.ViewModels.Tools;

/// <summary>Tools that produce an ISO offer to mount it when they finish.</summary>
public abstract partial class IsoProducingTool(AppServices services, DrivesViewModel drives) : ToolViewModel(services)
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    public partial string? OutputPath { get; set; }

    [ObservableProperty]
    public partial bool CanMountOutput { get; private set; }

    protected abstract bool Ready { get; }
    protected abstract Task<ToolOutcome> WorkAsync(IProgress<(long Done, long Total)> progress, CancellationToken cancel);
    protected abstract string Verb { get; }

    private bool CanRun => Ready && !string.IsNullOrWhiteSpace(OutputPath) && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run()
    {
        CanMountOutput = false;
        await RunAsync(Verb, WorkAsync);
    }

    protected override Task OnSucceededAsync()
    {
        CanMountOutput = OutputPath is { } p && File.Exists(p);
        if (OutputPath is { } path) Services.Catalog.Touch(path);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task MountOutput() => OutputPath is { } p ? drives.MountImagesAsync([p]) : Task.CompletedTask;

    [RelayCommand]
    private void RevealOutput()
    {
        if (OutputPath is { } p && File.Exists(p)) global::Mountly.App.Services.DesktopIntegration.Reveal(p);
    }

    [RelayCommand]
    private async Task PickOutput()
    {
        var suggested = OutputPath is { } current ? Path.GetFileName(current) : "image.iso";
        if (await Services.Dialogs.SaveFileAsync("Save ISO as", suggested, "ISO image", ".iso") is { } path) OutputPath = path;
    }

    protected void ReevaluateRun() => RunCommand.NotifyCanExecuteChanged();
}

public sealed partial class CreateIsoTool(AppServices services, DrivesViewModel drives) : IsoProducingTool(services, drives)
{
    public override string Name => "Create ISO";
    public override string Description => "Build an ISO (ISO 9660 + Joliet) from a folder";
    public override Geometry Icon => AppIcons.CreateIso;
    protected override string Verb => "Building";

    [ObservableProperty]
    public partial string? SourceFolder { get; set; }

    [ObservableProperty]
    public partial string? VolumeLabel { get; set; }

    [ObservableProperty]
    public partial string? BootImage { get; set; }

    protected override bool Ready => Directory.Exists(SourceFolder);

    partial void OnSourceFolderChanged(string? value)
    {
        if (value is null) return;
        VolumeLabel ??= IsoBuilder.SanitizeLabel(new DirectoryInfo(value).Name);
        OutputPath ??= SuggestOutput(value, ".iso");
        ReevaluateRun();
    }

    [RelayCommand]
    private async Task PickSource()
    {
        if (await Services.Dialogs.PickFolderAsync("Choose the folder to put in the ISO") is { } folder) SourceFolder = folder;
    }

    [RelayCommand]
    private async Task PickBoot()
    {
        BootImage = await Services.Dialogs.PickFileAsync("Choose a boot image (El Torito, no emulation)", "Boot images", ["*.bin", "*.img"]);
    }

    protected override async Task<ToolOutcome> WorkAsync(IProgress<(long Done, long Total)> progress, CancellationToken cancel)
    {
        var result = await IsoBuilder.BuildAsync(SourceFolder!, OutputPath!,
            new IsoBuildOptions { VolumeLabel = VolumeLabel, BootImagePath = string.IsNullOrWhiteSpace(BootImage) ? null : BootImage }, progress, cancel);
        var skipped = result.Skipped.Count > 0 ? $" Skipped {result.Skipped.Count} item(s) with names over {IsoBuilder.MaxNameLength} characters." : "";
        return new ToolOutcome($"Created {Path.GetFileName(OutputPath)}: {result.Files} files, {ByteSize.Format(result.IsoBytes)}.{skipped}");
    }
}

public sealed partial class ConvertTool(AppServices services, DrivesViewModel drives) : IsoProducingTool(services, drives)
{
    public override string Name => "Convert to ISO";
    public override string Description => "Turn BIN/CUE, NRG, MDS/MDF or CloneCD images into ISO";
    public override Geometry Icon => AppIcons.Convert;
    protected override string Verb => "Converting";

    [ObservableProperty]
    public partial string? SourcePath { get; set; }

    [ObservableProperty]
    public partial string? Layout { get; private set; }

    protected override bool Ready => File.Exists(SourcePath) && Layout is not null;

    partial void OnSourcePathChanged(string? value)
    {
        Layout = null;
        Result = null;
        if (value is null) return;
        try
        {
            var layout = OpticalImage.ReadLayout(value);
            var tracks = string.Join("\n", layout.Tracks.Select(t => $"Track {t.Number}: {t.ModeName}, {ByteSize.Format(t.Length)}"));
            var warnings = IsoConverter.Warnings(layout);
            Layout = $"{layout.Format.DisplayName()} · {layout.Tracks.Count} track(s)\n{tracks}" + (warnings.Count > 0 ? "\n" + string.Join("\n", warnings) : "");
            OutputPath = SuggestOutput(value, ".iso");
        }
        catch (Exception ex) when (ex is ImageFormatException or IOException or UnauthorizedAccessException)
        {
            Result = ex.Message;
            ResultOk = false;
        }

        ReevaluateRun();
    }

    [RelayCommand]
    private async Task PickSource()
    {
        if (await Services.Dialogs.PickFileAsync("Choose an image to convert", "CD/DVD images",
                ["*.cue", "*.bin", "*.nrg", "*.mds", "*.mdf", "*.ccd", "*.img"]) is { } path) SourcePath = path;
    }

    protected override async Task<ToolOutcome> WorkAsync(IProgress<(long Done, long Total)> progress, CancellationToken cancel)
    {
        var layout = OpticalImage.ReadLayout(SourcePath!);
        var total = layout.FirstDataTrack?.Length ?? 0;
        var warnings = await new IsoConverter().ConvertToIsoAsync(SourcePath!, OutputPath!,
            new Progress<long>(done => progress.Report((done, total))), cancel);
        return new ToolOutcome($"Wrote {Path.GetFileName(OutputPath)} ({ByteSize.Format(new FileInfo(OutputPath!).Length)})." +
                               (warnings.Count > 0 ? " " + string.Join(" ", warnings) : ""));
    }
}

public sealed partial class RipTool(AppServices services, DrivesViewModel drives) : IsoProducingTool(services, drives)
{
    public override string Name => "Disc to ISO";
    public override string Description => "Copy a data CD, DVD or Blu-ray in a drive to an ISO";
    public override Geometry Icon => AppIcons.Rip;
    protected override string Verb => "Reading";

    public System.Collections.ObjectModel.ObservableCollection<string> Drives { get; } = [];

    [ObservableProperty]
    public partial string? SelectedDrive { get; set; }

    protected override bool Ready => SelectedDrive is not null;

    partial void OnSelectedDriveChanged(string? value)
    {
        if (value is not null)
        {
            var label = value.Split(' ', 2) is { Length: 2 } parts ? parts[1].Trim('(', ')') : "disc";
            OutputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), (label.Length > 0 ? label : "disc") + ".iso");
        }

        ReevaluateRun();
    }

    [RelayCommand]
    public void RefreshDrives()
    {
        Drives.Clear();
        if (!OperatingSystem.IsWindows()) return;
        foreach (var d in Windows.DiscRipper.OpticalDrives())
            Drives.Add($"{d.Name.TrimEnd('\\')} ({d.VolumeLabel})");
        SelectedDrive = Drives.FirstOrDefault();
    }

    protected override async Task<ToolOutcome> WorkAsync(IProgress<(long Done, long Total)> progress, CancellationToken cancel)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var result = await Windows.DiscRipper.RipAsync(SelectedDrive![0], OutputPath!, progress, cancel);
        return result.UnreadableSectors > 0
            ? new ToolOutcome($"Wrote {Path.GetFileName(OutputPath)}, but {result.UnreadableSectors} sector(s) couldn't be read and are zeros.", Ok: false)
            : new ToolOutcome($"Wrote {Path.GetFileName(OutputPath)} ({ByteSize.Format(result.Bytes)}).");
    }
}
