using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Xml.Linq;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage;

internal static class Program
{
    private const string SourceText = "Kaydedilmiş metin";
    private const string EditedText = "# Kaydedilmemiş Çığ ve İğdır";
    private const string ConfirmTitle = "UDF Kopyası";
    private const string SaveTitle = "Yeni imzasız UDF kopyası oluştur";

    [MTAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Expected the WPF executable path.");
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            throw new InvalidOperationException("An interactive Windows session is required.");
        string executable = Path.GetFullPath(args[0]);
        var directory = Directory.CreateTempSubdirectory("enote-wpf-udf-");
        string source = Path.Combine(directory.FullName, "source.md");
        string layout = source + ".enote-layout.json";
        string existing = Path.Combine(directory.FullName, "existing.udf");
        string created = Path.Combine(directory.FullName, "new-copy.udf");
        string suggested = Path.Combine(directory.FullName, "source-kopya.udf");
        string rejected = Path.Combine(directory.FullName, "landscape.udf");
        File.WriteAllText(source, SourceText);
        new DocumentPageSettingsStore().Save(source, PageLayoutSettings.Default, expectedSha256: null);
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
            Require(!ById(window, "NewSecureNote").Current.IsEnabled, "Secure note must stay disabled.");
            Click(ById(window, "OpenMarkdownButton"));
            SubmitPath(process.Id, "Düz Markdown notu aç (.md)", source, open: true);
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
            Require(!File.Exists(suggested), "Cancelling the save dialog created the default file.");
            AssertFiles("save dialog cancellation", existing);
            Console.WriteLine("PASS native save dialog cancellation leaves no new file");

            BeginCopy(window, process.Id, accept: true);
            SubmitPath(process.Id, SaveTitle, existing, open: false);
            WaitClosed(process.Id, SaveTitle);
            var warning = FindDialog(process.Id, "Embasic E-Note", "UDF kopyası oluşturulamadı");
            Click(DialogButton(warning, ["2", "1"], ["OK", "Tamam"]));
            AssertFiles("existing destination refusal", existing);
            Console.WriteLine("PASS an existing target remains byte-identical after refusal");

            BeginCopy(window, process.Id, accept: true);
            SubmitPath(process.Id, SaveTitle, created, open: false);
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
            }
            AssertFiles("successful output", created, existing);
            Console.WriteLine("PASS successful copy contains unsaved text and A4 portrait without saving the note");

            Select(ByName(window, "Sayfa Düzeni (önizleme)", ControlType.TabItem));
            var orientation = ById(window, "PageOrientationComboBox");
            var expand = (ExpandCollapsePattern)orientation.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            expand.Expand();
            Select(ByName(orientation, "Yatay", ControlType.ListItem));
            if (expand.Current.ExpandCollapseState == ExpandCollapseState.Expanded) expand.Collapse();
            Select(ById(window, "MarkdownNotesTab"));
            BeginCopy(window, process.Id, accept: true);
            SubmitPath(process.Id, SaveTitle, rejected, open: false);
            warning = FindDialog(process.Id, "Embasic E-Note", "UDF kopyası oluşturulamadı");
            Click(DialogButton(warning, ["2", "1"], ["OK", "Tamam"]));
            AssertFiles("landscape refusal", created, existing);
            Require(ById(window, "MarkdownSourceText").Current.IsEnabled, "The source editor became unavailable.");
            Console.WriteLine("PASS landscape is rejected without a UDF file or sidecar write");
            Console.WriteLine("5/5 Tur39 native WPF UDF copy interaction checks passed.");
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

    private static void BeginCopy(AutomationElement window, int pid, bool accept)
    {
        Click(ById(window, "ExportMarkdownUdfButton"));
        var dialog = Dialog(pid, ConfirmTitle);
        ByName(dialog, "Düzenleyicideki metin, kaydedilmemiş değişiklikler dahil, imzasız ve şifresiz yeni bir UDF kopyası olarak yazılır. " +
            "Markdown işaretleri metin olarak kalır. Bu aktarım A4 dikey sayfayı destekler.", ControlType.Text);
        Click(accept ? DialogButton(dialog, ["1"], ["OK", "Tamam"])
            : DialogButton(dialog, ["2"], ["Cancel", "İptal"]));
    }

    private static void SubmitPath(int pid, string title, string path, bool open)
    {
        var dialog = Dialog(pid, title);
        var fileName = Wait(() => Find(dialog, AutomationElement.AutomationIdProperty, "1148", ControlType.Edit)
            ?? Find(dialog, AutomationElement.AutomationIdProperty, "1001", ControlType.Edit)
            ?? Find(dialog, AutomationElement.AutomationIdProperty, "FileNameControlHost")?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit))
            ?? Find(dialog, AutomationElement.NameProperty, "File name:", ControlType.Edit), "native file name field");
        ((ValuePattern)fileName.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
        Click(DialogButton(dialog, ["1"], open ? ["Open", "Aç"] : ["Save", "Kaydet"]));
    }

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
            EnumChildWindows(new IntPtr(dialog.Current.NativeWindowHandle), (window, _) =>
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
