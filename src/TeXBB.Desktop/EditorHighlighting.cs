using System.Collections.Concurrent;
using System.Xml.Linq;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using TeXBB.Core.Documents;

namespace TeXBB.Desktop;

/// <summary>Small, offline syntax definitions for the document formats TeXBB edits.</summary>
internal static class EditorHighlighting
{
    private static readonly XNamespace Syntax = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";
    private static readonly ConcurrentDictionary<(DocumentKind Kind, bool Dark), IHighlightingDefinition> Definitions = new();

    public static IHighlightingDefinition? For(DocumentKind kind, bool dark)
    {
        if (!Enum.IsDefined(kind)) return null;
        return Definitions.GetOrAdd((kind, dark), key => Create(key.Kind, key.Dark));
    }

    private static IHighlightingDefinition Create(DocumentKind kind, bool dark)
    {
        var definition = new XElement(Syntax + "SyntaxDefinition", new XAttribute("name", $"TeXBB {kind} {(dark ? "Dark" : "Light")}"),
            Color("Comment", dark ? "#94AA9E" : "#61766A", italic: true),
            Color("Keyword", dark ? "#7DDAB0" : "#0F6B4B"),
            Color("Heading", dark ? "#91E2BD" : "#0C6547", bold: true),
            Color("String", dark ? "#E3C58B" : "#815719"),
            Color("Math", dark ? "#9FCDF2" : "#285F91"),
            Color("Number", dark ? "#D0B2EE" : "#79509B"),
            Color("Emphasis", dark ? "#DCEDE3" : "#274F3C", italic: true),
            Color("Strong", dark ? "#EEF7F2" : "#183027", bold: true));
        var rules = new XElement(Syntax + "RuleSet");
        switch (kind)
        {
            case DocumentKind.Markdown:
                rules.Add(
                    Span("Comment", "<!--", "-->", true),
                    Span("String", @"^\s{0,3}`{3,}.*$", @"^\s{0,3}`{3,}\s*$", true),
                    Span("String", @"^\s{0,3}~{3,}.*$", @"^\s{0,3}~{3,}\s*$", true),
                    Span("String", "`", "`"),
                    Span("Math", @"(?<!\\)\$\$", @"(?<!\\)\$\$", true),
                    Span("Math", @"(?<!\\)\$(?!\$)", @"(?<!\\)\$(?!\$)"),
                    Span("Math", @"\\\[", @"\\\]", true),
                    Span("Math", @"\\\(", @"\\\)"),
                    Rule("Heading", @"^\s{0,3}\#{1,6}\s+.+$"),
                    Rule("Heading", @"^\s{0,3}(?:={3,}|-{3,})\s*$"),
                    Rule("Strong", @"\*\*[^*\r\n]+\*\*|__[^_\r\n]+__"),
                    Rule("Emphasis", @"(?<!\*)\*[^*\r\n]+\*(?!\*)|(?<!\w)_[^_\r\n]+_(?!\w)"),
                    Rule("Keyword", @"!?\[[^\]\r\n]*\]\([^\)\r\n]+\)"),
                    Rule("Keyword", @"^\s*(?:>|[-+*]|\d+\.)\s+"));
                break;
            case DocumentKind.Latex:
                rules.Add(
                    Rule("Comment", @"(?<!\\)%.*$"),
                    Span("Math", @"(?<!\\)\$\$", @"(?<!\\)\$\$", true),
                    Span("Math", @"(?<!\\)\$(?!\$)", @"(?<!\\)\$(?!\$)"),
                    Span("Math", @"\\\[", @"\\\]", true),
                    Span("Math", @"\\\(", @"\\\)"),
                    Rule("Heading", @"\\(?:part|chapter|section|subsection|subsubsection|paragraph|title)\*?\{[^}]*\}"),
                    Rule("Keyword", @"\\[A-Za-z@]+\*?|\\[^\sA-Za-z]"),
                    Rule("Number", @"[{}\[\]]"));
                break;
            case DocumentKind.Typst:
                rules.Add(
                    Span("Comment", @"/\*", @"\*/", true),
                    Span("Comment", "//", "$"),
                    Span("String", "\"", @"(?<!\\)\"""),
                    Span("String", "`", "`"),
                    Span("Math", @"(?<!\\)\$", @"(?<!\\)\$", true),
                    Rule("Heading", @"^\s*={1,6}\s+.+$"),
                    Rule("Keyword", @"\#[A-Za-z_][A-Za-z0-9_-]*"),
                    Rule("Keyword", @"\b(?:let|set|show|if|else|for|while|in|import|include|as|return|break|continue|true|false|none|auto)\b"),
                    Rule("Number", @"\b\d+(?:\.\d+)?(?:pt|em|cm|mm|in|deg|%)?"),
                    Rule("Strong", @"\*[^*\r\n]+\*"),
                    Rule("Emphasis", @"(?<!\w)_[^_\r\n]+_(?!\w)"));
                break;
            case DocumentKind.BibTeX:
                rules.Add(
                    Rule("Comment", @"(?<!\\)%.*$"),
                    Span("String", "\"", @"(?<!\\)\"""),
                    Rule("Heading", @"@[A-Za-z]+"),
                    Rule("Keyword", @"\b[A-Za-z][A-Za-z0-9_-]*(?=\s*=)"),
                    Rule("Number", @"\b\d{4}\b"),
                    Rule("Math", @"\\[A-Za-z]+"));
                break;
        }
        definition.Add(rules);
        using var reader = definition.CreateReader();
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private static XElement Color(string name, string foreground, bool bold = false, bool italic = false)
    {
        var color = new XElement(Syntax + "Color", new XAttribute("name", name), new XAttribute("foreground", foreground));
        if (bold) color.Add(new XAttribute("fontWeight", "Bold"));
        if (italic) color.Add(new XAttribute("fontStyle", "Italic"));
        return color;
    }

    private static XElement Rule(string color, string pattern) =>
        new(Syntax + "Rule", new XAttribute("color", color), pattern);

    private static XElement Span(string color, string begin, string end, bool multiline = false) =>
        new(Syntax + "Span", new XAttribute("color", color), new XAttribute("begin", begin),
            new XAttribute("end", end), new XAttribute("multiline", multiline));
}
