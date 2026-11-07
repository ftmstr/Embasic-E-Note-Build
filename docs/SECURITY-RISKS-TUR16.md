# Tur 16 — Security & release risk register

Date: 2026-11-07; all new ELV1 work is EXPERIMENTAL.

| ID | Risk / status | Required proof |
|---|---|---|
| SEC-EMD-05 | OLD VAULT ROLLBACK **BLOCKED** | Independently trustable non-rollback anchor, enforced on every open; old copies still decrypt with old password |
| ELV1-PIN-01 | New exact-envelope SHA-256 pin **PROTO PASS** | 21 synthetic tests; no protected pin persistence/epoch. Pin/epoch themselves can roll back |
| ELV1-LEGACY-02 | Legacy Open and Unlock bypass pin **OPEN** | Dedicated bypass tests intentionally PASS; remove bypasses before release |
| ELK1-CHAIN-03 | Independent folder/subfolder/note factors **EXPERIMENTAL** | Real WPF authorization, cross-user DPAPI, multi-layer backup restore and audit |
| ELH1-PATH-04 | Handle-bound ancestry/OS security **NOT RUN** | NTFS ACL, reparse/symlink/TOCTOU, concurrent rename/races |
| EMD-RECOVERY-05 | Root key loss/rotation/recovery **BLOCKED** | Audited, crash-safe offline recovery and strict old-backup revocation caveats |
| STORAGE-06 | Atomic CAS and disk recovery **NOT RUN** | Cross-process compare-and-swap, disk-full, power-loss, FileShare deny, encrypted metadata rollback |
| OFFICE-07 | RTF/DOCX/ODT complex layout **NOT RUN** | Independent actual Office/LibreOffice read/save visual comparisons; copy-only/source preserved |
| UI-08 | Native WPF GUI/DPI/accessibility **NOT RUN** | Real Windows GUI interaction, 200% DPI, keyboard/screen reader, large trees |
| UDF-09 | Real editable unsigned UDF **NOT RUN** | UYAP Doküman Editörü open/edit/resave/compare font/Unicode/table/paper |
| RELEASE-10 | NOTICE/SBOM/correct assets/installer **BLOCKED** | License review, audited build, SHA-256 signed Release and UDF security gates |

W007 and SEC-EMD-05 **BLOCKED**; W010/W011/W012/W014
**IN PROGRESS**. WPF New Secure Note (.emd) **DISABLED**.
V1.0 **BLOCKED**, no installer to Drive Uygulama.
Estimated engineering progress 39%, NOT measured completion/security
acceptance; 2027 Q2–Q3 is only conditional planning, not a promise.
