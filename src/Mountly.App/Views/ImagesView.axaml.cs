using Avalonia.Controls;
using Avalonia.Input;
using Mountly.App.ViewModels;

namespace Mountly.App.Views;

public partial class ImagesView : UserControl
{
    public ImagesView() => InitializeComponent();

    // Double-clicking an image mounts it, like double-clicking it in Explorer.
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (List.SelectedItem is CatalogItemViewModel { Exists: true } item) item.MountCommand.Execute(null);
    }
}
