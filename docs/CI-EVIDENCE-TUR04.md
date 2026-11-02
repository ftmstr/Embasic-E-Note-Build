# TUR 04 — evidence, status and outstanding risks (2026-11-02)

Repository: Public `ftmstr/Embasic-E-Note-Build`, base PR #3 HEAD
`19ba5a0e0c0add4a5951139dce74f5394fd5a4e8`.
Branch: `feature/tur-04-portable-emd-atomic-20261102`; stacked Draft PR #4.
`main` was not modified or merged.

Source commits:
1. `31ea266f446479c9b35c719407b4d76ea3ca8c91` — experimental portable EMD2 Argon2id.
2. `213a6a0eb15fecc9a9de98c591c3e23fd69768e6` — encrypted-only same-folder replacement and 30 new tests.
3. `10099f2b6c5c193d3914004709baf5ac457f0d46` — typed EMD2 envelope-only storage interface.
4. `97511f5bbd8ad5b5c1ae1ad2d8ea5a2f01806070` — target framework compatibility fix.

CI source run `41384643179`: [PASS](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41384643179),
0 warnings, 0 errors, 17/17 storage, 15/15 EMD1 and 30/30 EMD2/disk.
The typed-storage commit initially failed restore in run `41384905032`
due to NU1201: net10.0 Storage cannot reference net10.0-windows EMD library.
The EMD library was changed to net10.0; Windows-only DPAPI calls retain
runtime guards. Corrected [run 41385023814](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41385023814):
**Windows Release build PASS, 0 warnings/0 errors, 17/17 + 15/15 + 30/30
= 62/62 PASS**, verified from job logs.

New tests cover Unicode password/encrypted note, portable credential,
recovery, local DPAPI, incorrect password/recovery, unavailable local key,
ciphertext/tag/header tamper, rejected wrong schema/version/KDF parameters,
missing/trailing bytes, randomization, in-memory plaintext absence,
single-new-file encrypted storage, duplicate creation refusal, stale digest
refusal, simulated external change, corrupted disk-content decryption,
extension restrictions, invalid plaintext read, missing parent directory,
encrypted temporary file cleanup and EMD1/EMD2 version separation.

**NOT RUN:** actual NTFS ACL permission denial, disk-full/power-loss,
cross-process malicious path swaps, backup retrieval on a truly separate
Windows account/device, password UX, large file stress, DPI/GUI,
cryptographic independent audit, real UYAP UDF Editor and install package.
Tests of missing paths/invalid extensions do NOT substitute for ACL tests.
The format, key backup and independent locks are not complete. GUI
`.emd` creation remains DISABLED. Full transitive license/SBOM and
delivery-signing review remain open. Logo light/dark source files were
untouched and are still embedded in WPF.

Release: BLOCKED, NOT DONE. Progress estimate after this work: 12%,
not a measured feature-completion percentage. 2027 Q2–Q3 is an uncertain
planning window, NOT a promise.
