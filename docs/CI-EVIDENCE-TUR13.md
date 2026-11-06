# Tur 13 — Public GitHub / Windows CI evidence
Date: **2026-11-06**. Public repository only:
https://github.com/ftmstr/Embasic-E-Note-Build

## Verified entrypoint
The authoritative Google Drive `Kaynak` ENOTE-01,02,03,04,05,06,08,09
and 06.11.2026 Tur12 report were read. Public `main` was
`a10faf48e7cd153a53803192e0ac7de39c998458`.
Stacked Draft PR #9 HEAD `fe0030e2d8dbb3e972a7d3e0f59ce4c691ee21ca`,
#10 `facad8dd09d5df0ba705bc369bacdd11ef1a40d8`,
#11 `849887b5f27088b2e836af687780f7aacecca339`,
#13 `519880ab79fd4300eebeca9fbe2e912f3841b0fa`
all OPEN/DRAFT. Legacy sibling PR #12 CLOSED, not merged.
Verified Tur12 Actions #42112609705 SUCCESS.
[Draft PR #14](https://github.com/ftmstr/Embasic-E-Note-Build/pull/14)
is stacked on PR #13 at its verified SHA. No changes to `main`.

## Small changes
1. `c221db7840591f09393fa779c41ee946f304069b`:
   new pure Core `MarkdownInlineParser`, immutable inert span records,
   max-preview input guard, escaping rules, limited strong/emphasis/
   strikethrough/inline-code, 20 negative/Unicode tests plus new
   Tur13 test project and Windows CI.
2. `af9f3520e7943f0efc6337e6ac59099308eeed8f`:
   WPF FlowDocument `Run`-only inline styles for headings, paragraphs,
   quotes, lists and table cells; escaped `\|` / inline code containing
   `|` stays a single basic table cell; seven further tests.
3. Documentation-only closure commit updates README, NOTICE, Tur13
   evidence and risk register, and local PowerShell status dashboard.

## Real CI log inspected
[Windows Actions run 42173395275](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42173395275)
job `127746200352`: **SUCCESS**, Windows .NET 10 WPF Release:
**Build succeeded, 0 Warning(s), 0 Error(s)**.
- Workspace/PageLayout 31/31
- EMD1 15/15; EMD2/Storage 30/30
- Backup/Rotation 35/35; Tur06 38/38; Tur07 42/42
- Tur08 27/27; Tur09 31/31
- Native RTF Tur10 16/16; Guarded DOCX/ODT Tur11 32/32
- Tur12 safe Markdown preview 21/21; Tur13 inert inline 27/27
**TOTAL 345/345 PASS** on source code HEAD `af9f3520...`.
First 20-test commit run 42173180231 also SUCCESS.

IMPORTANT: This is console verification and headless Windows WPF
compilation, not an interactive Windows GUI, UX/DPI, accessibility,
actual print, or Word/LibreOffice comparison.

## Exact boundaries
- `.md` remains plain UTF-8, with an unencrypted per-document
  layout sidecar; the source text is never transformed/serialized
  by the preview engine.
- Inline support is deliberately a **subset**, not full CommonMark.
  It does not interpret nested/multiple delimiters, autolinks,
  trusted/untrusted HTML, images or link navigation.
- Visual WPF styling uses inert `Run` elements, never WPF `Hyperlink`,
  web browser, remote images or HTML execution. The visual rendering
  itself still needs actual Windows GUI verification.
- Code fences stay literal without applying inline formatting.
- Table cells are a draft basic pipe model; escaped pipes and
  single code span pipes work; complex tables and multiline cells
  are NOT accepted or asserted.
- Original RTF only saves a **new copy**; DOCX/ODT only guarded simple
  single-run paragraphs are writable to a new copy; complex tables,
  drawings/media, signatures and tracked revisions are read-only.
  No fidelity guarantee.
- New secure `.emd` WPF button is **DISABLED**.
- UDF writer real UYAP editor open-save-compare: **NOT RUN**.
- Physical Windows GUI/DPI, real NTFS ACL, disk-full, power-loss,
  malicious symlink/TOCTOU, cross-process CAS and cross-user DPAPI:
  **NOT RUN**. Security release gates remain BLOCKED.

Estimated engineering progress ~35%, not an acceptance metric.
Conditional V1.0 planning window 2027 Q2–Q3 is not a commitment.
No signed installer, EXE/MSI or `Uygulama` Drive upload.
