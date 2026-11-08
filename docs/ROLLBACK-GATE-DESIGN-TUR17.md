# Tur 17 — ELV1 policy trust boundary and recovery design
Date: 2026-11-08. Public repo `ftmstr/Embasic-E-Note-Build`.

## Attacker capabilities and prior proof
- Copies an older .elkey (ELV1) encrypted factor vault and knows its
  former password. `LayerKeyVault.Rotate` rewrites password slots
  but reuses the SAME LayerSecret. Offline historical copies stay
  decryptable, even if the current file is updated.
- Can roll back ordinary local files containing ciphertext, anchor
  claims and the alleged epoch on the same attacker-controlled disk.
- Can invoke older application builds or call the legacy
  `LayerKeyVault.Open` / `LayerVaultDiskStore.Unlock` methods.
- Can keep historical ELK1/EMD2 content or unwrapped backups. You
  cannot retroactively erase ciphertext already copied by an attacker.

## Tur 16 versus Tur 17
Tur16 `VaultSnapshotPin` captures an authenticated ELV1 ciphertext
SHA-256 digest and a caller-supplied epoch, refusing a mismatched
ciphertext on one opt-in opening path. The epoch is NOT embedded in
ELV1 or independently trustworthy. No persistent monotonic anchor
was implemented.

Tur17 `TrustedVaultOpenGate` adds a fail-closed **consumer contract**:
`IExternalVaultAnchorReader.TryRead(workspaceId,lockId,kind,out grant)`.
A grant binds workspace ID, factor ID, layer kind, expected Tur16
pin and minimum epoch. The gate refuses NULL, missing/unavailable
anchor, empty IDs, mismatched identity, unbound pin or stale/nonpositive
floor. It then invokes the existing pinned disk unlock. Tests perform
actual Windows FileShare.None denial on GitHub's Windows runner,
identity/epoch/credential/ciphertext rejection, and demonstrate the
old-policy rollback and both legacy API bypasses.

**The reader interface is NOT itself a trust anchor.** A fake reader
returning a historical pin and historical minimum epoch can still open
the old vault. An ordinary JSON, sqlite record, DPAPI-protected file,
time-of-day clock or authenticated hash on the same writable disk
cannot alone guarantee monotonic anti-rollback against a disk-copying
adversary. Interface injection does not prove trust or enforce its
use across the entire application. No production reader is shipped.

## Before SEC-EMD-05 may pass (NOT IMPLEMENTED)
1. Define an explicit threat model and an **offline, independently
   rollback-resistant** trust source. Consider optional hardware TPM/NV
   counter or external human-controlled recovery as design candidates,
   not implemented or universally present; determine portability and
   low-cost/low-memory Windows compatibility. Without a trustworthy
   source, fail closed and explain restore limitations to users.
2. Policy record should bind workspace ID, layer ID/kind, current
   encrypted digest, minimum allowed epoch, protected recovery policy,
   and independent app/folder/subfolder/note unlock requirements.
   Authenticate record and enforce monotonic advancement.
3. Make **every** user-facing ELV1/ELK1/EMD2 open depend on the
   trusted gate. Remove or constrain legacy bypasses, old binaries,
   alternate DPAPI/password slots and backup restore paths before
   claiming revocation.
4. For forward credential revocation, generate **new random layer
   factors** and atomically re-encrypt all CURRENT dependent data.
   Merely rewrapping the same factor cannot revoke existing copies.
   Document that historical exported copies remain readable and
   must be handled by a secure backup/retention policy.
5. Crash-safe key migration: prepare/verifiable encrypted candidates,
   commit monotonic authorization policy without exposing the old
   credentials, resume/rollback according to reviewed transaction
   states. Test power-loss, disk-full, shared locks and multi-process
   consistency on actual Windows. Do not invalidate legitimate
   backups before verified recoverability and clear user approval.
6. Root-key loss, independently protected child and note lock
   recovery, different-user DPAPI, portable mode and attacker-held
   backup limitations must have user-facing recovery rules and tests.
7. Physical Windows WPF GUI, UYAP UDF writer and P0 print/export
   gates are separate; no CI build passes them implicitly.

**Decision:** W007/SEC-EMD-05 BLOCKED, WPF secure `.emd`
create/open/save DISABLED. The new opt-in gate is a useful
testable layer but NOT production anti-rollback.
