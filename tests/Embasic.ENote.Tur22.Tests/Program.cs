using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

if (args is ["--read-stage", var childRoot, var ws, var epoch, var digest])
{
    var stamp = new VaultGenerationStamp(Guid.Parse(ws), long.Parse(epoch, CultureInfo.InvariantCulture), digest);
    Console.WriteLine(new VaultGenerationDiskStore(childRoot).Read(stamp)?.Stamp.ManifestSha256);
    return 0;
}
if (args is ["--stage-from", var sourceRoot, var targetRoot, var signalPath, var sourceWorkspace, var sourceEpoch, var sourceDigest])
{
    var stamp = new VaultGenerationStamp(Guid.Parse(sourceWorkspace),
        long.Parse(sourceEpoch, CultureInfo.InvariantCulture), sourceDigest);
    var manifest = new VaultGenerationDiskStore(sourceRoot).Read(stamp)
        ?? throw new InvalidDataException("Fixture source is missing.");
    Console.WriteLine("READY");
    Console.Out.Flush();
    if (!SpinWait.SpinUntil(() => File.Exists(signalPath), TimeSpan.FromSeconds(30))) return 4;
    new VaultGenerationDiskStore(targetRoot).Stage(manifest);
    return 0;
}
if (args is ["--partial-stage", var crashRoot])
{
    using var partial = new FileStream(Path.Combine(crashRoot, ".interrupted.egm-stage"),
        FileMode.CreateNew, FileAccess.Write, FileShare.None);
    partial.Write([0x45, 0x4e, 0x4f]);
    partial.Flush(flushToDisk: true);
    Console.WriteLine("READY");
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
    return 3;
}

