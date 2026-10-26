using System.Net;
using System.Text.Json;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace TeXBB.Core.Preview;

/// <summary>Produces an offline-capable preview document without desktop UI dependencies.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();

    public static string Render(string markdown, string sourcePath, bool dark,
        string? mathJaxScriptPath = null, string? macrosJson = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var baseUri = new Uri(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar);
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            link.Url = SafeUrl(link.Url, link.IsImage, baseUri);
            link.GetDynamicUrl = null;
        }
        foreach (var link in document.Descendants<AutolinkInline>())
        {
            var safe = SafeUrl(link.IsEmail ? "mailto:" + link.Url : link.Url, false, baseUri);
            if (link.IsEmail && safe.StartsWith("mailto:", StringComparison.Ordinal))
                link.Url = safe[7..];
            else
            {
                link.IsEmail = false;
                link.Url = safe;
            }
        }
        foreach (var block in document.Descendants<Block>())
            block.GetAttributes().AddProperty("data-source-line", (block.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

        var body = document.ToHtml(Pipeline);
        var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
        var mathScripts = BuildMathScripts(mathJaxScriptPath, macrosJson, nonce);
        var title = WebUtility.HtmlEncode(Path.GetFileName(sourcePath));
        return $$"""
            <!DOCTYPE html>
            <html lang="en" data-theme="{{(dark ? "dark" : "light")}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'nonce-{{nonce}}' file: 'unsafe-eval'; style-src 'unsafe-inline' file:; img-src file: data:; font-src file: data:; connect-src file:; base-uri file:; object-src 'none'; form-action 'none'">
            <title>{{title}}</title>
            <style>{{Style}}</style>
            {{mathScripts}}
            </head>
            <body><main id="texbb-preview">{{body}}</main></body>
            </html>
            """;
    }

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables().UseEmphasisExtras().UseAutoLinks().UseTaskLists()
            .UseAutoIdentifiers().UseMathematics().DisableHtml();
        builder.Extensions.Add(new LatexDelimiterExtension());
        return builder.Build();
    }

    private static string SafeUrl(string? value, bool image, Uri baseUri)
    {
        var url = WebUtility.HtmlDecode(value ?? string.Empty).Trim();
        if (url.Any(char.IsControl)) return "#";
        if (url.StartsWith('#')) return url;
        if (!Uri.TryCreate(baseUri, url, out var uri)) return "#";
        if (uri.Scheme is "http" or "https" or "file") return uri.AbsoluteUri;
        if (!image && uri.Scheme == "mailto") return uri.AbsoluteUri;
        // SVG/HTML data URLs are deliberately excluded from document content.
        if (image && (url.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:image/gif;base64,", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:image/webp;base64,", StringComparison.OrdinalIgnoreCase))) return url;
        return "#";
    }

    private static string BuildMathScripts(string? scriptPath, string? macrosJson, string nonce)
    {
        if (string.IsNullOrWhiteSpace(scriptPath)) return string.Empty;
        if (!Path.IsPathFullyQualified(scriptPath))
            throw new ArgumentException("MathJax must be an absolute local MathJax.js path.", nameof(scriptPath));
        var scriptUri = new Uri(Path.GetFullPath(scriptPath));
        if (!scriptUri.IsFile)
            throw new ArgumentException("MathJax must be a local file.", nameof(scriptPath));
        var source = WebUtility.HtmlEncode(scriptUri.AbsoluteUri + "?config=TeX-AMS_HTML,Safe");
        return $$"""
            <script nonce="{{nonce}}">
            window.MathJax = {
              messageStyle: "none",
              showMathMenu: false,
              tex2jax: {
                inlineMath: [["\\(", "\\)"]], displayMath: [["\\[", "\\]"]],
                processEscapes: true, processEnvironments: false,
                skipTags: ["script", "noscript", "style", "textarea", "pre", "code"]
              },
              TeX: { Macros: {{NormalizeMacros(macrosJson)}} },
              "HTML-CSS": { availableFonts: [], preferredFont: "TeX", webFont: "TeX" },
              Safe: { allow: { URLs: "safe", classes: "none", cssIDs: "none", styles: "none" } }
            };
            </script>
            <script nonce="{{nonce}}" type="text/x-mathjax-config">
            MathJax.OutputJax.fontDir = MathJax.Hub.config.root + "/fonts";
            if (MathJax.OutputJax["HTML-CSS"])
              MathJax.OutputJax["HTML-CSS"].webfontDir = MathJax.OutputJax.fontDir + "/HTML-CSS";
            </script>
            <script nonce="{{nonce}}" src="{{source}}"></script>
            """;
    }

    private static string NormalizeMacros(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) return "{}";
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "{}";
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Length == 0 || !property.Name.All(char.IsAsciiLetter) ||
                    property.Name is "constructor" or "prototype") return "{}";
                var value = property.Value;
                if (value.ValueKind == JsonValueKind.String) continue;
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 2 or > 3 ||
                    value[0].ValueKind != JsonValueKind.String || !value[1].TryGetInt32(out var arguments) ||
                    arguments is < 0 or > 9 ||
                    (value.GetArrayLength() == 3 && value[2].ValueKind != JsonValueKind.String)) return "{}";
            }
            // The default JSON encoder escapes '<', so a macro cannot close this script element.
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException) { return "{}"; }
        catch (InvalidOperationException) { return "{}"; }
    }

    private const string Style = """
        :root { color-scheme: light; --bg:#f6faf7; --fg:#17372b; --surface:#e5f0e9; --border:#bad1c3; --accent:#13744e; --muted:#526e60; }
        :root[data-theme=dark] { color-scheme:dark; --bg:#0a1612; --fg:#e6f3eb; --surface:#173d30; --border:#2b5946; --accent:#5cdaa4; --muted:#a4c3b3; }
        * { box-sizing:border-box; }
        html,body { margin:0; background:var(--bg); color:var(--fg); }
        body { font-family:system-ui,"Noto Sans",sans-serif; font-size:16px; line-height:1.65; overflow-wrap:anywhere; }
        main { max-width:1040px; margin:0 auto; padding:28px 32px 64px; }
        h1,h2,h3,h4 { line-height:1.3; margin-top:1.5em; } h1 { border-bottom:1px solid var(--border); padding-bottom:.35em; }
        a { color:var(--accent); text-underline-offset:.15em; } img { max-width:100%; height:auto; }
        code,pre { font-family:"DejaVu Sans Mono",Consolas,monospace; background:var(--surface); border-radius:5px; }
        code { padding:.12em .3em; } pre { padding:14px 16px; overflow:auto; white-space:pre; overflow-wrap:normal; }
        pre code { padding:0; background:transparent; border-radius:0; }
        blockquote { border-left:3px solid var(--accent); margin-left:0; padding:.1em 1.1em; color:var(--muted); }
        table { display:block; max-width:100%; width:max-content; overflow:auto; border-collapse:collapse; }
        th,td { border:1px solid var(--border); padding:.4em .75em; } th { background:var(--surface); }
        hr { border:0; border-top:1px solid var(--border); margin:1.5em 0; }
        .math, .MathJax_Display { max-width:100%; } div.math, .math-display, .MathJax_Display { overflow-x:auto; overflow-y:hidden; }
        .math-display { display:block; margin:1em 0; text-align:center; }
        ::selection { background:#397958; color:#fff; }
        * { scrollbar-width:thin; scrollbar-color:var(--border) var(--bg); }
        @media (max-width:600px) { main { padding:18px 16px 40px; } }
        """;
}
