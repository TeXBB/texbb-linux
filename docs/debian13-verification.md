# Debian 13 GUI verification

Target: the existing TeXBB desktop app on **Debian 13 x86-64 (amd64)**, in an
independent graphical desktop. Development remains on the Windows workstation;
Frieren performs the actual Linux installation and GUI verification. No separate
headless application or Debian-specific build is needed for this check.

## Candidate and known evidence

- Package: `texbb_0.1.0~alpha1_amd64.deb`.
- Candidate SHA-256: `5fcc8cfb558463622c0cd9d4def4f09025b161dbd7daa81aa33014195ccf6d58`.
- The same package was prepared for Ubuntu 24.04. Its dependency alternatives
  already include Debian 13's `libicu76`, `libssl3t64`, and `libgtk-3-0t64`, with
  `libwebkit2gtk-4.1-0` and `libsoup-3.0-0`. The installer now admits Debian 13.
- Microsoft lists [.NET 10 on Debian 13](https://learn.microsoft.com/en-us/dotnet/core/install/linux-debian)
  as supported. Debian provides the required
  [WebKitGTK 4.1 / GTK 3 backend](https://packages.debian.org/trixie/amd64/libwebkit2gtk-4.1-0).
- Read-only `readelf` inspection of 18 native files matching the candidate
  manifest found maximum requirements `GLIBC_2.27`, `GLIBCXX_3.4.22` and
  `CXXABI_1.3.7`, below the versions provided by Debian 13's
  [glibc 2.41](https://packages.debian.org/trixie/amd64/libc6) and
  [GCC 14 C++ runtime](https://packages.debian.org/trixie/amd64/libstdc%2B%2B6).
  This supports trying the unchanged package first; it is not an execution test.
- Package availability and Windows-side archive checks do not establish native
  GUI compatibility. A Debian 13 run has not yet been recorded here.

## Checks on the Debian desktop

1. Record `/etc/os-release`, `uname -m`, `dpkg --print-architecture`,
   `XDG_SESSION_TYPE`, and `DISPLAY`. This app uses X11; a Wayland desktop needs
   XWayland and a usable `DISPLAY`. A desktop being available does not yet prove
   that TeXBB can open a window.
2. Download the release assets using a GitHub account that can read the private
   repository, if the pre-release is private. In a fresh test directory, run
   `sha256sum --check SHA256SUMS`, then inspect `dpkg-deb --info` and install:

   ```bash
   sudo apt update
   sudo apt install ./texbb_0.1.0~alpha1_amd64.deb
   ```

   Record the APT result and installed package version. Try the existing package
   unchanged first; do not replace Debian packages with Ubuntu packages.
3. Launch from the application menu and from a desktop terminal. Use a fresh
   workspace for the terminal launch, for example
   `texbb --workspace "$PWD/verification-workspace"`. Record an actual window
   screenshot, launch logs and any missing-library errors.
4. Use the mouse and keyboard: open/edit/save/reopen Markdown, render equations
   and local images offline, open a multipage PDF, switch pages, and test
   clipboard and Korean input composition when an IME is available. Record any
   unavailable input method as untested. Confirm light/dark display and basic
   drawing. Missing optional LaTeX/Typst compilers should produce a clear status;
   if present, check one document compilation. Pandoc is optional.
5. Remove and reinstall TeXBB. Check application-menu registration and removal,
   and verify the test workspace documents survive removal. Report package
   installation, GUI startup, rendering and document operations separately.
6. Send OS/session details, the exact package hash, results, screenshots and
   reproducible failures back to the developer. Do not mark a check passed from
   source inspection or a mocked test alone.

## One-line installation

The public one-line installer needs anonymously accessible GitHub assets. A
private pre-release can deliver the `.deb` for testing, but its assets require
authentication; the public `curl` command will not work against that private
release. Test the public one-line command after the distribution assets and
homepage redirect are published. Until then, use the authenticated download
and local APT installation above.
