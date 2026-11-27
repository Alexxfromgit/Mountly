using CommunityToolkit.Mvvm.ComponentModel;
using Mountly.App.ViewModels.Tools;

namespace Mountly.App.ViewModels;

/// <summary>The Tools page: a list of tools on the left, the selected one on the right.</summary>
public sealed partial class ToolsViewModel : ObservableObject
{
    public ToolsViewModel(AppServices services, DrivesViewModel drives)
    {
        CreateIso = new CreateIsoTool(services, drives);
        Convert = new ConvertTool(services, drives);
        Browse = new BrowseTool(services);
        Verify = new VerifyTool(services);
        Rip = new RipTool(services, drives);
        All = [CreateIso, Convert, Browse, Verify, Rip];
        Selected = CreateIso;
    }

    /// <summary>Asks the window to show the Tools page (when a tool is opened from elsewhere).</summary>
    public event EventHandler? ShowRequested;

    public CreateIsoTool CreateIso { get; }
    public ConvertTool Convert { get; }
    public BrowseTool Browse { get; }
    public VerifyTool Verify { get; }
    public RipTool Rip { get; }

    public IReadOnlyList<ToolViewModel> All { get; }

    [ObservableProperty]
    public partial ToolViewModel Selected { get; set; }

    partial void OnSelectedChanged(ToolViewModel value)
    {
        if (value is RipTool rip && rip.Drives.Count == 0) rip.RefreshDrives();
    }

    public void OpenCreateIso(string folder) => Show(CreateIso, () => CreateIso.SourceFolder = folder);
    public void OpenConvert(string image) => Show(Convert, () => Convert.SourcePath = image);
    public void OpenVerify(string file) => Show(Verify, () => Verify.FilePath = file);
    public void OpenBrowse(string image) => Show(Browse, () => Browse.ImagePath = image);

    private void Show(ToolViewModel tool, Action fill)
    {
        if (!tool.IsRunning) fill();
        Selected = tool;
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }
}
