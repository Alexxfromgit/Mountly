using System.Text;
using Mountly.Tools;

namespace Mountly.Tests;

public sealed class ToolTests
{
    private static readonly CancellationToken Cancel = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Built_isos_have_the_files_and_a_clean_joliet_label()
    {
        var dir = TestImages.NewDirectory();
        var iso = await TestImages.CreateIsoAsync(dir, label: "My Backup");

        using var inspector = ImageInspector.Open(iso);

        Assert.Equal("MY_BACKUP", inspector.VolumeLabel); // no "††††" padding (DiscUtils bug, fixed after build)
        Assert.Contains("Joliet", inspector.FileSystem, StringComparison.Ordinal);
        var names = inspector.Walk().Select(e => e.Path).ToList();
        Assert.Contains("readme.txt", names);
        Assert.Contains(@"Sub Folder With Spaces\data.bin", names);
    }

    [Fact]
    public async Task Extracting_everything_reproduces_the_source_folder()
    {
        var dir = TestImages.NewDirectory();
        var iso = await TestImages.CreateIsoAsync(dir);
        var target = Path.Combine(dir, "extracted");

        using (var inspector = ImageInspector.Open(iso))
            await inspector.ExtractAsync("", target, cancel: Cancel);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir, "source"), "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(Path.Combine(dir, "source"), file));
            Assert.Equal(await File.ReadAllBytesAsync(file, Cancel), await File.ReadAllBytesAsync(copy, Cancel));
        }
    }

    [Theory]
    [InlineData(@"..\..\evil.txt")]
    [InlineData(@"a\..\..\evil.txt")]
    [InlineData("con:evil")]
    public void Extraction_never_writes_outside_the_destination(string name)
    {
        var root = TestImages.NewDirectory();
        var path = ImageInspector.SafeJoin(root, name);
        Assert.StartsWith(root, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_iso_cannot_be_written_into_its_own_source_folder()
    {
        var dir = TestImages.NewDirectory();
        Directory.CreateDirectory(Path.Combine(dir, "src"));
        await Assert.ThrowsAsync<IOException>(() => IsoBuilder.BuildAsync(Path.Combine(dir, "src"), Path.Combine(dir, "src", "self.iso"), cancel: Cancel));
    }

    [Theory]
    [InlineData("my disc!", "MY_DISC_")]
    [InlineData("", "CDROM")]
    [InlineData("a_very_long_label_that_goes_past_the_limit", "A_VERY_LONG_LABEL_THAT_GOES_PAST")]
    public void Volume_labels_are_sanitized(string input, string expected) => Assert.Equal(expected, IsoBuilder.SanitizeLabel(input));

    [Fact]
    public async Task Checksums_match_known_vectors()
    {
        var file = Path.Combine(TestImages.NewDirectory(), "abc.txt");
        await File.WriteAllTextAsync(file, "abc", Encoding.ASCII, Cancel);

        var hashes = await Checksums.ComputeAsync(file, [ChecksumAlgorithm.Md5, ChecksumAlgorithm.Sha1, ChecksumAlgorithm.Sha256], cancel: Cancel);

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", hashes[ChecksumAlgorithm.Md5]);
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", hashes[ChecksumAlgorithm.Sha1]);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hashes[ChecksumAlgorithm.Sha256]);
    }

    [Fact]
    public async Task Verify_reads_gnu_and_bsd_checksum_files()
    {
        var dir = TestImages.NewDirectory();
        var file = Path.Combine(dir, "image.iso");
        await File.WriteAllTextAsync(file, "abc", Encoding.ASCII, Cancel);
        var gnu = Path.Combine(dir, "SHA256SUMS");
        await File.WriteAllTextAsync(gnu,
            "0000000000000000000000000000000000000000000000000000000000000000 *other.iso\n" +
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad *image.iso\n", Cancel);
        var bsd = Path.Combine(dir, "image.iso.md5");
        await File.WriteAllTextAsync(bsd, "MD5 (image.iso) = 900150983CD24FB0D6963F7D28E17F72\n", Cancel);

        Assert.True((await Checksums.VerifyAsync(file, gnu, cancel: Cancel)).IsMatch);
        var md5 = await Checksums.VerifyAsync(file, bsd, cancel: Cancel);
        Assert.True(md5.IsMatch);
        Assert.Equal(ChecksumAlgorithm.Md5, md5.Algorithm);
        Assert.False((await Checksums.VerifyAsync(file, new string('1', 40), cancel: Cancel)).IsMatch);
    }

    [Fact]
    public void Unrecognised_checksums_are_rejected() =>
        Assert.Throws<FormatException>(() => Checksums.ResolveExpected("abcd", "x.iso"));
}
