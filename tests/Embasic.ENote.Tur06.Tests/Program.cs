using System.IO;
using System.Security.Cryptography;
using System.Text;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

if (!OperatingSystem.IsWindows()) return 2;
const string password = "Eksiksiz şifre 🌿 ABC 12345";
const string backupPassword = "Kurtarma yedek parolası 🎯 123";
const string note = "# Korumalı not\nTürkçe, 日本語, 𐱅𐰇𐰼𐰰";
var dpapi = new WindowsDpapiKeyProtector();
int passed = 0, failed = 0;
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

using var original = PortableEmd.Encrypt(note, password, createRecoveryKey: true,
    windowsKeyProtector: dpapi);
byte[] originalBytes = original.Envelope;
byte[] recovery = original.RecoverySecret!;
var folderKey = LayerSecret.Generate(LayerKind.Folder);
var childKey = LayerSecret.Generate(LayerKind.Subfolder);
var noteKey = LayerSecret.Generate(LayerKind.NoteAdditional);
using (folderKey)
using (childKey)
using (noteKey)
try
{
    var keys = new[] { folderKey, childKey, noteKey };
    byte[] protectedNote = LayeredEmd3.Seal(original, keys);
    Check("EMD3 layer signature differs from unprotected EMD2", () =>
        True(protectedNote.AsSpan(0, 4).SequenceEqual("ELK1"u8)));
    Check("folder subfolder note all required for password roundtrip", () =>
        True(LayeredEmd3.Open(protectedNote, keys, password: password) == note));
    Check("folder subfolder note all required for DPAPI roundtrip", () =>
        True(LayeredEmd3.Open(protectedNote, keys, windowsKeyProtector: dpapi) == note));
    Check("folder subfolder note all required for recovery roundtrip", () =>
        True(LayeredEmd3.Open(protectedNote, keys, recoverySecret: recovery) == note));
    Check("outer protected file never exposes plaintext or EMD2 header", () =>
        True(protectedNote.AsSpan().IndexOf(Encoding.UTF8.GetBytes(note)) < 0 &&
             protectedNote.AsSpan().IndexOf("EMD2"u8) < 0));
    Check("DPAPI alone cannot bypass missing outer folder lock", () =>
        Throws<ArgumentException>(() =>
            LayeredEmd3.Open(protectedNote, [childKey, noteKey], windowsKeyProtector: dpapi)));
    Check("parent and DPAPI cannot bypass separately locked subfolder", () =>
        Throws<CryptographicException>(() =>
            LayeredEmd3.Open(protectedNote, [folderKey, noteKey], windowsKeyProtector: dpapi)));
    Check("parent and child cannot bypass additional note lock", () =>
        Throws<CryptographicException>(() =>
            LayeredEmd3.Open(protectedNote, [folderKey, childKey], recoverySecret: recovery)));
    Check("wrong folder key refused cryptographically", () =>
    {
        using var wrong = LayerSecret.Generate(LayerKind.Folder);
        Throws<CryptographicException>(() =>
            LayeredEmd3.Open(protectedNote, [wrong, childKey, noteKey], password: password));
    });
    Check("wrong nested key refused cryptographically", () =>
    {
        using var wrong = new LayerSecret(childKey.Id, LayerKind.Subfolder,
            RandomNumberGenerator.GetBytes(32));
        Throws<CryptographicException>(() =>
            LayeredEmd3.Open(protectedNote, [folderKey, wrong, noteKey], password: password));
    });
    Check("wrong note key refused cryptographically", () =>
    {
        using var wrong = new LayerSecret(noteKey.Id, LayerKind.NoteAdditional,
            RandomNumberGenerator.GetBytes(32));
        Throws<CryptographicException>(() =>
            LayeredEmd3.Open(protectedNote, [folderKey, childKey, wrong], password: password));
    });
    Check("correct layers with wrong underlying password still fail", () =>
        Throws<CryptographicException>(() => LayeredEmd3.Open(protectedNote,
            keys, password: "wrong password 12345")));
    Check("correct layers with unavailable Windows account still fail", () =>
        Throws<UnauthorizedAccessException>(() => LayeredEmd3.Open(protectedNote,
            keys, windowsKeyProtector: new DeniedProtector())));
    Check("tampered outer tag refused", () =>
    {
        var bad = protectedNote.ToArray(); bad[^1] ^= 0x02;
        Throws<CryptographicException>(() => LayeredEmd3.Open(bad, keys, password: password));
    });
    Check("tampered layer ID refused", () =>
    {
        var bad = protectedNote.ToArray(); bad[8] ^= 0x02;
        Throws<CryptographicException>(() => LayeredEmd3.Open(bad, keys, password: password));
    });
    Check("tampered ciphertext refused", () =>
    {
        var bad = protectedNote.ToArray(); bad[^17] ^= 0x02;
        Throws<CryptographicException>(() => LayeredEmd3.Open(bad, keys, password: password));
    });
    Check("truncated outer ciphertext refused", () =>
        Throws<InvalidDataException>(() =>
            LayeredEmd3.Open(protectedNote[..^3], keys, password: password)));
    Check("no required layer keys refused", () =>
        Throws<ArgumentException>(() =>
            LayeredEmd3.Open(protectedNote, Array.Empty<LayerSecret>(), password: password)));
    Check("subfolder without ancestor refused", () =>
        Throws<ArgumentException>(() =>
            LayeredEmd3.Seal(original, [childKey, noteKey])));
    Check("duplicate layer identity refused", () =>
        Throws<ArgumentException>(() =>
            LayeredEmd3.Seal(original, [folderKey, folderKey])));
    Check("note lock may not precede a folder lock", () =>
        Throws<ArgumentException>(() =>
            LayeredEmd3.Seal(original, [noteKey, folderKey])));
    Check("ordinary EMD2 decoder cannot read layered EMD3", () =>
        Throws<InvalidDataException>(() =>
            PortableEmd.Decrypt(protectedNote, password: password)));
    Check("outer encryption does not mutate source EMD2", () =>
        True(original.Envelope.SequenceEqual(originalBytes)));
    Check("disposed layer keys no longer unwrap", () =>
    {
        using var disposed = LayerSecret.Generate(LayerKind.Folder);
        disposed.Dispose();
        Throws<ObjectDisposedException>(() => LayeredEmd3.Seal(original, [disposed]));
    });

    string directory = Path.Combine(Path.GetTempPath(), "enote-tur06-" +
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new RecoveryBackupDiskStore();
        string target = Path.Combine(directory, "Türkçe-kurtarma.erbk");
        byte[] backup = RecoveryBackupCodec.Create(recovery, backupPassword);
        Check("encrypted backup create-only and restore roundtrip", () =>
        {
            store.CreateNew(target, backup, backupPassword);
            byte[] restored = store.Restore(target, backupPassword);
            try { True(CryptographicOperations.FixedTimeEquals(restored, recovery)); }
            finally { CryptographicOperations.ZeroMemory(restored); }
        });
        Check("disk backup equals encrypted bytes, no raw recovery secret", () =>
        {
            byte[] onDisk = File.ReadAllBytes(target);
            True(onDisk.SequenceEqual(backup) && onDisk.AsSpan().IndexOf(recovery) < 0);
        });
        Check("existing backup cannot be overwritten", () =>
            Throws<IOException>(() => store.CreateNew(target, backup, backupPassword)));
        Check("bad backup password cannot create a file", () =>
        {
            string denied = Path.Combine(directory, "badpass.erbk");
            Throws<CryptographicException>(() =>
                store.CreateNew(denied, backup, "wrong password 987654"));
            True(!File.Exists(denied));
        });
        Check("tampered backup cannot be persisted", () =>
        {
            string denied = Path.Combine(directory, "tampered.erbk");
            byte[] tampered = backup.ToArray(); tampered[^1] ^= 0x02;
            Throws<CryptographicException>(() => store.CreateNew(denied, tampered, backupPassword));
            True(!File.Exists(denied));
        });
        Check("missing backup path is rejected", () =>
            Throws<FileNotFoundException>(() =>
                store.Restore(Path.Combine(directory, "missing.erbk"), backupPassword)));
        Check("unknown backup extension is rejected", () =>
            Throws<NotSupportedException>(() =>
                store.CreateNew(Path.Combine(directory, "raw.txt"), backup, backupPassword)));
        Check("backup nonexistent parent is rejected", () =>
            Throws<DirectoryNotFoundException>(() =>
                store.CreateNew(Path.Combine(directory, "missing", "bad.erbk"), backup, backupPassword)));
        Check("backup corruption on disk fails verification", () =>
        {
            var invalid = Path.Combine(directory, "corrupted.erbk");
            var tampered = backup.ToArray(); tampered[^1] ^= 0x01;
            File.WriteAllBytes(invalid, tampered);
            Throws<CryptographicException>(() => store.Restore(invalid, backupPassword));
        });
        Check("backup remains ciphertext after failure and no temp is left", () =>
            True(!Directory.EnumerateFiles(directory, "*.erbk-tmp").Any()));
        Check("backup can be used with EMD2 outside simulated DPAPI", () =>
        {
            byte[] secret = store.Restore(target, backupPassword);
            try { True(PortableEmd.Decrypt(originalBytes, recoverySecret: secret) == note); }
            finally { CryptographicOperations.ZeroMemory(secret); }
        });
        Check("backup refuses wrong password on restore", () =>
            Throws<CryptographicException>(() =>
                store.Restore(target, "wrong restoration passphrase 123")));
    }
    finally { Directory.Delete(directory, recursive: true); }

    Check("Result.Dispose rejects future envelope access", () =>
    {
        using var temporary = PortableEmd.Encrypt("temporary secret", password);
        byte[] copy = temporary.Envelope;
        temporary.Dispose();
        Throws<ObjectDisposedException>(() => { _ = temporary.Envelope; });
        CryptographicOperations.ZeroMemory(copy);
    });
    Check("Result.Dispose rejects future recovery access", () =>
    {
        using var temporary = PortableEmd.Encrypt("temporary secret", password);
        temporary.Dispose();
        Throws<ObjectDisposedException>(() => { _ = temporary.RecoverySecret; });
    });
}
finally
{
    CryptographicOperations.ZeroMemory(recovery);
}
Console.WriteLine($"{passed}/{passed+failed} Tur06 experimental lifecycle, layer and backup checks passed.");
return failed == 0 ? 0 : 1;

sealed class DeniedProtector : IFileKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> bytes) => throw new UnauthorizedAccessException();
    public byte[] Unprotect(ReadOnlySpan<byte> bytes) => throw new UnauthorizedAccessException();
}
