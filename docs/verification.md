# Linux alpha verification — 2026-10-26

This records the first `0.1.0-alpha1` Linux implementation. Verification ran on
Windows 10.0.26200 with the locally scoped .NET SDK 10.0.403. There is no installed
WSL distribution on this host. No native Linux execution or package installation
is claimed.

## Completed

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

## Final artifact

- Package: `.codex-tmp/packages/deb-cross-d2edcb5640a14e4e8b202f0dab5cb2e2/texbb_0.1.0~alpha1_amd64.deb`
- SHA-256: `2EE5128CEF4F503D6991B18D8FA1F00DCAA325B89483207E4AA8E11368881137`
- Payload manifest and archive verification report are next to the package.
- All source changes, SDK/dependency caches, fixtures, and artifacts from this
  Linux task are inside `linux/`. No release was uploaded or installed.

## Native Linux gate

On the target Ubuntu graphical desktop, still verify:

1. Core tests, including Unix file semantics, and clean package install/uninstall.
2. Native window launch, X11/XWayland and WebKitGTK preview, scaling, clipboard,
   Korean IME composition, and pen/touch input.
3. Actual TeX Live and Typst compilation with relative images/includes, PDF pages,
   cancellation, missing-tool messages, and build diagnostics.
4. Default document workspace, explicit workspace selection, recovery after exit,
   folder-opening via `xdg-open`, and coexistence with Windows workspace files.

The remaining feature-parity work is listed in [the README](../README.md).

## Reproduce Windows-side checks

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
