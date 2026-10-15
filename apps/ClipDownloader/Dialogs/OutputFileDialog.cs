using Microsoft.Win32;

namespace ClipDownloader.Dialogs;

public sealed class OutputFileDialog : IOutputFileDialog
{
    public string? ShowSaveFileDialog(string suggestedName, string currentPath)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "MP4 동영상 (*.mp4)|*.mp4", DefaultExt = ".mp4", AddExtension = true,
            CheckPathExists = true, OverwritePrompt = false, FileName = suggestedName
        };
        try
        {
            if (System.IO.Path.IsPathFullyQualified(currentPath))
            {
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(currentPath);
                dialog.FileName = System.IO.Path.GetFileName(currentPath);
            }
        }
        catch (ArgumentException) { /* Invalid draft paths are handled by validation. */ }
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
}
