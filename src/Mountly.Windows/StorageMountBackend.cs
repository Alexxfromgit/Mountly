using System.Runtime.Versioning;
using Microsoft.Management.Infrastructure;
using Mountly.Core.Images;
using Mountly.Core.Mounting;

namespace Mountly.Windows;

/// <summary>
/// Mounts ISO, VHD and VHDX files with Windows' own Storage provider (the MSFT_DiskImage WMI class that
/// Explorer's "Mount" and PowerShell's Mount-DiskImage use). No driver is needed, ISO files mount without
/// administrator rights, and mounts stay until they are ejected or Windows restarts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StorageMountBackend : IMountBackend
{
    private const string Namespace = @"root\Microsoft\Windows\Storage";
    private const string ClassName = "MSFT_DiskImage";

    // MSFT_DiskImage.Mount Access values.
    private const ushort AccessReadWrite = 2;
    private const ushort AccessReadOnly = 3;

    private static readonly TimeSpan LetterTimeout = TimeSpan.FromSeconds(10);

    public string Name => "Windows Storage";

    public IReadOnlySet<ImageFormat> NativeFormats { get; } = new HashSet<ImageFormat> { ImageFormat.Iso, ImageFormat.Vhd, ImageFormat.Vhdx };

    /// <summary>
    /// The path the disk service will see: the file's final path from an open handle. The service runs
    /// as SYSTEM, so subst drives, symlinks and the per-package file redirection of packaged parent apps
    /// all have to be resolved first, or it reports the image as "not found".
    /// </summary>
    public string NormalizePath(string path) => Native.FinalPath(Path.GetFullPath(path)) ?? Path.GetFullPath(path);

    public Task<MountedImage> MountAsync(string path, MountOptions options, CancellationToken cancel = default) => Task.Run(async () =>
    {
        var full = NormalizePath(path);
        using var session = CimSession.Create(null);
        var image = Query(session, full) ?? throw new MountException(MountError.InvalidImage, $"Windows can't open '{Path.GetFileName(full)}'.");
        if (Bool(image, "Attached")) throw new MountException(MountError.AlreadyMounted, $"'{Path.GetFileName(full)}' is already mounted.");

        var isIso = ImageFormats.Detect(full) == ImageFormat.Iso;
        using var parameters = new CimMethodParametersCollection
        {
            CimMethodParameter.Create("Access", options.ReadOnly || isIso ? AccessReadOnly : AccessReadWrite, CimType.UInt16, CimFlags.In),
            CimMethodParameter.Create("NoDriveLetter", options.NoDriveLetter, CimType.Boolean, CimFlags.In),
        };
        Invoke(session, image, "Mount", parameters, full);

        var mounted = await WaitForVolumeAsync(full, options.NoDriveLetter, cancel).ConfigureAwait(false);
        if (options.PreferredLetter is { } preferred && mounted.FirstLetter is { } current &&
            char.ToUpperInvariant(current[0]) != char.ToUpperInvariant(preferred))
        {
            if (DriveLetters.TryMove(current[0], preferred, out _))
                mounted = mounted with { DriveLetters = [char.ToUpperInvariant(preferred) + ":"] };
        }

        return mounted;
    }, cancel);

    public Task UnmountAsync(string backingPath, CancellationToken cancel = default) => Task.Run(() =>
    {
        var full = NormalizePath(backingPath);
        using var session = CimSession.Create(null);
        var image = Query(session, full);
        if (image is null || !Bool(image, "Attached"))
            throw new MountException(MountError.NotMounted, $"'{Path.GetFileName(full)}' isn't mounted.");
        using var parameters = new CimMethodParametersCollection();
        Invoke(session, image, "Dismount", parameters, full);
    }, cancel);

    // The provider doesn't enumerate attached images on its own; each volume knows its image through the
    // MSFT_DiskImageToVolume association (volumes without a drive letter included).
    public Task<IReadOnlyList<MountedImage>> ListAsync(CancellationToken cancel = default) => Task.Run<IReadOnlyList<MountedImage>>(() =>
    {
        using var session = CimSession.Create(null);
        var result = new Dictionary<string, MountedImage>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var volume in session.EnumerateInstances(Namespace, "MSFT_Volume"))
            {
                using (volume)
                {
                    cancel.ThrowIfCancellationRequested();
                    foreach (var image in session.EnumerateAssociatedInstances(Namespace, volume, "MSFT_DiskImageToVolume",
                                 ClassName, "Volume", "DiskImage"))
                    {
                        using (image)
                        {
                            if (Bool(image, "Attached") && Str(image, "ImagePath") is { } path && !result.ContainsKey(path) &&
                                ToMounted(image) is { } mounted)
                                result[path] = mounted;
                        }
                    }
                }
            }
        }
        catch (CimException ex)
        {
            throw Translate(ex, "mounted images");
        }

        return result.Values.ToList();
    }, cancel);

    private async Task<MountedImage> WaitForVolumeAsync(string path, bool noLetter, CancellationToken cancel)
    {
        var deadline = DateTime.UtcNow + LetterTimeout;
        MountedImage? latest = null;
        while (true)
        {
            using (var session = CimSession.Create(null))
            {
                var image = Query(session, path);
                latest = image is not null ? ToMounted(image) : latest;
            }

            if (latest is not null && (noLetter || latest.DriveLetters.Count > 0)) return latest;
            if (DateTime.UtcNow > deadline)
                return latest ?? throw new MountException(MountError.Unknown, "Windows mounted the image but didn't report its device.");
            await Task.Delay(150, cancel).ConfigureAwait(false);
        }
    }

    private static MountedImage? ToMounted(CimInstance image)
    {
        var path = Str(image, "ImagePath");
        if (path is null) return null;
        var device = Str(image, "DevicePath");
        var letters = device is null ? [] : DriveLetters.ForDevice(device);
        return new MountedImage
        {
            ImagePath = path,
            BackingPath = path,
            DevicePath = device,
            DriveLetters = letters,
            VolumeLabel = letters.Count > 0 ? Native.VolumeLabel(letters[0] + "\\") : null,
            SizeBytes = image.CimInstanceProperties["Size"]?.Value is { } size ? Convert.ToInt64(size) : 0,
            Format = ImageFormats.Detect(path),
            IsReadOnly = ImageFormats.Detect(path) == ImageFormat.Iso,
        };
    }

    // MSFT_DiskImage is keyed by ImagePath + StorageType, and the provider resolves any image file from
    // those keys, mounted or not. (Enumeration and WQL filters only see attached images, if any.)
    private static CimInstance? Query(CimSession session, string path)
    {
        uint storageType = ImageFormats.Detect(path) switch
        {
            ImageFormat.Vhd => 2,
            ImageFormat.Vhdx => 3,
            _ => 1, // ISO
        };
        using var key = new CimInstance(ClassName, Namespace);
        key.CimInstanceProperties.Add(CimProperty.Create("ImagePath", path, CimType.String, CimFlags.Key));
        key.CimInstanceProperties.Add(CimProperty.Create("StorageType", storageType, CimType.UInt32, CimFlags.Key));
        try
        {
            return session.GetInstance(Namespace, key);
        }
        catch (CimException ex)
        {
            throw Translate(ex, path);
        }
    }

    private static void Invoke(CimSession session, CimInstance image, string method, CimMethodParametersCollection parameters, string path)
    {
        CimMethodResult result;
        try
        {
            result = session.InvokeMethod(Namespace, image, method, parameters);
        }
        catch (CimException ex)
        {
            throw Translate(ex, path);
        }

        var code = result.ReturnValue?.Value is { } v ? Convert.ToUInt32(v) : 0u;
        if (code != 0) throw FromReturnCode(code, method, path);
    }

    private static MountException Translate(CimException ex, string path) => ex.NativeErrorCode switch
    {
        NativeErrorCode.AccessDenied => new MountException(MountError.NeedsElevation,
            $"Windows needs administrator rights to mount '{Path.GetFileName(path)}'.", ex),
        NativeErrorCode.NotFound => new MountException(MountError.FileNotFound, $"'{path}' was not found.", ex),
        NativeErrorCode.InvalidParameter => new MountException(MountError.InvalidImage,
            $"Windows can't use '{Path.GetFileName(path)}': {ex.Message.Trim()}", ex),
        _ when ex.Message.Contains("in use", StringComparison.OrdinalIgnoreCase) || ex.HResult == unchecked((int)0x80070020) =>
            new MountException(MountError.FileInUse, $"'{Path.GetFileName(path)}' is in use by another program.", ex),
        _ when ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase) =>
            new MountException(MountError.AlreadyMounted, $"'{Path.GetFileName(path)}' is already mounted.", ex),
        _ => new MountException(MountError.Unknown, $"Windows couldn't mount '{Path.GetFileName(path)}': {ex.Message.Trim()}", ex),
    };

    // Return codes of MSFT_DiskImage methods (see the Storage Management API reference).
    private static MountException FromReturnCode(uint code, string method, string path) => code switch
    {
        40001 or 4 => new MountException(MountError.NeedsElevation, $"Windows needs administrator rights to {method.ToLowerInvariant()} '{Path.GetFileName(path)}'."),
        5 or 55 => new MountException(MountError.InvalidImage, $"Windows says '{Path.GetFileName(path)}' isn't a valid image."),
        _ => new MountException(MountError.Unknown, $"{method} of '{Path.GetFileName(path)}' failed (code {code})."),
    };

    private static bool Bool(CimInstance instance, string name) => instance.CimInstanceProperties[name]?.Value is true;

    private static string? Str(CimInstance instance, string name) => instance.CimInstanceProperties[name]?.Value as string;
}
