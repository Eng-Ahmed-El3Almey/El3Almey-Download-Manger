namespace El3Almey.Services;

/// <summary>
/// Provides isolated temporary workspaces for media downloads.
/// Temporary files are kept out of the user's selected destination until processing is complete.
/// </summary>
internal static class TempFileService
{
    static readonly string PreferredRoot = Path.Combine(AppContext.BaseDirectory, ".temp");
    static readonly string FallbackRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "El3AlmeyDownloadManager", "Temp");

    public static string CreateWorkspace(Guid itemId)
    {
        var root = GetWritableRoot();
        var workspace = Path.Combine(root, itemId.ToString("N"));
        Directory.CreateDirectory(workspace);
        TryHide(root);
        TryHide(workspace);
        return workspace;
    }

    static string GetWritableRoot()
    {
        try
        {
            Directory.CreateDirectory(PreferredRoot);
            using var probe = new FileStream(Path.Combine(PreferredRoot, ".write-test"), FileMode.Create, FileAccess.Write, FileShare.None);
            probe.Dispose();
            File.Delete(Path.Combine(PreferredRoot, ".write-test"));
            TryHide(PreferredRoot);
            return PreferredRoot;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.CreateDirectory(FallbackRoot);
            TryHide(FallbackRoot);
            return FallbackRoot;
        }
        catch (IOException)
        {
            Directory.CreateDirectory(FallbackRoot);
            TryHide(FallbackRoot);
            return FallbackRoot;
        }
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

    public static void MoveToDestination(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var uniquePath = GetUniqueDestination(destinationPath);
        try
        {
            File.Move(sourcePath, uniquePath);
        }
        catch (IOException) when (File.Exists(sourcePath))
        {
            // The temp directory and download destination can be on different volumes.
            File.Copy(sourcePath, uniquePath, false);
            File.Delete(sourcePath);
        }
    }

    public static void TryDeleteWorkspace(string workspace)
    {
        try
        {
            if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
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
