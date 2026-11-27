using System.Globalization;
using Mountly.Core.Images;

namespace Mountly.Formats.Parsers;

/// <summary>Reads a cue sheet (.cue) and the layout of the BIN files it names.</summary>
public static class CueSheet
{
    private sealed class CueTrack
    {
        public int Number;
        public required string File;
        public TrackKind Kind;
        public int SectorSize;
        public int DataOffset;
        public long? Index0;
        public long? Index1;
    }

    public static DiscLayout Read(string cuePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(cuePath))!;
        var tracks = new List<CueTrack>();
        string? currentFile = null;
        var lineNumber = 0;

        foreach (var raw in File.ReadLines(cuePath))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var keyword = FirstWord(line, out var rest);
            switch (keyword.ToUpperInvariant())
            {
                case "FILE":
                    var (name, type) = ParseFile(rest);
                    if (type is "WAVE" or "MP3" or "AIFF" or "FLAC")
                        throw new ImageFormatException($"Line {lineNumber}: {type} audio files can't be mounted; only BINARY data files.");
                    currentFile = ResolveFile(directory, name, cuePath);
                    break;
                case "TRACK":
                    if (currentFile is null) throw new ImageFormatException($"Line {lineNumber}: TRACK before any FILE.");
                    var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var number))
                        throw new ImageFormatException($"Line {lineNumber}: malformed TRACK.");
                    var (kind, size, offset) = ParseMode(parts[1], lineNumber);
                    tracks.Add(new CueTrack { Number = number, File = currentFile, Kind = kind, SectorSize = size, DataOffset = offset });
                    break;
                case "INDEX":
                    if (tracks.Count == 0) throw new ImageFormatException($"Line {lineNumber}: INDEX before any TRACK.");
                    var idx = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (idx.Length < 2 || !int.TryParse(idx[0], CultureInfo.InvariantCulture, out var indexNumber))
                        throw new ImageFormatException($"Line {lineNumber}: malformed INDEX.");
                    var frames = ParseMsf(idx[1], lineNumber);
                    if (indexNumber == 0) tracks[^1].Index0 = frames;
                    else if (indexNumber == 1) tracks[^1].Index1 = frames;
                    break;
            }
        }

        if (tracks.Count == 0) throw new ImageFormatException("The cue sheet lists no tracks.");
        return new DiscLayout(ImageFormat.CueBin, Path.GetFullPath(cuePath), Layout(tracks));
    }

    private static List<TrackLayout> Layout(List<CueTrack> tracks)
    {
        var result = new List<TrackLayout>();
        foreach (var group in tracks.GroupBy(t => t.File))
        {
            var inFile = group.ToList();
            var fileLength = new FileInfo(group.Key).Length;
            long offset = 0; // byte offset of the current track's INDEX 01 in this file
            for (var i = 0; i < inFile.Count; i++)
            {
                var t = inFile[i];
                var start = t.Index1 ?? t.Index0 ?? 0;
                if (i == 0) offset = start * t.SectorSize;
                else
                {
                    // Sectors between the previous track's start and this one belong to the previous track.
                    var previous = inFile[i - 1];
                    offset += (start - (previous.Index1 ?? previous.Index0 ?? 0)) * previous.SectorSize;
                }

                long count;
                if (i + 1 < inFile.Count)
                {
                    var next = inFile[i + 1];
                    count = (next.Index0 ?? next.Index1 ?? start) - start;
                }
                else
                {
                    count = Math.Max(0, (fileLength - offset) / t.SectorSize);
                }

                if (count < 0 || offset > fileLength) throw new ImageFormatException($"Track {t.Number} lies outside '{Path.GetFileName(group.Key)}'.");
                var dataOffset = t.DataOffset;
                if (t.Kind == TrackKind.Data && t.SectorSize > 2336)
                {
                    using var file = OpenRead(group.Key);
                    dataOffset = RawLayout.DataOffsetFor(file, offset, t.SectorSize, t.DataOffset);
                }

                result.Add(new TrackLayout(t.Number, t.Kind, group.Key, offset, t.SectorSize, dataOffset, count));
            }
        }

        return result.OrderBy(t => t.Number).ToList();
    }

    private static (TrackKind, int, int) ParseMode(string mode, int line) => mode.ToUpperInvariant() switch
    {
        "MODE1/2048" or "MODE2/2048" => (TrackKind.Data, 2048, 0),
        "MODE1/2352" => (TrackKind.Data, 2352, 16),
        "MODE2/2352" or "CDI/2352" => (TrackKind.Data, 2352, 24),
        "MODE2/2336" or "CDI/2336" => (TrackKind.Data, 2336, 8),
        "AUDIO" => (TrackKind.Audio, 2352, 0),
        "CDG" => (TrackKind.Audio, 2448, 0),
        _ => throw new ImageFormatException($"Line {line}: unsupported track mode '{mode}'."),
    };

    private static long ParseMsf(string msf, int line)
    {
        var p = msf.Split(':');
        if (p.Length != 3 || !int.TryParse(p[0], CultureInfo.InvariantCulture, out var m) ||
            !int.TryParse(p[1], CultureInfo.InvariantCulture, out var s) || !int.TryParse(p[2], CultureInfo.InvariantCulture, out var f) ||
            s >= 60 || f >= 75 || m < 0 || s < 0 || f < 0)
            throw new ImageFormatException($"Line {line}: malformed time '{msf}'.");
        return ((long)m * 60 + s) * 75 + f;
    }

    private static (string Name, string Type) ParseFile(string rest)
    {
        string name;
        string after;
        if (rest.StartsWith('"'))
        {
            var end = rest.IndexOf('"', 1);
            name = end > 0 ? rest[1..end] : rest[1..];
            after = end > 0 ? rest[(end + 1)..] : "";
        }
        else
        {
            var space = rest.LastIndexOf(' ');
            name = space > 0 ? rest[..space] : rest;
            after = space > 0 ? rest[space..] : "";
        }

        return (name, after.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Finds the data file a cue sheet names. Renamed images often keep the old name inside the cue, so it
    /// falls back to the .bin with the cue's own name.
    /// </summary>
    private static string ResolveFile(string directory, string name, string cuePath)
    {
        // Only a file name is honoured: a cue sheet must not point Mountly at arbitrary paths.
        var candidate = Path.Combine(directory, Path.GetFileName(name.Replace('\\', '/').Split('/')[^1]));
        if (File.Exists(candidate)) return candidate;
        var match = Directory.EnumerateFiles(directory)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), Path.GetFileName(candidate), StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;
        var sameName = Path.ChangeExtension(cuePath, ".bin");
        if (File.Exists(sameName)) return sameName;
        throw new ImageFormatException($"The cue sheet names '{name}', which isn't next to it.");
    }

    private static string FirstWord(string line, out string rest)
    {
        var space = line.IndexOf(' ');
        if (space < 0)
        {
            rest = "";
            return line;
        }

        rest = line[(space + 1)..].Trim();
        return line[..space];
    }

    internal static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1, FileOptions.RandomAccess);
}
