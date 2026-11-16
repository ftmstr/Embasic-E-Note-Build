# TUR 44 | Microsoft Print to PDF acceptance

The hosted Windows job uses the installed Microsoft Print to PDF driver
with a temporary, local, file-backed queue. The optional inbox feature is
enabled only if needed on a disposable GitHub-hosted worker. Synthetic
fixtures contain unique Turkish text on four A4 pages.

The native WPF print dialog is opened with the test PDF queue available
and cancelled. The test observes no job or file. The production
submission path then sends portrait pages 2–3 and, on a separate queue,
the current landscape page 3. An invalid range is also checked before
each submission. The spooler job must finish before an independent PDF
reader validates page count, order, Unicode text and A4 media and crop
boxes, taking PDF rotation into account. The comparison uses a one-point
geometry tolerance. The job logs SHA-256 for each driver output.

Both queues, ports and outputs are temporary. XPS spooler acceptance is
a separate Windows job. The PDF reader is test-only, and this CI evidence
does not exercise a physical printer, physical DPI or physical security.
