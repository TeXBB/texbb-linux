TeXBB Linux's first desktop alpha targeting **Debian 13 and Ubuntu 24.04 LTS, x86-64 (amd64)** with the same `.deb`. Use X11 or XWayland. Native GUI installation and execution on Debian 13 are pending independent verification.

Download the `.deb` below, then run `sudo apt install ./texbb_0.1.0~alpha1_amd64.deb`, or use:

```bash
bash -c 'installer=$(curl -fsSL https://texbb.kiyuobjung.com/install.sh) && bash -c "$installer"'
```

The installer checks distribution/version/architecture, verifies the package's SHA-256 checksum and identity, and asks APT to install it with its native dependencies. It uses sudo only for APT. Open **TeXBB** from the app menu after installation. Run the same command when a newer release is offered; no background updater or APT repository is installed. Remove the app with `sudo apt remove texbb`; documents remain in your workspace.

The app includes its .NET runtime. LaTeX and Typst compilers are separate installs; Pandoc is optional and is not bundled. This alpha does not yet match the Windows app's full feature set. Core/headless tests and an installed X11 startup smoke test are part of the release workflow; Korean IME, clipboard, and full native WebKit/document workflows still need broader desktop testing.

`SHA256SUMS` covers the `.deb` and `install.sh` assets. See the source's `docs/releases.md` for the build and verification process.
