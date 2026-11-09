using System.Collections.Concurrent;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

const string Password = "Tur21 fixture current";
const string PreviousPassword = "Tur21 fixture previous";
const string RecoveryPassword = "Tur21 fixture recovery";
var workspace = Guid.Parse("9bf60276-8f6a-44ca-ad9f-2d2fa2d68aa2");
using var factor = LayerSecret.Generate(LayerKind.Folder);
using var child = LayerSecret.Generate(LayerKind.Subfolder);
byte[] previous = LayerKeyVault.Create(factor, PreviousPassword, RecoveryPassword);
byte[] current = LayerKeyVault.Rotate(previous, factor.Id, factor.Kind, PreviousPassword, Password, RecoveryPassword);
byte[] childBytes = LayerKeyVault.Create(child, Password, RecoveryPassword);
var temp = Directory.CreateTempSubdirectory("enote-generation-");
string path = Path.Combine(temp.FullName, "Main.elkey");
string childPath = Path.Combine(temp.FullName, "Child.elkey");
File.WriteAllBytes(path, current);
WindowsVaultFileIdentity identity;
if (OperatingSystem.IsWindows())
{
    using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    identity = WindowsVaultFileIdentity.Read(handle);
}
else identity = new WindowsVaultFileIdentity(10, 20, 30);
var first = new VaultAnchorGrant(workspace, factor.Id, factor.Kind,
    VaultSnapshotPin.CaptureAuthenticated(previous, factor.Id, factor.Kind, PreviousPassword, 1), 1, path)
    { BoundVaultFileIdentity = identity };
var second = first with
{
    Pin = VaultSnapshotPin.CaptureAuthenticated(current, factor.Id, factor.Kind, Password, 2),
    MinimumEpoch = 2
};
var childGrant = new VaultAnchorGrant(workspace, child.Id, child.Kind,
    VaultSnapshotPin.CaptureAuthenticated(childBytes, child.Id, child.Kind, Password, 2), 2, childPath)
    { BoundVaultFileIdentity = new WindowsVaultFileIdentity(10, 40, 50) };
var old = Make(first);
var next = Make(second, childGrant);
var rival = Make(second with { BoundVaultPath = Path.Combine(temp.FullName, "Rival.elkey") }, childGrant);

