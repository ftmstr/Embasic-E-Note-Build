# Embasic E-Note

**2026-11-02: EXPERIMENTAL Windows application source; not a usable
released secure Markdown editor.**

Stacked draft branches/PRs (all open, `main` unchanged):
[Tur 01 #1](https://github.com/ftmstr/Embasic-E-Note-Build/pull/1) ·
[Tur 02 #2](https://github.com/ftmstr/Embasic-E-Note-Build/pull/2) ·
[Tur 03 #3](https://github.com/ftmstr/Embasic-E-Note-Build/pull/3) ·
[Tur 04 #4](https://github.com/ftmstr/Embasic-E-Note-Build/pull/4) ·
[Tur 05 #5](https://github.com/ftmstr/Embasic-E-Note-Build/pull/5) ·
[Tur 06 #6](https://github.com/ftmstr/Embasic-E-Note-Build/pull/6).

The C#/.NET 10 WPF foundation supports a folder picker, lazy folder
tree and genuine light/dark Embasic logos. Experimental EMD1 and EMD2
encryptors use AES-256-GCM, Windows DPAPI, Argon2id and recovery key
slots; ERB1 backup protects a recovery secret.

**Tur 06 (experimental):** EMD2 result owns disposable/zeroed secret
buffers; encrypted ERB1 backup is created/read on local disk with
fail-closed authentication; new `ELK1` AEAD outer layers require
independent random cryptographic keys for folder, child and note
*before* EMD2's password, DPAPI or recovery slot can be reached.
These layers are NOT connected to the real workspace, UI or on-disk
EMD2 storage. There is no persistent folder-key hierarchy or
credential setup yet.

[Latest Tur 06 Windows CI](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41388057386):
135/135 automated PASS, Windows Release 0 warnings/errors. This is
headless automated test evidence, **not a security acceptance**.
See [Tur 06 security report](docs/CI-EVIDENCE-TUR06.md)
and [format draft](docs/EMD-DRAFT.md).

**New Secure Note (.emd) stays DISABLED.** Independent locks,
safe user backup/restore UX, real NTFS ACL, different-account
DPAPI, power-loss, anti-TOCTOU/CAS, GUI/DPI/keyboard and third-party
crypto security review remain open. No Windows installation package
was produced. P0 print, PDF/DOCX/RTF/HTML/TXT/MD and UDF (actual
UYAP Editor validation) have not been implemented/accepted.

Fully offline open-source runtime goal: no mandatory cloud, account,
subscription or paid API. Code MIT; third-party licenses documented
in [NOTICE](NOTICE.md).
