namespace El3Almey.Services;

/// <summary>
/// Provides hidden temporary workspaces inside the selected media destination.
/// Video and audio downloads therefore use their respective destination folders.
/// </summary>
internal static class TempFileService
{
    public static string CreateWorkspace(string destinationFolder, Guid itemId)
    {
        Directory.CreateDirectory(destinationFolder);
        var root = Path.Combine(destinationFolder, ".temp");
        Directory.CreateDirectory(root);
        TryHide(root);

        var workspace = Path.Combine(root, itemId.ToString("N"));
        Directory.CreateDirectory(workspace);
        TryHide(workspace);
        return workspace;
    }

    public static string GetUniqueDestination(string destinationPath)
    {
        if (!File.Exists(destinationPath)) return destinationPath;

        var directory = Path.GetDirectoryName(destinationPath)!;
        var name = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public static string MoveToDestination(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var uniquePath = GetUniqueDestination(destinationPath);
        try
        {
            File.Move(sourcePath, uniquePath);
            return uniquePath;
        }
        catch (IOException) when (File.Exists(sourcePath))
        {
            // Keep this fallback for destinations that resolve to different volumes.
            File.Copy(sourcePath, uniquePath, false);
            File.Delete(sourcePath);
            return uniquePath;
        }
    }

    public static void TryDeleteWorkspace(string workspace)
    {
        try
        {
            if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);

            var root = Path.GetDirectoryName(workspace);
            if (root != null && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
                Directory.Delete(root);
        }
        catch
        {
            // A locked partial file should not fail a completed download or shutdown.
        }
    }

    static void TryHide(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch
        {
            // Hidden attributes are best-effort; downloads must still work if unsupported.
        }
    }
}
