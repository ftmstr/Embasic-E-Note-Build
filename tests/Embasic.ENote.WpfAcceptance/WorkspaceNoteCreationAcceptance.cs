using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

internal static partial class Program
{
    private static void CheckWorkspaceNoteCreation(string application)
    {
        var fixture = Directory.CreateTempSubdirectory("enote-create-note-ui-");
        string target = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Hedef")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Diğer")).FullName;
        string stale = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Kaldırılacak")).FullName;
        const string name = "İğdır Çalışması";
        string old = Path.Combine(other, name + ".md");
        string created = Path.Combine(target, name + ".md");
        const string diskText = "# Diğer klasördeki not";
        const string draft = "# Kaydedilmemiş eski taslak";
        const string newText = "# Yeni notun içeriği";
        File.WriteAllText(old, diskText);
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
            }, "workspace note creation window");
            Click(ById(window, "ChooseWorkspaceButton"));
            const string workspaceTitle = "Embasic E-Note — Çalışma alanı klasörünü seçin";
            var chooser = Dialog(process.Id, workspaceTitle);
            var field = Wait(() => Find(chooser, AutomationElement.AutomationIdProperty, "1152", ControlType.Edit)
                ?? Find(chooser, AutomationElement.AutomationIdProperty, "1148", ControlType.Edit)
                ?? Find(chooser, AutomationElement.AutomationIdProperty, "1001", ControlType.Edit)
                ?? Find(chooser, AutomationElement.NameProperty, "Folder:", ControlType.Edit), "workspace path");
            ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue(fixture.FullName);
            Click(DialogButton(chooser, ["1"], ["Select Folder", "Select folder", "Klasör Seç", "Klasörü Seç"]));
            WaitClosed(process.Id, workspaceTitle);

            OpenSource(window, process.Id, old);
            Editor().SetValue(draft);
            var rootItem = TreeItem(fixture.Name);
            ((ExpandCollapsePattern)rootItem.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            Select(TreeItem("Hedef"));
            var targetItem = TreeItem("Hedef");
            ((ExpandCollapsePattern)targetItem.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();

            var dialog = EnterName("iptal");
            Click(DialogButton(dialog, ["2"], ["İptal", "Cancel"]));
            WaitClosed(process.Id, "Klasörde yeni Markdown notu");
            Require(!Directory.EnumerateFileSystemEntries(target).Any() && Editor().Current.Value == draft,
                "Cancel changed the folder or the old draft");
            Console.WriteLine("1/1 canceling folder note creation preserves the empty folder and existing draft checks passed.");

            ConfirmName("geçersiz/ad");
            DismissFailure();
            Require(!Directory.EnumerateFileSystemEntries(target).Any(), "Invalid name changed the folder");
            Console.WriteLine("1/1 invalid folder note name is rejected before creation checks passed.");

            ConfirmName(name);
            Wait(() => File.Exists(created) && Editor().Current.Value == "" && window.Current.IsEnabled
                ? "new editor" : null, "empty note opened at the selected path");
            Select(TreeItem(name + ".md"));
            Require(!ById(window, "CreateWorkspaceMarkdownButton").Current.IsEnabled,
                "A selected note must not be used as a creation folder");
            Editor().SetValue(newText);
            ById(window, "MarkdownSourceText").SetFocus();
            EditorControlKey(0x53);
            Wait(() => File.ReadAllText(created) == newText ? "saved" : null, "save to the selected folder");
            Require(File.ReadAllText(old) == diskText && !File.Exists(created + ".enote-layout.json"),
                "Creating or editing the new note changed the old source or spawned a sidecar");
            Select(ByName(window, name + ".md *", ControlType.TabItem));
            Require(Editor().Current.Value == draft, "Old dirty tab was replaced");
            Console.WriteLine("1/1 Unicode note opens and saves in the selected folder without replacing the same-name draft checks passed.");

            Select(TreeItem("Hedef"));
            ConfirmName(name + ".md");
            DismissFailure();
            Require(Editor().Current.Value == draft && File.ReadAllText(created) == newText,
                "Colliding create changed another tab or existing destination");
            Console.WriteLine("1/1 an existing note refuses creation without changing the destination or active editor checks passed.");

            Select(TreeItem("Kaldırılacak"));
            Directory.Delete(stale);
            ConfirmName("olmayan");
            DismissFailure();
            Require(!Directory.Exists(stale) && Editor().Current.Value == draft &&
                !File.Exists(Path.Combine(fixture.FullName, "olmayan.md")), "Stale folder silently redirected creation");
            Console.WriteLine("1/1 removing the selected folder before confirmation refuses creation without fallback checks passed.");

            ValuePattern Editor() => (ValuePattern)ById(window, "MarkdownSourceText").GetCurrentPattern(ValuePattern.Pattern);

            AutomationElement TreeItem(string label)
            {
                var text = ByName(ById(window, "WorkspaceTree"), label, ControlType.Text);
                AutomationElement? item = text;
                while (item is not null && item.Current.ControlType != ControlType.TreeItem)
                    item = TreeWalker.ControlViewWalker.GetParent(item);
                return item ?? throw new IOException("Expected tree item: " + label);
            }

            AutomationElement EnterName(string value)
            {
                Click(ById(window, "CreateWorkspaceMarkdownButton"));
                var prompt = Dialog(process.Id, "Klasörde yeni Markdown notu");
                var input = ById(prompt, "EntryNameInput");
                ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
                return prompt;
            }

            void ConfirmName(string value)
            {
                var prompt = EnterName(value);
                Click(ById(prompt, "EntryNameAccept"));
                WaitClosed(process.Id, "Klasörde yeni Markdown notu");
            }

            void DismissFailure()
            {
                var failure = Dialog(process.Id, "Not oluşturulamadı");
                Click(DialogButton(failure, ["1", "2"], ["OK", "Tamam"]));
                WaitClosed(process.Id, "Not oluşturulamadı");
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
