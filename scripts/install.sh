#!/usr/bin/env bash
# Download this complete script before running it; do not run it with sudo.
# Release automation verifies these values against release/version.env.
set -euo pipefail

readonly release_tag='v0.1.0-alpha.1'
readonly package_version='0.1.0~alpha1'
readonly repository='TeXBB/texbb-linux'

texbb_fail() { printf 'TeXBB: %s\n' "$*" >&2; exit 1; }

texbb_install() {
    [[ $# == 0 ]] || texbb_fail "Usage: bash install.sh"
    local tool
    for tool in uname cat dpkg dpkg-deb curl sha256sum mktemp chmod rm id apt; do
        command -v "$tool" >/dev/null 2>&1 || texbb_fail "Required command is missing: $tool"
    done
    [[ "$(uname -s)" == Linux ]] || texbb_fail "This installer supports Ubuntu 24.04 LTS and Debian 13 on x86-64."
    local os_id='' os_version='' key value os_release
    os_release="$(cat /etc/os-release)" || texbb_fail "Cannot read /etc/os-release."
    while IFS='=' read -r key value; do
        value="${value#\"}"; value="${value%\"}"
        value="${value#\'}"; value="${value%\'}"
        case "$key" in
            ID) os_id="$value" ;;
            VERSION_ID) os_version="$value" ;;
        esac
    done <<< "$os_release"
    # Debian normally reports VERSION_ID=13; accept numeric 13.x point releases
    # too, without admitting testing aliases, derivatives or future major versions.
    [[ ( "$os_id" == ubuntu && "$os_version" == 24.04 ) ||
       ( "$os_id" == debian && "$os_version" =~ ^13(\.[0-9]+)?$ ) ]] ||
        texbb_fail "This alpha supports Ubuntu 24.04 LTS and Debian 13 only (found ${os_id:-unknown} ${os_version:-unknown})."
    [[ "$(uname -m)" == x86_64 && "$(dpkg --print-architecture)" == amd64 ]] ||
        texbb_fail "This alpha requires an x86-64 (amd64) system."
    local as_root
    as_root="$(id -u)"
    if [[ "$as_root" != 0 ]]; then
        command -v sudo >/dev/null 2>&1 || texbb_fail "sudo is required to install the package."
    fi

    local package_name="texbb_${package_version}_amd64.deb"
    local asset_url="https://github.com/$repository/releases/download/$release_tag"
    local download_dir package_path manifest expected_digest='' digest name extra
    umask 077
    download_dir="$(mktemp -d /tmp/texbb-install.XXXXXXXX)" || texbb_fail "Cannot create a temporary download directory."
    # Only this invocation's private, freshly created directory is removed.
    texbb_download_dir="$download_dir"
    trap 'rm -rf -- "$texbb_download_dir"' EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM
    package_path="$download_dir/$package_name"
    manifest="$download_dir/SHA256SUMS"
    printf 'Downloading TeXBB %s for Linux amd64 (%s %s)…\n' "$package_version" "$os_id" "$os_version"
    curl --fail --show-error --silent --location --proto '=https' --proto-redir '=https' \
        --connect-timeout 20 --retry 2 --output "$manifest" "$asset_url/SHA256SUMS" ||
        texbb_fail "Could not download release checksums. Nothing was installed."
    while read -r digest name extra; do
        [[ "$name" == "$package_name" ]] || continue
        [[ "$digest" =~ ^[0-9a-fA-F]{64}$ && -z "$extra" && -z "$expected_digest" ]] ||
            texbb_fail "Invalid or duplicate package checksum. Nothing was installed."
        expected_digest="${digest,,}"
    done < "$manifest"
    [[ -n "$expected_digest" ]] || texbb_fail "Release checksums do not contain $package_name. Nothing was installed."
    curl --fail --show-error --silent --location --proto '=https' --proto-redir '=https' \
        --connect-timeout 20 --retry 2 --output "$package_path" "$asset_url/$package_name" ||
        texbb_fail "Could not download the package. Nothing was installed."
    digest="$(sha256sum -- "$package_path")" || texbb_fail "Could not calculate the package checksum."
    digest="${digest%% *}"
    [[ "${digest,,}" == "$expected_digest" ]] ||
        texbb_fail "The downloaded package failed its SHA-256 check. Nothing was installed."
    [[ "$(dpkg-deb --field "$package_path" Package)" == texbb &&
       "$(dpkg-deb --field "$package_path" Version)" == "$package_version" &&
       "$(dpkg-deb --field "$package_path" Architecture)" == amd64 ]] ||
        texbb_fail "The package metadata does not match this release. Nothing was installed."

    # Let APT's unprivileged download helper read this verified public package.
    chmod 755 "$download_dir"
    chmod 644 "$package_path"
    printf 'Verified. APT will install TeXBB and its required desktop libraries.\n'
    if [[ "$as_root" == 0 ]]; then
        apt install -- "$package_path" || texbb_fail "APT could not complete installation. Review its message above."
    else
        sudo apt install -- "$package_path" || texbb_fail "APT could not complete installation. Review its message above."
    fi
    printf 'TeXBB is installed. Open TeXBB from your application menu, or run texbb.\n'
}
# Keep invocation last: an incomplete piped download must not start installation.
texbb_install "$@"
