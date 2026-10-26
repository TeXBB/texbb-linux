#!/usr/bin/env bash
# Offline behavioral tests: curl, dpkg, apt and sudo are replaced, never installed.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
source "$root/release/version.env"
script="$root/scripts/install.sh"
grep -Fqx "readonly release_tag='$TEXBB_RELEASE_TAG'" "$script"
grep -Fqx "readonly package_version='$TEXBB_VERSION'" "$script"
bash -n "$script"
[[ ! -L "$root/.codex-tmp" ]] || { printf 'Refusing symbolic-link test output.\n' >&2; exit 1; }
mkdir -p "$root/.codex-tmp"
git -C "$root" check-ignore -q .codex-tmp/install-tests.example ||
    { printf 'Test output must be ignored.\n' >&2; exit 1; }
test_root="$(mktemp -d "$root/.codex-tmp/install-tests.XXXXXXXX")"
[[ "$test_root" == "$root/.codex-tmp/install-tests."* ]] || exit 1
mock_bin="$test_root/bin"
mkdir "$mock_bin"
export TEXBB_TEST_ROOT="$test_root"
export TEXBB_TEST_REAL_CAT="$(command -v cat)"
export TEXBB_TEST_REAL_RM="$(command -v rm)"
export TEXBB_TEST_PACKAGE="texbb_${TEXBB_VERSION}_amd64.deb"
export TEXBB_TEST_VERSION="$TEXBB_VERSION"
export TEXBB_TEST_TAG="$TEXBB_RELEASE_TAG"
printf 'offline package fixture\n' > "$test_root/package.deb"
package_digest="$(sha256sum "$test_root/package.deb")"
export TEXBB_TEST_DIGEST="${package_digest%% *}"

cat > "$mock_bin/uname" <<'MOCK'
#!/usr/bin/env bash
case "$1" in
    -s) printf '%s\n' "${TEXBB_TEST_KERNEL:-Linux}" ;;
    -m) printf '%s\n' "${TEXBB_TEST_MACHINE:-x86_64}" ;;
    *) exit 91 ;;
esac
MOCK
cat > "$mock_bin/cat" <<'MOCK'
#!/usr/bin/env bash
if [[ "$*" == /etc/os-release ]]; then
    printf 'ID="%s"\nVERSION_ID="%s"\n' "${TEXBB_TEST_OS:-ubuntu}" "${TEXBB_TEST_OS_VERSION:-24.04}"
else
    "$TEXBB_TEST_REAL_CAT" "$@"
fi
MOCK
cat > "$mock_bin/dpkg" <<'MOCK'
#!/usr/bin/env bash
[[ "$*" == --print-architecture ]] || exit 91
printf '%s\n' "${TEXBB_TEST_ARCH:-amd64}"
MOCK
cat > "$mock_bin/dpkg-deb" <<'MOCK'
#!/usr/bin/env bash
[[ $# == 3 && "$1" == --field && "$2" == "$TEXBB_TEST_CASE/download/$TEXBB_TEST_PACKAGE" ]] || exit 91
printf 'metadata:%s\n' "$3" >> "$TEXBB_TEST_CASE/calls"
case "$3" in
    Package) printf '%s\n' "${TEXBB_TEST_META_NAME:-texbb}" ;;
    Version) printf '%s\n' "${TEXBB_TEST_META_VERSION:-$TEXBB_TEST_VERSION}" ;;
    Architecture) printf '%s\n' "${TEXBB_TEST_META_ARCH:-amd64}" ;;
    *) exit 91 ;;
esac
MOCK
cat > "$mock_bin/id" <<'MOCK'
#!/usr/bin/env bash
[[ "$*" == -u ]] || exit 91
printf '%s\n' "${TEXBB_TEST_UID:-1000}"
MOCK
cat > "$mock_bin/mktemp" <<'MOCK'
#!/usr/bin/env bash
[[ "$*" == '-d /tmp/texbb-install.XXXXXXXX' ]] || exit 91
mkdir "$TEXBB_TEST_CASE/download"
printf '%s\n' "$TEXBB_TEST_CASE/download"
MOCK
cat > "$mock_bin/rm" <<'MOCK'
#!/usr/bin/env bash
[[ $# == 3 && "$1" == -rf && "$2" == -- && "$3" == "$TEXBB_TEST_CASE/download" &&
    "$TEXBB_TEST_CASE" == "$TEXBB_TEST_ROOT/"* ]] || exit 91
