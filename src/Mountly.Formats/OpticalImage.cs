using Mountly.Core.Images;
using Mountly.Core.Mounting;
using Mountly.Formats.Parsers;

namespace Mountly.Formats;

/// <summary>Entry point for optical images: reads their layout and opens their data track.</summary>
public static class OpticalImage
{
    public static DiscLayout ReadLayout(string path)
    {
        var descriptor = ImageFormats.DescriptorPath(Path.GetFullPath(path));
        if (!File.Exists(descriptor)) throw new FileNotFoundException("Image not found.", descriptor);
        return ImageFormats.Detect(descriptor) switch
        {
            ImageFormat.Iso => IsoLayout(descriptor),
            ImageFormat.CueBin => CueSheet.Read(descriptor),
            ImageFormat.Nrg => NrgImage.Read(descriptor),
            ImageFormat.Mds => MdsImage.Read(descriptor),
            ImageFormat.CloneCd => CloneCdImage.Read(descriptor),
            ImageFormat.RawBin => RawImage.Read(descriptor),
            var other => throw new ImageFormatException($"{other.DisplayName()} is not an optical image format."),
        };
    }

    /// <summary>The 2048-byte user data of the first data track (the file system), as a seekable stream.</summary>
    public static Stream OpenDataTrack(string path) => OpenDataTrack(ReadLayout(path));

    public static Stream OpenDataTrack(DiscLayout layout) =>
        layout.FirstDataTrack is { } track
            ? new TrackStream(track)
            : throw new ImageFormatException("The image has only audio tracks; there is no file system to mount or browse.");

    private static DiscLayout IsoLayout(string path)
    {
        var length = new FileInfo(path).Length;
        return new DiscLayout(ImageFormat.Iso, path,
            [new TrackLayout(1, TrackKind.Data, path, 0, TrackLayout.UserDataSize, 0, (length + 2047) / 2048)]);
    }
}

/// <summary>Converts BIN/CUE, NRG, MDS/MDF, CloneCD and raw images to ISO by copying their data track.</summary>
public sealed class IsoConverter : IImageConverter
{
    private const int BufferSize = 1 << 20;

    public bool CanConvert(string path) => ImageFormats.Detect(path) is ImageFormat.CueBin or ImageFormat.Nrg or ImageFormat.Mds
        or ImageFormat.CloneCd or ImageFormat.RawBin or ImageFormat.Iso;

    public async Task<IReadOnlyList<string>> ConvertToIsoAsync(string sourcePath, string isoPath, IProgress<long>? progress = null,
        CancellationToken cancel = default)
    {
        var layout = OpticalImage.ReadLayout(sourcePath);
        var warnings = Warnings(layout);
        await using var source = OpticalImage.OpenDataTrack(layout);
        await using var target = new FileStream(isoPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        target.SetLength(source.Length); // reserve the space up front: fails fast on a full disk
        var buffer = new byte[BufferSize];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
            done += read;
            progress?.Report(done);
        }

        return warnings;
    }

    public static IReadOnlyList<string> Warnings(DiscLayout layout)
    {
        var warnings = new List<string>();
        if (layout.AudioTrackCount > 0)
            warnings.Add($"{layout.AudioTrackCount} audio track(s) can't be stored in an ISO and were left out.");
        var dataTracks = layout.Tracks.Count(t => t.Kind == TrackKind.Data);
        if (dataTracks > 1) warnings.Add($"Only the first of {dataTracks} data tracks was converted.");
        if (layout.FirstDataTrack is { Number: > 1 })
            warnings.Add("The data track isn't the first track (an enhanced CD); its files may not open from the ISO.");
        return warnings;
    }
}
