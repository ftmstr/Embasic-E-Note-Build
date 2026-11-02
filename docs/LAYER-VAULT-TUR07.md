# Experimental ELV1 layer factor vault, ELK1 persistence — Tur 07

Not a production format or a user-facing secure folder feature.
Required security authority: ENOTE-08; architecture ENOTE-04; plan ENOTE-05.

## Per-layer factors

Each folder, separately locked subfolder, and note-additional lock uses its
own random 256-bit factor. `LayerKeyVault` writes that factor as a versioned
password-wrapped `ELV1` binary, with two **alternative credentials for that
same layer only**: layer password and a separate layer recovery password.
Each slot has independently random 16-byte salt and 12-byte nonce, Argon2id
32 MiB/3 passes/1 lane, AES-256-GCM, 16-byte tag. There is deliberately
**NO unconditional Windows DPAPI slot in ELV1**. This does not mean that
DPAPI is removed from the inner EMD2; EMD2 is shielded by all required
outer ELK1 encryption layers.

ELV1 draft layout (174 bytes): ASCII magic `ELV1` (4), version (1),
layer-kind (1), random factor UUID (16), then two 76-byte wrapped slots:
salt (16), nonce (12), ciphertext of the 32-byte factor (32), tag (16).
GCM AAD consists of the common 22-byte prefix plus slot discriminator
1 or 2. Wrong ID, kind, password, recovery, AEAD tag, version or size
is rejected. Password length >= 12 characters is only a crude guard,
not an entropy proof. Password strings are managed and not guaranteed
zeroed in memory.

ELV1 files may be saved as **only encrypted** `.elkey` files, create-only
with flush and same-folder temporary ciphertext. Key factor `LayerSecret`
supports Dispose/ZeroMemory. Each layer still needs separate authorization,
even where the same human deliberately reuses passwords.

## Locked note storage

`LayeredNoteDiskStore.Save` requires `PortableEmdResult` and the
authorized outer-to-inner `LayerSecret` collection. It first encrypts
the **whole EMD2**, including its DPAPI/recovery slots, as ELK1 layers
and writes only this ciphertext to an `.emd` target, never a plaintext
temp. Same-directory `.elk-tmp`, flushToDisk and File.Move/create-only
or File.Replace/explicit expected SHA-256 version are used. The caller
must authenticate ELK1 with `LayeredEmd3.Open` after `Read`; a magic
prefix alone is **NOT** proof of AEAD validity. No plaintext is ever
returned from `Read`.

## Rotations and recovery

`LayerKeyVault.Rotate` authenticates the existing factor using exactly
one of its authorized credentials and produces fresh encrypted slots,
preserving the secret factor, ID and kind without mutating original bytes.
A rollback copy of an older vault retains its former credential; this is
NOT revocation until all old copies are addressed. Full independent
folder key rotation would require safe nested ELK1 re-encryption and
transactional recoverability; this workflow is not yet implemented.

Lost keys without valid recovery credentials are irrecoverable. Password
backup and UI are absent; this is a storage and cryptography prototype.

## Security limits / release gate

- No tested NTFS ACL denial, disk-full or power-loss durability.
- No handle-bound path validation, safe directory-junction handling,
  or cross-process transactional compare-and-swap. Current digest checks
  and process-local locks reduce ordinary conflict but not malicious races.
- No binding to actual Windows folder tree, no secure key hierarchy
  enrollment, recovery UX or physical GUI.
- EMD1/EMD2/ERB1/ELK1/ELV1 remain experimental and cannot be treated
  as finalized production encrypted document formats.
- WPF `.emd` new note remains disabled; UDF, export and final Windows
  installer acceptance remain blocked.

Source PR #7: https://github.com/ftmstr/Embasic-E-Note-Build/pull/7.
Windows CI reference: https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41405104087.
