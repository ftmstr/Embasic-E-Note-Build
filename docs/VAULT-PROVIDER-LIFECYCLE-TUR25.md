# Tur 25 vault transition provider, recovery lifecycle and rekey staging

W007 / SEC-EMD-05 and W012 release acceptance remain **BLOCKED**.
The provider is a protocol reference for cooperating processes. Secure WPF
`.emd` creation stays **DISABLED**.

## File provider and exact recovery

`VaultTransitionFilePrototype` takes an existing protected directory and a
separately supplied 32-byte key. No deployment, key provisioning, IPC or
service identity is installed. First use is an explicit epoch-one operation.
A missing record cannot be re-created while its original lock marker remains.
The marker is opened exclusively by each cooperating reader or writer. This
serializes reservation, exact commit, receipt publication and recovery
authorization. Contending operations may time out and must fail closed.

EVP1 is an experimental provider record, separate from existing encrypted
formats. Its 103–263 bytes include the workspace, exact current stamp, optional
pending candidate and last receipt, recovery revision and optional proof. HMAC
authenticates the entire record before parsing. Writes flush a new
same-directory file before replacing the previous record under the marker.
Commit publishes the new stamp, clears pending, records the exact receipt and
invalidates the preceding recovery authorization in a single record.

A normal process restart retains reservation and receipt. Injected ambiguous
acknowledgements are resolved from the provider state. Lost local manifest
data blocks resume. `RestageReservedCandidate` accepts the exact reserved
manifest only, verifies factor continuity and readback, and leaves the commit
for an explicit resume. No cancel, stale receipt history, automatic
re-provision or unverified fallback is added. The local manifest and EVP1
record are still separate writes.

The reference assumes non-hostile directory ownership and cooperating
processes on local Windows/Linux filesystems. The HMAC detects corruption
and substitution across workspace slots. An old authentic snapshot of the
provider record can roll back its state. The risk test demonstrates this even
with the original key and marker. No result proves directory-metadata
persistence across power loss, malicious path-swap resistance or physical CAS.

## Scoped key use

`IVaultKeyUseFence` requires a provider to check the exact generation and
exclude all of its transition writers until the scope ends. Windows
`UseFileBound` is opt-in. It loads the selected grant, acquires the fence,
then checks the enrolled file and decrypts while the fence is held. The
callback receives the key synchronously. The key is disposed before the
fence is released, including when the callback throws. Legacy unlock entry
points keep their previous behavior.

The file provider exercises the contract with independent cooperating
processes. An untrusted callback could copy or use material outside its
scope. Async callbacks and provider re-entry are outside the contract. No
cross-process plaintext or atomic application handoff is claimed.

## Recovery authorization lifecycle

Enrollment, replacement and revocation compare the exact generation and
revision. Revocation writes a higher revision and a tombstone. A generation
commit advances the revision and clears the old proof in the same record.
A pending transition blocks authorization mutation and restores. Root
recovery rechecks the whole authorization state so revoking and re-enrolling
the identical proof during an operation invalidates that attempt. It still
has a race after its last checks, and the policy cannot retract an old
offline ERB1 and its password. Production issuance and recovery of a lost
provider remain unimplemented.

## Rekey inventory and exact staging

The inventory opens the source ELH1 under the selected root and uses a
caller-supplied catalog of canonical workspace-relative ELK1 documents and
source hashes. It derives the affected factor chains, rejects omitted
explicitly locked notes, aliases and skipped locked ancestors, and requires
all affected factors to be enrolled in the source generation. The changed
factor vaults, affected notes and ELH1 when the root changes form a sorted
inventory.

Rekey staging verifies the entire source set before preparing anything and
requires one changed target commitment per artifact. It copies bounded
ELV1/ELH1/ELK1 ciphertext into immutable content-addressed names after checking
format signatures, bounds and exact digest. On retry, identical published
items are verified and reused. Missing items can be supplied later. A corrupt
occupied object fails closed. `RequirePrepared` checks the source and entire
prepared set without publishing the workspace.

The catalog has no proof of completeness for inherited-only documents that
the caller omits. The target commitment map must be retained and supplied
by a trusted caller, and is not bound to the provider's generation CAS in
this round. The stage checks do not authenticate the new key material or
prove its freshness by themselves. The fixture generates replacements using
the Tur 24 rekey APIs and verifies their readability. Live source isolation,
authenticated inventory, deterministic restart enrollment and publication
of the whole workspace remain open.

## Independent provider feasibility

| Approach | Open acceptance |
|---|---|
| File reference | Process restart and protocol testing. Snapshots remain replayable. |
| Separate local service | Service identity and ACLs can separate clients from authority files. Its own storage still needs a trusted freshness source, a release fence and fault recovery. |
| TPM-backed authority | Establish permitted TPM commands and privileges on supported Windows devices. A counter would require a reviewed binding to the exact pending digest and receipt, an interrupted-update recovery protocol and provider key lifecycle. The read-only capability probe does not exercise any of those. |
| Remote independent authority | Useful as a protocol comparison. A mandatory remote service conflicts with the product's offline operation requirement. |

See [Windows TPM command blocking](https://learn.microsoft.com/windows/win32/tbs/command-blocking)
and [FileStream.Flush](https://learn.microsoft.com/dotnet/api/system.io.filestream.flush).
Physical hardware CAS, power loss, NTFS ACL, disk full, symlink/junction races,
hostile CAS, different Windows-user DPAPI, physical GUI/DPI, print/PDF and
real UYAP UDF open/edit/resave remain **NOT RUN**.
