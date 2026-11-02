# Embasic E-Note

**State (2026-11-02):** Public, MIT, experimental C#/.NET 10 WPF
Windows desktop source. **Not** a released or user-ready encrypted editor.

Tur 01: WPF shell. Tur 02: local workspace selection, folder tree,
folder creation. Tur 03: authentic light/dark Embasic logos and experimental
EMD1 AES-256-GCM/Windows DPAPI. Tur 04: experimental EMD2 portable
Argon2id key wrapping and ciphertext-only staging / optimistic SHA-256
overwrite guard. Tur 05: **ERB1 encrypted recovery-secret backup in memory**,
full EMD2 password-rotation re-encryption, explicit in-memory EMD1-to-EMD2
migration and **design-only** independent lock authorization planning.

## Verified source and tests

- [Tur 01 draft PR #1](https://github.com/ftmstr/Embasic-E-Note-Build/pull/1)
- [Tur 02 draft PR #2](https://github.com/ftmstr/Embasic-E-Note-Build/pull/2)
- [Tur 03 draft PR #3](https://github.com/ftmstr/Embasic-E-Note-Build/pull/3)
- [Tur 04 draft PR #4](https://github.com/ftmstr/Embasic-E-Note-Build/pull/4)
- [Tur 05 draft PR #5](https://github.com/ftmstr/Embasic-E-Note-Build/pull/5)
- [Tur 05 Windows CI](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41386156447):
  Release 0 errors/warnings, 97/97 automated checks PASS.
- [Tur 05 technical evidence](docs/CI-EVIDENCE-TUR05.md)
- [Encrypted backup and lock design](docs/RECOVERY-LOCK-DESIGN-TUR05.md)
- [Experimental EMD formats](docs/EMD-DRAFT.md)
- [Security restrictions](docs/SECURITY.md)
- [Licensed packages](NOTICE.md)

**Important:** EMD1, EMD2 and ERB1 are experimental formats. The
`.emd` New Secure Note WPF control is **DISABLED**. A lock planning
helper is not encryption or an access-control mechanism. Complete file
format freeze, robust key life cycle, backup UI, folder/subfolder/note
cryptographic locks, real Windows GUI/accessibility, crash/NTFS/ACL
recovery, and UDF official Editor acceptance are outstanding. No
installer/release binary is published.

The project aims to be offline-first, free/open source and independent
of paid APIs/cloud services. P0 printing, PDF/DOCX/RTF/HTML/TXT/MD
and UDF exports are planned, not yet available. UDF compatibility must
be verified with the real UYAP editor. No unapproved merge to `main`.
