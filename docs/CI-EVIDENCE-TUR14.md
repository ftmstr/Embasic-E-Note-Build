# Tur 14 — Verified Public GitHub Windows CI evidence

Date: **2026-11-06**. Repository **Public only**:
`ftmstr/Embasic-E-Note-Build`, `main` unchanged at
`a10faf48e7cd153a53803192e0ac7de39c998458`.
Google Drive Kaynak ENOTE-01/02/03/04/05/06/08/09 and Tur13 Rapor
were inspected before changes. Tur13 verified HEAD:
`c6330120ac37960d877215988ef04faad9c97caf`;
[Tur13 CI 42173857028](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42173857028)
SUCCESS, 345/345 tests.

[Tur14 Draft PR #15](https://github.com/ftmstr/Embasic-E-Note-Build/pull/15)
is stacked on Tur13 PR #14, not merged into main. Earlier Draft PRs
#9, #10, #11, #13, #14 remain intact. Original light/dark Embasic
and new E-Note icon blobs have not been replaced.

## Actual commits and trace
- `2c08b2f1354f4cf4f659e0261cbfb8e890c854cb`:
  bounded Core nested-inlines parser, 20 new tests, test project + CI.
  **CI run 42258944212 FAILED**, 1 compiler error CS1628.
- `5a3fa5643468a64ed41618b5f040ab0543642664`:
  WPF inert Span/Run nested display, structured Markdown table cells,
  escapes/backtick runs/tabs and malformed-column literal fallback,
  8 further negative cases. **CI 42259319855 FAILED** due to same CS1628
  issue; failures are recorded rather than suppressed.
- `c907fe9843a47d9251487e37696cfa6bfae62d93`:
  fixes C# CS1628 by replacing captured `ref` variable with shared
  `NodeBudget` reference. [CI 42259573972](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42259573972)
  SUCCESS on this code HEAD. Real job **128035656174** logs inspected:
  **Build succeeded, 0 Warning(s), 0 Error(s)**.
- Documentation-only closure commit: this file, README, NOTICE,
  security risk register, PowerShell status script. Final-HEAD CI
  should be checked independently and appended to Drive Rapor.

## Verified cumulative checks on code HEAD
| Suite | Result |
|---|---:|
| Workspace/paper | 31/31 |
| EMD1 | 15/15 |
| EMD2/storage | 30/30 |
| Recovery/rotation | 35/35 |
| Tur06 | 38/38 |
| Tur07 | 42/42 |
| Tur08 | 27/27 |
| Tur09 plaintext Markdown | 31/31 |
| Tur10 RTF guarded copy | 16/16 |
| Tur11 DOCX/ODT guarded copy | 32/32 |
| Tur12 basic inert Markdown | 21/21 |
| Tur13 inline styles | 27/27 |
| Tur14 nested/table | 28/28 |
| **TOTAL** | **373/373 PASS** |

Tur14 parser supports **depth 8** and **node budget 4096** in
*preview only*; these are DoS safeguards, not restrictions on number
of user notes, workspace folders, or source Markdown size. Testing
covers Unicode, nested emphasis/code/strike, malicious literal HTML,
remote URLs/images, malformed delimiters, table odd/even slash escapes,
multiple-backtick pipes, tabs and oversized previews.

## Important non-claims
No actual WPF interactive GUI/DPI, keyboard/screen-reader or visual
regression acceptance; no native printer/PDF/DOCX/RTF/UDF export acceptance
or real UYAP editable unsigned UDF open/save/compare. **NOT RUN**.
Old encrypted vault backup rollback, root key loss/recovery, malicious
symlink/TOCTOU and OS-wide atomic CAS remain unsolved; NTFS ACL,
disk-full, power-loss, different-Windows-account DPAPI testing **NOT RUN**.
Real WPF `.emd` New Secure Note **DISABLED**. Product V1.0 **BLOCKED**.
