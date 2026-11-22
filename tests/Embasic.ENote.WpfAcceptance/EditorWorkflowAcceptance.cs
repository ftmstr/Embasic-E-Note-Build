using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Xml.Linq;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage.Documents;

internal static partial class Program
{
    private static void CheckEditorWorkflow(string application)
    {
        var folder=Directory.CreateTempSubdirectory("enote-daily-editor-");string source=Path.Combine(folder.FullName,"note.md"),target=Path.Combine(folder.FullName,"note-kopya.docx");
        const string original="Kaydedilmiş kaynak";const string edited="# Günlük Not\n\nÇığ not not 😀";
        File.WriteAllText(source,original);File.WriteAllText(source+".enote-layout.json","{\"Version\":1,\"PaperId\":\"A4\",\"WidthMm\":210,\"HeightMm\":297,\"Landscape\":false,\"ZoomPercent\":100}");
        byte[] oldLayout=File.ReadAllBytes(source+".enote-layout.json");
        using var process=Process.Start(new ProcessStartInfo(Path.GetFullPath(application)){WorkingDirectory=folder.FullName})??throw new Exception("App did not start");
        try
        {
            var window=Wait(()=>{process.Refresh();return process.MainWindowHandle!=IntPtr.Zero?AutomationElement.FromHandle(process.MainWindowHandle):null;},"editor window");
            var workspaceTab=ByName(window,"Çalışma Alanı",ControlType.TabItem);Select(workspaceTab);workspaceTab.SetFocus();EditorControlKey(0x4f);
            var open=Dialog(process.Id,"Düz Markdown notu aç (.md)");FileName(open).SetValue(source);Click(DialogButton(open,["1"],["Open","Aç"]));
            var notes=ById(window,"MarkdownNotesTab");Require(((SelectionItemPattern)notes.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected,"Opening did not foreground the editor");
            var editor=ById(window,"MarkdownSourceText");var value=(ValuePattern)editor.GetCurrentPattern(ValuePattern.Pattern);value.SetValue(edited);
            Console.WriteLine("1/1 editor foreground and current unsaved buffer checks passed.");
            Click(ById(window,"ShowFindButton"));((ValuePattern)ById(window,"SearchTermBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("not");((ValuePattern)ById(window,"ReplaceTermBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("belge");
            Click(ById(window,"ReplaceAllButton"));Wait(()=>value.Current.Value=="# Günlük belge\n\nÇığ belge belge 😀"?"changed":null,"literal replace all");
            // Ignore-case includes the heading's 'Not', preserving the original Unicode.
            EditorControlKey(0x5a);Wait(()=>value.Current.Value==edited?"undone":null,"single undo of replace all");
            Console.WriteLine("1/1 literal replace-all and one-step undo checks passed.");
            Click(ById(window,"ExportDocumentCopyButton"));var confirm=Dialog(process.Id,"Belge Kopyası");Click(DialogButton(confirm,["2"],["Cancel","İptal"]));WaitClosed(process.Id,"Belge Kopyası");Require(!File.Exists(target),"Confirmation cancel wrote a file");
            Console.WriteLine("1/1 new export confirmation cancellation checks passed.");
            BeginExport();var save=Dialog(process.Id,"Yeni belge kopyası oluştur");Click(DialogButton(save,["2"],["Cancel","İptal"]));WaitClosed(process.Id,"Yeni belge kopyası oluştur");Require(!File.Exists(target),"Save cancellation wrote a file");
            Console.WriteLine("1/1 new export native save cancellation checks passed.");
            BeginExport();save=Dialog(process.Id,"Yeni belge kopyası oluştur");FileName(save).SetValue(target);Click(DialogButton(save,["1"],["Save","Kaydet"]));Wait(()=>File.Exists(target)?target:null,"DOCX copy");
            byte[] saved=File.ReadAllBytes(target);using(var z=ZipFile.OpenRead(target)){using var stream=z.GetEntry("word/document.xml")!.Open();Require(XDocument.Load(stream).Root!.Value.Contains("Çığ not not 😀"),"DOCX missed unsaved Unicode text");}
            Require(value.Current.Value==edited&&File.ReadAllText(source)==original&&File.ReadAllBytes(source+".enote-layout.json").SequenceEqual(oldLayout),"Export modified buffer/source/old sidecar");
            Console.WriteLine("1/1 live DOCX export preserves source sidecar and unsaved buffer checks passed.");
            BeginExport();save=Dialog(process.Id,"Yeni belge kopyası oluştur");FileName(save).SetValue(target);Click(DialogButton(save,["1"],["Save","Kaydet"]));var warning=FindDialog(process.Id,"Embasic E-Note","Kopya oluşturulamadı");Click(DialogButton(warning,["1","2"],["OK","Tamam"]));Require(File.ReadAllBytes(target).SequenceEqual(saved),"Existing DOCX target changed");
            Console.WriteLine("1/1 new export existing-target protection checks passed.");
            RunSta(()=>{string rtf=Path.Combine(folder.FullName,"native.rtf");new MarkdownCopyExportStore().WriteNew(source,"# Çığ 😀\n\n| Ad | Değer |\n| --- | --- |\n| Türkçe | 42 |",rtf,PageLayoutSettings.Default);var doc=new FlowDocument();using var stream=File.OpenRead(rtf);var range=new TextRange(doc.ContentStart,doc.ContentEnd);range.Load(stream,DataFormats.Rtf);Require(range.Text.Contains("Çığ 😀")&&range.Text.Contains("Türkçe"),"Native WPF RTF reader lost Unicode/table text");});
            Console.WriteLine("1/1 exported RTF opens in native WPF rich-text reader checks passed.");
            void BeginExport(){Click(ById(window,"ExportDocumentCopyButton"));var c=Dialog(process.Id,"Belge Kopyası");Click(DialogButton(c,["1"],["OK","Tamam"]));WaitClosed(process.Id,"Belge Kopyası");}
        }
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);process.WaitForExit(10000);}Directory.Delete(folder.FullName,true);}
    }
    private static void EditorControlKey(byte key)
    {EditorKeybd(0x11,0,0,UIntPtr.Zero);EditorKeybd(key,0,0,UIntPtr.Zero);EditorKeybd(key,0,2,UIntPtr.Zero);EditorKeybd(0x11,0,2,UIntPtr.Zero);}
    [DllImport("user32.dll",EntryPoint="keybd_event")]private static extern void EditorKeybd(byte key,byte scan,uint flags,UIntPtr extra);
}
