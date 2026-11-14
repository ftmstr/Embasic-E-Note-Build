# TUR 37 | UDF tables and desktop editor acceptance

The unsigned writer supports one trailing rectangular table, one paragraph
per cell, and optional font-family and point-size references per paragraph.
The Linux 5.4.20 editor's own two-column table informed the wire format:
equal `columnSpans` weights, names on rows and a body paragraph after the
table. Empty cells use the editor's space-plus-newline convention. A table
with no following body paragraph failed to open in the reference editor.

UYAP 5.4.20 for Linux refused a valid supplementary Unicode character in the
CDATA pool with a JDOM error. The 5.4.20 writer profile rejects supplementary
characters before publication and preserves existing files. This also applies
to cells. The synthetic positive fixture uses only BMP characters.

The Windows Actions job retrieves the official, pinned MSI, extracts it to
runner temp and opens a synthetic UDF with the official editor classes on
the Windows runner. The harness edits the visible Swing document, invokes the
normal editor Save action, opens the file in another JVM and compares the ZIP
payload. No proprietary installer, fonts, source or user document is bundled.
Windows CI evidence is separate from physical Windows acceptance.

The writer stays offline and outside the WPF workflow. WPF secure `.emd`
remains disabled. Font display depends on the local fonts and this fixture
does not test cross-platform pixel equivalence or font embedding.
