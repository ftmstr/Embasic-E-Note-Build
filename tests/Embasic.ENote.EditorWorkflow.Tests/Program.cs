using System.Security.AccessControl;
using System.Security.Principal;
using Embasic.ENote.Storage.Workspaces;
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
var searchRoot = Directory.CreateDirectory(Path.Combine(directory.FullName, "search")).FullName;
var workspace = new WorkspaceStorage().OpenExisting(searchRoot);
var child = Directory.CreateDirectory(Path.Combine(searchRoot, "nested")).FullName;
File.WriteAllText(Path.Combine(child, "IŞIK.MD"), "other");
File.WriteAllText(Path.Combine(child, "body.md"), "IŞIK, İĞDE ve Cafe\u0301 😀",
    new System.Text.UTF8Encoding(true));
File.WriteAllText(Path.Combine(searchRoot, "excluded.emd"), "IŞIK");
var trash = Directory.CreateDirectory(Path.Combine(searchRoot, ".enote-trash")).FullName;
File.WriteAllText(Path.Combine(trash, "IŞIK.md"), "excluded");
var staging = Directory.CreateDirectory(Path.Combine(searchRoot, ".enote-draft-fixture")).FullName;
File.WriteAllText(Path.Combine(staging, "IŞIK.md"), "excluded");
File.WriteAllBytes(Path.Combine(searchRoot, "invalid.md"), [0xFF, 0xFE]);
using (var huge = File.Create(Path.Combine(searchRoot, "large.md")))
    huge.SetLength(WorkspaceMarkdownSearch.MaximumContentBytes + 1);

WorkspaceMarkdownSearchResult Search(string query) =>
    WorkspaceMarkdownSearch.SearchAsync(workspace, query, CancellationToken.None).GetAwaiter().GetResult();

Check("workspace search matches Turkish names and UTF-8 content and excludes secure, trash and staging files", () =>
{
    var result = Search("  ışık  ");
    Yes(result.Hits.Count == 2 && !result.Truncated);
    Yes(result.Hits.Any(x => x.RelativePath == Path.Combine("nested", "IŞIK.MD") && x.MatchLabel == "Dosya adı"));
    Yes(result.Hits.Any(x => x.RelativePath == Path.Combine("nested", "body.md") && x.MatchLabel == "İçerik"));
    Yes(Search("iğde").Hits.Count == 1 && Search("IĞDE").Hits.Count == 0);
    Yes(Search("Café 😀").Hits.Count == 1);
});
Check("workspace search reports unreadable UTF-8 and oversized content while keeping other matches", () =>
{
    var result = Search("iğde");
    Yes(result.Hits.Count == 1 && result.Unreadable == 1 && result.Oversized == 1);
    Yes(Search("large.md").Hits.Count == 1);
});
Check("workspace search caps returned results and honors cancellation", () =>
{
    for (int index = 0; index <= WorkspaceMarkdownSearch.MaximumResults; index++)
        File.WriteAllText(Path.Combine(searchRoot, $"result-{index}.md"), "text");
    var result = Search("result-");
    Yes(result.Truncated && result.Hits.Count == WorkspaceMarkdownSearch.MaximumResults);
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    Reject<OperationCanceledException>(() =>
        WorkspaceMarkdownSearch.SearchAsync(workspace, "result", canceled.Token).GetAwaiter().GetResult());
});
Check("workspace result validation refuses removed files and paths outside the selected workspace", () =>
{
    string temporary = Path.Combine(searchRoot, "gone.md");
    File.WriteAllText(temporary, "about to move");
    WorkspaceMarkdownSearch.ValidateResult(workspace, temporary);
    File.Delete(temporary);
    Reject<IOException>(() => WorkspaceMarkdownSearch.ValidateResult(workspace, temporary));
    Reject<UnauthorizedAccessException>(() => WorkspaceMarkdownSearch.ValidateResult(workspace, source));
});
var noteRoot = Directory.CreateDirectory(Path.Combine(directory.FullName, "create-note")).FullName;
var noteWorkspace = new WorkspaceStorage().OpenExisting(noteRoot);
var noteFolder = Directory.CreateDirectory(Path.Combine(noteRoot, "Toplantı")).FullName;
string CreateNote(string name, string[]? openPaths = null) =>
    WorkspaceFileOperations.CreateMarkdown(noteWorkspace, noteFolder, name, openPaths ?? []);
