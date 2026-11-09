using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private const string Password = "Tur26 original fixture credential";
    private const string NewPassword = "Tur26 replacement fixture credential";
    private const string BackupPassword = "Tur26 backup fixture credential";
    private const string Markdown = "# TUR 26\n\nKapsam ve bağımlılık deneyi.";
    private static readonly byte[] Key = SHA256.HashData("Tur26 public fixture HMAC key"u8);
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        using var f = new Fixture();
        ProviderChecks(f);
        RootChecks(f);
        RekeyChecks(f);
        if (OperatingSystem.IsWindows()) WindowsChecks(f);
        else Console.WriteLine("NOT RUN Windows scoped operation checks");
        if (OperatingSystem.IsLinux()) LinuxChecks(f);
        else Console.WriteLine("NOT RUN Linux symbolic link checks");
        Console.WriteLine($"{_passed}/{_passed + _failed} Tur26 provider identity, bound rekey and recovery checks passed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
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

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("enote-tur26-");
        public readonly Guid Workspace = Guid.NewGuid();
        public readonly Guid ProviderId = Guid.NewGuid();
        public readonly LayerSecret Root = LayerSecret.Generate(LayerKind.Folder);
        public readonly LayerSecret Child = LayerSecret.Generate(LayerKind.Subfolder);
        public readonly LayerSecret Note = LayerSecret.Generate(LayerKind.NoteAdditional);
        public readonly LayerSecret Fresh;
        public readonly byte[] RootVault;
        public readonly byte[] NewVault;
        private readonly byte[] _childVault;
        private readonly byte[] _noteVault;
        public readonly byte[] Index;
        public readonly byte[] NewIndex;
        public readonly string VaultPath;
        private readonly string _newVaultPath;
        public readonly byte[] Inherited;
        public readonly byte[] Direct;
        public readonly byte[] Explicit;
        public readonly VaultRekeyDocument[] Catalog;
        public readonly VaultGenerationManifest Initial;
        public readonly VaultGenerationManifest PlainInitial;
        public readonly VaultGenerationManifest Next;
        public readonly VaultGenerationManifest PlainNext;
        public readonly VaultRekeyInventory Inventory;
        public readonly VaultRekeyCommitment Commitment;
        public readonly VaultRootRecoveryBackup Backup;
        private readonly Dictionary<string, byte[]> _source = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _targets = new(StringComparer.Ordinal);
        public string VaultId => "vault:" + Root.Id.ToString("N");
        public VaultRetryIntent Intent => new(Initial.Stamp, Next.Stamp);

        public Fixture()
        {
            Fresh = LayerSecret.Regenerate(Root);
            RootVault = LayerKeyVault.Create(Root, Password, BackupPassword);
            NewVault = LayerKeyVault.Create(Fresh, NewPassword, BackupPassword);
            _childVault = LayerKeyVault.Create(Child, Password, BackupPassword);
            _noteVault = LayerKeyVault.Create(Note, Password, BackupPassword);
            VaultPath = Path.Combine(_dir.FullName, "Root.elkey");
            _newVaultPath = Path.Combine(_dir.FullName, "Next.elkey");
            File.WriteAllBytes(VaultPath, RootVault);
            File.WriteAllBytes(_newVaultPath, NewVault);
            WorkspaceLockEntry[] entries =
            [
                new(Root.Id, Guid.Empty, LayerKind.Folder, "Vault"),
                new(Child.Id, Root.Id, LayerKind.Subfolder, "Vault/Sub"),
                new(Note.Id, Child.Id, LayerKind.NoteAdditional, "Vault/Sub/Explicit.emd")
            ];
            Index = WorkspaceLockIndex.Protect(Workspace, Root, entries);
            NewIndex = WorkspaceLockIndex.RekeyRoot(Index, Workspace, Root, Fresh);
            using var portable = PortableEmd.Encrypt(Markdown, Password);
            Inherited = LayeredEmd3.Seal(portable, [Root, Child]);
            Direct = LayeredEmd3.Seal(portable, [Root]);
            Explicit = LayeredEmd3.Seal(portable, [Root, Child, Note]);
            Catalog =
            [
                new("Vault/Sub/Inherited.emd", Digest(Inherited)),
                new("Vault/Direct.emd", Digest(Direct)),
                new("Vault/Sub/Explicit.emd", Digest(Explicit))
            ];
            string catalogDigest = CatalogDigest(Catalog);
            Initial = Manifest(1, new VaultGenerationBindings(catalogDigest));
            PlainInitial = Manifest(1);
            Inventory = VaultRekeyInventory.Create(Initial, Index, Root, [Root.Id], Catalog);
            _source[VaultId] = RootVault;
            _source["index"] = Index;
            _source["note:" + Catalog[0].RelativePath] = Inherited;
            _source["note:" + Catalog[1].RelativePath] = Direct;
            _source["note:" + Catalog[2].RelativePath] = Explicit;
            _targets[VaultId] = NewVault;
            _targets["index"] = NewIndex;
            _targets["note:" + Catalog[0].RelativePath] = Rekey(Inherited, [Root, Child], [Fresh, Child]);
            _targets["note:" + Catalog[1].RelativePath] = Rekey(Direct, [Root], [Fresh]);
            _targets["note:" + Catalog[2].RelativePath] = Rekey(Explicit, [Root, Child, Note], [Fresh, Child, Note]);
            Commitment = new VaultRekeyCommitment(Inventory, _targets.ToDictionary(x => x.Key, x => Digest(x.Value)));
            Next = Manifest(2, Commitment.Destination, fresh: true);
            PlainNext = Manifest(2);
            Backup = VaultRootRecovery.Prepare(Initial, Root.Id, RootVault, Password, Index, BackupPassword);
        }

        public string CatalogDigest(IEnumerable<VaultRekeyDocument> catalog) =>
            VaultRekeyInventory.ComputeCatalogSha256(Workspace, Root.Id, Digest(Index), catalog);

        public VaultGenerationManifest Manifest(long epoch, VaultGenerationBindings? binding = null,
            bool fresh = false, bool omitChild = false, bool redirectChild = false)
        {
            var grants = new List<VaultAnchorGrant>();
            string rootPath = fresh ? _newVaultPath : VaultPath;
            var rootIdentity = new WindowsVaultFileIdentity(5, (ulong)(fresh ? 9 : 2), 3);
            if (OperatingSystem.IsWindows())
            {
                using var handle = File.OpenHandle(rootPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                rootIdentity = WindowsVaultFileIdentity.Read(handle);
            }
            Add(Root, fresh ? NewVault : RootVault, rootPath, rootIdentity, fresh ? NewPassword : Password);
            if (!omitChild)
                Add(Child, _childVault, Path.Combine(_dir.FullName, redirectChild ? "OtherChild.elkey" : "Child.elkey"), new(6, 2, 3), Password);
            Add(Note, _noteVault, Path.Combine(_dir.FullName, "Note.elkey"), new(7, 2, 3), Password);
            return VaultGenerationManifest.Create(Workspace, epoch, grants, binding);

            void Add(LayerSecret factor, byte[] vault, string path, WindowsVaultFileIdentity identity, string credential)
            {
                var pin = VaultSnapshotPin.CaptureAuthenticated(vault, factor.Id, factor.Kind, credential, epoch);
                grants.Add(new VaultAnchorGrant(Workspace, factor.Id, factor.Kind, pin, epoch, path)
                    { BoundVaultFileIdentity = identity });
            }
        }

        public Stream? Source(VaultRekeyArtifact item) =>
            _source.TryGetValue(item.Id, out byte[]? bytes) ? new MemoryStream(bytes, writable: false) : null;
        public Stream? Target(VaultRekeyArtifact item) =>
            _targets.TryGetValue(item.Id, out byte[]? bytes) ? new MemoryStream(bytes, writable: false) : null;
        public string Folder() => Directory.CreateDirectory(Path.Combine(_dir.FullName, Guid.NewGuid().ToString("N"))).FullName;

        public VaultTransitionFilePrototype Provider(string? folder = null, bool provision = true)
        {
            var provider = new VaultTransitionFilePrototype(folder ?? Folder(), Key, ProviderId);
            if (provision) provider.Provision(Initial.Stamp);
            return provider;
        }

        public VaultGenerationDiskStore Data(VaultGenerationManifest? initial = null)
        {
            var store = new VaultGenerationDiskStore(Folder());
            store.Stage(initial ?? Initial);
            return store;
        }

        public void Dispose()
        {
            Fresh.Dispose(); Note.Dispose(); Child.Dispose(); Root.Dispose();
            _dir.Delete(recursive: true);
        }

        private static byte[] Rekey(byte[] ciphertext, LayerSecret[] current, LayerSecret[] replacement)
        {
            byte[] result = LayeredEmd3.Rekey(ciphertext, current, replacement, NewPassword,
                out byte[] secret, currentPassword: Password);
            CryptographicOperations.ZeroMemory(secret);
            return result;
        }
    }
}
