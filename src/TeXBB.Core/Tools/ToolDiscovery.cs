namespace TeXBB.Core.Tools;

public sealed record ToolDescriptor(string Name, string? ExecutablePath)
{
    public bool IsAvailable => ExecutablePath is not null;
}

public static class ToolDiscovery
{
    private static readonly string[] KnownTools =
        ["pdflatex", "latexmk", "tectonic", "typst", "pandoc", "pdfinfo", "pdftoppm", "pdftotext"];

    /// <summary>Finds an executable on PATH, or validates an explicit path. Never runs a shell.</summary>
    public static string? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar))
            return ExecutablePath(name);

        string[] suffixes = OperatingSystem.IsWindows() && !Path.HasExtension(name)
            ? [".exe", ".com", ""] : [""];
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            // Empty PATH entries must not silently search a document's working directory.
            string directory = entry.Trim().Trim('"');
            if (directory.Length == 0) continue;
            foreach (string suffix in suffixes)
            {
                string? path = ExecutablePath(Path.Combine(directory, name + suffix));
                if (path is not null) return path;
            }
        }
        return null;
    }

    public static IReadOnlyList<ToolDescriptor> Scan() =>
        KnownTools.Select(name => new ToolDescriptor(name, Find(name))).ToArray();

    private static string? ExecutablePath(string candidate)
    {
        try
        {
            string fullPath = Path.GetFullPath(candidate);
            if (!File.Exists(fullPath)) return null;
            if (OperatingSystem.IsWindows())
            {
                string extension = Path.GetExtension(fullPath);
                if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".com", StringComparison.OrdinalIgnoreCase)) return null;
            }
            else
            {
                const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((File.GetUnixFileMode(fullPath) & execute) == 0) return null;
            }
            return fullPath;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
