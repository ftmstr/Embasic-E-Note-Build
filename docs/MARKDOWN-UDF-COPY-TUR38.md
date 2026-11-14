# TUR 38 | Unsigned Markdown copy

The Notes toolbar offers **UDF Kopyası** for an active plaintext `.md` tab.
The confirmation explains that the current buffer, including unsaved edits,
becomes an unsigned, unencrypted new A4 portrait UDF. The source note and
its layout sidecar are untouched. The writer refuses an existing destination.

The copy carries Markdown characters literally. It normalizes CRLF and CR to
LF, preserves empty lines and keeps the final paragraph delimiter required by
the UDF writer. Rich Markdown rendering is outside this profile. Structured
table and font support remains available to the separate writer API. UYAP
5.4.20's supplementary Unicode restriction remains enforced.

The Windows CI editor job retains the existing table/font fixture and adds a
literal Markdown fixture through the same adapter used by WPF. The job opens,
edits, saves and reopens each specimen in the official editor and compares
the archive. WPF's dialog and physical interaction have separate acceptance.
Secure `.emd` remains disabled. The export runs locally without a network
connection or external document editor.
