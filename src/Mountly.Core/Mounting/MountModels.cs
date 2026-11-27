using Mountly.Core.Images;

namespace Mountly.Core.Mounting;

/// <summary>How to mount an image.</summary>
public sealed record MountOptions
{
    /// <summary>Mount read-only. Optical images are always read-only.</summary>
    public bool ReadOnly { get; init; } = true;

    /// <summary>Attach the device without giving its volumes a drive letter.</summary>
    public bool NoDriveLetter { get; init; }

    /// <summary>Drive letter to move the volume to after mounting (needs administrator rights), e.g. 'M'.</summary>
    public char? PreferredLetter { get; init; }

    public static MountOptions Default { get; } = new();
}

/// <summary>An image that is attached to the system right now.</summary>
public sealed record MountedImage
{
    /// <summary>The image the user mounted (the .cue/.nrg/... even when an ISO converted from it is attached).</summary>
    public required string ImagePath { get; init; }

    /// <summary>The file actually attached: <see cref="ImagePath"/>, or the cached ISO converted from it.</summary>
    public required string BackingPath { get; init; }

    /// <summary>Device path such as \\.\CDROM1 or \\.\PHYSICALDRIVE3.</summary>
    public string? DevicePath { get; init; }

    /// <summary>Drive letters of the image's volumes, e.g. "E:". Empty while Windows is still assigning one.</summary>
    public IReadOnlyList<string> DriveLetters { get; init; } = [];

    /// <summary>Label of the first volume, e.g. "UBUNTU_24_04".</summary>
    public string? VolumeLabel { get; init; }

    public ImageFormat Format { get; init; }

    public long SizeBytes { get; init; }

    public bool IsReadOnly { get; init; } = true;

    /// <summary>True when the image was converted to a cached ISO first (formats Windows can't mount itself).</summary>
    public bool IsConverted => !string.Equals(ImagePath, BackingPath, StringComparison.OrdinalIgnoreCase);

    public string? FirstLetter => DriveLetters.Count > 0 ? DriveLetters[0] : null;
}

/// <summary>Why a mount operation failed, so the UI can offer the right way out.</summary>
public enum MountError
{
    Unknown,
    FileNotFound,
    UnsupportedFormat,
    AlreadyMounted,
    NotMounted,
    NeedsElevation,
    FileInUse,
    InvalidImage,
    Cancelled,
}

public sealed class MountException(MountError error, string message, Exception? inner = null) : Exception(message, inner)
{
    public MountError Error { get; } = error;
}
