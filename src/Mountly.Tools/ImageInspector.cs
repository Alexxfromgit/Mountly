using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Udf;
using Mountly.Core.Images;
using Mountly.Formats;

namespace Mountly.Tools;

public sealed record ImageEntry(string Path, string Name, bool IsDirectory, long Size, DateTime? ModifiedUtc);

/// <summary>Browses and extracts the files inside an optical image without mounting it.</summary>
public sealed class ImageInspector : IDisposable
{
    private readonly Stream _stream;
    private readonly DiscFileSystem _fs;

    private ImageInspector(Stream stream, DiscFileSystem fs, string fileSystemName, DiscLayout layout)
    {
        _stream = stream;
        _fs = fs;
        FileSystem = fileSystemName;
        Layout = layout;
    }

    public string FileSystem { get; }

    public DiscLayout Layout { get; }

    // Joliet labels are UCS-2; space padding written as single bytes reads back as U+2020 ('†').
    public string VolumeLabel => (_fs.VolumeLabel ?? "").TrimEnd(' ', '†', '\0');

    public static ImageInspector Open(string path)
    {
        if (!ImageFormats.Detect(path).IsOptical())
            throw new NotSupportedException("Browsing virtual hard disks isn't supported yet; mount them instead.");
        var layout = OpticalImage.ReadLayout(path);
        var stream = OpticalImage.OpenDataTrack(layout);
        try
        {
            // DVDs are usually UDF with an ISO 9660 bridge; UDF has the full names, so it wins when present.
            if (UdfReader.Detect(stream))
            {
                stream.Position = 0;
                return new ImageInspector(stream, new UdfReader(stream), "UDF", layout);
            }

            stream.Position = 0;
            if (CDReader.Detect(stream))
            {
                stream.Position = 0;
                var reader = new CDReader(stream, joliet: true);
                return new ImageInspector(stream, reader, reader.ActiveVariant.ToString() == "Iso9660" ? "ISO 9660" : "ISO 9660 + Joliet", layout);
            }
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        stream.Dispose();
        throw new ImageFormatException("The image has no ISO 9660 or UDF file system.");
    }

    /// <summary>The entries directly inside <paramref name="directory"/> ("" for the root), folders first.</summary>
    public IReadOnlyList<ImageEntry> List(string directory = "")
    {
        var dir = Normalize(directory);
        var entries = new List<ImageEntry>();
        foreach (var d in _fs.GetDirectories(dir)) entries.Add(Entry(d, isDirectory: true));
        foreach (var f in _fs.GetFiles(dir)) entries.Add(Entry(f, isDirectory: false));
        return entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Every file and folder below <paramref name="directory"/>.</summary>
    public IEnumerable<ImageEntry> Walk(string directory = "")
    {
        foreach (var entry in List(directory))
        {
            yield return entry;
            if (entry.IsDirectory)
                foreach (var child in Walk(entry.Path))
                    yield return child;
        }
    }

    public bool IsDirectory(string path) => Normalize(path) is var p && (p.Length == 0 || _fs.DirectoryExists(p));

    public bool Exists(string path) => IsDirectory(path) || _fs.FileExists(Normalize(path));

    public Stream OpenFile(string path) => _fs.OpenFile(Normalize(path), FileMode.Open, FileAccess.Read);

    /// <summary>
    /// Copies a file, or a folder with everything in it, into <paramref name="destination"/>. Names from the
    /// image are sanitised and can never escape the destination folder.
    /// </summary>
    public async Task<long> ExtractAsync(string path, string destination, IProgress<long>? progress = null, CancellationToken cancel = default)
    {
        var source = Normalize(path);
        var root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        long done = 0;
        var buffer = new byte[1 << 20];

        if (!IsDirectory(source))
        {
            await CopyAsync(source, SafeJoin(root, Leaf(source))).ConfigureAwait(false);
            return done;
        }

        var baseDir = source.Length == 0 ? root : SafeJoin(root, Leaf(source));
        Directory.CreateDirectory(baseDir);
        foreach (var entry in Walk(source))
        {
            cancel.ThrowIfCancellationRequested();
            var relative = source.Length == 0 ? entry.Path : entry.Path[(source.Length + 1)..];
            var target = SafeJoin(baseDir, relative);
            if (entry.IsDirectory) Directory.CreateDirectory(target);
            else await CopyAsync(entry.Path, target).ConfigureAwait(false);
        }

        return done;

        async Task CopyAsync(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            await using var input = OpenFile(from);
            await using var output = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            int read;
            while ((read = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                done += read;
                progress?.Report(done);
            }
        }
    }

    public void Dispose()
    {
        (_fs as IDisposable)?.Dispose();
        _stream.Dispose();
    }

    private ImageEntry Entry(string path, bool isDirectory)
    {
        // DiscUtils returns paths with the platform's separator ('/' on Linux); paths here always use '\'.
        var p = Normalize(path);
        DateTime? modified = null;
        try { modified = _fs.GetLastWriteTimeUtc(p); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { }
        return new ImageEntry(p, Leaf(p), isDirectory, isDirectory ? 0 : _fs.GetFileLength(p), modified);
    }

    // ISO 9660 names may end in ";1" (the version); people never want to see it.
    private static string Leaf(string path)
    {
        var name = path.TrimEnd('\\');
        name = name[(name.LastIndexOf('\\') + 1)..];
        var semicolon = name.IndexOf(';');
        return semicolon > 0 ? name[..semicolon] : name;
    }

    internal static string Normalize(string path) => path.Replace('/', '\\').Trim('\\');

    internal static string SafeJoin(string root, string relative)
    {
        var parts = relative.Split('\\', '/')
            .Select(p =>
            {
                var semicolon = p.IndexOf(';');
                return semicolon > 0 ? p[..semicolon] : p;
            })
            .Select(p => string.Concat(p.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.'))
            .Where(p => p.Length > 0 && p != "." && p != "..")
            .ToArray();
        var full = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"'{relative}' would be written outside the destination folder.");
        return full;
    }
}
