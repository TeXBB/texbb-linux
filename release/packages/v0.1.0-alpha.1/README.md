# TeXBB Linux 0.1.0 alpha 1 verification package

The same amd64 package targets Debian 13 and Ubuntu 24.04 LTS.
Actual Debian GUI installation and execution are pending verification.

From this directory:

```bash
sha256sum --check SHA256SUMS
sudo apt install ./texbb_0.1.0~alpha1_amd64.deb
```

See ../../../docs/debian13-verification.md for the desktop verification checklist.
This package is intended to be committed for tester access through the repository.
