using System.Text;
using DiscUtils.Iso9660;

namespace Mountly.Tools;

public sealed record IsoBuildOptions
{
    /// <summary>Volume label; defaults to the folder's name.</summary>
    public string? VolumeLabel { get; init; }

    /// <summary>An El Torito boot image (no-emulation), e.g. a boot sector or isolinux.bin.</summary>
    public string? BootImagePath { get; init; }
}

/// <summary>Builds an ISO 9660 + Joliet image from a folder.</summary>
public static class IsoBuilder
{
    /// <summary>Joliet's limit on file and folder names.</summary>
    public const int MaxNameLength = 64;

    public static async Task<IsoBuildResult> BuildAsync(string sourceFolder, string isoPath, IsoBuildOptions? options = null,
        IProgress<(long Done, long Total)>? progress = null, CancellationToken cancel = default)
    {
        options ??= new IsoBuildOptions();
        var root = Path.GetFullPath(sourceFolder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"'{root}' does not exist.");
        if (Path.GetFullPath(isoPath).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The ISO can't be written inside the folder it is built from.");

        var builder = new CDBuilder
        {
            UseJoliet = true,
            VolumeIdentifier = SanitizeLabel(options.VolumeLabel ?? new DirectoryInfo(root).Name),
        };

        long total = 0;
        var files = 0;
        var skipped = new List<string>();
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", new EnumerationOptions
                 {
                     RecurseSubdirectories = true,
                     AttributesToSkip = FileAttributes.ReparsePoint, // never follow junctions or symlinks
                     IgnoreInaccessible = true,
                 }))
        {
            cancel.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, entry.FullName);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.Length > MaxNameLength))
            {
                skipped.Add(relative);
                continue;
            }

            var isoName = relative.Replace(Path.DirectorySeparatorChar, '\\');
            if (entry is DirectoryInfo) builder.AddDirectory(isoName);
            else if (entry is FileInfo file)
            {
                builder.AddFile(isoName, file.FullName);
                total += file.Length;
                files++;
            }
        }

        FileStream? boot = null;
        if (options.BootImagePath is { } bootPath)
        {
            boot = File.OpenRead(bootPath);
            builder.SetBootImage(boot, BootDeviceEmulation.NoEmulation, 0);
        }

        try
        {
            await using var output = new FileStream(isoPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            await using var counting = new ProgressStream(output, total, progress, cancel);
            await Task.Run(() => builder.Build(counting), cancel).ConfigureAwait(false);
        }
        catch
        {
            try { File.Delete(isoPath); } catch (IOException) { }
            throw;
        }
        finally
        {
            if (boot is not null) await boot.DisposeAsync().ConfigureAwait(false);
        }

        FixJolietLabel(isoPath, builder.VolumeIdentifier);
        return new IsoBuildResult(files, total, new FileInfo(isoPath).Length, skipped);
    }

    /// <summary>
    /// DiscUtils pads the Joliet volume label with single-byte spaces, which Windows shows as "LABEL††††"
    /// (0x2020). Rewrites the label of the Joliet supplementary descriptor as proper UCS-2 big-endian.
    /// </summary>
    internal static void FixJolietLabel(string isoPath, string label)
    {
        using var iso = new FileStream(isoPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var descriptor = new byte[2048];
        for (var sector = 16; sector < 32; sector++)
        {
            iso.Position = sector * 2048L;
            if (iso.Read(descriptor) < descriptor.Length) return;
            if (descriptor[0] == 255 || !descriptor.AsSpan(1, 5).SequenceEqual("CD001"u8)) return; // terminator or not ISO
            // Type 2 with a UCS-2 escape sequence ("%/@", "%/C" or "%/E") is the Joliet descriptor.
            if (descriptor[0] != 2 || descriptor[88] != '%' || descriptor[89] != '/') continue;

            var field = new byte[32];
            for (var i = 0; i < 16; i++)
            {
                var c = i < label.Length ? label[i] : ' ';
                field[i * 2] = (byte)(c >> 8);
                field[i * 2 + 1] = (byte)c;
            }

            iso.Position = sector * 2048L + 40;
            iso.Write(field);
            return;
        }
    }

    /// <summary>ISO 9660 volume identifiers: up to 32 upper-case letters, digits and underscores.</summary>
    public static string SanitizeLabel(string label)
    {
        var sb = new StringBuilder();
        foreach (var c in label.ToUpperInvariant())
        {
            if (sb.Length == 32) break;
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        }

        return sb.Length > 0 ? sb.ToString() : "CDROM";
    }
}

public sealed record IsoBuildResult(int Files, long SourceBytes, long IsoBytes, IReadOnlyList<string> Skipped);

/// <summary>Counts bytes written through it, reports them and honours cancellation (CDBuilder itself has neither).</summary>
internal sealed class ProgressStream(Stream inner, long total, IProgress<(long Done, long Total)>? progress, CancellationToken cancel) : Stream
{
    private long _written;
    private long _lastReport;

    public override bool CanRead => false;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => true;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        cancel.ThrowIfCancellationRequested();
        inner.Write(buffer);
        _written += buffer.Length;
        if (_written - _lastReport >= 1 << 20)
        {
            _lastReport = _written;
            progress?.Report((Math.Min(_written, Math.Max(total, _written)), Math.Max(total, _written)));
        }
    }

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
}
