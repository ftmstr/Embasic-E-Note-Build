# Embasic E-Note experimental encrypted formats — TUR 04

**Neither EMD1 nor EMD2 is a finalized production file format. The WPF `.emd`
New Secure Note control remains DISABLED.** These types are prototypes only.

## EMD1 (Tur 03, read prototype preserved)

- AES-256-GCM encrypted payload with header AAD, random file DEK.
- DPAPI CurrentUser wrapped DEK; optional random 256-bit recovery secret.
- No Argon2id or local disk persistence in the EMD1 prototype.
- Existing EMD1 test coverage retained in Tur 04.

## EMD2 (Tur 04 experimental portable prototype)

Little-endian layout, exact sizes:
- Magic ASCII `EMD2` (4), experimental schema version `2` (1),
  AES-256-GCM algorithm ID `1` (1), random file UUID (16).
- Argon2id salt (16), memory cost (Int32: 32768 KiB), passes
  (Int32: 3), lanes (UInt8: 1).
- Password key-wrap AES-GCM nonce (12), encrypted independent DEK (32),
  authentication tag (16).
- Recovery-slot presence flag (1); when set: independent random
  256-bit recovery secret, nonce (12), encrypted DEK (32), tag (16).
- Windows local-key slot flag (1); when set: 2-byte length and Windows
  DPAPI CurrentUser-wrapped DEK bytes (1–4096). No machine-wide DPAPI.
- Payload AES-GCM nonce (12), plaintext UTF-8 byte length (Int32),
  ciphertext (exact length), payload AES-GCM authentication tag (16).
- **All bytes preceding ciphertext are AAD**. Header changes fail authentication.
- Every new encryption chooses a fresh random 256-bit DEK, random payload
  nonce and salt. The password/recovery/local slots wrap the same DEK,
  not the plaintext.

The KDF has *fixed parameters* in this draft, rejecting unexpected values
before allocating memory. Users must provide a passphrase with at least
12 UTF-16 characters; this is NOT a proof of entropy or a substitute for
password strength guidance. Managed string copies cannot be reliably zeroed.
The KDF, nonce discipline, cryptographic interoperability and risk controls
require further independent review and reference vectors.

Experimental codec hard cap: 16 MiB UTF-8 payload. This limits *draft
note size*, not the number or depth of folders or notes.

## Encrypted-only disk writer

`EncryptedNoteDiskStore.Save(path, PortableEmdResult, expectedDigest)`
only accepts an opaque result produced by the experimental encryptor.
It does NOT accept arbitrary plaintext bytes. Filenames must end in `.emd`.
The writer creates a randomly named temporary file **in the same folder**
containing only encrypted bytes, flushes it and uses `File.Move` for
creation or `File.Replace` for replacement. On normal failure it deletes
the temporary encrypted file. Existing content requires an expected SHA-256
digest; stale digests fail without overwrite. Reading returns the encrypted
bytes plus SHA-256 for the caller to authenticate using the codec.

**IMPORTANT:** This is best-effort same-volume atomic replace, not
cross-process atomic compare-and-swap. The two digest checks and a
process-local lock do NOT prevent a hostile external process from swapping
files/links between checks. Reparse-point checks are not race-free.
Power-loss durability, Windows ACL-denial handling, backup recovery,
rollback/corruption recovery, antivirus interference and real Windows GUI
access are not fully accepted. Do not use it for real notes yet.

## Explicitly unfinished

- Portable password **migration and backup/recovery UI** with secure secret
  export, rotation, disaster-recovery acceptance on a different Windows user.
- Independently encrypted folder/subfolder/note locks and app lock.
- Version migration, cross-implementation known-answer Argon2id test vectors,
  guaranteed bounded memory/DoS mitigation, security review.
- Actual note editor, disk write activation in UI, real Windows GUI/DPI.
- UDF output, signed installer, release gates.

License: `Konscious.Security.Cryptography.Argon2` v1.3.1 MIT;
`System.Security.Cryptography.ProtectedData` v10.0.14 MIT.
