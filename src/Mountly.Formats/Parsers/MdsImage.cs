using System.Buffers.Binary;
using Mountly.Core.Images;

namespace Mountly.Formats.Parsers;

/// <summary>
/// Reads Alcohol 120% images: an .mds descriptor (little-endian) and the .mdf data next to it. A lone
/// .mdf is probed like a raw image.
/// </summary>
public static class MdsImage
{
    private const int TrackBlockSize = 80;

    public static DiscLayout Read(string path)
    {
        var full = Path.GetFullPath(path);
        var isMds = full.EndsWith(".mds", StringComparison.OrdinalIgnoreCase);
        var mdf = isMds ? FindMdf(full) : full;
        if (!isMds && File.Exists(Path.ChangeExtension(full, ".mds"))) return Read(Path.ChangeExtension(full, ".mds"));
        if (!isMds) return RawImage.Read(mdf, ImageFormat.Mds);

        var mds = File.ReadAllBytes(full);
        if (mds.Length < 0x58 || !mds.AsSpan(0, 16).SequenceEqual("MEDIA DESCRIPTOR"u8))
            throw new ImageFormatException("Not an Alcohol 120% descriptor (MEDIA DESCRIPTOR signature missing).");

        using var data = CueSheet.OpenRead(mdf);
        int sessionCount = BinaryPrimitives.ReadUInt16LittleEndian(mds.AsSpan(0x14));
        var sessionsOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(mds.AsSpan(0x50));
        var tracks = new List<(TrackLayout Track, long StartSector)>();

        for (var s = 0; s < Math.Max(1, sessionCount); s++)
        {
            var session = Slice(mds, sessionsOffset + s * 24, 24);
            int blockCount = session[10];
            var blocksOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(session[20..]);
            for (var b = 0; b < blockCount; b++)
            {
                var block = Slice(mds, blocksOffset + b * TrackBlockSize, TrackBlockSize);
                int point = block[4];
                if (point is 0 or > 99) continue; // lead-in entries (A0, A1, A2, ...)

                var mode = block[0] & 0x0F;
                var extraOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
                int sectorSize = BinaryPrimitives.ReadUInt16LittleEndian(block[16..]);
                long startSector = BinaryPrimitives.ReadUInt32LittleEndian(block[36..]);
                var startOffset = BinaryPrimitives.ReadInt64LittleEndian(block[40..]);
                long length = 0;
                if (extraOffset > 0 && extraOffset + 8 <= mds.Length)
                    length = BinaryPrimitives.ReadUInt32LittleEndian(mds.AsSpan(extraOffset + 4));

                if (sectorSize is not (2048 or 2336 or 2352 or 2448) || startOffset < 0 || startOffset > data.Length)
                    throw new ImageFormatException($"Track {point} has an invalid layout.");
                var kind = mode == 9 ? TrackKind.Audio : TrackKind.Data;
                var fallback = mode is 0xC or 0xD ? 24 : 16;
                var dataOffset = kind == TrackKind.Data ? RawLayout.DataOffsetFor(data, startOffset, sectorSize, fallback) : 0;
                tracks.Add((new TrackLayout(point, kind, mdf, startOffset, sectorSize, dataOffset, length), startSector));
            }
        }

        if (tracks.Count == 0) throw new ImageFormatException("The descriptor lists no tracks.");

        // Tracks without a length in the descriptor run to the next track (or the end of the file).
        var ordered = tracks.OrderBy(t => t.Track.FileOffset).ToList();
        var result = new List<TrackLayout>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var track = ordered[i].Track;
            if (track.SectorCount <= 0)
            {
                var end = i + 1 < ordered.Count ? ordered[i + 1].Track.FileOffset : data.Length;
                track = track with { SectorCount = (end - track.FileOffset) / track.SectorSize };
            }

            result.Add(track);
        }

        return new DiscLayout(ImageFormat.Mds, full, result.OrderBy(t => t.Number).ToList());
    }

    private static string FindMdf(string mds)
    {
        var mdf = Path.ChangeExtension(mds, ".mdf");
        if (File.Exists(mdf)) return mdf;
        var upper = Path.ChangeExtension(mds, ".MDF");
        if (File.Exists(upper)) return upper;
        throw new ImageFormatException($"'{Path.GetFileName(mdf)}' is missing next to the descriptor.");
    }

    private static ReadOnlySpan<byte> Slice(byte[] buffer, int offset, int length) =>
        offset >= 0 && offset + length <= buffer.Length
            ? buffer.AsSpan(offset, length)
            : throw new ImageFormatException("The descriptor is truncated.");
}
