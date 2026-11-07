# Tur 16 — Public Windows CI evidence

Date: 2026-11-07. Authorized Public repository
`ftmstr/Embasic-E-Note-Build`, `main` unchanged at
`a10faf48e7cd153a53803192e0ac7de39c998458`.

## Prerequisites rechecked
Google Drive Kaynak ENOTE-01/02/03/04/05/06/08/09, Tur15 Rapor;
Draft PR #9, #10, #11, #13, #14, #15, #16 HEADs; Tur15 final
CI [42266447557](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42266447557)
job 128059515217 SUCCESS, Windows Release 0 errors/0 warnings and
418/418 automated tests. Tur16 stacked [Draft PR #17](https://github.com/ftmstr/Embasic-E-Note-Build/pull/17)
on Tur15 SHA `4378ad7b7aa7316172f48d192c6c3dbcc3318570`.

## Code commit
`1cc9f69edcf016464c6b9839a518917deb78ac08` adds:
- `src/Embasic.ENote.Emd/VaultSnapshotPin.cs`
- optional `LayerVaultDiskStore.UnlockPinned`
- `tests/Embasic.ENote.Tur16.Tests` (21 synthetic checks)
- Windows CI and solution wiring.

Real Windows Actions [42411730279](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42411730279),
job `128521123803`: **SUCCESS**. Actual job log inspected:
**Build succeeded, 0 Warning(s), 0 Error(s)**.
Cumulative **418 previous + 21 Tur16 = 439/439 PASS**.
Tur16 positive tests validate current pinned + recovery slot; negative
tests refuse stale envelope, stale epoch, wrong ID/kind/credential,
tampering, truncated input. RISK CONFIRMATION TESTS intentionally PASS
when old vault opens through the legacy API and if old pin/epoch are
rolled back, proving security is not solved.

## Not accepted
No physical Windows GUI/DPI, WPF key flow integration, anti-rollback
trusted store, cross-account DPAPI, NTFS ACL, disk full/power loss,
FileShare hostile lock, symlink/TOCTOU, full root-key recovery,
third-party cryptography audit, printed PDF/UDF or real UYAP
edit/resave comparison. These are NOT RUN or BLOCKED.
Original logos/ICO preserved; no new NuGet, paid SDK, account or
cloud dependency. Public main not merged, no installer in Uygulama.
Documentation-only closure commit gets its own final SHA Windows
CI check; report final run number after real completion.
