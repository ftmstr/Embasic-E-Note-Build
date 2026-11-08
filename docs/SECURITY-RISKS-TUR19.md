# Tur 19 — W-ID and security release blockers
Date: 2026-11-08 | Status: REVIEW / V1.0 BLOCKED

| W-ID | Status | Evidence / remaining work |
|---|---|---|
| W007 / SEC-EMD-05 | BLOCKED | New opt-in lexical path-bound grant, 25 CI checks; no independently trusted monotonic anchor, old policy+old ELV1 remains usable; unguarded Open APIs bypass |
| W007-RECOVERY | BLOCKED | Root-key loss, portable recovery, old backup revocation and genuine layered factor rekey not designed/accepted |
| W012 | IN PROGRESS | Bound path check and Tur18 174-byte bounded read; Windows FileShare.None PASS. Actual file ID, hostile NTFS ACL/symlink/junction/TOCTOU, cross-process CAS/disk full/power loss NOT RUN |
| W009 | IN PROGRESS | Four original branded image/icon blobs retained; actual GUI acceptance NOT RUN |
| W010 | IN PROGRESS | A4 settings and per-note layout source present; actual print/PDF and P0 exports including UDF NOT RUN |
| W011 | IN PROGRESS | Plain .md safe source/preview, limited RTF new-copy and guarded DOCX/ODT; full Word/UYAP WYSIWYG and complex fidelity NOT RUN |
| W014 | IN PROGRESS | Windows CI 507/507 PASS on source HEAD, no physical GUI/UYAP/security release proof |
| ELK1/ELV1/ELH1 | BLOCKED | Independent app/folder/subfolder/note lock hierarchy and recovery/old backup gates not accepted |
| UDF / EXP | NOT RUN | Actual unsigned editable UDF in genuine UYAP Document Editor open/edit/resave/comparison absent |
| Release | BLOCKED | SBOM/NOTICE/font-icon provenance, Windows signed installer, security and all P0/UDF acceptance pending |

Engineering estimate **42%**, not measured feature/security
completion. Conditional V1.0 planning **2027 Q2–Q3**, not a delivery
promise. No EXE/MSI uploaded to Drive Uygulama; main unchanged.
