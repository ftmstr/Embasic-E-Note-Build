# Embasic E-Note

**Status (1 November 2026):** Early .NET 10 / WPF desktop source under review.
Tur 02 adds a real local folder picker, a lazy-loading directory tree,
folder creation and refresh. A usable Markdown editor, encrypted `.emd`
implementation, Windows GUI acceptance and an installable v1.0 release
**do not yet exist**.

## Development and evidence

- [Tur 01 WPF scaffold PR #1](https://github.com/ftmstr/Embasic-E-Note-Build/pull/1)
- [Tur 02 workspace tree PR #2](https://github.com/ftmstr/Embasic-E-Note-Build/pull/2)
- [Verified Tur 02 Windows CI](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41330908174): Release build with zero warnings/errors, **17/17 smoke checks passed**.
- [Windows CI details and limitations](docs/CI-EVIDENCE-TUR02.md)
- [Developer guide](docs/DEVELOPMENT.md)
- [Current security limitations](docs/SECURITY.md)

Tur 02 is a **stacked draft PR** based on Tur 01's feature branch.
The `main` branch has not been changed as part of this work.

## Planned note formats and security

- `.md` is standard, **unencrypted** Markdown.
- `.emd` (Embasic Mark Down) will be a public, versioned encrypted format,
  independent of optional application, folder and note locks.
- Existing `.emd` filenames appear in the tree but cannot be opened,
  created or saved. No plaintext fallback is permitted.
- The open format may be implemented by third parties; confidentiality
  will rely on authenticated encryption and secret key management.

## Planned v1.0 features and release gates

Local folders and files; hierarchical folders with custom icons; tabbed
Markdown editing and safe preview; encryption, key recovery and independent
locks; Windows printing and PDF/DOCX/RTF/HTML/TXT/MD export; separately
developed UDF writer proven by real UYAP editor open/save tests.

An isolated advertising slot is restricted to the lower left folder panel;
the app must remain offline-first and free of mandatory paid services.
No advertising SDK was introduced.

## License

[MIT](LICENSE). Before release, validate all third-party runtime licenses,
third-party notices, SBOM and the installer acceptance criteria.
