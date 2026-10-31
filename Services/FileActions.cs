using System.Diagnostics;

namespace El3Almey.Services;

public static class FileActions
{
    public static bool OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    public static void ShowInFolder(string path, string fallbackFolder)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(fallbackFolder))
                Process.Start("explorer.exe", $"\"{fallbackFolder}\"");
        }
        catch { }
    }
}
