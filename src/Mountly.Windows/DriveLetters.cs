using System.Runtime.Versioning;
using System.Security.Principal;

namespace Mountly.Windows;

/// <summary>Finds and changes the drive letters of mounted images.</summary>
[SupportedOSPlatform("windows")]
public static class DriveLetters
{
    /// <summary>Letters of the volumes on a device such as \\.\CDROM1 or \\.\PHYSICALDRIVE3, e.g. ["E:"].</summary>
    public static IReadOnlyList<string> ForDevice(string devicePath)
    {
        if (Native.DeviceNumber(devicePath) is not { } device) return [];
        var letters = new List<string>();
        var mask = Native.GetLogicalDrives();
        for (var i = 0; i < 26; i++)
        {
            if ((mask & (1u << i)) == 0) continue;
            var letter = (char)('A' + i);
            if (Native.DeviceNumber($@"\\.\{letter}:") is { } volume &&
                volume.DeviceType == device.DeviceType && volume.DeviceNumber == device.DeviceNumber)
                letters.Add(letter + ":");
        }

        return letters;
    }

    /// <summary>Letters that are not in use right now.</summary>
    public static IReadOnlyList<char> Free()
    {
        var mask = Native.GetLogicalDrives();
        return Enumerable.Range(3, 23).Where(i => (mask & (1u << i)) == 0).Select(i => (char)('A' + i)).ToList(); // D: … Z:
    }

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Moves a volume from one drive letter to another. Needs administrator rights; returns false with a
    /// reason when it can't, and leaves the old letter in place.
    /// </summary>
    public static bool TryMove(char from, char to, out string? error)
    {
        from = char.ToUpperInvariant(from);
        to = char.ToUpperInvariant(to);
        error = null;
        if (from == to) return true;
        if (!IsElevated)
        {
            error = "Choosing a drive letter needs administrator rights.";
            return false;
        }

        if ((Native.GetLogicalDrives() & (1u << (to - 'A'))) != 0)
        {
            error = $"{to}: is already in use.";
            return false;
        }

        var volume = Native.VolumeGuidPath($@"{from}:\");
        if (volume is null)
        {
            error = $"{from}: has no volume.";
            return false;
        }

        if (!Native.DeleteVolumeMountPoint($@"{from}:\"))
        {
            error = $"Couldn't release {from}: (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).";
            return false;
        }

        if (Native.SetVolumeMountPoint($@"{to}:\", volume)) return true;
        error = $"Couldn't assign {to}: (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).";
        Native.SetVolumeMountPoint($@"{from}:\", volume); // put the old letter back
        return false;
    }
}
