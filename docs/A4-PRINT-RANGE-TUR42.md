# TUR 42 | A4 page range and Windows printing

The read-only A4 preview opens the standard Windows print dialog with
the number of preview pages. Users can choose all pages, the current
page, or one inclusive range. The adapter passes only those pages to
the writer in the original order. The print run takes a separate
paginator from the preview's in-memory Markdown and layout snapshot.

Before submission, the selected Windows queue must be locally installed.
The validated A4 media and orientation must match the preview. The
driver's imageable rectangle must contain the preview's content box.
A rejected ticket, an invalid range or a cancelled dialog submits no
print job. The print status distinguishes a queued job from cancellation
and error. The preview and the printing command leave note files and
layout sidecars alone. The spooler job name excludes the source path.

Hosted acceptance exercises range bounds, all/current/range selection,
ticket and clipping refusal, and actual WPF XPS serialization of the
selected synthetic pages. It also checks native dialog cancellation
when the runner has a local print queue. XPS here is a temporary
test artifact. Physical Windows/DPI, actual printer output, Windows
PDF output and physical security are separate acceptance gates.

The separate UDF writer keeps the no-overwrite contract on Linux by
publishing its staged file with a native no-replace rename. If the
runtime or filesystem rejects that operation, the export fails and
the staging file is cleaned. This does not establish power-loss or
physical storage acceptance. The Windows publication path is unchanged.
