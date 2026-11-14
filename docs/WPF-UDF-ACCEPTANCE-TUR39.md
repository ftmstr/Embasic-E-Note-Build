# TUR 39 | WPF UDF copy acceptance

The dedicated hosted Windows job starts the real WPF executable with a
synthetic Markdown file, a persisted A4 layout and an existing UDF target.
Windows UI Automation selects the Notes tab, uses the native Open dialog and
edits the visible Markdown text box through its accessibility provider.

The acceptance requires both confirmation and file-picker cancellation,
byte-identical source and sidecar, refusal to overwrite the existing target,
successful new A4 portrait output containing the unsaved editor text, and
landscape refusal without publishing a file. It also checks that the export
command starts disabled without a note and that secure `.emd` stays disabled.
File operations run in a temporary test directory.

The existing official UYAP 5.4.20 Windows job continues to validate both
literal Markdown and structured table/font specimens. Physical Windows,
screen DPI, printer/PDF and signed release acceptance have separate gates.
