using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

const string Password = "Tur24 original fixture credential";
const string NextPassword = "Tur24 next fixture credential";
const string BackupPassword = "Tur24 separate backup credential";
const string Markdown = "# Kasa\n\nGüncel belge.";
Guid workspace = Guid.Parse("b91fe831-6494-478b-ab28-1c73d95ee7a6");
using var root = LayerSecret.Generate(LayerKind.Folder);
using var child = LayerSecret.Generate(LayerKind.Subfolder);
var dir = Directory.CreateTempSubdirectory("enote-tur24-");
string vaultPath = Path.Combine(dir.FullName, "Root.elkey");
byte[] vault = LayerKeyVault.Create(root, Password, BackupPassword);
File.WriteAllBytes(vaultPath, vault);
WindowsVaultFileIdentity identity;
if (OperatingSystem.IsWindows())
{
    using var handle = File.OpenHandle(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    identity = WindowsVaultFileIdentity.Read(handle);
}
else identity = new WindowsVaultFileIdentity(9, 8, 7);
VaultGenerationManifest previous = Manifest(1, vaultPath);
VaultGenerationManifest next = Manifest(2, vaultPath);
VaultGenerationManifest competing = Manifest(2, Path.Combine(dir.FullName, "Competing.elkey"));
var intent = new VaultRetryIntent(previous.Stamp, next.Stamp);
var rival = new VaultRetryIntent(previous.Stamp, competing.Stamp);
WorkspaceLockEntry[] entries = [new(root.Id, Guid.Empty, LayerKind.Folder, "Vault"),
    new(child.Id, root.Id, LayerKind.Subfolder, "Vault/Child")];
byte[] index = WorkspaceLockIndex.Protect(workspace, root, entries);
var packet = VaultRootRecovery.Prepare(previous, root.Id, vault, Password, index, BackupPassword);
using var document = PortableEmd.Encrypt(Markdown, Password, createRecoveryKey: true);
byte[] original = LayeredEmd3.Seal(document, [root, child]);
int passed = 0, failed = 0;
try
{
    Check("transition provider defaults reject legacy unbound CAS", () =>
    {
        IExternalVaultGenerationAnchor anchor = new DefaultTransitionAnchor(previous.Stamp);
        Equal(previous.Stamp, anchor.Read(workspace));
        Throws<NotSupportedException>(() => anchor.CompareAndAdvance(previous.Stamp, next.Stamp));
    });
    Check("bound path reserves externally before its exact commit", () =>
    {
        using var f = Fixture();
        f.Anchor.BeforeCommit = () =>
        {
            Equal(intent, f.Anchor.Snapshot.Pending);
            Equal(intent, f.Journal.Read(workspace, 1));
            Equal(next.Stamp, f.Data.Read(next.Stamp)?.Stamp);
        };
        Equal(VaultGenerationAdvanceState.Committed, f.Recovery.StageAndAdvance(previous.Stamp, next));
        Equal(intent, f.Anchor.Snapshot.LastCommitted);
        Equal<VaultRetryIntent?>(null, f.Anchor.Snapshot.Pending);
        Equal(0, f.Anchor.LegacyCalls);
    });
    Check("deleted local intent cannot start a different independently reserved candidate", () =>
    {
        using var f = Fixture();
        f.Anchor.CommitMode = Fault.Before;
        Equal(VaultGenerationAdvanceState.PriorObserved, f.Recovery.StageAndAdvance(previous.Stamp, next));
        File.Delete(IntentFile(f.Path));
        Throws<CryptographicException>(() => f.Recovery.StageAndAdvance(previous.Stamp, competing));
        Equal<VaultGenerationManifest?>(null, f.Data.Read(competing.Stamp));
        Equal(1, f.Anchor.CommitCalls);
        f.Anchor.CommitMode = Fault.None;
        Equal(VaultGenerationAdvanceState.Committed, f.Recovery.Resume(previous.Stamp));
        Equal(intent, f.Journal.Read(workspace, 1));
    });
    Check("independent intent can resume without any local journal configured", () =>
    {
        using var f = Fixture();
        f.Anchor.CommitMode = Fault.Before;
        var noJournal = new VaultGenerationRecovery(f.Anchor, f.Data);
        Equal(VaultGenerationAdvanceState.PriorObserved, noJournal.StageAndAdvance(previous.Stamp, next));
        Equal<VaultRetryIntent?>(null, f.Journal.Read(workspace, 1));
        f.Anchor.CommitMode = Fault.None;
        Equal(VaultGenerationAdvanceState.Committed, noJournal.Resume(previous.Stamp));
        Equal(intent, f.Anchor.Snapshot.LastCommitted);
    });
    Check("authentic same-slot replay cannot redirect the independent reservation", () =>
    {
        using var f = Fixture();
        f.Anchor.CommitMode = Fault.Before;
        f.Recovery.StageAndAdvance(previous.Stamp, next);
        using var alternate = Fixture();
        alternate.Journal.Record(rival);
        File.Copy(IntentFile(alternate.Path), IntentFile(f.Path), overwrite: true);
        Throws<CryptographicException>(() => f.Recovery.Resume(previous.Stamp));
        Throws<CryptographicException>(() => f.Recovery.StageAndAdvance(previous.Stamp, competing));
        Equal(intent, f.Anchor.Snapshot.Pending);
        Equal(1, f.Anchor.CommitCalls);
    });
    Check("lost commit reply can resume from independent receipt after local deletion", () =>
    {
        using var f = Fixture();
        f.Anchor.CommitMode = Fault.After;
        Equal(VaultGenerationAdvanceState.Committed, f.Recovery.StageAndAdvance(previous.Stamp, next));
        File.Delete(IntentFile(f.Path));
        Equal(VaultGenerationAdvanceState.Committed, f.Recovery.Resume(previous.Stamp));
        Equal(1, f.Anchor.CommitCalls);
    });
    Check("lost reservation reply after write is recovered by exact independent readback", () =>
    {
        using var f = Fixture();
        f.Anchor.ReserveMode = Fault.After;
        Equal(VaultGenerationAdvanceState.Committed, f.Recovery.StageAndAdvance(previous.Stamp, next));
        Equal(1, f.Anchor.CommitCalls);
    });
    Check("lost reservation reply before write cannot invoke commit", () =>
    {
        using var f = Fixture();
        f.Anchor.ReserveMode = Fault.Before;
        Throws<VaultGenerationOutcomeUnknownException>(() => f.Recovery.StageAndAdvance(previous.Stamp, next));
        Equal(0, f.Anchor.CommitCalls);
        Equal<VaultRetryIntent?>(null, f.Journal.Read(workspace, 1));
        Throws<CryptographicException>(() => f.Recovery.Resume(previous.Stamp));
    });
    Check("corrupt staged candidate cannot resume an independent reservation", () =>
    {
        using var f = Fixture();
        f.Anchor.CommitMode = Fault.Before;
        f.Recovery.StageAndAdvance(previous.Stamp, next);
        string path = Directory.EnumerateFiles(f.Path, "*" + next.Stamp.ManifestSha256 + ".egm").Single();
        File.WriteAllBytes(path, [1, 2, 3]);
        Throws<InvalidDataException>(() => f.Recovery.Resume(previous.Stamp));
        Equal(1, f.Anchor.CommitCalls);
    });
    Check("malformed independent state and missing receipt fail closed", () =>
    {
        using var f = Fixture();
        f.Anchor.Snapshot = new(previous.Stamp, new VaultRetryIntent(next.Stamp, Manifest(3, vaultPath).Stamp));
        Throws<CryptographicException>(() => f.Recovery.ReadCommitted(workspace));
        f.Data.Stage(next);
        f.Anchor.Snapshot = new(next.Stamp);
        Throws<CryptographicException>(() => f.Recovery.StageAndAdvance(previous.Stamp, next));
        Throws<CryptographicException>(() => f.Recovery.Resume(previous.Stamp));
    });
    Check("two concurrent external candidates leave one matching reservation", () =>
    {
        using var f = Fixture();
        f.Anchor.CommitMode = Fault.Before;
        using var barrier = new Barrier(2);
        f.Anchor.BeforeReserve = () => { if (!barrier.SignalAndWait(15_000)) throw new TimeoutException(); };
        bool Try(VaultGenerationManifest candidate)
        {
            try { return f.Recovery.StageAndAdvance(previous.Stamp, candidate) == VaultGenerationAdvanceState.PriorObserved; }
            catch (CryptographicException) { return false; }
        }
        var a = Task.Run(() => Try(next));
        var b = Task.Run(() => Try(competing));
        Task.WaitAll(a, b);
        Equal(true, a.Result ^ b.Result);
        Equal(a.Result ? intent : rival, f.Anchor.Snapshot.Pending);
        Equal(1, f.Anchor.CommitCalls);
    });
    Check("RISK rollback of external state and local files removes the reservation", () =>
    {
        using var f = Fixture();
        f.Anchor.CommitMode = Fault.Before;
        f.Recovery.StageAndAdvance(previous.Stamp, next);
        File.Delete(IntentFile(f.Path));
        f.Anchor.Snapshot = new(previous.Stamp);
        f.Anchor.CommitMode = Fault.None;
        Equal(VaultGenerationAdvanceState.Committed, f.Recovery.StageAndAdvance(previous.Stamp, competing));
    });
    Check("root backup restores an authorized hierarchy without the original vault credential", () =>
    {
        using var f = Fixture();
        using var restored = Restore(f, new RootAuthority(packet.Proof));
        Equal(root.Id, restored.Id);
        Equal(2, WorkspaceLockIndex.Open(index, workspace, restored).Count);
    });
    Check("recovered root alone cannot bypass a dependent child factor", () =>
    {
        using var f = Fixture();
        using var restored = Restore(f, new RootAuthority(packet.Proof));
        Throws<CryptographicException>(() => LayeredEmd3.Open(original, [restored], Password));
        Equal(Markdown, LayeredEmd3.Open(original, [restored, child], Password));
    });
    Check("root recovery requires live authorization and exact generation", () =>
    {
        using var f = Fixture();
        Throws<CryptographicException>(() => Restore(f, new RootAuthority(null)));
        f.Data.Stage(next);
        f.Anchor.Snapshot = new(next.Stamp, LastCommitted: intent);
        Throws<CryptographicException>(() => Restore(f, new RootAuthority(packet.Proof)));
    });
    Check("root backup, index and password tampering are refused", () =>
    {
        using var f = Fixture();
        var recovery = new VaultRootRecovery(f.Recovery, new RootAuthority(packet.Proof));
        byte[] badBackup = packet.Envelope.ToArray();
        badBackup[^1] ^= 1;
        byte[] badIndex = index.ToArray();
        badIndex[^1] ^= 1;
        Throws<CryptographicException>(() => recovery.Restore(workspace, badBackup, index, BackupPassword));
        Throws<CryptographicException>(() => recovery.Restore(workspace, packet.Envelope, badIndex, BackupPassword));
        Throws<CryptographicException>(() => recovery.Restore(workspace, packet.Envelope, index, NextPassword));
    });
    Check("root preparation rejects unregistered or wrong hierarchy factor", () =>
    {
        using var wrong = LayerSecret.Generate(LayerKind.Folder);
        using var sameIdWrongKey = LayerSecret.Regenerate(root);
        byte[] staleVault = LayerKeyVault.Create(sameIdWrongKey, Password, BackupPassword);
        byte[] staleIndex = WorkspaceLockIndex.Protect(workspace, sameIdWrongKey, entries);
        Throws<CryptographicException>(() => VaultRootRecovery.Prepare(previous, wrong.Id, vault, Password, index, BackupPassword));
        Throws<CryptographicException>(() => VaultRootRecovery.Prepare(previous, root.Id, staleVault, Password, staleIndex, BackupPassword));
        Throws<CryptographicException>(() => VaultRootRecovery.Prepare(previous, root.Id, vault, Password, staleIndex, BackupPassword));
    });
    Check("root recovery refuses wrong proof identity and a truncated backup", () =>
    {
        using var f = Fixture();
        Throws<CryptographicException>(() =>
            Restore(f, new RootAuthority(packet.Proof with { RootFactorId = child.Id })));
        var recovery = new VaultRootRecovery(f.Recovery, new RootAuthority(packet.Proof));
        Throws<InvalidDataException>(() => recovery.Restore(workspace, packet.Envelope.AsSpan(0, 12), index, BackupPassword));
    });
    Check("root recovery enrollment can authenticate the separately pinned recovery slot", () =>
    {
        using var f = Fixture();
        var recoveryPacket = VaultRootRecovery.Prepare(previous, root.Id, vault, BackupPassword, index,
            "Tur24 new offline backup passphrase", useRecoverySlot: true);
        var recovery = new VaultRootRecovery(f.Recovery, new RootAuthority(recoveryPacket.Proof));
        using var restored = recovery.Restore(workspace, recoveryPacket.Envelope, index, "Tur24 new offline backup passphrase");
        Equal(root.Id, restored.Id);
    });
    Check("root proof revoked during recovery prevents delivery", () =>
    {
        using var f = Fixture();
        var authority = new RootAuthority(packet.Proof) { RevokeOnSecondRead = true };
        Throws<CryptographicException>(() => Restore(f, authority));
    });
    Check("pending transition blocks root recovery", () =>
    {
        using var f = Fixture();
        f.Anchor.Snapshot = new(previous.Stamp, intent);
        Throws<CryptographicException>(() => Restore(f, new RootAuthority(packet.Proof)));
    });
    Check("a pending reservation appearing during root recovery blocks delivery", () =>
    {
        using var f = Fixture();
        f.Anchor.BeforeRead = n => { if (n == 3) f.Anchor.Snapshot = new(previous.Stamp, intent); };
        Throws<CryptographicException>(() => Restore(f, new RootAuthority(packet.Proof)));
        Equal(3, f.Anchor.Reads);
    });
    Check("fresh factor rebuild preserves hierarchy and document text", () =>
    {
        using var fresh = LayerSecret.Regenerate(root);
        byte[] nextIndex = WorkspaceLockIndex.RekeyRoot(index, workspace, root, fresh);
        byte[] updated = LayeredEmd3.Rekey(original, [root, child], [fresh, child], NextPassword,
            out byte[] recovery, currentPassword: Password);
        try
        {
            Equal(root.Id, fresh.Id);
            Equal(2, WorkspaceLockIndex.Open(nextIndex, workspace, fresh).Count);
            Throws<CryptographicException>(() => WorkspaceLockIndex.Open(nextIndex, workspace, root));
            Equal(Markdown, LayeredEmd3.Open(updated, [fresh, child], NextPassword));
            Equal(Markdown, LayeredEmd3.Open(updated, [fresh, child], recoverySecret: recovery));
            Throws<CryptographicException>(() => LayeredEmd3.Open(updated, [root, child], NextPassword));
            Throws<CryptographicException>(() => LayeredEmd3.Open(updated, [fresh, child], Password));
            byte[] oldRecovery = document.RecoverySecret!;
            try { Throws<CryptographicException>(() => LayeredEmd3.Open(updated, [fresh, child], recoverySecret: oldRecovery)); }
            finally { CryptographicOperations.ZeroMemory(oldRecovery); }
        }
        finally { CryptographicOperations.ZeroMemory(recovery); }
    });
    Check("fresh ELV1 requires new credentials and root identity stays stable", () =>
    {
        using var fresh = LayerSecret.Regenerate(root);
        byte[] freshVault = LayerKeyVault.Create(fresh, NextPassword, BackupPassword);
        using var opened = LayerKeyVault.Open(freshVault, root.Id, LayerKind.Folder, NextPassword);
        Equal(root.Id, opened.Id);
        Throws<CryptographicException>(() =>
        { using var _ = LayerKeyVault.Open(freshVault, root.Id, LayerKind.Folder, Password); });
    });
    Check("rekey rejects unchanged or structurally different factor chains", () =>
    {
        using var fresh = LayerSecret.Regenerate(root);
        Throws<ArgumentException>(() => LayeredEmd3.Rekey(original, [root, child], [root, child], NextPassword, out _, currentPassword: Password));
        Throws<ArgumentException>(() => LayeredEmd3.Rekey(original, [root, child], [fresh], NextPassword, out _, currentPassword: Password));
        Throws<ArgumentException>(() => WorkspaceLockIndex.RekeyRoot(index, workspace, root, root));
    });
    Check("rekey rejects incorrect old credentials without mutating source bytes", () =>
    {
        using var fresh = LayerSecret.Regenerate(root);
        byte[] source = original.ToArray();
        Throws<CryptographicException>(() => LayeredEmd3.Rekey(original, [root, child], [fresh, child], NextPassword, out _, currentPassword: NextPassword));
        Equal(true, original.SequenceEqual(source));
    });
    Check("RISK archived ciphertext remains readable using its old factors", () =>
    {
        using var fresh = LayerSecret.Regenerate(root);
        _ = LayeredEmd3.Rekey(original, [root, child], [fresh, child], NextPassword, out byte[] recovery, currentPassword: Password);
        CryptographicOperations.ZeroMemory(recovery);
        Equal(Markdown, LayeredEmd3.Open(original, [root, child], Password));
    });

    if (OperatingSystem.IsWindows())
    {
        Check("generation key delivery works when trusted transition is idle", () =>
        {
            using var f = Fixture();
            using var secret = Open(f);
            Equal(root.Id, secret.Id);
            Equal(3, f.Anchor.Reads);
        });
        Check("Windows refuses delivery when a transition is pending before decryption", () =>
        {
            using var f = Fixture();
            f.Anchor.Snapshot = new(previous.Stamp, intent);
            Throws<CryptographicException>(() => { using var _ = Open(f); });
            Equal(1, f.Anchor.Reads);
        });
        Check("Windows refuses delivery when pending reservation appears on final read", () =>
        {
            using var f = Fixture();
            f.Anchor.BeforeRead = n => { if (n == 3) f.Anchor.Snapshot = new(previous.Stamp, intent); };
            Throws<CryptographicException>(() => { using var _ = Open(f); });
            Equal(3, f.Anchor.Reads);
        });
    }
    else Console.WriteLine("NOT RUN Windows transition-aware key delivery checks");
}
finally { dir.Delete(recursive: true); }

Console.WriteLine($"{passed}/{passed + failed} Tur24 bound transition, root recovery and rekey checks passed.");
return failed == 0 ? 0 : 1;

VaultGenerationManifest Manifest(long epoch, string path)
{
    var pin = VaultSnapshotPin.CaptureAuthenticated(vault, root.Id, LayerKind.Folder, Password, epoch);
    return VaultGenerationManifest.Create(workspace, epoch,
        [new VaultAnchorGrant(workspace, root.Id, LayerKind.Folder, pin, epoch, path) { BoundVaultFileIdentity = identity }]);
}
FixtureContext Fixture() => new(Directory.CreateDirectory(Path.Combine(dir.FullName, Guid.NewGuid().ToString("N"))).FullName, previous);
LayerSecret Restore(FixtureContext f, RootAuthority authority) =>
    new VaultRootRecovery(f.Recovery, authority).Restore(workspace, packet.Envelope, index, BackupPassword);
LayerSecret Open(FixtureContext f) =>
    new TrustedVaultOpenGate(f.Recovery).UnlockFileBound(vaultPath, workspace, root.Id, LayerKind.Folder, Password);
static string IntentFile(string path) => Directory.EnumerateFiles(path, "*.eri").Single();
void Check(string label, Action check)
{
    try { check(); passed++; Console.WriteLine("PASS " + label); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + label + ": " + ex); }
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
static void Throws<T>(Action check) where T : Exception
{
    try { check(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}

enum Fault { None, Before, After }

sealed class TestAnchor(VaultGenerationStamp initial) : IExternalVaultTransitionAnchor
{
    private readonly object _gate = new();
    private VaultTransitionState _state = new(initial);
    public VaultTransitionState Snapshot
    {
        get { lock (_gate) return _state; }
        set { lock (_gate) _state = value; }
    }
    public Fault ReserveMode { get; set; }
    public Fault CommitMode { get; set; }
    public int LegacyCalls { get; private set; }
    public int CommitCalls { get; private set; }
    public int Reads { get; private set; }
    public Action? BeforeReserve { get; set; }
    public Action? BeforeCommit { get; set; }
    public Action<int>? BeforeRead { get; set; }

    public VaultGenerationStamp? Read(Guid workspaceId) => ReadTransition(workspaceId)?.Current;
    public VaultTransitionState? ReadTransition(Guid workspaceId)
    {
        lock (_gate)
        {
            Reads++;
            BeforeRead?.Invoke(Reads);
            return _state.Current.WorkspaceId == workspaceId ? _state : null;
        }
    }
    public void CompareAndAdvance(VaultGenerationStamp expected, VaultGenerationStamp next)
    {
        LegacyCalls++;
        throw new InvalidOperationException("Unbound CAS prohibited by fixture");
    }
    public void ReserveTransition(VaultRetryIntent intent)
    {
        BeforeReserve?.Invoke();
        lock (_gate)
        {
            if (ReserveMode == Fault.Before) throw new VaultGenerationOutcomeUnknownException("before reservation");
            if (_state.Current != intent.Expected || _state.Pending is not null) return;
            _state = _state with { Pending = intent };
            if (ReserveMode == Fault.After) throw new VaultGenerationOutcomeUnknownException("after reservation");
        }
    }
    public void CommitTransition(VaultRetryIntent intent)
    {
        lock (_gate)
        {
            CommitCalls++;
            BeforeCommit?.Invoke();
            if (CommitMode == Fault.Before) throw new VaultGenerationOutcomeUnknownException("before commit");
            if (_state.Current != intent.Expected || _state.Pending != intent) return;
            _state = new(intent.Candidate, LastCommitted: intent);
            if (CommitMode == Fault.After) throw new VaultGenerationOutcomeUnknownException("after commit");
        }
    }
}

sealed class FixtureContext : IDisposable
{
    public string Path { get; }
    public VaultGenerationDiskStore Data { get; }
    public VaultRetryIntentDiskStore Journal { get; }
    public TestAnchor Anchor { get; }
    public VaultGenerationRecovery Recovery { get; }
    public FixtureContext(string path, VaultGenerationManifest previous)
    {
        Path = path;
        Data = new(path);
        Data.Stage(previous);
        Journal = new(path, Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        Anchor = new(previous.Stamp);
        Recovery = new(Anchor, Data, Journal);
    }
    public void Dispose() => Journal.Dispose();
}

sealed class RootAuthority(VaultRootRecoveryProof? proof) : IExternalVaultRootRecoveryAuthority
{
    private int _reads;
    public bool RevokeOnSecondRead { get; set; }
    public VaultRootRecoveryProof? ReadRootRecovery(Guid workspaceId) =>
        ++_reads >= 2 && RevokeOnSecondRead ? null : proof;
}

sealed class DefaultTransitionAnchor(VaultGenerationStamp current) : IExternalVaultTransitionAnchor
{
    public VaultTransitionState? ReadTransition(Guid workspaceId) => current.WorkspaceId == workspaceId ? new(current) : null;
    public void ReserveTransition(VaultRetryIntent intent) => throw new NotSupportedException();
    public void CommitTransition(VaultRetryIntent intent) => throw new NotSupportedException();
}
