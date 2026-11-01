# Embasic E-Note — developer guide (Tur 02)

## Requirements

- Windows 10/11 and .NET 10 SDK (10.0.x) with WPF/Windows desktop targeting.
- No mandatory paid API, online activation or third-party runtime NuGet SDK
  exists in the current source.
- The source may be built in Windows CI. Interactive GUI behavior requires
  separate hands-on Windows checks.

## Build and run

```powershell
dotnet restore Embasic.ENote.slnx
dotnet build Embasic.ENote.slnx -c Release --no-restore
dotnet run --project tests/Embasic.ENote.SmokeTests/Embasic.ENote.SmokeTests.csproj -c Release
dotnet run --project src/Embasic.ENote.App/Embasic.ENote.App.csproj
pwsh -File scripts/Show-ENoteStatus.ps1
```

## Implemented functions in this branch

- Click **Klasör Seç…** to choose an existing local folder.
- Expand folder nodes to enumerate one level. Direct children are folders,
  `.md` and `.emd` filenames. No file content is read by tree browsing.
- Select a folder (or use workspace root) and click **Yeni Alt Klasör**.
  Validation rejects illegal, reserved and traversal names.
- **Yenile** reloads the selected folder; a new workspace selection clears
  the prior UI tree.
- No arbitrary per-folder item count/depth cap in storage enumeration; Windows
  filesystem, permissions and memory limits still apply.
- The `.emd` creation command is disabled and note editors are not present.
  Plain `.md` is explicitly unencrypted.

## Security and testing boundaries

- Directory links/junctions are not followed in the current navigator;
  this is not a hardened sandbox against concurrent path-swap races.
- Untrusted folders can produce access exceptions and very large listings.
  The UI receives a recoverable status message, but GUI and stress tests
  are outstanding.
- Verify evidence at [Tur 02 CI](CI-EVIDENCE-TUR02.md).
- Before any claim of a usable secure editor, implement the ENOTE-08
  cryptographic format, key and recovery tests, atomically safe persistence,
  and actual Windows GUI acceptance.
