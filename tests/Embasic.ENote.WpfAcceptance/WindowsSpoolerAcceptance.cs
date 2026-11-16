using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Security.Cryptography;
using System.Windows.Controls;
using Embasic.ENote.App.Printing;
using Embasic.ENote.Core.Printing;

internal static class WindowsSpoolerAcceptance
{
    private static readonly string[] PdfHeadings =
        ["SAYFA 1 Şafak", "SAYFA 2 Çığ ve İğdır", "SAYFA 3 Ölçü, şüphe, ıslak", "SAYFA 4 Güneş"];

    internal static void RequireDisposableQueue(PrintQueue queue, string queueName, string output, bool pdf)
    {
        string prefix = pdf ? "Embasic ENote TUR44 " : "Embasic ENote TUR43 ";
        string driver = queue.QueueDriver.Name;
        bool expectedDriver = pdf
            ? string.Equals(driver, "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase)
            : driver.StartsWith("Microsoft XPS Document Writer", StringComparison.Ordinal);
        if (!queueName.StartsWith(prefix, StringComparison.Ordinal) ||
            !string.Equals(queue.QueuePort.Name, output, StringComparison.OrdinalIgnoreCase) ||
            !expectedDriver || File.Exists(output) ||
            (pdf && !output.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Expected an isolated test queue, its driver, and a new output file.");
    }

    public static void Run(string queueName, string output, string scenario)
    {
        bool landscape = scenario switch
        {
            "portrait-range" => false,
            "landscape-current" => true,
            _ => throw new ArgumentException("Choose portrait-range or landscape-current.", nameof(scenario))
        };
        output = Path.GetFullPath(output);
        using var server = new LocalPrintServer();
        using var observer = new PrintQueue(server, queueName, PrintSystemDesiredAccess.AdministratePrinter);
        bool pdf = output.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        RequireDisposableQueue(observer, queueName, output, pdf);
        var layout = (PageLayoutSettings.Default with { Landscape = landscape }).WithMargins(17, 23, 19, 27);
        var pages = Program.CreatePrintFixture(layout, pdf ? PdfHeadings : null);
        if (pages.PageCount != 4)
            throw new InvalidOperationException("Expected four named print fixture pages.");

        observer.Pause();
        try
        {
            // Check refusal while the private queue is paused so a wrongly accepted job cannot disappear.
            var rejected = LocalA4Print.CreateDialog(pages.PageCount, 3);
            rejected.PrintQueue = server.GetPrintQueue(queueName);
            rejected.PageRangeSelection = PageRangeSelection.UserPages;
            rejected.PageRange = new PageRange(3, 5);
            try
            {
                LocalA4Print.Submit(rejected, pages, 3, landscape);
                throw new InvalidOperationException("The invalid range was accepted.");
            }
            catch (ArgumentOutOfRangeException error) when (error.ParamName == "range") { }
            if (observer.GetPrintJobInfoCollection().Any() || File.Exists(output))
                throw new InvalidOperationException("Invalid range created a print job or output.");

            var dialog = LocalA4Print.CreateDialog(pages.PageCount, 3);
            dialog.PrintQueue = server.GetPrintQueue(queueName);
            dialog.PageRangeSelection = landscape ? PageRangeSelection.CurrentPage : PageRangeSelection.UserPages;
            dialog.PageRange = new PageRange(2, 3);
            LocalA4Print.Submit(dialog, pages, 3, landscape);

            var job = Wait(() => observer.GetPrintJobInfoCollection().SingleOrDefault(), "paused spooler job");
            if (job.JobIdentifier <= 0 || job.Name != "Embasic E-Note — A4 Markdown" || File.Exists(output))
                throw new InvalidOperationException("The paused queue did not retain the submitted job.");
            Console.WriteLine($"Spooler accepted {scenario} as job {job.JobIdentifier}; invalid range yielded no job.");
            observer.Resume();

            Wait(() =>
            {
                observer.Refresh();
                var file = new FileInfo(output);
                return file.Exists && file.Length > 0 && !observer.GetPrintJobInfoCollection().Any()
                    ? file : null;
            }, "completed spooler output");

            if (pdf)
            {
                Console.WriteLine($"PDF queue completed {scenario} as job {job.JobIdentifier}; file ready for independent validation.");
                return;
            }
            string[] expected = landscape ? ["A4-N3"] : ["A4-N2", "A4-N3"];
            Program.AssertPrintedPages(output, pages.PageSize.Width, pages.PageSize.Height, expected);
            using var stream = File.OpenRead(output);
            Console.WriteLine($"{scenario} {expected.Length} page(s), {pages.PageSize.Width:F2} × {pages.PageSize.Height:F2} DIP, SHA-256 {Convert.ToHexString(SHA256.HashData(stream))}");
            Console.WriteLine($"1/1 Tur43 {scenario} spooler acceptance and geometry checks passed.");
        }
        catch
        {
            observer.Refresh();
            Console.Error.WriteLine($"Spooler status: {observer.QueueStatus}, jobs: {observer.NumberOfJobs}, output: {File.Exists(output)}");
            foreach (var job in observer.GetPrintJobInfoCollection())
                Console.Error.WriteLine($"Job {job.JobIdentifier}: {job.JobStatus}");
            throw;
        }
    }

    private static T Wait<T>(Func<T?> probe, string stage) where T : class
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(35))
        {
            if (probe() is { } value) return value;
            Thread.Sleep(200);
        }
        throw new TimeoutException("Timed out waiting for " + stage + ".");
    }
}
