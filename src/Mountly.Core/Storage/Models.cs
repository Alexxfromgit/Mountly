using System.Text.Json.Serialization;
using Mountly.Core.Images;

namespace Mountly.Core.Storage;

public sealed class AppSettings
{
    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Start Mountly (in the tray) when signing in to Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Mount the images loaded in the virtual drives again when Mountly starts.</summary>
    public bool RemountOnStart { get; set; } = true;

    /// <summary>Closing the window keeps Mountly running in the tray.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>"Mount with Mountly" and the other Explorer menu entries.</summary>
    public bool ShellIntegration { get; set; }

    public bool CheckForUpdates { get; set; } = true;
}

/// <summary>
/// A virtual drive the user created. Windows only has a device while an image is attached, so a slot
/// remembers its letter and image and Mountly re-creates the drive when it loads the image again.
/// </summary>
public sealed class DriveSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string? Name { get; set; }

    /// <summary>Drive letter to use (needs administrator rights), or null for the next free one.</summary>
    public char? PreferredLetter { get; set; }

    /// <summary>The image loaded in this drive, kept while it is ejected so it can be reloaded quickly.</summary>
    public string? ImagePath { get; set; }

    /// <summary>True while the image should be mounted (false after Eject).</summary>
    public bool Loaded { get; set; }
}

public sealed class SlotsData
{
    public List<DriveSlot> Slots { get; set; } = [];
}

public sealed class CatalogEntry
{
    public required string Path { get; set; }
    public ImageFormat Format { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastUsedUtc { get; set; }
    public bool Pinned { get; set; }
}

public sealed class CatalogData
{
    public List<CatalogEntry> Images { get; set; } = [];
}

/// <summary>A mount that attached a converted ISO instead of the user's file.</summary>
public sealed class ConvertedMount
{
    public required string ImagePath { get; set; }
    public required string BackingPath { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class MountStateData
{
    public List<ConvertedMount> Converted { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SlotsData))]
[JsonSerializable(typeof(CatalogData))]
[JsonSerializable(typeof(MountStateData))]
public sealed partial class StorageJson : JsonSerializerContext;
