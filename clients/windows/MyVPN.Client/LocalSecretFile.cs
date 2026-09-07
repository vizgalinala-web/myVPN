namespace MyVPN.Client;

/// <summary>
/// Writes local files that may contain a WireGuard private key. Restricts Unix mode to 600.
/// </summary>
public static class LocalSecretFile
{
    public static readonly UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void WriteAllText(string path, string contents)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        File.WriteAllText(full, contents);
        Restrict(full);
    }

    public static void Restrict(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, OwnerReadWrite);
        }
        catch (Exception)
        {
            // Best-effort on exotic filesystems.
        }
    }
}
