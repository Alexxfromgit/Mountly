using Mountly.Core.Images;

namespace Mountly.Core.Mounting;

/// <summary>
/// Attaches image files to the operating system. The Windows backend uses the built-in Storage
/// provider (ISO, VHD, VHDX). Later backends (a virtual SCSI driver, Linux loop devices, macOS hdiutil)
/// implement the same interface.
/// </summary>
public interface IMountBackend
{
    string Name { get; }

    /// <summary>Formats this backend attaches directly, without converting them first.</summary>
    IReadOnlySet<ImageFormat> NativeFormats { get; }

    /// <summary>The path under which the backend reports an image (e.g. with links and redirections resolved).</summary>
    string NormalizePath(string path) => Path.GetFullPath(path);

    /// <summary>Attaches <paramref name="path"/> and waits until its drive letter (if any) appears.</summary>
    Task<MountedImage> MountAsync(string path, MountOptions options, CancellationToken cancel = default);

    /// <summary>Detaches the image attached from <paramref name="backingPath"/>.</summary>
    Task UnmountAsync(string backingPath, CancellationToken cancel = default);

    /// <summary>Every image attached right now, including those mounted by Explorer or other tools.</summary>
    Task<IReadOnlyList<MountedImage>> ListAsync(CancellationToken cancel = default);
}

/// <summary>Turns a non-native optical image (BIN/CUE, NRG, MDS, CloneCD) into an ISO the backend can attach.</summary>
public interface IImageConverter
{
    bool CanConvert(string path);

    /// <summary>
    /// Writes the image's data track to <paramref name="isoPath"/>. Reports bytes written. Returns warnings
    /// (for example audio tracks that an ISO cannot hold).
    /// </summary>
    Task<IReadOnlyList<string>> ConvertToIsoAsync(string sourcePath, string isoPath, IProgress<long>? progress = null,
        CancellationToken cancel = default);
}
