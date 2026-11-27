using Mountly.Core.Images;
using Mountly.Formats;

namespace Mountly.Tests;

public sealed class FormatTests
{
    private static readonly CancellationToken Cancel = TestContext.Current.CancellationToken;

    public static TheoryData<string> Layouts => ["MODE1/2352", "MODE2/2352", "MODE1/2352+sub", "MODE1/2048"];

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task Cue_sheets_yield_the_original_iso(string layout)
    {
        var dir = TestImages.NewDirectory();
        var iso = await File.ReadAllBytesAsync(await TestImages.CreateIsoAsync(dir), Cancel);
        var (mode, bin) = layout switch
        {
            "MODE1/2352" => ("MODE1/2352", TestImages.RawSectors(iso)),
            "MODE2/2352" => ("MODE2/2352", TestImages.RawSectors(iso, mode: 2)),
            "MODE1/2352+sub" => ("MODE1/2352", TestImages.RawSectors(iso, subchannel: true)),
            _ => ("MODE1/2048", iso),
        };
        // Sub-channel data makes 2448-byte sectors; cue sheets still say 2352, so test that one through raw detection.
        var path = layout.EndsWith("+sub")
            ? WriteRaw(dir, bin)
            : TestImages.WriteCue(dir, "game", mode, bin);

        await using var track = OpticalImage.OpenDataTrack(path);
        Assert.Equal(iso, TestImages.ReadAll(track));
    }

    private static string WriteRaw(string dir, byte[] bin)
    {
        var path = Path.Combine(dir, "raw.bin");
        File.WriteAllBytes(path, bin);
        return path;
    }

