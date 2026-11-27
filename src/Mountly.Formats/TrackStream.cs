namespace Mountly.Formats;

/// <summary>
/// A read-only, seekable view of a data track's 2048-byte user data, skipping the sync, header, subheader
/// and error-correction bytes of raw sectors. For 2048-byte sectors it is a window onto the file.
/// </summary>
public sealed class TrackStream : Stream
{
    private const int User = TrackLayout.UserDataSize;
    private const int SectorsPerRead = 64;

    private readonly FileStream _file;
    private readonly TrackLayout _track;
    private readonly byte[]? _raw;
    private long _position;

    public TrackStream(TrackLayout track)
    {
        if (track.Kind != TrackKind.Data) throw new ArgumentException("Only data tracks hold a file system.", nameof(track));
        _track = track;
        _file = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1, FileOptions.RandomAccess);
        if (track.SectorSize != User) _raw = new byte[track.SectorSize * SectorsPerRead];
        // A truncated image keeps the sectors it has; reads past them return zeros rather than failing.
        Length = track.SectorCount * User;
    }

    public override long Length { get; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position >= Length || buffer.Length == 0) return 0;
        var wanted = (int)Math.Min(buffer.Length, Length - _position);
        var done = _raw is null ? ReadCooked(buffer[..wanted]) : ReadRaw(buffer[..wanted]);
        _position += done;
        return done;
    }

    private int ReadCooked(Span<byte> buffer)
    {
        var read = RandomAccess.Read(_file.SafeFileHandle, buffer, _track.FileOffset + _position);
        if (read < buffer.Length) buffer[read..].Clear(); // truncated image
        return buffer.Length;
    }

    private int ReadRaw(Span<byte> buffer)
    {
        var done = 0;
        var size = _track.SectorSize;
        while (done < buffer.Length)
        {
            var position = _position + done;
            var sector = position / User;
            var within = (int)(position % User);
            var sectors = (int)Math.Min(SectorsPerRead, (within + buffer.Length - done + User - 1) / User);
            var span = _raw.AsSpan(0, sectors * size);
            var read = RandomAccess.Read(_file.SafeFileHandle, span, _track.FileOffset + sector * size);
            if (read < span.Length) span[read..].Clear();

            for (var i = 0; i < sectors && done < buffer.Length; i++)
            {
                var start = i == 0 ? within : 0;
                var take = Math.Min(User - start, buffer.Length - done);
                span.Slice(i * size + _track.DataOffset + start, take).CopyTo(buffer[done..]);
                done += take;
            }
        }

        return done;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        SeekOrigin.End => Length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _file.Dispose();
        base.Dispose(disposing);
    }
}
