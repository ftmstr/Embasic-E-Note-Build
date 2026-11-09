# Tur 21 — Committed vault generation and recovery protocol

Status **REVIEW**. W007 / SEC-EMD-05 and V1.0 remain **BLOCKED**.

## Commitment and selection

`VaultGenerationStamp` binds a workspace GUID, positive epoch and SHA-256 manifest digest. `VaultGenerationManifest` commits the enrolled factor GUID and kind, exact normalized path, Windows volume serial and 128-bit file ID, and ELV1 ciphertext SHA-256 for each factor. The pin epoch and minimum epoch must both equal the manifest epoch. Factor GUIDs, exact paths and Windows file identities must be unique within a manifest. Ordinary migration may add grants and must retain each existing factor GUID and kind. An authorized factor-removal policy is a separate design decision.

The experimental digest uses a length-prefixed UTF-8 domain string `Embasic.ENote.VaultGeneration.1`, the .NET GUID byte representation, a little-endian Int64 epoch and Int32 count, then entries in .NET GUID comparison order. Each entry uses GUID bytes, the one-byte factor kind, a length-prefixed UTF-8 normalized path, three little-endian UInt64 Windows identity fields and 32 raw digest bytes. This is a commitment definition for the prototype. A persistent manifest format, bounded decoder and enrollment/import flow are pending.

`VaultGenerationRecovery` selects the exact manifest named by the independent anchor and refuses missing or mismatched data. It checks the anchor again after loading the manifest. An opt-in Windows caller can pass the coordinator to `TrustedVaultOpenGate` and use `UnlockFileBound`. The existing gate checks the registered file identity and pinned ciphertext. A concurrent advance after grant selection can still race with key extraction. A production unlock and key release need a verified freshness or lease protocol.

## Stage and compare-and-advance

1. Read the independent current stamp. Require the candidate to be the next epoch of the same workspace. If the expected stamp differs, use exact readback to identify an already committed candidate or a competing result.
2. Verify the prior manifest and factor continuity. Stage the candidate through the local store and verify an exact readback before requesting CAS.
3. Compare-and-advance the full expected stamp. After a normal reply or a specifically reported uncertain outcome, read the independent anchor and verify the selected generation. A new digest at the expected epoch or a lower epoch fails closed.
4. Return `Committed`, `PriorObserved` or `Superseded`. `PriorObserved` is a point-in-time observation. An outstanding CAS can still complete. Retry with the same expected stamp and candidate and preserve all staged data until an independently verified cleanup policy is available. A missing committed manifest blocks opening even if an older generation remains accessible locally.

The production adapter must supply linearizable reads and atomic full-stamp CAS independently of the local data. The local store must retain immutable, independently addressable generations and durably stage their content before CAS. Durable stage and the authenticity of the candidate submission are adapter responsibilities. No real hardware anchor, persistent generation store, durable retry intent or power-loss evidence ships in Tur 21. Crash consistency requires explicit storage assumptions and real testing. [SQLite atomic commit discussion](https://www.sqlite.org/atomiccommit.html)

## Risk and next acceptance

Tests exercise fake and rolled-back policies, exact digest mismatch, staged loss, interruptions before and after CAS, delayed completion and competing candidates. A deliberate risk case shows that a rolled-back anchor, matching local manifest and old ELV1 at the same Windows file identity still allow the old credential. The portable CI branch uses the existing pinned gate when Windows file identity is unavailable.

For the next acceptance increment, verify a bounded manifest decoder that checks the selected digest before creating policy grants, then validate a durable generation store and crash-restart readback. Verify root credential recovery independently, bind all ELK1/EMD2 dependents and implement fresh-factor rekey. Physical Windows ACL, disk-full, power loss, hostile junction TOCTOU and cross-process CAS, different-user DPAPI and actual UYAP remain NOT RUN. WPF secure `.emd` is disabled.