printf 'cleanup\n' >> "$TEXBB_TEST_CASE/calls"
"$TEXBB_TEST_REAL_RM" "$@"
MOCK
cat > "$mock_bin/curl" <<'MOCK'
#!/usr/bin/env bash
output='' url='' https=0 redirect_https=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --output) output="$2"; shift 2 ;;
        --proto) [[ "$2" == '=https' ]] || exit 91; https=1; shift 2 ;;
        --proto-redir) [[ "$2" == '=https' ]] || exit 91; redirect_https=1; shift 2 ;;
        --connect-timeout|--retry) shift 2 ;;
        --fail|--show-error|--silent|--location) shift ;;
        https://*) url="$1"; shift ;;
        *) exit 91 ;;
    esac
done
[[ $https == 1 && $redirect_https == 1 && "$url" == "https://github.com/TeXBB/texbb-linux/releases/download/$TEXBB_TEST_TAG/"* ]] || exit 91
printf 'curl:%s\n' "$url" >> "$TEXBB_TEST_CASE/calls"
if [[ "$url" == */SHA256SUMS ]]; then
    [[ "$output" == "$TEXBB_TEST_CASE/download/SHA256SUMS" ]] || exit 91
    [[ "${TEXBB_TEST_MODE:-}" != manifest_download_failure ]] || exit 22
    case "${TEXBB_TEST_MODE:-}" in
        missing_checksum) printf '%s  unrelated.deb\n' "$TEXBB_TEST_DIGEST" > "$output" ;;
        invalid_checksum) printf 'invalid  %s\n' "$TEXBB_TEST_PACKAGE" > "$output" ;;
        duplicate_checksum) printf '%s  %s\n%s  %s\n' "$TEXBB_TEST_DIGEST" "$TEXBB_TEST_PACKAGE" "$TEXBB_TEST_DIGEST" "$TEXBB_TEST_PACKAGE" > "$output" ;;
        *) printf '%s  %s\n%s  install.sh\n' "$TEXBB_TEST_DIGEST" "$TEXBB_TEST_PACKAGE" "$TEXBB_TEST_DIGEST" > "$output" ;;
    esac
elif [[ "$url" == */"$TEXBB_TEST_PACKAGE" ]]; then
    [[ "$output" == "$TEXBB_TEST_CASE/download/$TEXBB_TEST_PACKAGE" ]] || exit 91
    [[ "${TEXBB_TEST_MODE:-}" != package_download_failure ]] || exit 22
    cp "$TEXBB_TEST_ROOT/package.deb" "$output"
    [[ "${TEXBB_TEST_MODE:-}" != corrupt_package ]] || printf 'corrupt\n' >> "$output"
else
    exit 91
