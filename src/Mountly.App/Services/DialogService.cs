using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Mountly.Core.Images;

namespace Mountly.App.Services;

public interface IDialogService
{
    Task<IReadOnlyList<string>> PickImagesAsync(bool multiple = true);
    Task<string?> PickFileAsync(string title, string? filterName = null, IReadOnlyList<string>? patterns = null);
    Task<string?> PickFolderAsync(string title);
    Task<string?> SaveFileAsync(string title, string suggestedName, string filterName, string extension);
    Task SetClipboardTextAsync(string text);
}

public sealed class DialogService(Window owner) : IDialogService
{
    private static readonly FilePickerFileType Images = new("Disc and disk images") { Patterns = ImageFormats.Patterns.ToList() };

    public async Task<IReadOnlyList<string>> PickImagesAsync(bool multiple = true)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = multiple ? "Choose images" : "Choose an image",
            AllowMultiple = multiple,
            FileTypeFilter = [Images, FilePickerFileTypes.All],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickFileAsync(string title, string? filterName = null, IReadOnlyList<string>? patterns = null)
    {
        var filters = new List<FilePickerFileType>();
        if (filterName is not null && patterns is not null) filters.Add(new FilePickerFileType(filterName) { Patterns = patterns.ToList() });
        filters.Add(FilePickerFileTypes.All);
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters,
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, string filterName, string extension)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(filterName) { Patterns = ["*" + extension] }],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task SetClipboardTextAsync(string text)
    {
        if (owner.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }
}
