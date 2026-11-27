namespace Mountly.Cli;

/// <summary>A one-line progress display on stderr; silent when stderr is redirected or JSON is requested.</summary>
internal sealed class ConsoleProgress(string label, bool quiet) : IProgress<(long Done, long Total)>, IDisposable
{
    private readonly bool _enabled = !quiet && !Console.IsErrorRedirected;
    private DateTime _last;
    private bool _drawn;

    public void Report((long Done, long Total) value)
    {
        if (!_enabled || DateTime.UtcNow - _last < TimeSpan.FromMilliseconds(100)) return;
        _last = DateTime.UtcNow;
        var text = value.Total > 0
            ? $"{label} {100.0 * value.Done / value.Total,5:0.0}%  {Core.Util.ByteSize.Format(value.Done)} of {Core.Util.ByteSize.Format(value.Total)}"
            : $"{label} {Core.Util.ByteSize.Format(value.Done)}";
        Console.Error.Write("\r" + text.PadRight(Math.Max(text.Length, 60)));
        _drawn = true;
    }

    public IProgress<long> Bytes(long total) => new Progress<long>(done => Report((done, total)));

    public void Dispose()
    {
        if (_drawn) Console.Error.Write("\r" + new string(' ', 72) + "\r");
    }
}
