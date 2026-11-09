using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

if (args is ["--record", var journal, var signal, var ws, var epoch, var before, var after])
{
    var workspaceId = Guid.Parse(ws);
    var expectedEpoch = long.Parse(epoch, CultureInfo.InvariantCulture);
    var childIntent = new VaultRetryIntent(new(workspaceId, expectedEpoch, before), new(workspaceId, expectedEpoch + 1, after));
    using var store = new VaultRetryIntentDiskStore(journal, FixtureKey());
    Console.WriteLine("READY");
    Console.Out.Flush();
    if (!SpinWait.SpinUntil(() => File.Exists(signal), TimeSpan.FromSeconds(30))) return 4;
    try { store.Record(childIntent); return 0; }
    catch (CryptographicException) { return 2; }
}

const string Password = "Tur23 fixture credential";
const string Recovery = "Tur23 separate recovery";
var workspace = Guid.Parse("48bf4b93-f5e8-470f-b998-9824158d152c");
var root = Directory.CreateTempSubdirectory("enote-retry-");
string vaultPath = Path.Combine(root.FullName, "Folder.elkey");
using var factor = LayerSecret.Generate(LayerKind.Folder);
byte[] vault = LayerKeyVault.Create(factor, Password, Recovery);
File.WriteAllBytes(vaultPath, vault);
WindowsVaultFileIdentity fileId;
if (OperatingSystem.IsWindows())
{
    using var handle = File.OpenHandle(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    fileId = WindowsVaultFileIdentity.Read(handle);
}
else fileId = new WindowsVaultFileIdentity(1, 2, 3);

var prior = Generation(1, vaultPath);
var next = Generation(2, vaultPath);
var rival = Generation(2, Path.Combine(root.FullName, "Rival.elkey"));
var third = Generation(3, vaultPath);
var intent = new VaultRetryIntent(prior.Stamp, next.Stamp);
var conflicting = new VaultRetryIntent(prior.Stamp, rival.Stamp);
int passed = 0, failed = 0;
try
{
    Check("intent requires the same workspace and exactly the next epoch", () =>
    {
        Throws<ArgumentException>(() => new VaultRetryIntent(prior.Stamp, third.Stamp));
        Throws<ArgumentException>(() => new VaultRetryIntent(prior.Stamp,
            new VaultGenerationStamp(Guid.NewGuid(), 2, next.Stamp.ManifestSha256)));
        Throws<ArgumentException>(() => new VaultRetryIntent(
            new VaultGenerationStamp(workspace, long.MaxValue, next.Stamp.ManifestSha256), next.Stamp));
    });
    Check("intent reopens with the same key and exact stamps", () =>
    {
        string dir = CaseDirectory();
        using (var store = new VaultRetryIntentDiskStore(dir, FixtureKey()))
        {
            Equal<VaultRetryIntent?>(null, store.Read(workspace, 1));
            store.Record(intent);
        }
        using var reopened = new VaultRetryIntentDiskStore(dir, FixtureKey());
        Equal(intent, reopened.Read(workspace, 1));
        Equal(1, Directory.EnumerateFiles(dir).Count());
    });
    Check("key material is copied and invalid length or disposed store is refused", () =>
    {
        var key = FixtureKey();
        string dir = CaseDirectory();
        Throws<ArgumentException>(() => new VaultRetryIntentDiskStore(dir, [0]));
        using var store = new VaultRetryIntentDiskStore(dir, key);
        Array.Clear(key);
        store.Record(intent);
        using var reopened = new VaultRetryIntentDiskStore(dir, FixtureKey());
        Equal(intent, reopened.Read(workspace, 1));
        store.Dispose();
        Throws<ObjectDisposedException>(() => store.Read(workspace, 1));
        Throws<ObjectDisposedException>(() => store.Record(intent));
    });
    Check("same intent is idempotent and competing stamp cannot replace it", () =>
    {
        string dir = CaseDirectory();
        using var store = new VaultRetryIntentDiskStore(dir, FixtureKey());
        store.Record(intent);
        string file = RecordFile(dir);
        File.SetLastWriteTimeUtc(file, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var modified = File.GetLastWriteTimeUtc(file);
        store.Record(intent);
        Equal(modified, File.GetLastWriteTimeUtc(file));
        Throws<CryptographicException>(() => store.Record(conflicting));
        Equal(intent, store.Read(workspace, 1));
    });
    Check("wrong key or changed authenticated bytes are refused", () =>
    {
        string dir = CaseDirectory();
        using var store = new VaultRetryIntentDiskStore(dir, FixtureKey());
        store.Record(intent);
        using var wrongKey = new VaultRetryIntentDiskStore(dir, new byte[32]);
        Throws<CryptographicException>(() => wrongKey.Read(workspace, 1));
        string file = RecordFile(dir);
        byte[] corrupt = File.ReadAllBytes(file);
        corrupt[50] ^= 1;
        File.WriteAllBytes(file, corrupt);
        Throws<CryptographicException>(() => store.Read(workspace, 1));
        Throws<CryptographicException>(() => store.Record(intent));
        Equal(true, corrupt.SequenceEqual(File.ReadAllBytes(file)));
    });
    Check("truncation and trailing bytes are refused without replacement", () =>
    {
        string dir = CaseDirectory();
        using var store = new VaultRetryIntentDiskStore(dir, FixtureKey());
        store.Record(intent);
        string file = RecordFile(dir);
        byte[] bytes = File.ReadAllBytes(file);
        File.WriteAllBytes(file, bytes[..^1]);
        Throws<InvalidDataException>(() => store.Read(workspace, 1));
        File.WriteAllBytes(file, [..bytes, 0]);
        Throws<InvalidDataException>(() => store.Record(intent));
        Equal(bytes.Length + 1L, new FileInfo(file).Length);
    });
    Check("authenticated but unsupported record version is rejected", () =>
    {
        string dir = CaseDirectory();
        using var store = new VaultRetryIntentDiskStore(dir, FixtureKey());
        store.Record(intent);
        string file = RecordFile(dir);
        byte[] bytes = File.ReadAllBytes(file);
        bytes[7] = (byte)'9';
        HMACSHA256.HashData(FixtureKey(), bytes.AsSpan(0, 104), bytes.AsSpan(104));
        File.WriteAllBytes(file, bytes);
        Throws<CryptographicException>(() => store.Read(workspace, 1));
    });
    Check("unpublished partial intent never selects a candidate", () =>
    {
        string dir = CaseDirectory();
        File.WriteAllBytes(Path.Combine(dir, ".partial.eri-stage"), [1, 2, 3]);
        using var store = new VaultRetryIntentDiskStore(dir, FixtureKey());
        Equal<VaultRetryIntent?>(null, store.Read(workspace, 1));
        store.Record(intent);
        Equal(intent, store.Read(workspace, 1));
        Equal(1, Directory.EnumerateFiles(dir, "*.eri-stage").Count());
    });
    Check("authentic old epoch record copied to a later slot is rejected", () =>
    {
        string oldDir = CaseDirectory(), newDir = CaseDirectory();
        using var a = new VaultRetryIntentDiskStore(oldDir, FixtureKey());
        using var b = new VaultRetryIntentDiskStore(newDir, FixtureKey());
        a.Record(intent);
        b.Record(new VaultRetryIntent(next.Stamp, third.Stamp));
        File.Copy(RecordFile(oldDir), RecordFile(newDir), overwrite: true);
        Throws<CryptographicException>(() => b.Read(workspace, 2));
    });
    Check("authentic record copied to another workspace is rejected", () =>
    {
        string oldDir = CaseDirectory(), newDir = CaseDirectory();
        Guid foreign = Guid.NewGuid();
        using var a = new VaultRetryIntentDiskStore(oldDir, FixtureKey());
        using var b = new VaultRetryIntentDiskStore(newDir, FixtureKey());
        a.Record(intent);
        b.Record(new VaultRetryIntent(new(foreign, 1, prior.Stamp.ManifestSha256), new(foreign, 2, next.Stamp.ManifestSha256)));
        File.Copy(RecordFile(oldDir), RecordFile(newDir), overwrite: true);
        Throws<CryptographicException>(() => b.Read(foreign, 1));
    });
    Check("two independent processes reserve only one competing intent", () =>
    {
        string dir = CaseDirectory();
        string signal = Path.Combine(root.FullName, Guid.NewGuid().ToString("N"));
        using var left = StartChild(dir, signal, next.Stamp);
        using var right = StartChild(dir, signal, rival.Stamp);
        try
        {
            Equal("READY", ReadSignal(left));
            Equal("READY", ReadSignal(right));
            File.WriteAllText(signal, "");
            if (!left.WaitForExit(30_000) || !right.WaitForExit(30_000)) throw new TimeoutException();
            if (!new[] { left.ExitCode, right.ExitCode }.Order().SequenceEqual([0, 2]))
                throw new InvalidOperationException($"Unexpected exits {left.ExitCode}, {right.ExitCode}");
            using var store = new VaultRetryIntentDiskStore(dir, FixtureKey());
            Equal(left.ExitCode == 0 ? intent : conflicting, store.Read(workspace, 1));
            Equal(1, Directory.EnumerateFiles(dir).Count());
        }
        finally { Stop(left); Stop(right); }
    });
    Check("verified staged candidate and intent precede CAS and lost reply survives reopen", () =>
    {
        string dir = CaseDirectory();
        var data = new VaultGenerationDiskStore(dir);
        using var store = new VaultRetryIntentDiskStore(dir, FixtureKey());
        data.Stage(prior);
        var anchor = new TestAnchor(prior.Stamp, AdvanceMode.UnknownAfter);
        anchor.BeforeAdvance = () =>
        {
            Equal(intent, store.Read(workspace, 1));
            Equal(next.Stamp, data.Read(next.Stamp)?.Stamp);
        };
        var coordinator = new VaultGenerationRecovery(anchor, data, store);
        Equal(VaultGenerationAdvanceState.Committed, coordinator.StageAndAdvance(prior.Stamp, next));
        using var reopened = new VaultRetryIntentDiskStore(dir, FixtureKey());
        Equal(VaultGenerationAdvanceState.Committed,
            new VaultGenerationRecovery(anchor, new VaultGenerationDiskStore(dir), reopened).Resume(prior.Stamp));
        Equal(1, anchor.Attempts);
    });
    Check("uncertain prior observation pins the retry to the recorded candidate", () =>
    {
        using var fixture = Fixture();
        fixture.Anchor.Mode = AdvanceMode.UnknownBefore;
        Equal(VaultGenerationAdvanceState.PriorObserved, fixture.Coordinator.StageAndAdvance(prior.Stamp, next));
        Throws<CryptographicException>(() => fixture.Coordinator.StageAndAdvance(prior.Stamp, rival));
        Equal<VaultGenerationManifest?>(null, fixture.Data.Read(rival.Stamp));
        fixture.Anchor.Mode = AdvanceMode.Normal;
        Equal(VaultGenerationAdvanceState.Committed, fixture.Coordinator.Resume(prior.Stamp));
        Equal(2, fixture.Anchor.Attempts);
    });
    Check("resume without intent never calls CAS even with a staged candidate", () =>
    {
        using var fixture = Fixture();
        fixture.Data.Stage(next);
        Throws<CryptographicException>(() => fixture.Coordinator.Resume(prior.Stamp));
        Equal(0, fixture.Anchor.Attempts);
    });
    Check("resume with a configured anchor but no journal fails closed", () =>
    {
        using var fixture = Fixture();
        Throws<CryptographicException>(() =>
            new VaultGenerationRecovery(fixture.Anchor, fixture.Data).Resume(prior.Stamp));
        Equal(0, fixture.Anchor.Attempts);
    });
    Check("a corrupt persisted intent blocks both resume and new staging", () =>
    {
        using var fixture = Fixture();
        fixture.Journal.Record(intent);
        File.WriteAllBytes(RecordFile(fixture.Path), [1, 2]);
        Throws<InvalidDataException>(() => fixture.Coordinator.Resume(prior.Stamp));
        Throws<InvalidDataException>(() => fixture.Coordinator.StageAndAdvance(prior.Stamp, next));
        Equal(0, fixture.Anchor.Attempts);
    });
    Check("resume with missing candidate or changed expected digest refuses CAS", () =>
    {
        using var fixture = Fixture();
        fixture.Journal.Record(intent);
        Throws<CryptographicException>(() => fixture.Coordinator.Resume(prior.Stamp));
        var other = new VaultGenerationStamp(workspace, 1, new string('A', 64));
        Throws<CryptographicException>(() => fixture.Coordinator.Resume(other));
        Equal(0, fixture.Anchor.Attempts);
    });
    Check("resume observes a later trusted stamp without replaying CAS", () =>
    {
        using var fixture = Fixture();
        fixture.Journal.Record(intent);
        fixture.Data.Stage(third);
        fixture.Anchor.Current = third.Stamp;
        Equal(VaultGenerationAdvanceState.Superseded, fixture.Coordinator.Resume(prior.Stamp));
        Equal(0, fixture.Anchor.Attempts);
    });
    Check("resume refuses a replaced trusted digest at the expected epoch", () =>
    {
        using var fixture = Fixture();
        fixture.Journal.Record(intent);
        fixture.Data.Stage(next);
        fixture.Anchor.Current = new VaultGenerationStamp(workspace, 1, new string('F', 64));
        Throws<CryptographicException>(() => fixture.Coordinator.Resume(prior.Stamp));
        Equal(0, fixture.Anchor.Attempts);
    });
    Check("failed stage leaves no published intent or CAS attempt", () =>
    {
        using var fixture = Fixture();
        fixture.Data.Stage(next);
        string file = Directory.EnumerateFiles(fixture.Path, "*" + next.Stamp.ManifestSha256 + ".egm").Single();
        File.WriteAllBytes(file, [1, 2, 3]);
        Throws<InvalidDataException>(() => fixture.Coordinator.StageAndAdvance(prior.Stamp, next));
        Equal<VaultRetryIntent?>(null, fixture.Journal.Read(workspace, 1));
        Equal(0, fixture.Anchor.Attempts);
    });
    Check("reservation failure prevents CAS after successful staging", () =>
    {
        using var fixture = Fixture();
        var broken = new RejectingJournal();
        var coordinator = new VaultGenerationRecovery(fixture.Anchor, fixture.Data, broken);
        Throws<IOException>(() => coordinator.StageAndAdvance(prior.Stamp, next));
        Equal(next.Stamp, fixture.Data.Read(next.Stamp)?.Stamp);
        Equal(0, fixture.Anchor.Attempts);
    });
    Check("reservation without verified readback prevents CAS", () =>
    {
        using var fixture = Fixture();
        var coordinator = new VaultGenerationRecovery(fixture.Anchor, fixture.Data, new DroppingJournal());
        Throws<CryptographicException>(() => coordinator.StageAndAdvance(prior.Stamp, next));
        Equal(0, fixture.Anchor.Attempts);
    });
    Check("RISK deleted local intent can permit an explicit fresh start", () =>
    {
        using var fixture = Fixture();
        fixture.Anchor.Mode = AdvanceMode.UnknownBefore;
        Equal(VaultGenerationAdvanceState.PriorObserved, fixture.Coordinator.StageAndAdvance(prior.Stamp, next));
        File.Delete(RecordFile(fixture.Path));
        Throws<CryptographicException>(() => fixture.Coordinator.Resume(prior.Stamp));
        fixture.Anchor.Mode = AdvanceMode.Normal;
        Equal(VaultGenerationAdvanceState.Committed, fixture.Coordinator.StageAndAdvance(prior.Stamp, rival));
        Equal(rival.Stamp, fixture.Anchor.Current);
    });

    if (OperatingSystem.IsWindows())
    {
        Check("file-bound key is delivered only after three stable anchor reads", () =>
        {
            using var fixture = Fixture();
            using var secret = Gate(fixture).UnlockFileBound(vaultPath, workspace, factor.Id, factor.Kind, Password);
            Equal(factor.Id, secret.Id);
            Equal(3, fixture.Anchor.Reads);
        });
        Check("anchor advances between grant selection and key delivery", () =>
        {
            using var fixture = Fixture();
            fixture.Data.Stage(next);
            fixture.Anchor.BeforeRead = count => { if (count == 3) fixture.Anchor.Current = next.Stamp; };
            Throws<CryptographicException>(() =>
            { using var _ = Gate(fixture).UnlockFileBound(vaultPath, workspace, factor.Id, factor.Kind, Password); });
            Equal(3, fixture.Anchor.Reads);
        });
        Check("same epoch with different digest cannot release a key", () =>
        {
            using var fixture = Fixture();
            fixture.Anchor.BeforeRead = count =>
            {
                if (count == 3) fixture.Anchor.Current = new VaultGenerationStamp(workspace, 1, new string('B', 64));
            };
            Throws<CryptographicException>(() =>
            { using var _ = Gate(fixture).UnlockFileBound(vaultPath, workspace, factor.Id, factor.Kind, Password); });
            Equal(3, fixture.Anchor.Reads);
        });
        Check("anchor failure at key delivery refuses the unlock", () =>
        {
            using var fixture = Fixture();
            fixture.Anchor.BeforeRead = count => { if (count == 3) throw new IOException("fixture"); };
            Throws<IOException>(() =>
            { using var _ = Gate(fixture).UnlockFileBound(vaultPath, workspace, factor.Id, factor.Kind, Password); });
            Equal(3, fixture.Anchor.Reads);
        });
        Check("wrong credential cannot reach the final anchor check", () =>
        {
            using var fixture = Fixture();
            Throws<CryptographicException>(() =>
            { using var _ = Gate(fixture).UnlockFileBound(vaultPath, workspace, factor.Id, factor.Kind, Recovery); });
            Equal(2, fixture.Anchor.Reads);
        });
    }
    else Console.WriteLine("NOT RUN Windows file-bound key delivery checks");
}
finally { root.Delete(recursive: true); }

Console.WriteLine($"{passed}/{passed + failed} Tur23 retry and key delivery checks passed.");
return failed == 0 ? 0 : 1;

VaultGenerationManifest Generation(long epoch, string path)
{
    var grant = new VaultAnchorGrant(workspace, factor.Id, factor.Kind,
        VaultSnapshotPin.CaptureAuthenticated(vault, factor.Id, factor.Kind, Password, epoch), epoch, path)
        { BoundVaultFileIdentity = fileId };
    return VaultGenerationManifest.Create(workspace, epoch, [grant]);
}

FixtureContext Fixture() => new(CaseDirectory(), prior);
TrustedVaultOpenGate Gate(FixtureContext fixture) => new(fixture.Coordinator);
string CaseDirectory() => Directory.CreateDirectory(Path.Combine(root.FullName, Guid.NewGuid().ToString("N"))).FullName;

Process StartChild(string dir, string signal, VaultGenerationStamp candidate)
{
    string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path");
    var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true };
    if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (string arg in new[] { "--record", dir, signal, workspace.ToString(),
        prior.Stamp.Epoch.ToString(CultureInfo.InvariantCulture), prior.Stamp.ManifestSha256, candidate.ManifestSha256 })
        info.ArgumentList.Add(arg);
    return Process.Start(info) ?? throw new IOException("Could not start fixture process");
}

static string? ReadSignal(Process process) =>
    process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
static void Stop(Process process)
{
    if (!process.HasExited) process.Kill();
    process.WaitForExit(30_000);
}

static string RecordFile(string directory) => Directory.EnumerateFiles(directory, "*.eri").Single();
static byte[] FixtureKey() => Enumerable.Range(1, 32).Select(n => (byte)n).ToArray();
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
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

enum AdvanceMode { Normal, UnknownBefore, UnknownAfter }

sealed class TestAnchor(VaultGenerationStamp current, AdvanceMode mode = AdvanceMode.Normal) : IExternalVaultGenerationAnchor
{
    public VaultGenerationStamp Current { get; set; } = current;
    public AdvanceMode Mode { get; set; } = mode;
    public int Attempts { get; private set; }
    public int Reads { get; private set; }
    public Action? BeforeAdvance { get; set; }
    public Action<int>? BeforeRead { get; set; }

    public VaultGenerationStamp? Read(Guid workspaceId)
    {
        Reads++;
        BeforeRead?.Invoke(Reads);
        return Current.WorkspaceId == workspaceId ? Current : null;
    }

    public void CompareAndAdvance(VaultGenerationStamp expected, VaultGenerationStamp next)
    {
        Attempts++;
        BeforeAdvance?.Invoke();
        if (Mode == AdvanceMode.UnknownBefore) throw new VaultGenerationOutcomeUnknownException("before CAS");
        if (Current != expected) return;
        Current = next;
        if (Mode == AdvanceMode.UnknownAfter) throw new VaultGenerationOutcomeUnknownException("after CAS");
    }
}

sealed class FixtureContext : IDisposable
{
    public string Path { get; }
    public VaultGenerationDiskStore Data { get; }
    public VaultRetryIntentDiskStore Journal { get; }
    public TestAnchor Anchor { get; }
    public VaultGenerationRecovery Coordinator { get; }

    public FixtureContext(string path, VaultGenerationManifest prior)
    {
        Path = path;
        Data = new VaultGenerationDiskStore(path);
        Data.Stage(prior);
        Journal = new VaultRetryIntentDiskStore(path, Enumerable.Range(1, 32).Select(n => (byte)n).ToArray());
        Anchor = new TestAnchor(prior.Stamp);
        Coordinator = new VaultGenerationRecovery(Anchor, Data, Journal);
    }

    public void Dispose() => Journal.Dispose();
}
sealed class RejectingJournal : IVaultRetryIntentStore
{
    public VaultRetryIntent? Read(Guid workspaceId, long expectedEpoch) => null;
    public void Record(VaultRetryIntent intent) => throw new IOException("fixture staging failure");
}
sealed class DroppingJournal : IVaultRetryIntentStore
{
    public VaultRetryIntent? Read(Guid workspaceId, long expectedEpoch) => null;
    public void Record(VaultRetryIntent intent) { }
}