const string Password = "Tur22 test credential";
const string Recovery = "Tur22 separate recovery";
const string Historical = "Tur22 former credential";
const string Domain = "Embasic.ENote.VaultGeneration.1";
var workspace = Guid.Parse("19df522c-933f-4ecc-bc63-ecc18b4ab411");
using var factor = LayerSecret.Generate(LayerKind.Folder);
using var child = LayerSecret.Generate(LayerKind.Subfolder);
var temp = Directory.CreateTempSubdirectory("enote-generation-store-");
string vaultPath = Path.Combine(temp.FullName, "Root-Ğ.elkey");
string childPath = Path.Combine(temp.FullName, "Child.elkey");
byte[] oldVault = LayerKeyVault.Create(factor, Historical, Recovery);
byte[] vault = LayerKeyVault.Rotate(oldVault, factor.Id, factor.Kind, Historical, Password, Recovery);
byte[] childVault = LayerKeyVault.Create(child, Password, Recovery);
File.WriteAllBytes(vaultPath, vault);
WindowsVaultFileIdentity fileId;
if (OperatingSystem.IsWindows())
{
    using var handle = File.OpenHandle(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    fileId = WindowsVaultFileIdentity.Read(handle);
}
else fileId = new WindowsVaultFileIdentity(1, 2, 3);

var rootGrant = new VaultAnchorGrant(workspace, factor.Id, factor.Kind,
    VaultSnapshotPin.CaptureAuthenticated(vault, factor.Id, factor.Kind, Password, 2), 2, vaultPath)
    { BoundVaultFileIdentity = fileId };
var childGrant = new VaultAnchorGrant(workspace, child.Id, child.Kind,
    VaultSnapshotPin.CaptureAuthenticated(childVault, child.Id, child.Kind, Password, 2), 2, childPath)
    { BoundVaultFileIdentity = new WindowsVaultFileIdentity(2, 4, 5) };
var oldGrant = rootGrant with
{
    MinimumEpoch = 1,
    Pin = VaultSnapshotPin.CaptureAuthenticated(oldVault, factor.Id, factor.Kind, Historical, 1)
};
var prior = VaultGenerationManifest.Create(workspace, 1, [oldGrant]);
var current = VaultGenerationManifest.Create(workspace, 2, [rootGrant, childGrant]);
var competing = VaultGenerationManifest.Create(workspace, 2,
    [rootGrant with { BoundVaultPath = Path.Combine(temp.FullName, "Rival.elkey") }, childGrant]);
var ordered = new[] { rootGrant, childGrant }.OrderBy(g => g.LockId).ToArray();
byte[] canonical = Encode(ordered);
int countOffset = 1 + Encoding.UTF8.GetByteCount(Domain) + 16 + 8;
int firstPathPrefix = countOffset + 4 + 16 + 1;

int passed = 0, failed = 0;
try
{
    Check("canonical v1 bytes retain the existing manifest commitment", () =>
        Equal(current.Stamp, Stamp(canonical)));
    Check("verified decode restores path, Windows identity and digest without a credential", () =>
    {
        var m = Decode(canonical, current.Stamp);
        var restored = m.Find(factor.Id, factor.Kind) ?? throw new InvalidOperationException("Missing grant");
        Equal(rootGrant.BoundVaultPath, restored.BoundVaultPath);
        Equal(rootGrant.BoundVaultFileIdentity, restored.BoundVaultFileIdentity);
        Equal(rootGrant.Pin.CiphertextSha256, restored.Pin.CiphertextSha256);
        Equal(2L, restored.Pin.TrustedEpoch);
        using var opened = restored.Pin.OpenPinned(vault, Password, 2);
        Equal(factor.Id, opened.Id);
    });
    Check("untrusted malformed bytes fail digest verification before parser errors", () =>
    {
        byte[] altered = (byte[])canonical.Clone();
        altered[0] = 0xff;
        Throws<CryptographicException>(() => Decode(altered, current.Stamp));
    });
    Check("same hash with different workspace or epoch is refused", () =>
    {
        Throws<CryptographicException>(() => Decode(canonical, new VaultGenerationStamp(Guid.NewGuid(), 2, current.Stamp.ManifestSha256)));
        Throws<CryptographicException>(() => Decode(canonical, new VaultGenerationStamp(workspace, 3, current.Stamp.ManifestSha256)));
    });
    Check("oversized source is refused before a read or allocation", () =>
    {
        using var huge = new OversizedSource();
        Throws<InvalidDataException>(() => VaultGenerationManifest.ReadVerified(huge, current.Stamp));
    });
    Check("reader rejects growth after the reported snapshot length", () =>
    {
        using var growing = new GrowingSource(canonical);
        Throws<InvalidDataException>(() => VaultGenerationManifest.ReadVerified(growing, current.Stamp));
    });
    Check("reader uses the remaining stream snapshot", () =>
    {
        using var input = new MemoryStream([0xaa, ..canonical], writable: false);
        input.Position = 1;
        Equal(current.Stamp, VaultGenerationManifest.ReadVerified(input, current.Stamp).Stamp);
    });
    Check("verified digest does not permit an excessive count", () =>
    {
        byte[] bytes = (byte[])canonical.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(countOffset, 4), int.MaxValue);
        Throws<InvalidDataException>(() => Decode(bytes, Stamp(bytes)));
    });
    Check("verified digest does not permit an oversized string length", () =>
    {
        byte[] bytes = (byte[])canonical.Clone();
        byte[] length = [0xff, 0xff, 0xff, 0xff, 0x07];
        length.CopyTo(bytes, firstPathPrefix);
        Throws<InvalidDataException>(() => Decode(bytes, Stamp(bytes)));
    });
    Check("verified digest does not permit malformed UTF-8", () =>
    {
        byte[] bytes = (byte[])canonical.Clone();
        using var reader = new BinaryReader(new MemoryStream(bytes));
        reader.BaseStream.Position = firstPathPrefix;
        reader.Read7BitEncodedInt();
        bytes[(int)reader.BaseStream.Position] = 0xff;
        Throws<InvalidDataException>(() => Decode(bytes, Stamp(bytes)));
    });
    Check("verified digest does not permit truncated entry", () =>
    {
        byte[] bytes = canonical[..^1];
        Throws<InvalidDataException>(() => Decode(bytes, Stamp(bytes)));
    });
    Check("verified digest does not permit trailing bytes", () =>
    {
        byte[] bytes = [..canonical, 0];
        Throws<InvalidDataException>(() => Decode(bytes, Stamp(bytes)));
    });
    Check("alternate length encoding is rejected as noncanonical", () =>
    {
        byte[] bytes = [(byte)(canonical[0] | 0x80), 0, ..canonical.AsSpan(1)];
        Throws<InvalidDataException>(() => Decode(bytes, Stamp(bytes)));
    });
    Check("noncanonical order is rejected even with a matching supplied digest", () =>
    {
        byte[] bytes = Encode(ordered.Reverse().ToArray());
        Throws<InvalidDataException>(() => Decode(bytes, Stamp(bytes)));
    });
    Check("duplicate factor data is rejected even with a matching supplied digest", () =>
    {
        byte[] bytes = Encode([rootGrant, rootGrant]);
        Throws<InvalidDataException>(() => Decode(bytes, Stamp(bytes)));
    });
    Check("relative enrolled path is rejected from a digest-matched payload", () =>
    {
        byte[] bytes = Encode([rootGrant with { BoundVaultPath = "relative.elkey" }]);
        Throws<InvalidDataException>(() => Decode(bytes, Stamp(bytes)));
    });
    Check("stage publishes exact canonical bytes and reopens through a new store", () =>
    {
        var dir = CaseRoot();
        new VaultGenerationDiskStore(dir).Stage(current);
        string path = Published(dir, current.Stamp);
        if (!File.ReadAllBytes(path).SequenceEqual(canonical)) throw new InvalidOperationException("Unexpected on-disk bytes");
        Equal(current.Stamp, new VaultGenerationDiskStore(dir).Read(current.Stamp)?.Stamp);
        Equal(1, Directory.EnumerateFiles(dir).Count());
    });
    Check("an independent process can reopen the exact staged manifest", () =>
    {
        var dir = CaseRoot();
        new VaultGenerationDiskStore(dir).Stage(current);
        using var process = StartChild("--read-stage", dir, workspace.ToString(),
            current.Stamp.Epoch.ToString(CultureInfo.InvariantCulture), current.Stamp.ManifestSha256);
        string? value = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        if (!process.WaitForExit(30_000)) { process.Kill(); throw new TimeoutException(); }
        Equal(0, process.ExitCode);
        Equal(current.Stamp.ManifestSha256, value);
    });
    Check("restaging identical content preserves the already published file", () =>
    {
        var dir = CaseRoot();
        var store = new VaultGenerationDiskStore(dir);
        store.Stage(current);
        string path = Published(dir, current.Stamp);
        File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        DateTime before = File.GetLastWriteTimeUtc(path);
        store.Stage(current);
        Equal(before, File.GetLastWriteTimeUtc(path));
        Equal(1, Directory.EnumerateFiles(dir).Count());
    });
    Check("different digests at the same epoch coexist and the anchor selects one", () =>
    {
        var dir = CaseRoot();
        var store = new VaultGenerationDiskStore(dir);
        store.Stage(current);
        store.Stage(competing);
        Equal(2, Directory.EnumerateFiles(dir, "*.egm").Count());
        var state = new VaultGenerationRecovery(new SelectedAnchor(competing.Stamp), store);
        Equal(competing.Stamp, state.ReadCommitted(workspace).Stamp);
    });
    Check("corrupt bytes at the final name block read and restage without overwrite", () =>
    {
        var dir = CaseRoot();
        var store = new VaultGenerationDiskStore(dir);
        store.Stage(current);
        string path = Published(dir, current.Stamp);
        byte[] corrupt = (byte[])canonical.Clone();
        corrupt[^1] ^= 1;
        File.WriteAllBytes(path, corrupt);
        Throws<CryptographicException>(() => store.Read(current.Stamp));
        Throws<CryptographicException>(() => store.Stage(current));
        if (!File.ReadAllBytes(path).SequenceEqual(corrupt)) throw new InvalidOperationException("Corruption was overwritten");
    });
    Check("missing selected generation does not fall back to the prior or competing file", () =>
    {
        var store = new VaultGenerationDiskStore(CaseRoot());
        store.Stage(prior);
        store.Stage(competing);
        Equal<VaultGenerationManifest?>(null, store.Read(current.Stamp));
        var state = new VaultGenerationRecovery(new SelectedAnchor(current.Stamp), store);
        Throws<CryptographicException>(() => state.ReadCommitted(workspace));
    });
    Check("partial published file fails closed and is preserved", () =>
    {
        var dir = CaseRoot();
        var store = new VaultGenerationDiskStore(dir);
        store.Stage(current);
        string path = Published(dir, current.Stamp);
        File.WriteAllBytes(path, canonical[..5]);
        Throws<InvalidDataException>(() => store.Read(current.Stamp));
        Throws<InvalidDataException>(() => store.Stage(current));
        Equal(5L, new FileInfo(path).Length);
    });
    Check("a terminated partial writer leaves no committed file and an orphan cannot unlock", () =>
    {
        var dir = CaseRoot();
        using var process = StartChild("--partial-stage", dir);
        try
        {
            string? signal = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            Equal("READY", signal);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            if (!process.WaitForExit(30_000)) throw new TimeoutException();
        }
        var store = new VaultGenerationDiskStore(dir);
        Equal<VaultGenerationManifest?>(null, store.Read(current.Stamp));
        Equal(1, Directory.EnumerateFiles(dir, "*.egm-stage").Count());
        store.Stage(current);
        Equal(current.Stamp, store.Read(current.Stamp)?.Stamp);
        Equal(1, Directory.EnumerateFiles(dir, "*.egm-stage").Count());
    });
    Check("concurrent idempotent staging publishes one file and leaves no live temporaries", () =>
    {
        var dir = CaseRoot();
        using var barrier = new Barrier(2);
        void Stage()
        {
            var store = new VaultGenerationDiskStore(dir);
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            store.Stage(current);
        }
        Task.WaitAll(Task.Run(Stage), Task.Run(Stage));
        Equal(1, Directory.EnumerateFiles(dir).Count());
        Equal(current.Stamp, new VaultGenerationDiskStore(dir).Read(current.Stamp)?.Stamp);
    });
    Check("two independent publishers reconcile the same final manifest", () =>
    {
        var source = CaseRoot();
        var target = CaseRoot();
        var signal = Path.Combine(source, "start");
        new VaultGenerationDiskStore(source).Stage(current);
        string[] args = ["--stage-from", source, target, signal, workspace.ToString(),
            current.Stamp.Epoch.ToString(CultureInfo.InvariantCulture), current.Stamp.ManifestSha256];
        using var left = StartChild(args);
        using var right = StartChild(args);
        try
        {
            Equal("READY", ReadSignal(left));
            Equal("READY", ReadSignal(right));
            File.WriteAllText(signal, "");
            if (!left.WaitForExit(30_000) || !right.WaitForExit(30_000)) throw new TimeoutException();
            Equal(0, left.ExitCode);
            Equal(0, right.ExitCode);
        }
        finally
        {
            if (!left.HasExited) left.Kill();
            if (!right.HasExited) right.Kill();
            left.WaitForExit(30_000);
            right.WaitForExit(30_000);
        }
        Equal(1, Directory.EnumerateFiles(target).Count());
        Equal(current.Stamp, new VaultGenerationDiskStore(target).Read(current.Stamp)?.Stamp);
    });
    Check("disk stage survives a lost CAS reply and a new coordinator", () =>
    {
        var dir = CaseRoot();
        var store = new VaultGenerationDiskStore(dir);
        store.Stage(prior);
        var anchor = new AdvancingAnchor(prior.Stamp, () =>
            Equal(current.Stamp, new VaultGenerationDiskStore(dir).Read(current.Stamp)?.Stamp));
        Equal(VaultGenerationAdvanceState.Committed,
            new VaultGenerationRecovery(anchor, store).StageAndAdvance(prior.Stamp, current));
        Equal(1, anchor.Attempts);
        Equal(current.Stamp,
            new VaultGenerationRecovery(anchor, new VaultGenerationDiskStore(dir)).ReadCommitted(workspace).Stamp);
    });
    Check("corrupt occupied candidate blocks disk stage before CAS", () =>
    {
        var dir = CaseRoot();
        var store = new VaultGenerationDiskStore(dir);
        store.Stage(prior);
        store.Stage(current);
        string occupied = Published(dir, current.Stamp);
        byte[] corrupt = (byte[])canonical.Clone();
        corrupt[^1] ^= 1;
        File.WriteAllBytes(occupied, corrupt);
        var anchor = new AdvancingAnchor(prior.Stamp, () => throw new InvalidOperationException("Unexpected CAS"));
        var coordinator = new VaultGenerationRecovery(anchor, store);
        Throws<CryptographicException>(() => coordinator.StageAndAdvance(prior.Stamp, current));
        Equal(0, anchor.Attempts);
        Equal(prior.Stamp, coordinator.ReadCommitted(workspace).Stamp);
    });
    Check("reopened disk store resolves a committed grant and rejects historical vault bytes", () =>
    {
        var dir = CaseRoot();
        new VaultGenerationDiskStore(dir).Stage(current);
        var recovery = new VaultGenerationRecovery(new SelectedAnchor(current.Stamp), new VaultGenerationDiskStore(dir));
        var gate = new TrustedVaultOpenGate(recovery);
        using (var opened = Open(gate, Password)) Equal(factor.Id, opened.Id);
        try
        {
            File.WriteAllBytes(vaultPath, oldVault);
            Throws<CryptographicException>(() => { using var _ = Open(gate, Historical); });
        }
        finally { File.WriteAllBytes(vaultPath, vault); }
    });
}
finally { temp.Delete(recursive: true); }

