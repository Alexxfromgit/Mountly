using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Mountly.Windows;

public sealed record RipResult(long Bytes, long UnreadableSectors);

/// <summary>Copies a data CD/DVD/BD in an optical drive (or a mounted image) sector by sector into an ISO.</summary>
[SupportedOSPlatform("windows")]
public static class DiscRipper
{
    private const int SectorSize = 2048;
    private const int ChunkSectors = 32; // 64 KB reads
    private const int Retries = 2;

    /// <summary>Optical drives with a disc in them.</summary>
    public static IReadOnlyList<DriveInfo> OpticalDrives() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom && d.IsReady).ToList();

    public static Task<RipResult> RipAsync(char drive, string isoPath, IProgress<(long Done, long Total)>? progress = null,
        CancellationToken cancel = default) => Task.Run(() =>
    {
        drive = char.ToUpperInvariant(drive);
        using var device = Native.OpenDevice($@"\\.\{drive}:", Native.GenericRead);
        if (device.IsInvalid)
            throw new IOException($"Couldn't open {drive}: (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
        AllowWholeDisc(device);
        var length = DiscLength(device) ?? new DriveInfo(drive + ":").TotalSize;
        length -= length % SectorSize;

        using var output = new FileStream(isoPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        var buffer = new byte[ChunkSectors * SectorSize];
        long position = 0, unreadable = 0;
        try
        {
            while (position < length)
            {
                cancel.ThrowIfCancellationRequested();
                var size = (int)Math.Min(buffer.Length, length - position);
                if (!TryRead(device, buffer.AsSpan(0, size), position))
                {
                    // Read the chunk one sector at a time and keep what can be read; zeros stand in for the rest.
                    for (var s = 0; s < size; s += SectorSize)
                    {
                        var sector = buffer.AsSpan(s, SectorSize);
                        if (!TryRead(device, sector, position + s))
                        {
                            sector.Clear();
                            unreadable++;
                        }
                    }
                }

                output.Write(buffer, 0, size);
                position += size;
                progress?.Report((position, length));
            }
        }
        catch
        {
            output.Dispose();
            try { File.Delete(isoPath); } catch (IOException) { }
            throw;
        }

        return new RipResult(position, unreadable);
    }, cancel);

    private static bool TryRead(SafeFileHandle device, Span<byte> buffer, long offset)
    {
        for (var attempt = 0; attempt <= Retries; attempt++)
        {
            try
            {
                if (RandomAccess.Read(device, buffer, offset) == buffer.Length) return true;
            }
            catch (IOException)
            {
                // a scratch or an unreadable sector: retry, then give up on this range
            }
        }

        return false;
    }

    private static unsafe long? DiscLength(SafeFileHandle device)
    {
        long length;
        return Native.DeviceIoControl(device, Native.IoctlDiskGetLengthInfo, null, 0, &length, sizeof(long), out _, IntPtr.Zero) ? length : null;
    }

    // Lets reads go past the end of the file system to the end of the disc (no-op where not needed).
    private static unsafe void AllowWholeDisc(SafeFileHandle device) =>
        Native.DeviceIoControl(device, Native.FsctlAllowExtendedDasdIo, null, 0, null, 0, out _, IntPtr.Zero);
}
