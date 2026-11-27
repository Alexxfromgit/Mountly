namespace Mountly.Core.Images;

/// <summary>Detects an image's format from its extension (and, for ambiguous extensions, its contents).</summary>
public static class ImageFormats
{
    /// <summary>Extensions Mountly can open, for file pickers and shell registration (lower case, with dot).</summary>
    public static IReadOnlyList<string> Extensions { get; } =
        [".iso", ".cue", ".bin", ".img", ".nrg", ".mds", ".mdf", ".ccd", ".vhd", ".vhdx"];

    /// <summary>Extensions people double-click to mount; .bin/.img/.mdf are data files of a descriptor, so they are left out.</summary>
    public static IReadOnlyList<string> ShellExtensions { get; } = [".iso", ".cue", ".nrg", ".mds", ".ccd", ".vhd", ".vhdx"];

    public static ImageFormat Detect(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".iso" => ImageFormat.Iso,
            ".cue" => ImageFormat.CueBin,
            ".nrg" => ImageFormat.Nrg,
            ".mds" or ".mdf" => ImageFormat.Mds,
            ".ccd" => ImageFormat.CloneCd,
            ".vhd" => ImageFormat.Vhd,
            ".vhdx" => ImageFormat.Vhdx,
            ".bin" => HasSibling(path, ".cue") is { } ? ImageFormat.CueBin : ImageFormat.RawBin,
            ".img" => HasSibling(path, ".ccd") is { } ? ImageFormat.CloneCd : ImageFormat.RawBin,
            _ => ImageFormat.Unknown,
        };
    }

    /// <summary>
    /// The file that describes an image: the .cue next to a .bin, the .ccd next to a .img, the .mds next
    /// to a .mdf. Returns the path itself when it is already the descriptor or has none.
    /// </summary>
    public static string DescriptorPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var sibling = ext switch
        {
            ".bin" => HasSibling(path, ".cue"),
            ".img" => HasSibling(path, ".ccd"),
            ".mdf" => HasSibling(path, ".mds"),
            _ => null,
        };
        return sibling ?? path;
    }

    private static string? HasSibling(string path, string extension)
    {
        var candidate = Path.ChangeExtension(path, extension);
        if (File.Exists(candidate)) return candidate;
        var upper = Path.ChangeExtension(path, extension.ToUpperInvariant());
        return File.Exists(upper) ? upper : null;
    }

    /// <summary>A file-dialog filter pattern list such as "*.iso;*.cue;…".</summary>
    public static IReadOnlyList<string> Patterns => Extensions.Select(e => "*" + e).ToList();
}
