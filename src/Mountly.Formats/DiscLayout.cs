using Mountly.Core.Images;

namespace Mountly.Formats;

public enum TrackKind
{
    Data,
    Audio,
}

/// <summary>
/// Where one track's sectors live in an image file. Data tracks hold 2048 bytes of user data per sector,
/// starting <see cref="DataOffset"/> bytes into each <see cref="SectorSize"/>-byte raw sector.
/// </summary>
public sealed record TrackLayout(
    int Number,
    TrackKind Kind,
    string FilePath,
    long FileOffset,
    int SectorSize,
    int DataOffset,
    long SectorCount)
{
    public const int UserDataSize = 2048;

    /// <summary>Bytes of user data (data tracks) or raw audio (audio tracks).</summary>
    public long Length => Kind == TrackKind.Data ? SectorCount * UserDataSize : SectorCount * SectorSize;

    public string ModeName => Kind == TrackKind.Audio
        ? "Audio"
        : (SectorSize, DataOffset) switch
        {
            (2048, _) => "Mode 1/2048",
            (2336, _) => "Mode 2/2336",
            (_, 16) => $"Mode 1/{SectorSize}",
            (_, 24) => $"Mode 2 Form 1/{SectorSize}",
            _ => $"Data/{SectorSize}",
        };
}

/// <summary>The tracks of an optical image.</summary>
public sealed record DiscLayout(ImageFormat Format, string DescriptorPath, IReadOnlyList<TrackLayout> Tracks)
{
    public TrackLayout? FirstDataTrack => Tracks.FirstOrDefault(t => t.Kind == TrackKind.Data);

    public int AudioTrackCount => Tracks.Count(t => t.Kind == TrackKind.Audio);
}

public sealed class ImageFormatException(string message) : Exception(message);
