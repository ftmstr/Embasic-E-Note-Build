using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage;
using Embasic.ENote.Storage.Workspaces;

internal static partial class Program
{
    private static void CheckWorkspaceFiles(string application)
    {
        var folder = Directory.CreateTempSubdirectory("enote-workspace-ui-");
        string root = folder.FullName, a = Path.Combine(root, "A"), b = Path.Combine(root, "B");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        string one = Path.Combine(a, "one.md"), two = Path.Combine(b, "two.md");
        const string savedOne = "Kaynak bir", savedTwo = "Kaynak iki";
        const string editedOne = "# Çığ ve İğdır 😀\nKaydedilmemiş bir";
        const string editedTwo = "# İkinci not\nKaydedilmemiş iki";
        const string lastOne = "# Son taslak 😀\nSilmede korunacak metin";
        File.WriteAllText(one, savedOne); File.WriteAllText(two, savedTwo);
        var pages = new DocumentPageSettingsStore();
        pages.Save(one, PageLayoutSettings.Default, null); pages.Save(two, PageLayoutSettings.Default, null);
        byte[] layoutOne = File.ReadAllBytes(one + WorkspaceFileOperations.LayoutSuffix);
        byte[] layoutTwo = File.ReadAllBytes(two + WorkspaceFileOperations.LayoutSuffix);
        string protectedFolder = Path.Combine(root, "Korunan"); Directory.CreateDirectory(protectedFolder);
        File.WriteAllText(Path.Combine(protectedFolder, "secret.emd"), "opaque ciphertext");
        Process? process = null;
        try
        {
            var window = Start(); ChooseRoot();
            OpenSource(window, process!.Id, one); var value = Editor(); value.SetValue(editedOne);
            OpenSource(window, process.Id, two); Editor().SetValue(editedTwo);
            DocumentTab("one.md *"); value = Editor();
            ExpandTree(folder.Name); ExpandTree("A"); SelectTree("one.md");
            Click(ById(window, "RenameEntryButton")); var rename = Dialog(process.Id, "Yeniden adlandır");
            Click(DialogButton(rename, [], ["İptal"])); WaitClosed(process.Id, "Yeniden adlandır");
            Click(ById(window, "MoveEntryButton")); var move = Dialog(process.Id, "Çalışma alanı içinde hedef klasörü seçin");
            Click(DialogButton(move, ["2"], ["Cancel", "İptal"])); WaitClosed(process.Id, "Çalışma alanı içinde hedef klasörü seçin");
            Trash(accept: false);
            Require(File.ReadAllText(one) == savedOne && value.Current.Value == editedOne &&
                File.ReadAllBytes(one + WorkspaceFileOperations.LayoutSuffix).SequenceEqual(layoutOne), "Cancellation changed source/buffer/sidecar");
            Console.WriteLine("1/1 workspace rename move and trash cancellation checks passed.");

            ById(window, "MarkdownSourceText").SetFocus(); EditorControlKey(0x23); // Ctrl+End
            EditorKeybd(0x58, 0, 0, UIntPtr.Zero); EditorKeybd(0x58, 0, 2, UIntPtr.Zero); // x, one undo unit
            Wait(() => value.Current.Value.Length == editedOne.Length + 1 ? "typed" : null, "native editor keystroke");
            string typed = value.Current.Value;
            SelectTree("one.md"); Rename("renamed.md", keyboard: true); string renamed = Path.Combine(a, "renamed.md");
            Done(() => File.Exists(renamed) && !File.Exists(one));
            Require(Editor().Current.Value == typed && File.ReadAllText(renamed) == savedOne, "Rename changed unsaved/saved content");
            ById(window, "MarkdownSourceText").SetFocus(); EditorControlKey(0x5a);
            Wait(() => Editor().Current.Value == editedOne ? "undo" : null, "undo after path rename");
            Console.WriteLine("1/1 workspace note rename retains unsaved text and native undo history checks passed.");

            ExpandTree("A"); SelectTree("renamed.md"); MoveTo(b);
            string moved = Path.Combine(b, "renamed.md"); Done(() => File.Exists(moved) && !File.Exists(renamed));
            Require(Editor().Current.Value == editedOne && File.ReadAllBytes(moved + WorkspaceFileOperations.LayoutSuffix).SequenceEqual(layoutOne), "Move lost buffer/sidecar");
            ById(window, "MarkdownSourceText").SetFocus(); EditorControlKey(0x53);
            Wait(() => File.ReadAllText(moved) == editedOne ? "saved" : null, "save to relocated note path");
            Require(!File.Exists(one) && !File.Exists(renamed), "Save recreated an old path");
            Editor().SetValue(lastOne);
            Console.WriteLine("1/1 workspace note move saves to the new path and preserves the old sidecar checks passed.");

            SelectTree("B"); Rename("Çalışma"); string renamedFolder = Path.Combine(root, "Çalışma");
            Done(() => Directory.Exists(renamedFolder) && !Directory.Exists(b));
            Require(Editor().Current.Value == lastOne, "Folder rename lost first draft");
            DocumentTab("two.md *"); Require(Editor().Current.Value == editedTwo, "Folder rename lost second draft");
            Console.WriteLine("1/1 workspace folder rename retains two open unsaved note buffers checks passed.");

            SelectTree("Çalışma"); MoveTo(a); string finalFolder = Path.Combine(a, "Çalışma");
            string finalOne = Path.Combine(finalFolder, "renamed.md"), finalTwo = Path.Combine(finalFolder, "two.md");
            Done(() => Directory.Exists(finalFolder) && !Directory.Exists(renamedFolder));
            ById(window, "MarkdownSourceText").SetFocus(); EditorControlKey(0x53);
            Wait(() => File.ReadAllText(finalTwo) == editedTwo ? "saved" : null, "save second nested relocated note");
            Require(File.ReadAllText(finalOne) == editedOne && File.ReadAllBytes(finalTwo + WorkspaceFileOperations.LayoutSuffix).SequenceEqual(layoutTwo), "Folder move changed untouched content/layout");
            DocumentTab("renamed.md *"); Require(Editor().Current.Value == lastOne, "Folder move lost first unsaved draft");
            Console.WriteLine("1/1 workspace folder move updates both tab paths and saves the selected nested note checks passed.");

            ExpandTree("A"); SelectTree("Çalışma"); File.WriteAllText(finalOne, "EXTERNAL CHANGE");
            Trash(accept: true, keyboard: true); Warning();
            Require(Directory.Exists(finalFolder) && File.ReadAllText(finalOne) == "EXTERNAL CHANGE" && Editor().Current.Value == lastOne,
                "External-change refusal changed source/draft");
            File.WriteAllText(finalOne, editedOne);
            Console.WriteLine("1/1 workspace external source modification refuses deletion and retains the open draft checks passed.");

            Trash(accept: true); Done(() => !Directory.Exists(finalFolder) && Editor().Current.IsReadOnly);
            Require(Editor().Current.Value == lastOne && !ById(window, "ExportDocumentCopyButton").Current.IsEnabled,
                "Trash did not retain/read-protect the draft");
            ById(window, "MarkdownSourceText").SetFocus(); EditorControlKey(0x53);
            var saveWarning = FindDialog(process.Id, "Embasic E-Note", "Kaydetme reddedildi");
            Click(DialogButton(saveWarning, ["1", "2"], ["OK", "Tamam"]));
            Require(!Directory.Exists(finalFolder), "Saving a trashed tab recreated the deleted folder");
            Console.WriteLine("1/1 workspace trash preserves dirty tabs as read-only and blocks accidental recreation checks passed.");

            Click(ById(window, "UndoTrashButton")); Done(() => Directory.Exists(finalFolder) && !Editor().Current.IsReadOnly);
            Require(Editor().Current.Value == lastOne, "Undo-trash lost unsaved text");
            ById(window, "MarkdownSourceText").SetFocus(); EditorControlKey(0x53);
            Wait(() => File.ReadAllText(finalOne) == lastOne ? "saved" : null, "save after undo-trash");
            Require(File.ReadAllBytes(finalOne + WorkspaceFileOperations.LayoutSuffix).SequenceEqual(layoutOne), "Undo-trash changed old page metadata");
            Console.WriteLine("1/1 workspace undo-trash restores paths and permits saving the preserved draft checks passed.");

            ExpandTree("A"); ExpandTree("Çalışma"); SelectTree("two.md"); Trash(accept: true); Done(() => !File.Exists(finalTwo));
            ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
            Require(process.WaitForExit(10000), "Clean app close failed"); process.Dispose(); process = null;
            window = Start(); ChooseRoot(); Click(ById(window, "UndoTrashButton")); Done(() => File.Exists(finalTwo));
            Require(File.ReadAllText(finalTwo) == editedTwo && File.ReadAllBytes(finalTwo + WorkspaceFileOperations.LayoutSuffix).SequenceEqual(layoutTwo),
                "Restart undo did not restore bytes");
            Console.WriteLine("1/1 workspace deletion can be undone through the real UI after application restart checks passed.");

            ExpandTree(folder.Name); ExpandTree("A"); ExpandTree("Çalışma"); SelectTree("two.md"); Trash(accept: true); Done(() => !File.Exists(finalTwo));
            var item = new WorkspaceFileOperations().LatestTrash(new WorkspaceRoot(root))!;
            File.WriteAllText(finalTwo, "NEW TARGET"); Click(ById(window, "UndoTrashButton")); Warning();
            Require(File.ReadAllText(finalTwo) == "NEW TARGET" && File.ReadAllText(item.StoredPath) == editedTwo,
                "Restore overwrote an existing target or damaged retained content");
            Console.WriteLine("1/1 workspace restore collision preserves both the existing target and trashed bytes checks passed.");

            SelectTree("Korunan"); Trash(accept: true); Warning();
            Require(File.ReadAllText(Path.Combine(protectedFolder, "secret.emd")) == "opaque ciphertext" &&
                !ById(window, "NewSecureNote").Current.IsEnabled, "Encrypted subtree or secure gate changed");
            Console.WriteLine("1/1 workspace encrypted subtree mutation is refused and secure note creation stays disabled checks passed.");

            AutomationElement Start()
            {
                process = Process.Start(new ProcessStartInfo(Path.GetFullPath(application)) { WorkingDirectory = root })
                    ?? throw new IOException("App launch failed");
                return Wait(() => { process.Refresh(); return process.MainWindowHandle == IntPtr.Zero ? null : AutomationElement.FromHandle(process.MainWindowHandle); }, "workspace app window");
            }
            void ChooseRoot()
            {
                Click(ById(window, "ChooseWorkspaceButton"));
                ChooseFolder("Embasic E-Note — Çalışma alanı klasörünü seçin", root);
                ByName(ById(window, "WorkspaceTree"), folder.Name, ControlType.Text);
            }
            void ChooseFolder(string title, string path)
            {
                var d = Dialog(process!.Id, title);
                var field = Wait(() => Find(d, AutomationElement.AutomationIdProperty, "1152", ControlType.Edit)
                    ?? Find(d, AutomationElement.AutomationIdProperty, "1148", ControlType.Edit)
                    ?? Find(d, AutomationElement.AutomationIdProperty, "1001", ControlType.Edit)
                    ?? Find(d, AutomationElement.NameProperty, "Folder:", ControlType.Edit), "native folder path field");
                ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
                Click(DialogButton(d, ["1"], ["Select Folder", "Select folder", "Klasör Seç", "Klasörü Seç"]));
                WaitClosed(process.Id, title);
            }
            void Rename(string name, bool keyboard = false)
            {
                if (keyboard) { EditorKeybd(0x71, 0, 0, UIntPtr.Zero); EditorKeybd(0x71, 0, 2, UIntPtr.Zero); }
                else Click(ById(window, "RenameEntryButton"));
                var d = Dialog(process!.Id, "Yeniden adlandır");
                ((ValuePattern)ById(d, "EntryNameInput").GetCurrentPattern(ValuePattern.Pattern)).SetValue(name);
                Click(ById(d, "EntryNameAccept")); WaitClosed(process.Id, "Yeniden adlandır");
            }
            void MoveTo(string destination)
            {
                Click(ById(window, "MoveEntryButton")); ChooseFolder("Çalışma alanı içinde hedef klasörü seçin", destination);
            }
            void Trash(bool accept, bool keyboard = false)
            {
                if (keyboard) { EditorKeybd(0x2e, 0, 0, UIntPtr.Zero); EditorKeybd(0x2e, 0, 2, UIntPtr.Zero); }
                else Click(ById(window, "TrashEntryButton"));
                var d = Dialog(process!.Id, "Silinenlere taşı");
                Click(DialogButton(d, accept ? ["1"] : ["2"], accept ? ["OK", "Tamam"] : ["Cancel", "İptal"]));
                WaitClosed(process.Id, "Silinenlere taşı");
            }
            void Warning()
            {
                var d = FindDialog(process!.Id, "Dosya işlemi", "İşlem tamamlanamadı");
                Click(DialogButton(d, ["1", "2"], ["OK", "Tamam"])); WaitClosed(process.Id, "Dosya işlemi");
            }
            void Done(Func<bool> predicate) => Wait(() => window.Current.IsEnabled && predicate() ? "ready" : null, "workspace operation result");
            ValuePattern Editor() => (ValuePattern)ById(window, "MarkdownSourceText").GetCurrentPattern(ValuePattern.Pattern);
            AutomationElement NamedContainer(string name, ControlType kind, AutomationElement scope)
            {
                var text = ByName(scope, name, ControlType.Text);
                AutomationElement? current = text;
                while (current is not null && current.Current.ControlType != kind) current = TreeWalker.ControlViewWalker.GetParent(current);
                return current ?? throw new Exception("No item container for " + name);
            }
            void SelectTree(string name)
            {
                var item = NamedContainer(name, ControlType.TreeItem, ById(window, "WorkspaceTree"));
                Select(item); item.SetFocus();
            }
            void ExpandTree(string name)
            {
                var item = NamedContainer(name, ControlType.TreeItem, ById(window, "WorkspaceTree"));
                var p = (ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                if (p.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) p.Expand();
            }
            void DocumentTab(string name) => Select(ByName(window, name, ControlType.TabItem));
        }
        catch { if (process is not null && !process.HasExited) DumpWindows(process.Id); throw; }
        finally
        {
            if (process is not null) { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(10000); } process.Dispose(); }
            folder.Delete(recursive: true);
        }
    }
}
