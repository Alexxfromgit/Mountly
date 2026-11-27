using Mountly.Core.Images;
using Mountly.Core.Storage;

namespace Mountly.Core.Mounting;

/// <summary>Progress of a mount: converting a non-native image first, then attaching it.</summary>
public sealed record MountProgress(string Stage, long Done, long Total)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
}

/// <summary>
/// Mounts any supported image. Formats the backend attaches natively go straight to it; other optical
/// formats are converted to a cached ISO first, and the original path is remembered so listing and
/// unmounting work with the file the user picked. The CLI and the app share this state on disk.
/// </summary>
public sealed class MountService(IMountBackend backend, IImageConverter? converter)
{
    /// <summary>Converted ISOs whose mount disappeared (e.g. ejected in Explorer) are cleaned up after this.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    public IMountBackend Backend { get; } = backend;

    public bool CanMount(string path)
    {
        var format = ImageFormats.Detect(path);
        return Backend.NativeFormats.Contains(format) || (converter?.CanConvert(ImageFormats.DescriptorPath(path)) ?? false);
    }

    public async Task<MountedImage> MountAsync(string path, MountOptions? options = null, IProgress<MountProgress>? progress = null,
        CancellationToken cancel = default)
    {
        options ??= MountOptions.Default;
        var full = Path.GetFullPath(ImageFormats.DescriptorPath(path));
        if (!File.Exists(full)) throw new MountException(MountError.FileNotFound, $"'{full}' does not exist.");

        var mounted = await ListAsync(cancel).ConfigureAwait(false);
        if (Find(mounted, full) is { } existing)
            throw new MountException(MountError.AlreadyMounted,
                $"'{Path.GetFileName(full)}' is already mounted{(existing.FirstLetter is { } l ? " as " + l : "")}.");

        var format = ImageFormats.Detect(full);
        if (Backend.NativeFormats.Contains(format))
        {
            progress?.Report(new MountProgress("Mounting", 0, 0));
            var image = await Backend.MountAsync(full, options, cancel).ConfigureAwait(false);
            return image with { Format = format };
        }

        if (converter is null || !converter.CanConvert(full))
            throw new MountException(MountError.UnsupportedFormat, $"Mountly can't mount '{Path.GetFileName(full)}' ({format.DisplayName()}).");

        var iso = Backend.NormalizePath(await ConvertCachedAsync(full, progress, cancel).ConfigureAwait(false));
        RecordConverted(full, iso);
        try
        {
            progress?.Report(new MountProgress("Mounting", 0, 0));
            var image = await Backend.MountAsync(iso, options with { ReadOnly = true }, cancel).ConfigureAwait(false);
            return image with { ImagePath = full, BackingPath = iso, Format = format };
        }
        catch
        {
            ForgetConverted(full, deleteCache: true);
            throw;
        }
    }

    /// <summary>Unmounts by image path (original or backing file) or by drive letter ("E", "E:", "E:\").</summary>
    public async Task<MountedImage> UnmountAsync(string pathOrLetter, CancellationToken cancel = default)
    {
        var mounted = await ListAsync(cancel).ConfigureAwait(false);
        var target = Find(mounted, pathOrLetter)
                     ?? throw new MountException(MountError.NotMounted, $"No mounted image matches '{pathOrLetter}'.");
        await Backend.UnmountAsync(target.BackingPath, cancel).ConfigureAwait(false);
        if (target.IsConverted) ForgetConverted(target.ImagePath, deleteCache: true);
        return target;
    }

    public async Task<IReadOnlyList<MountedImage>> UnmountAllAsync(CancellationToken cancel = default)
    {
        var done = new List<MountedImage>();
        foreach (var image in await ListAsync(cancel).ConfigureAwait(false))
        {
            await Backend.UnmountAsync(image.BackingPath, cancel).ConfigureAwait(false);
            if (image.IsConverted) ForgetConverted(image.ImagePath, deleteCache: true);
            done.Add(image);
        }

        return done;
    }

    /// <summary>Mounted images, with converted ISOs reported under the file the user mounted.</summary>
    public async Task<IReadOnlyList<MountedImage>> ListAsync(CancellationToken cancel = default)
    {
        var attached = await Backend.ListAsync(cancel).ConfigureAwait(false);
        var state = LoadState();
        var result = new List<MountedImage>(attached.Count);
        foreach (var image in attached)
        {
            var converted = state.Converted.FirstOrDefault(c => SamePath(c.BackingPath, image.BackingPath));
            result.Add(converted is null
                ? image with { Format = image.Format == ImageFormat.Unknown ? ImageFormats.Detect(image.BackingPath) : image.Format }
                : image with { ImagePath = converted.ImagePath, Format = ImageFormats.Detect(converted.ImagePath) });
        }

        PruneStale(state, attached);
        return result;
    }

