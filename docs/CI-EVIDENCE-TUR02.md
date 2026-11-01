# TUR 02 — Windows CI and scope evidence

Date: 2026-11-01

## Repository and proposed branch

- Public repository: `ftmstr/Embasic-E-Note-Build`
- Parent: Tur 01 branch HEAD `05c65ad9c9ed99662335245fcc6d1d350be04f32`
- Stacked draft PR #2: https://github.com/ftmstr/Embasic-E-Note-Build/pull/2
- Source implementation commits:
  - `78de6f81f5b9ae6da284c342dcecb6ff7c5ab43f` storage/test service
  - `e8a6e87569104edbb42f172392843d712d2996b8` WPF workspace interface
  - `68463c2aa4f9521ef66a6b44da1dcd882ef9aabd` compile/test corrections

## Verified Windows build and automated checks

GitHub Actions Windows run:
https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41330908174

- `dotnet restore Embasic.ENote.slnx` — PASS
- `dotnet build Embasic.ENote.slnx --configuration Release --no-restore`
  — PASS (0 warnings, 0 errors)
- `dotnet run --project tests/Embasic.ENote.SmokeTests/Embasic.ENote.SmokeTests.csproj --configuration Release --no-restore`
  — PASS (17/17 checks)

The 17 checks cover baseline .md/.emd extension isolation, UTF-8 Markdown,
valid and missing workspace roots, shallow directory enumeration,
folder-first order, non-note filtering, Unicode nested directories,
out-of-root rejection, traversal, reserved Windows device names,
duplicate creation and cancellation.

Earlier CI runs 41330625502 and 41330780354 **FAILED** due to a
test tuple parenthesis error and missing `System.IO` imports. These
were corrected in commit `68463c2`; the later complete run above passed.
Do not omit historical failures when auditing this series.

## What is built

- Windows folder picker with existing-directory validation.
- Async one-level listing on expansion, with WPF TreeView and disabled
  secure-note creation.
- Real new-subfolder operation in the selected directory and refresh command.
- `.md`/ `.emd` entries shown by filename only; unsupported files hidden.
- Reparse-point paths skipped and workspace-path containment checks applied.

## Not accepted

- Interactive desktop GUI, focus, keyboard, accessibility, DPI, high-volume
  folder stress, junction/race and disk-permission recovery: **NOT RUN**.
- Directory tree virtualization attributes are enabled, but large-tree
  performance is **NOT MEASURED**.
- Directory-check race resistance is not a security sandbox; hostile
  concurrent symlink swaps are an open threat-model item.
- Secure `.emd` writing/reading and key recovery: **NOT IMPLEMENTED**.
- Markdown text editing, multi-tab saving, printing/export/UDF writer:
  **NOT IMPLEMENTED**.
- Windows packaged installer and final v1.0: **NOT BUILT**.

Current W004, W005 and W006 remain in REVIEW/IN PROGRESS pending GUI
and edge-case acceptance. No P0 release approval.
