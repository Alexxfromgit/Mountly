using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mountly.App.ViewModels;

public enum NoticeKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record NoticeAction(string Label, IRelayCommand Command);

/// <summary>A message at the bottom of the window: what happened, and what the user can do about it.</summary>
public sealed partial class NoticeViewModel : ObservableObject
{
    private readonly Notices _owner;

    public NoticeViewModel(Notices owner, NoticeKind kind, string title, string? message, IReadOnlyList<NoticeAction> actions)
    {
        _owner = owner;
        Kind = kind;
        Title = title;
        Message = message;
        Actions = actions.Select(a => new NoticeAction(a.Label, new RelayCommand(() =>
        {
            Dismiss();
            a.Command.Execute(null);
        }))).ToList();
    }

    public NoticeKind Kind { get; }
    public string Title { get; }
    public string? Message { get; }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public IReadOnlyList<NoticeAction> Actions { get; }
    public bool IsError => Kind == NoticeKind.Error;
    public bool IsWarning => Kind == NoticeKind.Warning;
    public bool IsSuccess => Kind == NoticeKind.Success;

    [RelayCommand]
    private void Dismiss() => _owner.Items.Remove(this);
}

public sealed class Notices
{
    private const int MaxShown = 3;

    public ObservableCollection<NoticeViewModel> Items { get; } = [];

    public void Show(NoticeKind kind, string title, string? message = null, params NoticeAction[] actions) =>
        Dispatcher.UIThread.Post(() =>
        {
            var notice = new NoticeViewModel(this, kind, title, message, actions);
            Items.Add(notice);
            while (Items.Count > MaxShown) Items.RemoveAt(0);
            // Good news goes away by itself; problems stay until dismissed or acted on.
            if (kind is NoticeKind.Success or NoticeKind.Info && actions.Length == 0)
                DispatcherTimer.RunOnce(() => Items.Remove(notice), TimeSpan.FromSeconds(6));
        });

    public void Error(string title, string? message = null, params NoticeAction[] actions) => Show(NoticeKind.Error, title, message, actions);
    public void Success(string title, string? message = null, params NoticeAction[] actions) => Show(NoticeKind.Success, title, message, actions);
    public void Warning(string title, string? message = null, params NoticeAction[] actions) => Show(NoticeKind.Warning, title, message, actions);
}
