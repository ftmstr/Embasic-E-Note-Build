# TUR 05 — proof and explicit limitations

Date: 2026-11-02. Public repository `ftmstr/Embasic-E-Note-Build`.
Stacked draft PR #5: https://github.com/ftmstr/Embasic-E-Note-Build/pull/5
Base: Tur 04 feature branch at `21e666ac745b058103db353c12f63849d6cb018a`.
`main` unchanged. Existing PR #1–#4 remain draft.

Implementation commit:
`7ea2f25ab170c6e0bf70cd0cc40607702d46e39a`.
Files added: `RecoveryBackupCodec.cs`, `PortableEmd.Reencrypt.cs`,
`Security/LockGatePlan.cs`, new security test project + runner; solution and
Windows workflow updated. EMD1/EMD2 existing formats preserved.
Logo-Light / Logo-Dark PNG resources unchanged.

Verified Windows Actions run:
https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41386156447

- Restore: PASS.
- Windows Release build: PASS (0 warnings / 0 errors).
- Workspace smoke: 17/17 PASS.
- Experimental EMD1: 15/15 PASS.
- Portable EMD2 / encrypted disk: 30/30 PASS.
- New recovery / rotation / migration / lock-plan tests: 35/35 PASS.
- Total: 97/97 PASS (Windows headless CI, not an interactive GUI).

New negative cases include wrong backup password, backup ciphertext/header/tag
tamper, KDF/version mismatch, truncated backup, short password/secret, restored
recovery outside DPAPI, wrong current password, old recovery invalidated by
fresh re-encryption, EMD1/EMD2 explicit migration and source byte preservation,
parent/child/note independent gate planning, sharing-lock refusal with old
ciphertext intact, and stale SHA-256 snapshot refusal.

**NOT RUN / NOT IMPLEMENTED:** real different-Windows-account DPAPI test,
NTFS ACL-denial/permission tests, power-failure/disk-full, hostile
symlink/TOCTOU, multi-process concurrent writers, actual secure secret backup
UI or disposal, cryptographic folder key wrapping, recovery rotation UI,
interoperability vectors, Windows GUI/DPI, UYAP Editor UDF, installer release
and complete SBOM. A test for exclusive sharing lock is NOT an ACL test.
No code commits to `main`, no release uploads, no DONE security status.
