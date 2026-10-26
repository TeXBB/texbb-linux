using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace TeXBB.Desktop;

internal sealed class DesktopSettings
{
    public bool Dark { get; set; } = true;
    public double FontSize { get; set; } = 15;
    public string MathMacros { get; set; } = "{}";

    public static DesktopSettings Load(string workspace)
    {
        string path = Path.Combine(workspace, "history", "linux-settings.json");
        if (!File.Exists(path)) return new();
        try
        {
            var value = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(path)) ?? new();
            value.FontSize = Math.Clamp(value.FontSize, 10, 32);
            return value;
        }
        catch (JsonException) { return new(); }
    }

    public void Save(string workspace)
    {
        using var macros = JsonDocument.Parse(MathMacros);
        if (macros.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Math macros must be a JSON object.");
        string path = Path.Combine(workspace, "history", "linux-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal static class DesktopServices
{
    private static readonly object MathJaxInitialization = new();
    public static void OpenUri(Uri uri)
    {
        if (uri.Scheme is not ("https" or "http" or "mailto" or "file"))
            throw new InvalidOperationException("This link type is not supported.");
        if (OperatingSystem.IsLinux())
        {
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri);
            Process.Start(start)?.Dispose();
        }
        else Process.Start(new ProcessStartInfo(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    public static string EnsureMathJax(string cache)
    {
        lock (MathJaxInitialization)
        {
            cache = Path.GetFullPath(cache);
            Directory.CreateDirectory(cache);
            // Only a completed, app-created cache is reused after an interrupted extraction.
            foreach (string completed in Directory.EnumerateDirectories(cache, "mathjax-2.7.9-*"))
            {
                string script = Path.Combine(completed, "MathJax.js");
                if (File.Exists(Path.Combine(completed, ".complete")) && File.Exists(script)) return script;
            }
            string archive = Path.Combine(AppContext.BaseDirectory, "assets", "mathjax-2.7.9.zip");
            if (!File.Exists(archive)) throw new FileNotFoundException("The bundled MathJax runtime is missing.", archive);
            string key = Guid.NewGuid().ToString("N");
            string staging = Path.Combine(cache, ".codex-mathjax-" + key);
            string destination = Path.Combine(cache, "mathjax-2.7.9-" + key);
            ZipFile.ExtractToDirectory(archive, staging);
            if (!File.Exists(Path.Combine(staging, "MathJax.js"))) throw new InvalidDataException("MathJax archive is incomplete.");
            File.WriteAllText(Path.Combine(staging, ".complete"), "MathJax 2.7.9");
            Directory.Move(staging, destination);
            return Path.Combine(destination, "MathJax.js");
        }
    }
}
