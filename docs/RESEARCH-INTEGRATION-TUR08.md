# Embasic E-Note — Araştırma chat integration (Tur 08, 2026-11-02)

Authoritative sources: project conversation `Araştırma`, latest 2026-11-02
HTML prototype v1.1.1 notes, verified Google Drive
`Uygulama/Embasic E-Note/Logo` folder, and project ENOTE-00..09 Docs.

## Decisions carried forward

1. Product editing direction: a Word/UYAP-like **visual editor as default**,
   with Markdown source optional. This is a REQUIREMENT, not an implemented
   WYSIWYG editor.
2. Default paper A4 (210 × 297 mm); A2/A3/A5/A6, ISO B4, ISO B5,
   Japanese JIS B5, Letter, Legal, Tabloid, Executive and custom sizes.
   Orientation, margins and 50–150% zoom; future per-note settings and
   real printing/export. Core `PageLayoutCatalog` and
   `PageLayoutSettings` implement geometry/validation only.
3. WPF **Sayfa Düzeni (önizleme)** tab previews proportions with paper,
   orientation and zoom. It does **NOT** render an editor page,
   persist per-note document preferences, print, export PDF, or support
   actual custom dimension entry from UI. Full layout acceptance remains
   BACKLOG/IN PROGRESS.
4. User-supplied new E-Note icon suite (6 files verified in Drive).
   The ICO (Drive ID `1hUKMHr-MngNBesIJUwubAlcLvJRjqt7F`,
   Git blob `cc2dcce16b7620b4cab68220b56ef5831fc9a436`,
   77,021 B) is embedded as WinExe ApplicationIcon and Window.Icon.
   The 512 PNG (Drive ID `126zfPsyKDBSE0eCHWnD8Vo7d3TCdZGmG`,
   Git blob `a73e3a37e932802b43e3d59cebabe934b23c7be1`,
   129,559 B) is embedded as WPF resource in the page tab.
5. The ORIGINAL real Embasic source theme logos remain:
   `Assets/Logo-Light.png` blob `2b4aadbf85bce04c5e46b9a5afeaa1ff97c4cc4b`;
   `Assets/Logo-Dark.png` blob `6cd10909b52479c12f5029b376f4699a0b789a6e`.
   The 1024 icon, transparent PNG and preview light/dark image variants
   remain in Google Drive Logo folder; they are not represented as
   integrated WPF assets yet.
6. The research HTML prototype v1.1.1, ZIP and its logo/JavaScript
   tests do **not** qualify as a compiled or validated Windows program.
   No offline browser storage/actual Windows GUI test, native printing,
   DOCX/UDF fidelity or installer is claimed from that prototype.

## Technical boundaries

The new icons do not replace the original two theme assets and do not
depend on a network request. No paid API/SDK, account, cloud or mandatory
online feature was introduced. Preview is non-editable. The WPF secure
`.emd` button remains DISABLED. User note content/filenames are never
provided to any advertising provider. UI screenshot/DPI test remains
NOT RUN. True UDF writer must pass the real UYAP document editor and
cannot be inferred from HTML/PDF preview.

[Tur 08 Windows CI](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41421579049)
successfully compiled WPF plus automated geometry/security tests, but
that is NOT an interactive desktop acceptance.
