# Markdown preview

`TeXBB.Core.Preview.MarkdownRenderer.Render` produces a complete UTF-8 HTML
document for the Linux web-view host. It has no WinForms, COM, or GDI dependency.
The source path is the document path, not the generated preview path. Relative
images and file links become absolute URIs from the source directory; spaces are
URI-escaped. Heading fragments remain relative to the current preview document.
The HTML deliberately has no `base` element, which would redirect those fragments
to the source directory instead.

The Markdig 1.3.2 pipeline supports CommonMark, pipe tables, task lists, emphasis
extensions, autolinks, heading IDs, and `$...$` / `$$...$$` mathematics. A small
parser also preserves `\(...\)` and `\[...\]` TeX delimiters. Code spans and code
fences stay literal. Block elements retain one-based `data-source-line` metadata.
Windows-specific theorem/comment/source-selection conventions are not yet ported.

Pass an absolute local path to **MathJax 2.7.9 `MathJax.js`**, with its sibling
configuration, extensions, jax and fonts directories intact. The existing Windows
and Android runtime uses this same version. The renderer never downloads assets
or falls back to a CDN. Without a configured runtime, TeX remains visible.
The optional macro argument is a JSON object with string definitions or
`[definition, argumentCount, optionalDefault]` arrays. Invalid settings are ignored.

Raw document HTML is displayed as text. Link schemes are limited to HTTP(S), local
files and mail links. Only local files and raster data URLs load as images;
remote images are blocked by the Content Security Policy. Macro JSON is parsed and escaped;
a Content Security Policy limits the scripts to the local runtime and the
generated configuration. MathJax's Safe extension is enabled. The host must still
intercept user navigation and open ordinary links through its explicit file/URL
workflow, rather than replacing the trusted preview with arbitrary web pages.

`PreviewTests.Run(root)` checks rendering boundaries without a browser or network.
It does not establish WebKit file access, actual MathJax layout, or Linux font
rendering. The runtime gate must exercise local images, local MathJax fonts,
long equations/tables, and navigation inside the chosen Linux web-view host.
