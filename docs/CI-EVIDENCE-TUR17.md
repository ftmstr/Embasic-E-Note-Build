# Tur 17 — Public GitHub Windows CI evidence

Date: 2026-11-08. Sole authorized Public repository
`ftmstr/Embasic-E-Note-Build`.

## Verified starting point
Google Drive Kaynak ENOTE-01,02,03,04,05,06,08,09 and
07.11.2026 Tur16 Rapor were read. Public main
`a10faf48e7cd153a53803192e0ac7de39c998458` unchanged.
Draft PRs #9,#10,#11,#13,#14,#15,#16,#17 confirmed OPEN/DRAFT.
Prior Tur16 HEAD `c5fc2a72acea637a5cd68ce39db975460e359dfe`.
[Tur16 final Actions 42412149290](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42412149290),
job 128522409436 SUCCESS, Windows Release 0 warnings/errors,
439/439 automated tests.

## Code commit and actual proof
`2d09026efc45da07e1ac859dddfc55dd316078b4`:
- `src/Embasic.ENote.Storage/TrustedVaultOpenGate.cs`:
  opt-in fail-closed external-policy contract, no actual protected
  storage, root recovery or revocation.
- `tests/Embasic.ENote.Tur17.Tests/Program.cs` plus
  project/solution/CI wiring: 23 regression cases including
  missing anchor, mismatched IDs/kind/epoch, old vault/credential
  rejection with current grant, tamper, truncation, actual Windows
  `FileShare.None` denial, and deliberate INSECURE old grant +
  old legacy API proof. This test class does NOT integrate WPF.

[Code Windows CI run 42483708395](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42483708395)
actual Windows job **128740908602**, logs inspected:
**Build succeeded; 0 Warning(s), 0 Error(s)**.
Baseline **439/439** plus Tur17 **23/23** = **462/462 PASS**.
A PASS for deliberately vulnerable old-policy/legacy scenarios
confirms those vulnerabilities persist; it is NOT a security pass.

## Documentation-only closure
README/NOTICE, `docs/ROLLBACK-GATE-DESIGN-TUR17.md`,
`docs/SECURITY-RISKS-TUR17.md`,
`docs/CI-EVIDENCE-TUR17.md`, `scripts/Show-ENoteStatus.ps1`
are updated in a separate commit. Final code+docs HEAD CI
requires an independent GitHub Actions run and log verification.

## NOT RUN/BLOCKED
No actual hardware monotonic anchor. No physical Windows GUI/DPI/
accessibility; cross-user DPAPI; malicious NTFS ACL denial, disk-full,
power outage, adversarial cross-process CAS, junction/symlink/TOCTOU;
true root key recovery/rotation and third-party crypto audit; actual
Windows printing/PDF or genuine UYAP Editor unsigned editable UDF
open/edit/resave/compare. Existing RTF copy-only and guarded DOCX/
ODT simple-paragraph engines, original Embasic/E-Note logos and
offline Markdown source preservation were untouched.
No added NuGet/paid API/cloud/account. No signed installer.
V1.0 remains BLOCKED.
