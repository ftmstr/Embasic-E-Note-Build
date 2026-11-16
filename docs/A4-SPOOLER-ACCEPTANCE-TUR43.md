# TUR 43 | Windows spooler acceptance for A4 output

The GitHub-hosted smoke test can enable the inbox XPS feature when missing.
It provisions a separate local Microsoft XPS Document
Writer queue for each case. Each uses its own new local file port and
only a synthetic four-page Markdown fixture. Invalid ranges must leave
the queue empty. The production submission path sends either pages 2–3
of a portrait document or the current page (3) of a landscape document.
The paused queue must first list the named job with a Windows job ID.
After resuming, the driver must finish the job and produce an XPS package.
The package is read in fixed-document order, checking exact page markers,
page count and A4 geometry with half a device-independent pixel tolerance.
The harness logs a SHA-256 for each job output. Temporary printers,
ports and files are removed after the run.

The XPS driver and the Windows spooler run on the hosted Windows worker.
Physical printer output, Microsoft Print to PDF, physical Windows/DPI
and physical security acceptance are separate. UDF acceptance continues
through the actual WPF copy and official UYAP Editor.
