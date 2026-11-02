# Embasic E-Note

**Status — 2026-11-02:** Early, public MIT .NET 10 Windows desktop source.
NOT a usable or released secure Markdown editor.

Progress to date: WPF shell (Tur 01); real folder picker, lazy tree and
folder creation (Tur 02); user's genuine light/dark Embasic logos and
experimental EMD1 AES-GCM/DPAPI/recovery (Tur 03); separately versioned
experimental EMD2 password-based Argon2id key wrapping plus encrypted-only
disk staging and optimistic conflict detection (Tur 04).

## Important security restrictions

- WPF's **New Secure Note (.emd)** remains disabled; it cannot safely create
  or save real notes. The EMD2 codec and disk writer are experimental and
  not connected to the editor UI.
- Password/recovery setup and secure backup UX, independent
  application/folder/subfolder/note locks, cross-user disaster recovery,
  and external crypto review remain unfinished.
- Same-volume temporary-write + replace and SHA-256 checks reduce ordinary
  data-loss/overwrite errors; they do not guarantee race-free cross-process
  CAS, and power-loss/disk-full testing remains outstanding.
- Plain `.md` is standard UTF-8 plaintext. Encrypted `.emd` will remain
  encrypted even when optional application and folder locks are off.
- No Windows GUI acceptance, UDF writer/UYAP official Editor tests, export
  acceptance or Windows install package exists yet.

## Source and CI

[Tur 01 PR #1](https://github.com/ftmstr/Embasic-E-Note-Build/pull/1) ·
[Tur 02 PR #2](https://github.com/ftmstr/Embasic-E-Note-Build/pull/2) ·
[Tur 03 PR #3](https://github.com/ftmstr/Embasic-E-Note-Build/pull/3) ·
[Tur 04 PR #4](https://github.com/ftmstr/Embasic-E-Note-Build/pull/4).

[Tur 04 corrected Windows build & 62/62 tests](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41385023814) ·
[Tur 04 scope and negative evidence](docs/CI-EVIDENCE-TUR04.md) ·
[EMD experimental format](docs/EMD-DRAFT.md) ·
[License provenance](NOTICE.md).

The PRs are stacked in numerical order; default `main` remains unchanged.
The target release is fully offline with no mandatory paid API, cloud,
account, subscription or paid runtime license. P0 printing and UDF export
require real UYAP acceptance before version 1.0 can be declared complete.

[MIT LICENSE](LICENSE)
