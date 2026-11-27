using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Mountly.Windows;

[SupportedOSPlatform("windows")]
internal static partial class Native
{
    public const uint GenericRead = 0x80000000;
    public const uint FileShareRead = 1;
    public const uint FileShareWrite = 2;
    public const uint OpenExisting = 3;

    public const uint IoctlStorageGetDeviceNumber = 0x002D1080;
    public const uint IoctlDiskGetLengthInfo = 0x0007405C;
    public const uint FsctlAllowExtendedDasdIo = 0x00090083;

    [StructLayout(LayoutKind.Sequential)]
    public struct StorageDeviceNumber
    {
        public uint DeviceType;
        public uint DeviceNumber;
        public uint PartitionNumber;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags,
        IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* inBuffer, uint inSize, void* outBuffer,
        uint outSize, out uint returned, IntPtr overlapped);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetLogicalDrives();

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetVolumeNameForVolumeMountPoint(string mountPoint, char* volumeName, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteVolumeMountPoint(string mountPoint);

    [LibraryImport("kernel32.dll", EntryPoint = "SetVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetVolumeMountPoint(string mountPoint, string volumeName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetVolumeInformation(string root, char* volumeName, uint volumeNameSize, IntPtr serial,
        IntPtr maxComponent, IntPtr flags, char* fsName, uint fsNameSize);

    [LibraryImport("shell32.dll")]
    public static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    public static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* path, uint length, uint flags);

    /// <summary>The real path of an existing file (links, subst drives and file redirection resolved), or null.</summary>
    public static unsafe string? FinalPath(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new char[1024];
            fixed (char* p = buffer)
            {
                var length = GetFinalPathNameByHandle(handle, p, (uint)buffer.Length, 0);
                if (length == 0 || length >= buffer.Length) return null;
                var result = new string(buffer, 0, (int)length);
                if (result.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + result[8..];
                return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SafeFileHandle OpenDevice(string path, uint access) =>
        CreateFile(path, access, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    public static unsafe StorageDeviceNumber? DeviceNumber(string devicePath)
    {
        using var handle = OpenDevice(devicePath, 0);
        if (handle.IsInvalid) return null;
        StorageDeviceNumber number;
        return DeviceIoControl(handle, IoctlStorageGetDeviceNumber, null, 0, &number, (uint)sizeof(StorageDeviceNumber), out _, IntPtr.Zero)
            ? number
            : null;
    }

    public static unsafe string? VolumeGuidPath(string letterRoot)
    {
        var buffer = stackalloc char[64];
        return GetVolumeNameForVolumeMountPoint(letterRoot, buffer, 64) ? new string(buffer) : null;
    }

    public static unsafe string? VolumeLabel(string letterRoot)
    {
        var name = stackalloc char[264];
        var fs = stackalloc char[64];
        return GetVolumeInformation(letterRoot, name, 264, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, fs, 64) ? new string(name) : null;
    }
}
