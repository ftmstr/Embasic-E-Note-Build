# Tur 26 provider identity, scoped reads and bound rekey

W007 / SEC-EMD-05 and W012 release acceptance remain **BLOCKED**.
WPF secure `.emd` creation stays **DISABLED**.

## W007 provider identity and access boundary

Opt-in pinned mode writes EVP2. The HMAC covers the provisioned provider GUID
in addition to the workspace and transition record. The expected GUID and
32-byte key must come from protected configuration. A different provider
GUID fails even with the same key. EVP1 and EVP2 do not silently downgrade
or convert. Pinned mode rejects already linked directory ancestors and
linked marker or state paths during its checks. These are path checks,
without a handle-relative traversal or physical ACL acceptance.

The pinned file prototype still admits an authentic old snapshot for its
own provider identity. A same-identity clone can present the old record.
The test suite deliberately reproduces rollback and direct access by a caller
that can issue trusted provider writes. A provider GUID is not a service
principal or attestation.

### Candidate production boundary

- A dedicated local Windows service can run with a restricted service SID.
  Use separate owners and DACLs for service configuration, binaries, state,
  backups and named pipes. Client access should allow only required operations
  for authorized principals. Generic pipe write access needs particular care
  because the pipe-instance creation right shares an access bit with append.
  Reject remote clients. Authenticate the real server and client for every
  channel and bind the operation, workspace, expected stamp and nonce to the
  authenticated exchange. Never trust a caller-supplied SID, path, GUID or PID.
- Separate client permissions for operation calls, authorization enrollment and
  transition administration. Keep the raw provider writer and vault keys inside
  the service. A client with direct write access can skip staging verification.
  An administrator, service-owner takeover or restored system image remains a
  distinct threat. A service ACL alone supplies no independently monotonic CAS.
- Evaluate TPM NV policy and usable commands on supported physical machines.
  Windows TBS filters TPM command access. A monotonic counter must still bind
  the exact manifest digest, pending transition, authorization revision and
  durable receipt through interrupted writes. A TPM query or counter read
  does not exercise that protocol.

[Service security and access rights](https://learn.microsoft.com/windows/win32/services/service-security-and-access-rights)
· [Restricted service SID](https://learn.microsoft.com/openspecs/windows_protocols/ms-scmr/ea1a9acd-4bb2-473f-ae5b-55969c5960fc)
· [Named pipe access rights](https://learn.microsoft.com/windows/win32/ipc/named-pipe-security-and-access-rights)
· [TPM command blocking](https://learn.microsoft.com/windows/win32/tbs/command-blocking).

The service and its IPC are feasibility work. No service was installed. The
executor maps one Linux UID and could not run a distinct-principal ACL test.
Provider link checks ran on Linux. Physical Windows ACL and TOCTOU remain
NOT RUN.

## W007 scoped key access

Operation-only mode rejects the three raw unlock entry points and the
raw-key callback on the configured gate. The built-in Windows ELK1
operation obtains the generation fence, authenticates the exact enrolled
vault and performs the read using the scoped key. It returns Markdown and
disposes the key before releasing the fence. Extra factors must be supplied
separately in order. Other legacy gate instances and direct ELV1/ELK1
readers remain usable when ciphertext and credentials are accessible. The
risk test proves that bypass. No atomic application handoff is claimed.

## W012 root authorization and ambiguous outcomes

The file provider can read the authorization revision and selected generation
in one lock scope. After a lost acknowledgement, the helper reports applied
only if the same quiescent generation observes the exact next revision and
requested proof or tombstone. The prior revision is an observation. There
is no automatic retry. A pending advance, a later or competing revision,
or a changed generation blocks the result. A generation advance cannot be
misread as revocation when it advances the revision and clears the proof.

Losing the authority state causes restore and re-enrollment to fail closed.
The existing marker prevents silent first-use provisioning. Recovery from
a lost provider needs separate policy and trusted backup. Historical ERB1
copies and their passwords remain usable.

## W012 catalog-bound rekey

Version 2 of the experimental generation manifest commits to a canonical
catalog and may also commit to a rekey plan. Existing version 1 encoding
and decoding are preserved. The catalog digest covers workspace, root,
source ELH1 bytes and every declared document path and ciphertext digest.
Inventory creation recomputes the digest from the verified ELH1 and catalog.
This detects removal of a previously enrolled inherited-only document,
even when ELH1 itself contains no explicit entry for it. An incomplete
catalog enrolled at source is still accepted by the authority boundary.
A risk test preserves this limitation.

The destination commitment covers the exact source stamp, source and
destination catalog hashes, affected item identities, factor order and
source/target digests. The candidate verifies changed ELV1 pins and retains
unchanged vault pins, paths and file identities. A bound source can advance
only through the bound rekey API at this coordinator. Legacy v1 transitions
keep their behavior. Catalog enrollment or ordinary catalog update migration
is outside this prototype.

The coordinator verifies the exact prepared set before reserving, before
committing and when resolving its committed receipt. A lost artifact blocks
the pending commit until the same prepared bytes are restored. A deleted
artifact after commit makes reconciliation fail closed without reverting
the provider's committed record. The tests inject both missing data and
ambiguous acknowledgements. A caller with direct authority access can
still bypass the coordinator. File checks, provider mutation and workspace
publication are distinct operations. Full inventory collection, local
snapshot isolation, atomic promotion and physical durability remain open.

Physical CAS, power loss, NTFS ACL, disk full, symlink/junction TOCTOU,
hostile CAS, different Windows-user DPAPI, physical GUI/DPI, print/PDF
and real UYAP UDF open/edit/resave remain **NOT RUN**.
