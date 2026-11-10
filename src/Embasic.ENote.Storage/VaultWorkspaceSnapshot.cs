using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>Frozen ciphertext copy of an explicitly supplied workspace root and its enrolled vaults.</summary>
public sealed class VaultWorkspaceSnapshot
{
    private readonly string _directory;
    private readonly byte[] _index;
    private readonly string _rootPath;
    public Guid WorkspaceId { get; }
    public Guid RootFactorId { get; }
    public string CatalogSha256 { get; }
    public IReadOnlyList<VaultRekeyDocument> Documents { get; }

    private VaultWorkspaceSnapshot(string directory, Guid workspace, Guid root, string rootPath,
        byte[] index, IReadOnlyList<VaultRekeyDocument> documents)
    {
        _directory = directory;
        _index = index;
        _rootPath = rootPath;
        WorkspaceId = workspace;
        RootFactorId = root;
        Documents = documents;
        CatalogSha256 = VaultRekeyInventory.ComputeCatalogSha256(workspace, root, Digest(index), documents);
    }

    /// <summary>Copy before enrollment and compare two observations of the live ciphertext set.</summary>
    public static VaultWorkspaceSnapshot Capture(string liveRoot, string destination,
        VaultGenerationManifest source, LayerSecret root, ReadOnlySpan<byte> index)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(root);
        string live = Path.TrimEndingDirectorySeparator(Path.GetFullPath(liveRoot));
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (IsWithin(live, target) || IsWithin(target, live))
            throw new ArgumentException("Snapshot and live roots must be disjoint.", nameof(destination));
        CheckPath(live, directory: true);
        CheckPath(Path.GetDirectoryName(target) ?? throw new ArgumentException("Snapshot needs a parent."), directory: true);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Snapshot destination already exists.");
        if (index.Length > WorkspaceLockIndex.MaximumEncodedBytes) throw new InvalidDataException("Oversized ELH1 index.");
        byte[] indexBytes = index.ToArray();
        string rootPath = RootPath(source.Stamp.WorkspaceId, root, indexBytes);

