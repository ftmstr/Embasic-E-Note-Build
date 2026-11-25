using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Windows.Automation;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage;
using Embasic.ENote.Storage.Workspaces;

internal static partial class Program
{
    private static void CheckWorkspaceCopy(string application)
    {
        var fixture = Directory.CreateTempSubdirectory("enote-copy-ui-");
        var root = fixture.CreateSubdirectory("Çalışma");
        var sourceFolder = root.CreateSubdirectory("Kaynak");
        var targetFolder = root.CreateSubdirectory("Hedef");
        var restricted = root.CreateSubdirectory("İzin");
        var outside = fixture.CreateSubdirectory("Dış");
        string source = Path.Combine(sourceFolder.FullName, "Özgün.md");
        string layout = source + WorkspaceFileOperations.LayoutSuffix;
        const string saved = "# Diskteki Çığ\r\nİğdır 😀";
        const string draft = "# Kaydedilmemiş taslak";
        File.WriteAllText(source, saved, new UTF8Encoding(true));
        new DocumentPageSettingsStore().Save(source, PageLayoutSettings.Default with
        {
            Paper = PageLayoutCatalog.Get("A5"), Landscape = true
        }, null);
        byte[] savedBytes = File.ReadAllBytes(source);
        byte[] layoutBytes = File.ReadAllBytes(layout);
        string plain = Path.Combine(sourceFolder.FullName, "Düz.md");
        File.WriteAllText(plain, "# Yan dosyasız");

        using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(application))
        {
            WorkingDirectory = root.FullName
        }) ?? throw new IOException("App launch failed");
        try
        {
            var window = Wait(() =>
            {
                process.Refresh();
                return process.MainWindowHandle == IntPtr.Zero ? null : AutomationElement.FromHandle(process.MainWindowHandle);
            }, "workspace copy window");

            Click(ById(window, "ChooseWorkspaceButton"));
            ChooseFolder("Embasic E-Note — Çalışma alanı klasörünü seçin", root.FullName);
            OpenSource(window, process.Id, source);
            Wait(() => Editor().Current.Value.ReplaceLineEndings("\r\n") == saved ? "opened" : null, "source content");
            Editor().SetValue(draft);
            Expand(TreeRow(root.Name));
            Expand(TreeRow(sourceFolder.Name));
            Select(TreeRow("Özgün.md"));

            const string FolderTitle = "Markdown kopyası için hedef klasörü seçin";
            const string NameTitle = "Markdown kopyasının adı";

            var pending = Begin();
            Cancel(pending, FolderTitle);
            ToFolder(targetFolder.FullName);
            Cancel(Dialog(process.Id, NameTitle), NameTitle);
            Require(!targetFolder.EnumerateFileSystemInfos().Any(), "Cancel left files");
            AssertDraft();
            Console.WriteLine("1/1 workspace copy folder and name cancellation leave the target empty and keep the draft checks passed.");

            const string copyName = "Çığ ve İğdır 😀";
            string copy = Path.Combine(targetFolder.FullName, copyName + ".md");
            ToFolder(targetFolder.FullName);
            ConfirmName(copyName);
            Done(copy);
            Require(File.ReadAllBytes(copy).SequenceEqual(savedBytes) &&
                File.ReadAllBytes(copy + WorkspaceFileOperations.LayoutSuffix).SequenceEqual(layoutBytes),
                "Copy must preserve the saved Markdown bytes and exact valid layout");
            AssertDraft();
            Require(File.ReadAllBytes(source).SequenceEqual(savedBytes) && File.ReadAllBytes(layout).SequenceEqual(layoutBytes), "Copy changed the source");
            Console.WriteLine("1/1 workspace copy preserves Unicode, saved Markdown and sidecar bytes without altering the active draft checks passed.");

            Select(TreeRow("Düz.md"));
            ToFolder(targetFolder.FullName);
            ConfirmName("Yan dosyasız.md");
            string withoutLayout = Path.Combine(targetFolder.FullName, "Yan dosyasız.md");
            Done(withoutLayout);
            Require(File.ReadAllBytes(withoutLayout).SequenceEqual(File.ReadAllBytes(plain)) &&
                !File.Exists(withoutLayout + WorkspaceFileOperations.LayoutSuffix), "A missing source sidecar must stay absent");
            File.WriteAllText(plain + WorkspaceFileOperations.LayoutSuffix, "{invalid");
            Click(ById(window, "CopyWorkspaceMarkdownButton"));
            Warning("Malformed page metadata.");
            File.Delete(plain + WorkspaceFileOperations.LayoutSuffix);
            AssertDraft();
            Select(TreeRow("Özgün.md"));
            Console.WriteLine("1/1 workspace copy preserves absence of a sidecar without opening or replacing the active tab checks passed.");

            ToFolder(targetFolder.FullName);
            ConfirmName(copyName);
            Warning("Bu adda bir dosya veya klasör zaten var.");
            string orphan = Path.Combine(targetFolder.FullName, "Eski.md" + WorkspaceFileOperations.LayoutSuffix);
            File.WriteAllText(orphan, "eski düzen");
            ToFolder(targetFolder.FullName);
            ConfirmName("Eski");
            Warning("Bu ada ait eski sayfa düzeni dosyası var.");
            Require(File.ReadAllText(orphan) == "eski düzen" && !File.Exists(Path.Combine(targetFolder.FullName, "Eski.md")) &&
                File.ReadAllBytes(copy).SequenceEqual(savedBytes) &&
                File.ReadAllBytes(copy + WorkspaceFileOperations.LayoutSuffix).SequenceEqual(layoutBytes), "Collision changed existing bytes");
            Console.WriteLine("1/1 workspace copy refuses existing Markdown and orphan sidecars without replacement checks passed.");

            ToFolder(targetFolder.FullName);
            Dialog(process.Id, NameTitle);
            File.WriteAllText(source, "# Haricî içerik");
            ConfirmName("Değişen");
            Warning("Kaynak not veya sayfa düzeni değişti");
            File.WriteAllBytes(source, savedBytes);
            ToFolder(targetFolder.FullName);
            Dialog(process.Id, NameTitle);
            File.AppendAllText(layout, " ");
            ConfirmName("Değişen düzen");
            Warning("Kaynak not veya sayfa düzeni değişti");
            File.WriteAllBytes(layout, layoutBytes);
            Require(!targetFolder.EnumerateFileSystemInfos("Değişen*").Any(), "Source change published a target");
            AssertDraft();
            Console.WriteLine("1/1 workspace copy refuses source note or layout changes while the dialog is open checks passed.");

            var acl = restricted.GetAccessControl(AccessControlSections.Access);
            using var user = WindowsIdentity.GetCurrent();
            var deny = new FileSystemAccessRule(user.User!, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, AccessControlType.Deny);
            acl.AddAccessRule(deny);
            restricted.SetAccessControl(acl);
            try
            {
                bool denied = false;
                try { using var probe = File.Create(Path.Combine(restricted.FullName, "probe")); }
                catch (UnauthorizedAccessException) { denied = true; }
                Require(denied, "Windows must deny creating files in the target directory");
                ToFolder(restricted.FullName);
                ConfirmName("İzinsiz");
                Warning();
                Require(!restricted.EnumerateFileSystemInfos().Any(), "Access denial left a target");
                AssertDraft();
            }
            finally
            {
                var restored = restricted.GetAccessControl(AccessControlSections.Access);
                restored.RemoveAccessRuleSpecific(deny);
                restricted.SetAccessControl(restored);
            }
            Console.WriteLine("1/1 workspace copy reports real Windows destination ACL denial without leaving a target checks passed.");

            string junction = Path.Combine(root.FullName, "Bağlantı");
            using var link = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{junction}\" \"{outside.FullName}\"")
                { CreateNoWindow = true }) ?? throw new IOException("Junction creation process failed");
            Require(link.WaitForExit(10000) && link.ExitCode == 0 &&
                File.GetAttributes(junction).HasFlag(FileAttributes.ReparsePoint), "Fixture must be a real directory junction");
            ToFolder(junction);
            ConfirmName("Bağlantı hedefi");
            Warning();
            Require(!outside.EnumerateFileSystemInfos().Any(), "Copy wrote through a directory junction");
            Directory.Delete(junction);
            AssertDraft();
            Require(!root.EnumerateDirectories(".enote-draft-*", SearchOption.AllDirectories).Any(), "Copy left staging directories");
            Console.WriteLine("1/1 workspace copy refuses a Windows junction destination and preserves the open draft checks passed.");

            ValuePattern Editor() => (ValuePattern)ById(window, "MarkdownSourceText").GetCurrentPattern(ValuePattern.Pattern);
            void AssertDraft() => Require(Editor().Current.Value == draft, "The active draft changed");
            void Done(string path) => Wait(() => File.Exists(path) && window.Current.IsEnabled &&
                ById(window, "MarkdownStatusText").Current.Name.StartsWith("Diskteki not kopyalandı", StringComparison.Ordinal)
                ? "copied" : null, "copy completed");
            AutomationElement Begin()
            {
                Click(ById(window, "CopyWorkspaceMarkdownButton"));
                return Dialog(process.Id, FolderTitle);
            }
            void ToFolder(string path)
            {
                Begin();
                ChooseFolder(FolderTitle, path);
            }
            void Cancel(AutomationElement dialog, string title)
            {
                Click(DialogButton(dialog, ["2"], ["Cancel", "İptal"]));
                WaitClosed(process.Id, title);
            }
            void ChooseFolder(string title, string path)
            {
                var dialog = Dialog(process.Id, title);
                var field = Wait(() => Find(dialog, AutomationElement.AutomationIdProperty, "1152", ControlType.Edit)
                    ?? Find(dialog, AutomationElement.AutomationIdProperty, "1148", ControlType.Edit)
                    ?? Find(dialog, AutomationElement.AutomationIdProperty, "1001", ControlType.Edit)
                    ?? Find(dialog, AutomationElement.NameProperty, "Folder:", ControlType.Edit), "native folder path");
                ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
                Click(DialogButton(dialog, ["1"], ["Select Folder", "Select folder", "Klasör Seç", "Klasörü Seç"]));
                WaitClosed(process.Id, title);
            }
            void ConfirmName(string name)
            {
                var dialog = Dialog(process.Id, NameTitle);
                ((ValuePattern)ById(dialog, "EntryNameInput").GetCurrentPattern(ValuePattern.Pattern)).SetValue(name);
                Click(ById(dialog, "EntryNameAccept"));
                WaitClosed(process.Id, NameTitle);
            }
            void Warning(string? contains = null)
            {
                var dialog = contains is null ? Dialog(process.Id, "Markdown kopyalanamadı")
                    : FindDialog(process.Id, "Markdown kopyalanamadı", contains);
                Click(DialogButton(dialog, ["1", "2"], ["OK", "Tamam"]));
                WaitClosed(process.Id, "Markdown kopyalanamadı");
            }
            AutomationElement TreeRow(string label)
            {
                AutomationElement? row = ByName(ById(window, "WorkspaceTree"), label, ControlType.Text);
                while (row is not null && row.Current.ControlType != ControlType.TreeItem)
                    row = TreeWalker.ControlViewWalker.GetParent(row);
                return row ?? throw new IOException("Tree row missing: " + label);
            }
            static void Expand(AutomationElement item) =>
                ((ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
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
