namespace Mountly.Formats;

/// <summary>
/// Works out how a data track is laid out when no descriptor says so (or to double-check one): it looks
/// for the CD sync pattern and for the ISO 9660 / UDF volume descriptors at sector 16.
/// </summary>
public static class RawLayout
{
    /// <summary>Raw sector sizes with their user-data offset for Mode 1 (16) and Mode 2 Form 1 (24).</summary>
    private static readonly (int Size, int Offset)[] Candidates =
    [
        (2048, 0), (2352, 16), (2352, 24), (2448, 16), (2448, 24), (2336, 8),
    ];

    /// <summary>Nero puts 150 sectors of 2048 bytes before the data track of a DAO image.</summary>
    public const long NeroPregap = 150 * 2048;

    private static ReadOnlySpan<byte> Sync => [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    /// <summary>
    /// Finds sector size and data offset of a data track starting at <paramref name="baseOffset"/>, or null
    /// when it has neither a recognisable file system nor CD sync headers.
    /// </summary>
    public static (int SectorSize, int DataOffset)? Detect(FileStream file, long baseOffset)
    {
        foreach (var (size, offset) in Candidates)
            if (HasVolumeDescriptor(file, baseOffset + 16L * size + offset))
                return (size, offset);

        // No file system we know, but raw sectors with sync headers still tell us the mode.
        if (ModeOfRawSector(file, baseOffset) is { } mode)
        {
            var size = file.Length - baseOffset >= 2448 && (file.Length - baseOffset) % 2448 == 0 && (file.Length - baseOffset) % 2352 != 0 ? 2448 : 2352;
            return (size, mode == 2 ? 24 : 16);
        }

        return null;
    }

    /// <summary>The mode byte (1 or 2) of a raw 2352-byte sector at <paramref name="offset"/>, or null if it has no sync header.</summary>
    public static int? ModeOfRawSector(FileStream file, long offset)
    {
        Span<byte> header = stackalloc byte[16];
        if (!ReadAt(file, offset, header)) return null;
        if (!header[..12].SequenceEqual(Sync)) return null;
        return header[15] is 1 or 2 ? header[15] : null;
    }

    /// <summary>The user-data offset for a data track whose sector size is known: checks the first sector's mode.</summary>
    public static int DataOffsetFor(FileStream file, long trackOffset, int sectorSize, int fallback) => sectorSize switch
    {
        2048 => 0,
        2336 => 8,
        _ => ModeOfRawSector(file, trackOffset) switch
        {
            1 => 16,
            2 => 24,
            _ => fallback,
        },
    };

    internal static bool HasVolumeDescriptor(FileStream file, long offset)
    {
        Span<byte> id = stackalloc byte[6];
        if (!ReadAt(file, offset, id)) return false;
        var tag = id[1..6];
        return (tag.SequenceEqual("CD001"u8) && id[0] is 0 or 1 or 2 or 255) ||
               tag.SequenceEqual("BEA01"u8) || tag.SequenceEqual("NSR02"u8) || tag.SequenceEqual("NSR03"u8) ||
               tag.SequenceEqual("TEA01"u8);
    }

    internal static bool ReadAt(FileStream file, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > file.Length) return false;
        return RandomAccess.Read(file.SafeFileHandle, buffer, offset) == buffer.Length;
    }
}
