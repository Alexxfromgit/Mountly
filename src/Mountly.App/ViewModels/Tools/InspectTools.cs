using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mountly.Core.Images;
using Mountly.Core.Util;
using Mountly.Tools;
using AppIcons = Mountly.App.Controls.Icons;

namespace Mountly.App.ViewModels.Tools;

public sealed class EntryViewModel(ImageEntry entry)
{
    public ImageEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public bool IsDirectory => Entry.IsDirectory;
    public Geometry Icon => Entry.IsDirectory ? AppIcons.Folder : AppIcons.File;
    public string SizeText => Entry.IsDirectory ? "" : ByteSize.Format(Entry.Size);
    public string ModifiedText => Entry.ModifiedUtc?.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) ?? "";
}

/// <summary>Browses the files inside an image and extracts them, without mounting.</summary>
public sealed partial class BrowseTool(AppServices services) : ToolViewModel(services)
{
    private ImageInspector? _inspector;

    public override string Name => "Browse image";
    public override string Description => "Look inside an image and copy files out without mounting it";
    public override Geometry Icon => AppIcons.Browse;

    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial string? ImagePath { get; set; }

    [ObservableProperty]
    public partial string Folder { get; private set; } = "";

    [ObservableProperty]
    public partial string? Summary { get; private set; }

    [ObservableProperty]
    public partial EntryViewModel? Selected { get; set; }

    public bool IsOpen => _inspector is not null;
    public string Breadcrumb => Folder.Length == 0 ? "\\" : "\\" + Folder;

    partial void OnImagePathChanged(string? value)
    {
        _inspector?.Dispose();
        _inspector = null;
        Entries.Clear();
        Summary = null;
        Result = null;
        if (value is null) return;
        try
        {
            _inspector = ImageInspector.Open(value);
            Summary = $"{_inspector.VolumeLabel} · {_inspector.FileSystem} · {_inspector.Layout.Format.DisplayName()}";
            Navigate("");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or Formats.ImageFormatException
                                       or DiscUtils.InvalidFileSystemException)
        {
            Result = ex.Message;
            ResultOk = false;
        }

        OnPropertyChanged(nameof(IsOpen));
    }

    private void Navigate(string folder)
    {
        if (_inspector is null) return;
        Folder = folder;
        Entries.Clear();
        foreach (var entry in _inspector.List(folder)) Entries.Add(new EntryViewModel(entry));
        OnPropertyChanged(nameof(Breadcrumb));
    }

    [RelayCommand]
    private async Task PickImage()
    {
        if (await Services.Dialogs.PickFileAsync("Choose an image to browse", "CD/DVD images",
                ["*.iso", "*.cue", "*.bin", "*.nrg", "*.mds", "*.mdf", "*.ccd", "*.img"]) is { } path) ImagePath = path;
    }

    [RelayCommand]
    private void OpenEntry(EntryViewModel? entry)
    {
        entry ??= Selected;
        if (entry is { IsDirectory: true }) Navigate(entry.Entry.Path);
    }

    [RelayCommand]
    private void Up()
    {
        var cut = Folder.LastIndexOf('\\');
        Navigate(cut < 0 ? "" : Folder[..cut]);
    }

    [RelayCommand]
    private Task ExtractSelected() => Selected is { } s ? ExtractAsync(s.Entry.Path, s.Entry.Size) : Task.CompletedTask;

    [RelayCommand]
    private Task ExtractAll() => ExtractAsync(Folder, 0);

    private async Task ExtractAsync(string path, long size)
    {
        if (_inspector is not { } inspector) return;
        if (await Services.Dialogs.PickFolderAsync("Extract to…") is not { } destination) return;
        var total = size > 0 ? size : inspector.Walk(path).Where(e => !e.IsDirectory).Sum(e => e.Size);
        await RunAsync("Extracting", async (progress, cancel) =>
        {
            await inspector.ExtractAsync(path, destination, new Progress<long>(d => progress.Report((d, total))), cancel);
            return new ToolOutcome($"Extracted {ByteSize.Format(total)} to {destination}.");
        });
    }
}

/// <summary>Computes checksums and checks them against a published value or checksum file.</summary>
public sealed partial class VerifyTool(AppServices services) : ToolViewModel(services)
{
    public override string Name => "Verify checksum";
    public override string Description => "Check an image against its published MD5, SHA-1 or SHA-256";
    public override Geometry Icon => AppIcons.Verify;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand), nameof(ComputeCommand))]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    public partial string? Expected { get; set; }

    [ObservableProperty]
    public partial string? Md5 { get; private set; }

    [ObservableProperty]
    public partial string? Sha1 { get; private set; }

    [ObservableProperty]
    public partial string? Sha256 { get; private set; }

    public bool HasHashes => Sha256 is not null;

    partial void OnFilePathChanged(string? value)
    {
        Md5 = Sha1 = Sha256 = null;
        Result = null;
        OnPropertyChanged(nameof(HasHashes));
        // A checksum file next to the image is the usual way they're published: offer it.
        if (value is null || !string.IsNullOrWhiteSpace(Expected)) return;
        var dir = Path.GetDirectoryName(value)!;
        var candidates = new[] { "SHA256SUMS", "SHA256SUMS.txt", "sha256sum.txt", Path.GetFileName(value) + ".sha256", "MD5SUMS", Path.GetFileName(value) + ".md5" };
        Expected = candidates.Select(c => Path.Combine(dir, c)).FirstOrDefault(File.Exists);
    }

    private bool HasFile => File.Exists(FilePath) && !IsRunning;
    private bool CanVerify => HasFile && !string.IsNullOrWhiteSpace(Expected);

    [RelayCommand]
    private async Task PickFile()
    {
        if (await Services.Dialogs.PickFileAsync("Choose a file to check") is { } path) FilePath = path;
    }

    [RelayCommand]
    private async Task PickChecksumFile()
    {
        if (await Services.Dialogs.PickFileAsync("Choose a checksum file", "Checksum files",
                ["*.sha256", "*.sha1", "*.md5", "*SUMS", "*.txt"]) is { } path) Expected = path;
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private Task Verify() => RunAsync("Hashing", async (progress, cancel) =>
    {
        var match = await Checksums.VerifyAsync(FilePath!, Expected!, progress, cancel);
        return match.IsMatch
            ? new ToolOutcome($"{match.Algorithm.Name()} matches. The file is intact.")
            : new ToolOutcome($"{match.Algorithm.Name()} doesn't match: got {match.Actual}, expected {match.Expected}. Download the file again.", Ok: false);
    });

    [RelayCommand(CanExecute = nameof(HasFile))]
    private async Task Compute()
    {
        IReadOnlyDictionary<ChecksumAlgorithm, string>? hashes = null;
        await RunAsync("Hashing", async (progress, cancel) =>
        {
            hashes = await Checksums.ComputeAsync(FilePath!, [ChecksumAlgorithm.Md5, ChecksumAlgorithm.Sha1, ChecksumAlgorithm.Sha256], progress, cancel);
            return new ToolOutcome("Checksums computed.");
        });
        if (hashes is null) return;
        Md5 = hashes[ChecksumAlgorithm.Md5];
        Sha1 = hashes[ChecksumAlgorithm.Sha1];
        Sha256 = hashes[ChecksumAlgorithm.Sha256];
        OnPropertyChanged(nameof(HasHashes));
    }

    [RelayCommand]
    private Task Copy(string? value) => value is null ? Task.CompletedTask : Services.Dialogs.SetClipboardTextAsync(value);
}
