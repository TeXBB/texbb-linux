using System.Net;
using System.Text.RegularExpressions;
using TeXBB.Core.Preview;

namespace TeXBB.Core.Tests;

public static class PreviewTests
{
    public static int Run(string root)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Markdown preview: " + message);
        }

        var source = Path.Combine(root, "project with spaces", "notes & examples.md");
        var runtime = Path.Combine(root, "math runtime", "MathJax.js");
        var markup = "# Notes\n\nA **result** and $x_1 < x_2$.\n\n$$\n\\sum_{i=1}^n i\n$$\n\n" +
            @"Also \(a_* + b\) and \[c^2 < d\]." + "\n\n`$not_math$ \\(code\\)`\n\n```tex\n$x$\n\\[y\\]\n```\n\n" +
            "| A | B |\n|---|---|\n| 1 | 2 |\n\n![plot](<images/my plot.png>)\n\n[site](https://example.com/a?q=1&x=2)\n\n[Jump to notes](#notes)";
        var html = MarkdownRenderer.Render(markup, source, true, runtime, "{\"RR\":\"\\\\mathbb{R}\",\"norm\":[\"|#1|\",1]}");
        Check(html.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal), "complete HTML document");
        Check(html.Contains("<strong>result</strong>", StringComparison.Ordinal) && html.Contains("<table", StringComparison.Ordinal), "formatted Markdown and tables");
        Check(html.Contains(@"\(x_1 &lt; x_2\)", StringComparison.Ordinal), "dollar math preserved and HTML escaped");
        Check(html.Contains(@"\sum_{i=1}^n i", StringComparison.Ordinal), "display math TeX preserved");
        Check(html.Contains(@"\(a_* + b\)", StringComparison.Ordinal) && html.Contains(@"\[c^2 &lt; d\]", StringComparison.Ordinal), "backslash delimiters preserve TeX punctuation");
        var inlineCode = Regex.Match(html, @"<code\b[^>]*>(\$not_math\$.*?)</code>", RegexOptions.Singleline);
        var fencedCode = Regex.Match(html, @"<pre\b[^>]*><code\b[^>]*>(.*?)</code></pre>", RegexOptions.Singleline);
        Check(inlineCode.Success && fencedCode.Success &&
            fencedCode.Groups[1].Value.Contains("$x$", StringComparison.Ordinal) &&
            fencedCode.Groups[1].Value.Contains(@"\[y\]", StringComparison.Ordinal) &&
            !fencedCode.Groups[1].Value.Contains("<span", StringComparison.Ordinal), "code spans and fences are not parsed as math");
        var expectedImage = new Uri(Path.Combine(root, "project with spaces", "images", "my plot.png")).AbsoluteUri;
        Check(html.Contains(WebUtility.HtmlEncode(expectedImage), StringComparison.Ordinal), "relative image is rooted at source document with escaped spaces");
        Check(!html.Contains("<base", StringComparison.Ordinal) && html.Contains("href=\"#notes\"", StringComparison.Ordinal) &&
            html.Contains("id=\"notes\"", StringComparison.Ordinal), "heading fragments stay in the current preview document");
        Check(html.Contains("math%20runtime/MathJax.js?config=TeX-AMS_HTML,Safe", StringComparison.Ordinal), "MathJax loads from configured local runtime");
        Check(html.Contains("data-theme=\"dark\"", StringComparison.Ordinal) && html.Contains("data-source-line=", StringComparison.Ordinal), "theme and source locations are present");
        Check(html.Contains("https://example.com/a?q=1&amp;x=2", StringComparison.Ordinal), "ordinary external links remain links");

        var unsafeHtml = MarkdownRenderer.Render("<script>alert(1)</script>\n\n<img src=x onerror=alert(2)>\n\n[bad](javascript:alert%281%29)\n\n<javascript:alert%282%29>", source, false);
        Check(!unsafeHtml.Contains("<script>", StringComparison.Ordinal) && !unsafeHtml.Contains("<img src=x", StringComparison.Ordinal), "raw HTML cannot create executable elements");
        Check(!unsafeHtml.Contains("href=\"javascript:", StringComparison.OrdinalIgnoreCase), "active URL schemes are removed for normal and angle links");
        Check(unsafeHtml.Contains("&lt;script&gt;", StringComparison.Ordinal), "raw HTML remains visible as text");
        Check(!unsafeHtml.Contains("<script ", StringComparison.Ordinal) && unsafeHtml.Contains("data-theme=\"light\"", StringComparison.Ordinal), "missing runtime adds no network fallback");

        var macroHtml = MarkdownRenderer.Render("$\\RR$", source, true, runtime, "{\"RR\":\"</script><script>oops</script>\"}");
        Check(!macroHtml.Contains("</script><script>oops", StringComparison.Ordinal) && macroHtml.Contains("\\u003C", StringComparison.Ordinal), "macro JSON cannot terminate the configuration script");
        Check(MarkdownRenderer.Render("$x$", source, true, runtime, "not json").Contains("Macros: {}", StringComparison.Ordinal), "malformed macro settings fall back safely");
        Check(MarkdownRenderer.Render("$x$", source, true, runtime, "{\"bad\":[\"x\",\"one\"]}").Contains("Macros: {}", StringComparison.Ordinal), "invalid macro argument count falls back safely");
        var browserRuntime = Environment.GetEnvironmentVariable("TEXBB_PREVIEW_MATHJAX_SCRIPT");
        if (!string.IsNullOrWhiteSpace(browserRuntime)) WriteBrowserFixture(root, source, markup, browserRuntime);
        return assertions;
    }

    private static void WriteBrowserFixture(string root, string source, string markup, string runtime)
    {
        if (!File.Exists(runtime)) throw new FileNotFoundException("Browser fixture needs a local MathJax runtime.", runtime);
        var images = Path.Combine(Path.GetDirectoryName(source)!, "images");
        Directory.CreateDirectory(images);
        File.WriteAllBytes(Path.Combine(images, "my plot.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1UAAAAASUVORK5CYII="));
        var sample = markup + "\n\nCustom macro: $\\RR$.";
        File.WriteAllText(source, sample);
        var html = MarkdownRenderer.Render(sample, source, true, runtime, "{\"RR\":\"\\\\mathbb{R}\"}");
        var nonce = Regex.Match(html, "nonce=\"([^\"]+)\"").Groups[1].Value;
        var probe = $$"""
            <script nonce="{{nonce}}">
            window.addEventListener("load", function () {
              function report(error) {
                var previewUrl = location.href.split("#")[0];
                var headingLink = document.querySelector("main a[href='#notes']");
                if (headingLink) headingLink.click();
                var image = document.querySelector("main img");
                var code = document.querySelector("pre code");
                var resources = performance.getEntriesByType("resource");
                var loadedFonts = 0;
                document.fonts.forEach(function (font) { if (font.status === "loaded" && /^MathJax_/.test(font.family)) loadedFonts++; });
                var fontRoot = document.querySelector("script[src*='MathJax.js']").src.split("/MathJax.js")[0] + "/fonts/";
                var localFontFaces = 0;
                Array.from(document.styleSheets).forEach(function (sheet) {
                  Array.from(sheet.cssRules).forEach(function (rule) {
                    if (rule.type === CSSRule.FONT_FACE_RULE && rule.style.src.indexOf(fontRoot) >= 0) localFontFaces++;
                  });
                });
                var result = {
                  error: error || "",
                  typesetCount: window.MathJax && MathJax.Hub ? MathJax.Hub.getAllJax().length : 0,
                  renderedFrames: document.querySelectorAll(".MathJax[id$='-Frame']").length,
                  processingFrames: document.querySelectorAll(".MathJax_Processing").length,
                  mathErrors: document.querySelectorAll(".MathJax_Error, .merror, .mjx-merror").length,
                  imageLoaded: !!(image && image.complete && image.naturalWidth > 0),
                  imageSource: image ? image.currentSrc : "",
                  fragmentSameDocument: !!(headingLink && document.getElementById("notes") && location.href === previewUrl + "#notes"),
                  fragmentUrl: location.href,
                  codePreserved: !!(code && code.textContent.indexOf("$x$") >= 0 && code.textContent.indexOf("\\[y\\]") >= 0 && !code.querySelector(".MathJax")),
                  remoteResources: resources.filter(function (item) { return /^https?:/i.test(item.name); }).map(function (item) { return item.name; }),
                  localFontFaces: localFontFaces,
                  loadedMathFonts: loadedFonts
                };
                var output = document.createElement("pre");
                output.id = "texbb-browser-result";
                output.textContent = JSON.stringify(result);
                document.body.appendChild(output);
              }
              if (!window.MathJax || !MathJax.Hub) { report("MathJax did not load"); return; }
              MathJax.Hub.Queue(["Typeset", MathJax.Hub], function () { report(""); });
            });
            </script>
            """;
        File.WriteAllText(Path.Combine(root, "browser-preview.html"), html.Replace("</body>", probe + "</body>", StringComparison.Ordinal));
    }
}
