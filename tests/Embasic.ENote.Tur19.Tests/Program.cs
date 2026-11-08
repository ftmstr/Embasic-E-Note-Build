using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

int passed = 0, failed = 0;
void Check(string label, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + label); }
    catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + label + ": " + e); }
}
static void Yes(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
const string formerPassword = "Tur19 old folder credential 2026";
const string currentPassword = "Tur19 current folder credential 2026";
const string recoverPassword = "Tur19 separate recovery credential 2026";
Guid workspaceId = Guid.NewGuid();
using var factor = LayerSecret.Generate(LayerKind.Folder);
byte[] oldVault = LayerKeyVault.Create(factor, formerPassword, recoverPassword);
byte[] currentVault = LayerKeyVault.Rotate(oldVault, factor.Id, factor.Kind,
    formerPassword, currentPassword, recoverPassword);
var previousPin = VaultSnapshotPin.CaptureAuthenticated(oldVault,
    factor.Id, factor.Kind, formerPassword, 8);
var currentPin = VaultSnapshotPin.CaptureAuthenticated(currentVault,
    factor.Id, factor.Kind, currentPassword, 9);
string dir = Path.Combine(Path.GetTempPath(),
    "enote-tur19-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
string authorized = Path.Combine(dir, "Active.elkey");
string clone = Path.Combine(dir, "Cloned.elkey");
var grant = new VaultAnchorGrant(workspaceId, factor.Id, factor.Kind,
    currentPin, 9, authorized);
var provider = new FakeAnchor { Grant = grant };
var gate = new TrustedVaultOpenGate(provider);
try
{
    File.WriteAllBytes(authorized, currentVault);
    File.WriteAllBytes(clone, currentVault);
    Check("current source opens when canonical policy path matches", () =>
    {
        using var unlocked = gate.UnlockPathBound(authorized, workspaceId,
            factor.Id, factor.Kind, currentPassword);
        Yes(unlocked.Id == factor.Id);
    });
    Check("independent recovery works with bound path", () =>
    {
        using var unlocked = gate.UnlockPathBound(authorized, workspaceId,
            factor.Id, factor.Kind, recoverPassword, recovery: true);
        Yes(unlocked.Id == factor.Id);
    });
    Check("canonical dot path accepted for the same policy filename", () =>
    {
        string equivalent = Path.Combine(dir, ".", "Active.elkey");
        using var unlocked = gate.UnlockPathBound(equivalent, workspaceId,
            factor.Id, factor.Kind, currentPassword);
        Yes(unlocked.Id == factor.Id);
    });
    Check("canonical parent traversal resolves back to approved path", () =>
    {
        string equivalent = Path.Combine(dir, "sub", "..", "Active.elkey");
        using var unlocked = gate.UnlockPathBound(equivalent, workspaceId,
            factor.Id, factor.Kind, currentPassword);
        Yes(unlocked.Id == factor.Id);
    });
    Check("same byte ciphertext replay at another path rejected by NEW gate", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.UnlockPathBound(clone, workspaceId,
            factor.Id, factor.Kind, currentPassword); }));
    Check("wrong case in Windows policy path works on case-insensitive Windows", () =>
    {
        if (!OperatingSystem.IsWindows()) return;
        var p = new FakeAnchor { Grant = grant with
            { BoundVaultPath = authorized.ToUpperInvariant() } };
        using var x = new TrustedVaultOpenGate(p).UnlockPathBound(authorized,
            workspaceId, factor.Id, factor.Kind, currentPassword);
        Yes(x.Id == factor.Id);
    });
    Check("missing bound path refuses NEW gate", () =>
    {
        var p = new FakeAnchor { Grant = grant with { BoundVaultPath = null } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(p).UnlockPathBound(authorized,
            workspaceId, factor.Id, factor.Kind, currentPassword); });
    });
    Check("empty bound path refuses NEW gate", () =>
    {
        var p = new FakeAnchor { Grant = grant with { BoundVaultPath = "" } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(p).UnlockPathBound(authorized,
            workspaceId, factor.Id, factor.Kind, currentPassword); });
    });
    Check("whitespace bound path refuses NEW gate", () =>
    {
        var p = new FakeAnchor { Grant = grant with { BoundVaultPath = "  " } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(p).UnlockPathBound(authorized,
            workspaceId, factor.Id, factor.Kind, currentPassword); });
    });
    Check("relative bound path refused to avoid current-dir ambiguity", () =>
    {
        var p = new FakeAnchor { Grant = grant with
            { BoundVaultPath = "relative/Active.elkey" } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(p).UnlockPathBound(authorized,
            workspaceId, factor.Id, factor.Kind, currentPassword); });
    });
    Check("independent trust record for a different full path refused", () =>
    {
        var p = new FakeAnchor { Grant = grant with { BoundVaultPath = clone } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(p).UnlockPathBound(authorized,
            workspaceId, factor.Id, factor.Kind, currentPassword); });
    });
    Check("wrong workspace binding refused", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.UnlockPathBound(authorized, Guid.NewGuid(),
            factor.Id, factor.Kind, currentPassword); }));
    Check("wrong lock identity refused", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.UnlockPathBound(authorized, workspaceId,
            Guid.NewGuid(), factor.Kind, currentPassword); }));
    Check("wrong independent lock kind refused", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.UnlockPathBound(authorized, workspaceId,
            factor.Id, LayerKind.Subfolder, currentPassword); }));
    Check("missing anchor refuses path-bound unlock", () =>
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(null).UnlockPathBound(authorized,
            workspaceId, factor.Id, factor.Kind, currentPassword); }));
    Check("stale minimum epoch refuses path-bound unlock", () =>
    {
        var p = new FakeAnchor { Grant = grant with { MinimumEpoch = 10 } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(p).UnlockPathBound(authorized,
            workspaceId, factor.Id, factor.Kind, currentPassword); });
    });
    Check("incorrect password refused with valid matching identity", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.UnlockPathBound(authorized, workspaceId,
            factor.Id, factor.Kind, formerPassword); }));
    Check("zero-byte vault refused after path is approved", () =>
    {
        File.WriteAllBytes(authorized, []);
        Throws<InvalidDataException>(() =>
        { using var _ = gate.UnlockPathBound(authorized, workspaceId,
            factor.Id, factor.Kind, currentPassword); });
        File.WriteAllBytes(authorized, currentVault);
    });
    Check("oversized vault refused after path approval", () =>
    {
        File.WriteAllBytes(authorized, new byte[1024 * 1024]);
        Throws<InvalidDataException>(() =>
        { using var _ = gate.UnlockPathBound(authorized, workspaceId,
            factor.Id, factor.Kind, currentPassword); });
        File.WriteAllBytes(authorized, currentVault);
    });
    Check("FileShare.None denies path-bound reader on Windows runner", () =>
    {
        using var lockStream = new FileStream(authorized, FileMode.Open,
            FileAccess.Read, FileShare.None);
        Throws<IOException>(() =>
        { using var _ = gate.UnlockPathBound(authorized, workspaceId,
            factor.Id, factor.Kind, currentPassword); });
    });
    Check("historical current pin refuses old ciphertext on approved path", () =>
    {
        File.WriteAllBytes(authorized, oldVault);
        Throws<CryptographicException>(() =>
        { using var _ = gate.UnlockPathBound(authorized, workspaceId,
            factor.Id, factor.Kind, formerPassword); });
        File.WriteAllBytes(authorized, currentVault);
    });
    Check("RISK: cloned same-byte file remains accessible through OLD opt-in gate", () =>
    {
        using var x = gate.Unlock(clone, workspaceId,
            factor.Id, factor.Kind, currentPassword);
        Yes(x.Id == factor.Id);
    });
    Check("RISK: cloned ciphertext remains accessible through unguarded disk API", () =>
    {
        using var x = new LayerVaultDiskStore().Unlock(clone,
            factor.Id, factor.Kind, currentPassword);
        Yes(x.Id == factor.Id);
    });
    Check("RISK: rolled-back trusted path grant and old ciphertext still open", () =>
    {
        File.WriteAllBytes(authorized, oldVault);
        var p = new FakeAnchor { Grant = new VaultAnchorGrant(workspaceId,
            factor.Id, factor.Kind, previousPin, 8, authorized) };
        using var x = new TrustedVaultOpenGate(p).UnlockPathBound(authorized,
            workspaceId, factor.Id, factor.Kind, formerPassword);
        Yes(x.Id == factor.Id);
    });
    Check("RISK: direct historical LayerKeyVault.Open remains a bypass", () =>
    {
        using var x = LayerKeyVault.Open(oldVault, factor.Id,
            factor.Kind, formerPassword);
        Yes(x.Id == factor.Id);
    });
}
finally { Directory.Delete(dir, recursive: true); }
Console.WriteLine($"{passed}/{passed + failed} Tur19 path-policy guard and bypass tests passed.");
return failed == 0 ? 0 : 1;

sealed class FakeAnchor : IExternalVaultAnchorReader
{
    public VaultAnchorGrant? Grant { get; set; }
    public bool TryRead(Guid workspaceId, Guid lockId,
        LayerKind kind, out VaultAnchorGrant? grant)
    {
        grant = Grant;
        return grant is not null;
    }
}
