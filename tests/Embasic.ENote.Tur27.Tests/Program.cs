using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private const string Password = "Tur27 original synthetic fixture credential";
    private const string NextPassword = "Tur27 replacement synthetic fixture credential";
    private const string BackupPassword = "Tur27 backup synthetic fixture credential";
    private const string Markdown = "# TUR 27\n\nTamlık ve tekrar başlatma.";
    private static readonly byte[] ProviderKey = SHA256.HashData("public test provider key"u8);
    private static readonly byte[] JournalKey = SHA256.HashData("public test journal key"u8);
    private static readonly byte[] ReaderKey = SHA256.HashData("public test reader key"u8);
    private static readonly byte[] PublisherKey = SHA256.HashData("public test publisher key"u8);
    private static int _passed;
    private static int _failed;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0) return await RunChild(args);
        SnapshotChecks();
        ArchiveChecks();
        await ProcessChecks();
        if (NativePipesAvailable()) await PipeChecks();
        else Console.WriteLine("NOT RUN native IPC on Linux: executor denies Unix socket creation");
        Console.WriteLine($"{_passed}/{_passed + _failed} Tur27 IPC, snapshot publication and root archive checks passed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, Action test) => CheckAsync(name, () =>
    {
        test();
        return Task.CompletedTask;
    }).GetAwaiter().GetResult();

    private static async Task CheckAsync(string name, Func<Task> test)
    {
        try { await test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void Throws<T>(Action test) where T : Exception
    {
        try { test(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static async Task ThrowsAsync<T>(Func<Task> test) where T : Exception
    {
        try { await test(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static Process StartChild(params string[] arguments)
    {
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
        var start = new ProcessStartInfo(processPath) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start child.");
    }

    private static async Task<int> WaitChild(Process child)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try { await child.WaitForExitAsync(timeout.Token); return child.ExitCode; }
        finally { if (!child.HasExited) child.Kill(); }
    }

    private static async Task<int> RunChild(string[] args)
    {
        string path = args[1];
        Guid workspace = Guid.Parse(args[2]), providerId = Guid.Parse(args[3]), rootId = Guid.Parse(args[4]);
        using var provider = OpenProvider(path, providerId);
        using var root = LayerKeyVault.Open(File.ReadAllBytes(Path.Combine(path, "Root.elkey")),
            rootId, LayerKind.Folder, Password);
        if (args[0] == "legacy")
        {
            string read = LayeredEmd3.Open(File.ReadAllBytes(Path.Combine(path, "live", "Direct.emd")), [root], Password);
            if (read != Markdown) return 19;
            var current = provider.ReadTransition(workspace).Current;
            var unregistered = new VaultGenerationStamp(workspace, current.Epoch + 1, new string('C', 64));
            var intent = new VaultRetryIntent(current, unregistered);
            provider.ReserveTransition(intent);
            provider.CommitTransition(intent);
            return 0;
        }

        string mode = args[6];
        IExternalVaultTransitionAnchor anchor = mode is "before" or "after" ? new CrashOnCommit(provider, mode) : provider;
        var store = new VaultGenerationDiskStore(Path.Combine(path, "generation"));
        using var publication = OpenPublication(path, store);
        var recovery = new VaultGenerationRecovery(anchor, store);
        if (args[0] == "resume")
        {
            var stamp = new VaultGenerationStamp(workspace, long.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), args[7]);
            return publication.Resume(recovery, stamp, root) == VaultGenerationAdvanceState.Committed ? 0 : 20;
        }
        using var signer = ECDsa.Create();
        signer.ImportPkcs8PrivateKey(File.ReadAllBytes(Path.Combine(path, mode == "imposter" ? "imposter.key" : "service.key")), out _);
        using var service = new VaultAuthorityPipeService(args[5], workspace, ReaderKey, PublisherKey,
            signer, recovery, publication, root);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await service.ServeOnceAsync(deadline.Token);
        return 0;
    }

    private sealed class CrashOnCommit(VaultTransitionFilePrototype provider, string mode) : IExternalVaultTransitionAnchor
    {
        public VaultTransitionState ReadTransition(Guid workspaceId) => provider.ReadTransition(workspaceId);
        public void ReserveTransition(VaultRetryIntent intent) => provider.ReserveTransition(intent);
        public void CommitTransition(VaultRetryIntent intent)
        {
            if (mode == "after") provider.CommitTransition(intent);
            Environment.Exit(mode == "after" ? 72 : 71);
        }
    }

    private static VaultTransitionFilePrototype OpenProvider(string path, Guid identity) =>
        new(Path.Combine(path, "authority"), ProviderKey, identity);

    private static VaultRekeyPublication OpenPublication(string path, IVaultGenerationStore store) =>
        new(Path.Combine(path, "published"), Path.Combine(path, "journal"), store,
            new VaultRekeyStaging(Path.Combine(path, "staging")), Path.Combine(path, "snapshot"),
            Path.Combine(path, "live"), JournalKey);

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("enote-tur27-");
        public readonly Guid Workspace = Guid.NewGuid();
        public readonly Guid ProviderId = Guid.NewGuid();
        public readonly LayerSecret Root = LayerSecret.Generate(LayerKind.Folder);
        public readonly LayerSecret Child = LayerSecret.Generate(LayerKind.Subfolder);
        public readonly LayerSecret Note = LayerSecret.Generate(LayerKind.NoteAdditional);
        public readonly LayerSecret Fresh;
        public readonly byte[] OriginalVault;
        public readonly byte[] NewVault;
        public readonly byte[] Index;
        public readonly byte[] NewIndex;
        public readonly byte[] Direct;
        public readonly byte[] Inherited;
        public readonly byte[] Explicit;
        public readonly VaultGenerationDiskStore Store;
        public readonly VaultGenerationManifest Original;
        public readonly VaultTransitionFilePrototype Provider;
        public readonly VaultGenerationRecovery Recovery;
        public readonly VaultRekeyStaging Staging;
        public readonly byte[] PublicKey;
        public string Home => _dir.FullName;
        public string Live => Path.Combine(Home, "live");
        public string SnapshotPath => Path.Combine(Home, "snapshot");
        public string Journal => Path.Combine(Home, "journal");
        public string Published => Path.Combine(Home, "published");
        public VaultWorkspaceSnapshot? Snapshot { get; private set; }
        public VaultGenerationManifest? Source { get; private set; }
        public VaultRekeyCommitment? Commitment { get; private set; }
        public VaultGenerationManifest? Candidate { get; private set; }

        public Fixture()
        {
            foreach (string name in new[] { "live/Sub", "authority", "generation", "staging", "journal", "published" })
                Directory.CreateDirectory(Path.Combine(Home, name));
            Fresh = LayerSecret.Regenerate(Root);
            OriginalVault = LayerKeyVault.Create(Root, Password, BackupPassword);
            NewVault = LayerKeyVault.Create(Fresh, NextPassword, BackupPassword);
            using var portable = PortableEmd.Encrypt(Markdown, Password);
            Direct = LayeredEmd3.Seal(portable, [Root]);
            Inherited = LayeredEmd3.Seal(portable, [Root, Child]);
            Explicit = LayeredEmd3.Seal(portable, [Root, Child, Note]);
            File.WriteAllBytes(Path.Combine(Live, "Direct.emd"), Direct);
            File.WriteAllBytes(Path.Combine(Live, "Sub", "Inherited.emd"), Inherited);
            File.WriteAllBytes(Path.Combine(Live, "Sub", "Explicit.emd"), Explicit);
            File.WriteAllText(Path.Combine(Live, "Readme.md"), "# original Markdown");
            WorkspaceLockEntry[] entries =
            [
                new(Root.Id, Guid.Empty, LayerKind.Folder, "Vault"),
                new(Child.Id, Root.Id, LayerKind.Subfolder, "Vault/Sub"),
                new(Note.Id, Child.Id, LayerKind.NoteAdditional, "Vault/Sub/Explicit.emd")
            ];
            Index = WorkspaceLockIndex.Protect(Workspace, Root, entries);
            NewIndex = WorkspaceLockIndex.RekeyRoot(Index, Workspace, Root, Fresh);
            Original = VaultGenerationManifest.Create(Workspace, 1,
            [
                WriteGrant("Root.elkey", Root, OriginalVault, 1),
                WriteGrant("Child.elkey", Child, LayerKeyVault.Create(Child, Password, BackupPassword), 2),
                WriteGrant("Note.elkey", Note, LayerKeyVault.Create(Note, Password, BackupPassword), 3)
            ]);
            Store = new VaultGenerationDiskStore(Path.Combine(Home, "generation"));
            Store.Stage(Original);
            Provider = OpenProvider(Home, ProviderId);
            Provider.Provision(Original.Stamp);
            Recovery = new VaultGenerationRecovery(Provider, Store);
            Staging = new VaultRekeyStaging(Path.Combine(Home, "staging"));
            using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            PublicKey = signer.ExportSubjectPublicKeyInfo();
            File.WriteAllBytes(Path.Combine(Home, "service.key"), signer.ExportPkcs8PrivateKey());
        }

        private VaultAnchorGrant WriteGrant(string name, LayerSecret factor, byte[] envelope, ulong fixtureId)
        {
            string path = Path.Combine(Home, name);
            File.WriteAllBytes(path, envelope);
            var identity = new WindowsVaultFileIdentity(100, fixtureId, 1);
            if (OperatingSystem.IsWindows())
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                identity = WindowsVaultFileIdentity.Read(handle);
            }
            return new VaultAnchorGrant(Workspace, factor.Id, factor.Kind,
                VaultSnapshotPin.CaptureAuthenticated(envelope, factor.Id, factor.Kind, Password, 1), 1, path)
                { BoundVaultFileIdentity = identity };
        }

        public VaultWorkspaceSnapshot Capture() =>
            Snapshot = VaultWorkspaceSnapshot.Capture(Live, SnapshotPath, Original, Root, Index);

        public VaultGenerationManifest Enroll()
        {
            var snapshot = Snapshot ?? Capture();
            Equal(VaultGenerationAdvanceState.Committed, Recovery.EnrollCatalog(Original.Stamp, snapshot, Root, Live));
            return Source = Recovery.ReadCommitted(Workspace);
        }

        public VaultRekeyCommitment Stage(byte[]? replacementVault = null)
        {
            var source = Source ?? Enroll();
            var snapshot = Snapshot!;
            var inventory = snapshot.BuildInventory(source, Root, [Root.Id]);
            var target = new Dictionary<string, byte[]>
            {
                ["index"] = NewIndex,
                ["vault:" + Root.Id.ToString("N")] = replacementVault ?? NewVault,
                ["note:Vault/Direct.emd"] = Rekey(Direct, [Root], [Fresh]),
                ["note:Vault/Sub/Inherited.emd"] = Rekey(Inherited, [Root, Child], [Fresh, Child]),
                ["note:Vault/Sub/Explicit.emd"] = Rekey(Explicit, [Root, Child, Note], [Fresh, Child, Note])
            };
            Commitment = new VaultRekeyCommitment(inventory, target.ToDictionary(x => x.Key, x => Digest(x.Value)));
            Staging.Prepare(inventory, Commitment.Targets, snapshot.OpenSource, item => new MemoryStream(target[item.Id]));
            return Commitment;
        }

        public VaultGenerationManifest Prepare()
        {
            var commitment = Commitment ?? Stage();
            using var publication = OpenPublication(Home, Store);
            return Candidate = publication.PrepareCandidate(commitment, Root, _ => new WindowsVaultFileIdentity(101, 1, 1));
        }

        public Process Host(string pipe, string mode = "normal") =>
            StartChild("serve", Home, Workspace.ToString(), ProviderId.ToString(), Root.Id.ToString(), pipe, mode);

        public Process ResumeChild(VaultGenerationStamp stamp, string mode = "normal") =>
            StartChild("resume", Home, Workspace.ToString(), ProviderId.ToString(), Root.Id.ToString(),
                stamp.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture), mode, stamp.ManifestSha256);

        public Process LegacyClient() => StartChild("legacy", Home, Workspace.ToString(), ProviderId.ToString(), Root.Id.ToString());

        public void Dispose()
        {
            Provider.Dispose();
            Fresh.Dispose(); Note.Dispose(); Child.Dispose(); Root.Dispose();
            _dir.Delete(recursive: true);
        }

        private static byte[] Rekey(byte[] ciphertext, LayerSecret[] current, LayerSecret[] replacement)
        {
            byte[] result = LayeredEmd3.Rekey(ciphertext, current, replacement, NextPassword,
                out byte[] secret, currentPassword: Password);
            CryptographicOperations.ZeroMemory(secret);
            return result;
        }
    }
}
