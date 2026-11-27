using Avalonia.Controls;
using Avalonia.Input;
using Mountly.App.ViewModels.Tools;

namespace Mountly.App.Views;

public partial class ToolsView : UserControl
{
    public ToolsView() => InitializeComponent();

    // In the image browser, double-clicking a folder opens it.
    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: EntryViewModel entry, DataContext: BrowseTool browse })
            browse.OpenEntryCommand.Execute(entry);
    }
}
