# TUR 09 — Verifiable Windows CI and file safety record

**Date:** 2026-11-02. Public only `ftmstr/Embasic-E-Note-Build`.
Base Tur08 `98dbd80f8e5b9ef3e94a9c33da826cb94e5e01a1`.
[Draft PR #9](https://github.com/ftmstr/Embasic-E-Note-Build/pull/9).
`main` remains unchanged. No Windows installer uploaded.

## Source commits
- `4cfce05e2aca45d243913c5cb9c80a637be10878`:
  `PlainMarkdownDocumentStore`, `DocumentPageSettingsStore`,
  24 new Windows tests, new test project and CI.
- `26f96b045dd309e873970ca25fb8f88238c0ade1`:
  WPF tabbed text Markdown editor, change warnings, Ctrl+S/O/Alt+N,
  per-note paper settings selection, original logos preserved.
- `2e3cc01c0658c2260919d481ebcedc3311d69783`:
  experimental read-only `WorkspaceLockPathBinding`, 7 new tests.
  Explicit ELH1 AEAD root authentication plus real OS path existence
  and independently authorized folder/child/note factors.
- Subsequent documentation-only commit: README, technical evidence
  and local PowerShell status script.

## Actual CI
[Windows Actions 41499583043](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41499583043):
Release 0 warnings/errors, previous 218 + 24 = 242/242 PASS.
[Windows Actions 41500273510](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41500273510):
Release **0 Warning(s) / 0 Error(s)**; 31/31 workspace/page geometry,
15/15 EMD1, 30/30 EMD2/disk, 35/35 recovery/rotation,
38/38 Tur06, 42/42 Tur07, 27/27 Tur08, and **31/31 Tur09** =
**249/249 automated PASS**. Job logs were read.
This does NOT constitute a physical interactive GUI or security audit.

## Plain Markdown persistence
Only `.md`, strict UTF-8 with optional original UTF-8 BOM.
No decode path for `.emd`. Stale expected SHA-256 refuses overwrite.
File.Move/create only; File.Replace/known digest for updates;
random same-directory `.enote-tmp` only, flush-to-disk, no
auto-save or hidden overwrite. This is an optimistic conflict guard,
not OS-wide atomic compare-and-swap or malicious TOCTOU prevention.

Per-note `.md.enote-layout.json` sidecar version 1 stores only
unencrypted paper ID/dimensions/orientation/zoom and does not change
Markdown bytes. It is NOT private/encrypted. Page metadata/text
updates are separate operations, not a transaction: one may succeed
while the other fails. Physical crash recovery remains NOT RUN.

## Independent lock integration boundary
`WorkspaceLockPathBinding` receives an **encrypted** ELH1 index,
authenticates it with a workspace GUID and root factor, resolves the
real existing filesystem path, rejects missing components and simple
reparse targets, and requires the ordered independently unlocked
folder/child/note factors. Only the caller's full ELK1 AEAD decrypt
can authenticate locked note contents. This is **not** connected to
the WPF UI and **not** an ACL or handle-bound path security proof.

## Not run / blocked
- Actual Windows GUI interactions, UI automation, WYSIWYG,
  high-DPI and screen readers: **NOT RUN / NOT IMPLEMENTED**.
- NTFS ACL denial, disk-full, power loss, malicious symlink/junction
  swaps, race-free cross-process CAS, different-Windows-account
  DPAPI/recovery, old-vault rollback and root-key-loss recovery:
  **NOT RUN / NOT SOLVED**.
- Native print, page preview, PDF/DOCX/RTF/UDF writer and mandatory
  real UYAP Editor open/save/layout comparison: **NOT RUN**.
- Transitive SBOM, security review, signed Windows installer and
  public V1 release: **BLOCKED**.
- Affected formats EMD1/2, ELK1, ELV1, ELH1, ERB1 remain experimental.
  **WPF New Secure Note (.emd) remains DISABLED.**
- `main` and logos unchanged; no external paid API/account/cloud.

Engineering estimate **28%** (unmeasured and NOT release readiness).
Conditional 2027 Q2–Q3 planning window is NOT a delivery promise.
