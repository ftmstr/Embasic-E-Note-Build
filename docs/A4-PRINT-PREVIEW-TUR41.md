# TUR 41 | Offline A4 print preview

W010 adds a read-only, paginated preview for the active plaintext Markdown
note. Open it from the Notes toolbar or with Ctrl+P. The window takes the
current editor buffer and per-note page settings, including unsaved
changes. It supports portrait and landscape A4 and displays the selected
dimensions and four margins. Previous and Next browse the fixed page
sequence. Closing the preview returns to the editor without saving.
Other paper sizes and input beyond the existing parser budget are refused.

The window formats a fresh inert Markdown document with the existing WPF
renderer, using 96 device-independent units per inch. The read-only
viewport scales to the window without changing document pagination.
The editor geometry zoom remains independent. The page window has no
printer command, PDF writer, preview file, account or network dependency.

Hosted Windows acceptance checks real window interaction and file
preservation, A4 geometry and margins in the WPF paginator, last-page
content and an empty document, and explicit input rejection.
EXP-007 continues to pass the independently generated Markdown/table
specimens and the actual WPF UDF copy through the official UYAP editor.

W010 is IN PROGRESS. Physical Windows/DPI, real printer, PDF and physical
security are separate NOT RUN acceptance gates. Secure .emd is disabled.