Check("folder note creation preserves Unicode name and appends Markdown extension once", () =>
{
    string first = CreateNote("İğdır Çalışması 😀");
    string second = CreateNote("ışık.MD");
    Yes(first == Path.Combine(noteFolder, "İğdır Çalışması 😀.md"));
    Yes(second == Path.Combine(noteFolder, "ışık.MD"));
    foreach (string path in new[] { first, second })
        Yes(new FileInfo(path).Length == 0 && !File.Exists(path + WorkspaceFileOperations.LayoutSuffix));
});
Check("folder note creation rejects invalid names, reserved devices and secure extensions", () =>
{
    foreach (string name in new[] { "", "  ", "..", "../başka", "a/b", "a\\b", "a:b", "a?b",
                                  "CON.md", "AUX .md", "LPT¹.md", "CONIN$", "son.", "son ", ".md", "gizli.EMD" })
        Reject<ArgumentException>(() => CreateNote(name));
    Yes(Directory.EnumerateFileSystemEntries(noteFolder).Count() == 2);
});
Check("folder note creation refuses existing file, directory, orphan layout and an open missing path", () =>
{
    string existing = Path.Combine(noteFolder, "existing.md");
    File.WriteAllText(existing, "var olan not");
    Reject<IOException>(() => CreateNote("existing"));
    Yes(File.ReadAllText(existing) == "var olan not");
    Directory.CreateDirectory(Path.Combine(noteFolder, "dir.md"));
    Reject<IOException>(() => CreateNote("dir"));
    string orphan = Path.Combine(noteFolder, "orphan.md");
    File.WriteAllText(orphan + WorkspaceFileOperations.LayoutSuffix, "eski ayar");
    Reject<IOException>(() => CreateNote("orphan"));
    Yes(!File.Exists(orphan) && File.ReadAllText(orphan + WorkspaceFileOperations.LayoutSuffix) == "eski ayar");
    string openMissing = Path.Combine(noteFolder, "open.md");
    Reject<IOException>(() => CreateNote("open", [openMissing]));
    Yes(!File.Exists(openMissing));
});
Check("folder note creation does not follow a linked ancestor or create a missing folder", () =>
{
    string outside = Directory.CreateDirectory(Path.Combine(directory.FullName, "outside")).FullName;
    string childPath = Directory.CreateDirectory(Path.Combine(outside, "child")).FullName;
    string alias = Path.Combine(noteRoot, "alias");
    Directory.CreateSymbolicLink(alias, outside);
    try
    {
        Reject<UnauthorizedAccessException>(() =>
            WorkspaceFileOperations.CreateMarkdown(noteWorkspace, Path.Combine(alias, "child"), "yasak", []));
        Reject<UnauthorizedAccessException>(() =>
            WorkspaceFileOperations.CreateMarkdown(noteWorkspace, childPath, "yasak", []));
        Reject<IOException>(() =>
            WorkspaceFileOperations.CreateMarkdown(noteWorkspace, Path.Combine(noteRoot, "missing"), "yasak", []));
        Yes(!Directory.EnumerateFileSystemEntries(childPath).Any());
    }
    finally { Directory.Delete(alias); }
});
Check("folder note creation respects a real directory write denial", () =>
{
    var denied = Directory.CreateDirectory(Path.Combine(noteRoot, "denied"));
    void Attempt() => Reject<UnauthorizedAccessException>(() =>
        WorkspaceFileOperations.CreateMarkdown(noteWorkspace, denied.FullName, "yasak", []));
    if (OperatingSystem.IsWindows())
    {
        using var identity = WindowsIdentity.GetCurrent();
        var originalAcl = denied.GetAccessControl(AccessControlSections.Access);
        var restricted = denied.GetAccessControl(AccessControlSections.Access);
        restricted.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
        denied.SetAccessControl(restricted);
        try { Attempt(); }
        finally { denied.SetAccessControl(originalAcl); }
    }
    else
    {
        var mode = File.GetUnixFileMode(denied.FullName);
        File.SetUnixFileMode(denied.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try { Attempt(); }
        finally { File.SetUnixFileMode(denied.FullName, mode); }
    }
    Yes(!Directory.EnumerateFileSystemEntries(denied.FullName).Any());
});
// Optional real-viewer inspection receives fresh synthetic documents, not a shipping package.
if(args is ["--evidence",var output]){Directory.CreateDirectory(output);foreach(string ext in new[]{"md","txt","html","rtf","docx","odt"})File.Copy(Path.Combine(directory.FullName,"copy."+ext),Path.Combine(output,"editor-synthetic."+ext),false);}
Directory.Delete(directory.FullName,true);
Console.WriteLine($"{passed}/{passed+failed} editor workflow checks passed.");return failed==0?0:1;
