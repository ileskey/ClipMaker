namespace ClipDownloader.Dialogs;

public interface IOutputFileDialog
{
    string? ShowSaveFileDialog(string suggestedName, string currentPath);
}
