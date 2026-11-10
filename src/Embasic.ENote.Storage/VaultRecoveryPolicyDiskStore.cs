using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

public sealed record VaultRecoveryPolicyPin(Guid Workspace, long Revision, string Sha256);

public sealed record VaultRecoveryPolicyRecord(Guid Workspace, long Revision, string? WitnessTipSha256,
    VaultRootReEnrollmentApproval? RootApproval);

/// <summary>Signed recovery policy files addressed by an externally supplied exact pin.</summary>
public sealed class VaultRecoveryPolicyDiskStore
{
    private const string Domain = "Embasic.VaultRecoveryPolicy.v1";
    private const int MaximumBytes = 256;
    private readonly VaultAtomicFileDirectory _files;
    private readonly byte[] _publicKey;

    public VaultRecoveryPolicyDiskStore(string directory, ReadOnlySpan<byte> signingPublicKey)
    {
        _files = new(directory);
        _publicKey = signingPublicKey.ToArray();
    }

    public static byte[] Sign(VaultRecoveryPolicyRecord record, ECDsa key)
    {
        Validate(record);
        return VaultRecoverySignatures.Sign(key, Domain, writer => Write(writer, record));
    }

    public VaultRecoveryPolicyPin Stage(VaultRecoveryPolicyRecord record, ReadOnlySpan<byte> signature)
    {
        Validate(record);
        VaultRecoverySignatures.Verify(_publicKey, signature, Domain, writer => Write(writer, record));
        using var data = new MemoryStream(MaximumBytes);
        using (var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("ERP1"u8);
            Write(writer, record);
            writer.Write(signature);
        }
        byte[] bytes = data.ToArray();
        var pin = new VaultRecoveryPolicyPin(record.Workspace, record.Revision, Convert.ToHexString(SHA256.HashData(bytes)));
        _files.Publish(Name(pin), stream => stream.Write(bytes), () => Read(pin) == record);
        return pin;
    }

