# TUR 06 — EMD key lifecycle, nested cryptographic layers and recovery disk

**Status:** REVIEW; production EMD, actual folder locking, GUI and release **BLOCKED**.
**Date:** 2026-11-02. Public repository `ftmstr/Embasic-E-Note-Build`.
Base Tur 05 SHA: `13b6d9a70b9b41621a7fdd5fa7ff0c9ff7320a71`.
New feature branch `feature/tur-06-key-lifecycle-layer-20261102`.
Draft [PR #6](https://github.com/ftmstr/Embasic-E-Note-Build/pull/6).
`main` stays unchanged.

## Verified source commits / CI

- `480992ce07005ae37dd752305f406a4e0d5a912d` —
  `PortableEmdResult : IDisposable`. Its private encrypted-envelope
  and recovery-secret arrays are cleared under a lock on disposal.
  Access after disposal throws; the constructor and public accessors
  return defensive copies. Temporary original recovery secret in
  `PortableEmd.Encrypt` is cleared in `finally`.
- `ff53c4b71dd863e8fe3e9279739135ff0a7cbf60` —
  `LayeredEmd3`, `LayerSecret`, `RecoveryBackupDiskStore`, a new
  Tur 06 security test project and expanded Windows CI.
- [Windows CI 41388057386](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41388057386):
  Release **PASS, zero warnings/errors**. Previous smoke 17/17, EMD1 15/15,
  EMD2/disk 30/30, recovery/rotation/gates 35/35, and new lifecycle/locked
  layers/backup-disk **38/38**, totaling **135/135 PASS**. Headless Windows
  runner only; NOT a physical interactive Windows GUI test.

## Experimental ELK1 (layered cryptographic lock proof-of-concept)

This is a **separately versioned outer AEAD container**, not the EMD2
file-format release, not an implemented workspace key tree. It wraps
the *entire encrypted EMD2 file*, including all alternative
DPAPI/password/recovery key slots. Separate folder, locked subfolder,
and note-additional 256-bit random factors encrypt the next inner layer
through AES-256-GCM. Each layer's type, UUID, nonce and exact length
are authenticated as AAD. Only after **every matching outer factor** is
supplied does the codec invoke EMD2 credential unwrapping.

Therefore having the EMD2 DPAPI key or recovery secret alone does not
unlock the **ELK1-protected file at rest**. This does NOT revoke access
to previously copied unlayered EMD2 ciphertext, to backups, or to
malware with all session keys. Lock factors currently enter via
`LayerSecret` 256-bit keys directly; password derivation, secure
persistent key hierarchy, policy migration/rotation and UI are all
**NOT IMPLEMENTED**. The application UI lock remains separately
conceptual; it does not encrypt plaintext `.md`.

Envelope each layer: magic ASCII `ELK1`, version 1, kind byte
(Folder=1, Subfolder=2, NoteAdditional=3), 16-byte layer ID, 12-byte
nonce, 4-byte little-endian inner length, ciphertext, 16-byte tag.
Folder layer is outside any separately locked child, note lock is
innermost; no hard-coded folder nesting depth limit, but a 32 MiB
experimental envelope size guard. These are **draft format details**,
not final ENOTE-08 interoperability commitment.

## Recovery backup file, secret lifecycle

`RecoveryBackupDiskStore` only writes encrypted **ERB1** backup bytes
after successfully decrypting/authenticating with the supplied backup
password, to a `.erbk` target; fails if target exists. A randomly
named same-directory `.erbk-tmp` file holds **only ciphertext**, is
flushed, then `File.Move` without overwrite is used. On normal
errors the temporary ciphertext is cleaned. `Restore` checks ERB1
authentication through `RecoveryBackupCodec.Open`; caller must zero
the returned 32-byte secret and store the password separately.

`PortableEmdResult.Dispose` clears its *private buffers*, not
external copies, previously returned arrays, .NET strings, VM swap,
crash dumps, or clipboard histories. Callers must zero all secret
copies themselves and dispose every result. `LayerSecret.Dispose`
likewise zeroes its owned 32-byte key, and rejects later operations.

## Security and acceptance blockers

- **Windows ACL permission-denial:** NOT RUN. Existing exclusive
  sharing-lock tests are NOT ACL tests. Missing path, corruption,
  failed backup password and cleanup scenarios passed in CI.
- **Separate real Windows user/account/DPAPI recovery:** NOT RUN.
  Simulated denied DPAPI is not a real distinct user session.
- **Power loss, disk full, AV interference, rollback and NTFS
  durability:** NOT RUN.
- **Cross-process path TOCTOU, symlink replacement, cross-process
  atomic compare-and-swap:** NOT SOLVED.
- **Production root/folder/subfolder/note key distribution, persistent
  key hierarchy, credential-gated AND policy, safe locked-note file
  integration, migration and recovery UI:** NOT IMPLEMENTED.
- **External security audit, known-answer interoperability vectors,
  cryptographic parameter benchmarking, final SBOM and licensing audit:**
  NOT RUN.
- **Real WPF GUI/DPI/keyboard, print/exports/UDF in actual UYAP Editor,
  final Windows installer, clean VM:** NOT RUN / NOT BUILT.

In particular, `EncryptedNoteDiskStore` still accepts **only EMD2**,
not the new ELK1 format. No actual folder locked note persistence or
New Secure Note workflow has been enabled. Releasing the outer layer
without credential management and atomic safe persistence would be
premature. Existing authentic open/dark Embasic logos and WPF resources
are unchanged.

Estimated engineering progress **18%** (non-measured heuristic).
Conditional V1.0 2027 Q2–Q3 is **not a delivery commitment**. No
verified installer was uploaded to Drive `Uygulama`.
