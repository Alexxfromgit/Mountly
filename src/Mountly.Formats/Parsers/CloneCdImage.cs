using System.Globalization;
using Mountly.Core.Images;

namespace Mountly.Formats.Parsers;

/// <summary>
/// Reads CloneCD images: a .ccd (INI) descriptor and a .img of raw 2352-byte sectors (subchannel data,
/// if any, lives in a separate .sub file).
/// </summary>
public static class CloneCdImage
{
    private const int SectorSize = 2352;

    public static DiscLayout Read(string path)
    {
        var full = Path.GetFullPath(path);
        var ccd = full.EndsWith(".ccd", StringComparison.OrdinalIgnoreCase) ? full : Path.ChangeExtension(full, ".ccd");
        var img = Path.ChangeExtension(ccd, ".img");
        if (!File.Exists(img)) throw new ImageFormatException($"'{Path.GetFileName(img)}' is missing next to the descriptor.");
        if (!File.Exists(ccd)) return RawImage.Read(img, ImageFormat.CloneCd);

        var sections = ParseIni(ccd);
        var entries = sections
            .Where(s => s.Key.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
            .Select(s => (Number: int.Parse(s.Key[6..].Trim(), CultureInfo.InvariantCulture), Values: s.Value))
            .Select(t => (t.Number,
                Mode: t.Values.TryGetValue("MODE", out var mode) ? int.Parse(mode, CultureInfo.InvariantCulture) : 0,
                Start: t.Values.TryGetValue("INDEX 1", out var index) ? long.Parse(index, CultureInfo.InvariantCulture) : 0))
            .OrderBy(t => t.Start)
            .ToList();
        if (entries.Count == 0) throw new ImageFormatException("The CloneCD descriptor lists no tracks.");

        using var data = CueSheet.OpenRead(img);
        var total = data.Length / SectorSize;
        var tracks = new List<TrackLayout>();
        for (var i = 0; i < entries.Count; i++)
        {
            var (number, mode, start) = entries[i];
            var end = i + 1 < entries.Count ? entries[i + 1].Start : total;
            if (start < 0 || end < start || end > total) throw new ImageFormatException($"Track {number} lies outside the image.");
            var offset = start * SectorSize;
            var kind = mode == 0 ? TrackKind.Audio : TrackKind.Data;
            var dataOffset = kind == TrackKind.Data ? RawLayout.DataOffsetFor(data, offset, SectorSize, mode == 2 ? 24 : 16) : 0;
            tracks.Add(new TrackLayout(number, kind, img, offset, SectorSize, dataOffset, end - start));
        }

        return new DiscLayout(ImageFormat.CloneCd, ccd, tracks.OrderBy(t => t.Number).ToList());
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[line[1..^1].Trim()] = current;
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq > 0 && current is not null) current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        return sections;
    }
}

/// <summary>A data track without a descriptor (.bin, .img, lone .mdf): the layout is detected from the data.</summary>
public static class RawImage
{
    public static DiscLayout Read(string path, ImageFormat format = ImageFormat.RawBin)
    {
        using var file = CueSheet.OpenRead(path);
        var layout = RawLayout.Detect(file, 0)
                     ?? throw new ImageFormatException($"'{Path.GetFileName(path)}' has no ISO 9660 or UDF file system Mountly recognises.");
        var track = new TrackLayout(1, TrackKind.Data, Path.GetFullPath(path), 0, layout.SectorSize, layout.DataOffset,
            file.Length / layout.SectorSize);
        return new DiscLayout(format, Path.GetFullPath(path), [track]);
    }
}
