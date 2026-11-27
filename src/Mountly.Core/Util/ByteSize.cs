using System.Globalization;

namespace Mountly.Core.Util;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>Binary units with one decimal, as Explorer shows them: 4.7 GB.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.CurrentCulture) + " B";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}
