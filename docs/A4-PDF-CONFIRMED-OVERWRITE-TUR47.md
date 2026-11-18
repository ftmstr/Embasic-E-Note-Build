# TUR 47 · Confirmed PDF replacement

The real E-Note PDF dialog acceptance retains the preceding No/Cancel checks
and their exact SHA-256 comparison. It then copies the original driver PDF to
a separate evidence file, keeping its hash. With a different unsaved Turkish
draft in the editor, it opens the native print and save dialogs again.

The existing destination is named in the native warning before the test clicks
Yes. The test waits for the PDF file to close and the queue to drain. It checks
that the destination hash changed, the preserved original hash stayed the same,
and the Markdown file, page sidecar and current unsaved draft stayed intact.
The independent PDF reader validates both files: one A4 portrait page each,
including media and crop geometry and the distinct expected Turkish text.

All files and the prompt queue belong to the disposable hosted test. The
physical printer, four-edge and DPI procedure remains in
`A4-PHYSICAL-ACCEPTANCE-TUR46.md`. Physical execution requires an accessible
Windows device and a printer. Record unperformed steps as NOT RUN.
