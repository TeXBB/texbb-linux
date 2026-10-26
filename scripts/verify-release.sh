#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
source "$root/release/version.env"
[[ "$TEXBB_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+~alpha[0-9]+$ ]] || { echo 'Invalid Linux alpha package version.' >&2; exit 1; }
expected_tag="v${TEXBB_VERSION/~alpha/-alpha.}"
[[ "$TEXBB_RELEASE_TAG" == "$expected_tag" ]] || { echo 'Release tag and Debian version disagree.' >&2; exit 1; }
grep -Fqx "readonly release_tag='$TEXBB_RELEASE_TAG'" "$root/scripts/install.sh"
grep -Fqx "readonly package_version='$TEXBB_VERSION'" "$root/scripts/install.sh"
assembly_version="${TEXBB_VERSION/\~/-}"
grep -Fq "<Version>$assembly_version</Version>" "$root/src/TeXBB.Desktop/TeXBB.Desktop.csproj"
[[ -s "$root/assets/legal/DEPENDENCIES.json" ]] || { echo 'Dependency license inventory is missing.' >&2; exit 1; }
printf 'Release identity verified: %s (%s)\n' "$TEXBB_RELEASE_TAG" "$TEXBB_VERSION"
