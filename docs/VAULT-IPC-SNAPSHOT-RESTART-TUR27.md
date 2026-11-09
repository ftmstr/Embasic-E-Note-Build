# Tur 27 authority IPC, snapshot enrollment and restart

W007 / SEC-EMD-05 and W012 release acceptance remain **BLOCKED**.
Secure WPF `.emd` creation stays **DISABLED**.

## Authority IPC and direct-access risk

The local named-pipe reference host owns the coordinator and the registered
publication store. The interface accepts one fixed-size, versioned request
per connection. Observe returns the selected stamp. Publish takes only an
exact candidate stamp and invokes server-side restart verification. There
are no raw reserve, commit, credential, path, key-return or legacy decrypt
commands. Separate HMAC role keys distinguish the operations. The host
challenge and client nonce bind the request to the current session, and
P-256 signatures bind replies to the request and challenge. Clients pin a
service public key and treat absent or unverified publication replies as
uncertain. An operation failure after dispatch may leave a previously
committed selection in place. Read back and retry only the exact candidate.

The experiment uses `CurrentUserOnly` and separate processes under the same
OS principal. That option is documented as limiting a pipe to the current
user and, on Windows, also checking elevation. Production account
separation needs explicit service installation and appropriate pipe and
file DACLs. The experimental host, service signing key, journal key and
provider writer require protected storage and configuration. Transport
request authentication alone does not protect the raw files or a same-user
process that already holds the local secrets. The negative child-process
test directly changes provider state and decrypts a legacy ELK1 with raw
credentials. The managed Linux executor denied Unix socket creation.
Its process restart tests use a direct test-host entry point; the Windows CI
suite exercises the actual named pipe. No installed Windows service, independent UID or physical ACL
boundary is claimed.

[.NET PipeOptions](https://learn.microsoft.com/dotnet/api/system.io.pipes.pipeoptions)
· [Named pipe security and access rights](https://learn.microsoft.com/windows/win32/ipc/named-pipe-security-and-access-rights)

## First catalog and source snapshot

Capture walks the supplied live root, copies all supported ELK1 `.emd`
files and verifies the enrolled ELV1 bytes. An inherited-only document
is included even when ELH1 has no explicit entry for that file. A missing
explicit lock, unsupported `.emd` container, linked path or changed
observation blocks capture. Plain Markdown is ignored by the encrypted
catalog. Capture uses a fresh sibling staging directory and a no-replace
directory move. First enrollment checks both frozen and live inputs before
selecting a v2 catalog manifest from the v1 generation. Existing grants
retain their digest, path and file identity. The full catalog digest is
derived from the verified index, paths and encrypted bytes. Repeated
enumeration and hashing detect changes observed by this protocol. The
configured root, filesystem traversal, concurrent writers and any
unobserved TOCTOU remain part of the threat model.

## Versioned targets and complete process restart

Prepared ELH1, ELK1 and ELV1 targets are rehashed as they are copied into
digest-named objects. Existing objects are reused only after verification.
The candidate uses the new physical ELV1 file path and, on Windows, the
identity from the same verified file handle. The ELV1 header must match
its factor identity. Linux uses an explicit synthetic identity in tests.
Old workspace files are left in place. The publisher records an HMAC
authenticated ERJ1 restart plan containing both exact stamps, changed
factors and target commitments. The separately retained journal key and
source root credential are required for a new process to reconstruct and
verify the plan. The candidate manifest is staged before plan publication.
An incomplete registration cannot be used by the IPC host.

On restart the host reopens the exact plan, source snapshot, live files,
staging and published objects. It verifies them before reservation, before
commit and when reconciling the exact committed receipt. The tests end the
host immediately before and after provider commit, and start a new host
from disk. Missing or modified artifacts prevent a success receipt. Once
the provider committed, later corruption does not rewind the selection.
This publishes changed artifacts and advances the experimental provider
pointer. Integration of all selected versions into the live application,
workspace-wide write isolation, target credential custody, cleanup of
unselected files and physical power-loss durability remain open.

## Root authorization archive and re-enrollment

ERA1 contains the exact old generation, authority revision and root proof.
It contains no root key, raw provider state or credential. A separate
trusted source must select the exact archive SHA-256 and approve one newer
vacant revision at the same generation. Re-enrollment compares that
tombstone and uses the live provider's generation-bound revision CAS.
Retry can recognize the exact resulting revision and proof. Stale, missing
or modified archives, changed approval and generation changes are rejected.
An archive cannot recreate a wholly missing provider state. Recovery of
the provider itself needs an independently trusted state and custody
procedure. Legacy encrypted recovery copies remain a separate risk.

Hardware CAS, physical power loss, NTFS ACL, disk full, hostile TOCTOU/CAS,
different Windows-user DPAPI, physical GUI/DPI, print/PDF and real UYAP UDF
open/edit/resave remain **NOT RUN**.
