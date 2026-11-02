# Embasic E-Note

**Status — 2026-11-02:** Experimental open-source C#/.NET 10 WPF Windows
Markdown notebook. **Not a released or user-ready secure editor.**

Stacked draft PRs: [#1](https://github.com/ftmstr/Embasic-E-Note-Build/pull/1),
[#2](https://github.com/ftmstr/Embasic-E-Note-Build/pull/2),
[#3](https://github.com/ftmstr/Embasic-E-Note-Build/pull/3),
[#4](https://github.com/ftmstr/Embasic-E-Note-Build/pull/4),
[#5](https://github.com/ftmstr/Embasic-E-Note-Build/pull/5),
[#6](https://github.com/ftmstr/Embasic-E-Note-Build/pull/6),
[#7](https://github.com/ftmstr/Embasic-E-Note-Build/pull/7).
The Public default `main` remains unchanged.

Tur 01–02: WPF shell and local folder/lazy tree; Tur 03 genuine user
light/dark brand logos and experimental EMD1; Tur 04 experimental EMD2
Argon2id/DPAPI/password/recovery, encrypted-only disk staging;
Tur 05 ERB1 recovery, full EMD2 password rotation and draft EMD migration;
Tur 06 ELK1 independent folder/subfolder/note AEAD outer layers and
ciphertext-only recovery backup file; **Tur 07** password and recovery
protected **ELV1 factor vaults** and **ELK1-only `.emd` ciphertext disk
store** with optimistic conflict checks and regression tests.

[Tur 07 verified Windows CI](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41405104087):
Release 0 errors/warnings, 177/177 automated PASS on the corrected code.
[Tur07 technical evidence](docs/CI-EVIDENCE-TUR07.md);
[ELV1/ELK1 draft format](docs/LAYER-VAULT-TUR07.md).

**Important security restrictions:** EMD1/2, ELK1, ELV1, ERB1 are
experimental. A local password/recovery vault per layer is not a
production-backed folder key hierarchy. Encrypted disk staging is not
race-free cross-process atomic CAS. No real Windows ACL/disk-full/
power-loss, separate Windows account, interactive GUI/DPI, independent
audit, UYAP Editor UDF acceptance, or signed Windows installer passed.
Actual WPF **New Secure Note (.emd) stays DISABLED**.

All core features must work fully offline without paid APIs, accounts,
subscriptions, mandatory cloud or paid software licenses. MIT source
and third-party NOTICE are tracked; transitive SBOM not yet accepted.

## Tur 08 — research integration, 2026-11-02

Draft [PR #8](https://github.com/ftmstr/Embasic-E-Note-Build/pull/8).
The companion `Araştırma` conversation's A4-by-default page design and
expanded paper formats are implemented as a .NET Core **geometry model**
and a WPF **non-editable preview tab**. Newly provided genuine
E-Note ICO and 512px PNG resources are bundled; the original light/dark
Embasic theme logos remain unchanged.

Experimental encrypted `ELH1` workspace root/parent/child/note GUID
metadata now has ciphertext-only `.elh` create-only disk persistence.
The index requires the authenticated root factor but does NOT implement
the actual workspace UI-key relationship or transaction-safe password
recovery. Real locked-note operation remains disabled.

[Tur 08 Windows Actions evidence](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41421579049):
Release 0 warnings/errors, 218/218 automated PASS on the code HEAD
before documentation-only changes. See
[research integration scope](docs/RESEARCH-INTEGRATION-TUR08.md) and
[CI evidence and open blockers](docs/CI-EVIDENCE-TUR08.md).
