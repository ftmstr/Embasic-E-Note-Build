# Embasic E-Note

**Project status:** Design and planning — no working Windows release yet.

Embasic E-Note is an open-source, offline-first Windows Markdown editor and note-taking application featuring an expandable folder tree, customizable folder icons, tabbed editing, and local file storage.

## Planned file formats

- **`.md`** — standard, unencrypted Markdown for interoperability with other editors.
- **`.emd`** — **Embasic Mark Down**, the planned versioned encrypted note format. Its file encryption will be independent of the optional application lock, folder/subfolder locks, and additional per-note passwords.

The `.emd` format specification will be public. Since the format and application are open source, other software may implement compatible readers; confidentiality depends on cryptographic keys, not proprietary file extensions. The encryption implementation has **not yet been built or audited**.

## Planned features

- Unlimited application-defined nesting of folders and notes, subject to filesystem and hardware limits.
- Custom folder icons (`.ico` and supported image formats) and a built-in icon library.
- Tabbed Markdown editor, source/preview/split modes, and light/dark themes.
- Optional application, folder, subfolder, and individual-note locking.
- An isolated advertising area outside the text editor; no access to note contents.
- Fully usable offline, without mandatory paid APIs, accounts, subscriptions, cloud services, or proprietary software dependencies.

## Proposed technology

C# / .NET 10 LTS / WPF (XAML), with a modular architecture. This stack is proposed and has not yet been implemented in this repository.

## Licensing

The repository is licensed under the [MIT License](LICENSE). Third-party components will be selected for open-source compatibility and reviewed for their own license conditions.

## Development

Development milestones, security design, and acceptance tests will be documented as the implementation progresses. No encryption/security guarantees should be inferred from this planning document alone.
