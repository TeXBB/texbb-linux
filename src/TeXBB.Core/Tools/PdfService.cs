using System.Globalization;
using System.Text.RegularExpressions;

namespace TeXBB.Core.Tools;

public static class PdfService
{
    public static async Task<int> GetPageCountAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        string pdf = RequirePdfPath(pdfPath);
        string executable = ToolDiscovery.Find("pdfinfo") ?? throw new FileNotFoundException("Install Poppler's pdfinfo to inspect PDF documents.");
        ToolResult result = await ToolRunner.RunAsync(executable, [pdf], Path.GetDirectoryName(pdf)!,
            TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        DocumentBuildService.EnsureSuccess("pdfinfo", result, cancellationToken);
        Match match = Regex.Match(result.Stdout, @"(?m)^Pages:\s*(\d+)\s*$", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int pages) || pages < 1)
            throw new InvalidDataException("pdfinfo did not report a valid page count.");
        return pages;
    }

    /// <summary>Renders a one-based page; scale multiplies a 1024px long-edge target, capped at 4096px.</summary>
    public static async Task<string> RenderPageAsync(string pdfPath, int pageNumber, string outputDirectory,
        double scale = 1.5, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (!double.IsFinite(scale) || scale < 0.25 || scale > 4) throw new ArgumentOutOfRangeException(nameof(scale));
        cancellationToken.ThrowIfCancellationRequested();
        string pdf = RequirePdfPath(pdfPath);
        string executable = ToolDiscovery.Find("pdftoppm") ?? throw new FileNotFoundException("Install Poppler's pdftoppm to render PDF pages.");
        string output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        string prefix = Path.Combine(output, ".codex-page-" + Guid.NewGuid().ToString("N"));
        string temporary = prefix + ".png";
        try
        {
            string page = pageNumber.ToString(CultureInfo.InvariantCulture);
            string pixels = Math.Clamp((int)Math.Round(1024 * scale), 256, 4096).ToString(CultureInfo.InvariantCulture);
            ToolResult result = await ToolRunner.RunAsync(executable,
                ["-f", page, "-l", page, "-singlefile", "-png", "-scale-to", pixels, pdf, prefix],
                Path.GetDirectoryName(pdf)!, TimeSpan.FromSeconds(45), cancellationToken).ConfigureAwait(false);
            DocumentBuildService.EnsureSuccess("pdftoppm", result, cancellationToken);
            if (!File.Exists(temporary) || new FileInfo(temporary).Length is < 8 or > 67_108_864)
                throw new InvalidDataException("Poppler produced no usable PNG, or its output exceeded 64 MiB.");
            await using (var stream = File.OpenRead(temporary))
            {
                byte[] signature = new byte[8];
                await stream.ReadExactlyAsync(signature, cancellationToken).ConfigureAwait(false);
                if (!signature.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new InvalidDataException("Poppler's render output is not a PNG image.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            // The unique filename also prevents concurrent documents/pages from replacing each other's images.
            string published = Path.Combine(output, $"page-{pageNumber}-{Guid.NewGuid():N}.png");
            File.Move(temporary, published);
            return published;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Task<string> RenderPageAsync(string pdfPath, int pageNumber, string outputDirectory,
        CancellationToken cancellationToken) => RenderPageAsync(pdfPath, pageNumber, outputDirectory, 1.5, cancellationToken);

    private static string RequirePdfPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("PDF document was not found.", fullPath);
        return fullPath;
    }
}
