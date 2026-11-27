using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Mountly.Core.Storage;
using Mountly.Tools;

namespace Mountly.Tests;

/// <summary>
/// Builds test images at run time (no binary fixtures in the repository): a small ISO from a folder, then
/// the same data track wrapped in every format Mountly reads, byte for byte as the real tools lay it out.
/// </summary>
internal static class TestImages
{
    public const int User = 2048;

    private static readonly byte[] Sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    [ModuleInitializer]
    internal static void IsolateAppData() =>
        AppPaths.OverrideRoot = Path.Combine(Path.GetTempPath(), "mountly-tests-" + Environment.ProcessId);

    public static string NewDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mountly-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>An ISO with a few files (one in a subfolder with a long, spaced name).</summary>
    public static async Task<string> CreateIsoAsync(string dir, string label = "Test Disc")
    {
        var source = Path.Combine(dir, "source");
        Directory.CreateDirectory(Path.Combine(source, "Sub Folder With Spaces"));
        await File.WriteAllTextAsync(Path.Combine(source, "readme.txt"), "hello from mountly");
        var random = new byte[300_000];
        new Random(42).NextBytes(random);
        await File.WriteAllBytesAsync(Path.Combine(source, "Sub Folder With Spaces", "data.bin"), random);
        var iso = Path.Combine(dir, "test.iso");
        await IsoBuilder.BuildAsync(source, iso, new IsoBuildOptions { VolumeLabel = label });
        return iso;
    }

    /// <summary>Raw CD sectors: Mode 1 (16-byte header) or Mode 2 Form 1 (24 bytes incl. subheader), optionally + 96 bytes subchannel.</summary>
    public static byte[] RawSectors(byte[] iso, int mode = 1, bool subchannel = false)
    {
        var sectors = iso.Length / User;
        var size = 2352 + (subchannel ? 96 : 0);
        var raw = new byte[sectors * size];
        for (var i = 0; i < sectors; i++)
        {
            var s = raw.AsSpan(i * size, size);
            Sync.CopyTo(s);
            s[15] = (byte)mode;
            var offset = 16;
            if (mode == 2)
            {
                s[18] = 0x08; // subheader: data, form 1
                s[22] = 0x08;
                offset = 24;
            }

            iso.AsSpan(i * User, User).CopyTo(s[offset..]);
        }

        return raw;
    }

    public static string WriteCue(string dir, string name, string mode, byte[] bin, string? fileNameInCue = null)
    {
        File.WriteAllBytes(Path.Combine(dir, name + ".bin"), bin);
        var cue = Path.Combine(dir, name + ".cue");
        File.WriteAllText(cue, $"FILE \"{fileNameInCue ?? name + ".bin"}\" BINARY\r\n  TRACK 01 {mode}\r\n    INDEX 01 00:00:00\r\n");
        return cue;
    }

    /// <summary>A Nero image: 150-sector pregap, the track, then DAO chunks and the v2 ("NER5") or v1 ("NERO") footer.</summary>
    public static string WriteNrg(string dir, byte[] iso, bool v2)
    {
        const long pregap = 150 * User;
        using var ms = new MemoryStream();
        ms.Write(new byte[pregap]);
        ms.Write(iso);
        var chunks = ms.Position;

        var entrySize = v2 ? 42 : 30;
        var dao = new byte[22 + entrySize];
        BinaryPrimitives.WriteUInt32BigEndian(dao, (uint)dao.Length);
        dao[20] = 1; // first track
        dao[21] = 1; // last track
        var e = dao.AsSpan(22);
        BinaryPrimitives.WriteUInt16BigEndian(e[12..], User);
        if (v2)
        {
            BinaryPrimitives.WriteInt64BigEndian(e[18..], pregap);
            BinaryPrimitives.WriteInt64BigEndian(e[26..], pregap);
            BinaryPrimitives.WriteInt64BigEndian(e[34..], pregap + iso.Length);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(e[18..], (uint)pregap);
            BinaryPrimitives.WriteUInt32BigEndian(e[22..], (uint)pregap);
            BinaryPrimitives.WriteUInt32BigEndian(e[26..], (uint)(pregap + iso.Length));
        }

        WriteChunk(ms, v2 ? "DAOX" : "DAOI", dao);
        WriteChunk(ms, "END!", []);
        Span<byte> footer = stackalloc byte[12];
        if (v2)
        {
            Encoding.ASCII.GetBytes("NER5").CopyTo(footer);
            BinaryPrimitives.WriteInt64BigEndian(footer[4..], chunks);
            ms.Write(footer);
        }
        else
        {
            Encoding.ASCII.GetBytes("NERO").CopyTo(footer);
            BinaryPrimitives.WriteUInt32BigEndian(footer[4..], (uint)chunks);
            ms.Write(footer[..8]);
        }

        var path = Path.Combine(dir, v2 ? "image-v2.nrg" : "image-v1.nrg");
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static void WriteChunk(Stream s, string id, byte[] data)
    {
        s.Write(Encoding.ASCII.GetBytes(id));
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)data.Length);
        s.Write(size);
        s.Write(data);
    }

    /// <summary>An Alcohol 120% descriptor (.mds) for one Mode 1 track of raw 2352-byte sectors in the .mdf.</summary>
    public static string WriteMds(string dir, byte[] iso)
    {
        var raw = RawSectors(iso);
        File.WriteAllBytes(Path.Combine(dir, "image.mdf"), raw);
        var mds = new byte[0xC8];
        Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR").CopyTo(mds, 0);
        mds[16] = 1;
        mds[17] = 3; // version 1.3
        BinaryPrimitives.WriteUInt16LittleEndian(mds.AsSpan(0x14), 1); // one session
        BinaryPrimitives.WriteUInt32LittleEndian(mds.AsSpan(0x50), 0x58); // sessions
        var session = mds.AsSpan(0x58, 24);
        session[10] = 1; // one block
        BinaryPrimitives.WriteUInt16LittleEndian(session[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(session[14..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(session[20..], 0x70); // track blocks
        var track = mds.AsSpan(0x70, 80);
        track[0] = 0xAA; // mode 1
        track[4] = 1; // track 1
        BinaryPrimitives.WriteUInt32LittleEndian(track[12..], 0xC0); // extra block
        BinaryPrimitives.WriteUInt16LittleEndian(track[16..], 2352);
        BinaryPrimitives.WriteInt64LittleEndian(track[40..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(mds.AsSpan(0xC4), (uint)(iso.Length / User)); // length in sectors
        var path = Path.Combine(dir, "image.mds");
        File.WriteAllBytes(path, mds);
        return path;
    }

    public static string WriteCloneCd(string dir, byte[] iso)
    {
        File.WriteAllBytes(Path.Combine(dir, "clone.img"), RawSectors(iso));
        var ccd = Path.Combine(dir, "clone.ccd");
        File.WriteAllText(ccd, "[CloneCD]\r\nVersion=3\r\n[Disc]\r\nTocEntries=4\r\nSessions=1\r\n[TRACK 1]\r\nMODE=1\r\nINDEX 1=0\r\n");
        return ccd;
    }

    public static byte[] ReadAll(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
