# Developer bootstrap — Tur 01

## Requirements

- Windows 10/11 with .NET SDK 10.0.x (Windows Desktop / WPF workload in the SDK)
- Visual Studio with .NET desktop development workload is optional.
- No paid account, service or third-party NuGet dependency is required for this bootstrap.

## Build and run

```powershell
dotnet restore Embasic.ENote.slnx
dotnet build Embasic.ENote.slnx --configuration Release --no-restore
dotnet run --project src/Embasic.ENote.App/Embasic.ENote.App.csproj
dotnet run --project tests/Embasic.ENote.SmokeTests/Embasic.ENote.SmokeTests.csproj --configuration Release
pwsh -File scripts/Show-ENoteStatus.ps1
```

The WPF app is a **non-editing UI shell**. The smoke tests are a small dependency-free console
runner for extension policy and UTF-8 reading; they do not replace full xUnit/NUnit,
cryptographic tests, GUI automation or UYAP acceptance.

Do not claim a successful Windows build until the GitHub Actions Windows job completes.
The Linux authoring environment initially had no .NET SDK and could not build.

## Next engineer tasks

1. Specify and review .emd v1 binary envelope and key-slot API against ENOTE-08.
2. Develop authenticated encryption/negative tests and secure Windows key management.
3. Add explicit workspace selection and safe atomic .md/.emd note saving.
4. Connect real edit/preview tabs only after persistence and secret-handling safeguards.
