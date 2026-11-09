# Tur 23 — Authenticated retry intent and last-observed generation

Status **REVIEW**. W007 / SEC-EMD-05 and V1.0 remain **BLOCKED**.

## Record and replay rules

The optional retry journal stores one authenticated transition per workspace and expected epoch. The fixed 136-byte record includes a version domain, workspace GUID, expected and candidate epochs, both SHA-256 commitments, and an HMAC-SHA-256 tag. The candidate must be the next epoch in the same workspace. Reads bound length and verify the HMAC before interpreting fields, then verify the workspace and epoch against the requested slot. The 32-byte key must be supplied by independently protected configuration. The prototype copies it into memory and zeroes that copy when disposed. No production key provisioning is included. [HMACSHA256 .NET API](https://learn.microsoft.com/dotnet/api/system.security.cryptography.hmacsha256)

The retry record and Tur 22 manifest share the same no-replace local publication primitive. A competing authenticated candidate for an occupied slot is refused. The coordinator verifies the staged manifest, publishes and re-reads the exact intent, then calls the external compare-and-advance interface. A restart uses `Resume(expected)`, which requires that record and the exact staged candidate if the anchor still selects the expected generation. If the anchor already moved, the coordinator verifies the selected manifest and resolves the outcome without repeating CAS.

The protected directory remains a requirement. Corruption and cross-slot replay can be detected. Deleting a local record is detected by explicit resume, but an explicit fresh start cannot distinguish deletion from first use. The tests preserve this negative result. Restoring an authentic same-slot record from a separately authorized journal is also outside the guarantees of one local directory. A production design must protect intent independently or bind it to the trusted authority. Do not treat HMAC, a local directory, or the fixture anchor as a monotonic mechanism. Disk flush and process races leave physical power-loss durability open.

## Windows key delivery

For Windows file-bound unlocks backed by the generation coordinator, the existing ELV1 and file-identity checks run with the selected manifest. The gate re-reads the independent stamp after obtaining the key and before returning it. A changed or unreadable stamp aborts the unlock and disposes the key. This narrows the race window. An advance can still occur after that read. Atomic release requires an independently verified lease, operation fence, or trusted release protocol. Legacy callers without the generation coordinator retain their existing contract.

## Hardware and recovery acceptance

The read-only PowerShell preflight reports only TPM presence and readiness. It discards owner authorization and identifiers. TPM discovery does not exercise monotonic storage or CAS. Windows TBS can restrict TPM commands, and key storage alone does not prove rollback protection. [Get-Tpm](https://learn.microsoft.com/powershell/module/trustedplatformmodule/get-tpm) · [Windows TBS command blocking](https://learn.microsoft.com/windows/win32/tbs/command-blocking)

| Acceptance item | Required evidence |
|---|---|
| Independent anchor | Demonstrate protected epoch plus manifest digest, exactly one winner for competing candidates, explicit lost response and post-reboot readback. Verify allowed commands and privilege on supported physical Windows devices. |
| Root recovery | Verify recovery of an authenticated generation with the old device unavailable. Reject missing, mismatched and revoked recovery authorization without falling back to an older generation. |
| Fresh-factor rekey | Prove every dependent ELK1/EMD2 object is rewrapped or rebuilt before advance, and verify old and new credentials at each transition. Historical copies remain a separate acceptance gate. |
| Physical storage and release | Test real power loss, disk full, NTFS ACL and hostile path races, different-user DPAPI, physical GUI/DPI, print/PDF and real UYAP UDF. |

The hardware, root recovery and physical rows remain NOT RUN or incomplete. WPF secure `.emd` creation is disabled.