fi
MOCK
cat > "$mock_bin/apt" <<'MOCK'
#!/usr/bin/env bash
[[ $# == 3 && "$1" == install && "$2" == -- && "$3" == "$TEXBB_TEST_CASE/download/$TEXBB_TEST_PACKAGE" ]] || exit 91
[[ -f "$3" ]] || exit 91
printf 'apt\n' >> "$TEXBB_TEST_CASE/calls"
[[ "${TEXBB_TEST_MODE:-}" != apt_failure ]]
MOCK
cat > "$mock_bin/sudo" <<'MOCK'
#!/usr/bin/env bash
[[ "$1" == apt ]] || exit 91
printf 'sudo\n' >> "$TEXBB_TEST_CASE/calls"
exec "$@"
MOCK
chmod +x "$mock_bin/"*

passed=0
run_case() {
    local label="$1" expected="$2" expected_message="$3"
    shift 3
    local case_dir="$test_root/$label" code=0
    mkdir "$case_dir"
    : > "$case_dir/calls"
    env PATH="$mock_bin:$PATH" TEXBB_TEST_CASE="$case_dir" "$@" bash "$script" > "$case_dir/output" 2>&1 || code=$?
    if [[ "$expected" == success ]]; then
        [[ "$code" == 0 ]] || { "$TEXBB_TEST_REAL_CAT" "$case_dir/output"; exit 1; }
        [[ "$(grep -c '^apt$' "$case_dir/calls")" == 1 ]] || exit 1
    elif [[ "$expected" == apt_failure ]]; then
        [[ "$code" != 0 && "$(grep -c '^apt$' "$case_dir/calls")" == 1 ]] || exit 1
    else
        [[ "$code" != 0 ]] || { printf 'Unexpected success: %s\n' "$label"; exit 1; }
        if grep -Eq '^(apt|sudo)$' "$case_dir/calls"; then
            printf 'Failure reached privileged installation: %s\n' "$label"; exit 1
        fi
    fi
    grep -Fq "$expected_message" "$case_dir/output" || { "$TEXBB_TEST_REAL_CAT" "$case_dir/output"; exit 1; }
    [[ ! -e "$case_dir/download" ]] || { printf 'Temporary download was not removed: %s\n' "$label"; exit 1; }
    passed=$((passed + 1))
    printf 'PASS %s\n' "$label"
}

run_case success success 'TeXBB is installed.'
[[ "$(grep -c '^sudo$' "$test_root/success/calls")" == 1 ]]
run_case root_success success 'TeXBB is installed.' TEXBB_TEST_UID=0
! grep -q '^sudo$' "$test_root/root_success/calls"
run_case debian_13_success success 'TeXBB is installed.' TEXBB_TEST_OS=debian TEXBB_TEST_OS_VERSION=13
[[ "$(grep -c '^sudo$' "$test_root/debian_13_success/calls")" == 1 ]]
grep -Fq 'Linux amd64 (debian 13)' "$test_root/debian_13_success/output"
run_case debian_13_root_success success 'TeXBB is installed.' TEXBB_TEST_OS=debian TEXBB_TEST_OS_VERSION=13 TEXBB_TEST_UID=0
! grep -q '^sudo$' "$test_root/debian_13_root_success/calls"
run_case debian_13_point_release success 'TeXBB is installed.' TEXBB_TEST_OS=debian TEXBB_TEST_OS_VERSION=13.1
run_case unsupported_kernel failure 'supports Ubuntu 24.04' TEXBB_TEST_KERNEL=MINGW64_NT
run_case unsupported_distribution failure 'Ubuntu 24.04 LTS and Debian 13 only' TEXBB_TEST_OS=fedora
run_case unsupported_version failure 'Ubuntu 24.04 LTS and Debian 13 only' TEXBB_TEST_OS_VERSION=22.04
run_case unsupported_debian_12 failure 'Ubuntu 24.04 LTS and Debian 13 only' TEXBB_TEST_OS=debian TEXBB_TEST_OS_VERSION=12
run_case unsupported_debian_14 failure 'Ubuntu 24.04 LTS and Debian 13 only' TEXBB_TEST_OS=debian TEXBB_TEST_OS_VERSION=14
run_case unsupported_debian_suffix failure 'Ubuntu 24.04 LTS and Debian 13 only' TEXBB_TEST_OS=debian TEXBB_TEST_OS_VERSION=13testing
run_case unsupported_debian_130 failure 'Ubuntu 24.04 LTS and Debian 13 only' TEXBB_TEST_OS=debian TEXBB_TEST_OS_VERSION=130
run_case unsupported_cpu failure 'x86-64 (amd64)' TEXBB_TEST_MACHINE=aarch64
run_case unsupported_arch failure 'x86-64 (amd64)' TEXBB_TEST_ARCH=arm64
run_case unsupported_debian_arch failure 'x86-64 (amd64)' TEXBB_TEST_OS=debian TEXBB_TEST_OS_VERSION=13 TEXBB_TEST_ARCH=arm64
for unsupported_case in "$test_root"/unsupported_*; do
    ! grep -q '^curl:' "$unsupported_case/calls" || { printf 'Unsupported platform reached download: %s\n' "$unsupported_case"; exit 1; }
done
run_case manifest_download_failure failure 'Could not download release checksums' TEXBB_TEST_MODE=manifest_download_failure
run_case package_download_failure failure 'Could not download the package' TEXBB_TEST_MODE=package_download_failure
run_case missing_checksum failure 'do not contain' TEXBB_TEST_MODE=missing_checksum
run_case invalid_checksum failure 'Invalid or duplicate' TEXBB_TEST_MODE=invalid_checksum
run_case duplicate_checksum failure 'Invalid or duplicate' TEXBB_TEST_MODE=duplicate_checksum
run_case corrupt_package failure 'failed its SHA-256 check' TEXBB_TEST_MODE=corrupt_package
run_case wrong_package_name failure 'metadata does not match' TEXBB_TEST_META_NAME=unrelated
run_case wrong_package_version failure 'metadata does not match' TEXBB_TEST_META_VERSION=99.0.0
run_case wrong_package_architecture failure 'metadata does not match' TEXBB_TEST_META_ARCH=arm64
run_case apt_failure apt_failure 'APT could not complete installation' TEXBB_TEST_MODE=apt_failure
printf '%s installer scenarios passed. Evidence: %s\n' "$passed" "$test_root"
