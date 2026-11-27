using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Mountly.Tools;

public enum ChecksumAlgorithm
{
    Md5,
    Sha1,
    Sha256,
}

public sealed record ChecksumMatch(ChecksumAlgorithm Algorithm, string Expected, string Actual)
{
    public bool IsMatch => string.Equals(Expected, Actual, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Computes and verifies image checksums in one streaming pass.</summary>
public static partial class Checksums
{
    public static string Name(this ChecksumAlgorithm algorithm) => algorithm switch
    {
        ChecksumAlgorithm.Md5 => "MD5",
        ChecksumAlgorithm.Sha1 => "SHA-1",
        _ => "SHA-256",
    };

    public static ChecksumAlgorithm? Parse(string name) => name.Replace("-", "").ToUpperInvariant() switch
    {
        "MD5" => ChecksumAlgorithm.Md5,
        "SHA1" => ChecksumAlgorithm.Sha1,
        "SHA256" => ChecksumAlgorithm.Sha256,
        _ => null,
    };

    /// <summary>Guesses the algorithm from a hex digest's length.</summary>
    public static ChecksumAlgorithm? FromDigestLength(int hexLength) => hexLength switch
    {
        32 => ChecksumAlgorithm.Md5,
        40 => ChecksumAlgorithm.Sha1,
        64 => ChecksumAlgorithm.Sha256,
        _ => null,
    };

    /// <summary>Hashes <paramref name="path"/> with every requested algorithm while reading it once.</summary>
    public static async Task<IReadOnlyDictionary<ChecksumAlgorithm, string>> ComputeAsync(string path,
        IReadOnlyCollection<ChecksumAlgorithm> algorithms, IProgress<(long Done, long Total)>? progress = null,
        CancellationToken cancel = default)
    {
        var hashes = algorithms.Distinct().ToDictionary(a => a, a => IncrementalHash.CreateHash(a switch
        {
            ChecksumAlgorithm.Md5 => HashAlgorithmName.MD5,
            ChecksumAlgorithm.Sha1 => HashAlgorithmName.SHA1,
            _ => HashAlgorithmName.SHA256,
        }));
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var total = file.Length;
            var buffer = new byte[1 << 20];
            long done = 0;
            int read;
            while ((read = await file.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                foreach (var hash in hashes.Values) hash.AppendData(buffer, 0, read);
                done += read;
                progress?.Report((done, total));
            }

            return hashes.ToDictionary(h => h.Key, h => Convert.ToHexStringLower(h.Value.GetHashAndReset()));
        }
        finally
        {
            foreach (var hash in hashes.Values) hash.Dispose();
        }
    }

    /// <summary>
    /// Reads the expected digest for <paramref name="imagePath"/> from what the user gave: a hex digest, or a
    /// checksum file in GNU ("digest  name") or BSD ("SHA256 (name) = digest") style.
    /// </summary>
    public static (ChecksumAlgorithm Algorithm, string Digest) ResolveExpected(string expected, string imagePath)
    {
        var text = expected.Trim();
        if (HexOnly().IsMatch(text))
            return (FromDigestLength(text.Length) ?? throw new FormatException("A checksum has 32 (MD5), 40 (SHA-1) or 64 (SHA-256) hex digits."),
                text.ToLowerInvariant());

        if (!File.Exists(text)) throw new FormatException("Enter a hex checksum or the path of a checksum file.");
        var name = Path.GetFileName(imagePath);
        var candidates = new List<(string Name, string Digest, ChecksumAlgorithm? Algorithm)>();
        foreach (var line in File.ReadLines(text).Take(100_000))
        {
            if (Bsd().Match(line) is { Success: true } bsd)
                candidates.Add((bsd.Groups["name"].Value, bsd.Groups["digest"].Value, Parse(bsd.Groups["algo"].Value)));
            else if (Gnu().Match(line) is { Success: true } gnu)
                candidates.Add((gnu.Groups["name"].Value.Trim(), gnu.Groups["digest"].Value, null));
        }

        var pick = candidates.FirstOrDefault(c => string.Equals(Path.GetFileName(c.Name), name, StringComparison.OrdinalIgnoreCase));
        if (pick.Digest is null && candidates.Count == 1) pick = candidates[0];
        if (pick.Digest is null) throw new FormatException($"The checksum file has no entry for '{name}'.");
        var algorithm = pick.Algorithm ?? FromDigestLength(pick.Digest.Length) ?? throw new FormatException("Unrecognised checksum length.");
        return (algorithm, pick.Digest.ToLowerInvariant());
    }

    public static async Task<ChecksumMatch> VerifyAsync(string imagePath, string expected, IProgress<(long Done, long Total)>? progress = null,
        CancellationToken cancel = default)
    {
        var (algorithm, digest) = ResolveExpected(expected, imagePath);
        var actual = await ComputeAsync(imagePath, [algorithm], progress, cancel).ConfigureAwait(false);
        return new ChecksumMatch(algorithm, digest, actual[algorithm]);
    }

    [GeneratedRegex("^[0-9a-fA-F]+$")]
    private static partial Regex HexOnly();

    [GeneratedRegex(@"^(?<digest>[0-9a-fA-F]{32,64})\s+\*?(?<name>.+)$")]
    private static partial Regex Gnu();

    [GeneratedRegex(@"^(?<algo>MD5|SHA1|SHA-1|SHA256|SHA-256)\s*\((?<name>.+)\)\s*=\s*(?<digest>[0-9a-fA-F]{32,64})$", RegexOptions.IgnoreCase)]
    private static partial Regex Bsd();
}
