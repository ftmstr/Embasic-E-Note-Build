using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

if (!OperatingSystem.IsWindows())
{
    try
    {
        using var _ = new TrustedVaultOpenGate(null).UnlockFileBound(
            "ignored.elkey", Guid.NewGuid(), Guid.NewGuid(), LayerKind.Folder, "ignored");
    }
    catch (PlatformNotSupportedException)
    {
        Console.WriteLine("PASS non-Windows host refuses the file-bound API");
        Console.WriteLine("NOT RUN Windows file identity cases");
        return 0;
    }
    Console.Error.WriteLine("FAIL non-Windows host did not refuse the file-bound API");
    return 1;
}

const string formerPassword = "Tur20 historical fixture password";
const string password = "Tur20 current fixture password";
const string recovery = "Tur20 separate fixture recovery";
Guid workspace = Guid.NewGuid();
using var factor = LayerSecret.Generate(LayerKind.Folder);
byte[] oldBytes = LayerKeyVault.Create(factor, formerPassword, recovery);
byte[] bytes = LayerKeyVault.Rotate(oldBytes, factor.Id, factor.Kind, formerPassword, password, recovery);
var oldPin = VaultSnapshotPin.CaptureAuthenticated(oldBytes, factor.Id, factor.Kind, formerPassword, 1);
var pin = VaultSnapshotPin.CaptureAuthenticated(bytes, factor.Id, factor.Kind, password, 2);
using var root = new TemporaryDirectory(Directory.CreateTempSubdirectory("enote-tur20-").FullName);
string vault = Path.Combine(root.Path, "Active.elkey");
File.WriteAllBytes(vault, bytes);
var grant = new VaultAnchorGrant(workspace, factor.Id, factor.Kind, pin, 2, vault)
    { BoundVaultFileIdentity = ReadIdentity(vault) };

int passed = 0, failed = 0;
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + e); }
}
void Open(string path, VaultAnchorGrant policy, string credential = password, bool isRecovery = false)
{
    using var opened = new TrustedVaultOpenGate(new FixtureAnchor(policy))
        .UnlockFileBound(path, workspace, factor.Id, factor.Kind, credential, isRecovery);
    if (opened.Id != factor.Id) throw new InvalidOperationException("Unexpected lock identity.");
}

Check("exact path and enrolled file identity open the current vault", () => Open(vault, grant));
Check("independent recovery credential opens the enrolled file", () => Open(vault, grant, recovery, true));
Check("missing file identity fails closed", () =>
    Throws<CryptographicException>(() => Open(vault, grant with { BoundVaultFileIdentity = null })));
Check("default file identity fails closed", () =>
    Throws<CryptographicException>(() => Open(vault, grant with { BoundVaultFileIdentity = default(WindowsVaultFileIdentity) })));
Check("relative requested path fails closed", () =>
    Throws<CryptographicException>(() => Open("Active.elkey", grant)));
Check("different path casing is refused by the exact Windows policy", () =>
    Throws<CryptographicException>(() => Open(Path.Combine(root.Path, "active.elkey"), grant)));

Check("same-byte clone substituted at approved path fails file identity check", () =>
{
    string original = Path.Combine(root.Path, "Original.elkey");
    File.Move(vault, original);
    try
    {
        File.Copy(original, vault);
        Throws<CryptographicException>(() => Open(vault, grant));
    }
    finally
    {
        File.Delete(vault);
        File.Move(original, vault);
    }
});
Check("renamed file keeps its identity but its new path needs authorization", () =>
{
    string moved = Path.Combine(root.Path, "Moved.elkey");
    File.Move(vault, moved);
    try
    {
        Throws<CryptographicException>(() => Open(moved, grant));
        Open(moved, grant with { BoundVaultPath = moved });
    }
    finally { File.Move(moved, vault); }
});
Check("hard link shares identity but an unapproved alias is refused", () =>
{
    string alias = Path.Combine(root.Path, "Alias.elkey");
    NativeTestLinks.CreateHardLink(alias, vault);
    try
    {
        if (ReadIdentity(alias) != grant.BoundVaultFileIdentity)
            throw new InvalidOperationException("Expected both names to refer to one file.");
        Throws<CryptographicException>(() => Open(alias, grant));
    }
    finally { File.Delete(alias); }
});
Check("retargeted ancestor junction to an identical clone is refused", () =>
{
    string original = Directory.CreateDirectory(Path.Combine(root.Path, "target-a")).FullName;
    string clone = Directory.CreateDirectory(Path.Combine(root.Path, "target-b")).FullName;
    foreach (string target in new[] { original, clone })
        File.Copy(vault, Path.Combine(Directory.CreateDirectory(Path.Combine(target, "nested")).FullName, "Vault.elkey"));

    string junction = Path.Combine(root.Path, "linked");
    string linkedVault = Path.Combine(junction, "nested", "Vault.elkey");
    try
    {
        NativeTestLinks.CreateJunction(junction, original);
        var enrolled = grant with { BoundVaultPath = linkedVault, BoundVaultFileIdentity = ReadIdentity(linkedVault) };
        Open(linkedVault, enrolled);
        Directory.Delete(junction);
        NativeTestLinks.CreateJunction(junction, clone);
        Throws<CryptographicException>(() => Open(linkedVault, enrolled));
    }
    finally { if (Directory.Exists(junction)) Directory.Delete(junction); }
});
Check("FileShare.None prevents opening the enrolled file", () =>
{
    using var locked = new FileStream(vault, FileMode.Open, FileAccess.Read, FileShare.None);
    Throws<IOException>(() => Open(vault, grant));
});
Check("wrong length on the enrolled file is refused before reading ciphertext", () =>
{
    try
    {
        File.WriteAllBytes(vault, new byte[1024 * 1024]);
        Throws<InvalidDataException>(() => Open(vault, grant));
    }
    finally { File.WriteAllBytes(vault, bytes); }
});
Check("same-size in-place ciphertext corruption is refused", () =>
{
    byte[] corrupted = (byte[])bytes.Clone();
    corrupted[^1] ^= 1;
    try
    {
        File.WriteAllBytes(vault, corrupted);
        Throws<CryptographicException>(() => Open(vault, grant));
    }
    finally { File.WriteAllBytes(vault, bytes); }
});
Check("RISK rolled-back grant and old bytes written in place still open", () =>
{
    try
    {
        File.WriteAllBytes(vault, oldBytes);
        Throws<CryptographicException>(() => Open(vault, grant));
        Open(vault, grant with { Pin = oldPin, MinimumEpoch = 1 }, formerPassword);
    }
    finally { File.WriteAllBytes(vault, bytes); }
});

Console.WriteLine($"{passed}/{passed + failed} Tur20 Windows vault file identity checks passed.");
return failed == 0 ? 0 : 1;

static WindowsVaultFileIdentity ReadIdentity(string path)
{
    using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    return WindowsVaultFileIdentity.Read(handle);
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}

sealed class FixtureAnchor(VaultAnchorGrant grant) : IExternalVaultAnchorReader
{
    public bool TryRead(Guid workspaceId, Guid lockId, LayerKind kind, out VaultAnchorGrant? value)
    {
        value = grant;
        return true;
    }
}

sealed class TemporaryDirectory(string path) : IDisposable
{
    public string Path { get; } = path;
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

static class NativeTestLinks
{
    internal static void CreateHardLink(string link, string target)
    {
        if (!CreateHardLinkW(link, target, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string link, string target, IntPtr securityAttributes);

    internal static void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false
        };
        using var process = Process.Start(start) ?? throw new IOException("Unable to start mklink.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("mklink failed: " + process.ExitCode);
    }
}
