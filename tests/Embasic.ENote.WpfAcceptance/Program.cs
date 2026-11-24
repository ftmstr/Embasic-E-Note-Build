using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Printing;
using System.Windows.Controls;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Xps.Packaging;
using System.Xml.Linq;
using Embasic.ENote.App.Preview;
using Embasic.ENote.App.Printing;
using Embasic.ENote.Core.Markdown;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private const string SourceText = "Kaydedilmiş metin";
    private const string EditedText = "# EMBASIC E-NOTE TUR37\n\nKaydedilmemiş **Çığ ve İğdır**\nSON PARAGRAF TUR37";
    private const string PrintPreviewTitle = "A4 Baskı Önizlemesi";
    private const string ConfirmTitle = "UDF Kopyası";
    private const string SaveTitle = "Yeni imzasız UDF kopyası oluştur";

    [MTAThread]
    private static void Main(string[] args)
    {
        if (args is ["--workspace-search", var searchApplication])
        {
            CheckWorkspaceSearch(searchApplication);
            return;
        }
        if (args is ["--external-revision", var revisionApplication])
        {
            CheckExternalRevision(revisionApplication);
            return;
        }
        if (args is ["--workspace-files", var workspaceApplication])
        {
            CheckWorkspaceFiles(workspaceApplication);
            return;
        }
        if (args is ["--editor-workflow", var editorApplication])
        {
            CheckEditorWorkflow(editorApplication);
            return;
        }
        if (args is ["--pdf-save-dialog", var application, var promptQueue, var outputDirectory])
        {
            CheckPdfSaveDialogs(application, promptQueue, outputDirectory);
            return;
        }
        if (args is ["--pdf-cancel", var pdfQueue, var pdfOutput])
        {
            CheckPdfCancellation(pdfQueue, pdfOutput);
            return;
        }
        if (args is ["--spooler", var queueName, var output, var scenario])
        {
            RunSta(() => WindowsSpoolerAcceptance.Run(queueName, output, scenario));
            return;
        }
        if (args.Length is < 1 or > 2)
            throw new ArgumentException("Expected the WPF executable path and optionally a new UDF evidence path.");
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            throw new InvalidOperationException("An interactive Windows session is required.");
        string executable = Path.GetFullPath(args[0]);
        string? evidence = args.Length == 2 ? Path.GetFullPath(args[1]) : null;
        var directory = Directory.CreateTempSubdirectory("enote-wpf-udf-");
        string source = Path.Combine(directory.FullName, "source.md");
        string layout = source + ".enote-layout.json";
        string created = Path.Combine(directory.FullName, "source-kopya.udf");
        string collisionSource = Path.Combine(directory.FullName, "collision.md");
        string existing = Path.Combine(directory.FullName, "collision-kopya.udf");
        string landscapeSource = Path.Combine(directory.FullName, "landscape.md");
        string rejected = Path.Combine(directory.FullName, "landscape-kopya.udf");
        File.WriteAllText(source, SourceText);
        var layouts = new DocumentPageSettingsStore();
        var portrait = PageLayoutSettings.Default with
        {
            LeftMarginMm = 17, RightMarginMm = 23, TopMarginMm = 19, BottomMarginMm = 27
        };
        layouts.Save(source, portrait, expectedSha256: null);
        File.WriteAllText(collisionSource, SourceText);
        File.WriteAllText(landscapeSource, SourceText);
        string landscapeLayout = landscapeSource + ".enote-layout.json";
        layouts.Save(landscapeSource, PageLayoutSettings.Default with { Landscape = true }, expectedSha256: null);
        byte[] savedLandscape = File.ReadAllBytes(landscapeLayout);
        byte[] savedNote = File.ReadAllBytes(source), savedPage = File.ReadAllBytes(layout);
        byte[] existingBytes = [0x45, 0x4E, 0x4F, 0x54, 0x45];
        File.WriteAllBytes(existing, existingBytes);

        using var process = Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = directory.FullName })
            ?? throw new InvalidOperationException("Could not start the WPF application.");
        try
        {
            var window = Wait(() =>
            {
                process.Refresh();
                return process.MainWindowHandle != IntPtr.Zero
                    ? AutomationElement.FromHandle(process.MainWindowHandle) : null;
            }, "E-Note main window");
            Select(ById(window, "MarkdownNotesTab"));
            Require(!ById(window, "ExportMarkdownUdfButton").Current.IsEnabled, "Copy enabled without an open note.");
            Require(!ById(window, "PrintPreviewButton").Current.IsEnabled, "Print preview enabled without an open note.");
            Require(!ById(window, "NewSecureNote").Current.IsEnabled, "Secure note must stay disabled.");
            OpenSource(window, process.Id, source);
            var buffer = ById(window, "MarkdownSourceText");
            var value = (ValuePattern)buffer.GetCurrentPattern(ValuePattern.Pattern);
            Require(value.Current.Value == SourceText, "The editor did not load the synthetic source.");
            value.SetValue(EditedText);
            Require(value.Current.Value == EditedText, "The editor did not retain the unsaved edit.");

            BeginCopy(window, process.Id, accept: false);
            WaitClosed(process.Id, ConfirmTitle);
            AssertFiles("confirmation cancellation", existing);
            Console.WriteLine("PASS initial gates and confirmation cancellation leave source and destination unchanged");

            BeginCopy(window, process.Id, accept: true);
            Click(DialogButton(Dialog(process.Id, SaveTitle), ["2"], ["Cancel", "İptal"]));
            WaitClosed(process.Id, SaveTitle);
            Require(!File.Exists(created), "Cancelling the save dialog created the default file.");
            AssertFiles("save dialog cancellation", existing);
            Console.WriteLine("PASS native save dialog cancellation leaves no new file");

            OpenSource(window, process.Id, collisionSource);
            BeginCopy(window, process.Id, accept: true);
            AcceptSuggestedPath(process.Id, existing);
            WaitClosed(process.Id, SaveTitle);
            var warning = FindDialog(process.Id, "Embasic E-Note", "UDF kopyası oluşturulamadı");
            Click(DialogButton(warning, ["2", "1"], ["OK", "Tamam"]));
            AssertFiles("existing destination refusal", existing);
            Console.WriteLine("PASS an existing target remains byte-identical after refusal");

            OpenSource(window, process.Id, source);
            Require(value.Current.Value == EditedText, "Switching tabs discarded the unsaved edit.");
            BeginCopy(window, process.Id, accept: true);
            AcceptSuggestedPath(process.Id, created);
            Wait(() => File.Exists(created) ? created : null, "new UDF file");
            using (var archive = ZipFile.OpenRead(created))
            {
                Require(archive.Entries.Count == 1 && archive.Entries[0].FullName == "content.xml", "Unexpected UDF members.");
                using var stream = archive.Entries[0].Open();
                var root = XElement.Load(stream);
                var page = root.Element("properties")?.Element("pageFormat");
                Require(root.Element("content")?.Value == EditedText + "\n", "UDF does not contain the current unsaved text.");
                Require((string?)page?.Attribute("mediaSizeName") == "1" &&
                    (string?)page?.Attribute("paperOrientation") == "1", "UDF is not A4 portrait.");
                Require((string?)page?.Attribute("leftMargin") == "48.19" &&
                    (string?)page?.Attribute("rightMargin") == "65.20" &&
                    (string?)page?.Attribute("topMargin") == "53.86" &&
                    (string?)page?.Attribute("bottomMargin") == "76.54",
                    "The UDF did not retain the selected note's four asymmetric margins.");
            }
            AssertFiles("successful output", created, existing);
            Console.WriteLine("PASS successful copy contains unsaved text and A4 portrait without saving the note");

            OpenSource(window, process.Id, landscapeSource);
            BeginCopy(window, process.Id, accept: true);
            AcceptSuggestedPath(process.Id, rejected);
            warning = FindDialog(process.Id, "Embasic E-Note", "UDF kopyası oluşturulamadı");
            Click(DialogButton(warning, ["2", "1"], ["OK", "Tamam"]));
            AssertFiles("landscape refusal", created, existing);
            Require(File.ReadAllText(landscapeSource) == SourceText &&
                File.ReadAllBytes(landscapeLayout).SequenceEqual(savedLandscape), "Landscape source or sidecar changed.");
            Require(ById(window, "MarkdownSourceText").Current.IsEnabled, "The source editor became unavailable.");
            Console.WriteLine("PASS landscape is rejected without a UDF file or sidecar write");
            if (evidence is not null) File.Copy(created, evidence, overwrite: false);
            Console.WriteLine("5/5 Tur39 native WPF UDF copy interaction checks passed.");

            OpenSource(window, process.Id, source);
            string longNote = MultiPageMarkdown();
            value.SetValue(longNote);
            var beforePreview = Directory.GetFiles(directory.FullName).ToDictionary(path => path, File.ReadAllBytes);
            bool nativePrintCancelled = CheckPrintPreview(window, process.Id);
            Require(value.Current.Value == longNote, "Preview changed or discarded the unsaved buffer.");
            var afterPreview = Directory.GetFiles(directory.FullName);
            Require(afterPreview.Length == beforePreview.Count && afterPreview.All(path =>
                beforePreview.TryGetValue(path, out var before) && File.ReadAllBytes(path).SequenceEqual(before)),
                "Preview changed files in the note directory.");
            Console.WriteLine("PASS WPF preview opens and navigates the unsaved draft without creating or modifying files");
            CheckPagination();
            Console.WriteLine("4/4 Tur41 offline A4 print preview checks passed.");
            CheckA4Print(directory.FullName);
            Console.WriteLine("4/4 Tur42 A4 print selection and XPS checks passed.");
            if (nativePrintCancelled)
                Console.WriteLine("1/1 Tur42 native Windows print dialog cancellation checks passed.");
        }
        catch (Exception error)
        {
            DumpWindows(process.Id);
            throw new InvalidOperationException("Hosted WPF acceptance failed.", error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            directory.Delete(recursive: true);
        }

        void AssertFiles(string stage, params string[] expected)
        {
            Require(File.ReadAllBytes(source).SequenceEqual(savedNote), $"Source changed during {stage}.");
            Require(File.ReadAllBytes(layout).SequenceEqual(savedPage), $"Page sidecar changed during {stage}.");
            Require(File.ReadAllBytes(existing).SequenceEqual(existingBytes), $"Existing target changed during {stage}.");
            Require(Directory.GetFiles(directory.FullName, "*.udf").Order().SequenceEqual(expected.Order()),
                $"Unexpected UDF output during {stage}.");
            Require(!Directory.EnumerateFiles(directory.FullName, "*.enote-udf-tmp").Any(), "Staging leaked.");
        }
    }

    private static string MultiPageMarkdown() =>
        "# İlk Sayfa: Çığ\n\n" + string.Join("\n\n", Enumerable.Range(1, 180).Select(i =>
            $"Paragraf {i} — A4 sayfalama örneği. **İğdır** ve Türkçe karakterler.")) +
        "\n\n# SON SAYFA İŞARETİ";

    private static bool CheckPrintPreview(AutomationElement window, int pid)
    {
        Click(ById(window, "PrintPreviewButton"));
        var preview = Dialog(pid, PrintPreviewTitle);
        var previous = ById(preview, "PreviousPageButton");
        var next = ById(preview, "NextPageButton");
        var counter = ById(preview, "PageCounter");
        Require(counter.Current.Name.StartsWith("Sayfa 1 / ", StringComparison.Ordinal) &&
            !previous.Current.IsEnabled && next.Current.IsEnabled, "The initial multipage view is incorrect.");
        Require(ById(preview, "PrintGeometry").Current.Name ==
            "A4 · Dikey · 210 × 297 mm · Kenar boşlukları (sol, sağ, üst, alt) 17, 23, 19, 27 mm",
            "Preview did not use the selected note's geometry.");
        Click(next);
        Wait(() => counter.Current.Name.StartsWith("Sayfa 2 / ", StringComparison.Ordinal) ? counter : null, "preview page 2");
        Click(previous);
        Wait(() => counter.Current.Name.StartsWith("Sayfa 1 / ", StringComparison.Ordinal) ? counter : null, "preview page 1");
        bool cancelled = TryCancelWindowsPrint(preview, pid);
        Click(ById(preview, "ClosePrintPreview"));
        WaitClosed(pid, PrintPreviewTitle);
        return cancelled;
    }

    private static bool TryCancelWindowsPrint(AutomationElement preview, int pid)
    {
        try
        {
            using var server = new LocalPrintServer();
            using var queues = server.GetPrintQueues([EnumeratedPrintQueueTypes.Local]);
            if (!queues.Any())
            {
                Console.WriteLine("NOT RUN Tur42 native print dialog: no local queue on this runner.");
                return false;
            }
        }
        catch (PrintSystemException error)
        {
            Console.WriteLine("NOT RUN Tur42 native print dialog: " + error.Message);
            return false;
        }

        Click(ById(preview, "PrintButton"));
        CancelPrintDialog(pid);
        var status = ById(preview, "PrintStatus");
        Wait(() => status.Current.Name == "Yazdırma iptal edildi." ? status : null, "cancellation status");
        return true;
    }

    private static void CancelPrintDialog(int pid)
    {
        var print = Wait(() => LocateDialog(pid, "Print") ?? LocateDialog(pid, "Yazdır"), "native Windows print dialog");
        Click(DialogButton(print, ["2"], ["Cancel", "İptal"]));
    }

    private static void CheckPdfCancellation(string queueName, string output)
    {
        output = Path.GetFullPath(output);
        var result = PrintSubmissionResult.Queued;
        ExceptionDispatchInfo? failure = null;
        var dialogThread = new Thread(() =>
        {
            try
            {
                using var server = new LocalPrintServer();
                using var observer = server.GetPrintQueue(queueName);
                WindowsSpoolerAcceptance.RequireDisposableQueue(observer, queueName, output, pdf: true);
                var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                var owner = new System.Windows.Window { Title = "Embasic E-Note PDF acceptance", Width = 420, Height = 180 };
                application.MainWindow = owner;
                owner.Show();
                try
                {
                    var dialog = LocalA4Print.CreateDialog(4, 3);
                    dialog.PrintQueue = observer;
                    result = LocalA4Print.ShowAndSubmit(dialog, 3, landscape: false,
                        () => throw new InvalidOperationException("Cancelled dialog attempted to create print pages."));
                    Require(result == PrintSubmissionResult.Cancelled && !observer.GetPrintJobInfoCollection().Any() && !File.Exists(output),
                        "PDF cancellation created a job or output.");
                }
                finally
                {
                    owner.Close();
                    application.Shutdown();
                }
            }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        }) { IsBackground = true };
        dialogThread.SetApartmentState(ApartmentState.STA);
        dialogThread.Start();
        CancelPrintDialog(Environment.ProcessId);
        Require(dialogThread.Join(TimeSpan.FromSeconds(20)), "PDF dialog cancellation did not return.");
        failure?.Throw();
        Require(result == PrintSubmissionResult.Cancelled, "PDF print dialog was not cancelled.");
        Console.WriteLine("1/1 Tur44 native PDF cancellation without job or file checks passed.");
    }

    private static void CheckPagination()
    {
        RunSta(() =>
        {
            var layout = PageLayoutSettings.Default.WithMargins(17, 23, 19, 27);
            var portrait = A4MarkdownPagination.Create(MultiPageMarkdown(), layout);
            using var first = portrait.GetPage(0);
            Near(first.Size.Width, 210 * 96d / 25.4);
            Near(first.Size.Height, 297 * 96d / 25.4);
            Near(first.ContentBox.Left, 17 * 96d / 25.4);
            Near(first.ContentBox.Top, 19 * 96d / 25.4);
            Near(first.Size.Width - first.ContentBox.Right, 23 * 96d / 25.4);
            Near(first.Size.Height - first.ContentBox.Bottom, 27 * 96d / 25.4);
            var zoomed = A4MarkdownPagination.Create(MultiPageMarkdown(), layout with { ZoomPercent = 150 });
            Require(zoomed.PageCount == portrait.PageCount, "Editor zoom changed physical pagination.");
            var landscape = A4MarkdownPagination.Create("Türkçe", layout with { Landscape = true });
            using var widePage = landscape.GetPage(0);
            Near(widePage.Size.Width, 297 * 96d / 25.4);
            Near(widePage.Size.Height, 210 * 96d / 25.4);
            Console.WriteLine("PASS portrait and landscape use A4 geometry, and editor zoom leaves pagination unchanged");

            var source = (FlowDocument)portrait.Source;
            var finalParagraph = (Paragraph)source.Blocks.LastBlock;
            Require(new TextRange(finalParagraph.ContentStart, finalParagraph.ContentEnd).Text == "SON SAYFA İŞARETİ",
                "Last heading was lost before pagination.");
            Require(portrait.IsPageCountValid && portrait.PageCount > 1 &&
                portrait.GetPageNumber(finalParagraph.ContentStart) == portrait.PageCount - 1,
                "The last heading did not reach the last page.");
            Require(A4MarkdownPagination.Create("", layout).PageCount == 1, "Empty notes need one blank page.");
            Console.WriteLine("PASS flowing content reaches the last page and empty notes get one blank page");

            RequireUnsupported(() => A4MarkdownPagination.Create("Türkçe", layout with { Paper = PageLayoutCatalog.Get("A5") }));
            RequireUnsupported(() => A4MarkdownPagination.Create(new string('a', MarkdownPreviewParser.MaximumPreviewCharacters + 1), layout));
            RequireUnsupported(() => A4MarkdownPagination.Create(new string('\n', MarkdownPreviewParser.MaximumPreviewLines), layout));
            Console.WriteLine("PASS paper and text limits refuse unsupported or incomplete previews");
        });

        static void Near(double actual, double expected) =>
            Require(Math.Abs(actual - expected) < 0.5, $"Unexpected A4 dimensions: {actual} ≠ {expected} DIP.");

        static void RequireUnsupported(Action create)
        {
            try { create(); }
            catch (NotSupportedException) { return; }
            throw new InvalidOperationException("The preview accepted an unsupported input.");
        }
    }

    private static void CheckA4Print(string directory)
    {
        RunSta(() =>
        {
            var layout = PageLayoutSettings.Default.WithMargins(17, 23, 19, 27);
            var pages = CreatePrintFixture(layout);
            Require(pages.PageCount == 4, "Expected four named test pages.");
            var dialog = LocalA4Print.CreateDialog(pages.PageCount, currentPage: 3);
            Require(dialog.MinPage == 1 && dialog.MaxPage == 4 && dialog.UserPageRangeEnabled &&
                dialog.CurrentPageEnabled && dialog.PageRange.PageFrom == 3,
                "The Windows dialog did not receive the preview's page bounds.");
            Require(ReferenceEquals(pages, LocalA4Print.SelectPages(pages, PageRangeSelection.AllPages, default, 3)),
                "All-page selection changed the source.");
            var current = LocalA4Print.SelectPages(pages, PageRangeSelection.CurrentPage, default, 3);
            Require(current.PageCount == 1 && current.GetPage(1) == DocumentPage.Missing,
                "Current page did not yield exactly one page.");
            Console.WriteLine("PASS all pages and current page use the preview bounds");

            var selected = LocalA4Print.SelectPages(pages, PageRangeSelection.UserPages, new PageRange(2, 3), 1);
            Require(selected.PageCount == 2 && selected.PageSize == pages.PageSize &&
                selected.GetPage(2) == DocumentPage.Missing, "Restricted pagination exposed an extra page.");
            try
            {
                LocalA4Print.SelectPages(pages, PageRangeSelection.UserPages, new PageRange(3, 6), 1);
                throw new InvalidOperationException("An out-of-bounds range was accepted.");
            }
            catch (ArgumentOutOfRangeException) { }
            Console.WriteLine("PASS selected ranges are bounded and exclude unselected indices");

            var ticket = new PrintTicket
            {
                PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4),
                PageOrientation = PageOrientation.Portrait
            };
            var imageable = new System.Windows.Rect(12, 12, pages.PageSize.Width - 24, pages.PageSize.Height - 24);
            var clipped = new System.Windows.Rect(100, 100, pages.PageSize.Width - 200, pages.PageSize.Height - 200);
            Require(LocalA4Print.CanPrint(ticket, PageOrientation.Portrait, imageable, pages) &&
                !LocalA4Print.CanPrint(ticket, PageOrientation.Portrait, clipped, pages) &&
                !LocalA4Print.CanPrint(ticket, PageOrientation.Landscape, imageable, pages),
                "Driver orientation or imageable-area protection failed.");
            ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.NorthAmericaLetter);
            Require(!LocalA4Print.CanPrint(ticket, PageOrientation.Portrait, imageable, pages), "Letter substitution was accepted.");
            Console.WriteLine("PASS A4 driver validation rejects clipping, orientation changes, and Letter substitution");

            AssertXpsPages(selected, Path.Combine(directory, "test-range.xps"), ["A4-N2", "A4-N3"]);
            AssertXpsPages(current, Path.Combine(directory, "test-current.xps"), ["A4-N3"]);
            Console.WriteLine("PASS the real WPF XPS writer serializes only the requested A4 pages in order");
        });
    }

    internal static DocumentPaginator CreatePrintFixture(PageLayoutSettings layout, string[]? headings = null)
    {
        headings ??= ["A4-N1", "A4-N2", "A4-N3", "A4-N4"];
        var pages = A4MarkdownPagination.Create(string.Join("\n\n", headings.Select(text => "# " + text)), layout);
        var flow = (FlowDocument)pages.Source;
        foreach (var block in flow.Blocks.Skip(1)) block.BreakPageBefore = true;
        pages.ComputePageCount();
        return pages;
    }

    private static void AssertXpsPages(DocumentPaginator pages, string path, string[] expected)
    {
        using (var file = new XpsDocument(path, FileAccess.Write))
            XpsDocument.CreateXpsDocumentWriter(file).Write(pages);
        AssertPrintedPages(path, pages.PageSize.Width, pages.PageSize.Height, expected);
    }

    internal static void AssertPrintedPages(string path, double width, double height, string[] expected)
    {
        using var archive = ZipFile.OpenRead(path);
        var documents = archive.Entries.Where(e => e.Name.EndsWith(".fdoc", StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(documents.Length == 1, "Expected exactly one fixed document.");
        var document = documents[0];
        var root = ReadXml(document);
        Require(root.Name.LocalName == "FixedDocument", "Unexpected print document root.");
        var pageReferences = root.Elements(root.Name.Namespace + "PageContent").ToArray();
        Require(pageReferences.Length == expected.Length &&
            archive.Entries.Count(e => e.Name.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase)) == expected.Length,
            "The package contains the wrong number of pages.");
        var documentUri = new Uri("https://print-package.invalid/" + document.FullName);
        for (int i = 0; i < pageReferences.Length; i++)
        {
            string reference = (string?)pageReferences[i].Attribute("Source")
                ?? throw new InvalidDataException("Fixed page reference is missing.");
            var pageUri = new Uri(documentUri, reference);
            Require(pageUri.Host == documentUri.Host, "Unexpected external print page reference.");
            var entry = archive.GetEntry(Uri.UnescapeDataString(pageUri.AbsolutePath.TrimStart('/')))
                ?? throw new InvalidDataException("Referenced fixed page is missing.");
            var page = ReadXml(entry);
            Require(page.Name.LocalName == "FixedPage", "Unexpected print page root.");
            var text = string.Concat(page.Descendants(page.Name.Namespace + "Glyphs").Select(e => (string?)e.Attribute("UnicodeString")));
            Require(text == expected[i], $"Printed wrong source page at index {i}: {text}");
            Require(Math.Abs((double)page.Attribute("Width")! - width) < 0.5 &&
                Math.Abs((double)page.Attribute("Height")! - height) < 0.5, "Output geometry changed.");
        }

        static XElement ReadXml(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            return XElement.Load(stream);
        }
    }

    private static void RunSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static void BeginCopy(AutomationElement window, int pid, bool accept)
    {
        Click(ById(window, "ExportMarkdownUdfButton"));
        var dialog = Dialog(pid, ConfirmTitle);
        ByName(dialog, "Düzenleyicideki metin, kaydedilmemiş değişiklikler dahil, imzasız ve şifresiz yeni bir UDF kopyası olarak yazılır. " +
            "Markdown işaretleri metin olarak kalır. Bu aktarım A4 dikey sayfayı destekler.", ControlType.Text);
        Click(accept ? DialogButton(dialog, ["1"], ["OK", "Tamam"])
            : DialogButton(dialog, ["2"], ["Cancel", "İptal"]));
    }

    private static void OpenSource(AutomationElement window, int pid, string path)
    {
        Click(ById(window, "OpenMarkdownButton"));
        var dialog = Dialog(pid, "Düz Markdown notu aç (.md)");
        FileName(dialog).SetValue(path);
        Click(DialogButton(dialog, ["1"], ["Open", "Aç"]));
    }

    private static void AcceptSuggestedPath(int pid, string path)
    {
        var dialog = Dialog(pid, SaveTitle);
        string suggestion = FileName(dialog).Current.Value;
        Require(suggestion == Path.GetFileName(path) || suggestion == Path.GetFileNameWithoutExtension(path),
            $"Unexpected destination suggested by native dialog: {suggestion}.");
        Click(DialogButton(dialog, ["1"], ["Save", "Kaydet"]));
    }

    private static ValuePattern FileName(AutomationElement dialog) =>
        (ValuePattern)Wait(() => Find(dialog, AutomationElement.AutomationIdProperty, "1148", ControlType.Edit)
            ?? Find(dialog, AutomationElement.AutomationIdProperty, "1001", ControlType.Edit)
            ?? Find(dialog, AutomationElement.AutomationIdProperty, "FileNameControlHost")?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit))
            ?? Find(dialog, AutomationElement.NameProperty, "File name:", ControlType.Edit), "native file name field")
            .GetCurrentPattern(ValuePattern.Pattern);

    private static AutomationElement DialogButton(AutomationElement dialog, string[] ids, string[] names) =>
        Wait(() => names.Select(n => Find(dialog, AutomationElement.NameProperty, n, ControlType.Button))
            .Concat(ids.Select(id => Find(dialog, AutomationElement.AutomationIdProperty, id, ControlType.Button)))
            .FirstOrDefault(e => e is not null), $"dialog button {string.Join('/', names)}");

    private static AutomationElement Dialog(int pid, string title) =>
        Wait(() => LocateDialog(pid, title), $"dialog {title}");

    private static AutomationElement FindDialog(int pid, string title, string prefix) =>
        Wait(() =>
        {
            var dialog = LocateDialog(pid, title);
            if (dialog is null) return null;
            bool match = false;
            EnumChildWindows(new IntPtr(dialog.Current.NativeWindowHandle), (window, state) =>
            {
                var text = new StringBuilder(1024);
                const uint getText = 0x000D, abortIfHung = 0x0002;
                SendMessageTimeout(window, getText, (nuint)text.Capacity, text, abortIfHung, 200, out _);
                match = text.ToString().StartsWith(prefix, StringComparison.Ordinal);
                return !match;
            }, IntPtr.Zero);
            return match ? dialog : null;
        }, $"warning {prefix}");

    private static AutomationElement? LocateDialog(int pid, string title)
    {
        // Owned shell dialogs can sit outside the UI Automation root's direct children.
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != pid || !IsWindowVisible(window)) return true;
            if (!string.Equals(WindowText(window), title, StringComparison.Ordinal)) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found == IntPtr.Zero ? null : AutomationElement.FromHandle(found);
    }

    private delegate bool WindowCallback(IntPtr window, IntPtr state);

    private static string WindowText(IntPtr window)
    {
        var text = new StringBuilder(1024);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private static void DumpWindows(int pid)
    {
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != pid || !IsWindowVisible(window)) return true;
            Console.Error.WriteLine($"Visible test window: {WindowText(window)}");
            var element = AutomationElement.FromHandle(window);
            foreach (AutomationElement child in element.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>().Where(x => x.Current.ControlType == ControlType.Text ||
                    x.Current.ControlType == ControlType.Button).Take(25))
                Console.Error.WriteLine($"  {child.Current.AutomationId}: {child.Current.Name}");
            return true;
        }, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr window, WindowCallback callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, nuint size,
        StringBuilder text, uint flags, uint timeout, out nuint result);

    private static void WaitClosed(int pid, string title) =>
        Wait(() => LocateDialog(pid, title) is null ? title : null, $"closure of {title}");

    private static AutomationElement ById(AutomationElement scope, string id) =>
        Wait(() => Find(scope, AutomationElement.AutomationIdProperty, id), id);

    private static AutomationElement ByName(AutomationElement scope, string name, ControlType type) =>
        Wait(() => Find(scope, AutomationElement.NameProperty, name, type), name);

    private static AutomationElement? Find(AutomationElement scope, AutomationProperty property, string value, ControlType? type = null)
    {
        var match = new PropertyCondition(property, value);
        return scope.FindFirst(TreeScope.Descendants, type is null ? match : new AndCondition(match,
            new PropertyCondition(AutomationElement.ControlTypeProperty, type)));
    }

    private static void Select(AutomationElement element) =>
        ((SelectionItemPattern)element.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();

    private static void Click(AutomationElement element) =>
        ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static T Wait<T>(Func<T?> probe, string description) where T : class
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            try { if (probe() is { } found) return found; }
            catch (ElementNotAvailableException) { }
            Thread.Sleep(150);
        }
        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }
}
