using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

internal static partial class Program
{
    private static void CheckWorkspaceSearch(string application)
    {
        var fixture = Directory.CreateTempSubdirectory("enote-search-ui-");
        string root = fixture.FullName;
        const string saved = "İĞDE ve kış";
        const string dirty = "# Açık taslak 😀";
        var one = Directory.CreateDirectory(Path.Combine(root, "A"));
        var two = Directory.CreateDirectory(Path.Combine(root, "B"));
        string target = Path.Combine(one.FullName, "not.md");
        string other = Path.Combine(two.FullName, "not.md");
        string nameOnly = Path.Combine(root, "İĞDE başlık.md");
        File.WriteAllText(target, saved);
        File.WriteAllText(other, "Başka sekme");
        File.WriteAllText(nameOnly, "Başlık eşleşmesi");
        File.WriteAllText(Path.Combine(root, "excluded.emd"), "İĞDE gizli");
        string denied = Path.Combine(root, "unreadable.md");
        File.WriteAllText(denied, "İĞDE erişilemez");

        using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(application)) { WorkingDirectory = root })
            ?? throw new IOException("App launch failed");
        try
        {
            var window = Wait(() =>
            {
                process.Refresh();
                return process.MainWindowHandle == IntPtr.Zero ? null : AutomationElement.FromHandle(process.MainWindowHandle);
            }, "workspace search window");

            Click(ById(window, "ChooseWorkspaceButton"));
            const string folderTitle = "Embasic E-Note — Çalışma alanı klasörünü seçin";
            var dialog = Dialog(process.Id, folderTitle);
            var folderField = Wait(() => Find(dialog, AutomationElement.AutomationIdProperty, "1152", ControlType.Edit)
                ?? Find(dialog, AutomationElement.AutomationIdProperty, "1148", ControlType.Edit)
                ?? Find(dialog, AutomationElement.AutomationIdProperty, "1001", ControlType.Edit)
                ?? Find(dialog, AutomationElement.NameProperty, "Folder:", ControlType.Edit), "native workspace path");
            ((ValuePattern)folderField.GetCurrentPattern(ValuePattern.Pattern)).SetValue(root);
            Click(DialogButton(dialog, ["1"], ["Select Folder", "Select folder", "Klasör Seç", "Klasörü Seç"]));
            WaitClosed(process.Id, folderTitle);
            ByName(ById(window, "WorkspaceTree"), fixture.Name, ControlType.Text);

            OpenSource(window, process.Id, target);
            var editor = ById(window, "MarkdownSourceText");
            ((ValuePattern)editor.GetCurrentPattern(ValuePattern.Pattern)).SetValue(dirty);
            OpenSource(window, process.Id, other);
            Click(ById(window, "ShowWorkspaceSearchButton"));
            using (File.Open(denied, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Start("iğde");
                Status("2 not bulundu. 1 dosya veya klasör okunamadı.");
                ByName(ById(window, "WorkspaceSearchResults"), "İĞDE başlık.md", ControlType.ListItem);
                Console.WriteLine("1/1 workspace search finds Turkish names and content and skips an exclusively locked note checks passed.");
            }

            var hit = ByName(ById(window, "WorkspaceSearchResults"), Path.Combine("A", "not.md"), ControlType.ListItem);
            ((SelectionItemPattern)hit.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Click(ById(window, "OpenWorkspaceSearchResult"));
            Wait(() =>
            {
                var active = Find(window, AutomationElement.AutomationIdProperty, "MarkdownSourceText");
                return active?.GetCurrentPattern(ValuePattern.Pattern) is ValuePattern value && value.Current.Value == dirty
                    ? "draft" : null;
            }, "original unsaved draft at exact result path");
            Require(File.ReadAllText(target) == saved && File.ReadAllText(other) == "Başka sekme",
                "Opening a search result changed a file");
            Console.WriteLine("1/1 workspace result activates the correct existing unsaved tab among duplicate filenames checks passed.");

            Click(ById(window, "ShowWorkspaceSearchButton"));
            File.Delete(nameOnly);
            hit = ByName(ById(window, "WorkspaceSearchResults"), "İĞDE başlık.md", ControlType.ListItem);
            ((SelectionItemPattern)hit.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Click(ById(window, "OpenWorkspaceSearchResult"));
            Status("Bu not taşınmış, silinmiş veya erişilemez durumda.");
            Require(!File.Exists(nameOnly), "Opening a stale result recreated the deleted file");
            Console.WriteLine("1/1 stale workspace result stays in search and reports the removed file checks passed.");

            string padding = new('x', 512 * 1024);
            for (int i = 0; i < 320; i++)
                File.WriteAllText(Path.Combine(root, $"cancel-{i:D3}.md"), padding);
            var cancel = ById(window, "CancelWorkspaceSearchButton");
            Start("eşleşme olmadığı bilinen uzun arama");
            Wait(() => cancel.Current.IsEnabled ? "running" : null, "search started");
            Click(cancel);
            Status("Arama iptal edildi.");
            Require(ById(window, "WorkspaceSearchResults").FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)) is null,
                "Canceled results leaked into the list");
            Start("iğde");
            Status("2 not bulundu.");
            Console.WriteLine("1/1 workspace search cancels an active scan and can immediately search again checks passed.");

            void Start(string value)
            {
                var query = ById(window, "WorkspaceSearchQuery");
                ((ValuePattern)query.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
                Click(ById(window, "StartWorkspaceSearchButton"));
            }

            void Status(string expected) => Wait(() =>
                ById(window, "WorkspaceSearchStatus").Current.Name.StartsWith(expected, StringComparison.Ordinal)
                    ? "ready" : null, expected);
        }
        catch { if (!process.HasExited) DumpWindows(process.Id); throw; }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10000);
            }
            fixture.Delete(recursive: true);
        }
    }
}
