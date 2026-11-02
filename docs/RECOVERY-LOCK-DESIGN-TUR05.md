# Embasic E-Note — Tur 05 recovery and lock hierarchy (design draft)
Date: 2026-11-02. Status: REVIEW. **Not a user security feature.**

## Experimental recovery-backup contract (ERB1)

`RecoveryBackupCodec` takes an **already-authorized 32-byte EMD2 recovery secret** and
an independent backup passphrase and creates password-encrypted backup bytes in
memory. There is no file chooser, backup provisioning, cloud syncing, clipboard,
password prompt or persistent user key store. Never persist the raw secret as a
plaintext file. A saved ERB1 binary may be stored offline, but its passphrase
must be kept separately; forgetting it without other unlock methods can be
unrecoverable.

The encrypted backup consists of: `ERB1` magic, version 1, 16-byte random
Argon2id salt, fixed 32 MiB / 3 passes / 1 lane parameters, 12-byte nonce,
32-byte encrypted recovery key and 16-byte AES-256-GCM tag. All preceding
header bytes are AAD. Exact 90 bytes. Future schema changes require a new
version. The backup is encrypted with a **separate Argon2id password**;
the raw 32-byte recovery secret is not written by this codec. Argon2id
password quality is not proven by a simple 12-character length threshold.

## Key rotation and migration

`PortableEmd.RotatePassword` authenticates/decrypts EMD2 using exactly one
current credential; `PortableEmd.Encrypt` creates a **new document UUID, file
DEK, salt, recovery secret and nonces** using the next password. This is
full-content re-encryption, **not** a header-only password rewrap. The original
`.emd` byte span is unchanged. The new recovery key must be protected and
backed up afresh before using the rotated note.

`PortableEmd.MigrateDraftV1` requires an explicitly authenticated legacy
EMD1 in-memory envelope and creates new experimental EMD2 bytes. No implicit
migration, no overwrite, no unchanged-key promise, and no guarantee of
future format compatibility. Migration only becomes a supported workflow after
tests for backup, rollback, original preservation and actual Windows UI pass.

## Independent locks: cryptographic requirement vs planning checks

`LockGatePlan` is an **authorization-planning helper**, not a cryptographic
lock, and MUST NOT be relied upon to protect disk contents. Its four distinct
gate identifiers can represent application UI, folder, subfolder and note-extra
locks. A parent folder grant does not imply a child or note grant. The
application UI lock is separately defined and cannot turn plain `.md` into
encrypted data.

**Future cryptographic enforcement requirement:** each folder must have its
own random key (FK). Parent unlock yields only the parent key, never a separately
locked child key without its own authorized wrapping factor. A child that
requires both parent and independent password must use a wrapping operation
that cryptographically requires *both*. A locked note also requires its
independent wrapping factor in addition to ancestor grants. An unconditionally
available DPAPI or password slot that bypasses this key hierarchy would be a
security defect: EMD2's current alternative slots do NOT implement these locks.
The format/schema and key envelope will therefore need review and versioning
before folder locking is enabled.

## Windows and filesystem threat model: still open

A single process-local lock, SHA-256 pre/post check and `File.Replace` are
not OS-wide compare-and-swap. A hostile process can race path swaps. ACL
denials, directory junction races, disk-full, power loss, antivirus,
cross-process editing, backup rollback and recovery are NOT RUN in full.
The new test runner exercises a Windows exclusive file handle and confirms
an ordinary sharing violation does not change ciphertext. That is **NOT**
equivalent to a real NTFS ACL test or malicious race test. A simulated
`DeniedProtector` does not substitute for a separate Windows account.

Secrets live transiently in .NET managed strings/arrays and could appear in
memory dumps. PortableEmdResult keeps a private recovery-secret copy and does
not yet support explicit destruction; this is an open security hardening item.
Need secret lifetime controls, independent crypto review, non-UI disk escrow
policy, offline bootstrapped recovery, and physical Windows acceptance.

## Evidence and release status

Tur 05 source commit `7ea2f25ab170c6e0bf70cd0cc40607702d46e39a`.
Windows CI: https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41386156447
— Release 0 warnings/errors, 17/17 workspace + 15/15 EMD1 +
30/30 portable/atomic + 35/35 recovery/rotation/migration/policy =
**97/97 automated PASS**. This is **NOT** a complete secure editor pass.
WPF New Secure Note remains disabled; real UYAP UDF, installer and Windows
GUI acceptance remain NOT RUN. Experimental formats NOT frozen.
