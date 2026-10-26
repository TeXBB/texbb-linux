#!/usr/bin/env bash
# Run only on the disposable Ubuntu CI runner after apt installs the package.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
[[ "${GITHUB_ACTIONS:-}" == true ]] || { echo 'This installed-package probe is reserved for the disposable CI runner.' >&2; exit 1; }
[[ -x /usr/bin/texbb && -n "${DISPLAY:-}" ]] || { echo 'Installed TeXBB and X11 display required.' >&2; exit 1; }
evidence="$root/.codex-tmp/test-runs/installed-smoke"
[[ ! -e "$evidence" ]] || { echo 'Use a fresh evidence directory.' >&2; exit 1; }
mkdir -p "$evidence"
texbb --workspace "$evidence/workspace" > "$evidence/launch.log" 2>&1 &
app_pid=$!
trap 'kill "$app_pid" 2>/dev/null || true; wait "$app_pid" 2>/dev/null || true' EXIT
window=''
for attempt in {1..80}; do
    kill -0 "$app_pid" 2>/dev/null || { cat "$evidence/launch.log" >&2; exit 1; }
    window="$(xdotool search --onlyvisible --pid "$app_pid" 2>/dev/null | head -n 1 || true)"
    [[ -z "$window" ]] || break
    sleep 0.25
done
[[ -n "$window" ]] || { echo 'No visible TeXBB X11 window appeared.' >&2; cat "$evidence/launch.log" >&2; exit 1; }
sleep 3
kill -0 "$app_pid"
scrot "$evidence/installed-desktop.png"
test -s "$evidence/installed-desktop.png"
printf 'PASS: installed ELF starts and creates a visible X11 window on Ubuntu CI.\nThis smoke test does not validate physical-device IME, clipboard, or full document rendering.\n' | tee "$evidence/results.txt"
