using System.IO;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

if (!OperatingSystem.IsWindows()) return 2;
const string password = "Deneysel workspace 2026 🌿";
const string markdown = "# Türkçe alt klasör\n日本語 ✨";
int passed = 0, failed = 0;
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch(Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
static void Yes(bool cond) { if (!cond) throw new Exception("Assertion failed."); }
static void Throws<T>(Action action) where T:Exception
{
    try {action();} catch(T) {return;}
    throw new Exception("Expected " + typeof(T).Name);
}
using var root = LayerSecret.Generate(LayerKind.Folder);
using var child = LayerSecret.Generate(LayerKind.Subfolder);
using var note = LayerSecret.Generate(LayerKind.NoteAdditional);
using var doc = PortableEmd.Encrypt(markdown, password);
Guid workspace = Guid.NewGuid();
var index = new[]
{
    new WorkspaceLockEntry(root.Id, Guid.Empty, LayerKind.Folder, "Klasör"),
    new WorkspaceLockEntry(child.Id, root.Id, LayerKind.Subfolder, "Klasör/Alt"),
    new WorkspaceLockEntry(note.Id, child.Id, LayerKind.NoteAdditional, "Klasör/Alt/Gizli.emd")
};
byte[] ciphertext = WorkspaceLockIndex.Protect(workspace, root, index);
Check("encrypted workspace index has ELH1 format", () => Yes(ciphertext.AsSpan(0,4).SequenceEqual("ELH1"u8)));
Check("workspace index has no plaintext Unicode folder names", () =>
    Yes(ciphertext.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("Klasör")) < 0));
Check("authorized root unlocks three separate node identities", () =>
    Yes(WorkspaceLockIndex.Open(ciphertext, workspace, root).Count == 3));
Check("required factors preserve ancestor order", () =>
{
    var entries = WorkspaceLockIndex.Open(ciphertext, workspace, root);
    Yes(WorkspaceLockIndex.RequiredFactors(entries, "Klasör/Alt/Gizli.emd")
        .SequenceEqual(new[]{root.Id, child.Id, note.Id}));
});
Check("required factors can decrypt ELK1 without modifying EMD2 source", () =>
{
    byte[] before = doc.Envelope;
    byte[] wrapped = LayeredEmd3.Seal(doc,[root,child,note]);
    Yes(LayeredEmd3.Open(wrapped,[root,child,note],password:password)==markdown &&
        before.SequenceEqual(doc.Envelope));
});
Check("missing child factor cannot be bypassed by parent and note", () =>
{
    byte[] wrapped = LayeredEmd3.Seal(doc,[root,child,note]);
    Throws<CryptographicException>(()=>LayeredEmd3.Open(wrapped,[root,note],password:password));
});
Check("wrong workspace identifier cannot unlock index", () =>
    Throws<CryptographicException>(()=>WorkspaceLockIndex.Open(ciphertext,Guid.NewGuid(),root)));
Check("wrong independent root key cannot unlock index", () =>
{
    using var wrong = new LayerSecret(root.Id, LayerKind.Folder,RandomNumberGenerator.GetBytes(32));
    Throws<CryptographicException>(()=>WorkspaceLockIndex.Open(ciphertext,workspace,wrong));
});
Check("tampered index ciphertext fails authentication", () =>
{
    byte[] altered = ciphertext.ToArray();altered[^17]^=1;
    Throws<CryptographicException>(()=>WorkspaceLockIndex.Open(altered,workspace,root));
});
Check("tampered index header fails authentication", () =>
{
    byte[] altered=ciphertext.ToArray();altered[37]^=1;
    Throws<CryptographicException>(()=>WorkspaceLockIndex.Open(altered,workspace,root));
});
Check("unknown encrypted schema refused", () =>
{
    byte[] altered=ciphertext.ToArray();altered[4]=2;
    Throws<NotSupportedException>(()=>WorkspaceLockIndex.Open(altered,workspace,root));
});
Check("missing workspace node refuses access", () =>
    Throws<KeyNotFoundException>(()=>WorkspaceLockIndex.RequiredFactors(index,"Klasör/Other.emd")));
