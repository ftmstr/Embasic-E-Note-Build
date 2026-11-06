# Tur 15 — Release security risk register

Status date: 2026-11-06. Experimental modules only.

1. **Historical ELV1 vault rollback**: A rotated current vault rejects
   its former password, but a *retained old vault file* continues to
   expose the SAME 256-bit factor to the former password; the old
   encrypted ELK1 EMD still opens with it. This was asserted in a
   synthetic regression test, so a PASS proves the hazard remains.
   Missing: trusted monotonic rollback protection, explicit revocation,
   independent recovery, multi-user threat analysis, secure backups.
   State: BLOCKED for production secure-note operations.
2. **ELK1 independent factor chain**: Synthetic attempts missing child
   and note factors, plus corrupted AEAD ciphertext, fail closed.
   This is positive code evidence but NOT a physical WPF end-to-end
   authorization, ACL, root recovery or attacker-resistance proof.
   State: IN PROGRESS / production BLOCKED.
3. **ELH1 authenticated hierarchy**: Wrong workspace GUID, wrong root
   and changed ciphertext refuse synthetic opening. Real parent-child
   renames, move races, symlink/junction swaps, handle-bound path
   validation and cross-process coordination remain NOT RUN/BLOCKED.
4. **EMD2 inner DPAPI/password/recovery slots**: Must NEVER bypass
   independent ELK1 outer folder/subfolder/note locks. Old unwrapped
   EMD2 or historical backups can remain accessible after policy
   changes. Third-party cryptography audit and secure key loss/recovery
   incomplete. WPF New Secure Note remains DISABLED.
5. **Plain Markdown integrity**: Source .md unchanged by preview.
   SHA-256 optimistic conflict is NOT malicious cross-process atomic
   compare-and-swap; sidecar .md.enote-layout.json plaintext and
   separate writes. Windows ACL-deny, disk full, sharing-lock,
   power failure, reparse TOCTOU NOT RUN.
6. **Native document fidelity**: RTF copy-only and DOCX/ODT guarded
   simple-paragraph new copy only. Full Office-level tables/images/
   styles/tracked revisions and actual Word/LibreOffice comparisons
   NOT RUN; complex content intentionally fails closed.
7. **Markdown preview**: Conservative triple strong+italic support,
   literal fallback for unknown punctuation, malformed table/code,
   inert WPF Span/Run. NOT full CommonMark, not WYSIWYG editing.
   Row 256/column 32 and inline budget guard PREVIEW ONLY;
   original user note and tree have no arbitrary app caps.
8. **Print and UDF**: Windows printer/PDF export not accepted.
   Genuine UYAP editor must open, edit, save and compare generated
   unsigned editable UDF including Unicode/fonts/tables/page geometry.
   NOT RUN; export/release BLOCKED.
9. **Supply chain and Windows installer**: No new package this turn,
   existing NOTICE/SBOM/license/security audit incomplete.
   Windows GUI/DPI/keyboard/screenreader, signed installer,
   SHA-256 binary provenance and UDF release gates NOT RUN/BLOCKED.

W007/SEC-EMD-05 BLOCKED; W010/W011/W012/W014 IN PROGRESS;
EXP-001–011 UDF NOT RUN. Engineering 38% estimated planning only.
V1.0 conditional 2027 Q2–Q3, not promised.
