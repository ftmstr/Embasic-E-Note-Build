using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void CheckExternalRevision(string application)
    {
        using var fixture = new RevisionFixture();
        using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(application))
        {
            WorkingDirectory = fixture.Folder.FullName
        }) ?? throw new IOException("App launch failed");
        try
        {
            var window = Wait(() =>
            {
                process.Refresh();
                return process.MainWindowHandle == IntPtr.Zero
                    ? null : AutomationElement.FromHandle(process.MainWindowHandle);
            }, "revision app window");
            OpenSource(window, process.Id, fixture.Source);
            var editor = ById(window, "MarkdownSourceText");
            var text = (ValuePattern)editor.GetCurrentPattern(ValuePattern.Pattern);
            const string external = "# Dış sürüm Çığ";
            const string draft = "# Kaydedilmemiş taslak İğdır 😀";
            const string reloadTitle = "Diskten yeniden yükle";
            const string saveTitle = "Taslağı ayrı Markdown dosyasına kaydet";

            File.WriteAllText(fixture.Source, external);
            Notice("Diskteki not");
            Require(text.Current.Value == RevisionFixture.Original, "Clean note silently reloaded");
            Click(ById(window, "ReloadRevisionButton"));
            Wait(() => text.Current.Value == external ? "ready" : null, "clean disk reload");
            Console.WriteLine("1/1 external note is announced without auto-reload and can be loaded explicitly checks passed.");

            text.SetValue(draft);
            string diskNote = "# İkinci disk sürümü";
            var layout = PageLayoutSettings.Default with { Landscape = true };
            var pages = new DocumentPageSettingsStore();
            pages.Save(fixture.Source, layout, null);
            Notice("Diskteki not");
            Require(text.Current.Value == draft && File.ReadAllText(fixture.Source) == external,
                "A sidecar-only change replaced the draft");
            editor.SetFocus();
            EditorControlKey(0x53);
            Dismiss("Embasic E-Note", "Kaydetme reddedildi");
            Require(File.ReadAllText(fixture.Source) == external && pages.Read(fixture.Source).Settings.Landscape,
                "Sidecar-only conflict permitted a text overwrite");
            File.WriteAllText(fixture.Source, diskNote);
            Console.WriteLine("1/1 layout-only conflict is announced and blocks saving the open text over disk checks passed.");

            BeginDirtyReload();
            Click(DialogButton(Dialog(process.Id, saveTitle), ["2"], ["Cancel", "İptal"]));
            WaitClosed(process.Id, saveTitle);
            Require(text.Current.Value == draft && !File.Exists(fixture.Copy), "Cancelling reload discarded or saved the draft");
            Console.WriteLine("1/1 cancelling the draft copy prevents dirty reload checks passed.");

            File.WriteAllText(fixture.CopyLayout, "unrelated old metadata");
            BeginDirtyReload();
            PickCopy(fixture.Copy);
            Wait(() => ById(window, "MarkdownStatusText").Current.Name.StartsWith(
                "Açık taslak korundu.", StringComparison.Ordinal) ? "rejected" : null, "destination collision feedback");
            Require(text.Current.Value == draft && !File.Exists(fixture.Copy) &&
                File.ReadAllText(fixture.CopyLayout) == "unrelated old metadata",
                "An old sidecar was overwritten or a rejected draft was discarded");
            File.Delete(fixture.CopyLayout);
            Console.WriteLine("1/1 old destination sidecar is kept and refused copy retains the dirty draft checks passed.");

            BeginDirtyReload();
            PickCopy(fixture.Copy);
            Wait(() => text.Current.Value == diskNote ? "ready" : null, "dirty disk reload");
            Require(File.ReadAllText(fixture.Copy) == draft && pages.Read(fixture.Copy).Settings == PageLayoutSettings.Default,
                "Recovery copy lost draft text or in-memory page layout");
            Require(File.ReadAllText(fixture.Source) == diskNote && pages.Read(fixture.Source).Settings.Landscape,
                "Reload changed the original or disk layout");
            Console.WriteLine("1/1 dirty reload first preserves Unicode draft and its previous page layout in a separate copy checks passed.");

            text.SetValue(draft);
            File.Delete(fixture.Source);
            Notice("Not artık");
            Require(!ById(window, "ReloadRevisionButton").Current.IsEnabled, "Missing source can be reloaded");
            Click(ById(window, "SaveRevisionCopyButton"));
            PickCopy(fixture.MissingCopy);
            Wait(() =>
            {
                if (File.Exists(fixture.MissingCopy)) return "ready";
                string status = ById(window, "MarkdownStatusText").Current.Name;
                if (status.StartsWith("Açık taslak korundu.", StringComparison.Ordinal))
                    throw new InvalidOperationException(status);
                return null;
            }, "missing-source recovery copy");
            Require(!File.Exists(fixture.Source) && File.ReadAllText(fixture.MissingCopy) == draft &&
                pages.Read(fixture.MissingCopy).Settings.Landscape && text.Current.Value == draft,
                "Recovery of the missing-source draft did not retain both parts");
            Console.WriteLine("1/1 missing source is not recreated and its open buffer can be copied with the loaded layout checks passed.");

            void Notice(string prefix) => Wait(() =>
            {
                var notice = Find(window, AutomationElement.AutomationIdProperty, "RevisionMessage");
                return notice is not null && notice.Current.Name.StartsWith(prefix, StringComparison.Ordinal)
                    ? notice : null;
            }, "disk change notice: " + prefix);

            void BeginDirtyReload()
            {
                Click(ById(window, "ReloadRevisionButton"));
                Click(DialogButton(Dialog(process.Id, reloadTitle), ["1"], ["OK", "Tamam"]));
                WaitClosed(process.Id, reloadTitle);
            }

            void PickCopy(string path)
            {
                var dialog = Dialog(process.Id, saveTitle);
                var name = FileName(dialog);
                var edit = Find(dialog, AutomationElement.AutomationIdProperty, "1148", ControlType.Edit)
                    ?? Find(dialog, AutomationElement.AutomationIdProperty, "1001", ControlType.Edit)
                    ?? throw new IOException("The native file-name field cannot receive keyboard focus.");
                edit.SetFocus();
                RunSta(() => System.Windows.Clipboard.SetText(path));
                EditorControlKey(0x41);
                EditorControlKey(0x56);
                Wait(() => name.Current.Value == path ? "entered" : null, "native copy destination");
                Click(DialogButton(dialog, ["1"], ["Save", "Kaydet"]));
                WaitClosed(process.Id, saveTitle);
            }

            void Dismiss(string title, string prefix)
            {
                var warning = FindDialog(process.Id, title, prefix);
                Click(DialogButton(warning, ["1", "2"], ["OK", "Tamam"]));
                WaitClosed(process.Id, title == "Embasic E-Note" ? "Kaydetme reddedildi" : title);
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
        }
    }

    private sealed class RevisionFixture : IDisposable
    {
        public const string Original = "# İlk kaynak";
        public DirectoryInfo Folder { get; } = Directory.CreateTempSubdirectory("enote-revision-ui-");
        public string Source => Path.Combine(Folder.FullName, "note.md");
        public string Copy => Path.Combine(Folder.FullName, "note-taslak.md");
        public string CopyLayout => Copy + ".enote-layout.json";
        public string MissingCopy => Path.Combine(Folder.FullName, "missing-draft.md");
        public RevisionFixture() => File.WriteAllText(Source, Original);
        public void Dispose() => Folder.Delete(recursive: true);
    }
}
