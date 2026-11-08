# Tur 19 — Public Windows CI evidence
Date 2026-11-08. Public repo: ftmstr/Embasic-E-Note-Build.

## Starting verification
Google Drive Kaynak ENOTE-01/02/03/04/05/06/08/09 and
08.11.2026 Tur18 Rapor reviewed.
Public main SHA `a10faf48e7cd153a53803192e0ac7de39c998458`
unchanged. Draft PR #9/#10/#11/#13/#14/#15/#16/#17/#18/#19
OPEN; prior #12 CLOSED/NOT MERGED.
Tur18 final HEAD `19a4e2908c1d345b196096d1a6002af516f9f447`.
Tur18 final [Windows CI 42506424214](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42506424214)
job 128809941620 real logs inspected: 0 warnings/errors,
482/482 tests PASS.

Tur19 Draft PR #20 is stacked on Tur18, never merged into main.

## Tested source commit
`9f806cb8fc97734ce271101f04f155fb43b35f73`:
- `TrustedVaultOpenGate.cs`: optional BoundVaultPath on policy
  grant and new opt-in `UnlockPathBound` lexical canonical path
  check, with existing `Unlock` retaining historical semantics.
- `tests/Embasic.ENote.Tur19.Tests`: 25 checks including
  correct/recovery paths, clone refusal, canonical . and ..,
  Windows case handling, missing/relative/mismatched grants,
  bad workspace/lock/kind/epoch/credential, malformed/oversized
  encrypted vault and Windows FileShare.None.
- **Three intentionally PASS-ing bypass risk families:** cloned
  ciphertext with old APIs, rolled-back old grant and old
  vault with old password, direct historical EMD vault reader.
- Solution and GitHub Actions added Tur19 test target.

[Code HEAD Actions 42510423535](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42510423535),
Windows job 128822073999: SUCCESS, real log examined:
Build succeeded, **0 Warning(s), 0 Error(s)**;
prior **482/482** + Tur19 **25/25** = **507/507 PASS**.

## NOT RUN / BLOCKED
- This is path-string matching, NOT NTFS file ID or handle-bound
  identity. Hostile symlink/junction/TOCTOU, ACL-deny, cross-process
  CAS, disk-full, power loss, multi-Windows-account DPAPI NOT RUN.
- Genuine independent monotonic trust anchor NOT IMPLEMENTED.
  Old policy/epoch and old vault backup still work, legacy APIs
  still bypass. No root-key recovery or complete re-encryption.
- Physical WPF interactive GUI, DPI, keyboard/screen-reader, real
  printer/PDF, full Word/LibreOffice fidelity, and genuine UYAP
  unsigned editable UDF open/edit/resave/compare NOT RUN.
- WPF New Secure Note (.emd) DISABLED; original logos untouched,
  no new NuGet/paid API/cloud/account/ad SDK, no installer.
This documentation-only closure commit requires final HEAD CI
verification separately before claiming final PR success.
