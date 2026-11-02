# EMD1 experimental container (TUR 03)

**NOT PRODUCTION READY.** This is a draft codec for ENOTE-08, not a frozen
public format or an enabled note editor.

Binary little-endian format:
- ASCII magic EMD1 (4), draft version 1 (1), DPAPI-CurrentUser slot ID 1 (1).
- Random file UUID (16), wrapped DEK length (UInt16), DPAPI-wrapped key (1..4096).
- Recovery slot flag (1). Optional: recovery nonce (12), wrapped DEK (32),
  recovery authentication tag (16).
- Payload nonce (12), payload length (Int32), ciphertext (exact length),
  payload AES-256-GCM tag (16). No trailing bytes permitted.
- Payload AES-GCM authenticates all header bytes as AAD.
- A cryptographically random 256-bit DEK and 96-bit payload nonce are generated
  for every encryption. Recovery is a separate random 256-bit secret that
  wraps the DEK with independent AES-256-GCM.
- Hard 16 MiB draft content bound mitigates malicious allocations.
  It is not a product-wide arbitrary limit for folder depth/count.

The code acts on in-memory strings/byte arrays only. No filename or .emd file is
written. No portable Argon2id password mode, separate folder/note/app locks,
secure user backup UX or persisted key escrow exists. Losing Windows DPAPI
profile without a retained recovery secret causes permanent loss. Malware
running as that user may use DPAPI. Recovery secrets must never be logged or
sent to an ad component. External cryptographic review, Windows GUI, backup/
restore and disk atomicity are release gates. Do not turn on New Secure Note.
