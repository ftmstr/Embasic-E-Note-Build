# Tur 19 — ELV1 encrypted vault path-policy binding and trust limitations
Date: 2026-11-08 | Public repository ftmstr/Embasic-E-Note-Build

## Change and precise acceptance
`VaultAnchorGrant` now optionally carries a `BoundVaultPath`
string. New `TrustedVaultOpenGate.UnlockPathBound` requires an
independently supplied absolute path, canonicalizes the path and
requested vault filename using `Path.GetFullPath`, compares using
ordinal ignore-case for Windows (ordinal on other systems), and
rejects a mismatch BEFORE calling `LayerVaultDiskStore.UnlockPinned`.
The legacy `TrustedVaultOpenGate.Unlock` is intentionally unchanged.
The code cannot attest that the supplied anchor is independently
trustworthy, and the new method is not wired into WPF.

The implementation is a **lexical filename policy** only. It is not
NTFS stable file identity: no volume GUID, file ID, opened-handle ID,
object identity, parent-directory handle traversal, junction/symlink
resolution or atomic open-and-check is implemented. It is also not
robust against per-directory Windows case-sensitivity settings or
malicious mount aliases. `Path.GetFullPath` resolves relative
components, not the final filesystem object. A malicious local actor
who can rename/replace a pathname, modify an ancestor, or roll back
the independent grant can bypass its intended guarantee.

## Deterministic CI-proven properties
- Correct policy path and distinct recovery credential open the
  current vault; path spellings with canonical . or .. are equivalent.
- A same-byte ciphertext clone at a different fully-qualified path
  fails on **this new path-bound API**.
- Missing/empty/whitespace/relative/mismatched policy path,
  incorrect workspace/factor/kind, stale epoch, missing anchor,
  wrong password, empty or 1-MB oversized envelope are rejected.
- `FileShare.None` denial is verified on a GitHub-hosted
  Windows runner. This is a single-process sharing-conflict test,
  NOT hostile cross-process atomic compare-and-swap.
- Risk-confirmation tests intentionally PASS when the same clone
  opens via historical Tur17 `Unlock` or direct disk `Unlock`.
  A rolled-back old grant + old ELV1 and old password still open
  via the *new* path-bound API at its historical path. Direct
  `LayerKeyVault.Open` remains usable. These tests document
  unresolved bypasses, not remediated vulnerabilities.

## Requirements before any real offline anti-rollback claim
1. Define threat actors: accidental restoration, malicious local
   same-account code, offline stolen old backups, cloned whole disk,
   and legitimate user recovery are not interchangeable.
2. Prove an independently rollback-resistant, **offline** policy
   source with controlled enrollment and portable migration.
   TPM/NV counter and user-held external recovery medium are
   research candidates, NOT supported implementations.
   Storing signed/HMAC/DPAPI metadata beside the same rollbackable
   encrypted disk does not establish monotonicity.
3. Bind an authenticated policy to workspace ID, factor ID/kind,
   current ciphertext digest, minimum epoch, physical file identity
   where appropriate and mandatory nested lock requirements.
   Require the policy on every app/folder/subfolder/note lock,
   EMD2/ELK1/ELV1 opening, legacy codepath and backup recovery.
   Address old binary builds and alternate recovery/DPAPI slots.
4. A password-only ELV1 rewrap cannot revoke an already-exfiltrated
   LayerSecret. For CURRENT datasets, fresh random layer keys and
   complete dependency re-encryption are required, plus
   crash-consistent migration and explicit old-backup handling.
   Historical stolen ciphertext can never be recalled remotely.
5. Review root-key loss, portable encrypted backup, offline
   hardware loss, verified restore and independent child locks.
   Fail closed without a trusted anchor; do not silently fall
   back to weaker historical credentials.
6. Physically test NTFS ACL deny, symlink/junction/reparse TOCTOU,
   malicious cross-process races, disk-full, power failure,
   multi-user DPAPI and actual Windows WPF GUI before release.

Tur19 is **an opt-in policy-path experiment**. W007 / SEC-EMD-05
are BLOCKED; WPF New Secure Note (.emd) DISABLED; V1.0 BLOCKED.