int passed = 0, failed = 0;
try
{
    Check("canonical stamp is independent of input grant order", () =>
        Equal(next.Stamp, Make(childGrant, second).Stamp));
    Check("normalized equivalent paths produce the same stamp", () =>
        Equal(next.Stamp, Make(second with { BoundVaultPath = Path.Combine(temp.FullName, ".", "Main.elkey") }, childGrant).Stamp));
    Check("different enrolled path changes digest", () => Different(next.Stamp, rival.Stamp));
    Check("different Windows file identity changes digest", () =>
        Different(next.Stamp, Make(second with { BoundVaultFileIdentity = new WindowsVaultFileIdentity(10, 20, 31) }, childGrant).Stamp));
    Check("same-epoch forged ELV1 snapshot pin changes digest", () =>
    {
        var forged = second with
        {
            Pin = VaultSnapshotPin.CaptureAuthenticated(previous, factor.Id, factor.Kind, PreviousPassword, 2)
        };
        Different(next.Stamp, Make(forged, childGrant).Stamp);
    });
    Check("manifest refuses empty or mixed workspaces", () =>
    {
        Throws<ArgumentException>(() => VaultGenerationManifest.Create(workspace, 2, []));
        Throws<ArgumentException>(() => Make(second, childGrant with { WorkspaceId = Guid.NewGuid() }));
    });
    Check("manifest refuses duplicate factor enrollment", () =>
        Throws<ArgumentException>(() => Make(second, second)));
    Check("manifest refuses inconsistent pin or generation epoch", () =>
    {
        Throws<ArgumentException>(() => Make(second, first));
        Throws<ArgumentException>(() => Make(second with { MinimumEpoch = 3 }));
        Throws<ArgumentException>(() => Make(second with { Pin = childGrant.Pin }));
    });
    Check("manifest refuses relative path or missing file identity", () =>
    {
        Throws<ArgumentException>(() => Make(second with { BoundVaultPath = "relative.elkey" }));
        Throws<ArgumentException>(() => Make(second with { BoundVaultFileIdentity = null }));
    });
    Check("manifest refuses multiple factors sharing one path or Windows file object", () =>
    {
        Throws<ArgumentException>(() => Make(second, childGrant with { BoundVaultPath = second.BoundVaultPath }));
        Throws<ArgumentException>(() => Make(second, childGrant with { BoundVaultFileIdentity = second.BoundVaultFileIdentity }));
    });
    Check("manifest snapshot does not use a caller-mutated grant array", () =>
    {
        VaultAnchorGrant[] grants = [second, childGrant];
        var m = Make(grants);
        grants[0] = first;
        Equal(next.Stamp, m.Stamp);
        Equal(second, m.Find(factor.Id, factor.Kind));
    });
    Check("trusted stamp validates and canonicalizes digest text", () =>
    {
        Equal(next.Stamp, new VaultGenerationStamp(workspace, 2, next.Stamp.ManifestSha256.ToLowerInvariant()));
        Throws<ArgumentException>(() => new VaultGenerationStamp(workspace, 2, new string('z', 64)));
        Throws<ArgumentException>(() => new VaultGenerationStamp(workspace, 2, "abc"));
    });
    Check("exact committed generation loads and missing grant does not substitute a factor", () =>
    {
        var (state, _, _) = Fixture(next);
        Equal(next.Stamp, state.ReadCommitted(workspace).Stamp);
        Equal(false, state.TryRead(workspace, factor.Id, child.Kind, out _));
        Equal(true, state.TryRead(workspace, factor.Id, factor.Kind, out var found));
        Equal(second, found);
    });
    Check("missing or cross-workspace anchor fails closed", () =>
    {
        var (state, anchor, _) = Fixture(next);
        anchor.Force(null);
        Throws<CryptographicException>(() => state.ReadCommitted(workspace));
        anchor.Force(new VaultGenerationStamp(Guid.NewGuid(), 2, next.Stamp.ManifestSha256));
        Throws<CryptographicException>(() => state.ReadCommitted(workspace));
    });
    Check("selected manifest missing or forged at the same epoch fails closed", () =>
    {
        var (state, _, store) = Fixture(next);
        store.ReadOverride = _ => null;
        Throws<CryptographicException>(() => state.ReadCommitted(workspace));
        store.ReadOverride = _ => rival;
        Throws<CryptographicException>(() => state.ReadCommitted(workspace));
        store.ReadOverride = _ => old;
        Throws<CryptographicException>(() => state.ReadCommitted(workspace));
    });
    Check("anchor changed while reading committed manifest requires a fresh unlock", () =>
    {
        var (state, anchor, store) = Fixture(old);
        store.Stage(next);
        store.ReadOverride = stamp =>
        {
            if (stamp != old.Stamp) return next;
            anchor.Force(next.Stamp);
            return old;
        };
        Throws<CryptographicException>(() => state.ReadCommitted(workspace));
        Equal(next.Stamp, state.ReadCommitted(workspace).Stamp);
    });
    Check("before commit, staged manifest does not supersede the trusted prior generation", () =>
    {
        var (state, _, store) = Fixture(old);
        store.Stage(next);
        Equal(old.Stamp, state.ReadCommitted(workspace).Stamp);
    });
    Check("verified stage is visible before CAS and after reopening coordinator", () =>
    {
        var (state, anchor, store) = Fixture(old);
        anchor.BeforeAdvance = () => Equal(next.Stamp, store.Read(next.Stamp)?.Stamp);
        Equal(VaultGenerationAdvanceState.Committed, state.StageAndAdvance(old.Stamp, next));
        Equal(next.Stamp, new VaultGenerationRecovery(anchor, store).ReadCommitted(workspace).Stamp);
    });
    Check("interruption after writing stage never invokes CAS", () =>
    {
        var (state, anchor, store) = Fixture(old);
        store.AfterStage = () => throw new IOException("interrupted after stage");
        Throws<IOException>(() => state.StageAndAdvance(old.Stamp, next));
        Equal(0, anchor.Attempts);
        Equal(old.Stamp, state.ReadCommitted(workspace).Stamp);
        Equal(next.Stamp, store.Read(next.Stamp)?.Stamp);
    });
    Check("missing or corrupt staging readback never invokes CAS", () =>
    {
        var (state, anchor, store) = Fixture(old);
        store.ReadOverride = stamp => stamp == old.Stamp ? old : rival;
        Throws<CryptographicException>(() => state.StageAndAdvance(old.Stamp, next));
        Equal(0, anchor.Attempts);
    });
    Check("ambiguous reply before commit observes prior and permits an exact retry", () =>
    {
        var (state, anchor, _) = Fixture(old);
        anchor.Outcome = AdvanceOutcome.UnknownBefore;
        Equal(VaultGenerationAdvanceState.PriorObserved, state.StageAndAdvance(old.Stamp, next));
        Equal(old.Stamp, state.ReadCommitted(workspace).Stamp);
        anchor.Outcome = AdvanceOutcome.Normal;
        Equal(VaultGenerationAdvanceState.Committed, state.StageAndAdvance(old.Stamp, next));
    });
    Check("ambiguous reply after commit resolves by readback without a second CAS", () =>
    {
        var (state, anchor, store) = Fixture(old);
        anchor.Outcome = AdvanceOutcome.UnknownAfter;
        Equal(VaultGenerationAdvanceState.Committed, state.StageAndAdvance(old.Stamp, next));
        Equal(VaultGenerationAdvanceState.Committed, new VaultGenerationRecovery(anchor, store).StageAndAdvance(old.Stamp, next));
        Equal(1, anchor.Attempts);
    });
    Check("late CAS after prior observation is recognized by an exact retry", () =>
    {
        var (state, anchor, store) = Fixture(old);
        anchor.Outcome = AdvanceOutcome.UnknownBefore;
        Equal(VaultGenerationAdvanceState.PriorObserved, state.StageAndAdvance(old.Stamp, next));
        anchor.Outcome = AdvanceOutcome.Normal;
        anchor.CompareAndAdvance(old.Stamp, next.Stamp);
        Equal(VaultGenerationAdvanceState.Committed, new VaultGenerationRecovery(anchor, store).StageAndAdvance(old.Stamp, next));
        Equal(2, anchor.Attempts);
        Equal(1, anchor.Commits);
    });
    Check("committed manifest lost after CAS blocks and leaves anchor untouched", () =>
    {
        var (state, anchor, store) = Fixture(old);
        anchor.AfterCommit = () => store.ReadOverride = stamp => stamp == old.Stamp ? old : null;
        Throws<CryptographicException>(() => state.StageAndAdvance(old.Stamp, next));
        Equal(next.Stamp, anchor.Read(workspace));
        Throws<CryptographicException>(() => state.ReadCommitted(workspace));
    });
    Check("readback failure after CAS never attempts automatic rollback", () =>
    {
        var (state, anchor, _) = Fixture(old);
        anchor.AfterCommit = () => anchor.ReadError = true;
        Throws<IOException>(() => state.StageAndAdvance(old.Stamp, next));
        Equal(1, anchor.Attempts);
        anchor.ReadError = false;
        Equal(next.Stamp, state.ReadCommitted(workspace).Stamp);
    });
    Check("epoch jumps and cross-workspace updates are rejected before CAS", () =>
    {
        var (state, anchor, _) = Fixture(old);
        var foreign = new VaultGenerationStamp(Guid.NewGuid(), 1, old.Stamp.ManifestSha256);
        Throws<ArgumentException>(() => state.StageAndAdvance(old.Stamp, old));
        Throws<ArgumentException>(() => state.StageAndAdvance(foreign, next));
        Equal(0, anchor.Attempts);
    });
    Check("observed lower epoch is refused before staging", () =>
    {
        var (state, anchor, store) = Fixture(old);
        var third = Make(second with
        {
            MinimumEpoch = 3,
            Pin = VaultSnapshotPin.CaptureAuthenticated(current, factor.Id, factor.Kind, Password, 3)
        });
        Throws<CryptographicException>(() => state.StageAndAdvance(next.Stamp, third));
        Equal(0, anchor.Attempts);
        Equal<VaultGenerationManifest?>(null, store.Read(third.Stamp));
    });
    Check("next generation cannot silently drop a committed factor", () =>
    {
        var (state, anchor, _) = Fixture(next);
        var third = Make(second with
        {
            MinimumEpoch = 3,
            Pin = VaultSnapshotPin.CaptureAuthenticated(current, factor.Id, factor.Kind, Password, 3)
        });
        Throws<ArgumentException>(() => state.StageAndAdvance(next.Stamp, third));
        Equal(0, anchor.Attempts);
        Equal(next.Stamp, state.ReadCommitted(workspace).Stamp);
    });
    Check("deterministic concurrent CAS chooses one exact same-epoch digest", () =>
    {
        var (state, anchor, store) = Fixture(old);
        using var barrier = new Barrier(2);
        anchor.BeforeAdvance = () =>
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
        };
        var left = Task.Run(() => state.StageAndAdvance(old.Stamp, next));
        var right = Task.Run(() => new VaultGenerationRecovery(anchor, store).StageAndAdvance(old.Stamp, rival));
        Task.WaitAll(left, right);
        var selected = state.ReadCommitted(workspace);
        Equal(2, anchor.Attempts);
        Equal(1, anchor.Commits);
        Equal(selected.Stamp == next.Stamp ? VaultGenerationAdvanceState.Committed : VaultGenerationAdvanceState.Superseded, left.Result);
        Equal(selected.Stamp == rival.Stamp ? VaultGenerationAdvanceState.Committed : VaultGenerationAdvanceState.Superseded, right.Result);
    });
    Check("already superseded writer does not stage or retry CAS", () =>
    {
        var (state, anchor, store) = Fixture(rival);
        Equal(VaultGenerationAdvanceState.Superseded, state.StageAndAdvance(old.Stamp, next));
        Equal(0, anchor.Attempts);
        Equal<VaultGenerationManifest?>(null, store.Read(next.Stamp));
    });
    Check("committed grant rejects old ciphertext written in place", () =>
    {
        var (state, _, _) = Fixture(next);
        var gate = new TrustedVaultOpenGate(state);
        using (var unlocked = Open(gate, Password)) Equal(factor.Id, unlocked.Id);
        try
        {
            File.WriteAllBytes(path, previous);
            Throws<CryptographicException>(() => { using var _ = Open(gate, PreviousPassword); });
        }
        finally { File.WriteAllBytes(path, current); }
    });
    Check("RISK rolled-back external anchor and local manifest still open historical ciphertext", () =>
    {
        var (state, anchor, store) = Fixture(next);
        store.Stage(old);
        anchor.Force(old.Stamp);
        try
        {
            File.WriteAllBytes(path, previous);
            using var unlocked = Open(new TrustedVaultOpenGate(state), PreviousPassword);
            Equal(factor.Id, unlocked.Id);
        }
        finally { File.WriteAllBytes(path, current); }
    });
}
finally { temp.Delete(recursive: true); }

