using Embasic.ENote.Emd;
using Embasic.ENote.Storage;
using System.Security.Cryptography;

int passed = 0, failed = 0;
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
}
static void Yes(bool b) { if (!b) throw new InvalidOperationException("Assertion failed."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch(T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}
const string oldPassword = "eski-bağımsız-parola-2026";
const string newPassword = "yenilenmiş-bağımsız-parola-2026";
const string recovery = "ayrı-kurtarma-parolası-2026";
using var root = LayerSecret.Generate(LayerKind.Folder);
byte[] oldSnapshot = LayerKeyVault.Create(root, oldPassword, recovery);
byte[] freshSnapshot = LayerKeyVault.Rotate(oldSnapshot, root.Id, root.Kind,
    oldPassword, newPassword, recovery);
var oldPin = VaultSnapshotPin.CaptureAuthenticated(oldSnapshot,
    root.Id, root.Kind, oldPassword, 4);
var currentPin = VaultSnapshotPin.CaptureAuthenticated(freshSnapshot,
    root.Id, root.Kind, newPassword, 5);

Check("authenticated ELV1 snapshot pin records correct factor", () =>
    Yes(currentPin.LockId == root.Id && currentPin.LockKind == LayerKind.Folder));
Check("pin requires positive externally asserted epoch", () =>
    Throws<ArgumentOutOfRangeException>(() =>
        VaultSnapshotPin.CaptureAuthenticated(freshSnapshot, root.Id, root.Kind, newPassword, 0)));
Check("pin cannot be captured with invalid password", () =>
    Throws<CryptographicException>(() =>
        VaultSnapshotPin.CaptureAuthenticated(freshSnapshot, root.Id, root.Kind, oldPassword, 5)));
Check("pin cannot be captured with wrong lock GUID", () =>
    Throws<CryptographicException>(() =>
        VaultSnapshotPin.CaptureAuthenticated(freshSnapshot, Guid.NewGuid(), root.Kind, newPassword, 5)));
Check("pin cannot be captured with wrong lock kind", () =>
    Throws<CryptographicException>(() =>
        VaultSnapshotPin.CaptureAuthenticated(freshSnapshot, root.Id, LayerKind.NoteAdditional, newPassword, 5)));
Check("current pinned snapshot opens using rotated password", () =>
{
    using var verified = currentPin.OpenPinned(freshSnapshot, newPassword, 5);
    Yes(verified.Id == root.Id && verified.Kind == root.Kind);
});
Check("current pinned snapshot opens through recovery slot", () =>
{
    using var verified = currentPin.OpenPinned(freshSnapshot, recovery, 5, recovery: true);
    Yes(verified.Id == root.Id);
});
Check("old offline vault copy refused by current pin even with former password", () =>
    Throws<CryptographicException>(() =>
    { using var _ = currentPin.OpenPinned(oldSnapshot, oldPassword, 5); }));
Check("old offline vault copy refused by current pin using new password", () =>
    Throws<CryptographicException>(() =>
    { using var _ = currentPin.OpenPinned(oldSnapshot, newPassword, 5); }));
Check("stale pin cannot unlock newer envelope", () =>
    Throws<CryptographicException>(() =>
    { using var _ = oldPin.OpenPinned(freshSnapshot, oldPassword, 4); }));
Check("rollback of trusted minimum epoch makes old pin stale", () =>
    Throws<CryptographicException>(() =>
    { using var _ = oldPin.OpenPinned(oldSnapshot, oldPassword, 5); }));
Check("invalid zero minimum epoch rejected", () =>
    Throws<ArgumentOutOfRangeException>(() =>
    { using var _ = currentPin.OpenPinned(freshSnapshot, newPassword, 0); }));
Check("mismatched ciphertext refused before decrypt", () =>
{
    var tampered = freshSnapshot.ToArray();
    tampered[^1] ^= 1;
    Throws<CryptographicException>(() =>
    { using var _ = currentPin.OpenPinned(tampered, newPassword, 5); });
});
Check("truncated snapshot refuses pinned open", () =>
    Throws<System.IO.InvalidDataException>(() =>
    { using var _ = currentPin.OpenPinned(freshSnapshot[..^1], newPassword, 5); }));
Check("pinned current envelope still rejects wrong password", () =>
    Throws<CryptographicException>(() =>
    { using var _ = currentPin.OpenPinned(freshSnapshot, oldPassword, 5); }));
Check("historical offline vault remains decryptable without trusted pin: RISK OPEN", () =>
{
    // This passing test documents the UNSOLVED old-vault rollback property.
    using var unprotected = LayerKeyVault.Open(oldSnapshot, root.Id, root.Kind, oldPassword);
    Yes(unprotected.Id == root.Id);
});
Check("pin has no public writable digest or epoch", () =>
    Yes(typeof(VaultSnapshotPin).GetProperties().All(p => !p.CanWrite)));
string folder = Path.Combine(Path.GetTempPath(), "enote-tur16-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string path = Path.Combine(folder, "Root.elkey");
    File.WriteAllBytes(path, freshSnapshot);
    var store = new LayerVaultDiskStore();
    Check("on-disk opted-in pinned current vault accepted", () =>
    {
        using var unlocked = store.UnlockPinned(path, currentPin, newPassword, 5);
        Yes(unlocked.Id == root.Id);
    });
    Check("on-disk rollback copy rejected with trusted current pin", () =>
    {
        File.WriteAllBytes(path, oldSnapshot);
        Throws<CryptographicException>(() =>
        { using var _ = store.UnlockPinned(path, currentPin, oldPassword, 5); });
    });
    Check("original unlocked API remains bypassable: gate NOT production enforcement", () =>
    {
        using var bypass = store.Unlock(path, root.Id, root.Kind, oldPassword);
        Yes(bypass.Id == root.Id);
    });
    Check("old pin and lower epoch bypass when trust anchor itself rolled back: RISK OPEN", () =>
    {
        using var exposed = store.UnlockPinned(path, oldPin, oldPassword, 4);
        Yes(exposed.Id == root.Id);
    });
}
finally { Directory.Delete(folder, recursive: true); }
Console.WriteLine($"{passed}/{passed+failed} Tur16 vault rollback pin PROTOTYPE tests passed.");
return failed == 0 ? 0 : 1;
