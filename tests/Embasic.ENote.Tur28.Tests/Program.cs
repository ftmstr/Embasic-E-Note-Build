using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private const string Password = "TUR 28 synthetic original passphrase";
    private const string NewPassword = "TUR 28 synthetic replacement passphrase";
    private const string BackupPassword = "TUR 28 synthetic backup passphrase";
    private const string Text = "# TUR 28\n\nSeçili nesil.";
    private static readonly byte[] ProviderKey = SHA256.HashData("TUR 28 provider fixture"u8);
    private static readonly byte[] JournalKey = SHA256.HashData("TUR 28 journal fixture"u8);
    private static int _total, _passed;

    private static async Task<int> Main(string[] args)
    {
        if (args is ["hold", var dir])
        {
            using var held = Publication(dir).EnterLiveWrite();
            File.WriteAllText(Path.Combine(dir, "held"), "held");
            await Task.Delay(TimeSpan.FromSeconds(35));
            return 0;
        }
        if (args is ["tur29"])
        {
            LineageChecks();
            WitnessChecks();
            Console.WriteLine($"{_passed}/{_total} Tur29 catalog, recovery, cleanup and witness checks passed.");
            return _total == _passed ? 0 : 1;
        }
        if (args is ["tur30"])
        {
            Tur30LiveChecks();
            Tur30RecoveryChecks();
            Console.WriteLine($"{_passed}/{_total} Tur30 live synchronization, inventory and recovery policy checks passed.");
            return _total == _passed ? 0 : 1;
        }
        if (args is ["tur31"])
        {
            Tur31LiveChecks();
            Tur31PolicyChecks();
            Console.WriteLine($"{_passed}/{_total} Tur31 crash, catalog and pin selection checks passed.");
            return _total == _passed ? 0 : 1;
        }
        if (args is ["tur32"])
        {
            Tur32LiveChecks();
            Tur32SelectorChecks();
            Console.WriteLine($"{_passed}/{_total} Tur32 midcopy recovery, catalog backup and selector checks passed.");
            return _total == _passed ? 0 : 1;
        }
        if (args is ["tur33"])
        {
            Tur33LiveChecks();
            Tur33OperatorChecks();
            Console.WriteLine($"{_passed}/{_total} Tur33 selection, stage lineage, backup and operator checks passed.");
            return _total == _passed ? 0 : 1;
        }
        if (args is ["tur33-windows-stage"])
            return OperatingSystem.IsWindows() ? Tur33WindowsStageChecks() : 2;
        if (args is ["tur33-floor-worker", ..]) return Tur33FloorWorker(args);
        if (args.Length > 0 && args[0].StartsWith("tur33-win-", StringComparison.Ordinal))
            return OperatingSystem.IsWindows() ? Tur33WindowsServices(args) : 2;
        if (args is ["tur32-copy-worker", ..]) return RunLiveWorker(args);
        if (args.Length > 0 && args[0].StartsWith("tur32-win-", StringComparison.Ordinal))
            return OperatingSystem.IsWindows() ? WindowsStartupExperiment(args) : 2;
        if (args is ["tur31-live-worker", ..]) return RunLiveWorker(args);
        if (args is ["tur31-windows-live"])
            return OperatingSystem.IsWindows() ? RunWindowsLiveAcceptance() : 2;
        if (args.Length > 0 && args[0].StartsWith("cng-", StringComparison.Ordinal))
            return OperatingSystem.IsWindows() ? await WindowsCngCommand(args) : 2;
        if (args.Length > 0)
            return OperatingSystem.IsWindows() ? await WindowsCommand(args) : 2;
        SelectionChecks();
        RecoveryChecks();
        KeyChecks();
        Console.WriteLine($"{_passed}/{_total} Tur28 selected reads, restart journals, recovery and key custody checks passed.");
        return _total == _passed ? 0 : 1;
    }

    private static void Check(string description, Action test)
    {
        _total++;
        try { test(); _passed++; Console.WriteLine("PASS " + description); }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + description + ": " + error); }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static VaultGenerationDiskStore Store(string home) => new(Path.Combine(home, "generation"));
    private static VaultRekeyStaging Staging(string home) => new(Path.Combine(home, "staging"));
    private static VaultRekeyPublication Publication(string home) => new(Path.Combine(home, "published"),
        Path.Combine(home, "journal"), Store(home), Staging(home), Path.Combine(home, "snapshot"),
        Path.Combine(home, "live"), JournalKey);

    private static Process HoldChild(string home) => StartHarness("hold", home);

    private static Process StartHarness(params string[] args)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in args) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException("Child process not started.");
    }

    private sealed class Fixture : IDisposable
    {
        public string Home { get; }
        private readonly bool _ownsHome;
        public Guid Workspace { get; } = Guid.NewGuid();
        public Guid ProviderId { get; } = Guid.NewGuid();
        public LayerSecret Root { get; } = LayerSecret.Generate(LayerKind.Folder);
        public LayerSecret Child { get; } = LayerSecret.Generate(LayerKind.Subfolder);
        public byte[] Index { get; }
        public byte[] Direct { get; }
        public byte[] Inherited { get; }
        public byte[] RootVault { get; }
        public VaultGenerationManifest Initial { get; }
        public VaultGenerationManifest? Enrolled { get; private set; }
        public VaultWorkspaceSnapshot? Snapshot { get; private set; }
        public VaultTransitionFilePrototype Provider { get; }
        public VaultGenerationRecovery Recovery { get; }
        public VaultRekeyPublication Published { get; }
        public string Journal(VaultGenerationStamp stamp) =>
            Path.Combine(Home, "journal", stamp.WorkspaceId.ToString("N") + "-" + stamp.ManifestSha256 + ".erj");
        public string Live => Path.Combine(Home, "live");

        public Fixture(string? home = null)
        {
            _ownsHome = home is null;
            Home = home ?? Directory.CreateTempSubdirectory("enote28-").FullName;
            foreach (string folder in new[] { "live/Sub", "authority", "generation", "published", "staging", "journal" })
                Directory.CreateDirectory(Path.Combine(Home, folder));
            RootVault = LayerKeyVault.Create(Root, Password, BackupPassword);
            byte[] childVault = LayerKeyVault.Create(Child, Password, BackupPassword);
            using var portable = PortableEmd.Encrypt(Text, Password);
            Direct = LayeredEmd3.Seal(portable, [Root]);
            Inherited = LayeredEmd3.Seal(portable, [Root, Child]);
            File.WriteAllBytes(Path.Combine(Live, "Direct.emd"), Direct);
            File.WriteAllBytes(Path.Combine(Live, "Sub", "Inherited.emd"), Inherited);
            Index = WorkspaceLockIndex.Protect(Workspace, Root,
            [
                new(Root.Id, Guid.Empty, LayerKind.Folder, "Vault"),
                new(Child.Id, Root.Id, LayerKind.Subfolder, "Vault/Sub")
            ]);
            Initial = VaultGenerationManifest.Create(Workspace, 1,
                [Grant("root.elkey", Root, RootVault, 1), Grant("child.elkey", Child, childVault, 2)]);
            var store = Store(Home);
            store.Stage(Initial);
            Provider = new VaultTransitionFilePrototype(Path.Combine(Home, "authority"), ProviderKey, ProviderId);
            Provider.Provision(Initial.Stamp);
            Recovery = new VaultGenerationRecovery(Provider, store);
            Published = Publication(Home);
        }

        private VaultAnchorGrant Grant(string name, LayerSecret secret, byte[] content, ulong slot)
        {
            string path = Path.Combine(Home, name);
            File.WriteAllBytes(path, content);
            var identity = new WindowsVaultFileIdentity(18, slot, 1);
            if (OperatingSystem.IsWindows())
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                identity = WindowsVaultFileIdentity.Read(handle);
            }
            return new VaultAnchorGrant(Workspace, secret.Id, secret.Kind,
                VaultSnapshotPin.CaptureAuthenticated(content, secret.Id, secret.Kind, Password, 1), 1, path)
                { BoundVaultFileIdentity = identity };
        }

        public VaultGenerationManifest Enroll()
        {
            if (Enrolled is not null) return Enrolled;
            Snapshot = VaultWorkspaceSnapshot.Capture(Live, Path.Combine(Home, "snapshot"), Initial, Root, Index);
            Equal(VaultGenerationAdvanceState.Committed, Recovery.EnrollCatalog(Initial.Stamp, Snapshot, Root, Live));
            return Enrolled = Recovery.ReadCommitted(Workspace);
        }

        public (VaultGenerationManifest Manifest, string TargetDigest) Prepare(LayerSecret freshChild)
        {
            var source = Enroll();
            var snapshot = Snapshot!;
            var inventory = snapshot.BuildInventory(source, Root, [Child.Id]);
            byte[] target = LayeredEmd3.Rekey(Inherited, [Root, Child], [Root, freshChild], NewPassword,
                out byte[] recovery, currentPassword: Password);
            CryptographicOperations.ZeroMemory(recovery);
            var objects = new Dictionary<string, byte[]>
            {
                ["vault:" + Child.Id.ToString("N")] = LayerKeyVault.Create(freshChild, NewPassword, BackupPassword),
                ["note:Vault/Sub/Inherited.emd"] = target
            };
            var commitment = new VaultRekeyCommitment(inventory, objects.ToDictionary(x => x.Key, x => Digest(x.Value)));
            Staging(Home).Prepare(inventory, commitment.Targets, snapshot.OpenSource,
                item => new MemoryStream(objects[item.Id]));
            return (Published.PrepareCandidate(commitment, Root, _ => new WindowsVaultFileIdentity(19, 1, 1)), Digest(target));
        }

        public void Dispose()
        {
            Published.Dispose();
            Provider.Dispose();
            Child.Dispose();
            Root.Dispose();
            if (_ownsHome) Directory.Delete(Home, recursive: true);
        }
    }
}
