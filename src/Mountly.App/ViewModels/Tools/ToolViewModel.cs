using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mountly.Core.Util;

namespace Mountly.App.ViewModels.Tools;

/// <summary>A tool on the Tools page: a form, a job with progress and cancel, and its outcome.</summary>
public abstract partial class ToolViewModel(AppServices services) : ObservableObject
{
    private CancellationTokenSource? _cancel;
    private DateTime _lastProgress;

    protected AppServices Services { get; } = services;

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract Geometry Icon { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsRunning { get; private set; }

    public bool IsIdle => !IsRunning;

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; private set; }

    [ObservableProperty]
    public partial string? Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial string? Result { get; protected set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultIsGood), nameof(ResultIsBad))]
    public partial bool? ResultOk { get; protected set; }

    public bool HasResult => !string.IsNullOrEmpty(Result);
    public bool ResultIsGood => ResultOk == true;
    public bool ResultIsBad => ResultOk == false;

    /// <summary>Runs a job off the UI thread: <paramref name="work"/> reports (done, total) bytes and returns its outcome.</summary>
    protected async Task RunAsync(string status, Func<IProgress<(long Done, long Total)>, CancellationToken, Task<ToolOutcome>> work)
    {
        if (IsRunning) return;
        _cancel = new CancellationTokenSource();
        IsRunning = true;
        IsIndeterminate = true;
        Progress = 0;
        Status = status;
        Result = null;
        ResultOk = null;
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            if (DateTime.UtcNow - _lastProgress < TimeSpan.FromMilliseconds(80) && p.Done < p.Total) return;
            _lastProgress = DateTime.UtcNow;
            IsIndeterminate = p.Total <= 0;
            Progress = p.Total > 0 ? 100.0 * p.Done / p.Total : 0;
            Status = p.Total > 0 ? $"{status} {ByteSize.Format(p.Done)} of {ByteSize.Format(p.Total)}" : $"{status} {ByteSize.Format(p.Done)}";
        });

        try
        {
            var outcome = await Task.Run(() => work(progress, _cancel.Token));
            Result = outcome.Text;
            ResultOk = outcome.Ok;
            if (outcome.Ok) await OnSucceededAsync();
        }
        catch (OperationCanceledException)
        {
            Result = "Cancelled.";
            ResultOk = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Result = ex.Message;
            ResultOk = false;
        }
        finally
        {
            IsRunning = false;
            Status = null;
            _cancel.Dispose();
            _cancel = null;
        }
    }

    /// <summary>Runs on the UI thread after a successful job (e.g. to offer mounting the new ISO).</summary>
    protected virtual Task OnSucceededAsync() => Task.CompletedTask;

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    /// <summary>A sibling path with a new extension that doesn't exist yet: "name.iso", "name (2).iso", …</summary>
    protected static string SuggestOutput(string source, string extension)
    {
        var directory = Path.GetDirectoryName(source) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var name = Path.GetFileNameWithoutExtension(source.TrimEnd('\\', '/'));
        var candidate = Path.Combine(directory, name + extension);
        for (var i = 2; File.Exists(candidate); i++) candidate = Path.Combine(directory, $"{name} ({i}){extension}");
        return candidate;
    }
}

/// <summary>What a tool's job produced: a line for the user and whether it went well.</summary>
public sealed record ToolOutcome(string Text, bool Ok = true);
