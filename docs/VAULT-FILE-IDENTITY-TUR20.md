# Tur 20 — ELV1 file identity and offline recovery design

Status **REVIEW**. W007 / SEC-EMD-05 and V1.0 remain **BLOCKED**.

## Experimental read

The external grant supplies an absolute vault path, the expected Windows volume serial and 128-bit file ID, a ciphertext pin, the minimum epoch and the workspace and factor identifiers. `UnlockFileBound` requires exact ordinal comparison of normalized paths. A case alias requires a separate policy decision even on case-insensitive directories.

The disk store uses `GetFileInformationByHandleEx(FileIdInfo)` on the same open handle from which it acquires the bounded 174-byte ELV1 snapshot. The expected ID must match before any ciphertext is copied. The digest and AEAD checks then run on that snapshot. The API requires Windows and fails when an identity is missing, unusable or unavailable. The previous experimental path gate and the legacy opens retain their existing contracts. The WPF secure-note entry remains disabled.

The identifier is scoped to the Windows environment in which it was enrolled. A hard link shares the underlying identity and requires an approved path. A rename preserves the underlying identity and requires a policy update. Copies, restore to another object and replace-by-rename normally need deliberate re-enrollment. The grant issuer must derive its identity during trusted enrollment. The caller must never replace a missing grant by inspecting the requested file.

The existing storage path checks still inspect the leaf and immediate parent before open. The new guard authenticates the resulting file object and bytes against the grant. It has no authenticated traversal of each ancestor, guarantee over network or virtual providers, hostile process CAS, or safe enrollment through a mutable namespace. The CI junction scenario is a sequential retarget with a byte-identical clone. Hostile simultaneous mutation and physical Windows acceptance remain NOT RUN. File IDs and digests also leave in-place replay possible when the policy itself is rolled back.

Win32 reference [FILE_ID_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info).

## Offline trust source feasibility

The minimum independent anchor contract is an atomic compare-and-advance for a workspace that binds the current epoch to an authenticated committed manifest digest. A manifest identifies enrolled objects and all required lock factors. Recovery verifies its authenticity and freshness before it can restore access. A local file, including a DPAPI-protected file on the same rollbackable disk, cannot supply this independent evidence.

A TPM NV counter is a candidate for advancing an epoch. A counter by itself leaves a digest-binding and crash-ordering problem. Before selecting it, demonstrate exclusive writer authorization, prevention of two candidate manifests for one epoch, binding of epoch to a digest, ambiguous update readback, counter reset and TPM replacement handling, and acceptable update frequency on the supported Windows matrix. A separately held offline device is a candidate only if the device enforces monotonic compare-and-advance itself and has tested loss and migration behavior. An ordinary removable file and a recovery key can carry recovery material but are not monotonic state.

No provider implements this contract in Tur 20. Threat actors include accidental restore, whole-disk rollback, same-account malicious code, old binaries and exfiltrated ciphertext. Independent freshness and mandatory enforcement are required even when the original file ID still matches.

## Root recovery and staged factor rekey

1. Enroll a separate recovery credential to wrap root recovery material. Authenticate the workspace ID and the recovery manifest independently. A recovery credential must not implicitly satisfy a separately locked app, folder, child folder or note. Restore onto replacement hardware requires an explicit re-enrollment ceremony with independent evidence of the last accepted state. If that evidence is unavailable, keep ordinary unlock closed and require a separately authorized recovery flow with the loss of freshness made visible.
2. Build an authenticated staged generation under exclusive migration ownership. Generate new random factors and re-encrypt the current ELK1/EMD2 objects whose security depends on them. Verify all updated envelopes and required child factors. Keep the accepted generation readable until the staged generation is complete and durably written.
3. Commit the independent epoch and digest by compare-and-advance from the exact previous anchor. A durable authenticated journal identifies both generations. After restart or an ambiguous commit, read the trusted anchor first. If it still selects the prior generation, keep the prior generation authoritative. If it selects the staged generation, finish activation only after verifying that generation against the committed digest. If the selected generation is missing or fails validation, close ordinary access and use the explicit recovery path. Never lower the epoch or pick a different digest on retry.
4. After verified activation, retire the old credentials for the current dataset. Manage old backups through separate retention and recovery rules. ELV1 password rewrap can retain a historical factor, and a copied historical ELV1/ELK1/EMD2 dataset and its historical factors remain available to an attacker who holds them. Fresh rekey protects the current generation once mandatory enforcement exists.

Acceptance requires independently protected anchor tests, crash injection around each durable and anchor transition, rollback and old-binary attempts, concurrent writers, fresh-factor dependency checks and recovery on independent Windows accounts and replacement hardware. NTFS ACL, power loss, disk-full, hostile junction TOCTOU and cross-process CAS still require targeted Windows acceptance.
