#!/usr/bin/env bash
set -euo pipefail

# The other scripts source these helpers; sourcing does not start a build.
texbb_linux_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
texbb_temporary="$texbb_linux_root/.codex-tmp"

texbb_fail() { printf '%s\n' "TeXBB: $*" >&2; exit 1; }

texbb_directory() {
    local directory="$texbb_temporary/$1"
    [[ ! -L "$texbb_temporary" && ! -L "$directory" ]] || texbb_fail "Artifact directories must not be symbolic links: $directory"
    mkdir -p -- "$directory"
    [[ "$(cd -- "$directory" && pwd -P)" == "$directory" ]] || texbb_fail "Artifact path resolves outside its expected location: $directory"
}

texbb_prepare_environment() {
    local directory
    for directory in dotnet-home nuget nuget-http tmp cache publish test-runs packages workspace; do
        texbb_directory "$directory"
    done
    export DOTNET_CLI_HOME="$texbb_temporary/dotnet-home"
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_NOLOGO=1
    export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
    export NUGET_PACKAGES="$texbb_temporary/nuget" NUGET_HTTP_CACHE_PATH="$texbb_temporary/nuget-http"
    export TMPDIR="$texbb_temporary/tmp" TMP="$texbb_temporary/tmp" TEMP="$texbb_temporary/tmp"
    export XDG_CACHE_HOME="$texbb_temporary/cache"
}

texbb_find_dotnet() {
    if [[ -n "${TEXBB_DOTNET:-}" ]]; then
        texbb_dotnet="$TEXBB_DOTNET"
    elif [[ -x "$texbb_linux_root/.codex-tools/dotnet/dotnet" ]]; then
        texbb_dotnet="$texbb_linux_root/.codex-tools/dotnet/dotnet"
    else
        texbb_dotnet="$(command -v dotnet || true)"
    fi
    [[ -n "$texbb_dotnet" ]] || texbb_fail "Install .NET SDK 10.0.403, or set TEXBB_DOTNET to its executable."
    texbb_dotnet="$(command -v -- "$texbb_dotnet" || true)"
    [[ -n "$texbb_dotnet" && -x "$texbb_dotnet" ]] || texbb_fail "TEXBB_DOTNET must name an executable."
    # Resolve before changing directory, including an explicit relative path.
    texbb_dotnet="$(readlink -f -- "$texbb_dotnet")"
    if [[ -d "$(dirname -- "$texbb_dotnet")/host/fxr" ]]; then
        export DOTNET_ROOT="$(dirname -- "$texbb_dotnet")"
    fi
}

texbb_build() {
    texbb_prepare_environment
    texbb_find_dotnet
    cd -- "$texbb_linux_root"
    local staging destination previous
    staging="$(mktemp -d "$texbb_temporary/publish/.linux-x64.XXXXXX")"
    "$texbb_dotnet" publish src/TeXBB.Desktop/TeXBB.Desktop.csproj \
        --configuration Release --runtime linux-x64 --self-contained true --output "$staging"
    [[ -f "$staging/TeXBB" && -f "$staging/libcoreclr.so" ]] || texbb_fail "Publish did not produce the self-contained Linux app. Retained: $staging"
    destination="$texbb_temporary/publish/linux-x64"
    [[ ! -L "$destination" ]] || texbb_fail "Publish destination must not be a symbolic link."
    if [[ -e "$destination" ]]; then
        [[ -d "$destination" && "$(cd -- "$destination" && pwd -P)" == "$destination" ]] || texbb_fail "Unexpected publish destination: $destination"
        previous="$(mktemp -d "$texbb_temporary/publish/previous.XXXXXX")"
        mv -- "$destination" "$previous/linux-x64"
    fi
    mv -- "$staging" "$destination"
    printf 'Published: %s\n' "$destination"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    [[ $# == 0 ]] || texbb_fail "Usage: bash scripts/build.sh (set TEXBB_DOTNET to select an SDK)"
    texbb_build
fi
