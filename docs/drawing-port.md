# Linux drawing prototype

`DrawingWindow(workspaceRoot)` provides freehand mouse/pen/touch drawing, five pen
colors, five widths, 50-step undo (including Clear), explicit editable-board saving, and
PNG export. Coordinates stay in logical board units when the window is resized.
Pressure-sensitive widths, erasing, object selection, image insertion, text, and
redo are not implemented. The initial board is 3200 × 1600 logical units.

The window reads `current/current_board.strokes.json`. Editable Windows V1 files
with only strokes are supported, including their `#AARRGGBB` colors, stroke widths,
point arrays, and canvas dimensions. Saves retain the V1 `images`, `strokes`, and
`texts` arrays and `mode: editable`. Existing data is backed up to `.bak`; a sibling
temporary file is moved into place. A changed-on-disk drawing is not overwritten.
**Save copy** writes a separate `current_board.linux-<timestamp>-<suffix>.strokes.json`.

Images, texts, raster-only mode, unknown properties/schema, malformed data,
missing originals with existing backups, and unsupported limits disable editing
and saving of the original. **New board** starts a separate Linux file. No fallback
attempt silently replaces a board with an empty or partial drawing. Backup
recovery remains a Windows/manual workflow.

The prototype accepts files up to 64 MiB, dimensions from 1 to 16384, and up to one
million points; widths must be positive and at most 16384. Stored coordinates
must be finite and representable by the Windows float model. PNG export uses the
current board aspect ratio, with its longest edge capped at 4096 pixels, and does
not mark editable changes as saved. Closing a modified board asks to save,
discard, or cancel.

Compilation is distinct from Linux runtime verification. Before release, exercise
pointer capture, pen and touch input, resize behavior, undo/clear, JSON round trips
with Windows, file-conflict handling, and PNG export on the target Linux desktop.