        string stage = target + "." + Guid.NewGuid().ToString("N") + ".snapshot";
        Directory.CreateDirectory(stage);
        try
        {
            string notes = Directory.CreateDirectory(Path.Combine(stage, "notes")).FullName;
            string vaults = Directory.CreateDirectory(Path.Combine(stage, "vaults")).FullName;
            var copied = ObserveNotes(live, rootPath, notes);
            foreach (var grant in source.Grants)
            {
                using var input = OpenFile(grant.BoundVaultPath!);
                if (OperatingSystem.IsWindows() &&
                    WindowsVaultFileIdentity.Read(input.SafeFileHandle) != grant.BoundVaultFileIdentity)
                    throw new CryptographicException("Source vault file identity changed.");
                using var output = CreateFile(Path.Combine(vaults, VaultName(grant.LockId)));
                if (HashAndCopy(input, "ELV1"u8, LayerKeyVault.Size, output, exact: true) != grant.Pin.CiphertextSha256)
                    throw new CryptographicException("Source vault digest changed.");
                output.Flush(flushToDisk: true);
            }
            using (var output = CreateFile(Path.Combine(stage, "index.elh")))
            {
                output.Write(indexBytes);
                output.Flush(flushToDisk: true);
            }
            if (!copied.SequenceEqual(ObserveNotes(live, rootPath)))
                throw new CryptographicException("Live workspace changed during snapshot capture.");
            var captured = Open(stage, source.Stamp.WorkspaceId, root);
            captured.VerifyAgainst(source, root);
            Directory.Move(stage, target);
            return Open(target, source.Stamp.WorkspaceId, root);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    public static VaultWorkspaceSnapshot Open(string directory, Guid workspaceId, LayerSecret root)
    {
        ArgumentNullException.ThrowIfNull(root);
        string path = Path.GetFullPath(directory);
        CheckPath(path, directory: true);
        byte[] index;
        using (var input = OpenFile(Path.Combine(path, "index.elh")))
        {
            long length = input.Length;
            if (length <= 0 || length > WorkspaceLockIndex.MaximumEncodedBytes)
                throw new InvalidDataException("Invalid snapshot index size.");
            index = new byte[(int)length];
            input.ReadExactly(index);
            if (input.ReadByte() != -1) throw new InvalidDataException("Snapshot index grew during read.");
        }
        string rootPath = RootPath(workspaceId, root, index);
        var docs = ObserveNotes(Path.Combine(path, "notes"), rootPath);
        return new VaultWorkspaceSnapshot(path, workspaceId, root.Id, rootPath, index, Array.AsReadOnly(docs));
    }

    internal static void CheckDirectoryPath(string path) => CheckPath(path, directory: true);

    internal void VerifyAgainst(VaultGenerationManifest manifest, LayerSecret root)
    {
        var observed = Open(_directory, WorkspaceId, root);
        if (observed.CatalogSha256 != CatalogSha256 ||
            (manifest.Bindings is { } bindings && bindings.CatalogSha256 != CatalogSha256))
            throw new CryptographicException("Snapshot differs from the enrolled catalog.");
        _ = VaultRekeyInventory.Create(manifest, _index, root, [RootFactorId], Documents);
        foreach (var grant in manifest.Grants)
        {
            using var input = OpenFile(Path.Combine(_directory, "vaults", VaultName(grant.LockId)));
            if (HashAndCopy(input, "ELV1"u8, LayerKeyVault.Size, exact: true) != grant.Pin.CiphertextSha256)
                throw new CryptographicException("Snapshot factor digest changed.");
        }
    }

    internal void VerifyLive(string liveRoot, VaultGenerationManifest manifest)
    {
        if (!Documents.SequenceEqual(ObserveNotes(Path.GetFullPath(liveRoot), _rootPath)))
            throw new CryptographicException("Live workspace differs from the captured catalog.");
        VerifyLiveVaults(manifest);
    }

    internal static void VerifyLiveVaults(VaultGenerationManifest manifest)
    {
        foreach (var grant in manifest.Grants)
        {
            using var input = OpenFile(grant.BoundVaultPath!);
            if ((OperatingSystem.IsWindows() &&
                    WindowsVaultFileIdentity.Read(input.SafeFileHandle) != grant.BoundVaultFileIdentity) ||
                HashAndCopy(input, "ELV1"u8, LayerKeyVault.Size, exact: true) != grant.Pin.CiphertextSha256)
                throw new CryptographicException("Live vault differs from its selected grant.");
        }
    }

    public VaultRekeyInventory BuildInventory(VaultGenerationManifest source, LayerSecret root,
        IEnumerable<Guid> changedFactors)
    {
        VerifyAgainst(source, root);
        return VaultRekeyInventory.Create(source, _index, root, changedFactors, Documents);
    }

    public Stream OpenSource(VaultRekeyArtifact item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind == VaultRekeyArtifactKind.RootIndex && item.Id == "index")
            return OpenFile(Path.Combine(_directory, "index.elh"));
        if (item.Kind == VaultRekeyArtifactKind.FactorVault && item.RequiredFactors.Count == 1 &&
            item.Id == "vault:" + item.RequiredFactors[0].ToString("N"))
            return OpenFile(Path.Combine(_directory, "vaults", VaultName(item.RequiredFactors[0])));
        if (item.Kind == VaultRekeyArtifactKind.Document && item.RelativePath is { } relative &&
            Documents.Any(x => x.RelativePath == relative && x.SourceSha256 == item.SourceSha256))
            return OpenFile(Path.Combine(_directory, "notes", relative[(_rootPath.Length + 1)..]));
        throw new InvalidDataException("Source artifact is absent from the snapshot.");
    }

    internal IReadOnlyList<VaultRekeyDocument> ObserveLiveDocuments(string liveRoot) =>
        ObserveNotes(Path.GetFullPath(liveRoot), _rootPath);

    internal string LiveDocumentPath(string liveRoot, VaultRekeyDocument document)
    {
        string path = Path.Combine(liveRoot, document.RelativePath[(_rootPath.Length + 1)..]);
        CheckPath(path, directory: false);
        return path;
    }

