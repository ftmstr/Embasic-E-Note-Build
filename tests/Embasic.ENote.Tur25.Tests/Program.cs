using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private const string Password = "Tur25 original synthetic credential";
    private const string ReplacementPassword = "Tur25 replacement synthetic credential";
    private const string BackupPassword = "Tur25 recovery synthetic credential";
    private const string Markdown = "# TUR 25\n\nDeneme belgesi.";
    private static readonly byte[] Key = SHA256.HashData("Tur25 public fixture HMAC key"u8);
    private static int _passed;
    private static int _failed;

    private static int Main(string[] args)
    {
        if (args.Length > 0) return RunChild(args);
        using var f = new Fixture();
        ProviderChecks(f);
        RecoveryChecks(f);
        RekeyChecks(f);
        if (OperatingSystem.IsWindows()) WindowsChecks(f);
        else Console.WriteLine("NOT RUN Windows scoped key use checks");
        Console.WriteLine($"{_passed}/{_passed + _failed} Tur25 provider, lifecycle and rekey staging checks passed.");
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

    private static Process StartChild(string command, string folder, VaultRetryIntent intent, string signal)
    {
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
        var start = new ProcessStartInfo(processPath) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var arg in new[] { command, folder, intent.Expected.WorkspaceId.ToString(),
            intent.Expected.ManifestSha256, intent.Candidate.ManifestSha256, signal })
            start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start child.");
    }

    private static int RunChild(string[] args)
    {
        var workspace = Guid.Parse(args[2]);
        var intent = new VaultRetryIntent(new VaultGenerationStamp(workspace, 1, args[3]),
            new VaultGenerationStamp(workspace, 2, args[4]));
        using var provider = new VaultTransitionFilePrototype(args[1], Key);
        switch (args[0])
        {
            case "hold":
                using (provider.EnterKeyUse(intent.Expected))
                {
                    File.WriteAllText(args[5], "entered");
                    Thread.Sleep(Timeout.Infinite);
                }
                return 0;
            case "reserve-after-signal":
                WaitForSignal(args[5]);
                break;
            case "reserve-with-signal":
                File.WriteAllText(args[5], "started");
                break;
            default: return 9;
        }
        try { provider.ReserveTransition(intent); return 0; }
        catch (CryptographicException) { return 3; }
    }

    private static void WaitForSignal(string path)
    {
        if (!SpinWait.SpinUntil(() => File.Exists(path), TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Child signal was not observed.");
    }

    private static int WaitForChild(Process process)
    {
        if (!process.WaitForExit(10_000))
        {
            process.Kill();
            process.WaitForExit();
            throw new TimeoutException("Child process did not terminate.");
        }
        return process.ExitCode;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("enote-tur25-");
        public readonly Guid Workspace = Guid.NewGuid();
        public readonly LayerSecret Root = LayerSecret.Generate(LayerKind.Folder);
        public readonly LayerSecret Child = LayerSecret.Generate(LayerKind.Subfolder);
        public readonly LayerSecret Note = LayerSecret.Generate(LayerKind.NoteAdditional);
        public readonly string VaultPath;
        public readonly byte[] Index;
        public readonly byte[] RootVault;
        public readonly VaultGenerationManifest Initial;
        public readonly VaultGenerationManifest Next;
        public readonly VaultGenerationManifest Rival;
        public readonly VaultRootRecoveryBackup Backup;
        public VaultRetryIntent Intent => new(Initial.Stamp, Next.Stamp);
        public VaultRetryIntent RivalIntent => new(Initial.Stamp, Rival.Stamp);
        public WorkspaceLockEntry[] Entries =>
        [
            new(Root.Id, Guid.Empty, LayerKind.Folder, "Vault"),
            new(Child.Id, Root.Id, LayerKind.Subfolder, "Vault/Sub"),
            new(Note.Id, Child.Id, LayerKind.NoteAdditional, "Vault/Sub/Locked.emd")
        ];

        public Fixture()
        {
            VaultPath = Path.Combine(_directory.FullName, "Root.elkey");
            RootVault = LayerKeyVault.Create(Root, Password, BackupPassword);
            File.WriteAllBytes(VaultPath, RootVault);
            WindowsVaultFileIdentity identity = new(5, 2, 3);
            if (OperatingSystem.IsWindows())
            {
                using var handle = File.OpenHandle(VaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                identity = WindowsVaultFileIdentity.Read(handle);
            }
            byte[] childVault = LayerKeyVault.Create(Child, Password, BackupPassword);
            byte[] noteVault = LayerKeyVault.Create(Note, Password, BackupPassword);
            Initial = Manifest(1, VaultPath);
            Next = Manifest(2, VaultPath);
            Rival = Manifest(2, Path.Combine(_directory.FullName, "Rival.elkey"));
            Index = WorkspaceLockIndex.Protect(Workspace, Root, Entries);
            Backup = VaultRootRecovery.Prepare(Initial, Root.Id, RootVault, Password, Index, BackupPassword);

            VaultGenerationManifest Manifest(long epoch, string path) => VaultGenerationManifest.Create(Workspace, epoch,
            [
                Grant(Root, RootVault, path, identity, epoch),
                Grant(Child, childVault, Path.Combine(_directory.FullName, "Child.elkey"), new(6, 2, 3), epoch),
                Grant(Note, noteVault, Path.Combine(_directory.FullName, "Note.elkey"), new(7, 2, 3), epoch)
            ]);
        }

        private VaultAnchorGrant Grant(LayerSecret factor, byte[] vault, string path,
            WindowsVaultFileIdentity identity, long epoch) => new(Workspace, factor.Id, factor.Kind,
            VaultSnapshotPin.CaptureAuthenticated(vault, factor.Id, factor.Kind, Password, epoch), epoch, path)
            { BoundVaultFileIdentity = identity };

        public string Folder() => Directory.CreateDirectory(Path.Combine(_directory.FullName, Guid.NewGuid().ToString("N"))).FullName;

        public VaultTransitionFilePrototype Open(string directory, bool provision = false)
        {
            var provider = new VaultTransitionFilePrototype(directory, Key);
            if (provision) provider.Provision(Initial.Stamp);
            return provider;
        }

        public VaultGenerationDiskStore Data(string directory)
        {
            var store = new VaultGenerationDiskStore(directory);
            store.Stage(Initial);
            return store;
        }

        public void Dispose()
        {
            Note.Dispose(); Child.Dispose(); Root.Dispose();
            _directory.Delete(recursive: true);
        }
    }
}
