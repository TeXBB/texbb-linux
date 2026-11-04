# Linux alpha verification

## Ubuntu 24.04 Linux runtime — 2026-10-27

The existing tracked `release/packages/v0.1.0-alpha.1/texbb_0.1.0~alpha1_amd64.deb`
was installed and exercised without changing application source or binaries.
Its SHA-256 was verified as
`5fcc8cfb558463622c0cd9d4def4f09025b161dbd7daa81aa33014195ccf6d58`.

The dedicated `TeXBB-Ubuntu2404` distribution runs Ubuntu 24.04.5 LTS x86-64
under WSL 2.7.3 / WSLg 1.0.73. WSLg supplied the first native application and
WebKit windows. Interactive checks then used a separate Xvfb display `:77` with
Openbox and XTest mouse/keyboard input to the actual installed application.
This is Linux runtime coverage, not a full GNOME/Wayland desktop VM test.

Evidence is under `.codex-tmp/linux-runtime-20261027/`; `results.json` records the
passed checks and remaining gaps:

| Check | Result and evidence |
| --- | --- |
| Package installation | APT installed the unchanged package and native dependencies. `dpkg-query` reported `texbb 0.1.0~alpha1 install ok installed`; see `installation.log`. |
| WSLg launch and rendering | The native application and WebKit processes created X11 windows. Direct X11 captures showed Markdown inline/display equations and a relative local image; see `environment-and-launch.log`, `gui-session.log`, and `markdown-rendered.png`. |
| Korean display | Installed `fonts-noto-cjk` in the test distribution to supply missing Korean glyphs. After application restart, Korean text displayed in the editor and preview. This does not test IME composition. |
| Edit, save and reopen | In the Xvfb/Openbox session, XTest input typed `GUI_SAVE_CHECK_20261027`; Ctrl+S wrote it to the Markdown file. Selecting `second.md` and returning to the first document retained the marker; see `x11-saved.png` and `x11-reopened.png`. |
| Native PDF picker and navigation | The native file picker opened `two-pages.pdf`. The first page showed a red rectangle and `1 / 2`; clicking the next-page button showed a green rectangle and `2 / 2`. See `pdf-open-dialog.png`, `pdf-page-1.png`, and `pdf-page-2.png`. |

Windows-side screen capture returned the wrong background for the WSLg window,
and WSLg focus automation was unreliable. Therefore WSLg end-to-end user
interaction is not marked passed; the save/reopen and PDF interaction results
above belong specifically to the separate Xvfb/Openbox session. Handled app
errors can appear only in the status area and Build log, so an empty process log
alone is not a pass. `gui-session.log` also retains portal/PipeWire, DRI3, and GTK
warnings from the WSLg attempts; these were not silently treated as resolved.

WSL resource limits in the Windows user's `.wslconfig` were reduced to
`processors=4`, `memory=4GB`, and `swap=2GB`. The guest reported four processors,
3.8 GiB memory and 2.0 GiB swap. A GUI-running observation reported 726 MiB used
inside the guest; this is a transient guest measurement, not application RSS or
a guarantee about total Windows host memory usage. After testing, `wsl --shutdown`
was run and `wsl --list --verbose` confirmed `TeXBB-Ubuntu2404` was `Stopped`;
the final Windows process query found no remaining `VmmemWSL` process.

Not verified in this run: Debian 13, a full Linux desktop VM, physical Korean
IME or pen/touch input, desktop-menu launch, Windows/Linux clipboard integration,
TeX/Typst compilation, uninstall/reinstall, or the public download/one-line
installation path. The portable/core and headless suites below remain their
historical Windows results; they were not rerun on Linux in this session.

## Windows-side verification — 2026-10-26

This records the first `0.1.0-alpha1` Linux implementation. Verification ran on
Windows 10.0.26200 with the locally scoped .NET SDK 10.0.403. At that time there
was no installed WSL distribution on this host. This historical section does not
claim native Linux execution or package installation.

### Completed