Check("missing ancestor metadata refused", () =>
    Throws<InvalidDataException>(()=>WorkspaceLockIndex.Protect(workspace,root,[index[0],index[2]])));
Check("sibling path masquerade rejected", () =>
    Throws<InvalidDataException>(()=>WorkspaceLockIndex.Protect(workspace,root,[
        index[0],new WorkspaceLockEntry(child.Id,root.Id,LayerKind.Subfolder,"Farklı/Alt")])));
Check("path traversal refused", () =>
    Throws<InvalidDataException>(()=>WorkspaceLockIndex.Protect(workspace,root,[
        new WorkspaceLockEntry(root.Id,Guid.Empty,LayerKind.Folder,"../Secret")])));
Check("unrelated root ID cannot be saved under root key", () =>
    Throws<InvalidDataException>(() => WorkspaceLockIndex.Protect(workspace, root, [
        new WorkspaceLockEntry(Guid.NewGuid(), Guid.Empty, LayerKind.Folder, "Klasör")])));
Check("duplicate node identity refused", () =>
    Throws<InvalidDataException>(()=>WorkspaceLockIndex.Protect(workspace,root,[index[0],index[0]])));
Check("unlocked note ID still requires original root key", () =>
{
    byte[] wrapped=LayeredEmd3.Seal(doc,[root,child,note]);
    Throws<CryptographicException>(()=>LayeredEmd3.Open(wrapped,[note],password:password));
});
Check("read-only index does not alter original EMD2", () =>
{
    _=WorkspaceLockIndex.Open(ciphertext,workspace,root);
    Yes(PortableEmd.Decrypt(doc.Envelope,password:password)==markdown);
});
string folder = Path.Combine(Path.GetTempPath(), "enote-tur08-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var disk = new WorkspaceLockIndexDiskStore();
    string path = Path.Combine(folder, "workspace.elh");
    Check("ELH1 encrypted index saved and restored from disk", () =>
    {
        disk.CreateNew(path, workspace, root, index);
        var restored = disk.Open(path, workspace, root);
        Yes(WorkspaceLockIndex.RequiredFactors(restored, "Klasör/Alt/Gizli.emd")
            .SequenceEqual(new[] { root.Id, child.Id, note.Id }));
    });
    Check("ELH1 disk bytes do not reveal folder names", () =>
        Yes(File.ReadAllBytes(path).AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("Klasör")) < 0));
    Check("duplicate ELH1 creation cannot overwrite existing ciphertext", () =>
        Throws<IOException>(() => disk.CreateNew(path, workspace, root, index)));
    Check("wrong workspace identity refuses saved index", () =>
        Throws<CryptographicException>(() => disk.Open(path, Guid.NewGuid(), root)));
    Check("tampered saved index is rejected by AEAD", () =>
    {
        var other = Path.Combine(folder, "tampered.elh");
        byte[] invalid = File.ReadAllBytes(path);
        invalid[^1] ^= 1;
        File.WriteAllBytes(other, invalid);
        Throws<CryptographicException>(() => disk.Open(other, workspace, root));
    });
    Check("missing encrypted workspace index is rejected", () =>
        Throws<FileNotFoundException>(() => disk.Open(Path.Combine(folder,"missing.elh"),workspace,root)));
    Check("plaintext index extension refused", () =>
        Throws<NotSupportedException>(() => disk.CreateNew(Path.Combine(folder,"not.txt"), workspace,root,index)));
    Check("encrypted hierarchy staging cleaned up on normal path", () =>
        Yes(!Directory.EnumerateFiles(folder,"*.elh-tmp").Any()));
}
finally { Directory.Delete(folder, recursive: true); }

Console.WriteLine($"{passed}/{passed+failed} encrypted workspace ancestry tests passed.");
return failed==0?0:1;
