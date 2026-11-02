using System.IO;
using System.Security.Cryptography;
using System.Text;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

if (!OperatingSystem.IsWindows()) return 2;
var protector = new WindowsDpapiKeyProtector();
const string password = "Türkçe ! geçerli uzun parola 2026";
const string wrongPassword = "wrong credential 12345";
const string plain = "# Hassas not\nTürkçe / 日本語 / ✨";
var encrypted = PortableEmd.Encrypt(plain, password, createRecoveryKey: true, windowsKeyProtector: protector);
var bytes = encrypted.Envelope;
var recovery = encrypted.RecoverySecret!;
int passed = 0, failed = 0;
void Test(string label, Action check)
{
    try { check(); passed++; Console.WriteLine("PASS " + label); }
    catch (Exception e)
    { failed++; Console.Error.WriteLine($"FAIL {label}: {e}"); }
}
static void Assert(bool check)
{
    if (!check) throw new Exception("Assertion failed");
}
static void Fails<T>(Action run) where T : Exception
{
    try { run(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
try
{
    Test("portable Argon2id password decrypt", () =>
        Assert(PortableEmd.Decrypt(bytes, password: password) == plain));
    Test("recovery secret bypasses unavailable Windows account", () =>
        Assert(PortableEmd.Decrypt(bytes, recoverySecret: recovery) == plain));
    Test("local DPAPI slot works without password", () =>
        Assert(PortableEmd.Decrypt(bytes, windowsKeyProtector: protector) == plain));
    Test("wrong password rejected", () =>
        Fails<CryptographicException>(() => PortableEmd.Decrypt(bytes, password: wrongPassword)));
    Test("wrong recovery secret rejected", () =>
    {
        var wrong = RandomNumberGenerator.GetBytes(32);
        try { Fails<CryptographicException>(() => PortableEmd.Decrypt(bytes, recoverySecret: wrong)); }
        finally { CryptographicOperations.ZeroMemory(wrong); }
    });
    Test("unavailable local key refused", () =>
        Fails<UnauthorizedAccessException>(() =>
            PortableEmd.Decrypt(bytes, windowsKeyProtector: new DeniedProtector())));
    Test("no plaintext secret appears in envelope", () =>
        Assert(!Convert.ToHexString(bytes).Contains(Convert.ToHexString(Encoding.UTF8.GetBytes(plain)))));
    Test("ciphertext tamper fails", () =>
    {
        var corrupt = bytes.ToArray(); corrupt[^17] ^= 1;
        Fails<CryptographicException>(() => PortableEmd.Decrypt(corrupt, recoverySecret: recovery));
    });
    Test("AEAD tag tamper fails", () =>
    {
        var corrupt = bytes.ToArray(); corrupt[^1] ^= 1;
        Fails<CryptographicException>(() => PortableEmd.Decrypt(corrupt, recoverySecret: recovery));
    });
    Test("authenticated metadata tamper fails", () =>
    {
        var corrupt = bytes.ToArray(); corrupt[7] ^= 1;
        Fails<CryptographicException>(() => PortableEmd.Decrypt(corrupt, recoverySecret: recovery));
    });
    Test("unsupported schema version rejected", () =>
    {
        var corrupt = bytes.ToArray(); corrupt[4] = 9;
        Fails<NotSupportedException>(() => PortableEmd.Decrypt(corrupt, recoverySecret: recovery));
    });
    Test("unsupported Argon2 parameters rejected without KDF allocation", () =>
    {
        var corrupt = bytes.ToArray(); corrupt[38] ^= 1; // Argon2 memory field
        Fails<NotSupportedException>(() => PortableEmd.Decrypt(corrupt, recoverySecret: recovery));
    });
    Test("truncated payload refused", () =>
        Fails<InvalidDataException>(() => PortableEmd.Decrypt(bytes[..^2], recoverySecret: recovery)));
    Test("trailing payload refused", () =>
        Fails<InvalidDataException>(() => PortableEmd.Decrypt([.. bytes, 0], recoverySecret: recovery)));
    Test("wrong magic refused", () =>
    {
        var corrupt = bytes.ToArray(); corrupt[0] ^= 1;
        Fails<InvalidDataException>(() => PortableEmd.Decrypt(corrupt, recoverySecret: recovery));
    });
    Test("ambiguous unwrapping credentials refused", () =>
        Fails<ArgumentException>(() => PortableEmd.Decrypt(bytes, password: password, recoverySecret: recovery)));
    Test("password length policy enforced", () =>
        Fails<ArgumentException>(() => PortableEmd.Encrypt(plain, "short")));
    Test("portable file can omit Windows account slot", () =>
    {
        var portable = PortableEmd.Encrypt(plain, password, createRecoveryKey: false);
        Assert(PortableEmd.Decrypt(portable.Envelope, password: password) == plain);
        Fails<CryptographicException>(() => PortableEmd.Decrypt(portable.Envelope, recoverySecret: recovery));
    });
    Test("fresh files are randomized", () =>
    {
        var another = PortableEmd.Encrypt(plain, password, createRecoveryKey: false);
        Assert(!bytes.SequenceEqual(another.Envelope));
    });

    string folder = Path.Combine(Path.GetTempPath(), "enote-tur04-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var store = new EncryptedNoteDiskStore();
        string target = Path.Combine(folder, "Güvenli Türkçe.emd");
        string mdTarget = Path.Combine(folder, "forbidden.md");
        Test("ciphertext-only new file saved and roundtrip decrypted", () =>
        {
            var version = store.Save(target, bytes);
            Assert(version == store.Read(target).Sha256);
            Assert(PortableEmd.Decrypt(store.Read(target).Envelope, recoverySecret: recovery) == plain);
            Assert(!Convert.ToHexString(File.ReadAllBytes(target))
                 .Contains(Convert.ToHexString(Encoding.UTF8.GetBytes(plain))));
        });
        Test("duplicate creation fails closed", () =>
            Fails<IOException>(() => store.Save(target, bytes)));
        Test("stale digest refuses replacement", () =>
            Fails<IOException>(() => store.Save(target, bytes, new string('A', 64))));
        Test("new version with valid digest replaces atomically", () =>
        {
            var snapshot = store.Read(target);
            var replaced = store.Save(target, bytes, snapshot.Sha256);
            Assert(replaced == store.Read(target).Sha256);
        });
        Test("external mutation makes snapshot stale", () =>
        {
            var snapshot = store.Read(target);
            var changed = File.ReadAllBytes(target);
            changed[^1] ^= 1;
            File.WriteAllBytes(target, changed);
            Fails<IOException>(() => store.Save(target, bytes, snapshot.Sha256));
        });
        Test("corruption is rejected at decryption after read", () =>
            Fails<CryptographicException>(() =>
                PortableEmd.Decrypt(store.Read(target).Envelope, recoverySecret: recovery)));
        Test("encrypted writer denies md extension", () =>
            Fails<NotSupportedException>(() => store.Save(mdTarget, bytes)));
        Test("encrypted writer denies arbitrary plain bytes", () =>
            Fails<InvalidDataException>(() => store.Save(Path.Combine(folder, "raw.emd"), "plaintext"u8)));
        Test("missing parent directory rejected", () =>
            Fails<DirectoryNotFoundException>(() =>
                store.Save(Path.Combine(folder, "missing", "file.emd"), bytes)));
        Test("new file write does not leave temporary files", () =>
            Assert(!Directory.EnumerateFiles(folder, "*.enc-tmp").Any()));
        Test("unsupported EMD1 encrypted data rejected by EMD2 store", () =>
        {
            var legacy = EmdDraftCodec.Encrypt("test", protector);
            Fails<InvalidDataException>(() => store.Save(Path.Combine(folder, "old.emd"), legacy.Envelope));
        });
    }
    finally { Directory.Delete(folder, recursive: true); }
}
finally { CryptographicOperations.ZeroMemory(recovery); }
Console.WriteLine($"{passed}/{passed + failed} portable/storage checks passed.");
return failed == 0 ? 0 : 1;

sealed class DeniedProtector : IFileKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> key) => throw new UnauthorizedAccessException();
    public byte[] Unprotect(ReadOnlySpan<byte> wrapped) => throw new UnauthorizedAccessException();
}
