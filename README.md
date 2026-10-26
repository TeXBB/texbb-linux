# TeXBB for Linux

An independent .NET 10 / Avalonia desktop alpha, targeting Debian 13 and
Ubuntu 24.04 LTS on x86-64 with X11 or XWayland. All Linux source, dependencies, tests, documentation,
and generated artifacts stay inside this directory. Windows and Android sources
outside it are read-only references.

## Available in this alpha

- Open, edit, and save Markdown, LaTeX, Typst, and BibTeX documents in the existing
  `current/` and `reference/` workspace layout. Unsaved buffers survive document
  switches; recovery drafts, history snapshots, and external-change detection
  protect edits.
- Markdown preview with bundled offline MathJax 2.7.9, custom macros, tables,
  local images, and light/dark themes.
- Manual LaTeX/Typst PDF builds using installed compilers; PDF page preview using
  Poppler, build logs, and cancellation.
- Pen drawing with undo, colors, width, and PNG export. Existing stroke-only
  Windows drawing boards can be read; unsupported board features are protected
  from overwriting.
- Workspace selection, editor search, font size, tool discovery, and a clickable
  status path that opens the document's folder.

## Download and install

One Linux x86-64 `.deb` targets both Debian 13 and Ubuntu 24.04 LTS through
[GitHub Releases](https://github.com/TeXBB/texbb-linux/releases). This is an alpha;
Windows and Android versions are independent. The homepage offers a download
button and a one-line installer once the matching public release is available:

```bash
bash -c 'installer=$(curl -fsSL https://texbb.kiyuobjung.com/install.sh) && bash -c "$installer"'
```

The installer verifies the package checksum and identity, then uses APT to
install native dependencies and add TeXBB to your app menu. It does not bundle
LaTeX, Typst or Pandoc. See [release and installation details](docs/releases.md)
for updates, removal, supported systems and the verification procedure.
Debian 13 GUI installation and execution are pending independent verification;
see the [Debian 13 checklist](docs/debian13-verification.md).

## Build and run

With .NET SDK 10.0.403 available, from the repository root on Linux:

```bash
bash scripts/build.sh
bash scripts/test.sh
bash scripts/run.sh
bash scripts/package-deb.sh
```

The scripts put build results, caches, test fixtures, and packages under ignored
`.codex-*` directories. The published app includes .NET, while GTK/WebKit,
Poppler, and document compilers remain external dependencies. Pandoc is optional.
See [Linux setup](docs/linux-setup.md) for native dependencies and package details.

Windows development and cross-compilation:

```powershell
./scripts/build.ps1
./scripts/test.ps1 -IncludeUi
./scripts/build.ps1 -PublishLinux
```

`bootstrap-dotnet.ps1` installs a checksum-verified Windows SDK into
`.codex-tools/dotnet/` when a local development SDK is needed. The PowerShell
scripts select this SDK before the one on `PATH`.

## Verification boundary and remaining work

This is a first functional alpha, not feature parity with the Windows version.
Cross-compilation and headless UI checks do not establish actual Linux GUI,
Korean IME composition, WebKit rendering, clipboard, or package installation.
Those checks need a Linux graphical session. See
[verification](docs/verification.md) for the recorded evidence and remaining
runtime checks.

The next porting stages include the complete References browser/import workflow,
automatic compilation, PDF search/zoom/source synchronization, image/text drawing
objects, complete backup restoration, mobile pairing, and desktop integration.
The current History button creates a snapshot; a history browser is not yet
implemented. Windows and Android versions and release numbers are unchanged.

Implementation notes: [Markdown preview](docs/preview-port.md),
[drawing compatibility](docs/drawing-port.md).
