using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Windows.Automation;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private const string PdfDraft = "TUR 45 Çığ ve İğdır. Ölçü, şüphe, ıslak.";
    private const string PdfSaveTitle = "Save Print Output As";
    private const string PdfSaveTitleTr = "Yazdırma Çıktısını Farklı Kaydet";
    private const string PdfPromptStatus = "Dosyayı kaydettiyseniz seçtiğiniz konumu kontrol edin.";

    private static void CheckPdfSaveDialogs(string executable, string queueName, string outputDirectory)
    {
        Require(OperatingSystem.IsWindows() && Environment.UserInteractive, "An interactive Windows session is required.");
        Require(queueName.StartsWith("Embasic ENote TUR45 ", StringComparison.Ordinal), "Expected a disposable PDF queue.");
        using var server = new LocalPrintServer();
        using var queue = server.GetPrintQueue(queueName);
        string driver = queue.QueueDriver.Name, port = queue.QueuePort.Name;
        Require(string.Equals(driver, "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase), $"Unexpected PDF driver '{driver}'.");
        Require(string.Equals(port, "PORTPROMPT:", StringComparison.OrdinalIgnoreCase), $"Unexpected PDF prompt port '{port}'.");
        Require(!queue.GetPrintJobInfoCollection().Any(), "The dedicated native PDF prompt queue was not empty.");
        Console.WriteLine($"Using prompt queue '{queue.Name}' on '{port}' with '{driver}'.");

        var directory = Directory.CreateTempSubdirectory("enote-wpf-pdf-");
        string source = Path.Combine(directory.FullName, "not.md");
        string sidecar = source + ".enote-layout.json";
        string cancelled = Path.Combine(outputDirectory, "TUR 45 iptal.pdf");
        string saved = Path.Combine(outputDirectory, "TUR 45 çıktı.pdf");
        File.WriteAllText(source, SourceText);
        new DocumentPageSettingsStore().Save(source, PageLayoutSettings.Default.WithMargins(17, 23, 19, 27),
            expectedSha256: null);
        byte[] originalSource = File.ReadAllBytes(source), originalSidecar = File.ReadAllBytes(sidecar);

        using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(executable)) { WorkingDirectory = directory.FullName })
            ?? throw new InvalidOperationException("Could not start the WPF application.");
        try
        {
            var window = Wait(() =>
            {
                process.Refresh();
                return process.MainWindowHandle != IntPtr.Zero ? AutomationElement.FromHandle(process.MainWindowHandle) : null;
            }, "E-Note main window");
            Select(ById(window, "MarkdownNotesTab"));
            Require(!ById(window, "NewSecureNote").Current.IsEnabled, "Secure note must stay disabled.");
            OpenSource(window, process.Id, source);
            var buffer = (ValuePattern)ById(window, "MarkdownSourceText").GetCurrentPattern(ValuePattern.Pattern);
            Require(buffer.Current.Value == SourceText, "The editor did not load the Markdown file.");
            buffer.SetValue(PdfDraft);
            Click(ById(window, "PrintPreviewButton"));
            var preview = Dialog(process.Id, PrintPreviewTitle);
            ByName(preview, "Sayfa 1 / 1", ControlType.Text);

            var picker = OpenPdfSaveDialog(preview, process.Id, queueName);
            FileName(picker).SetValue(cancelled);
            Click(DialogButton(picker, ["2"], ["Cancel", "İptal"]));
            Wait(() => FindPdfSaveDialog() is null ? "closed" : null, "PDF save cancellation to close");
            string cancelStatus = WaitForPrintStatus(preview);
            Wait(() => !queue.GetPrintJobInfoCollection().Any() ? "drained" : null, "cancelled PDF queue to drain");
            Require(!Directory.EnumerateFileSystemEntries(outputDirectory).Any(), "Cancelling the native save dialog created an output.");
            Console.WriteLine($"Native PDF save cancellation returned: {cancelStatus}");
            Require(cancelStatus == PdfPromptStatus, "The preview incorrectly claimed that the cancelled PDF was submitted.");
            Console.WriteLine("1/1 Tur45 native PDF save cancellation without output checks passed.");

            picker = OpenPdfSaveDialog(preview, process.Id, queueName);
            FileName(picker).SetValue(saved);
            Click(DialogButton(picker, ["1"], ["Save", "Kaydet"]));
            Wait(() => FindPdfSaveDialog() is null ? "closed" : null, "PDF save confirmation to close");
            Require(WaitForPrintStatus(preview) == PdfPromptStatus, "The preview did not report the driver-managed output.");
            Wait(() => IsReady(saved) && !queue.GetPrintJobInfoCollection().Any() ? saved : null, "completed and closed PDF output");
            Require(Directory.EnumerateFileSystemEntries(outputDirectory).SequenceEqual([saved]), "Unexpected PDF output files.");
            Console.WriteLine("1/1 Tur45 native PDF filename confirmation and completed file checks passed.");

            Click(ById(preview, "ClosePrintPreview"));
            WaitClosed(process.Id, PrintPreviewTitle);
            Require(buffer.Current.Value == PdfDraft, "The unsaved draft changed during printing.");
            Require(File.ReadAllBytes(source).SequenceEqual(originalSource) && File.ReadAllBytes(sidecar).SequenceEqual(originalSidecar),
                "The source or page sidecar changed during printing.");
            Console.WriteLine("1/1 Tur45 Markdown, sidecar and unsaved buffer preservation checks passed.");
        }
        catch (Exception error)
        {
            DumpWindows(process.Id);
            throw new InvalidOperationException("Hosted PDF save dialog acceptance failed.", error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            directory.Delete(recursive: true);
        }
    }

    private static AutomationElement OpenPdfSaveDialog(AutomationElement preview, int pid, string queueName)
    {
        Require(FindPdfSaveDialog() is null, "Another native PDF save dialog was already open.");
        Click(ById(preview, "PrintButton"));
        var print = Wait(() => LocateDialog(pid, "Print") ?? LocateDialog(pid, "Yazdır"), "native Windows print dialog");
        var displayedQueue = Wait(() => Find(print, AutomationElement.NameProperty, queueName), "disposable PDF queue in print dialog");
        var selection = (SelectionItemPattern)displayedQueue.GetCurrentPattern(SelectionItemPattern.Pattern);
        if (!selection.Current.IsSelected) selection.Select();
        Require(selection.Current.IsSelected, "The PDF prompt queue was not selected.");
        Click(DialogButton(print, ["1"], ["Print", "Yazdır"]));
        var picker = Wait(FindPdfSaveDialog, "native Microsoft Print to PDF save dialog");
        Console.WriteLine($"Native PDF picker observed: {picker.Current.Name} (PID {picker.Current.ProcessId}, E-Note PID {pid}).");
        return picker;
    }

    private static AutomationElement? FindPdfSaveDialog()
    {
        // The Windows print UI may own this dialog in a different process on the same desktop.
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || WindowText(window) is not (PdfSaveTitle or PdfSaveTitleTr)) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found == IntPtr.Zero ? null : AutomationElement.FromHandle(found);
    }

    private static string WaitForPrintStatus(AutomationElement preview) => Wait(() =>
    {
        string text = ById(preview, "PrintStatus").Current.Name;
        return text.Length == 0 ? null : text;
    }, "E-Note print status");

    private static bool IsReady(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return file.Length > 0;
        }
        catch (IOException) { return false; }
    }
}
