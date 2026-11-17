# TUR 45 | Native PDF save acceptance

The Windows acceptance opens a synthetic `.md` file in the built E-Note
executable, edits its buffer and enters its A4 print preview. The native
Windows print dialog selects a dedicated Microsoft Print to PDF queue on
the system `PORTPROMPT:` port. The queue and all fixture files are temporary.

The first print opens the driver's save picker and cancels after entering
an unused destination. The queue must drain and the output directory must
stay empty. The same preview then prints again. After confirming a new file
name containing a space and Turkish characters, the queue must drain and the
file must be closed and independently readable. The PDF reader verifies one
A4 portrait page, the unchanged media and crop geometry and exact Turkish
text from the unsaved buffer. The Markdown file, page sidecar and editor
buffer are checked separately for changes.

The separate file-port PDF and XPS regressions continue to run, including
selected portrait and landscape pages. This test uses the hosted Windows
desktop. Physical printing, physical DPI and security are separate gates.
