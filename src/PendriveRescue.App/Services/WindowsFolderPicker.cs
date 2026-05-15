using System.Windows.Forms;

namespace PendriveRescue.App.Services;

public class WindowsFolderPicker : IFolderPicker
{
    public string? PickFolder(string title)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
