using Mountly.Core.Images;

namespace Mountly.Core.Storage;

public sealed class SettingsStore
{
    public AppSettings Current { get; private set; } = JsonFile.Load(AppPaths.SettingsFile, StorageJson.Default.AppSettings, () => new AppSettings());

    public void Save() => JsonFile.Save(AppPaths.SettingsFile, Current, StorageJson.Default.AppSettings);

    public void Reload() => Current = JsonFile.Load(AppPaths.SettingsFile, StorageJson.Default.AppSettings, () => new AppSettings());
}

/// <summary>The virtual drives (slots) the user created.</summary>
public sealed class SlotStore
{
    public IReadOnlyList<DriveSlot> Load() => JsonFile.Load(AppPaths.SlotsFile, StorageJson.Default.SlotsData, () => new SlotsData()).Slots;

    public IReadOnlyList<DriveSlot> Update(Action<List<DriveSlot>> change) =>
        JsonFile.Update(AppPaths.SlotsFile, StorageJson.Default.SlotsData, () => new SlotsData(), data => change(data.Slots)).Slots;
}

/// <summary>Recently used and pinned images.</summary>
public sealed class CatalogStore
{
    public const int MaxRecent = 50;

    public IReadOnlyList<CatalogEntry> Load() =>
        Sort(JsonFile.Load(AppPaths.CatalogFile, StorageJson.Default.CatalogData, () => new CatalogData()).Images);

    public IReadOnlyList<CatalogEntry> Touch(string path) => Update(images =>
    {
        var full = Path.GetFullPath(path);
        var entry = images.FirstOrDefault(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new CatalogEntry { Path = full };
            images.Add(entry);
        }

        entry.Format = ImageFormats.Detect(full);
        entry.SizeBytes = File.Exists(full) ? new FileInfo(full).Length : entry.SizeBytes;
        entry.LastUsedUtc = DateTime.UtcNow;

        // Keep pinned images and the most recent others.
        var drop = images.Where(e => !e.Pinned).OrderByDescending(e => e.LastUsedUtc).Skip(MaxRecent).ToList();
        foreach (var old in drop) images.Remove(old);
    });

    public IReadOnlyList<CatalogEntry> Remove(string path) =>
        Update(images => images.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)));

    public IReadOnlyList<CatalogEntry> SetPinned(string path, bool pinned) => Update(images =>
    {
        foreach (var e in images.Where(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))) e.Pinned = pinned;
    });

    private IReadOnlyList<CatalogEntry> Update(Action<List<CatalogEntry>> change) =>
        Sort(JsonFile.Update(AppPaths.CatalogFile, StorageJson.Default.CatalogData, () => new CatalogData(), d => change(d.Images)).Images);

    private static List<CatalogEntry> Sort(List<CatalogEntry> images) =>
        images.OrderByDescending(e => e.Pinned).ThenByDescending(e => e.LastUsedUtc).ToList();
}
