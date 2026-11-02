# Tur 03: Official logo asset provenance

Source folder: Google Drive `Uygulama / Embasic E-Note / Logo`
ID: `1kmS0aWmxmil88sl7InwIgLxrmw0_zx3l`.

- Light UI: `Embasic Logo-1.png adlı dosyanın kopyası` (Drive ID
  `1nTD55BAK42-_tNp663__s-0cuIsALOYg`), embedded at
  `src/Embasic.ENote.App/Assets/Logo-Light.png`; Git blob
  `2b4aadbf85bce04c5e46b9a5afeaa1ff97c4cc4b`.
- Dark UI: `Logo2.png adlı dosyanın kopyası` (Drive ID
  `1lS6kEPwKtSJjBEv70lb54Jb82RMQ05Lp`), embedded at
  `src/Embasic.ENote.App/Assets/Logo-Dark.png`; Git blob
  `6cd10909b52479c12f5029b376f4699a0b789a6e`.

These are user-provided authentic brand artwork, not AI-generated replacements.
The original image files are unchanged and included as WPF Resource assets,
which work offline. User-supplied logo ownership/reuse and final trademark
distribution clearance are a release checklist item; this commit does not
declare any third-party license.

WPF build is verified in CI, but interactive Windows screenshot / DPI /
theme visual acceptance tests are NOT RUN. Dark and light views use
a theme-toggle action in the WPF window.
