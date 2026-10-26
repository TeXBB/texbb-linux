#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/build.sh"
[[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || texbb_fail "run.sh requires an x86-64 Linux desktop."
texbb_prepare_environment
application="$texbb_temporary/publish/linux-x64/TeXBB"
if [[ ! -x "$application" ]]; then texbb_build; fi
has_workspace=false
for argument in "$@"; do
    if [[ "$argument" == --workspace ]]; then has_workspace=true; fi
done
if [[ "$has_workspace" == false ]]; then
    set -- --workspace "$texbb_temporary/workspace" "$@"
fi
exec "$application" "$@"
