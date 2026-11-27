using Mountly.Core.Mounting;
using Mountly.Core.Storage;
using Mountly.Formats;
using Mountly.Tools;
using Mountly.Windows;

namespace Mountly.Windows.Tests;

/// <summary>
/// Real mounts through Windows' Storage provider. They run on Windows (CI's windows-latest included) and
/// clean up after themselves; elsewhere they are skipped.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class MountIntegrationTests
{
    private static readonly CancellationToken Cancel = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_iso_mounts_with_a_drive_letter_and_its_files_can_be_read()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var (dir, iso) = await CreateIsoAsync();
        AppPaths.OverrideRoot = Path.Combine(dir, "home");
        var service = new MountService(new StorageMountBackend(), new IsoConverter());

        var mounted = await service.MountAsync(iso, cancel: Cancel);
        try
        {
            Assert.NotNull(mounted.FirstLetter);
            Assert.Equal("hello from a mounted image", await File.ReadAllTextAsync(Path.Combine(mounted.FirstLetter + "\\", "hello.txt"), Cancel));
            Assert.Equal("MOUNTLY_IT", mounted.VolumeLabel);
            Assert.Contains(await service.ListAsync(Cancel), m => m.FirstLetter == mounted.FirstLetter);
        }
        finally
        {
            await service.UnmountAsync(mounted.FirstLetter ?? iso, Cancel);
        }

        Assert.DoesNotContain(await service.ListAsync(Cancel), m => string.Equals(m.BackingPath, mounted.BackingPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_bin_cue_mounts_through_conversion_and_ripping_the_drive_gives_back_the_iso()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var (dir, iso) = await CreateIsoAsync();
        AppPaths.OverrideRoot = Path.Combine(dir, "home");
        var bytes = await File.ReadAllBytesAsync(iso, Cancel);
        var cue = WriteCue(dir, bytes);
        var service = new MountService(new StorageMountBackend(), new IsoConverter());

        var mounted = await service.MountAsync(cue, cancel: Cancel);
        try
        {
            Assert.True(mounted.IsConverted);
            Assert.NotNull(mounted.FirstLetter);
            var ripped = Path.Combine(dir, "ripped.iso");
            var result = await DiscRipper.RipAsync(mounted.FirstLetter[0], ripped, cancel: Cancel);
            Assert.Equal(0, result.UnreadableSectors);
            Assert.Equal(bytes, (await File.ReadAllBytesAsync(ripped, Cancel))[..bytes.Length]);
        }
        finally
        {
            await service.UnmountAsync(cue, Cancel);
        }

        Assert.False(File.Exists(mounted.BackingPath)); // the converted ISO is removed on eject
    }

    private static async Task<(string Dir, string Iso)> CreateIsoAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mountly-it", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(dir, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "hello.txt"), "hello from a mounted image", Cancel);
        var iso = Path.Combine(dir, "it.iso");
        await IsoBuilder.BuildAsync(source, iso, new IsoBuildOptions { VolumeLabel = "Mountly IT" }, cancel: Cancel);
        return (dir, iso);
    }

    private static string WriteCue(string dir, byte[] iso)
    {
        byte[] sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        var raw = new byte[iso.Length / 2048 * 2352];
        for (var i = 0; i < iso.Length / 2048; i++)
        {
            sync.CopyTo(raw, i * 2352);
            raw[i * 2352 + 15] = 1;
            Array.Copy(iso, i * 2048, raw, i * 2352 + 16, 2048);
        }

        File.WriteAllBytes(Path.Combine(dir, "it.bin"), raw);
        var cue = Path.Combine(dir, "it.cue");
        File.WriteAllText(cue, "FILE \"it.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
        return cue;
    }
}