    public VaultRecoveryPolicyRecord? Read(VaultRecoveryPolicyPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        using var file = _files.OpenRead(Name(pin));
        if (file is null) return null;
        if (file.Length is < 94 or > MaximumBytes) throw new InvalidDataException("Invalid recovery policy length.");
        byte[] bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1 || Convert.ToHexString(SHA256.HashData(bytes)) != pin.Sha256 ||
            !bytes.AsSpan(0, 4).SequenceEqual("ERP1"u8))
            throw new CryptographicException("Recovery policy differs from the independent pin.");
        using var reader = new BinaryReader(new MemoryStream(bytes, writable: false));
        _ = reader.ReadBytes(4);
        var workspace = new Guid(reader.ReadBytes(16));
        long revision = reader.ReadInt64();
        string? tip = ReadPresence(reader) ? ReadDigest(reader) : null;
        VaultRootReEnrollmentApproval? approval = null;
        if (ReadPresence(reader))
        {
            var stamp = new VaultGenerationStamp(new Guid(reader.ReadBytes(16)), reader.ReadInt64(), ReadDigest(reader));
            approval = new(stamp, reader.ReadInt64(), ReadDigest(reader));
        }
        var record = new VaultRecoveryPolicyRecord(workspace, revision, tip, approval);
        byte[] signature = reader.ReadBytes(64);
        Validate(record);
        if (reader.BaseStream.Position != reader.BaseStream.Length || workspace != pin.Workspace || revision != pin.Revision)
            throw new CryptographicException("Recovery policy identity or tail is invalid.");
        VaultRecoverySignatures.Verify(_publicKey, signature, Domain, writer => Write(writer, record));
        return record;
    }

    public VaultRecoveryPolicyReader Select(Func<Guid, VaultRecoveryPolicyPin?> independentSelection,
        VaultProviderWitnessChain? chain = null) => new(this, independentSelection, chain);

    private static void Validate(VaultRecoveryPolicyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Workspace == Guid.Empty || record.Revision <= 0)
            throw new ArgumentException("Invalid recovery policy workspace or revision.");
        if (record.WitnessTipSha256 is { } tip && VaultRekeyInventory.NormalizeDigest(tip) != tip)
            throw new ArgumentException("Witness tip must use its canonical digest.");
        if (record.RootApproval is { } approval && (approval.Generation.WorkspaceId != record.Workspace ||
            approval.TombstoneRevision is < 1 or long.MaxValue ||
            VaultRekeyInventory.NormalizeDigest(approval.ArchiveSha256) != approval.ArchiveSha256))
            throw new ArgumentException("Invalid recovery policy root approval.");
    }

    private static void Write(BinaryWriter writer, VaultRecoveryPolicyRecord record)
    {
        writer.Write(record.Workspace.ToByteArray());
        writer.Write(record.Revision);
        writer.Write(record.WitnessTipSha256 is not null);
        if (record.WitnessTipSha256 is { } tip) VaultRecoverySignatures.WriteDigest(writer, tip);
        writer.Write(record.RootApproval is not null);
        if (record.RootApproval is { } approval)
        {
            VaultRecoverySignatures.WriteStamp(writer, approval.Generation);
            writer.Write(approval.TombstoneRevision);
            VaultRecoverySignatures.WriteDigest(writer, approval.ArchiveSha256);
        }
    }

    private static bool ReadPresence(BinaryReader reader) => reader.ReadByte() switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Invalid recovery policy field presence.")
    };

    private static string ReadDigest(BinaryReader reader) => Convert.ToHexString(reader.ReadBytes(32));

    private static string Name(VaultRecoveryPolicyPin pin)
    {
        if (pin.Workspace == Guid.Empty || pin.Revision <= 0 ||
            VaultRekeyInventory.NormalizeDigest(pin.Sha256) != pin.Sha256)
            throw new ArgumentException("Invalid independent recovery policy pin.", nameof(pin));
        return $"{pin.Workspace:N}-{pin.Revision}-{pin.Sha256}.erp";
    }
}

public sealed class VaultRecoveryPolicyReader : IVaultProviderRecoveryWitness, IVaultRootReEnrollmentApprovalReader
{
    private readonly VaultRecoveryPolicyDiskStore _store;
    private readonly Func<Guid, VaultRecoveryPolicyPin?> _select;
    private readonly VaultProviderWitnessChain? _chain;

    internal VaultRecoveryPolicyReader(VaultRecoveryPolicyDiskStore store, Func<Guid, VaultRecoveryPolicyPin?> select,
        VaultProviderWitnessChain? chain)
    {
        _store = store;
        _select = select ?? throw new ArgumentNullException(nameof(select));
        _chain = chain;
    }

    /// <summary>Construct the generation and root authority surface with the same independently selected policy.</summary>
    public IVaultWitnessedAuthority BindProvider(VaultTransitionFilePrototype provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.RequireActiveAuthority(this);
    }

    public VaultProviderRecoveryCheckpoint? Read(Guid workspace)
    {
        var record = ReadCurrent(workspace);
        return record?.WitnessTipSha256 is { } tip && _chain?.TipSha256 == tip ? _chain.Read(workspace) : null;
    }

    public VaultRootReEnrollmentApproval? Read(VaultGenerationStamp generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        var approval = ReadCurrent(generation.WorkspaceId)?.RootApproval;
        return approval?.Generation == generation ? approval : null;
    }

    private VaultRecoveryPolicyRecord? ReadCurrent(Guid workspace)
    {
        var pin = _select(workspace);
        if (pin is null) return null;
        if (pin.Workspace != workspace) throw new CryptographicException("Recovery policy pin has another workspace.");
        var record = _store.Read(pin);
        if (_select(workspace) != pin)
            throw new VaultGenerationOutcomeUnknownException("External recovery policy selection changed during read.");
        return record;
    }
}
