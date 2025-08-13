public interface IFolderDialogService
{
    string? BrowseForFolder(string description);
}

public class FolderDialogService : IFolderDialogService
{
    public string? BrowseForFolder(string description)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };
        var result = dlg.ShowDialog();
        return result == DialogResult.OK ? dlg.SelectedPath : null;
    }
}
