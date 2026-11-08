# Tur 18 — ELV1 rollback, key transition and Windows threat review
Date: 2026-11-08. Status: REVIEW / RELEASE BLOCKED.

## Risk boundaries
1. Bounded 174-byte ELV1 snapshot read protects **opt-in** pinned
   read from unbounded allocation. FileShare.Read avoids a standard
   conflicting writer while the read stream remains open. It is
   not a guarantee against malicious reparse swaps or old handles.
2. ELV1 `Rotate` rewraps the same LayerSecret, so an old encrypted
   copy + old password can reveal that old factor. A file update
   does not revoke ciphertext already copied by a third party.
3. Tur16 `VaultSnapshotPin` uses exact SHA-256 of a ciphertext
   and caller-asserted epoch; Tur17 `TrustedVaultOpenGate` refuses
   missing policy. **No independent, rollback-resistant anchor
   exists.** Old grant/epoch+old file still pass, and legacy
   `LayerKeyVault.Open`/`LayerVaultDiskStore.Unlock` bypass checks.
4. The same valid ciphertext stored at a different .elkey path
   is currently accepted by `UnlockPinned`; no policy-bound
   file identity check exists. A path string alone is insufficient
   against a malicious filesystem or junction race.
5. Offline anchor feasibility must be evaluated against low-end
   Windows devices and portable mode. A TPM NV policy register,
   externally held recovery secret or multi-device attestation
   is only a candidate; not implemented, available or accepted.
   Storing an HMAC, signature or DPAPI ciphertext alongside the
   vault cannot prove a monotonic non-rollback history if all
   local state can be reverted simultaneously.
6. Rekey must generate **new independent factor secrets** and
   re-encrypt current ELK1/EMD2 data and dependent hierarchy
   in a durable, recoverable, authenticated migration. Rewrap
   alone does not revoke old copied factors. Physical old copies
   remain readable without retention and user controls.
7. Crash consistency, root-key loss/recovery, portable backups and
   app/folder/subfolder/note independent gates require a separate
   documented policy. Do not enable WPF .emd until reviewed and
   independently tested.

## W-ID
| Task | Status | Required evidence |
|---|---|---|
| W007 / SEC-EMD-05 | BLOCKED | Trusted monotonic policy, forced all-open-path enforcement, old copied backup threat and true root factor rekey/recovery |
| W012 | IN PROGRESS | 20 new bounded-read tests PASS; NTFS ACL, disk-full, power loss, cross-process CAS, malicious junction/TOCTOU NOT RUN |
| W009 | IN PROGRESS | Four original logos unchanged, physical Windows GUI test NOT RUN |
| W010 | IN PROGRESS | A4/page-layout prototype; actual print/PDF/DOCX/RTF/UDF export NOT RUN |
| W011 | IN PROGRESS | Markdown inert editor/preview and guarded RTF/DOCX/ODT; full Word/UYAP WYSIWYG and complex fidelity NOT RUN |
| W014 | IN PROGRESS | Headless Windows Release CI 482 automated PASS; actual GUI/DPI/a11y NOT RUN |
| UDF | BLOCKED | Genuine UYAP Document Editor unsigned editable UDF open/edit/resave/compare NOT RUN |
| Release | BLOCKED | NOTICE/SBOM, security and UDF/P0 gates, signed installer not accepted |

Estimated engineering progress **41%**, not a measured release
readiness/security percentage. Conditional V1.0 2027 Q2–Q3
(April–September 2027), not a delivery promise. No installer.
