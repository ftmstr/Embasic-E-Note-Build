# Tur 15 — Public Windows CI proof and failed-closed boundaries

Date: 2026-11-06, repo: Public ftmstr/Embasic-E-Note-Build.
Public main verified at a10faf48e7cd153a53803192e0ac7de39c998458,
untouched. Earlier PR #9, #10, #11, #13, #14 and #15 all OPEN and
Draft at recorded heads. Tur14 HEAD 01690b193df14e4245b322a9d3aa0eef92719da0,
Windows run 42260282437 confirmed 373/373 PASS, 0 errors/warnings.

## Small public commits (Draft PR #16)
- a75c4a09d0b59d11f435e5b51f7809508ed5d653:
  Core nested Markdown conservative triple *** / ___ and StrongEmphasis,
  ambiguous 4+ star/underscore runs literal, test project with 22 checks.
  Windows run 42263721511 SUCCESS.
- 9190aa7180e34fb6e27f743d1f8e6f1718358a70:
  inert WPF Run/Span bold+italic, table terminal backslash/pipe parity,
  escaped backtick, unclosed code literal fallback, 32-column/256-row
  preview-only limits; 14 extra Markdown tests (36 total).
  Windows run 42264435727, job 128052537913, SUCCESS:
  Windows Release 0 errors/warnings, 409/409 PASS.
- cc20acc86c2fa4fde04f7180dbde713e4e516d29:
  9 further synthetic tests of missing folder-child-note factors,
  tampered ELK1/ELH1 ciphertext, mismatched workspace/root and
  historical ELV1 vault snapshot after rotation. The old snapshot
  remains decryptable with the previous password; this test documents
  an UNRESOLVED anti-rollback risk, not an anti-rollback defense.
  Windows Actions 42265556341 job 128056421374 SUCCESS;
  actual log read: Build succeeded, 0 Warning(s), 0 Error(s),
  45/45 Tur15 and **418/418 cumulative PASS**.
- Documentation-only commit: README, NOTICE, CI evidence, risk register,
  PowerShell status. Final docs HEAD CI checked separately.

## Exactly verified cumulative Windows console test counts
Workspace/page 31; EMD1 15; EMD2/storage 30; recovery/rotation 35;
Tur06 38; Tur07 42; Tur08 27; Tur09 31; Tur10 RTF 16;
Tur11 DOCX/ODT 32; Tur12 basic Markdown 21; Tur13 inline 27;
Tur14 nested 28; Tur15 Markdown + experimental crypto policy probes 45.
TOTAL = **418/418 PASS** on code commit cc20acc86c2fa4fde04f7180dbde713e4e516d29.

## Explicit no-test and no-release statements
- The source .md bytes and plaintext .md.enote-layout.json never pass
  through any Markdown preview writer. WPF uses inert Span/Run only,
  no hyperlink, browser/HTML script, remote image fetch or network.
  This is a subset, not full CommonMark or Word/UYAP WYSIWYG.
- 32 columns/256 rows, 200k characters/3000 lines, inline depth/node
  budgets are only preview CPU/memory guards. NOT an application cap
  on user file counts, folder depth or note size.
- Complex RTF/DOCX/ODT fidelity in real Word/LibreOffice NOT RUN.
  RTF and guarded simple-paragraph DOCX/ODT remain new-copy only.
- Independent ELK1/ELV1/ELH1 layer experiments are not end-to-end
  production security. OLD COPIED VAULTS ARE STILL READABLE after
  current password rotation; root-key recovery and rollback handling
  are unsolved. WPF New Secure Note (.emd) DISABLED.
- Malicious symlink/junction/TOCTOU and OS-wide CAS, Windows ACL-deny,
  actual disk full/power outage/sharing-lock/multi-user DPAPI,
  actual native GUI/DPI/keyboard/accessibility NOT RUN.
- Actual printer/PDF/DOCX/RTF/UDF export and real editable unsigned
  UYAP UDF opening/re-saving/geometric fidelity in official editor
  NOT RUN. WPF UI compiled, not physically exercised.
- Runtime SBOM/NOTICE/font/icon/cryptography external review,
  signed Windows installer and V1 acceptance BLOCKED.
- Public main and four genuine original/brand logo assets unchanged;
  no installer uploaded to Google Drive Uygulama folder.

Engineering progress estimate 38%, NOT measured acceptance.
Conditional planning 2027 Q2–Q3 (April–September), NOT a promise.
