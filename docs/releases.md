# Linux releases

TeXBB Linux uses GitHub Releases at `https://github.com/TeXBB/texbb-linux/releases`.
The initial channel is one **alpha for Debian 13 and Ubuntu 24.04 LTS, x86-64**. It does not
change the Windows or Android release versions.

## Installation

The homepage's Linux download button links to the exact release's `.deb` asset.
The terminal alternative downloads the complete installer before executing it:

```bash
bash -c 'installer=$(curl -fsSL https://texbb.kiyuobjung.com/install.sh) && bash -c "$installer"'
```

Run this as your normal user. The installer checks the supported distribution
and architecture, downloads the pinned release's package and `SHA256SUMS`, checks
the checksum and Debian identity, and uses sudo only for APT. APT displays the
packages and asks for installation confirmation. It adds TeXBB to the app menu.
The checksum detects corrupt or mismatched downloads; it is not a separate
publisher signature. All release assets are fetched over HTTPS from GitHub.

The `.deb` installs `/opt/texbb`, `/usr/bin/texbb`, a desktop entry and an icon.
.NET is included. Native libraries are resolved by APT. LaTeX and Typst compilers
remain separate, and Pandoc remains optional. `sudo apt remove texbb` removes
the application and leaves documents in their workspace.

There is no APT repository or background updater. After a new version is
published and the homepage is updated to it, rerun the one-line command to
upgrade. The installer intentionally follows an exact tag, so alpha releases do
not depend on GitHub's stable-only `/releases/latest` behavior.

## Release procedure

1. Update `release/version.env`, the matching constants in `scripts/install.sh`,
   the desktop project's version and `release/notes.md`. Windows and Android
   metadata are independent. Run `bash scripts/verify-release.sh`.
2. If dependencies change, update `assets/legal/DEPENDENCIES.json` and the
   original license/notice copies with their provenance in `SOURCES.json`.
   `scripts/verify-licenses.py` verifies the inventory against the actual
   self-contained publish and checks every copied notice's SHA-256.
3. Push the reviewed source commit. Run the **Linux release** workflow manually
   to build and retain candidate assets without creating a public release.
4. Push the tag named by `TEXBB_RELEASE_TAG` to the public distribution repository.
   The tag workflow checks identity, builds/tests on Ubuntu 24.04, creates the
   `.deb`, installs it with APT, validates its desktop entry, checks an actual
   X11 window under Xvfb, captures a screenshot and removes the package.
5. Only after those checks pass does a separate job publish the `.deb`,
   `SHA256SUMS` and unchanged `install.sh` as a GitHub **prerelease**. Existing
   releases are not overwritten. Inspect the `linux-validation-evidence`
   artifact and release assets before updating the website's pinned URLs.
6. Deploy the homepage download button and `/install.sh` redirect only after
   anonymously verifying the public release and all asset URLs. Never change a
   private source repository's visibility merely to make downloads work; choose
   a separate public distribution repository instead, and update the installer
   and workflow contract consistently. The workflow refuses public distribution
   from a private repository.

The current release identity is `v0.1.0-alpha.1`, Debian version
`0.1.0~alpha1`, with asset `texbb_0.1.0~alpha1_amd64.deb`.

## Verification scope

`bash tests/verify-install.sh` checks success, architecture/distro rejection,
network errors, corrupt checksums and metadata, failure cleanup and privilege
boundaries using mocked commands. It never installs host packages.
The installer accepts Debian 13 and Ubuntu 24.04; that check establishes the
installation target, not a successful native GUI run. Debian 13 verification is
assigned to an independent graphical desktop using the
[same-package checklist](debian13-verification.md).

Windows cross-publishing and `scripts/package-deb.ps1` verify the ELF apphost and
archive payload; they do not establish Linux execution. The GitHub workflow's
Ubuntu installation and Xvfb window smoke provide additional evidence only when
the workflow actually runs successfully. They still do not replace real desktop
checks of Korean IME, clipboard, native WebKit preview, PDF rendering, and
external compiler integration. See `docs/verification.md` for prior evidence.
