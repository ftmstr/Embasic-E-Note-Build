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

## Tur 09 — plaintext Markdown editor foundation (2026-11-02)

[Draft PR #9](https://github.com/ftmstr/Embasic-E-Note-Build/pull/9)
adds a **plain `.md`-only** WPF multi-tab **source text** editor and
per-document page-layout sidecar. It is NOT a Word/UYAP WYSIWYG editor.
Saved Markdown remains interoperable UTF-8 text. The editor displays
unsaved-change indicators, asks before discarding changes and refuses
stale SHA-256 writes. Ctrl+O opens, Ctrl+S saves; Ctrl+Alt+N creates
plain `.md`; Ctrl+N is reserved for a DISABLED secure `.emd` flow.
Custom page geometry remains in Core, with sidecar JSON for plain
Markdown only (not inside `.md`).

A separate, still experimental read-only
`WorkspaceLockPathBinding` performs authenticated ELH1 root GUID
validation, filesystem existence/path-segment checks and authorization
to require **all** independently unlocked ELV1/ELK1 factors before a
locked note could be decrypted. It is not connected to the WPF tree
and is not race-free vs hostile reparse/path swaps. Secure `.emd`
note creation/open/save stays DISABLED.

[Tur09 Windows CI run](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41500273510):
Windows Release 0 errors/warnings, **249/249** automated PASS on
source HEAD prior to docs commit. See
[Tur09 technical report](docs/CI-EVIDENCE-TUR09.md).

No actual interactive Windows GUI/DPI, printing, PDF/DOCX/RTF/UDF
exports or native UYAP Editor acceptance passed. The WPF editor
source-text milestone and sidecar are NOT a complete product release.

## Tur 12 (2026-11-06) — offline Markdown visual preview

Stacked [Draft PR #13](https://github.com/ftmstr/Embasic-E-Note-Build/pull/13)
builds on the existing Tur 10/11 native RTF and guarded DOCX/ODT editors,
**without removing them**. Core inert Markdown block parsing + WPF
**Write / Preview / Split** displays headings, lists, quotes, simple
tables and fenced code. Source `.md` remains editable UTF-8; preview
is read-only, with literal HTML and non-fetched images/links. GUI
truncation guards never truncate note content.

[Windows CI](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42111850623):
Windows Release 0 warnings/errors, 318/318 automated checks on code
HEAD before documentation commit. See
[CI evidence](docs/CI-EVIDENCE-TUR12.md) and
[research integration](docs/RESEARCH-INTEGRATION-TUR12.md).

Physical WPF GUI/DPI, true Word/UYAP WYSIWYG, full DOCX/ODT fidelity,
printer/PDF/UDF and security/recovery release gates remain NOT RUN or
BLOCKED. New secure note (.emd) remains DISABLED.

## Tur 13 (2026-11-06) — safe inline Markdown and table escapes

[Stacked Draft PR #14](https://github.com/ftmstr/Embasic-E-Note-Build/pull/14)
adds inert `MarkdownInlineParser` tokens for limited **bold**,
*italic*, inline code and ~~strikethrough~~; WPF displays these using
ordinary literal `Run` styles inside the existing FlowDocument.
Markdown tables now keep escaped `\|` and simple code-span pipes
within the same cell. All changes affect **visual preview only**;
source `.md`, per-note paper-layout sidecars, RTF copy-only saves,
and guarded DOCX/ODT editing remain untouched.

No HTML, JavaScript, remote images, links, browser engine or
new API. This is not full CommonMark, full WYSIWYG or print/PDF/UDF.
[Windows CI 42173395275](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42173395275)
confirmed Release **0 warnings/0 errors, 345/345 checks** on the
code HEAD before the documentation-only commit.
See [Tur13 CI evidence](docs/CI-EVIDENCE-TUR13.md) and
[Tur13 security risk register](docs/RISKS-TUR13.md).
Physical Windows GUI/DPI, rich-document fidelity and UYAP are NOT RUN.
The WPF secure `.emd` note workflow remains DISABLED.

## Tur 14 (2026-11-06) — bounded nested Markdown preview

[Stacked Draft PR #15](https://github.com/ftmstr/Embasic-E-Note-Build/pull/15)
adds a conservative `MarkdownNestedInlineParser` with bounded nested
strong/emphasis/strike/code spans, and WPF literal `Span`/`Run` rendering.
The basic Markdown table preview preserves escaped pipes (odd/even
backslash runs), multi-backtick code-span pipes and literal tab characters
using structured cells. Extra/malformed columns fail closed to literal
text rather than silently losing content. Original `.md` bytes are never
rewritten by this feature.

[Windows CI 42259573972](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42259573972)
shows **373/373 automated PASS**, Windows Release 0 errors/warnings
on source code HEAD. Two initial commits FAILED compilation
(**CS1628**, local function captured `ref`), fixed in
`c907fe9843a47d9251487e37696cfa6bfae62d93`.
See [Tur14 evidence](docs/CI-EVIDENCE-TUR14.md) and
[security limits](docs/SECURITY-RISKS-TUR14.md).
Full CommonMark, WYSIWYG editing, real printing/PDF/UDF and physical
Windows GUI acceptance are NOT claimed. `.emd` creation remains DISABLED.

## Tur 15 (2026-11-06) — conservative triple emphasis, safe table fallback, lock-risk probes

Draft PR #16: https://github.com/ftmstr/Embasic-E-Note-Build/pull/16
This stacks on verified Tur14 PR #15 (01690b193df14e4245b322a9d3aa0eef92719da0), preserving Tur10 RTF copy-only and Tur11 guarded DOCX/ODT editing. The offline Markdown preview understands a conservative triple-asterisk / triple-underscore strong+emphasis subset. Ambiguous four-plus delimiters stay literal. Inert WPF Span/Run formatting, escaped table pipes, balanced code runs, malformed rows, and preview-only table-size limits avoid source rewriting and silent extra-column loss. This is NOT full CommonMark, native WYSIWYG, or Office output fidelity.

Experimental synthetic security tests exercise independent ELK1 factors, authenticated ELH1 index failures, and ELV1 password rotation. Importantly, an old copied ELV1 vault STILL OPENS with the former password, even after a current vault rotates credentials. This is a documented ANTI-ROLLBACK VULNERABILITY, not a resolved security feature. New secure .emd creation/open/save in WPF stays DISABLED.

Code HEAD Windows CI: https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42265556341
Windows .NET 10 Release 0 errors and 0 warnings, **418/418 automated PASS** including 45 Tur15 checks. Headless CI does not establish physical Windows GUI/DPI, disk-full/ACL/TOCTOU, cross-account DPAPI, print/PDF/UDF acceptance. See docs/CI-EVIDENCE-TUR15.md and docs/SECURITY-RISKS-TUR15.md. No signed installer released.

## Tur 16 (2026-11-07) — experimental ELV1 snapshot pin

[Draft PR #17](https://github.com/ftmstr/Embasic-E-Note-Build/pull/17)
stacks on Tur15. `VaultSnapshotPin` authenticates an existing ELV1
envelope, captures its SHA-256 digest and a **caller-asserted** epoch,
and rejects a different snapshot on its optional pinned-open path
before deriving a secret. `LayerVaultDiskStore.UnlockPinned` is an
optional wrapper. [Code CI 42411730279](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42411730279):
Windows .NET10 WPF Release, **0 errors/warnings and 439/439 tests PASS**.

**THIS IS NOT A COMPLETE ANTI-ROLLBACK MECHANISM.**
The old `LayerKeyVault.Open` / `LayerVaultDiskStore.Unlock` APIs remain
available, and an attacker can supply an older pin and epoch if no
independent rollback-resistant trusted store exists. ELV1 itself has
no authenticated monotonic revision. An attacker with old vault
ciphertext plus old password can still extract the historical factor.
See [threat model](docs/ROLLBACK-THREAT-MODEL-TUR16.md),
[CI evidence](docs/CI-EVIDENCE-TUR16.md) and
[risk register](docs/SECURITY-RISKS-TUR16.md).

No WPF secure `.emd` enablement, changes to Markdown/RTF/DOCX/ODT,
new packages/SDKs, browser/network integration, installer or main merge.
Actual Windows GUI/DPI/UYAP/NTFS and cross-user recovery remain NOT RUN.

## Tur 17 (2026-11-08) — fail-closed ELV1 policy gate experiment

[Stacked Draft PR #18](https://github.com/ftmstr/Embasic-E-Note-Build/pull/18)
introduces `TrustedVaultOpenGate` and an
`IExternalVaultAnchorReader` contract. The *new, opt-in path* refuses
vault unlocking if a source of trusted policy is absent, unavailable,
wrongly bound to the workspace/factor, or below its supplied epoch floor.
[Code HEAD Windows CI](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42483708395):
.NET 10 Release **0 warnings, 0 errors, 462/462 automated PASS**,
including 23 Tur17 cases and a real Windows Actions `FileShare.None`
sharing-lock refusal.

**WARNING: Not an anti-rollback solution.** No authenticated monotonic
policy anchor exists; fake/rolled-back anchors still allow old ELV1
copies with previous passwords. Old `LayerKeyVault.Open` and
`LayerVaultDiskStore.Unlock` bypass this opt-in gate. The new tests
**intentionally prove these flaws persist**. This is NOT secure
root-key recovery, old-backup revocation or production cryptography.
See [design gates](docs/ROLLBACK-GATE-DESIGN-TUR17.md),
[CI evidence](docs/CI-EVIDENCE-TUR17.md) and
[security risks](docs/SECURITY-RISKS-TUR17.md).
Secure `.emd` WPF workflow remains DISABLED; no main merge or installer.

## Tur 18 (2026-11-08) — bounded pinned ELV1 snapshot read

[Stacked Draft PR #19](https://github.com/ftmstr/Embasic-E-Note-Build/pull/19)
hardens only the **opt-in** `LayerVaultDiskStore.UnlockPinned` read:
it opens an ELV1 file with a standard `FileShare.Read` stream, rejects
all lengths except the 174-byte `LayerKeyVault.Size` BEFORE allocating
the envelope buffer, uses `ReadExactly`, and zeroes the buffer.
It does not change the unguarded legacy `Unlock` API. It does
not provide anti-TOCTOU, cross-process CAS, path-bound identity,
trusted monotonic anchor or old-key revocation.

[Tur18 code Windows CI 42506028582](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42506028582):
Windows .NET10 Release **0 warnings/errors, 482/482 automated PASS**
(462 prior + **20 Tur18**). Tests cover wrong/old credentials and epochs,
missing files, Windows sharing conflict, empty/short/oversized/tampered
envelopes and intentional passing legacy and cloned-path RISK proofs.
See `docs/CI-EVIDENCE-TUR18.md`,
`docs/ROLLBACK-RECOVERY-DECISION-TUR18.md`, and
`docs/SECURITY-RISKS-TUR18.md`.
WPF secure .emd remains **DISABLED**, no installer, no main merge.

## Tur 19 (2026-11-08) — experimental vault path-policy binding

[Stacked Draft PR #20](https://github.com/ftmstr/Embasic-E-Note-Build/pull/20)
adds the optional `TrustedVaultOpenGate.UnlockPathBound` method.
Its policy grant has a new, optional `BoundVaultPath` string.
This route fails closed when the path is absent, relative, or lexically
different from the trusted grant's absolute normalized path. It still
calls the previous Tur18 174-byte bounded `UnlockPinned` implementation.
The *historical* `Unlock` method remains unchanged for test/backward
compatibility, and still accepts ciphertext cloned to another filename.

[Code HEAD Windows CI 42510423535](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42510423535):
.NET 10 WPF Release **0 warnings, 0 errors, 507/507 automated PASS**,
including 25 Tur19 regression tests. Positive tests cover the bound
path and independent recovery; negative tests cover cloned paths,
missing/relative/incorrect policies, broken/truncated/oversized vaults,
wrong password and real Windows FileShare.None refusal. Intentional
PASS-ing risk tests confirm that legacy APIs and old grant/epoch +
old ciphertext still unlock.

**NOT secure physical NTFS file identity.** This is lexical canonical
path comparison only, not volume/file-ID validation or a handle-bound
anti-reparse/symlink/junction/TOCTOU check. Neither an independently
trusted rollback-resistant anchor nor mandatory enforcement on every
ELV1/ELK1/EMD2 open exists. Historical copied ciphertext and old
passwords remain recoverable through old readers. Secure WPF `.emd`
remains DISABLED. The implementation is NOT production anti-rollback.
See [path binding design](docs/PATH-POLICY-IDENTITY-TUR19.md),
[CI evidence](docs/CI-EVIDENCE-TUR19.md) and
[risk matrix](docs/SECURITY-RISKS-TUR19.md).
