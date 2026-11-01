# Embasic E-Note

**Current status (2026-11-01):** Early, non-editing Windows WPF application **source scaffold**.
The first Windows CI run built successfully with 0 warnings / 0 errors, and 7/7
bootstrap smoke checks passed. **There is no usable note editor, encrypted .emd
implementation, GUI acceptance, UDF writer or installable v1.0 release yet.**

## Development evidence

- [Tur 01 draft pull request #1](https://github.com/ftmstr/Embasic-E-Note-Build/pull/1)
- [Windows .NET 10 CI run #1](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41324011720)
- [CI evidence and scope](docs/CI-EVIDENCE-TUR01.md)
- [Build instructions](docs/DEVELOPMENT.md)
- [Security limitations](docs/SECURITY.md)

The code was committed to a feature branch for review. The default main branch
remains at its earlier design/documentation stage until this pull request is
reviewed and merged.

## Purpose and platform

Embasic E-Note is an MIT-licensed, open-source, offline-first Windows Markdown
note editor under construction. The target stack is **C# / .NET 10 LTS / WPF
(XAML) / MVVM**. It will support a real workspace folder tree, customizable icons,
tabbed Markdown editing, printing and local document export.

## Planned note formats

- **`.md`:** interoperable *unencrypted* Markdown.
- **`.emd`:** versioned **Embasic Mark Down** encrypted document. Disk encryption
  will apply even when optional UI, folder and note locks are disabled.

**Critical current limitation:** the encrypted .emd implementation, secure Windows
key handling and recovery are **NOT IMPLEMENTED**. This scaffold deliberately
refuses to route .emd files through its plaintext .md reader. No operation in
the WPF shell opens, creates or saves a note.

The .emd specification will be public. Protection depends on authenticated
cryptography and secret keys, not obscurity or a proprietary filename extension.

## v1.0 planned release gates

The intended P0 scope includes .md/.emd storage, key and lock management, safe
preview/editor, local printing, PDF/DOCX/RTF/HTML/TXT/MD export, and a **separate**
UDF writer accepted by real UYAP Doküman Editörü open/save comparisons.
A designed ad slot is restricted to the bottom of the left folder panel and
must never receive note content. None of those product-level acceptance gates
is complete just because the bootstrap compiles.

No mandatory paid API, account, cloud platform, subscription or paid runtime
license will be required. All future third-party dependencies must be
license-verified and recorded in NOTICE/SBOM.

## License

[MIT](LICENSE). Existing attribution is preserved.
