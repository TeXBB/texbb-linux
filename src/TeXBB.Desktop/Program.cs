using Avalonia;

namespace TeXBB.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine("TeXBB Linux 0.1.0-alpha1"); return; }
        int index = Array.IndexOf(args, "--workspace");
        if (index >= 0)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("--workspace requires a directory.");
            App.WorkspacePath = Path.GetFullPath(args[index + 1]);
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect().WithInterFont().LogToTrace();
}
