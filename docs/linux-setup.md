# TeXBB Linux alpha setup

This independent Avalonia desktop lives entirely in `linux/`. The initial package
target is an x86-64, glibc-based desktop such as Ubuntu 24.04 LTS. The package
version is `0.1.0~alpha1`; it is separate from the Windows and Android releases.
Linux desktop rendering and package installation still require verification on
an actual Linux system. A successful Windows build or cross-publish does not
establish that GTK/WebKit, clipboard, input methods, or PDF rendering work there.

## Prerequisites

Building requires Bash and .NET SDK **10.0.403**, selected by `global.json` (later
patches in the same SDK feature band are permitted). Install the Linux x64 SDK
using Microsoft's [manual installation guidance](https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual),
or supply an existing SDK executable. A local SDK can be unpacked into
`linux/.codex-tools/dotnet/`. The scripts choose `TEXBB_DOTNET` first, then that
local SDK, then `dotnet` on `PATH`. They do not install an SDK or system packages.
The first build needs network access for NuGet restore.

The published application includes the .NET runtime, but still requires native
libraries. On **Ubuntu 24.04 LTS**, the following is a manual dependency setup
command for the operator to run:

```bash
sudo apt update
sudo apt install \
  ca-certificates tzdata libc6 libgcc-s1 libgssapi-krb5-2 libstdc++6 \
  libicu74 libssl3t64 zlib1g \
  libx11-6 libice6 libsm6 libfontconfig1 \
  libgtk-3-0t64 libwebkit2gtk-4.1-0 libsoup-3.0-0 poppler-utils xdg-utils
```

The .NET dependencies follow [Microsoft's Ubuntu guidance](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-decision#dependencies).
The X11/font packages follow [Avalonia's supported-platform requirements](https://docs.avaloniaui.net/docs/supported-platforms#desktop-linux).
GTK 3, WebKitGTK 4.1 and libsoup 3 provide the web preview backend, per
[Avalonia's Linux WebView prerequisites](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/app-development/embedding-web-content.md#linux).
WPE WebKit is an optional backend on distributions that provide it; this alpha
package declares the baseline WebKitGTK dependencies. WebKitGTK 6.0 is not a
replacement for the GTK 3 backend. Package names differ on other distributions.

`poppler-utils` supplies `pdfinfo`, `pdftoppm`, and `pdftotext`; see the
[Ubuntu package file list](https://packages.ubuntu.com/noble/amd64/poppler-utils/filelist).
LaTeX and Typst compilation additionally require an installed compiler on `PATH`:
`pdflatex` (for example, TeX Live), `tectonic`, or `latexmk` for `.tex`, and `typst`
for `.typ`. Compiler packages and fonts are external dependencies. Pandoc is
optional and is neither bundled nor required for basic editing. `xdg-utils`
provides `xdg-open`, used to open workspace paths and external links.

## Build and test

Run from the repository's `linux/` directory:

```bash
bash scripts/build.sh
bash scripts/test.sh
```

To choose a specific SDK, including a path containing spaces:

```bash
TEXBB_DOTNET="$PWD/.codex-tools/dotnet/dotnet" bash scripts/build.sh
```

The build publishes Release, `linux-x64`, self-contained output to
`.codex-tmp/publish/linux-x64/`. It publishes into a fresh staging directory and
only replaces that destination after success. Earlier published directories
remain under `.codex-tmp/publish/previous.*/`; failed staging directories remain
available for diagnosis. These scripts do not recursively delete artifacts.

The console test runner creates a new fixture directory each time under
`.codex-tmp/test-runs/` and writes `results.log` beside it. It exercises document
formats, preview generation and process execution, including literal arguments,
bounded output, cancellation and timeouts. These core checks do not require a
graphical session and do not prove native web preview or compiler integration.

SDK state, NuGet caches, temporary files, XDG caches, test fixtures, and published
output are all directed into `.codex-tmp/`. `Directory.Build.props` also places
MSBuild intermediates and binaries there. The local SDK stays in `.codex-tools/`.

## Run on Linux

```bash
bash scripts/run.sh
```

This launches the published application, building it first if absent, with a
development workspace at `.codex-tmp/workspace/`. Run `build.sh` again after source
changes. A deliberate workspace override is supported:

```bash
bash scripts/run.sh --workspace "$PWD/.codex-tmp/another-workspace"
```

The application uses Avalonia's X11 backend. In a Wayland session it therefore
needs XWayland and a usable `DISPLAY`; a pure Wayland-only session is not yet a
supported target. Avalonia's native Wayland backend requires an explicit package
and initialization change, as described in its [Linux platform guide](https://docs.avaloniaui.net/docs/platform-specific-guides/linux#wayland).
WebKitGTK also requires the X11 GDK backend. WSL without a graphical Linux session
is insufficient for this UI test.

Use a fresh development workspace to check opening/saving documents, Markdown
with local MathJax, the LaTeX/Typst compile buttons, PDF page navigation, IME input,
clipboard and window scaling. Missing native libraries or external tools should
be distinguished from document compilation errors. TeX shell escape is disabled;
this is not a general sandbox for untrusted documents. Relative input resources
are read from the source directory while build products go to the workspace.

## Create a Debian package

On Linux, after a successful `build.sh`, with `dpkg-deb` available:

```bash
bash scripts/package-deb.sh
```

The script checks the published apphost's ELF format and x86-64 architecture,
requires the self-contained runtime, and creates a fresh package directory under
`.codex-tmp/packages/deb.*/`. It prints the resulting
`texbb_0.1.0~alpha1_amd64.deb` path and package metadata. It does **not** install it.
No root privileges are required to build the package. Installation remains a
separate operator action after review on a disposable Linux test environment.

Windows developers with PowerShell 7.4 or later can also package an existing
Linux cross-publish, without WSL or `dpkg-deb`. From `linux/`:

```powershell
./scripts/package-deb.ps1
# Optional: another Linux publish, still inside linux/.codex-tmp.
./scripts/package-deb.ps1 -PublishDirectory "$PWD/.codex-tmp/publish/linux-x64"
```

This writes a fresh `.codex-tmp/packages/deb-cross-*/` directory. It builds the
Debian `ar` container with `control.tar.gz` and `data.tar.gz`, then independently
rereads it to verify member order, metadata, root ownership, Unix modes, and the
SHA-256 hash of every payload file. `payload-manifest.json` and `verification.txt`
record the evidence. Input/output paths must remain under `.codex-tmp`; symlinks
and non-Linux apphosts are rejected. Metadata timestamps default to Unix epoch
zero or the supplied `SOURCE_DATE_EPOCH`. This is archive validation, with no
`dpkg` installation, native Linux execution, or desktop integration claim.

The package contains the app under `/opt/texbb`, `/usr/bin/texbb` as its launcher,
a desktop entry, and the 256-pixel TeXBB icon. External compilers are not bundled.
The packaged launcher uses the application's regular Documents/TeXBB workspace
unless `--workspace` is supplied. Dependency alternatives support common package
renames, but do not constitute compatibility testing on every Debian derivative.

Before distributing this alpha, record actual Linux launch/rendering evidence,
the distribution and desktop session, core-test results, compiler/PDF smoke
tests, and a clean package install/uninstall check. No such Linux runtime or
installation result is implied by the scripts or Windows-side validation.
