namespace TeXBB.Core.Tools;

public sealed record DocumentBuildResult(string PdfPath, string ToolName, ToolResult ProcessResult)
{
    public string Stdout => ProcessResult.Stdout;
    public string Stderr => ProcessResult.Stderr;
}

public sealed class ToolExecutionException(string toolName, ToolResult result, string message) : Exception(message)
{
    public string ToolName { get; } = toolName;
    public ToolResult Result { get; } = result;
}

public static class DocumentBuildService
{
    public static async Task<DocumentBuildResult> BuildAsync(string sourcePath, string workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("Document source was not found.", source);
        string extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not ".tex" and not ".typ") throw new NotSupportedException("PDF builds support .tex and .typ documents.");
        string sourceDirectory = Path.GetDirectoryName(source)!;
        string outputDirectory = Path.GetFullPath(workDir);
        if (string.Equals(Path.TrimEndingDirectorySeparator(sourceDirectory), Path.TrimEndingDirectorySeparator(outputDirectory),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Choose a build directory separate from the source directory.", nameof(workDir));
        string[] choices = extension == ".typ" ? ["typst"] : ["pdflatex", "tectonic", "latexmk"];
        ToolDescriptor? tool = choices.Select(name => new ToolDescriptor(name, ToolDiscovery.Find(name)))
            .FirstOrDefault(candidate => candidate.IsAvailable);
        if (tool is null) throw new FileNotFoundException($"No PDF build tool was found. Install {string.Join(", ", choices)}.");
        Directory.CreateDirectory(outputDirectory);
        string staging = Path.Combine(outputDirectory, ".codex-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string name = Path.GetFileNameWithoutExtension(source);
        string generatedPdf = Path.Combine(staging, name + ".pdf");
        try
        {
            string[] arguments = tool.Name switch
            {
                "typst" => ["compile", "--root", sourceDirectory, source, generatedPdf],
                "tectonic" => ["-X", "compile", "--untrusted", "--keep-logs", "--outdir", staging, source],
                "latexmk" => ["-norc", "-pdf", "-interaction=nonstopmode", "-halt-on-error", "-file-line-error",
                    "-pdflatex=pdflatex -no-shell-escape %O %S", "-outdir=" + staging, source],
                _ => ["-no-shell-escape", "-interaction=nonstopmode", "-halt-on-error", "-file-line-error",
                    "-output-directory=" + staging, source]
            };
            ToolResult result = await ToolRunner.RunAsync(tool.ExecutablePath!, arguments, sourceDirectory,
                TimeSpan.FromSeconds(90), cancellationToken).ConfigureAwait(false);
            EnsureSuccess(tool.Name, result, cancellationToken);
            // Resolve labels/cross references for the direct engine without changing external source files.
            if (tool.Name == "pdflatex")
            {
                result = await ToolRunner.RunAsync(tool.ExecutablePath!, arguments, sourceDirectory,
                    TimeSpan.FromSeconds(90), cancellationToken).ConfigureAwait(false);
                EnsureSuccess(tool.Name, result, cancellationToken);
            }
            if (!File.Exists(generatedPdf))
                throw new ToolExecutionException(tool.Name, result, $"{tool.Name} finished without producing a PDF.");
            await ValidatePdfAsync(generatedPdf, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string published = Path.Combine(outputDirectory, name + ".pdf");
            File.Move(generatedPdf, published, overwrite: true);
            return new(published, tool.Name, result);
        }
        finally
        {
            // Only this invocation's fresh staging directory is eligible for cleanup.
            try { Directory.Delete(staging, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static void EnsureSuccess(string name, ToolResult result, CancellationToken cancellationToken)
    {
        if (result.Canceled) throw new OperationCanceledException(cancellationToken);
        if (result.Success) return;
        string reason = result.TimedOut ? "timed out" : $"exited with code {result.ExitCode}";
        throw new ToolExecutionException(name, result, $"{name} {reason}. {result.Stderr}".Trim());
    }

    private static async Task ValidatePdfAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        byte[] signature = new byte[5];
        int count = await stream.ReadAsync(signature, cancellationToken).ConfigureAwait(false);
        if (count != signature.Length || !signature.AsSpan().SequenceEqual("%PDF-"u8))
            throw new InvalidDataException("The build output is not a PDF document.");
    }
}
