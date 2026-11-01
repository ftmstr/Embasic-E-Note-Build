# Tur 01 — Windows CI evidence (2026-11-01)

## Source under test

- Repository: ftmstr/Embasic-E-Note-Build (Public)
- Branch: feature/tur-01-wpf-bootstrap-20261101
- Commit: 3217c7724103f47d2097397a2d86b820a0618947
- GitHub Actions run: https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41324011720
- Job ID: 124900038711

## Observed result from GitHub Actions job logs

- Windows runner: build-and-smoke — **success**
- `dotnet restore Embasic.ENote.slnx` — **success**
- `dotnet build Embasic.ENote.slnx --configuration Release --no-restore`
  — **Build succeeded; 0 Warning(s); 0 Error(s)**.
- `dotnet run --project tests/Embasic.ENote.SmokeTests/Embasic.ENote.SmokeTests.csproj --configuration Release --no-restore`
  — **7/7 checks passed**:
  - .md recognized as plaintext
  - uppercase .EMD classified as encrypted format
  - .emd plaintext reader denied
  - unsupported extension denied
  - blank path denied
  - encrypted note passed to plaintext reader rejected
  - UTF-8 Turkish/Japanese/emoji plaintext .md read round-trip

## Limits

This is a small self-hosted console smoke suite, not a cryptographic security
audit. No .emd cryptography, key handling, folder security, printing, UDF or
other export feature exists. WPF compiled, but opening the window on an
interactive Windows desktop, accessibility/DPI checks and installer tests
remain **NOT RUN**. No release approval is implied.

## Status dashboard

Use `pwsh -File scripts/Show-ENoteStatus.ps1`. Its default PASS
indicates the **historically verified run above**, not the latest future CI status.
Passing a different `-CiState` value explicitly overrides the display.
