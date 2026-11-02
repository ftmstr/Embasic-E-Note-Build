# TUR 07 — Windows CI evidence and risk register (2026-11-02)

Repository: Public `ftmstr/Embasic-E-Note-Build`, MIT, stacked DRAFT PR #7,
base Tur 06 `fd2beb8657251e7c44e208570e8e472b7b4a72e6`.
`main` unmodified.

## Commits and scope

- `642b9eb6f5cd55de1fd7565b9048186cc0ad69e8`: experimental
  `LayerKeyVault` (independent Argon2id-wrapped factor/recovery password slots),
  `LayerVaultDiskStore` ciphertext-only `.elkey` create-only vault,
  `LayeredNoteDiskStore` ciphertext-only `ELK1` .emd create/replace with
  SHA-256 optimistic conflict detection; 37 Windows negative checks.
- `f0b3394c0a1893fd961311df566ea2d858f7b5e6`: five extra
  independent child/note credential tests.
- `7eaf229a42f355c7b8674c52167530593f514954`: correct expected
  cryptographic denial exception in one extra test.

## Real Windows CI and regression

- [41404656830](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41404656830):
  PASS, 0 warnings/errors, 135 old + 37 new = 172/172 automated.
- [41404980058](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41404980058):
  FAILED 41/42 Tur07, one test incorrectly expected ArgumentException.
  Observed CryptographicException: Wrong layer kind. This failure is retained
  as actual audit evidence and was corrected, not marked PASS.
- [41405104087](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41405104087):
  PASS Windows Release 0 warnings/errors, 17/17 workspace + 15/15 EMD1 +
  30/30 EMD2 + 35/35 recovery + 38/38 Tur06 + 42/42 Tur07 = **177/177 PASS**.
  Job log was read. This is headless Windows CI, not interactive GUI acceptance.

## Scope of new negative tests

Independent per-layer passwords/recovery, wrong password, wrong recovery,
child/note not implicitly unlocked by parent, wrong UUID/type, unsupported
version, ciphertext/tag tamper, truncated vault, short/same credentials,
vault factor rotation and old password rejection, Unicode note, create-only
encrypted vault files, missing vault, encrypted ELK1 .emd disk roundtrip,
DPAPI cannot directly read ELK1, tampered AEAD ciphertext, duplicate creation,
SHA-256 stale conflict and simulated external change, Windows exclusive
sharing lock, missing path, invalid extension, corrupted header, cleanup.

## NOT RUN / unfinished release gates

Real NTFS ACL write-denial, disk full, power-loss crash durability,
cross-process malicious file-swap/TOCTOU or race-free atomic CAS, real
separate Windows user/DPAPI, full workspace path symlink chain, true
adversarial multi-process edits, actual physical Windows GUI/DPI,
password/recovery UI, secure keyboard and memory audit, final SBOM/license
closure, cross-implementation interoperability test vectors, real
UYAP Editor UDF acceptance and signed Windows installer: **NOT RUN**.

`LayerVaultDiskStore` can create encrypted factor vault files, but there is
no production key-recovery orchestration, folder tree integration or
transactional rotation/rollback protocol. Rotation returns a new vault
envelope but cannot revoke **copies of old vaults**.
The new disk writer's SHA-256 pre-check is optimistic, not cross-process CAS.
It cannot guarantee crash durability and is not used by WPF.

**No source code was modified in WPF. Genuine light/dark brand logos kept
unchanged. New Secure Note (.emd) stays DISABLED. V1.0 RELEASE BLOCKED.**
Estimated engineering progress 21% is a non-measured planning judgment,
not a security or feature-completion percentage. Conditional planning
window 2027 Q2–Q3 is not a delivery commitment.
