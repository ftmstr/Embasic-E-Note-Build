# Tur 18 — Offline vault rollback and root-key recovery decision
Date: 2026-11-08. Public repository ftmstr/Embasic-E-Note-Build.

## Verified security facts
- Tur15/16 tests show that old ELV1 copies and old passwords decrypt historical LayerSecrets. Password rotation wraps the SAME secret under a new password and does not revoke earlier exported keys.
- Tur16 VaultSnapshotPin and Tur17 TrustedVaultOpenGate provide useful **opt-in** checks, not trusted monotonic storage or mandatory interception of all callers.
- Tur18 bounded pinned read prevents unbounded encrypted-envelope allocation but old legacy APIs remain and the same-byte envelope can be replayed at another path.
- A fully compromised local user account and a malicious filesystem may defeat file-based local metadata. No absolute same-user malware protection is promised.

## Offline trust-anchor feasibility — candidates, none implemented
1. TPM-backed policy/NV monotonic counter **might** be feasible on some Windows hardware. Must verify hardware support, NV wear/access permissions, Windows TPM API rights, device migration, recovery after board replacement, lockout and performance on low-RAM systems. TPM alone does not erase previously copied ciphertext.
2. An externally held high-entropy recovery token or second offline medium could provide independent authorization, but a user may lose it and a copied token may also be replayed. Needs reviewed enrollment, secure recovery and ergonomics.
3. A monotonic ledger on the SAME Windows user-writable disk, including signed/HMAC/DPAPI-protected JSON/SQLite, is **not** rollback-resistant when attacker can roll back both data and ledger.
4. Internet-based witness or paid API is unacceptable as a mandatory dependency; optional Internet features may never gate offline note editing.

## Production requirements — NOT IMPLEMENTED
- Bind workspace and nested factor identity, current encrypted digest, monotonic epoch, recovery status, active policy and permitted format versions in an authenticated policy.
- Require this trust evidence on **every** app/folder/subfolder/note, EMD2, ELK1, ELV1 and backup restoration path. Remove/control legacy bypasses, old binaries and alternate DPAPI/portable slots.
- Real key revocation for CURRENT content requires fresh randomly generated factors and re-encryption of every dependent ELK1/EMD2 object under a staged, crash-recoverable transaction. Changed passwords alone cannot revoke copied keys.
- Historical encrypted backups cannot be retroactively unreadable to an adversary who already holds ciphertext and the historical secret. State this limit clearly when enabling future backup retention policies.
- Offline root recovery must handle lost hardware/TPM, lost passphrase, safe portable backups, and independent nested locks without silently reverting to less-protective old keys.
- Atomic migration should validate all new ciphertext, recover from power-loss, disk-full and interrupts, commit a trusted policy epoch, revoke old current keys, and inspect restored backups without bypass.
- Before safe release: real Windows cross-user DPAPI, NTFS ACL deny, sharing-lock, malicious symlink/junction/TOCTOU, cross-process CAS, recovery, crash and actual GUI acceptance.

## Tur18 accepted work (source level only)
The opt-in pinned read now checks ELV1 fixed size (174 bytes) before allocation and holds a FileShare.Read handle through ReadExactly. Windows CI verifies malformed/oversized inputs and sharing conflict. This does **not** bind path identity or enforce any trusted policy. Continue SEC-EMD-05 and W007 as BLOCKED. WPF encrypted .emd workflow DISABLED.