Console.WriteLine($"{passed}/{passed + failed} Tur21 generation recovery checks passed.");
return failed == 0 ? 0 : 1;

VaultGenerationManifest Make(params VaultAnchorGrant[] grants) =>
    VaultGenerationManifest.Create(workspace, grants[0].MinimumEpoch, grants);

LayerSecret Open(TrustedVaultOpenGate gate, string credential) =>
    OperatingSystem.IsWindows()
        ? gate.UnlockFileBound(path, workspace, factor.Id, factor.Kind, credential)
        : gate.Unlock(path, workspace, factor.Id, factor.Kind, credential);

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
static void Different(VaultGenerationStamp left, VaultGenerationStamp right)
{
    if (left.ManifestSha256 == right.ManifestSha256)
        throw new InvalidOperationException("Different inputs produced the same commitment.");
}
static void Throws<T>(Action test) where T : Exception
{
    try { test(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}
static (VaultGenerationRecovery, TestAnchor, TestStore) Fixture(VaultGenerationManifest manifest)
{
    var anchor = new TestAnchor(manifest.Stamp);
    var store = new TestStore();
    store.Stage(manifest);
    return (new VaultGenerationRecovery(anchor, store), anchor, store);
}

enum AdvanceOutcome { Normal, UnknownBefore, UnknownAfter }

sealed class TestAnchor(VaultGenerationStamp initial) : IExternalVaultGenerationAnchor
{
    private readonly object _lock = new();
    private VaultGenerationStamp? _current = initial;
    public AdvanceOutcome Outcome { get; set; }
    public Action? BeforeAdvance { get; set; }
    public Action? AfterCommit { get; set; }
    public bool ReadError { get; set; }
    public int Attempts { get; private set; }
    public int Commits { get; private set; }

    public VaultGenerationStamp? Read(Guid workspaceId)
    {
        lock (_lock)
        {
            if (ReadError) throw new IOException("independent read unavailable");
            return _current;
        }
    }

    public void CompareAndAdvance(VaultGenerationStamp expected, VaultGenerationStamp next)
    {
        BeforeAdvance?.Invoke();
        lock (_lock)
        {
            Attempts++;
            if (Outcome == AdvanceOutcome.UnknownBefore) throw new VaultGenerationOutcomeUnknownException("before CAS");
            if (_current != expected) return;
            _current = next;
            Commits++;
            AfterCommit?.Invoke();
            if (Outcome == AdvanceOutcome.UnknownAfter) throw new VaultGenerationOutcomeUnknownException("after CAS");
        }
    }

    public void Force(VaultGenerationStamp? stamp)
    {
        lock (_lock) _current = stamp;
    }
}

sealed class TestStore : IVaultGenerationStore
{
    private readonly ConcurrentDictionary<VaultGenerationStamp, VaultGenerationManifest> _items = new();
    public Func<VaultGenerationStamp, VaultGenerationManifest?>? ReadOverride { get; set; }
    public Action? AfterStage { get; set; }

    public VaultGenerationManifest? Read(VaultGenerationStamp stamp) =>
        ReadOverride is { } read ? read(stamp) : _items.GetValueOrDefault(stamp);

    public void Stage(VaultGenerationManifest manifest)
    {
        _items.TryAdd(manifest.Stamp, manifest);
        AfterStage?.Invoke();
    }
}
