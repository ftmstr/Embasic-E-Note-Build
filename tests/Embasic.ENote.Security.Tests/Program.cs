using System.IO;
using System.Security.Cryptography;
using System.Text;
using Embasic.ENote.Emd;
using Embasic.ENote.Core.Security;
using Embasic.ENote.Storage;

if (!OperatingSystem.IsWindows()) return 2;
const string originalPassword = "Eski parola 🌿 123456789";
const string nextPassword = "Yeni parola 🌿 987654321";
const string backupPassword = "Kurtarma yedegi şifresi ☀ 1234567";
const string markdown = "# Gizli not\nTürkçe / 日本語 / 🌟";
var dpapi = new WindowsDpapiKeyProtector();
var original = PortableEmd.Encrypt(markdown, originalPassword,
    createRecoveryKey: true, windowsKeyProtector: dpapi);
byte[] recovery = original.RecoverySecret!;
byte[] originalBytes = original.Envelope;
int passed = 0, failed = 0;
void Check(string label, Action assertion)
{
    try { assertion(); passed++; Console.WriteLine("PASS " + label); }
    catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL {label}: {e}"); }
}
static void Require(bool condition)
{
    if (!condition) throw new InvalidOperationException("Assertion failed");
}
static void MustThrow<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}");
}
try
{
    byte[] protectedBackup = RecoveryBackupCodec.Create(recovery, backupPassword);
    Check("recovery backup encrypted and size bounded", () =>
        Require(protectedBackup.Length == 90 && protectedBackup.AsSpan().IndexOf(recovery) < 0));
    Check("correct backup password restores original recovery secret", () =>
    {
        byte[] restored = RecoveryBackupCodec.Open(protectedBackup, backupPassword);
        try { Require(CryptographicOperations.FixedTimeEquals(restored, recovery)); }
        finally { CryptographicOperations.ZeroMemory(restored); }
    });
    Check("restored key decrypts EMD2 with unavailable Windows account", () =>
    {
        byte[] restored = RecoveryBackupCodec.Open(protectedBackup, backupPassword);
        try
        {
            Require(PortableEmd.Decrypt(originalBytes,
                recoverySecret: restored) == markdown);
        }
        finally { CryptographicOperations.ZeroMemory(restored); }
    });
    Check("wrong backup password rejected", () =>
        MustThrow<CryptographicException>(() =>
            RecoveryBackupCodec.Open(protectedBackup, "Incorrect backup password 999")));
    Check("modified backup tag rejected", () =>
    {
        byte[] changed = protectedBackup.ToArray();
        changed[^1] ^= 1;
        MustThrow<CryptographicException>(() =>
            RecoveryBackupCodec.Open(changed, backupPassword));
    });
    Check("modified backup ciphertext rejected", () =>
    {
        byte[] changed = protectedBackup.ToArray();
        changed[45] ^= 1;
        MustThrow<CryptographicException>(() =>
            RecoveryBackupCodec.Open(changed, backupPassword));
    });
    Check("modified backup authenticated salt rejected", () =>
    {
        byte[] changed = protectedBackup.ToArray();
        changed[10] ^= 1;
        MustThrow<CryptographicException>(() =>
            RecoveryBackupCodec.Open(changed, backupPassword));
    });
    Check("unsupported backup version rejected", () =>
    {
        byte[] changed = protectedBackup.ToArray();
        changed[4] = 15;
        MustThrow<NotSupportedException>(() =>
            RecoveryBackupCodec.Open(changed, backupPassword));
    });
    Check("unsupported backup KDF parameters rejected", () =>
    {
        byte[] changed = protectedBackup.ToArray();
        changed[21] ^= 1;
        MustThrow<NotSupportedException>(() =>
            RecoveryBackupCodec.Open(changed, backupPassword));
    });
    Check("truncated backup rejected", () =>
        MustThrow<InvalidDataException>(() =>
            RecoveryBackupCodec.Open(protectedBackup[..^1], backupPassword)));
    Check("backup cannot be created with weak short password", () =>
        MustThrow<ArgumentException>(() =>
            RecoveryBackupCodec.Create(recovery, "short")));
    Check("wrong-sized recovery secret cannot be backed up", () =>
        MustThrow<ArgumentException>(() =>
            RecoveryBackupCodec.Create(new byte[12], backupPassword)));
    Check("new backups use random independent salt and nonce", () =>
        Require(!protectedBackup.SequenceEqual(RecoveryBackupCodec.Create(recovery, backupPassword))));

    var rotated = PortableEmd.RotatePassword(originalBytes, nextPassword,
        currentPassword: originalPassword, nextWindowsProtector: dpapi);
    byte[] newRecovery = rotated.RecoverySecret!;
    try
    {
        Check("rotated encrypted note decrypts with next password", () =>
            Require(PortableEmd.Decrypt(rotated.Envelope, password: nextPassword) == markdown));
        Check("rotated note rejects previous password", () =>
            MustThrow<CryptographicException>(() =>
                PortableEmd.Decrypt(rotated.Envelope, password: originalPassword)));
        Check("rotated note rejects previous recovery secret", () =>
            MustThrow<CryptographicException>(() =>
                PortableEmd.Decrypt(rotated.Envelope, recoverySecret: recovery)));
        Check("rotated new recovery secret decrypts without Windows account", () =>
            Require(PortableEmd.Decrypt(rotated.Envelope, recoverySecret: newRecovery) == markdown));
        Check("rotation changes payload ciphertext and key material", () =>
            Require(!originalBytes.SequenceEqual(rotated.Envelope)));
        Check("rotation never mutates original ciphertext", () =>
            Require(PortableEmd.Decrypt(originalBytes, password: originalPassword) == markdown));
        Check("rotation with wrong current password fails closed", () =>
            MustThrow<CryptographicException>(() =>
                PortableEmd.RotatePassword(originalBytes, nextPassword,
                    currentPassword: "Incorrect current secret 000")));
        Check("rotation via recovery key supports unavailable DPAPI", () =>
        {
            var converted = PortableEmd.RotatePassword(originalBytes, nextPassword,
                currentRecoverySecret: recovery);
            Require(PortableEmd.Decrypt(converted.Envelope, password: nextPassword) == markdown);
        });
    }
    finally { CryptographicOperations.ZeroMemory(newRecovery); }

    var legacy = EmdDraftCodec.Encrypt(markdown, dpapi, createRecoveryKey: true);
    byte[] legacyCopy = legacy.Envelope.ToArray();
    byte[] legacyRecovery = legacy.RecoveryKey!;
    try
    {
        Check("explicit EMD1 to EMD2 migration authenticated and Unicode", () =>
        {
            var converted = PortableEmd.MigrateDraftV1(
                legacy.Envelope, dpapi, nextPassword, oldRecoverySecret: legacyRecovery);
            Require(PortableEmd.Decrypt(converted.Envelope, password: nextPassword) == markdown);
            Require(converted.Envelope.AsSpan(0, 4).SequenceEqual("EMD2"u8));
        });
        Check("EMD1 migration keeps original envelope unchanged", () =>
            Require(legacy.Envelope.SequenceEqual(legacyCopy)));
        Check("EMD1 to EMD2 migration refuses wrong old recovery key", () =>
            MustThrow<CryptographicException>(() =>
                PortableEmd.MigrateDraftV1(legacy.Envelope, dpapi, nextPassword,
                    oldRecoverySecret: RandomNumberGenerator.GetBytes(32))));
        Check("EMD2 cannot silently read EMD1 input", () =>
            MustThrow<InvalidDataException>(() =>
                PortableEmd.Decrypt(legacy.Envelope, recoverySecret: legacyRecovery)));
    }
    finally { CryptographicOperations.ZeroMemory(legacyRecovery); }

    var app = new LockGate(Guid.NewGuid(), LockLayer.Application);
    var folderGate = new LockGate(Guid.NewGuid(), LockLayer.Folder);
    var nested = new LockGate(Guid.NewGuid(), LockLayer.Subfolder);
    var note = new LockGate(Guid.NewGuid(), LockLayer.NoteAdditional);
    var plan = new LockGatePlan([app, folderGate, nested, note]);
    Check("all four independent gates initially missing", () =>
        Require(plan.Missing(new HashSet<Guid>()).Count == 4));
    Check("opening parent folder never automatically unlocks subfolder", () =>
        Require(!plan.AllIndividuallyUnlocked(new HashSet<Guid> { app.Id, folderGate.Id, note.Id })));
    Check("opening parent and subfolder never bypasses note additional lock", () =>
        Require(!plan.AllIndividuallyUnlocked(new HashSet<Guid> { app.Id, folderGate.Id, nested.Id })));
    Check("all four distinct grants satisfy planning policy only", () =>
        Require(plan.AllIndividuallyUnlocked(new HashSet<Guid> { app.Id, folderGate.Id, nested.Id, note.Id })));
    Check("duplicate lock identifiers prohibited", () =>
        MustThrow<ArgumentException>(() => new LockGatePlan([app, app])));
    Check("empty gate identifiers prohibited", () =>
        MustThrow<ArgumentException>(() =>
            new LockGatePlan([new LockGate(Guid.Empty, LockLayer.NoteAdditional)])));

    string folder = Path.Combine(Path.GetTempPath(), "enote-tur05-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var store = new EncryptedNoteDiskStore();
        string target = Path.Combine(folder, "only-ciphertext.emd");
        var digest = store.Save(target, original);
        Check("exclusive Windows file handle blocks overwrite and preserves ciphertext", () =>
        {
            byte[] before = File.ReadAllBytes(target);
            using (var locked = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                MustThrow<IOException>(() => store.Save(target, rotated, digest));
            Require(File.ReadAllBytes(target).SequenceEqual(before));
        });
        Check("read after exclusive-lock refusal decrypts normally", () =>
            Require(PortableEmd.Decrypt(store.Read(target).Envelope, password: originalPassword) == markdown));
        Check("exclusive-lock refusal leaves no encrypted temporary file", () =>
            Require(!Directory.EnumerateFiles(folder, "*.enc-tmp").Any()));
        Check("old version and new version conflict on stale digest", () =>
        {
            var fresh = PortableEmd.RotatePassword(originalBytes, nextPassword,
                currentPassword: originalPassword);
            var earlier = store.Read(target);
            store.Save(target, fresh, earlier.Sha256);
            MustThrow<IOException>(() => store.Save(target, original, earlier.Sha256));
        });
    }
    finally { Directory.Delete(folder, recursive: true); }
}
finally { CryptographicOperations.ZeroMemory(recovery); }
Console.WriteLine($"{passed}/{passed + failed} backup, rotation, migration and lock-plan checks passed.");
return failed == 0 ? 0 : 1;
