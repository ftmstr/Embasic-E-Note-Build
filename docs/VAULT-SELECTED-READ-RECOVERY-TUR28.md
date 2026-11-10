# TUR 28 vault selection, service boundary and recovery

W007 / SEC-EMD-05 and W012 release acceptance stay **BLOCKED**.
WPF secure `.emd` creation remains **DISABLED**.

## Windows service endpoint and key epochs

The opt-in Windows endpoint has an explicit DACL for the service and client
SIDs. It denies remote connections, reserves the first pipe instance and
does not give the client the instance-creation bit. The client requests
specific data, attribute and synchronization rights. The original
same-user protocol remains available for its existing callers.

The CI acceptance harness registers a temporary SCM service as LocalService
and uses a client under the runner's distinct Windows account. It checks the
actual service SID, a successful signed observation, rejected reader-key
publication, denied instance creation and denied ordinary access to the
service key and vault file. The service input directory is protected. The
client pin lives in a separate directory readable by the service and the
service result uses a separate writable directory. Fixture credentials are
synthetic. The runner administrator can reconfigure its fixture. The
experiment does not establish an administrator-resistant boundary, a dedicated
production service SID or physical NTFS acceptance. The fixture uses
LocalService, which can be shared by other services.

The custody API accepts a signed workspace key epoch and independently
supplied expected epoch. It checks the exact SPKI and both role key digests.
Rotation requires the next epoch, a new signing key and two new role keys,
including no swaps. The owner disposes the endpoint before disposing
custody. No automatic reload, deployment-time credential provisioning,
production CNG key isolation or independent monotonic epoch store is claimed.

[Windows named pipe access rights](https://learn.microsoft.com/windows/win32/ipc/named-pipe-security-and-access-rights)
and [CreateNamedPipe](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-createnamedpipea).

## Selected catalog and cooperating writers

The read API starts from the authoritative selected stamp. The initial
catalog reads verified immutable snapshot bytes. A rekey catalog reconstructs
its exact ERJ1 commitment. Affected entries come from the hash-addressed
publication. Unaffected entries come from the verified source snapshot. The
API copies and hashes ciphertext before returning it, checks the selected
stamp again and rejects missing targets, tampering and noncatalog paths.
It never falls back to a live file or to the old version of an affected
entry. The current prototype resolves one snapshot-backed generation
transition and is not wired into the WPF application.

A protected journal-directory lock excludes cooperating live writers,
selection reads and publication. A child-process test holds it while a
second process attempts publication. Process exit releases the open handle.
All writers must explicitly use this fence, including the caller around
initial enrollment if it shares this path. Hostile writes, manual lock
file replacement and filesystem power loss remain outside the contract.

## ERJ1 backup and cleanup

ERJ1 export verifies the existing HMAC. Restore checks the same HMAC, exact
candidate, source manifest and either its current reservation or exact
committed receipt. Restore does not replace a differing registration. It
does not restore staging, published objects or a missing manifest.

Cleanup removes an authenticated losing sibling journal only after the
provider reports the exact committed winner at that epoch with no pending
transition. It retains the winner, source, manifests and shared objects.
This is deliberately not a general object collector. It relies on the
independent state and the cooperating writer fence.

## Provider loss and separately signed ERA1 approval

The recovery checkpoint binds the lost identity, a different replacement
identity, the exact selected manifest and a vacant authorization revision.
It requires a pinned offline P-256 approval and a separately trusted
current witness, checked again before provisioning. It creates a
tombstone under a new identity in empty storage and does not restore an
old pending transition, commit receipt, provider key or root proof.
ERA1 can then be re-enrolled only under a separately signed approval
for the exact archive hash, selected generation and vacant revision.
Missing manifests, altered signatures, stale witnesses and an occupied
replacement directory are tested. The witness must be current and
independent. The fixture witness is not a hardware or deployed trust
source. Replacement does not disable a surviving old provider.

Physical Windows security, hardware CAS, power loss, physical GUI/DPI,
print/PDF and real UYAP Editor UDF open/edit/resave remain **NOT RUN**.
Old copy revocation, hardware anti-rollback and atomic delivery remain open.