Console.WriteLine($"{passed}/{passed + failed} Tur22 verified manifest store checks passed.");
return failed == 0 ? 0 : 1;

LayerSecret Open(TrustedVaultOpenGate gate, string credential) =>
    OperatingSystem.IsWindows()
        ? gate.UnlockFileBound(vaultPath, workspace, factor.Id, factor.Kind, credential)
        : gate.Unlock(vaultPath, workspace, factor.Id, factor.Kind, credential);

string CaseRoot() => Directory.CreateDirectory(Path.Combine(temp.FullName, Guid.NewGuid().ToString("N"))).FullName;

byte[] Encode(VaultAnchorGrant[] entries)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true);
    writer.Write(Domain);
    writer.Write(workspace.ToByteArray());
    writer.Write(2L);
    writer.Write(entries.Length);
    foreach (var grant in entries)
    {
        writer.Write(grant.LockId.ToByteArray());
        writer.Write((byte)grant.Kind);
        writer.Write(grant.BoundVaultPath!);
        var identity = grant.BoundVaultFileIdentity!.Value;
        writer.Write(identity.VolumeSerialNumber);
        writer.Write(identity.FileIdPart1);
        writer.Write(identity.FileIdPart2);
        writer.Write(Convert.FromHexString(grant.Pin.CiphertextSha256));
    }
    return stream.ToArray();
}
VaultGenerationStamp Stamp(byte[] bytes) => new(workspace, 2, Convert.ToHexString(SHA256.HashData(bytes)));

