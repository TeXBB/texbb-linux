using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using TeXBB.Core.Tools;

namespace TeXBB.Core.Tests;

public static class ToolTests
{
    public static bool TryRunFixture(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 2 || args[0] != "--tool-fixture") return false;
        switch (args[1])
        {
            case "echo":
                Console.Write(JsonSerializer.Serialize(new { Directory = Directory.GetCurrentDirectory(), Arguments = args.Skip(2).ToArray() }));
                Console.Error.Write("fixture stderr");
                break;
            case "flood":
                Console.Write(new string('x', ToolRunner.MaximumCapturedCharacters + 16_384));
                Console.Error.Write(new string('y', ToolRunner.MaximumCapturedCharacters + 16_384));
                break;
            case "sleep": Thread.Sleep(TimeSpan.FromSeconds(30)); break;
            case "fail": Console.Error.Write("synthetic failure"); exitCode = 7; break;
            default: exitCode = 2; break;
        }
        return true;
    }

    public static int Run(string root) => RunAsync(root).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string root)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Tool test: " + message);
        }
        string directory = Path.Combine(Path.GetFullPath(root), "tool fixture with spaces");
        Directory.CreateDirectory(directory);
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The test process executable is unavailable.");
        string[] prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [Assembly.GetEntryAssembly()!.Location] : [];
        string[] Arguments(string mode, params string[] values) => [.. prefix, "--tool-fixture", mode, .. values];
        ToolResult echo = await ToolRunner.RunAsync(executable,
            Arguments("echo", "a b", "\"quoted\"", "한글", "$(not-a-command); & harmless", ""), directory, TimeSpan.FromSeconds(10));
        Check(echo.Success && echo.Stderr == "fixture stderr", "stdout and stderr are read concurrently and success is preserved");
        using (JsonDocument json = JsonDocument.Parse(echo.Stdout))
        {
            Check(json.RootElement.GetProperty("Directory").GetString() == directory, "working directory with spaces reaches the child intact");
            string?[] arguments = json.RootElement.GetProperty("Arguments").EnumerateArray().Select(value => value.GetString()).ToArray();
            Check(arguments.SequenceEqual(new[] { "a b", "\"quoted\"", "한글", "$(not-a-command); & harmless", "" }),
                "argument boundaries, Unicode, empty arguments and shell metacharacters remain literal");
        }
        ToolResult failed = await ToolRunner.RunAsync(executable, Arguments("fail"), directory, TimeSpan.FromSeconds(10));
        Check(failed.ExitCode == 7 && !failed.Success && failed.Stderr == "synthetic failure", "nonzero exit code and diagnostics remain available");
        ToolResult flood = await ToolRunner.RunAsync(executable, Arguments("flood"), directory, TimeSpan.FromSeconds(10));
        Check(flood.Success && flood.StdoutTruncated && flood.StderrTruncated, "both full pipes are drained after capture limits");
        Check(flood.Stdout.Length == ToolRunner.MaximumCapturedCharacters && flood.Stderr.Length == ToolRunner.MaximumCapturedCharacters,
            "retained output remains bounded");
        var timer = Stopwatch.StartNew();
        ToolResult timeout = await ToolRunner.RunAsync(executable, Arguments("sleep"), directory, TimeSpan.FromMilliseconds(300));
        Check(timeout.TimedOut && !timeout.Canceled && !timeout.Success && timer.Elapsed < TimeSpan.FromSeconds(8), "timeout terminates the child promptly");
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
        {
            ToolResult canceled = await ToolRunner.RunAsync(executable, Arguments("sleep"), directory, TimeSpan.FromSeconds(20), cancellation.Token);
            Check(canceled.Canceled && !canceled.TimedOut, "caller cancellation is distinguished from timeout");
        }
        ToolResult preCanceled = await ToolRunner.RunAsync("definitely-missing-executable", [], directory,
            TimeSpan.FromSeconds(1), new CancellationToken(canceled: true));
        Check(preCanceled.Canceled && preCanceled.ExitCode == -1, "pre-cancellation does not start or discover a process");

        string fake = Path.Combine(directory, OperatingSystem.IsWindows() ? "texbb-test-tool.exe" : "texbb-test-tool");
        File.WriteAllText(fake, "discovery fixture, never executed");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory);
            Check(ToolDiscovery.Find("texbb-test-tool") == fake, "PATH discovery returns an absolute executable path with spaces");
            Check(ToolDiscovery.Find(fake) == fake, "explicit executable paths are accepted");
            Check(ToolDiscovery.Find("missing-tool") is null, "missing tools are represented as unavailable");
            Check(ToolDiscovery.Scan().Count == 8 && ToolDiscovery.Scan().All(tool => !tool.IsAvailable), "scan includes all eight expected tools");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(fake, UnixFileMode.UserRead);
                Check(ToolDiscovery.Find(fake) is null, "Unix discovery rejects files without execute permission");
            }
        }
        finally { Environment.SetEnvironmentVariable("PATH", oldPath); }
        string source = Path.Combine(directory, "source.tex");
        File.WriteAllText(source, "\\documentclass{article}\\begin{document}Fixture\\end{document}");
        try { await DocumentBuildService.BuildAsync(source, directory); Check(false, "build must reject the source directory as output"); }
        catch (ArgumentException) { Check(true, "build never writes beside the original when source and output directories coincide"); }
        foreach (double scale in new[] { 0d, 5d, double.NaN })
        {
            try { await PdfService.RenderPageAsync("not-needed.pdf", 1, directory, scale); Check(false, "invalid scale must be rejected"); }
            catch (ArgumentOutOfRangeException) { Check(true, "PDF scale is bounded before any process starts"); }
        }
        return checks;
    }
}
