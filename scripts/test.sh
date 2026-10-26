#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/build.sh"
[[ $# == 0 ]] || texbb_fail "Usage: bash scripts/test.sh"
texbb_prepare_environment
texbb_find_dotnet
cd -- "$texbb_linux_root"
test_run="$(mktemp -d "$texbb_temporary/test-runs/core.XXXXXX")"
printf 'Test evidence: %s\n' "$test_run"
"$texbb_dotnet" run --project tests/TeXBB.Core.Tests/TeXBB.Core.Tests.csproj \
    --configuration Release -- "$test_run/fixtures" 2>&1 | tee "$test_run/results.log"
