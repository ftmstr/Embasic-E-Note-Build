using System.IO;
using System.Security.Cryptography;
using System.Text;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

if (!OperatingSystem.IsWindows()) return 2;
const string pwd = "Klasör parola 2026 🌿";
const string rec = "Yedek kurtarma parola 2026 🌟";
const string next = "Yeni bağımsız klasör parola 2026";
const string textValue = "# Türkçe kilitli not\n日本語 ✨";
int ok = 0, bad = 0;
void Check(string name, Action action)
{
    try { action(); ok++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { bad++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
static void Yes(bool yes) { if (!yes) throw new Exception("Assert"); }
static void Throws<T>(Action action) where T: Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}

using var document = PortableEmd.Encrypt(textValue, pwd, windowsKeyProtector: new WindowsDpapiKeyProtector());
byte[] previousEmd2 = document.Envelope;
using var parent = LayerSecret.Generate(LayerKind.Folder);
using var nested = LayerSecret.Generate(LayerKind.Subfolder);
using var note = LayerSecret.Generate(LayerKind.NoteAdditional);
byte[] parentVault = LayerKeyVault.Create(parent, pwd, rec);
byte[] nestedVault = LayerKeyVault.Create(nested, pwd, rec);
byte[] noteVault = LayerKeyVault.Create(note, pwd, rec);
Check("ELV1 encrypted factor has fixed layout", () => Yes(parentVault.Length == LayerKeyVault.Size && parentVault.AsSpan(0,4).SequenceEqual("ELV1"u8)));
Check("distinct folder nested note vaults", () => Yes(!parentVault.SequenceEqual(nestedVault) && !nestedVault.SequenceEqual(noteVault)));
Check("wrong factor password fails authentication", () => Throws<CryptographicException>(() => LayerKeyVault.Open(parentVault, parent.Id, parent.Kind, "yanlış parola 123456789")));
Check("wrong factor recovery fails authentication", () => Throws<CryptographicException>(() => LayerKeyVault.Open(parentVault, parent.Id, parent.Kind, "yanlış kurtarma 123456", true)));
Check("recovery factor unlocks correct independent key", () =>
{
    using var viaRecovery = LayerKeyVault.Open(nestedVault, nested.Id, nested.Kind, rec, true);
    using var viaPassword = LayerKeyVault.Open(nestedVault, nested.Id, nested.Kind, pwd);
    var encrypted = LayeredEmd3.Seal(document, [parent, viaRecovery, note]);
    Yes(LayeredEmd3.Open(encrypted, [parent, viaPassword, note], password: pwd) == textValue);
});
Check("incorrect vault ID fails closed", () => Throws<CryptographicException>(() => LayerKeyVault.Open(parentVault, Guid.NewGuid(), parent.Kind, pwd)));
Check("incorrect vault kind fails closed", () => Throws<CryptographicException>(() => LayerKeyVault.Open(parentVault, parent.Id, LayerKind.NoteAdditional, pwd)));
Check("truncated vault rejected", () => Throws<InvalidDataException>(() => LayerKeyVault.Open(parentVault[..^1], parent.Id, parent.Kind, pwd)));
Check("unknown vault version rejected", () =>
{
    byte[] corrupt = parentVault.ToArray(); corrupt[4] = 8;
    Throws<NotSupportedException>(() => LayerKeyVault.Open(corrupt, parent.Id, parent.Kind, pwd));
});
Check("tampered vault pass ciphertext rejected", () =>
{
    byte[] corrupt = parentVault.ToArray(); corrupt[54] ^= 1;
    Throws<CryptographicException>(() => LayerKeyVault.Open(corrupt, parent.Id, parent.Kind, pwd));
});
Check("tampered vault recovery tag rejected", () =>
{
    byte[] corrupt = parentVault.ToArray(); corrupt[^1] ^= 1;
    Throws<CryptographicException>(() => LayerKeyVault.Open(corrupt, parent.Id, parent.Kind, rec, true));
});
Check("same recovery and password forbidden", () => Throws<ArgumentException>(() => LayerKeyVault.Create(parent, pwd, pwd)));
Check("short factor password forbidden", () => Throws<ArgumentException>(() => LayerKeyVault.Create(parent, "123", rec)));
byte[] rotatedVault = LayerKeyVault.Rotate(parentVault, parent.Id, parent.Kind, pwd, next, rec);
Check("factor password rotation invalidates old password", () => Throws<CryptographicException>(() => LayerKeyVault.Open(rotatedVault, parent.Id, parent.Kind, pwd)));
Check("factor password rotation preserves same ELK1 layer secret", () =>
{
    using var rotated = LayerKeyVault.Open(rotatedVault, parent.Id, parent.Kind, next);
    byte[] outer = LayeredEmd3.Seal(document, [parent, nested, note]);
    Yes(LayeredEmd3.Open(outer, [rotated, nested, note], password: pwd) == textValue);
});
Check("factor rotation leaves encrypted original untouched", () =>
{
    using var old = LayerKeyVault.Open(parentVault, parent.Id, parent.Kind, pwd);
    Yes(old.Id == parent.Id && previousEmd2.SequenceEqual(document.Envelope));
});
Check("rotation using recovery credential succeeds", () =>
{
    byte[] newVault = LayerKeyVault.Rotate(parentVault, parent.Id, parent.Kind, rec, next, rec + " yeni", true);
    using var opened = LayerKeyVault.Open(newVault, parent.Id, parent.Kind, next);
    Yes(opened.Id == parent.Id);
});
Check("rotation with wrong current password rejected", () => Throws<CryptographicException>(() => LayerKeyVault.Rotate(parentVault, parent.Id, parent.Kind, "wrong current password 12345", next, rec)));

string dir = Path.Combine(Path.GetTempPath(), "enote-tur07-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    var keyStore = new LayerVaultDiskStore();
    var noteStore = new LayeredNoteDiskStore();
    string parentPath = Path.Combine(dir, "folder.elkey");
    string nestedPath = Path.Combine(dir, "child.elkey");
    string notePath = Path.Combine(dir, "note.elkey");
    string target = Path.Combine(dir, "özel.emd");
    Check("encrypted folder vault file create and reopen", () =>
    {
        keyStore.CreateNew(parentPath, parent, pwd, rec);
        using var stored = keyStore.Unlock(parentPath, parent.Id, parent.Kind, pwd);
        Yes(stored.Id == parent.Id);
    });
    Check("encrypted nested and note vault persistence", () =>
    {
        keyStore.CreateNew(nestedPath, nested, pwd, rec);
        keyStore.CreateNew(notePath, note, pwd, rec);
        Yes(File.ReadAllBytes(nestedPath).AsSpan(0,4).SequenceEqual("ELV1"u8));
    });
    Check("no duplicate vault overwrite", () => Throws<IOException>(() => keyStore.CreateNew(parentPath, parent, pwd, rec)));
    Check("wrong stored vault recovery denied", () => Throws<CryptographicException>(() => keyStore.Unlock(nestedPath, nested.Id, nested.Kind, "wrong recovery password 123456", true)));
    Check("missing key vault rejected", () => Throws<FileNotFoundException>(() => keyStore.Unlock(Path.Combine(dir,"missing.elkey"), parent.Id, parent.Kind, pwd)));
    Check("wrong vault filename extension rejected", () => Throws<NotSupportedException>(() => keyStore.CreateNew(Path.Combine(dir,"bad.txt"), parent, pwd, rec)));
    Check("ELK1 only-ciphertext new file persists and decrypts", () =>
    {
        using var p = keyStore.Unlock(parentPath, parent.Id, parent.Kind, pwd);
        using var c = keyStore.Unlock(nestedPath, nested.Id, nested.Kind, rec, true);
        using var n = keyStore.Unlock(notePath, note.Id, note.Kind, pwd);
        string digest = noteStore.Save(target, document, [p,c,n]);
        var snap = noteStore.Read(target);
        Yes(digest == snap.Sha256 && snap.Envelope.AsSpan(0,4).SequenceEqual("ELK1"u8));
        Yes(LayeredEmd3.Open(snap.Envelope, [p,c,n], password: pwd) == textValue);
        Yes(snap.Envelope.AsSpan().IndexOf("EMD2"u8) < 0 && snap.Envelope.AsSpan().IndexOf(Encoding.UTF8.GetBytes(textValue)) < 0);
    });
    Check("ELK1 note cannot be opened by EMD2 DPAPI direct", () =>
        Throws<InvalidDataException>(() => PortableEmd.Decrypt(noteStore.Read(target).Envelope, windowsKeyProtector: new WindowsDpapiKeyProtector())));
    Check("ELK1 stale SHA conflict rejected", () =>
        Throws<IOException>(() => noteStore.Save(target, document, [parent,nested,note], new string('F',64))));
    Check("ELK1 duplicate creation rejected", () => Throws<IOException>(() => noteStore.Save(target, document, [parent,nested,note])));
    Check("ELK1 valid replacement uses expected digest", () =>
    {
        var prior = noteStore.Read(target);
        var digest = noteStore.Save(target, document, [parent,nested,note], prior.Sha256);
        Yes(digest == noteStore.Read(target).Sha256 && digest != prior.Sha256);
    });
    Check("ELK1 tamper rejected on decryption", () =>
    {
        var corrupt = noteStore.Read(target).Envelope; corrupt[^1] ^= 1;
        Throws<CryptographicException>(() => LayeredEmd3.Open(corrupt, [parent,nested,note], password: pwd));
    });
    Check("exclusive Windows handle refuses overwrite without mutation", () =>
    {
        var prior = noteStore.Read(target);
        using (var handle = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Throws<IOException>(() => noteStore.Save(target, document, [parent,nested,note], prior.Sha256));
        Yes(prior.Sha256 == noteStore.Read(target).Sha256);
    });
    Check("external change makes prior hash stale", () =>
    {
        var prior = noteStore.Read(target);
        var corrupt = prior.Envelope.ToArray(); corrupt[^1] ^= 1;
        File.WriteAllBytes(target, corrupt);
        Throws<IOException>(() => noteStore.Save(target, document, [parent,nested,note], prior.Sha256));
    });
    Check("missing layered file rejected", () => Throws<FileNotFoundException>(() => noteStore.Read(Path.Combine(dir,"absent.emd"))));
    Check("corrupted header rejected by layered store", () =>
    {
        string bad = Path.Combine(dir, "bad.emd"); File.WriteAllText(bad, "not encrypted");
        Throws<InvalidDataException>(() => noteStore.Read(bad));
    });
    Check("wrong layered file extension rejected", () => Throws<NotSupportedException>(() => noteStore.Save(Path.Combine(dir,"not.md"), document, [parent,nested,note])));
    Check("missing layered folder rejected", () => Throws<DirectoryNotFoundException>(() => noteStore.Save(Path.Combine(dir,"gone","a.emd"), document, [parent,nested,note])));
    Check("only encrypted temp names and normal cleanup", () => Yes(!Directory.EnumerateFiles(dir, "*.elk-tmp").Any() && !Directory.EnumerateFiles(dir,"*.elkey-tmp").Any()));
}
finally { Directory.Delete(dir, true); }
Console.WriteLine($"{ok}/{ok+bad} Tur07 lock-vault and encrypted persistence checks passed.");
return bad == 0 ? 0 : 1;
