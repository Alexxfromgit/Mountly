namespace Mountly.Cli;

/// <summary>Positional arguments plus --flags and --options with values. Small enough not to need a library.</summary>
internal sealed class Args
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    private Args(string command, List<string> positional)
    {
        Command = command;
        Positional = positional;
    }

    public string Command { get; }
    public List<string> Positional { get; }

    /// <summary>Options that take a value; everything else starting with "--" is a flag.</summary>
    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--letter", "--algo", "--label", "--boot", "--result-file",
    };

    public static Args Parse(IReadOnlyList<string> argv)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < argv.Count; i++)
        {
            var a = argv[i];
            if (a.StartsWith("--", StringComparison.Ordinal) || (a.Length == 2 && a[0] == '-' && char.IsLetter(a[1])))
            {
                var name = a.Length == 2 ? Expand(a) : a;
                var eq = name.IndexOf('=');
                if (eq > 0) options[name[..eq]] = name[(eq + 1)..];
                else if (ValueOptions.Contains(name))
                {
                    if (i + 1 >= argv.Count) throw new UsageException($"{name} needs a value.");
                    options[name] = argv[++i];
                }
                else options[name] = null;
            }
            else positional.Add(a);
        }

        var command = positional.Count > 0 ? positional[0].ToLowerInvariant() : "help";
        if (positional.Count > 0) positional.RemoveAt(0);
        var args = new Args(command, positional);
        foreach (var (k, v) in options) args._options[k] = v;
        return args;
    }

    private static string Expand(string shortName) => shortName switch
    {
        "-h" => "--help",
        "-r" => "--recursive",
        "-l" => "--letter",
        "-a" => "--all",
        "-j" => "--json",
        _ => shortName,
    };

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Value(string name) => _options.TryGetValue(name, out var v) ? v : null;

    public string Require(int index, string what) =>
        index < Positional.Count ? Positional[index] : throw new UsageException($"Missing {what}.");
}

internal sealed class UsageException(string message) : Exception(message);