    internal VaultSelectedCatalogInventory InspectEntries(VaultGenerationManifest manifest)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal) { "index.elh", "notes", "vaults" };
        int vaultCount = 0;
        foreach (var grant in manifest.Grants)
        {
            expected.Add(Path.Combine("vaults", VaultName(grant.LockId)));
            vaultCount++;
        }
        foreach (var document in Documents)
        {
            string path = Path.Combine("notes", document.RelativePath[(_rootPath.Length + 1)..]
                .Replace('/', Path.DirectorySeparatorChar));
            while (!string.IsNullOrEmpty(path) && expected.Add(path))
                path = Path.GetDirectoryName(path)!;
        }

        var unknown = new List<string>();
        var pending = new Stack<string>();
        pending.Push(_directory);
        while (pending.TryPop(out var directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked selected catalog entry is refused.");
                string relative = Path.GetRelativePath(_directory, path);
                if (!expected.Remove(relative))
                    unknown.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        }
        if (expected.Count != 0) throw new IOException("A verified selected catalog entry disappeared.");
        unknown.Sort(StringComparer.Ordinal);
        return new VaultSelectedCatalogInventory(Documents.Count, vaultCount, unknown.AsReadOnly());
    }

    internal bool HasIndex(ReadOnlySpan<byte> index) => index.SequenceEqual(_index);

    internal void Materialize(string directory, VaultGenerationManifest source,
        Func<VaultRekeyArtifact, string?> targetDigest, Func<VaultRekeyArtifact, string, Stream> openTarget)
    {
        Directory.CreateDirectory(Path.Combine(directory, "notes"));
        Directory.CreateDirectory(Path.Combine(directory, "vaults"));
        Copy(new("index", VaultRekeyArtifactKind.RootIndex, null, Digest(_index), [RootFactorId]), "index.elh");
        foreach (var grant in source.Grants)
            Copy(new("vault:" + grant.LockId.ToString("N"), VaultRekeyArtifactKind.FactorVault, null,
                grant.Pin.CiphertextSha256, [grant.LockId]), Path.Combine("vaults", VaultName(grant.LockId)));
        foreach (var doc in Documents)
            Copy(new("note:" + doc.RelativePath, VaultRekeyArtifactKind.Document, doc.RelativePath,
                doc.SourceSha256, []), Path.Combine("notes", doc.RelativePath[(_rootPath.Length + 1)..]));

        void Copy(VaultRekeyArtifact artifact, string path)
        {
            string? digest = targetDigest(artifact);
            using var input = digest is null ? OpenSource(artifact) : openTarget(artifact, digest);
            string destination = Path.Combine(directory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var output = CreateFile(destination);
            VaultRekeyStaging.Verify(artifact, input, digest ?? artifact.SourceSha256, output);
            output.Flush(flushToDisk: true);
        }
    }

    private static VaultRekeyDocument[] ObserveNotes(string directory, string rootPath, string? copyTo = null)
    {
        CheckPath(directory, directory: true);
        var documents = new List<VaultRekeyDocument>();
        var folders = new Stack<string>();
        folders.Push(directory);
        while (folders.TryPop(out string? folder))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked entries are refused during workspace capture.");
                if ((attrs & FileAttributes.Directory) != 0) { folders.Push(path); continue; }
                if (!Path.GetExtension(path).Equals(".emd", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
                using var input = OpenFile(path);
                if (copyTo is null)
                {
                    documents.Add(new(rootPath + "/" + relative, HashAndCopy(input, "ELK1"u8, LayeredEmd3.MaxBytes)));
                    continue;
                }
                string copy = Path.Combine(copyTo, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                using var output = CreateFile(copy);
                string digest = HashAndCopy(input, "ELK1"u8, LayeredEmd3.MaxBytes, output);
                output.Flush(flushToDisk: true);
                documents.Add(new(rootPath + "/" + relative, digest));
            }
        }
        return documents.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static string HashAndCopy(Stream source, ReadOnlySpan<byte> magic, int max, Stream? destination = null,
        bool exact = false)
    {
        Span<byte> header = stackalloc byte[4];
        source.ReadExactly(header);
        if (!header.SequenceEqual(magic)) throw new InvalidDataException("Unexpected snapshot ciphertext format.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header);
        destination?.Write(header);
        byte[] buffer = new byte[8192];
        int total = header.Length;
        for (int count; (count = source.Read(buffer)) > 0;)
        {
            total += count;
            if (total > max) throw new InvalidDataException("Snapshot ciphertext exceeds its bound.");
            hash.AppendData(buffer.AsSpan(0, count));
            destination?.Write(buffer, 0, count);
        }
        if (exact && total != max) throw new InvalidDataException("Invalid vault snapshot size.");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string RootPath(Guid workspace, LayerSecret root, byte[] index) =>
        WorkspaceLockIndex.Open(index, workspace, root).Single(x => x.Id == root.Id).RelativePath;

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string VaultName(Guid factor) => factor.ToString("N") + ".elv";
    private static FileStream CreateFile(string path) => new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    private static FileStream OpenFile(string path)
    {
        CheckPath(path, directory: false);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static bool IsWithin(string parent, string candidate)
    {
        string relative = Path.GetRelativePath(parent, candidate);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void CheckPath(string path, bool directory)
    {
        var expected = directory ? FileAttributes.Directory : 0;
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != expected)
            throw new IOException("Unexpected or linked snapshot path.");
        for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
        {
            if ((File.GetAttributes(parent.FullName) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) !=
                FileAttributes.Directory)
                throw new IOException("Linked snapshot ancestor.");
        }
    }
}
