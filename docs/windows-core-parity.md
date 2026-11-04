# Windows-style core workspace — 2026-11-02

The free Linux edition now follows the Windows app's core navigation and green palette. Advanced Windows Tools features are intentionally outside this change.

## Implemented

- File/Edit/View/Settings/Help menu, Blackboard/Markdown/LaTeX/Typst/Reference mode row, and compact document toolbar.
- Mode-filtered collapsible document list, open tabs with close/save/recovery decisions, aligned editor/preview headers, global build log, clickable path and caret/status footer.
- Save As, reload with a history snapshot for displaced unsaved text, per-document Undo/Redo/caret/selection/scroll retention, and offline syntax colors for all four text formats.
- Successful PDF build cache restored on returning to a document only when its source text still matches. Edited documents require rebuilding instead of silently displaying a stale cached result.
- Blackboard Open, pen color swatches, stroke eraser, Undo/Redo, editable save and PNG export. Unsupported Windows image/text objects remain protected. A workspace has one Blackboard editor; owner exit/workspace change waits for its save decision.
- General/Markdown/LaTeX/Typst/PDF settings pages with persistent preferences and atomic invalid-macro handling. No unused Pandoc settings page.

## Verification

- 71 portable core checks passed on Windows (`.codex-tmp/test-runs/2d989e4154c04547add0f4edb21e67cd`).
- 167 Skia headless UI checks passed, including dark/light layouts at 860 and 1280 px, syntax colors, tab-specific Undo, Save As destination protection, close/recovery/reload, settings persistence, board erasing/history/open, and Cancel/Save when closing a parent with a dirty Blackboard (`.codex-tmp/ui-parity-20261102/probe-4`). Final debug test build: zero warnings/errors.
- Native Ubuntu 24.04 under WSLg displayed the installed app, Markdown equations, Korean text and syntax colors. WSLg automated keyboard focus was unreliable, so input success is not inferred from that display check.
- A separate native Linux Xvfb/Openbox session on display :78 verified actual keyboard input, Ctrl+S disk persistence, switching documents, Undo of the injected text back to byte-for-byte original content, and drawing/saving a pointer stroke. Evidence images are in `.codex-tmp/ui-parity-20261102/`.
- The final self-contained Linux x64 publish was packaged with all 247 payload hashes verified. `dpkg -i` installed it into `TeXBB-Ubuntu2404`; installed `/opt/texbb/TeXBB.dll` SHA-256 matched the final publish (`9023491a07fc4997ee53e2bc14e733eb357714f8663467c1f77e82801ddac51a`). The installed app was relaunched under WSLg and left open with an isolated demo workspace.

The development package retains the current `0.1.0~alpha1` identity and was not published. Final package: `.codex-tmp/packages/deb-cross-b0007c7ca1c24edc9bee58e367681057/texbb_0.1.0~alpha1_amd64.deb` (SHA-256 `8a0cb2ed590b9514a5934f7cbe1029beff8a81ccd9087e9af404de2f9d7a27d0`). Prior source copies are under `.codex-tmp/ui-parity-20261102/before/`.

Not established here: actual LaTeX/Typst compilation (neither compiler installed in this WSL instance), physical Korean IME/pen/touch, clipboard integration, Debian 13/full desktop behavior, or public release/download installation. Native runs retained portal/PipeWire/DRI3 warnings also seen in the initial run; rendered output and native checks above succeeded despite them.