| Check | Result and evidence under `linux/` |
| --- | --- |
| Portable core | 71 checks passed: document formats, recovery, conflict protection, Markdown, process arguments, bounded pipes, cancellation, and timeouts. Final suite: `.codex-tmp/test-runs/ea50e88eba474694ab95f225b2287d94/`. Linux-only case sensitivity, symlink, and permission checks remain for Linux. |
| Headless desktop | 53 checks passed with real Skia captures: Korean/Unicode text events and saving, dirty bibliography retention, non-first-document recovery, stale PDF navigation/cancellation, build-preview debounce, light/dark layouts at 860/1280 px, pointer drawing and save. Results and six screenshots: `.codex-tmp/test-runs/ea50e88eba474694ab95f225b2287d94/ui/`. Text events do not test IME composition. |
| Browser Markdown | Actual Windows Chrome, isolated profile and blocked network: 5 equations rendered, 5 MathJax fonts loaded, 0 math errors/unfinished frames, relative image loaded, heading fragment remained in the current preview, code preserved, no remote document resource requests. `.codex-tmp/markdown-browser-770263cddea841a6a6c212786061b058/browser-result.json`. This is not Linux WebKit coverage. |
| PDF integration | Actual Windows Poppler through `PdfService`: 2-page PDF with spaces/Korean filename, correct page count, distinct 1024×768 rendered pages. `.codex-tmp/pdf-native-smoke-20261026/results.txt`. No actual LaTeX/Typst compilation was run in this check. |
| Bash scripts | Syntax and fake-SDK harness checked quoted paths, isolated caches, fresh test fixtures, publish success/failure preservation, and guards. `.codex-tmp/script-probe.446Rtr/`. Shell files have LF line endings. |
| Linux cross-publish | Self-contained Release `linux-x64` generated successfully, ELF64 x86-64 apphost plus .NET runtime. Final build: `.codex-tmp/publish/linux-x64-20261026-134253/`. No compiler warnings or errors in final publish. |
| Debian archive | Windows cross-packager verified ar member order, control metadata, safe paths, root ownership, Unix file/directory permissions, and all 232 payload hashes. Windows `tar` independently read its member list. No `dpkg` install/uninstall check. |

An earlier sandboxed restore emitted NU1900 because NuGet vulnerability metadata
was unreachable. The final publish repeated restore with approved NuGet access
and completed without that warning. The vulnerability check was not disabled.

### Historical artifact

- Package: `.codex-tmp/packages/deb-cross-d2edcb5640a14e4e8b202f0dab5cb2e2/texbb_0.1.0~alpha1_amd64.deb`
- SHA-256: `2EE5128CEF4F503D6991B18D8FA1F00DCAA325B89483207E4AA8E11368881137`
- Payload manifest and archive verification report are next to the package.
- All source changes, SDK/dependency caches, fixtures, and artifacts from this
  Linux task are inside `linux/`. No release was uploaded or installed.

### Original native Linux gate

The following checks were pending on 2026-10-26. The Ubuntu runtime results
above record the subset completed on 2026-10-27; they do not complete the entire
desktop checklist.

1. Core tests, including Unix file semantics, and clean package install/uninstall.
2. Native window launch, X11/XWayland and WebKitGTK preview, scaling, clipboard,
   Korean IME composition, and pen/touch input.
3. Actual TeX Live and Typst compilation with relative images/includes, PDF pages,
   cancellation, missing-tool messages, and build diagnostics.
4. Default document workspace, explicit workspace selection, recovery after exit,
   folder-opening via `xdg-open`, and coexistence with Windows workspace files.

The remaining feature-parity work is listed in [the README](../README.md).

### Reproduce Windows-side checks

```powershell
./scripts/test.ps1 -IncludeUi
./scripts/verify-markdown-browser.ps1
. ./scripts/Environment.ps1
& $LinuxDotnet run --project tests/TeXBB.Core.Tests --no-build -- --pdf-smoke .codex-tmp/a-fresh-pdf-fixture
./scripts/build.ps1 -PublishLinux
./scripts/package-deb.ps1 -PublishDirectory .codex-tmp/publish/<the-new-publish-directory>
```

The browser check uses a new profile, never an existing user profile. Poppler must
be on `PATH` for the optional PDF check. See [Linux setup](linux-setup.md) for
native dependencies and Linux commands.
