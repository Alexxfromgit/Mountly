using System.Buffers.Binary;
using Mountly.Core.Images;

namespace Mountly.Formats.Parsers;

/// <summary>
/// Reads Nero images (.nrg). The track list is in chunks at the end of the file: the last 12 bytes are
/// "NER5" + a 64-bit offset (v2), or the last 8 are "NERO" + a 32-bit offset (v1). All numbers are big-endian.
/// </summary>
public static class NrgImage
{
    private const int MaxChunks = 4096;

    public static DiscLayout Read(string path)
    {
        using var file = CueSheet.OpenRead(path);
        var tracks = new List<TrackLayout>();
        try
        {
            if (FindChunks(file) is { } start) ReadChunks(file, path, start, tracks);
        }
        catch (ImageFormatException)
        {
            tracks.Clear(); // fall back to probing the data below
        }

        if (tracks.Count == 0)
        {
            // Footer missing or unreadable: the common case is a single data track after Nero's 150-sector pregap.
            foreach (var baseOffset in new[] { RawLayout.NeroPregap, 0L })
            {
                if (RawLayout.Detect(file, baseOffset) is not { } layout) continue;
                var footer = FindChunks(file) ?? file.Length;
                tracks.Add(new TrackLayout(1, TrackKind.Data, path, baseOffset, layout.SectorSize, layout.DataOffset,
                    (footer - baseOffset) / layout.SectorSize));
                break;
            }
        }

        if (tracks.Count == 0) throw new ImageFormatException("No tracks found in the Nero image.");
        return new DiscLayout(ImageFormat.Nrg, Path.GetFullPath(path), tracks);
    }

    private static long? FindChunks(FileStream file)
    {
        if (file.Length < 12) return null;
        Span<byte> tail = stackalloc byte[12];
        RawLayout.ReadAt(file, file.Length - 12, tail);
        if (tail[..4].SequenceEqual("NER5"u8))
        {
            var offset = BinaryPrimitives.ReadInt64BigEndian(tail[4..]);
            return offset >= 0 && offset < file.Length ? offset : null;
        }

        if (tail[4..8].SequenceEqual("NERO"u8))
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(tail[8..]);
            return offset < file.Length ? offset : null;
        }

        return null;
    }

    private static void ReadChunks(FileStream file, string path, long position, List<TrackLayout> tracks)
    {
        Span<byte> header = stackalloc byte[8];
        for (var i = 0; i < MaxChunks && position + 8 <= file.Length; i++)
        {
            RawLayout.ReadAt(file, position, header);
            var id = System.Text.Encoding.ASCII.GetString(header[..4]);
            var size = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            if (id == "END!") break;
            if (position + 8 + size > file.Length) throw new ImageFormatException($"Chunk {id} runs past the end of the file.");
            if (size > 16 * 1024 * 1024) throw new ImageFormatException($"Chunk {id} is implausibly large.");
            var data = new byte[size];
            RawLayout.ReadAt(file, position + 8, data);

            switch (id)
            {
                case "DAOX": ReadDao(file, path, data, wide: true, tracks); break;
                case "DAOI": ReadDao(file, path, data, wide: false, tracks); break;
                case "ETN2": ReadTao(file, path, data, wide: true, tracks); break;
                case "ETNF": ReadTao(file, path, data, wide: false, tracks); break;
            }

            position += 8 + size;
        }
    }

    // DAO header: size (4), catalog number (13), padding (1), type (1), unknown (1), first track (1), last track (1).
    // Each track: ISRC (12), sector size (2), mode (1), padding (3), then pregap, start and end offsets
    // (8 bytes each in DAOX, 4 in DAOI).
    private static void ReadDao(FileStream file, string path, byte[] data, bool wide, List<TrackLayout> tracks)
    {
        const int headerSize = 22;
        var entrySize = wide ? 42 : 30;
        if (data.Length < headerSize) throw new ImageFormatException("DAO chunk is too short.");
        int first = data[20], last = data[21];
        for (var n = 0; n <= last - first; n++)
        {
            var at = headerSize + n * entrySize;
            if (at + entrySize > data.Length) throw new ImageFormatException("DAO chunk is truncated.");
            var entry = data.AsSpan(at, entrySize);
            int sectorSize = BinaryPrimitives.ReadUInt16BigEndian(entry[12..]);
            long start = wide ? BinaryPrimitives.ReadInt64BigEndian(entry[26..]) : BinaryPrimitives.ReadUInt32BigEndian(entry[22..]);
            long end = wide ? BinaryPrimitives.ReadInt64BigEndian(entry[34..]) : BinaryPrimitives.ReadUInt32BigEndian(entry[26..]);
            AddTrack(file, path, first + n, start, end, sectorSize, tracks);
        }
    }

    // TAO entries: offset, size (8 bytes each in ETN2, 4 in ETNF), mode (4), start LBA (4), unknown (4).
    private static void ReadTao(FileStream file, string path, byte[] data, bool wide, List<TrackLayout> tracks)
    {
        var entrySize = wide ? 32 : 20;
        for (var n = 0; (n + 1) * entrySize <= data.Length; n++)
        {
            var entry = data.AsSpan(n * entrySize, entrySize);
            long offset = wide ? BinaryPrimitives.ReadInt64BigEndian(entry) : BinaryPrimitives.ReadUInt32BigEndian(entry);
            long size = wide ? BinaryPrimitives.ReadInt64BigEndian(entry[8..]) : BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var mode = BinaryPrimitives.ReadUInt32BigEndian(entry[(wide ? 16 : 8)..]);
            var sectorSize = mode switch
            {
                0 => 2048,
                6 or 7 => 2352, // audio
                _ when RawLayout.ModeOfRawSector(file, offset) is not null => size % 2448 == 0 && size % 2352 != 0 ? 2448 : 2352,
                _ => size % 2048 == 0 ? 2048 : 2336,
            };
            AddTrack(file, path, tracks.Count + 1, offset, offset + size, sectorSize, tracks, audio: mode is 6 or 7);
        }
    }

    private static void AddTrack(FileStream file, string path, int number, long start, long end, int sectorSize,
        List<TrackLayout> tracks, bool audio = false)
    {
        if (sectorSize is not (2048 or 2336 or 2352 or 2448) || start < 0 || end < start || end > file.Length)
            throw new ImageFormatException($"Track {number} has an invalid layout.");
        var isData = !audio && (sectorSize is 2048 or 2336 || RawLayout.ModeOfRawSector(file, start) is not null);
        var dataOffset = isData ? RawLayout.DataOffsetFor(file, start, sectorSize, 16) : 0;
        tracks.Add(new TrackLayout(number, isData ? TrackKind.Data : TrackKind.Audio, path, start, sectorSize, dataOffset,
            (end - start) / sectorSize));
    }
}
