using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

int pass = 0, fail = 0;
void Check(string label, Action action)
{
    try { action(); pass++; Console.WriteLine("PASS " + label); }
    catch(Exception error) { fail++; Console.Error.WriteLine("FAIL " + label + ": " + error); }
}
static void Yes(bool check) { if(!check) throw new Exception("Assertion failed"); }
static void Throws<T>(Action action) where T:Exception
{
    try { action(); } catch(T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
const string oldPassword = "Tur17 former folder password 2026";
const string rotated = "Tur17 updated folder password 2026";
const string recovery = "Tur17 independent recovery secret 2026";
var workspaceId = Guid.NewGuid();
using var root = LayerSecret.Generate(LayerKind.Folder);
byte[] oldVault = LayerKeyVault.Create(root, oldPassword, recovery);
byte[] currentVault = LayerKeyVault.Rotate(oldVault, root.Id,
    root.Kind, oldPassword, rotated, recovery);
var oldPin = VaultSnapshotPin.CaptureAuthenticated(oldVault,
    root.Id, root.Kind, oldPassword, 6);
var newPin = VaultSnapshotPin.CaptureAuthenticated(currentVault,
    root.Id, root.Kind, rotated, 7);
var trusted = new TestAnchor
{
    Grant = new VaultAnchorGrant(workspaceId, root.Id, root.Kind, newPin, 7)
};
var gate = new TrustedVaultOpenGate(trusted);
var folder = Path.Combine(Path.GetTempPath(),
    "enote-tur17-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
string path = Path.Combine(folder, "Root.elkey");
try
{
    File.WriteAllBytes(path, currentVault);

    Check("correct externally supplied current anchor opens current copy", () =>
    {
        using var f = gate.Unlock(path, workspaceId, root.Id, root.Kind, rotated);
        Yes(f.Id == root.Id && f.Kind == LayerKind.Folder);
    });
    Check("correct current anchor permits explicitly independent recovery", () =>
    {
        using var f = gate.Unlock(path, workspaceId, root.Id, root.Kind,
            recovery, recovery: true);
        Yes(f.Id == root.Id);
    });
    Check("no external anchor refuses even correct current credential", () =>
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(null).Unlock(
            path, workspaceId, root.Id, root.Kind, rotated); }));
    Check("anchor unavailable refuses all credential attempts", () =>
    {
        var missing = new TestAnchor();
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(missing).Unlock(
            path, workspaceId, root.Id, root.Kind, rotated); });
    });
    Check("empty workspace identifier refused", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.Unlock(path, Guid.Empty, root.Id, root.Kind, rotated); }));
    Check("empty factor identifier refused", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.Unlock(path, workspaceId, Guid.Empty, root.Kind, rotated); }));
    Check("wrong workspace refused without fallback", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.Unlock(path, Guid.NewGuid(), root.Id, root.Kind, rotated); }));
    Check("wrong lock id refused without fallback", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.Unlock(path, workspaceId, Guid.NewGuid(), root.Kind, rotated); }));
    Check("wrong lock kind refused without fallback", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.Unlock(path, workspaceId, root.Id, LayerKind.Subfolder, rotated); }));
    Check("anchor with wrong declared workspace refused", () =>
    {
        var a = new TestAnchor { Grant = trusted.Grant! with { WorkspaceId = Guid.NewGuid() } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(a).Unlock(
            path, workspaceId, root.Id, root.Kind, rotated); });
    });
    Check("anchor with wrong declared lock identifier refused", () =>
    {
        var a = new TestAnchor { Grant = trusted.Grant! with { LockId = Guid.NewGuid() } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(a).Unlock(
            path, workspaceId, root.Id, root.Kind, rotated); });
    });
    Check("stale epoch threshold fails closed", () =>
    {
        var a = new TestAnchor { Grant = trusted.Grant! with { MinimumEpoch = 8 } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(a).Unlock(
            path, workspaceId, root.Id, root.Kind, rotated); });
    });
    Check("nonpositive epoch threshold fails closed", () =>
    {
        var a = new TestAnchor { Grant = trusted.Grant! with { MinimumEpoch = 0 } };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(a).Unlock(
            path, workspaceId, root.Id, root.Kind, rotated); });
    });
    Check("old vault swapped on disk rejected by current pin", () =>
    {
        File.WriteAllBytes(path, oldVault);
        Throws<CryptographicException>(() =>
        { using var _ = gate.Unlock(path, workspaceId, root.Id, root.Kind, oldPassword); });
    });
    Check("stale old pin against newer vault refused", () =>
    {
        File.WriteAllBytes(path, currentVault);
        var a = new TestAnchor
        { Grant = new VaultAnchorGrant(workspaceId, root.Id, root.Kind, oldPin, 6) };
        Throws<CryptographicException>(() =>
        { using var _ = new TrustedVaultOpenGate(a).Unlock(
            path, workspaceId, root.Id, root.Kind, oldPassword); });
    });
    Check("wrong password rejected even with matching current anchor", () =>
        Throws<CryptographicException>(() =>
        { using var _ = gate.Unlock(path, workspaceId, root.Id, root.Kind, oldPassword); }));
    Check("tampered current vault rejected before layer key is released", () =>
    {
        byte[] corrupt = currentVault.ToArray(); corrupt[^1] ^= 0x80;
        File.WriteAllBytes(path, corrupt);
        Throws<CryptographicException>(() =>
        { using var _ = gate.Unlock(path, workspaceId, root.Id, root.Kind, rotated); });
    });
    Check("truncated current vault rejected", () =>
    {
        File.WriteAllBytes(path, currentVault[..^1]);
        Throws<InvalidDataException>(() =>
        { using var _ = gate.Unlock(path, workspaceId, root.Id, root.Kind, rotated); });
    });
    Check("FileShare.None lock prevents simultaneous guarded read on Windows", () =>
    {
        File.WriteAllBytes(path, currentVault);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.None);
        Throws<IOException>(() =>
        { using var _ = gate.Unlock(path, workspaceId, root.Id, root.Kind, rotated); });
    });
    Check("missing encrypted vault file refuses read", () =>
    {
        File.Delete(path);
        Throws<FileNotFoundException>(() =>
        { using var _ = gate.Unlock(path, workspaceId, root.Id, root.Kind, rotated); });
    });
    Check("INSECURE old policy and old vault accepted if anchor is rolled back", () =>
    {
        File.WriteAllBytes(path, oldVault);
        var rolledBack = new TestAnchor
        { Grant = new VaultAnchorGrant(workspaceId, root.Id, root.Kind, oldPin, 6) };
        using var recovered = new TrustedVaultOpenGate(rolledBack).Unlock(
            path, workspaceId, root.Id, root.Kind, oldPassword);
        Yes(recovered.Id == root.Id);
    });
    Check("INSECURE historical unguarded LayerVaultDiskStore.Unlock bypass survives", () =>
    {
        using var recovered = new LayerVaultDiskStore().Unlock(
            path, root.Id, root.Kind, oldPassword);
        Yes(recovered.Id == root.Id);
    });
    Check("INSECURE historical LayerKeyVault.Open also bypasses gate", () =>
    {
        using var recovered = LayerKeyVault.Open(oldVault, root.Id, root.Kind, oldPassword);
        Yes(recovered.Kind == LayerKind.Folder);
    });
}
finally { Directory.Delete(folder, recursive: true); }
Console.WriteLine($"{pass}/{pass+fail} Tur17 fail-closed vault gate tests passed.");
return fail==0 ? 0 : 1;

sealed class TestAnchor : IExternalVaultAnchorReader
{
    public VaultAnchorGrant? Grant { get; set; }
    public bool TryRead(Guid workspaceId, Guid lockId, LayerKind kind,
        out VaultAnchorGrant? grant)
    {
        grant = Grant;
        return grant is not null;
    }
}
