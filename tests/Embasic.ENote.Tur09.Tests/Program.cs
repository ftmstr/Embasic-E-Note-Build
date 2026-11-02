using System.IO;
using System.Text;
using Embasic.ENote.Storage;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Emd;

if (!OperatingSystem.IsWindows()) return 2;
int passed = 0, failed = 0;
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch(Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
static void Yes(bool b) { if(!b) throw new Exception("Assertion failed."); }
static void Throws<T>(Action a) where T:Exception
{
    try { a(); } catch(T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
string dir=Path.Combine(Path.GetTempPath(),"enote-tur09-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    var store=new PlainMarkdownDocumentStore();
    var pages=new DocumentPageSettingsStore();
    string one=Path.Combine(dir,"Türkçe bir.md"),two=Path.Combine(dir,"iki.md");
    string unicode="# Türkçe 😀\n- 日本語\n- Κείμενο\n";
    string first="";
    Check("create plain Markdown UTF-8 and read digest",()=>{
        first=store.Save(one,unicode,expectedSha256:null);
        var read=store.Read(one);
        Yes(read.Text==unicode && read.Sha256==first && !read.HasUtf8Bom);
    });
    Check("plain markdown on disk is interop UTF-8",()=>Yes(File.ReadAllText(one,Encoding.UTF8)==unicode));
    Check("existing markdown saved with correct snapshot",()=>{
        var snapshot=store.Read(one);
        var updated=store.Save(one,"# Başlık\nGüncel",snapshot.Sha256);
        Yes(updated==store.Read(one).Sha256 && updated!=snapshot.Sha256);
    });
    Check("stale digest rejects external change and preserves it",()=>{
        var prior=store.Read(one);
        File.WriteAllText(one,"Dış değişiklik",Encoding.UTF8);
        Throws<IOException>(()=>store.Save(one,"eski oturum",prior.Sha256));
        Yes(store.Read(one).Text=="Dış değişiklik");
    });
    Check("create-only refuses to overwrite",()=>Throws<IOException>(()=>store.Save(one,"kaybetme",null)));
    Check("missing opened file fails without resurrection",()=>{
        var snap=store.Read(one);File.Delete(one);
        Throws<IOException>(()=>store.Save(one,"yeni",snap.Sha256));
        Yes(!File.Exists(one));
    });
    Check("UTF-8 BOM preserved",()=>{
        var sha=store.Save(one,"Üçüncü 💡",null,writeBom:true);
        var snap=store.Read(one);
        Yes(snap.HasUtf8Bom && snap.Sha256==sha && snap.Text=="Üçüncü 💡");
        store.Save(one,"Yenilendi",sha,snap.HasUtf8Bom);
        Yes(store.Read(one).HasUtf8Bom);
    });
    Check("invalid UTF-8 file rejected",()=>{
        File.WriteAllBytes(two,[0xC3,0x28]);
        Throws<DecoderFallbackException>(()=>store.Read(two));
        File.Delete(two);
    });
    Check("encrypted EMD never enters plaintext editor",()=>Throws<NotSupportedException>(()=>store.Save(Path.Combine(dir,"secret.emd"),"test",null)));
    Check("EMD read never enters plaintext editor",()=>{
        string emd=Path.Combine(dir,"secret.emd");File.WriteAllText(emd,"do not read");
        Throws<NotSupportedException>(()=>store.Read(emd));
    });
    Check("wrong file extension refused",()=>Throws<NotSupportedException>(()=>store.Save(Path.Combine(dir,"x.txt"),"no",null)));
    Check("missing folder refuses write",()=>Throws<DirectoryNotFoundException>(()=>store.Save(Path.Combine(dir,"gone","test.md"),"no",null)));
    Check("malformed digest refused",()=>Throws<ArgumentException>(()=>store.Save(one,"no","not-a-digest")));
    Check("exclusive sharing lock rejects save without mutation",()=>{
        var snap=store.Read(one);
        using (var handle=new FileStream(one,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            Throws<IOException>(()=>store.Save(one,"blocked",snap.Sha256));
        Yes(store.Read(one).Sha256==snap.Sha256);
    });
    Check("per-note default paper is A4 without sidecar",()=>{
        var p=pages.Read(one);
        Yes(p.Sha256 is null && p.Settings.Paper.Id=="A4");
    });
    Check("page settings separately saved without changing .md hash",()=>{
        var old=store.Read(one).Sha256;
        var setting=PageLayoutSettings.Default with {Paper=PageLayoutCatalog.Get("A5"),Landscape=true,ZoomPercent=125};
        string hash=pages.Save(one,setting,expectedSha256:null);
        var p=pages.Read(one);
        Yes(p.Sha256==hash && p.Settings.Paper.Id=="A5" &&
            p.Settings.Landscape && p.Settings.ZoomPercent==125 && old==store.Read(one).Sha256);
    });
    Check("per-note layout persists custom paper dimensions",()=>{
        var p=pages.Read(one);
        var custom=PageLayoutSettings.Default with {Paper=PageLayoutCatalog.Custom(231m,330m)};
        pages.Save(one,custom,p.Sha256);
        Yes(pages.Read(one).Settings.Paper.WidthMm==231m && pages.Read(one).Settings.Paper.HeightMm==330m);
    });
    Check("layout stale hash refused",()=>{
        string old=pages.Read(one).Sha256!;
        var current=pages.Read(one);
        pages.Save(one,PageLayoutSettings.Default,old);
        Throws<IOException>(()=>pages.Save(one,PageLayoutSettings.Default with {Landscape=true},old));
        Yes(pages.Read(one).Settings.Paper.Id=="A4");
    });
    Check("each note gets an independent sidecar",()=>{
        store.Save(two,"# İkinci",null);
        Yes(pages.Read(two).Sha256 is null && pages.Read(one).Sha256 is not null);
    });
    Check("layout sidecar does not override other note",()=>{
        pages.Save(two,PageLayoutSettings.Default with {Paper=PageLayoutCatalog.Get("Letter")},null);
        Yes(pages.Read(two).Settings.Paper.Id=="Letter" && pages.Read(one).Settings.Paper.Id=="A4");
    });
    Check("malformed JSON metadata refused",()=>{
        string side=two+".enote-layout.json";
        File.WriteAllText(side,"{broken}");
        Throws<InvalidDataException>(()=>pages.Read(two));
    });
    Check("unsupported metadata schema refuses unknown version",()=>{
        string side=two+".enote-layout.json";
        File.WriteAllText(side,"{\"Version\":99,\"PaperId\":\"A4\",\"WidthMm\":210,\"HeightMm\":297,\"Landscape\":false,\"ZoomPercent\":100}");
        Throws<NotSupportedException>(()=>pages.Read(two));
    });
    Check("missing parent and unauthorized .emd layout rejected",()=>{
        Throws<NotSupportedException>(()=>pages.Read(Path.Combine(dir,"secret.emd")));
    });
    Check("all write staging files cleaned on normal and error paths",()=>
        Yes(!Directory.EnumerateFiles(dir,"*.enote-tmp").Any()));
}
finally { Directory.Delete(dir,recursive:true); }
string locked = Path.Combine(Path.GetTempPath(), "enote-tur09-binding-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(locked);
try
{
    Directory.CreateDirectory(Path.Combine(locked, "Alt"));
    string target=Path.Combine(locked,"Alt","Gizli.emd");
    using var parent=LayerSecret.Generate(LayerKind.Folder);
    using var child=LayerSecret.Generate(LayerKind.Subfolder);
    using var secret=LayerSecret.Generate(LayerKind.NoteAdditional);
    using var document=PortableEmd.Encrypt("Şifreli Türkçe 🔐","Şifreli not parola 2026");
    byte[] encrypted=LayeredEmd3.Seal(document,[parent,child,secret]);
    File.WriteAllBytes(target,encrypted);
    Guid workspaceId=Guid.NewGuid();
    var entries=new[]
    {
        new WorkspaceLockEntry(parent.Id,Guid.Empty,LayerKind.Folder,"Klasör"),
        new WorkspaceLockEntry(child.Id,parent.Id,LayerKind.Subfolder,"Klasör/Alt"),
        new WorkspaceLockEntry(secret.Id,child.Id,LayerKind.NoteAdditional,"Klasör/Alt/Gizli.emd")
    };
    byte[] index=WorkspaceLockIndex.Protect(workspaceId,parent,entries);
    var all=new Dictionary<Guid,LayerSecret>{{parent.Id,parent},{child.Id,child},{secret.Id,secret}};
    Check("verified ELH1 IDs bound to existing directory and file",()=>{
        var bound=WorkspaceLockPathBinding.Resolve(locked,index,workspaceId,parent,"Klasör/Alt/Gizli.emd",all);
        Yes(bound.FullPath==Path.GetFullPath(target) && bound.RequiredIds.SequenceEqual(new[]{parent.Id,child.Id,secret.Id}));
    });
    Check("authorized binding can open ELK1 only with full factor chain",()=>{
        var bound=WorkspaceLockPathBinding.Resolve(locked,index,workspaceId,parent,"Klasör/Alt/Gizli.emd",all);
        Yes(LayeredEmd3.Open(File.ReadAllBytes(bound.FullPath),bound.AuthorizedFactors,password:"Şifreli not parola 2026")=="Şifreli Türkçe 🔐");
    });
    Check("missing independent child factor refuses binding",()=>{
        var missing=new Dictionary<Guid,LayerSecret>{{parent.Id,parent},{secret.Id,secret}};
        Throws<UnauthorizedAccessException>(()=>WorkspaceLockPathBinding.Resolve(locked,index,workspaceId,parent,"Klasör/Alt/Gizli.emd",missing));
    });
    Check("wrong root workspace cannot bind",()=>Throws<System.Security.Cryptography.CryptographicException>(()=>
        WorkspaceLockPathBinding.Resolve(locked,index,Guid.NewGuid(),parent,"Klasör/Alt/Gizli.emd",all)));
    Check("absent filesystem node refuses binding",()=>{
        File.Delete(target);
        Throws<FileNotFoundException>(()=>WorkspaceLockPathBinding.Resolve(locked,index,workspaceId,parent,"Klasör/Alt/Gizli.emd",all));
        File.WriteAllBytes(target,encrypted);
    });
    Check("non-indexed path cannot be opened by name",()=>Throws<KeyNotFoundException>(()=>
        WorkspaceLockPathBinding.Resolve(locked,index,workspaceId,parent,"Klasör/Alt/Fake.emd",all)));
    Check("tombstoned child folder refuses binding",()=>{
        File.Delete(target);Directory.Delete(Path.Combine(locked,"Alt"));
        Throws<FileNotFoundException>(()=>WorkspaceLockPathBinding.Resolve(locked,index,workspaceId,parent,"Klasör/Alt/Gizli.emd",all));
    });
}
finally {Directory.Delete(locked,recursive:true);}

Console.WriteLine($"{passed}/{passed+failed} Tur09 plain-md and page settings checks passed.");
return failed==0?0:1;