using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TeXBB.Core.Tools;

public sealed record ToolResult(int ExitCode, string Stdout, string Stderr, bool TimedOut, bool Canceled)
{
    public bool StdoutTruncated { get; init; }
    public bool StderrTruncated { get; init; }
    public bool Success => ExitCode == 0 && !TimedOut && !Canceled;
}

public static class ToolRunner
{
    public const int MaximumCapturedCharacters = 262_144;

    public static async Task<ToolResult> RunAsync(string exe, IEnumerable<string> args,
        string workingDir, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDir);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(24))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (cancellationToken.IsCancellationRequested) return new(-1, "", "", false, true);
        string executable = ToolDiscovery.Find(exe) ?? throw new FileNotFoundException($"Executable was not found: {exe}", exe);
        string directory = Path.GetFullPath(workingDir);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in args)
        {
            ArgumentNullException.ThrowIfNull(argument);
            start.ArgumentList.Add(argument);
        }
        // Poppler's metadata keys are parsed below; keep tool diagnostics deterministic.
        start.Environment["LC_ALL"] = "C";
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new IOException($"Could not start {executable}.");
        }
        catch (Win32Exception error) { throw new IOException($"Could not start {executable}: {error.Message}", error); }
        process.StandardInput.Close();
        using var pipeCancellation = new CancellationTokenSource();
        Task<CapturedText> stdout = ReadBoundedAsync(process.StandardOutput, pipeCancellation.Token);
        Task<CapturedText> stderr = ReadBoundedAsync(process.StandardError, pipeCancellation.Token);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        bool canceled = false, timedOut = false;
        try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            canceled = cancellationToken.IsCancellationRequested;
            timedOut = !canceled && timeoutCancellation.IsCancellationRequested;
            KillTree(process);
            using var exitGrace = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await process.WaitForExitAsync(exitGrace.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { KillTree(process); }
        }
        // A descendant that inherited a pipe must not keep the caller waiting indefinitely.
        pipeCancellation.CancelAfter(TimeSpan.FromSeconds(2));
        CapturedText[] output = await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return new(process.HasExited ? process.ExitCode : -1, output[0].Text, output[1].Text, timedOut, canceled)
        {
            StdoutTruncated = output[0].Truncated,
            StderrTruncated = output[1].Truncated
        };
    }

    private static void KillTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception)
        {
            // Preserve cancellation reporting if a process exits during tree enumeration.
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
        }
    }

    private sealed record CapturedText(string Text, bool Truncated);

    private static async Task<CapturedText> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        char[] buffer = new char[4096];
        bool truncated = false;
        try
        {
            while (true)
            {
                int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                int retained = Math.Min(count, MaximumCapturedCharacters - text.Length);
                if (retained > 0) text.Append(buffer, 0, retained);
                truncated |= retained < count;
            }
        }
        catch (OperationCanceledException) { truncated = true; }
        return new(text.ToString(), truncated);
    }
}
