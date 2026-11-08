using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

int passed=0,failed=0;
void Check(string name,Action action)
{
    try { action(); passed++; Console.WriteLine("PASS "+name); }
    catch(Exception error) { failed++; Console.Error.WriteLine("FAIL "+name+": "+error); }
}
static void Yes(bool ok) { if(!ok) throw new Exception("Assertion failed"); }
static void Throws<T>(Action run) where T:Exception
{
    try { run(); } catch(T) { return; }
    throw new Exception("Expected "+typeof(T).Name);
}
const string originalPassword="Tur18 former secure folder password 2026";
const string updatedPassword="Tur18 updated secure folder password 2026";
const string recoveryPassword="Tur18 distinct recovery password 2026";
using var factor=LayerSecret.Generate(LayerKind.Folder);
byte[] old=LayerKeyVault.Create(factor,originalPassword,recoveryPassword);
byte[] current=LayerKeyVault.Rotate(old,factor.Id,factor.Kind,
    originalPassword,updatedPassword,recoveryPassword);
var oldPin=VaultSnapshotPin.CaptureAuthenticated(old,factor.Id,
    factor.Kind,originalPassword,6);
var pin=VaultSnapshotPin.CaptureAuthenticated(current,factor.Id,
    factor.Kind,updatedPassword,7);
var store=new LayerVaultDiskStore();
string folder=Path.Combine(Path.GetTempPath(),"enote-tur18-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
string path=Path.Combine(folder,"Root.elkey");
try
{
    File.WriteAllBytes(path,current);
    Check("pinned bounded current ELV1 opens with updated credential",()=>{
        using var secret=store.UnlockPinned(path,pin,updatedPassword,7);
        Yes(secret.Id==factor.Id && secret.Kind==factor.Kind);
    });
    Check("pinned bounded current ELV1 opens through distinct recovery",()=>{
        using var secret=store.UnlockPinned(path,pin,recoveryPassword,7,recovery:true);
        Yes(secret.Id==factor.Id);
    });
    Check("pinned bounded read refuses former password on current vault",()=>
        Throws<CryptographicException>(()=>{ using var _=store.UnlockPinned(path,pin,originalPassword,7); }));
    Check("pinned bounded read refuses absent external epoch",()=>
        Throws<ArgumentOutOfRangeException>(()=>{ using var _=store.UnlockPinned(path,pin,updatedPassword,0); }));
    Check("pinned bounded read refuses stale minimum epoch",()=>
        Throws<CryptographicException>(()=>{ using var _=store.UnlockPinned(path,pin,updatedPassword,8); }));
    Check("pinned bounded read refuses null pin",()=>
        Throws<ArgumentNullException>(()=>{ using var _=store.UnlockPinned(path,null!,updatedPassword,7); }));
    Check("file share none refuses overlapping vault reader on Windows CI",()=>{
        using var exclusive=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);
        Throws<IOException>(()=>{using var _=store.UnlockPinned(path,pin,updatedPassword,7);});
    });
    Check("zero byte vault safely rejected before hash or decrypt",()=>{
        File.WriteAllBytes(path,Array.Empty<byte>());
        Throws<InvalidDataException>(()=>{using var _=store.UnlockPinned(path,pin,updatedPassword,7);});
    });
    Check("one byte vault safely rejected",()=>{
        File.WriteAllBytes(path,new byte[1]);
        Throws<InvalidDataException>(()=>{using var _=store.UnlockPinned(path,pin,updatedPassword,7);});
    });
    Check("truncated 173 byte ELV1 refused",()=>{
        File.WriteAllBytes(path,current[..^1]);
        Throws<InvalidDataException>(()=>{using var _=store.UnlockPinned(path,pin,updatedPassword,7);});
    });
    Check("extra byte ELV1 refused rather than ignoring trailing data",()=>{
        File.WriteAllBytes(path,[..current,(byte)0]);
        Throws<InvalidDataException>(()=>{using var _=store.UnlockPinned(path,pin,updatedPassword,7);});
    });
    Check("two megabyte hostile vault refused before unbounded allocation",()=>{
        File.WriteAllBytes(path,new byte[2*1024*1024]);
        Throws<InvalidDataException>(()=>{using var _=store.UnlockPinned(path,pin,updatedPassword,7);});
    });
    Check("same-size ciphertext modification refused",()=>{
        byte[] damaged=current.ToArray(); damaged[^1]^=4;
        File.WriteAllBytes(path,damaged);
        Throws<CryptographicException>(()=>{using var _=store.UnlockPinned(path,pin,updatedPassword,7);});
    });
    Check("older ciphertext refused with current trusted pin",()=>{
        File.WriteAllBytes(path,old);
        Throws<CryptographicException>(()=>{using var _=store.UnlockPinned(path,pin,originalPassword,7);});
    });
    Check("unknown extension rejected by existing vault path validator",()=>{
        string other=Path.Combine(folder,"Other.txt");
        File.WriteAllBytes(other,current);
        Throws<NotSupportedException>(()=>{using var _=store.UnlockPinned(other,pin,updatedPassword,7);});
    });
    Check("missing path refused without silently creating a vault",()=>{
        string missing=Path.Combine(folder,"Absent.elkey");
        Throws<FileNotFoundException>(()=>{using var _=store.UnlockPinned(missing,pin,updatedPassword,7);});
    });
    Check("copied identical ciphertext at different path accepted: UNBOUND PATH RISK OPEN",()=>{
        string copy=Path.Combine(folder,"Copied.elkey");
        File.WriteAllBytes(copy,current);
        using var existing=store.UnlockPinned(copy,pin,updatedPassword,7);
        Yes(existing.Id==factor.Id);
    });
    Check("old pinned snapshot and old epoch still decrypt: ROLLBACK RISK OPEN",()=>{
        File.WriteAllBytes(path,old);
        using var oldSecret=store.UnlockPinned(path,oldPin,originalPassword,6);
        Yes(oldSecret.Id==factor.Id);
    });
    Check("unguarded old vault API still decrypts former ciphertext: BYPASS OPEN",()=>{
        using var unsafeSecret=store.Unlock(path,factor.Id,factor.Kind,originalPassword);
        Yes(unsafeSecret.Id==factor.Id);
    });
    Check("direct LayerKeyVault.Open still bypasses pinned API: RISK OPEN",()=>{
        using var unsafeSecret=LayerKeyVault.Open(old,factor.Id,factor.Kind,originalPassword);
        Yes(unsafeSecret.Id==factor.Id);
    });
}
finally {Directory.Delete(folder,recursive:true);}
Console.WriteLine($"{passed}/{passed+failed} Tur18 bounded ELV1 read and risk tests passed.");
return failed==0?0:1;
