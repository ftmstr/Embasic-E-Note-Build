# Tur 22 — Verified generation files and disk staging

Status **REVIEW**. W007 / SEC-EMD-05 and V1.0 remain **BLOCKED**.

## Manifest verification

The experimental store uses the Tur 21 canonical V1 commitment unchanged. A separate encoding fixture checks equality with those exact bytes. The reader snapshots the remaining bytes of a readable, seekable stream and rejects lengths over 16 MiB before allocating. It verifies SHA-256 over all snapshot bytes against the supplied stamp before parsing or restoring any grant. It then checks workspace, epoch, feasible entry count, strict UTF-8 paths of at most 128 KiB encoded bytes, and the existing unique factor, path and identity rules. Trailing bytes and any input that does not reconstruct the same canonical commitment fail closed. These are manifest transport bounds for the experimental store.

Verified entry digests are imported through an internal storage-to-EMD bridge without requesting vault credentials. This does not open ELV1. The normal vault pin, AEAD and, on the Windows path, file identity checks still run at actual unlock. The committed stamp must come from an independently trusted provider. Anyone able to replace that provider's stamp can supply a matching historical manifest.

## Publication and recovery

An existing protected local directory is required. Each final name is derived from the full workspace GUID, epoch and manifest SHA-256. Competing digests at one epoch have distinct names. The store never searches another file when the selected stamp is missing.

A staged manifest is written to a new temporary file in the same directory with write-through and `Flush(true)`. The Windows branch publishes with `MoveFileExW` without copy or replace flags. Linux publishes using a no-replace `link` and unlinks its temporary name. Both then verify an exact readback. A concurrent publication of the same bytes is idempotent. An occupied final name with different or incomplete bytes fails closed and is preserved. Orphan temporary files are ignored. Automatic cleanup would need a separate way to distinguish interrupted writers from active writers. [Win32 MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw) · [POSIX link](https://pubs.opengroup.org/onlinepubs/9799919799/functions/link.html) · [.NET file flush](https://learn.microsoft.com/dotnet/api/system.io.filestream.flush)

The directory and final path receive attribute checks. This does not provide handle-bound ancestor traversal, a proof of a regular file against hostile namespace mutation, or a filesystem security boundary. Manifest fields include absolute paths and file identities as plaintext metadata. The deployment must protect the directory from untrusted writers and readers. Publication assumes a supported local filesystem. No cross-volume copy fallback is used. File data flush, same-directory publication and process-reopen tests do not certify parent-directory metadata or physical power-loss behavior.

## Acceptance scope

Tests use real files for process reopen, same-stamp contention between separate processes and corrupt final files. A child fixture that writes a partial staging name is terminated before publication. That test proves how the real reader treats an orphan. It does not inject a power loss or kill the production writer inside its native publication call. The lost-CAS test combines the real disk store with an in-memory anchor. The coordinator fails closed if the selected file is missing or corrupt.

The independent monotonic provider, protected durable retry intent, trusted recovery enrollment, root key recovery, factor rekey and final freshness at key release remain pending. Physical Windows ACL, disk full, actual power loss, hostile reparse TOCTOU and independent hardware CAS, different-user DPAPI, physical WPF GUI/DPI, print/PDF and real UYAP UDF remain NOT RUN. WPF secure `.emd` creation is disabled.
