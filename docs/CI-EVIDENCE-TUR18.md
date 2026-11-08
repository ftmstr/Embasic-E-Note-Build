# Tur 18 — Inspectable Windows CI evidence (2026-11-08)

Public repository only: `ftmstr/Embasic-E-Note-Build`.
Google Drive ENOTE-01/02/03/04/05/06/08/09 and Tur17 technical
report inspected. Public main unchanged:
`a10faf48e7cd153a53803192e0ac7de39c998458`.
Verified Draft PRs #9/#10/#11/#13/#14/#15/#16/#17/#18 OPEN,
PR #12 CLOSED/NOT MERGED. Parent Tur17 final commit:
`4b05e4367a43044aa236dfd235b6c8a6c302626e`.
Tur17 [Windows CI 42484115208](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42484115208)
job 128742146476: SUCCESS, 0 warnings/errors, 462/462 PASS.

Tur18 new stacked [Draft PR #19](https://github.com/ftmstr/Embasic-E-Note-Build/pull/19)
is based on the Tur17 HEAD, with Public main unmerged.

## Tested code commit
`5bf7b3bb2c562ecf0a99656006ed074ce5e893c9`:
- `LayerVaultDiskStore.UnlockPinned`: exact ELV1 envelope length check
  BEFORE allocation, FileStream FileShare.Read, ReadExactly, buffer
  zeroization in finally. Only this *opt-in* guarded path changed;
  legacy unguarded read remains.
- `tests/Embasic.ENote.Tur18.Tests`: 20 checks for valid password and
  independent recovery slot; stale/zero epoch, null pin, Windows
  sharing-lock conflict; 0/1/173/175/2,097,152-byte and corrupted
  ciphertext; missing file, wrong extension.
- Intentional **INSECURE proof tests** confirm same-byte ciphertext at
  a different `.elkey` path still opens, old pin/epoch+old ciphertext
  still opens, and legacy disk/crypto APIs still open the old vault.
  These tests PASS while indicating unresolved security vulnerabilities.

[**Code HEAD Windows CI 42506028582**](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42506028582),
job 128808747399: actual logs inspected. Windows WPF Release
**Build succeeded; 0 Warning(s), 0 Error(s)**. Baseline 462/462
plus Tur18 **20/20** = **482/482 PASS**.

## Source protection / release blockers
- Stronger bounded read is not a proof of race-free handle/path identity
  or cross-process atomic CAS. There is still Validate(path) then
  FileStream(path), which is exposed to reparse/junction/symlink
  swaps under a hostile local adversary. Actual targeted Windows
  symlink/ACL/disk-full/power-loss tests NOT RUN.
- `VaultSnapshotPin` trust anchor not implemented; only a caller's
  asserted epoch. `TrustedVaultOpenGate` opt-in only. Old backup
  copies + old password still decrypt; legacy APIs bypass.
- LayerSecret rotation rewraps the SAME factor; no retroactive
  key revocation. Current ELK1/EMD2 full rekey, root-key recovery,
  independent nested locks and rollback-resistant recovery incomplete.
- Real WPF GUI/DPI/accessibility, full Word/UYAP editable WYSIWYG
  and complex DOCX/ODT/RTF comparison, printer/PDF, UDFWriter and
  real UYAP unsigned editable UDF open/edit/resave/compare NOT RUN.
- Full SBOM, trademark/font/icon rights and release installer
  security acceptance NOT RUN/BLOCKED.
- `New Secure Note (.emd)` remains DISABLED. No installer uploaded.

Documentation closure commit and final CI tracked separately.