static VaultGenerationManifest Decode(byte[] bytes, VaultGenerationStamp stamp)
{
    using var stream = new MemoryStream(bytes, writable: false);
    return VaultGenerationManifest.ReadVerified(stream, stamp);
}
static string Published(string dir, VaultGenerationStamp stamp) =>
    Directory.EnumerateFiles(dir, "*" + stamp.ManifestSha256 + ".egm").Single();

static string? ReadSignal(Process process) =>
    process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

static Process StartChild(params string[] arguments)
{
    string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable");
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true };
    if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (string argument in arguments) info.ArgumentList.Add(argument);
    return Process.Start(info) ?? throw new IOException("Could not launch fixture process");
}
void Check(string label, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + label); }
    catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + label + ": " + e); }
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
static void Throws<T>(Action test) where T : Exception
{
    try { test(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}

sealed class GrowingSource(byte[] bytes) : MemoryStream(bytes)
{
    public override int ReadByte() => Position == Length ? 1 : base.ReadByte();
}
sealed class OversizedSource : MemoryStream
{
    public override long Length => long.MaxValue;
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Unexpected read");
    public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Unexpected read");
}
sealed class SelectedAnchor(VaultGenerationStamp selected) : IExternalVaultGenerationAnchor
{
    public VaultGenerationStamp? Read(Guid workspaceId) => selected.WorkspaceId == workspaceId ? selected : null;
    public void CompareAndAdvance(VaultGenerationStamp expected, VaultGenerationStamp next) =>
        throw new InvalidOperationException("This fixture is read-only");
}

sealed class AdvancingAnchor(VaultGenerationStamp initial, Action beforeAdvance) : IExternalVaultGenerationAnchor
{
    private VaultGenerationStamp _current = initial;
    public int Attempts { get; private set; }
    public VaultGenerationStamp? Read(Guid workspaceId) => _current.WorkspaceId == workspaceId ? _current : null;

    public void CompareAndAdvance(VaultGenerationStamp expected, VaultGenerationStamp next)
    {
        Attempts++;
        beforeAdvance();
        if (_current != expected) return;
        _current = next;
        throw new VaultGenerationOutcomeUnknownException("Fixture lost the CAS reply.");
    }
}
