using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace TeXBB.Core.Preview;

// Parse TeX delimiters before Markdown consumes their backslashes. Code spans and
// fenced blocks remain owned by Markdig; there is no whole-document regex rewrite.
internal sealed class LatexDelimiterExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline) =>
        pipeline.InlineParsers.InsertBefore<EscapeInlineParser>(new LatexDelimiterParser());

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html) html.ObjectRenderers.Add(new LatexMathRenderer());
    }

    private sealed class LatexMathInline(string content, bool display) : LeafInline
    {
        public string Content { get; } = content;
        public bool Display { get; } = display;
    }

    private sealed class LatexDelimiterParser : InlineParser
    {
        public LatexDelimiterParser() => OpeningCharacters = ['\\'];

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            var start = slice.Start;
            if (start + 1 > slice.End) return false;
            var opening = slice.Text[start + 1];
            if (opening is not ('(' or '[')) return false;
            var closing = opening == '(' ? ')' : ']';
            for (var index = start + 2; index < slice.End; index++)
            {
                if (opening == '(' && slice.Text[index] is '\r' or '\n') return false;
                if (slice.Text[index] != '\\') continue;
                if (slice.Text[index + 1] == '\\') { index++; continue; }
                if (slice.Text[index + 1] != closing) continue;
                var inline = new LatexMathInline(slice.Text.Substring(start + 2, index - start - 2), opening == '[')
                {
                    Span = new SourceSpan(processor.GetSourcePosition(start, out var line, out var column),
                        processor.GetSourcePosition(index + 1)),
                    Line = line,
                    Column = column
                };
                processor.Inline = inline;
                slice.Start = index + 2;
                return true;
            }
            return false;
        }
    }

    private sealed class LatexMathRenderer : HtmlObjectRenderer<LatexMathInline>
    {
        protected override void Write(HtmlRenderer renderer, LatexMathInline math)
        {
            if (!renderer.EnableHtmlForInline) { renderer.WriteEscape(math.Content); return; }
            renderer.Write(math.Display ? "<span class=\"math math-display\">\\[" : "<span class=\"math\">\\(");
            renderer.WriteEscape(math.Content);
            renderer.Write(math.Display ? "\\]</span>" : "\\)</span>");
        }
    }
}
