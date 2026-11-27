using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mountly.App.Services;
using Mountly.Core.Images;
using Mountly.Core.Storage;
using Mountly.Core.Util;

namespace Mountly.App.ViewModels;

public sealed partial class CatalogItemViewModel(CatalogViewModel owner, CatalogEntry entry) : ObservableObject
{
    public CatalogEntry Entry { get; } = entry;
    public string Name => Path.GetFileName(Entry.Path);
    public string Folder => Path.GetDirectoryName(Entry.Path) ?? "";
    public string FormatText => Entry.Format.DisplayName();
    public string SizeText => ByteSize.Format(Entry.SizeBytes);
    public string LastUsedText => Entry.LastUsedUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);
    public bool Exists { get; } = File.Exists(entry.Path);
    public bool IsMissing => !Exists;
    public bool IsPinned => Entry.Pinned;
    public string PinLabel => Entry.Pinned ? "Unpin" : "Pin";

    [RelayCommand(CanExecute = nameof(Exists))]
    private Task Mount() => owner.Drives.MountImagesAsync([Entry.Path]);

    [RelayCommand]
    private void TogglePin() => owner.SetPinned(this, !Entry.Pinned);

    [RelayCommand]
    private void Remove() => owner.Remove(this);

    [RelayCommand(CanExecute = nameof(Exists))]
    private void Reveal() => DesktopIntegration.Reveal(Entry.Path);

    [RelayCommand(CanExecute = nameof(Exists))]
    private void Verify() => owner.Tools.OpenVerify(Entry.Path);

    [RelayCommand(CanExecute = nameof(Exists))]
    private void Browse() => owner.Tools.OpenBrowse(Entry.Path);
}

/// <summary>The Images page: recently used and pinned images.</summary>
public sealed partial class CatalogViewModel : ObservableObject
{
    private readonly AppServices _services;
    private IReadOnlyList<CatalogEntry> _all = [];

    public CatalogViewModel(AppServices services, DrivesViewModel drives, ToolsViewModel tools)
    {
        _services = services;
        Drives = drives;
        Tools = tools;
    }

    public DrivesViewModel Drives { get; }
    public ToolsViewModel Tools { get; }

    public ObservableCollection<CatalogItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string? Filter { get; set; }

    public bool IsEmpty => Items.Count == 0;

    partial void OnFilterChanged(string? value) => Show();

    public void Reload()
    {
        _all = _services.Catalog.Load();
        Show();
    }

    private void Show()
    {
        Items.Clear();
        var filter = Filter?.Trim();
        foreach (var entry in _all.Where(e => string.IsNullOrEmpty(filter) || e.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            Items.Add(new CatalogItemViewModel(this, entry));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task AddImages()
    {
        var picked = await _services.Dialogs.PickImagesAsync();
        foreach (var path in picked) _all = _services.Catalog.Touch(path);
        Show();
    }

    public void SetPinned(CatalogItemViewModel item, bool pinned)
    {
        _all = _services.Catalog.SetPinned(item.Entry.Path, pinned);
        Show();
    }

    public void Remove(CatalogItemViewModel item)
    {
        _all = _services.Catalog.Remove(item.Entry.Path);
        Show();
    }

    [RelayCommand]
    private void ClearMissing()
    {
        foreach (var missing in _all.Where(e => !File.Exists(e.Path)).ToList()) _all = _services.Catalog.Remove(missing.Path);
        Show();
    }
}
