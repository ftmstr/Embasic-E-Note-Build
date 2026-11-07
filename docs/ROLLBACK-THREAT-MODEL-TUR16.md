# Tur 16 — ELV1 rollback threat model, decision and acceptance gates

Date 2026-11-07. Public ftmstr/Embasic-E-Note-Build only.
The source of truth is the current Public stack, ENOTE-08 security
specification, and Tur15 demonstration that an old ELV1 encrypted
vault remains decryptable using its old password after password rotation.

## What an attacker with a historical vault can do
ELV1 encrypts the SAME 256-bit LayerSecret under a freshly derived password
and a distinct recovery credential. `LayerKeyVault.Rotate` decrypts the
old secret then rewraps it in a new ELV1 envelope without changing
LayerSecret. Consequently, copies of old envelopes are neither
cryptographically erased nor revoked by replacing the current file.
If an adversary already possessed an old envelope AND the old password,
they can continue to recover the historical secret and may open the
associated old ELK1 note and/or retained EMD2 backups. No local code
update can erase copies the adversary holds.

## What the Tur16 proof-of-concept does
- `VaultSnapshotPin.CaptureAuthenticated` first authenticates an envelope
  using its expected GUID, layer kind and credential. It then holds
  a SHA-256 of the exact ciphertext and a **caller supplied** epoch.
- `OpenPinned` verifies a positive trusted minimum epoch and compares
  the full SHA-256 of the supplied envelope with the pinned digest using
  `CryptographicOperations.FixedTimeEquals`, before decrypting the
  vault credential. Altered ciphertext, wrong ID/kind, old snapshots,
  bogus credentials, short lengths and stale pinned epochs are tested.
- `LayerVaultDiskStore.UnlockPinned` optionally exposes this read-only
  validation for an on-disk .elkey. There are **21 tests** including
  intentionally passing observations of insecure fallback scenarios.

## What it definitely does NOT do
1. **No trusted persistence** for the pin or epoch. The caller could
   store both beside the vault; an attacker can then roll back all three.
   No protected monotonic clock/counter, Windows TPM NV index, signed
   ledger or independent backup revocation service is implemented.
2. **No secure epoch in ELV1 format**. The integer is asserted by the
   caller and cannot independently be validated against the envelope.
3. **No forced interception**: the existing `LayerKeyVault.Open`,
   `LayerVaultDiskStore.Unlock` and older distributed program versions
   still open the old envelope. Production cannot claim pins are mandatory.
4. **No old-key destruction or retroactive confidentiality**. A third
   party who kept the original ciphertext and old password can still
   decrypt that historical vault with a compatible reader.
5. **No recovery architecture** that guarantees both independent
   folder/note factors and revocation of revoked historic credentials.
   Full rekey/re-encryption of current assets plus backup policy,
   legitimate offline recovery and old copied ciphertext risk require
   a separate reviewed design.
6. **No physical adversarial Windows validation**: ACL deny,
   cross-account DPAPI, sharing-lock, disk full, interruption, power
   loss, symlink/junction/TOCTOU and cross-process atomic CAS NOT RUN.

## Required design for production release (not implemented)
- A signed/versioned policy binding workspace ID, factor ID/kind,
  encrypted vault digest, epoch, recovery policy and required
  independent root/child/note factors.
- Its trust anchor MUST be demonstrably rollback-resistant from the
  specified threat actor; file-only storage on the same attacker-controlled
  filesystem is insufficient. Offline fallback must fail closed rather
  than silently revert to old passwords. Consider TPM-backed attestation
  where available only after OS/licensing portability analysis; do not
  impose paid API or Internet requirements.
- A documented initial enrollment, atomic policy update and full
  key rewrap/re-encryption protocol with crash-safe recovery. Handling
  of export, old backups, unsupported hardware, restore and loss of
  the trusted anchor requires an explicit user-facing recovery policy.
- Pin checks enforced at EVERY vault-opening path; remove or tightly
  constrain bypassable legacy APIs and disable old incompatible app
  versions during migration. Maintain independent app/folder/subfolder/
  note gates and encrypted-at-rest .emd when all locks are disabled.
- Threat model clearly differentiates rogue same-account malware
  (no absolute security), removable copied backups, offline clones,
  honest accidental restore and authorized recovery.

**Decision:** W007 / SEC-EMD-05 remain BLOCKED for user-facing `.emd`.
This prototype is only a unit-testable guard. Passing tests demonstrate
both a guarded path and an exploitable old-pin/no-pin path.
