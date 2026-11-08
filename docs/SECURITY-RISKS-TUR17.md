# Tur 17 — release risk register (2026-11-08)

| W-ID | State | Actual evidence / blocker |
|---|---|---|
| W007 / SEC-EMD-05 | BLOCKED | Tur17 fail-closed optional gate + 23 tests; **no trusted monotonic anchor**, old policy with old vault works, old LayerKeyVault.Open and DiskStore.Unlock bypass gate |
| W007-RECOVERY | BLOCKED | No reviewed offline root-key recovery, lost factor, old backup revocation or migration commitment |
| W012 | IN PROGRESS | Windows CI FileShare.None denial tested; actual ACL-deny, disk-full, power interruption, cross-process CAS, symlink/TOCTOU and untrusted reparse traversal NOT RUN |
| W009 | IN PROGRESS | Official Embasic light/dark logo, E-Note ICO/512 PNG unchanged; physical UI validation NOT RUN |
| W010 | IN PROGRESS | A4/layout sidecar exists; true pagination, printer, PDF, DOCX/RTF/UDF output acceptance NOT RUN |
| W011 | IN PROGRESS | Local .md inert read-only preview, bounded inline/table support. Real Word/UYAP WYSIWYG and Office-level fidelity NOT DONE |
| W014 | IN PROGRESS | Windows Release CI 462/462 automated PASS on source HEAD, not GUI or production security |
| ELK1/ELV1/ELH1 | BLOCKED | Independent lock chain remains experimental; old EMD2 backup and legacy readers can bypass current policy |
| UDF/EXP-001..011 | NOT RUN | Real unsigned editable UDF must open, edit, resave, compare in genuine UYAP Document Editor |
| RELEASE | BLOCKED | Full license/NOTICE/SBOM and Windows signed installer/UDF/security acceptance outstanding |

WPF `New Secure Note (.emd)` **DISABLED**; security analysis
explicitly excludes guarantees against malicious same-user local
software and copied historical ciphertext. No changes to Public main,
no installer uploaded to Google Drive Uygulama.
Engineering estimate **40%**, not measured product/security
acceptance. Conditional V1.0 planning **2027 Q2–Q3**, not a promise.
