# Embasic E-Note

**Current state (2026-11-02): Experimental Windows desktop source, not a released encrypted editor.**

Tur 01: .NET 10 WPF solution scaffold. Tur 02: workspace selection,
folder tree, folder creation and Unicode filesystem smoke checks.
Tur 03: experimental in-memory EMD1 AES-256-GCM container with per-file
random key, DPAPI CurrentUser key wrapping and optional random recovery
secret. The user's official light/dark Embasic logos are embedded as
offline WPF resources with a theme toggle.

**Warning:** Secure note creation/open/save, portable Argon2id password mode,
independent folder/note locks, atomic ciphertext disk writes, key backup UX,
Markdown editor and printing/UDF are NOT IMPLEMENTED. In particular the
`.emd` UI button stays DISABLED. Experimental EMD1 is not a frozen format
and should not be used for real user notes.

## Engineering evidence

- [Tur 01 draft PR #1](https://github.com/ftmstr/Embasic-E-Note-Build/pull/1)
- [Tur 02 draft PR #2](https://github.com/ftmstr/Embasic-E-Note-Build/pull/2)
- [Tur 03 draft PR #3](https://github.com/ftmstr/Embasic-E-Note-Build/pull/3)
- [Tur 03 last verified CI (build, 17/17 storage + 15/15 experimental EMD)](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41383011430)
- [Experimental EMD specification](docs/EMD-DRAFT.md)
- [Official asset provenance](docs/ASSET-PROVENANCE-TUR03.md)
- [Developer setup](docs/DEVELOPMENT.md)
- [Security release limitations](docs/SECURITY.md)

These are stacked feature PRs; main is unchanged. Build and automated
smoke-test success do not prove real Windows GUI/keyboard/DPI acceptance.

## Product goal

Fully offline, MIT-licensed Windows Markdown editor. Standard `.md` remains
plain UTF-8 text; planned `.emd` is independently encrypted even without
optional UI/folder/note locks. Planned P0 exports: Windows print,
PDF/DOCX/RTF/HTML/TXT/MD and UDF writer validated with real UYAP Editor.
A left-panel-only advertisement area must never see note content or keys.

No required subscription, cloud account, paid SDK or online activation.
The Microsoft ProtectedData package (10.0.14) is MIT licensed; a full
license/SBOM and physical Windows release acceptance are outstanding.

[MIT license](LICENSE)
