using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Mountly.App.ViewModels;
using Mountly.Core.Images;

namespace Mountly.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private static List<string> ImagePaths(DragEventArgs e) =>
        (e.DataTransfer.TryGetFiles() ?? [])
        .Select(f => f.TryGetLocalPath())
        .OfType<string>()
        .Where(p => ImageFormats.Detect(p) != ImageFormat.Unknown)
        .ToList();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var images = ImagePaths(e);
        e.DragEffects = images.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = images.Count > 0;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        var images = ImagePaths(e);
        if (images.Count == 0 || ViewModel is not { } vm) return;
        vm.Page = MainWindowViewModel.DrivesPage;
        await vm.Drives.MountImagesAsync(images);
    }
}
