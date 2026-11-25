using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Automation;

internal static partial class Program
{
    private static void CheckWorkspaceWatch(string application)
    {
        var fixture = Directory.CreateTempSubdirectory("enote-watch-ui-");
        var first = fixture.CreateSubdirectory("Birinci");
        var second = fixture.CreateSubdirectory("İkinci");
        var selected = first.CreateSubdirectory("Seçili");
        var protectedFolder = second.CreateSubdirectory("İzin");
        string source = Path.Combine(selected.FullName, "taslak.md");
        const string disk = "# Diskteki not";
        const string draft = "# Kaydedilmemiş Türkçe taslak";
        File.WriteAllText(source, disk);
        File.WriteAllText(Path.Combine(protectedFolder.FullName, "eski.md"), "# Okunabilen not");

        using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(application))
        {
            WorkingDirectory = fixture.FullName
        }) ?? throw new IOException("App launch failed");
        try
        {
            var window = Wait(() =>
            {
                process.Refresh();
                return process.MainWindowHandle == IntPtr.Zero ? null : AutomationElement.FromHandle(process.MainWindowHandle);
            }, "workspace watch window");

            Choose(first);
            OpenSource(window, process.Id, source);
            Wait(() => Editor().Current.Value == disk ? "opened" : null, "note opened from disk");
            Editor().SetValue(draft);
            Expand(TreeItem(first.Name));
            Expand(TreeItem(selected.Name));
            TreeItem("taslak.md");
            Select(TreeItem(selected.Name));
            AssertSelectedDraft();

            string created = Path.Combine(selected.FullName, "dışarıdan.md");
            File.WriteAllText(created, "# Yeni");
            var folder = first.CreateSubdirectory("Haricî Klasör");
            TreeItem("dışarıdan.md");
            TreeItem(folder.Name);
            AssertSelectedDraft();
            Console.WriteLine("1/1 workspace watcher shows externally created notes and folders without losing the selection or draft checks passed.");

            string renamed = Path.Combine(selected.FullName, "değişen.md");
            File.Move(created, renamed);
            string renamedFolder = Path.Combine(first.FullName, "Yeni Klasör Adı");
            Directory.Move(folder.FullName, renamedFolder);
            TreeItem("değişen.md");
            TreeItem("Yeni Klasör Adı");
            WaitAbsent("dışarıdan.md");
            WaitAbsent(folder.Name);
            AssertSelectedDraft();
            Console.WriteLine("1/1 workspace watcher reconciles external note and directory renames without replacing unchanged nodes checks passed.");

            File.Delete(renamed);
            Directory.Delete(renamedFolder);
            const int burst = 16;
            for (int i = 0; i < burst; i++)
                File.WriteAllText(Path.Combine(selected.FullName, $"olay-{i:D2}.md"), "# Olay");
            WaitAbsent("değişen.md");
            WaitAbsent("Yeni Klasör Adı");
            for (int i = 0; i < burst; i++) TreeItem($"olay-{i:D2}.md");
            AssertSelectedDraft();
            Console.WriteLine("1/1 workspace watcher reconciles external deletions and the complete final state of a burst checks passed.");

            Choose(second);
            Expand(TreeItem(second.Name));
            Expand(TreeItem(protectedFolder.Name));
            TreeItem("eski.md");
            WaitAbsent(first.Name);
            var originalAcl = protectedFolder.GetAccessControl(AccessControlSections.Access);
            var restrictedAcl = protectedFolder.GetAccessControl(AccessControlSections.Access);
            using var identity = WindowsIdentity.GetCurrent();
            restrictedAcl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
            protectedFolder.SetAccessControl(restrictedAcl);
            try
            {
                bool denied = false;
                try { _ = Directory.EnumerateFileSystemEntries(protectedFolder.FullName).ToArray(); }
                catch (UnauthorizedAccessException) { denied = true; }
                Require(denied, "Synthetic ACL must deny actual enumeration");

                File.WriteAllText(Path.Combine(first.FullName, "eski-izleyici.md"), "# Eski kök");
                AssertWatcherStaysHealthy();
                Require(Find(ById(window, "WorkspaceTree"), AutomationElement.NameProperty, "eski-izleyici.md", ControlType.Text) is null &&
                    Editor().Current.Value == draft, "Old root altered the active tree or open draft");
                Console.WriteLine("1/1 workspace switching isolates old watcher events while preserving the open dirty tab checks passed.");

                File.WriteAllText(Path.Combine(second.FullName, "tetik.md"), "# Yeni kök");
                WatchStatus("Klasör okunamadı (İzin)");
                TreeItem("tetik.md");
                TreeItem("eski.md");
                Require(Editor().Current.Value == draft && File.ReadAllText(source) == disk, "Access error changed the draft or disk");
            }
            finally { protectedFolder.SetAccessControl(originalAcl); }

            File.WriteAllText(Path.Combine(protectedFolder.FullName, "izin-döndü.md"), "# Kurtarılan erişim");
            TreeItem("izin-döndü.md");
            WatchStatus("Klasör ağacı otomatik izleniyor.");
            Console.WriteLine("1/1 workspace watcher reports actual access denial and recovers after directory access returns checks passed.");

            void Choose(DirectoryInfo root)
            {
                Click(ById(window, "ChooseWorkspaceButton"));
                const string title = "Embasic E-Note — Çalışma alanı klasörünü seçin";
                var chooser = Dialog(process.Id, title);
                var field = Wait(() => Find(chooser, AutomationElement.AutomationIdProperty, "1152", ControlType.Edit)
                    ?? Find(chooser, AutomationElement.AutomationIdProperty, "1148", ControlType.Edit)
                    ?? Find(chooser, AutomationElement.AutomationIdProperty, "1001", ControlType.Edit)
                    ?? Find(chooser, AutomationElement.NameProperty, "Folder:", ControlType.Edit), "workspace path");
                ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue(root.FullName);
                Click(DialogButton(chooser, ["1"], ["Select Folder", "Select folder", "Klasör Seç", "Klasörü Seç"]));
                WaitClosed(process.Id, title);
                TreeItem(root.Name);
                WatchStatus("Klasör ağacı otomatik izleniyor.");
            }

            AutomationElement TreeItem(string label)
            {
                var text = ByName(ById(window, "WorkspaceTree"), label, ControlType.Text);
                AutomationElement? item = text;
                while (item is not null && item.Current.ControlType != ControlType.TreeItem)
                    item = TreeWalker.ControlViewWalker.GetParent(item);
                return item ?? throw new IOException("Expected tree item: " + label);
            }

            static void Expand(AutomationElement item) =>
                ((ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();

            ValuePattern Editor() => (ValuePattern)ById(window, "MarkdownSourceText").GetCurrentPattern(ValuePattern.Pattern);

            void WaitAbsent(string label) => Wait(() =>
                Find(ById(window, "WorkspaceTree"), AutomationElement.NameProperty, label, ControlType.Text) is null
                    ? "removed" : null, "tree no longer contains " + label);

            string Status() => ById(window, "WorkspaceWatchStatus").Current.Name;

            void WatchStatus(string prefix) => Wait(() =>
                Status().StartsWith(prefix, StringComparison.Ordinal) ? "reported" : null, "watch status " + prefix);

            void AssertWatcherStaysHealthy()
            {
                var watch = Stopwatch.StartNew();
                do
                {
                    Require(Status().StartsWith("Klasör ağacı otomatik izleniyor.", StringComparison.Ordinal),
                        "An old root event triggered a scan of the new workspace");
                    Thread.Sleep(100);
                } while (watch.Elapsed < TimeSpan.FromSeconds(1.2));
            }

            void AssertSelectedDraft()
            {
                var item = TreeItem(selected.Name);
                bool isSelected = ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected;
                var expansion = ((ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Current.ExpandCollapseState;
                Require(isSelected && expansion == ExpandCollapseState.Expanded,
                    $"Expected selected, expanded folder. IsSelected={isSelected}, ExpandCollapseState={expansion}");
                Require(Editor().Current.Value == draft && File.ReadAllText(source) == disk,
                    "A tree refresh changed the draft or disk");
            }
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
