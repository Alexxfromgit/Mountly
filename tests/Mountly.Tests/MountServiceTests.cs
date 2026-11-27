using Mountly.Core.Images;
using Mountly.Core.Mounting;
using Mountly.Core.Storage;
using Mountly.Formats;

namespace Mountly.Tests;

/// <summary>MountService and the stores, against a fake backend (no real mounting; see Mountly.Windows.Tests for that).</summary>
[Collection(nameof(SharedAppData))]
public sealed class MountServiceTests
{
    private static readonly CancellationToken Cancel = TestContext.Current.CancellationToken;

    private sealed class FakeBackend : IMountBackend
    {
        private readonly List<MountedImage> _mounted = [];
        private char _next = 'E';

        public string Name => "Fake";
        public IReadOnlySet<ImageFormat> NativeFormats { get; } = new HashSet<ImageFormat> { ImageFormat.Iso };

        public Task<MountedImage> MountAsync(string path, MountOptions options, CancellationToken cancel = default)
        {
            var image = new MountedImage
            {
                ImagePath = Path.GetFullPath(path),
                BackingPath = Path.GetFullPath(path),
                DriveLetters = options.NoDriveLetter ? [] : [$"{options.PreferredLetter ?? _next++}:"],
                SizeBytes = new FileInfo(path).Length,
            };
            _mounted.Add(image);
            return Task.FromResult(image);
        }

        public Task UnmountAsync(string backingPath, CancellationToken cancel = default)
        {
            if (_mounted.RemoveAll(m => string.Equals(m.BackingPath, backingPath, StringComparison.OrdinalIgnoreCase)) == 0)
                throw new MountException(MountError.NotMounted, "not mounted");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MountedImage>> ListAsync(CancellationToken cancel = default) =>
            Task.FromResult<IReadOnlyList<MountedImage>>(_mounted.ToList());
    }

    [Fact]
    public async Task Native_images_mount_directly_and_unmount_by_letter()
    {
        var iso = await TestImages.CreateIsoAsync(TestImages.NewDirectory());
        var service = new MountService(new FakeBackend(), new IsoConverter());

        var mounted = await service.MountAsync(iso, cancel: Cancel);

        Assert.False(mounted.IsConverted);
        Assert.Equal("E:", mounted.FirstLetter);
        var error = await Assert.ThrowsAsync<MountException>(() => service.MountAsync(iso, cancel: Cancel));
        Assert.Equal(MountError.AlreadyMounted, error.Error);
        await service.UnmountAsync("e", Cancel);
        Assert.Empty(await service.ListAsync(Cancel));
    }

    [Fact]
    public async Task Other_formats_are_converted_to_a_cached_iso_that_goes_away_on_eject()
    {
        var dir = TestImages.NewDirectory();
        var iso = await File.ReadAllBytesAsync(await TestImages.CreateIsoAsync(dir), Cancel);
        var cue = TestImages.WriteCue(dir, "game", "MODE1/2352", TestImages.RawSectors(iso));
        var service = new MountService(new FakeBackend(), new IsoConverter());

        var mounted = await service.MountAsync(Path.ChangeExtension(cue, ".bin"), cancel: Cancel); // the .bin resolves to its .cue

        Assert.True(mounted.IsConverted);
        Assert.Equal(cue, mounted.ImagePath);
        Assert.Equal(iso, await File.ReadAllBytesAsync(mounted.BackingPath, Cancel));
        var listed = Assert.Single(await service.ListAsync(Cancel));
        Assert.Equal(cue, listed.ImagePath); // reported under the user's file, not the cache
        Assert.Equal(ImageFormat.CueBin, listed.Format);

        await service.UnmountAsync(cue, Cancel);
        Assert.False(File.Exists(mounted.BackingPath));
    }

    [Fact]
    public async Task Unsupported_and_missing_files_fail_with_a_reason()
    {
        var dir = TestImages.NewDirectory();
        var text = Path.Combine(dir, "notes.txt");
        await File.WriteAllTextAsync(text, "x", Cancel);
        var service = new MountService(new FakeBackend(), new IsoConverter());

        Assert.Equal(MountError.UnsupportedFormat, (await Assert.ThrowsAsync<MountException>(() => service.MountAsync(text, cancel: Cancel))).Error);
        Assert.Equal(MountError.FileNotFound,
            (await Assert.ThrowsAsync<MountException>(() => service.MountAsync(Path.Combine(dir, "nope.iso"), cancel: Cancel))).Error);
        Assert.False(service.CanMount(text));
    }

    [Theory]
    [InlineData("E", 'E')]
    [InlineData("e:", 'E')]
    [InlineData(@"M:\", 'M')]
    [InlineData("EF", null)]
    [InlineData(@"C:\x.iso", null)]
    public void Drive_letters_are_parsed_leniently(string text, char? expected) => Assert.Equal(expected, MountService.ParseLetter(text));

    [Fact]
    public void The_catalog_keeps_pinned_images_and_the_most_recent_others()
    {
        var store = new CatalogStore();
        var dir = TestImages.NewDirectory();
        var pinned = Path.Combine(dir, "pinned.iso");
        store.Touch(pinned);
        store.SetPinned(pinned, true);
        for (var i = 0; i < CatalogStore.MaxRecent + 5; i++) store.Touch(Path.Combine(dir, $"image{i}.iso"));

        var all = store.Load();

        Assert.Equal(CatalogStore.MaxRecent + 1, all.Count);
        Assert.Equal(pinned, all[0].Path);
        Assert.DoesNotContain(all, e => e.Path.EndsWith("image0.iso", StringComparison.Ordinal));
    }

    [Fact]
    public void Damaged_settings_fall_back_to_defaults()
    {
        File.WriteAllText(AppPaths.SettingsFile, "{ this is not json");
        var settings = new SettingsStore();
        Assert.True(settings.Current.RemountOnStart);
        settings.Current.Theme = "Dark";
        settings.Save();
        Assert.Equal("Dark", new SettingsStore().Current.Theme);
    }
}

[CollectionDefinition(nameof(SharedAppData), DisableParallelization = true)]
public sealed class SharedAppData;
