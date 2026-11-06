# Tur 14 — Security and release risk review (2026-11-06)

## New code
The nested Markdown preview is inert: `MarkdownNestedInlineParser`
produces bounded data nodes, WPF builds **Span/Run only**.
No browser, hyperlinks, JS, HTML interpretation, local file dereference,
user-supplied image network fetch or ad telemetry. Source `.md` not
rewritten. Depth/node caps are preview-only and do not cap file/folder
counts. Partial CommonMark subset and code-run parsing may differ
from full Markdown specification. Unsupported complex tables fall
back to literal text instead of dropping extra columns.

## Previously known blockers
- Plain `.md` and `.md.enote-layout.json` are unencrypted; separate
  writes may only partially commit. Optimistic SHA-256 is **not**
  cross-process race-free CAS.
- RTF `RichTextBox` only copies to a new file; DOCX/ODT ZIP/XML
  editing restricted to simple supported paragraphs. Complex content
  read-only/fail-closed; actual Word/LibreOffice layout comparison
  NOT RUN. No full fidelity guarantee.
- ELK1/ELV1/ELH1 folder/subfolder/note independent factors remain
  experimental. Old portable or DPAPI `EMD2` backups and older ELV1
  copies can retain prior credential access after rotation.
  Rotated vaults need reliable anti-rollback and security-policy-aware
  recovery; root-key-loss recovery, cross-user DPAPI, Windows ACL,
  malformed encrypted backups and OS-level TOCTOU/cross-process
  concurrency need more work. Do not claim malicious local malware
  protection.
- WPF `New Secure Note (.emd)` stays **DISABLED**.
- Windows ACL deny, disk full, power loss, junction/symlink races,
  high DPI, keyboard accessibility, screen reader, long-note UI,
  real printer acceptance: **NOT RUN**.
- Real `Embasic.ENote.Export.UdfWriter` and editability acceptance
  in genuine UYAP Doküman Editörü, including actual open/save and
  page/font/table/Unicode comparison: **NOT RUN/BLOCKED**.
- Transitive NuGet licenses, logo rights, NOTICE, SBOM, complete
  release SHA-256, signed installer: **NOT RUN/BLOCKED**.

W007 and SEC-EMD-05 BLOCKED; W010/W011/W012/W014 IN PROGRESS;
UDF/physical acceptance NOT RUN; release V1.0 BLOCKED.
Estimated engineering progress **36%**, an unmeasured planning figure,
not product completion or security clearance. Conditional 2027
Q2-Q3 (April–September) is **not a delivery promise**. No EXE/MSI
uploaded to Google Drive `Uygulama`.
