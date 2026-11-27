using Mountly.App.Services;
using Mountly.Core.Mounting;
using Mountly.Core.Storage;

namespace Mountly.App.ViewModels;

/// <summary>Everything the view models share.</summary>
public sealed class AppServices(
    MountService mounts,
    SettingsStore settings,
    SlotStore slots,
    CatalogStore catalog,
    IDialogService dialogs,
    UpdateService updates,
    Notices notices)
{
    public MountService Mounts { get; } = mounts;
    public SettingsStore Settings { get; } = settings;
    public SlotStore Slots { get; } = slots;
    public CatalogStore Catalog { get; } = catalog;
    public IDialogService Dialogs { get; } = dialogs;
    public UpdateService Updates { get; } = updates;
    public Notices Notices { get; } = notices;
}
