using System.IO.Compression;
using System.Xml.Linq;
using Embasic.ENote.Core.Markdown;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage.Documents;
using Embasic.ENote.Storage;

int passed=0,failed=0;
void Check(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}}
void Yes(bool value){if(!value)throw new Exception("Assertion failed");}
void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
var directory=Directory.CreateTempSubdirectory("enote-editor-");string source=Path.Combine(directory.FullName,"source.md");
string text="# Çığ ve İğdır 😀\n\n**Kalın** ve *italik* & <script>alert(1)</script>\n\n- Bir\n- İki\n\n| Ad | Sonuç |\n| --- | --- |\n| Türkçe | 42 |\n\n```\n  kod\t{örnek}\\x\n```";
File.WriteAllText(source,"Kaydedilmiş orijinal");File.WriteAllText(source+".enote-layout.json","Eski yan dosya");
byte[] original=File.ReadAllBytes(source),sidecar=File.ReadAllBytes(source+".enote-layout.json");
var store=new MarkdownCopyExportStore();var page=PageLayoutSettings.Default.WithMargins(17,23,19,27);
Check("literal forward search wraps and preserves regex characters",()=>{Yes(MarkdownEditing.Find("a.b a.b","a.b",5,false,true)==0);Yes(MarkdownEditing.Find("abc",".*",0,false,false)==-1);});
Check("backwards search and Unicode selection indexes",()=>{Yes(MarkdownEditing.Find("😀 Çığ Çığ","Çığ",7,true,true)==3);Yes(MarkdownEditing.Find("aa BB aa","aa",0,true,true)==6);});
Check("case sensitive replacement distinguishes original case",()=>{Yes(MarkdownEditing.ReplaceAll("Not not NOT","not","yeni",true)==("Not yeni NOT",1));Yes(MarkdownEditing.ReplaceAll("Not not NOT","not","yeni",false)==("yeni yeni yeni",3));});
Check("replacement does not recursively replace inserted text",()=>Yes(MarkdownEditing.ReplaceAll("aa aa","aa","aaaa",true)==("aaaa aaaa",2)));
Check("empty query is harmless",()=>Yes(MarkdownEditing.ReplaceAll("abc","","x",false)==("abc",0)));
foreach(string ext in new[]{"md","txt","html","rtf","docx","odt"})
{
    Check(ext+" export retains current buffer and preserves source/sidecar",()=>
    {
        string target=Path.Combine(directory.FullName,"copy."+ext);store.WriteNew(source,text,target,page);Yes(File.Exists(target));
        Yes(File.ReadAllBytes(source).SequenceEqual(original));Yes(File.ReadAllBytes(source+".enote-layout.json").SequenceEqual(sidecar));
        if(ext=="md")Yes(File.ReadAllText(target)==text);
        if(ext=="txt")Yes(File.ReadAllText(target).Contains("Çığ ve İğdır 😀")&&File.ReadAllText(target).Contains("Türkçe\t42"));
        if(ext=="html"){string html=File.ReadAllText(target);Yes(html.Contains("<table>")&&html.Contains("<strong>Kalın</strong>")&&!html.Contains("<script>")&&html.Contains("default-src 'none'"));}
        if(ext=="rtf"){string rtf=File.ReadAllText(target);Yes(rtf.StartsWith("{\\rtf1")&&rtf.Contains("\\u")&&rtf.Contains("\\trowd")&&rtf.Contains("\\margl964"));}
        if(ext is "docx" or "odt")
        {
            using var zip=ZipFile.OpenRead(target);string part=ext=="docx"?"word/document.xml":"content.xml";using var stream=zip.GetEntry(part)!.Open();var xml=XDocument.Load(stream);Yes(xml.ToString().Contains("Çığ")&&xml.ToString().Contains("İğdır")&&xml.ToString().Contains("😀"));Yes(xml.ToString().Contains("Türkçe"));
            if(ext=="docx"){XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";Yes(xml.Descendants(w+"tbl").Count()==1&&xml.Descendants(w+"b").Any());Yes((string?)xml.Descendants(w+"pgMar").Single().Attribute(w+"left")=="964");}
            else{XNamespace table="urn:oasis:names:tc:opendocument:xmlns:table:1.0";Yes((string?)xml.Descendants(table+"table-column").Single().Attribute(table+"number-columns-repeated")=="2");Yes(zip.Entries[0].FullName=="mimetype");using var mime=new StreamReader(zip.GetEntry("mimetype")!.Open());Yes(mime.ReadToEnd()=="application/vnd.oasis.opendocument.text");}
        }
    });
}
Check("all six existing destinations remain byte identical",()=>{foreach(string ext in new[]{"md","txt","html","rtf","docx","odt"}){string target=Path.Combine(directory.FullName,"copy."+ext);byte[] before=File.ReadAllBytes(target);Reject<IOException>(()=>store.WriteNew(source,"changed",target,page));Yes(File.ReadAllBytes(target).SequenceEqual(before));}});
Check("source overwrite and encrypted source/destination rejected",()=>{Reject<IOException>(()=>store.WriteNew(source,text,source,page));Reject<NotSupportedException>(()=>store.WriteNew(Path.ChangeExtension(source,"emd"),text,Path.Combine(directory.FullName,"secret.docx"),page));Reject<NotSupportedException>(()=>store.WriteNew(source,text,Path.Combine(directory.FullName,"secret.emd"),page));});
Check("old destination sidecar is preserved",()=>{string target=Path.Combine(directory.FullName,"orphan.md");File.WriteAllText(target+".enote-layout.json","existing");Reject<IOException>(()=>store.WriteNew(source,text,target,page));Yes(!File.Exists(target));Yes(File.ReadAllText(target+".enote-layout.json")=="existing");});
Check("structured export never silently truncates long Markdown",()=>{string large=new('x',MarkdownPreviewParser.MaximumPreviewCharacters+1);string target=Path.Combine(directory.FullName,"large.docx");Reject<NotSupportedException>(()=>store.WriteNew(source,large,target,page));Yes(!File.Exists(target));store.WriteNew(source,large,Path.Combine(directory.FullName,"large.md"),page);Yes(File.ReadAllText(Path.Combine(directory.FullName,"large.md"))==large);});
Check("failed export leaves no temporary files",()=>Yes(!Directory.EnumerateFiles(directory.FullName,"*.enote-tmp").Any()));
Check("revision detects exact note and layout bytes and distinguishes missing source", () =>
{
    string note = Path.Combine(directory.FullName, "revision.md");
    var notes = new PlainMarkdownDocumentStore();
    var pages = new DocumentPageSettingsStore();
    string digest = notes.Save(note, "one", null);
    Yes(PlainMarkdownRevision.Check(note, digest, null) == PlainMarkdownRevisionState.Current);
    string layoutDigest = pages.Save(note, page, null);
    Yes(PlainMarkdownRevision.Check(note, digest, null) == PlainMarkdownRevisionState.Changed);
    Yes(PlainMarkdownRevision.Check(note, digest, layoutDigest) == PlainMarkdownRevisionState.Current);
    File.WriteAllText(note, "two");
    Yes(PlainMarkdownRevision.Check(note, digest, layoutDigest) == PlainMarkdownRevisionState.Changed);
    File.Delete(note);
    Yes(PlainMarkdownRevision.Check(note, digest, layoutDigest) == PlainMarkdownRevisionState.Missing);
});
Check("draft copy restores missing source buffer and metadata without recreating original", () =>
{
    string missing = Path.Combine(directory.FullName, "missing-parent", "lost.md");
    string target = Path.Combine(directory.FullName, "recovered.md");
    PlainMarkdownRevision.WriteDraftCopy(missing, target, text, page, writeBom: true);
    var note = new PlainMarkdownDocumentStore().Read(target);
    Yes(note.HasUtf8Bom && note.Text == text);
    Yes(new DocumentPageSettingsStore().Read(target).Settings == page && !File.Exists(missing));
    Reject<IOException>(() => PlainMarkdownRevision.WriteDraftCopy(missing, target, "replacement", page, false));
    Yes(new PlainMarkdownDocumentStore().Read(target) == note);
    string orphan = Path.Combine(directory.FullName, "orphan.md");
    Reject<IOException>(() => PlainMarkdownRevision.WriteDraftCopy(missing, orphan, "replacement", page, false));
    Yes(!File.Exists(orphan) && File.ReadAllText(orphan + ".enote-layout.json") == "existing");
    Yes(!Directory.EnumerateDirectories(directory.FullName, ".enote-draft-*").Any());
});
// Optional real-viewer inspection receives fresh synthetic documents, not a shipping package.
if(args is ["--evidence",var output]){Directory.CreateDirectory(output);foreach(string ext in new[]{"md","txt","html","rtf","docx","odt"})File.Copy(Path.Combine(directory.FullName,"copy."+ext),Path.Combine(output,"editor-synthetic."+ext),false);}
Directory.Delete(directory.FullName,true);
Console.WriteLine($"{passed}/{passed+failed} editor workflow checks passed.");return failed==0?0:1;
