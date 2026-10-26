#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/build.sh"
source "$texbb_linux_root/release/version.env"
[[ "$TEXBB_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+~alpha[0-9]+$ ]] || texbb_fail "Invalid Linux alpha package version."
[[ $# == 0 ]] || texbb_fail "Usage: bash scripts/package-deb.sh"
[[ "$(uname -s)" == Linux ]] || texbb_fail "Debian packaging must run on Linux."
command -v dpkg-deb >/dev/null || texbb_fail "dpkg-deb is required. No packages are installed by this script."
texbb_prepare_environment
publish="$texbb_temporary/publish/linux-x64"
[[ -d "$publish" && ! -L "$publish" && "$(cd -- "$publish" && pwd -P)" == "$publish" ]] || texbb_fail "Run bash scripts/build.sh first."
application="$publish/TeXBB"
[[ -x "$application" && -f "$publish/libcoreclr.so" ]] || texbb_fail "A self-contained Linux publish is required."
# Reject a Windows apphost or a wrong-architecture ELF rather than packaging it.
elf_signature="$(od -An -tx1 -N6 "$application" | tr -d ' \n')"
elf_machine="$(od -An -tx1 -j18 -N2 "$application" | tr -d ' \n')"
[[ "$elf_signature" == 7f454c460201 && "$elf_machine" == 3e00 ]] || texbb_fail "TeXBB must be a 64-bit little-endian x86-64 Linux ELF executable."
[[ -f "$texbb_linux_root/assets/texbb-logo.png" ]] || texbb_fail "The application icon is missing."

package_run="$(mktemp -d "$texbb_temporary/packages/deb.XXXXXX")"
stage="$package_run/root"
mkdir -p -- "$stage/DEBIAN" "$stage/opt/texbb" "$stage/usr/bin" \
    "$stage/usr/share/applications" "$stage/usr/share/icons/hicolor/256x256/apps"
cp -a -- "$publish/." "$stage/opt/texbb/"
cp -- "$texbb_linux_root/assets/texbb-logo.png" "$stage/usr/share/icons/hicolor/256x256/apps/texbb.png"
cat > "$stage/usr/bin/texbb" <<'LAUNCHER'
#!/bin/sh
exec /opt/texbb/TeXBB "$@"
LAUNCHER
cat > "$stage/usr/share/applications/texbb.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=TeXBB
Comment=Markdown, LaTeX, Typst and PDF workspace
Exec=/usr/bin/texbb
TryExec=/usr/bin/texbb
Icon=texbb
Terminal=false
Categories=Office;Education;
StartupWMClass=TeXBB
DESKTOP
installed_size="$(du -sk "$stage/opt" "$stage/usr" | awk '{total += $1} END {print total}')"
cat > "$stage/DEBIAN/control" <<CONTROL
Package: texbb
Version: $TEXBB_VERSION
Architecture: amd64
Maintainer: TeXBB contributors
Section: editors
Priority: optional
Installed-Size: $installed_size
Depends: ca-certificates, tzdata, libc6 (>= 2.35), libgcc-s1, libgssapi-krb5-2, libstdc++6, zlib1g, libicu74 | libicu76 | libicu78 | libicu72 | libicu70, libssl3t64 | libssl3, libx11-6, libice6, libsm6, libfontconfig1, libgtk-3-0t64 | libgtk-3-0, libwebkit2gtk-4.1-0, libsoup-3.0-0, poppler-utils, xdg-utils
Suggests: texlive-latex-base, latexmk, pandoc
Description: TeXBB Linux desktop alpha
 Markdown, LaTeX, Typst and PDF editing workspace with optional external
 compilers. Includes the .NET runtime; native desktop libraries are required.
CONTROL
chmod 755 "$stage/DEBIAN" "$stage/usr/bin/texbb" "$stage/opt/texbb/TeXBB"
chmod 644 "$stage/DEBIAN/control" "$stage/usr/share/applications/texbb.desktop" \
    "$stage/usr/share/icons/hicolor/256x256/apps/texbb.png"
package="$package_run/texbb_${TEXBB_VERSION}_amd64.deb"
dpkg-deb --root-owner-group --build "$stage" "$package"
dpkg-deb --info "$package"
printf 'Created (not installed): %s\n' "$package"
