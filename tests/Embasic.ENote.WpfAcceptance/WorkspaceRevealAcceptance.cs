using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Automation;

internal static partial class Program
{
    private static void CheckWorkspaceReveal(string application)
    {
        var fixture = Directory.CreateTempSubdirectory("enote-reveal-ui-");
        var root = fixture.CreateSubdirectory("Çalışma");
        var branch = root.CreateSubdirectory("Türkçe").CreateSubdirectory("İç Çığ");
        var duplicateBranch = root.CreateSubdirectory("Başka");
        var restricted = root.CreateSubdirectory("Erişim");
        var outside = fixture.CreateSubdirectory("Çalışma-Dış");
        for (int i = 0; i < 96; i++) root.CreateSubdirectory($"A-{i:D2}");
        const string name = "Özet.md";
        const string draft = "# Kaydedilmemiş çığ ve İğdır";
        string target = Write(branch, name, "# Doğru not");
        string duplicate = Write(duplicateBranch, name, "# Diğer not");
        string external = Write(outside, name, "# Dış not");
        string locked = Write(restricted, "Kısıtlı.md", "# İzin notu");

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
            }, "workspace reveal window");

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

            Open(duplicate, "# Diğer not");
            Open(target, "# Doğru not");
            Editor().SetValue(draft);
            Reveal(@"Not ağaçta seçildi: Türkçe\İç Çığ\Özet.md");
            AssertSelection(branch.Name);
            AssertEditor(draft);
            Require(File.ReadAllText(target) == "# Doğru not", "Locating draft wrote to disk");
            Console.WriteLine("1/1 workspace reveal opens nested Unicode branches and selects a virtualized note without losing the draft checks passed.");

            SelectTab(name);
            AssertEditor("# Diğer not");
            Reveal(@"Not ağaçta seçildi: Başka\Özet.md");
            AssertSelection(duplicateBranch.Name);
            SelectTab(name + " *");
            AssertEditor(draft);
            Console.WriteLine("1/1 workspace reveal selects the correct open duplicate by path and leaves both editor buffers intact checks passed.");

            Open(external, "# Dış not");
            Reveal("Açık not seçili çalışma alanının dışında.");
            AssertSelection(duplicateBranch.Name);
            AssertEditor("# Dış not");
            Console.WriteLine("1/1 workspace reveal rejects an outside note in a similarly named sibling folder checks passed.");

            SelectTab(name + " *");
            string renamed = Path.Combine(branch.FullName, "Yeniden.md");
            File.Move(target, renamed);
            Reveal("Açık not çalışma alanındaki bu konumda bulunamadı.");
            AssertSelection(duplicateBranch.Name);
            AssertEditor(draft);
            Require(File.ReadAllText(renamed) == "# Doğru not" && !File.Exists(target), "Locating a renamed note changed disk");
            File.Move(renamed, target);
            File.Delete(target);
            Reveal("Açık not çalışma alanındaki bu konumda bulunamadı.");
            AssertSelection(duplicateBranch.Name);
            AssertEditor(draft);
            Require(!File.Exists(target), "Locating a deleted note recreated the source");
            Console.WriteLine("1/1 workspace reveal refuses externally renamed and deleted paths and preserves the existing draft checks passed.");

            Open(locked, "# İzin notu");
            Reveal(@"Not ağaçta seçildi: Erişim\Kısıtlı.md");
            AssertSelection(restricted.Name, "Kısıtlı.md");
            var acl = restricted.GetAccessControl(AccessControlSections.Access);
            using var identity = WindowsIdentity.GetCurrent();
            var deny = new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
            acl.AddAccessRule(deny);
            restricted.SetAccessControl(acl);
            try
            {
                bool denied = false;
                try { _ = Directory.EnumerateFileSystemEntries(restricted.FullName).ToArray(); }
                catch (UnauthorizedAccessException) { denied = true; }
                Require(denied, "Windows must deny the directory enumeration");
                Reveal("Klasör okunamadı (Erişim):");
                AssertEditor("# İzin notu");
                Require(File.ReadAllText(locked) == "# İzin notu", "Access denial changed the note");
            }
            finally
            {
                var restored = restricted.GetAccessControl(AccessControlSections.Access);
                restored.RemoveAccessRuleSpecific(deny);
                restricted.SetAccessControl(restored);
            }
            Reveal(@"Not ağaçta seçildi: Erişim\Kısıtlı.md");
            AssertSelection(restricted.Name, "Kısıtlı.md");
            Console.WriteLine("1/1 workspace reveal reports a real Windows ACL denial for a cached branch and succeeds when access returns checks passed.");

            void Open(string path, string contents)
            {
                OpenSource(window, process.Id, path);
                Wait(() => Editor().Current.Value == contents ? "opened" : null, "active source " + path);
            }

            ValuePattern Editor() => (ValuePattern)ById(window, "MarkdownSourceText").GetCurrentPattern(ValuePattern.Pattern);

            void AssertEditor(string expected) => Require(Editor().Current.Value == expected, "The active tab or its buffer changed");

            void SelectTab(string label) => Select(ByName(window, label, ControlType.TabItem));

            void Reveal(string status)
            {
                var button = ById(window, "RevealWorkspaceNoteButton");
                Wait(() => button.Current.IsEnabled ? "ready" : null, "reveal button enabled");
                Click(button);
                Wait(() => ById(window, "MarkdownStatusText").Current.Name.StartsWith(status, StringComparison.Ordinal) &&
                    button.Current.IsEnabled ? "reported" : null, "reveal status " + status);
            }

            void AssertSelection(string parentLabel, string noteLabel = name)
            {
                var tree = ById(window, "WorkspaceTree");
                var selection = (SelectionPattern)tree.GetCurrentPattern(SelectionPattern.Pattern);
                var selectedRow = selection.Current.GetSelection().Single();
                Require(Find(selectedRow, AutomationElement.NameProperty, noteLabel, ControlType.Text) is not null,
                    "The selected row was not the expected note");
                var parent = TreeWalker.ControlViewWalker.GetParent(selectedRow);
                while (parent is not null && parent.Current.ControlType != ControlType.TreeItem)
                    parent = TreeWalker.ControlViewWalker.GetParent(parent);
                Require(parent is not null && Find(parent, AutomationElement.NameProperty, parentLabel, ControlType.Text) is not null,
                    "The note was selected in the wrong folder");
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

        static string Write(DirectoryInfo directory, string fileName, string text)
        {
            string path = Path.Combine(directory.FullName, fileName);
            File.WriteAllText(path, text);
            return path;
        }
    }
}