    [Fact]
    public async Task A_bin_without_a_cue_is_detected_from_its_sectors()
    {
        var dir = TestImages.NewDirectory();
        var iso = await File.ReadAllBytesAsync(await TestImages.CreateIsoAsync(dir), Cancel);
        var bin = WriteRaw(dir, TestImages.RawSectors(iso, mode: 2));

        var layout = OpticalImage.ReadLayout(bin);

        Assert.Equal(ImageFormat.RawBin, layout.Format);
        Assert.Equal(2352, layout.Tracks[0].SectorSize);
        Assert.Equal(24, layout.Tracks[0].DataOffset);
        await using var track = OpticalImage.OpenDataTrack(layout);
        Assert.Equal(iso, TestImages.ReadAll(track));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Nero_images_yield_the_original_iso(bool v2)
    {
        var dir = TestImages.NewDirectory();
        var iso = await File.ReadAllBytesAsync(await TestImages.CreateIsoAsync(dir), Cancel);
        var nrg = TestImages.WriteNrg(dir, iso, v2);

        var layout = OpticalImage.ReadLayout(nrg);

        Assert.Equal(ImageFormat.Nrg, layout.Format);
        Assert.Single(layout.Tracks);
        await using var track = OpticalImage.OpenDataTrack(layout);
        Assert.Equal(iso, TestImages.ReadAll(track));
    }

    [Fact]
    public async Task Alcohol_images_yield_the_original_iso_from_the_mds_or_the_mdf()
    {
        var dir = TestImages.NewDirectory();
        var iso = await File.ReadAllBytesAsync(await TestImages.CreateIsoAsync(dir), Cancel);
        var mds = TestImages.WriteMds(dir, iso);

        foreach (var path in new[] { mds, Path.ChangeExtension(mds, ".mdf") })
        {
            var layout = OpticalImage.ReadLayout(path);
            Assert.Equal(ImageFormat.Mds, layout.Format);
            Assert.Equal(iso.Length / TestImages.User, layout.Tracks[0].SectorCount);
            await using var track = OpticalImage.OpenDataTrack(layout);
            Assert.Equal(iso, TestImages.ReadAll(track));
        }
    }

    [Fact]
    public async Task CloneCd_images_yield_the_original_iso()
    {
        var dir = TestImages.NewDirectory();
        var iso = await File.ReadAllBytesAsync(await TestImages.CreateIsoAsync(dir), Cancel);
        var ccd = TestImages.WriteCloneCd(dir, iso);

        await using var track = OpticalImage.OpenDataTrack(ccd);
        Assert.Equal(iso, TestImages.ReadAll(track));
        Assert.Equal(ImageFormat.CloneCd, ImageFormats.Detect(Path.ChangeExtension(ccd, ".img")));
    }

    [Fact]
    public async Task Converting_writes_the_data_track_and_warns_about_audio()
    {
        var dir = TestImages.NewDirectory();
        var iso = await File.ReadAllBytesAsync(await TestImages.CreateIsoAsync(dir), Cancel);
        var raw = TestImages.RawSectors(iso);
        var audio = new byte[2352 * 300];
        await File.WriteAllBytesAsync(Path.Combine(dir, "mixed.bin"), [.. raw, .. audio], Cancel);
        var sectors = iso.Length / TestImages.User;
        var start = $"{sectors / 75 / 60:00}:{sectors / 75 % 60:00}:{sectors % 75:00}";
        var cue = Path.Combine(dir, "mixed.cue");
        await File.WriteAllTextAsync(cue, $"""
            FILE "mixed.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 {start}
            """, Cancel);
        var output = Path.Combine(dir, "out.iso");

        var warnings = await new IsoConverter().ConvertToIsoAsync(cue, output, cancel: Cancel);

        Assert.Equal(iso, await File.ReadAllBytesAsync(output, Cancel));
        Assert.Contains(warnings, w => w.Contains("audio track", StringComparison.Ordinal));
        var layout = OpticalImage.ReadLayout(cue);
        Assert.Equal(300, layout.Tracks[1].SectorCount);
        Assert.Equal(TrackKind.Audio, layout.Tracks[1].Kind);
    }

    [Fact]
    public async Task A_cue_sheet_cannot_point_outside_its_folder()
    {
        var dir = TestImages.NewDirectory();
        var iso = await File.ReadAllBytesAsync(await TestImages.CreateIsoAsync(dir), Cancel);
        var inner = Path.Combine(dir, "inner");
        Directory.CreateDirectory(inner);
        // The cue names ..\secret.bin; only a file of that name next to the cue may be used.
        await File.WriteAllBytesAsync(Path.Combine(dir, "secret.bin"), TestImages.RawSectors(iso), Cancel);
        var cue = Path.Combine(inner, "evil.cue");
        await File.WriteAllTextAsync(cue, "FILE \"..\\secret.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n", Cancel);

        var error = Assert.Throws<ImageFormatException>(() => OpticalImage.ReadLayout(cue));
        Assert.Contains("isn't next to it", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_renamed_bin_is_found_through_the_cue_name()
    {
        var dir = TestImages.NewDirectory();
        var iso = await File.ReadAllBytesAsync(await TestImages.CreateIsoAsync(dir), Cancel);
        var cue = TestImages.WriteCue(dir, "renamed", "MODE1/2352", TestImages.RawSectors(iso), fileNameInCue: "Original Name (Track 1).bin");

        await using var track = OpticalImage.OpenDataTrack(cue);
        Assert.Equal(iso.Length, track.Length);
    }

    [Theory]
    [InlineData("FILE \"x.bin\" BINARY\n  TRACK 01 MODE3/9999\n    INDEX 01 00:00:00", "unsupported track mode")]
    [InlineData("FILE \"x.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:99:00", "malformed time")]
    [InlineData("  TRACK 01 MODE1/2352", "TRACK before any FILE")]
    [InlineData("REM nothing here", "no tracks")]
    public void Malformed_cue_sheets_are_rejected_with_a_reason(string text, string reason)
    {
        var dir = TestImages.NewDirectory();
        File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[2352 * 20]);
        var cue = Path.Combine(dir, "x.cue");
        File.WriteAllText(cue, text);

        var error = Assert.Throws<ImageFormatException>(() => OpticalImage.ReadLayout(cue));
        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Garbage_is_not_mistaken_for_an_image()
    {
        var dir = TestImages.NewDirectory();
        var junk = Path.Combine(dir, "junk.nrg");
        var bytes = new byte[100_000];
        new Random(7).NextBytes(bytes);
        File.WriteAllBytes(junk, bytes);

        Assert.Throws<ImageFormatException>(() => OpticalImage.ReadLayout(junk));
    }

    [Theory]
    [InlineData("a.iso", ImageFormat.Iso)]
    [InlineData("a.CUE", ImageFormat.CueBin)]
    [InlineData("a.nrg", ImageFormat.Nrg)]
    [InlineData("a.mdf", ImageFormat.Mds)]
    [InlineData("a.vhdx", ImageFormat.Vhdx)]
    [InlineData("a.bin", ImageFormat.RawBin)]
    [InlineData("a.txt", ImageFormat.Unknown)]
    public void Formats_are_detected_from_the_extension(string name, ImageFormat expected) =>
        Assert.Equal(expected, ImageFormats.Detect(name));
}