    /// <summary>The mounted image a path (as the user wrote it, or as the backend reports it) or a drive letter refers to.</summary>
    public MountedImage? Find(IEnumerable<MountedImage> mounted, string pathOrLetter)
    {
        var letter = ParseLetter(pathOrLetter);
        if (letter is not null)
            return mounted.FirstOrDefault(m => m.DriveLetters.Any(l => char.ToUpperInvariant(l[0]) == letter));
        var full = Path.GetFullPath(ImageFormats.DescriptorPath(pathOrLetter));
        var normalized = File.Exists(full) ? Backend.NormalizePath(full) : full;
        return mounted.FirstOrDefault(m => SamePath(m.ImagePath, full) || SamePath(m.BackingPath, full) ||
                                           SamePath(m.ImagePath, normalized) || SamePath(m.BackingPath, normalized));
    }

    /// <summary>"E", "e:", "E:\" → 'E'; anything else → null.</summary>
    public static char? ParseLetter(string text)
    {
        var t = text.Trim().TrimEnd('\\', '/');
        if (t.EndsWith(':')) t = t[..^1];
        return t.Length == 1 && char.IsAsciiLetter(t[0]) ? char.ToUpperInvariant(t[0]) : null;
    }

    private async Task<string> ConvertCachedAsync(string source, IProgress<MountProgress>? progress, CancellationToken cancel)
    {
        var iso = AppPaths.CachedIsoFor(source);
        var info = new FileInfo(iso);
        // Reuse the ISO converted last time while it is newer than the source.
        if (info.Exists && info.Length > 0 && info.LastWriteTimeUtc >= NewestPart(source)) return iso;

        var temp = iso + ".part";
        var total = EstimateSize(source);
        var bytes = new Progress<long>(done => progress?.Report(new MountProgress("Converting", done, total)));
        try
        {
            await converter!.ConvertToIsoAsync(source, temp, bytes, cancel).ConfigureAwait(false);
            File.Move(temp, iso, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temp);
            throw new MountException(MountError.Cancelled, "Mounting was cancelled.");
        }
        catch (Exception ex) when (ex is not MountException)
        {
            TryDelete(temp);
            throw new MountException(MountError.InvalidImage, $"Couldn't read '{Path.GetFileName(source)}': {ex.Message}", ex);
        }

        return iso;
    }

    // A descriptor (.cue/.mds/.ccd) is tiny; its data files decide the work and the freshness.
    private static DateTime NewestPart(string source)
    {
        var newest = File.GetLastWriteTimeUtc(source);
        foreach (var part in DataFiles(source))
            if (File.Exists(part) && File.GetLastWriteTimeUtc(part) > newest) newest = File.GetLastWriteTimeUtc(part);
        return newest;
    }

    private static long EstimateSize(string source) =>
        DataFiles(source).Append(source).Where(File.Exists).Select(f => new FileInfo(f).Length).DefaultIfEmpty(0).Max();

    private static IEnumerable<string> DataFiles(string source) =>
    [
        Path.ChangeExtension(source, ".bin"), Path.ChangeExtension(source, ".img"), Path.ChangeExtension(source, ".mdf"),
    ];

    private static MountStateData LoadState() =>
        JsonFile.Load(AppPaths.MountStateFile, StorageJson.Default.MountStateData, () => new MountStateData());

    private static void RecordConverted(string image, string backing) =>
        JsonFile.Update(AppPaths.MountStateFile, StorageJson.Default.MountStateData, () => new MountStateData(), s =>
        {
            s.Converted.RemoveAll(c => SamePath(c.ImagePath, image));
            s.Converted.Add(new ConvertedMount { ImagePath = image, BackingPath = backing, CreatedUtc = DateTime.UtcNow });
        });

    private static void ForgetConverted(string image, bool deleteCache)
    {
        string? backing = null;
        JsonFile.Update(AppPaths.MountStateFile, StorageJson.Default.MountStateData, () => new MountStateData(), s =>
        {
            backing = s.Converted.FirstOrDefault(c => SamePath(c.ImagePath, image))?.BackingPath;
            s.Converted.RemoveAll(c => SamePath(c.ImagePath, image));
        });
        if (deleteCache && backing is not null) TryDelete(backing);
    }

    private static void PruneStale(MountStateData state, IReadOnlyList<MountedImage> attached)
    {
        var stale = state.Converted
            .Where(c => DateTime.UtcNow - c.CreatedUtc > StaleAfter && !attached.Any(a => SamePath(a.BackingPath, c.BackingPath)))
            .ToList();
        foreach (var entry in stale) ForgetConverted(entry.ImagePath, deleteCache: true);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* still attached or locked: next prune */ }
    }

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
