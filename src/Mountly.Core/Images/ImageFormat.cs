namespace Mountly.Core.Images;

/// <summary>Disc and disk image formats Mountly knows about.</summary>
public enum ImageFormat
{
    Unknown,

    /// <summary>ISO 9660 / UDF image with 2048-byte sectors (.iso).</summary>
    Iso,

    /// <summary>Raw CD image described by a cue sheet (.cue + .bin).</summary>
    CueBin,

    /// <summary>Raw track without a descriptor (.bin, .img): the data layout is detected from its contents.</summary>
    RawBin,

    /// <summary>Nero image (.nrg).</summary>
    Nrg,

    /// <summary>Alcohol 120% image (.mds descriptor + .mdf data, or a lone .mdf).</summary>
    Mds,

    /// <summary>CloneCD image (.ccd descriptor + .img data).</summary>
    CloneCd,

    /// <summary>Virtual hard disk, version 1 (.vhd).</summary>
    Vhd,

    /// <summary>Virtual hard disk, version 2 (.vhdx).</summary>
    Vhdx,
}

public static class ImageFormatExtensions
{
    /// <summary>Optical formats hold a CD/DVD data track; virtual hard disks hold partitions.</summary>
    public static bool IsOptical(this ImageFormat format) => format is not (ImageFormat.Vhd or ImageFormat.Vhdx or ImageFormat.Unknown);

    public static string DisplayName(this ImageFormat format) => format switch
    {
        ImageFormat.Iso => "ISO",
        ImageFormat.CueBin => "BIN/CUE",
        ImageFormat.RawBin => "BIN/IMG",
        ImageFormat.Nrg => "NRG",
        ImageFormat.Mds => "MDS/MDF",
        ImageFormat.CloneCd => "CloneCD",
        ImageFormat.Vhd => "VHD",
        ImageFormat.Vhdx => "VHDX",
        _ => "Unknown",
    };
}
