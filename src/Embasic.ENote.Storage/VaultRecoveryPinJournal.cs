using System.Globalization;
using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>Durable signed pin selection in a workspace-private directory with an external minimum pin.</summary>
public sealed class VaultRecoveryPinJournal
{
    private const string Domain = "Embasic.VaultRecoveryPinJournal.v1";
    private const int DigestBytes = 32;
    private const int RecordBytes = 4 + 16 + 8 + DigestBytes * 2 + 64;
    private static readonly byte[] Genesis = new byte[DigestBytes];
    private readonly VaultAtomicFileDirectory _files;
    private readonly string _directory;
    private readonly Guid _workspace;
    private readonly byte[] _publicKey;

    public VaultRecoveryPinJournal(string directory, Guid workspace, ReadOnlySpan<byte> selectorPublicKey)
    {
        if (workspace == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspace));
        _directory = Path.GetFullPath(directory);
        _files = new(_directory);
        _workspace = workspace;
        _publicKey = selectorPublicKey.ToArray();
    }

    public static byte[] Sign(VaultRecoveryPolicyPin? expected, VaultRecoveryPolicyPin next, ECDsa selectorKey)
    {
        ValidateSuccessor(expected, next);
        return VaultRecoverySignatures.Sign(selectorKey, Domain, writer => Write(writer, expected, next));
    }

    /// <summary>Append one authenticated successor. A retry of the exact selected slot is idempotent.</summary>
    public void Append(VaultRecoveryPolicyPin? expected, VaultRecoveryPolicyPin next, ReadOnlySpan<byte> signature)
    {
        ValidateSuccessor(expected, next);
        if (next.Workspace != _workspace) throw new CryptographicException("Pin belongs to another workspace.");
        VaultRecoverySignatures.Verify(_publicKey, signature, Domain, writer => Write(writer, expected, next));
        using var fence = _files.OpenExclusiveLock("pin-selection.lock");
        var current = ReadChain(expected);
        if (current == next) return;
        if (current != expected)
            throw new CryptographicException("Pin selection changed or has a competing successor.");
        using var buffer = new MemoryStream(RecordBytes);
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("EPS1"u8);
            Write(writer, expected, next);
            writer.Write(signature);
        }
        byte[] bytes = buffer.ToArray();
        _files.Publish(Name(next.Revision), output => output.Write(bytes), () =>
        {
            using var existing = _files.OpenRead(Name(next.Revision));
            if (existing is null) return false;
            return Decode(existing, expected) == next;
        });
    }

    /// <summary>The supplied floor must itself be maintained outside the selector and provider disks.</summary>
    public VaultRecoveryPolicyPin ReadRequired(VaultRecoveryPolicyPin? independentMinimum)
    {
        if (independentMinimum is null || independentMinimum.Workspace != _workspace)
            throw new CryptographicException("An independent minimum pin for this workspace is required.");
        using var fence = _files.OpenExclusiveLock("pin-selection.lock");
        return ReadChain(independentMinimum)
            ?? throw new CryptographicException("No verified selection is available.");
    }

    private VaultRecoveryPolicyPin? ReadChain(VaultRecoveryPolicyPin? minimum)
    {
        VaultRecoveryPolicyPin? previous = null;
        bool sawMinimum = minimum is null;
        foreach (string name in Directory.EnumerateFiles(_directory, "*.eps").Select(Path.GetFileName).OfType<string>()
                     .Order(StringComparer.Ordinal))
        {
            using var file = _files.OpenRead(name) ?? throw new IOException("Selection slot disappeared during inspection.");
            var current = Decode(file, previous);
            if (name != Name(current.Revision)) throw new CryptographicException("Selection slot has an unexpected name.");
            sawMinimum |= current == minimum;
            previous = current;
        }
        if (!sawMinimum) throw new CryptographicException("The independent minimum pin is absent from the selection history.");
        return previous;
    }

    private VaultRecoveryPolicyPin Decode(Stream file, VaultRecoveryPolicyPin? previous)
    {
        if (file.Length != RecordBytes) throw new InvalidDataException("Unexpected selection slot size.");
        using var reader = new BinaryReader(file, System.Text.Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("EPS1"u8))
            throw new InvalidDataException("Unexpected selection slot format.");
        var workspace = new Guid(reader.ReadBytes(16));
        long revision = reader.ReadInt64();
        if (!reader.ReadBytes(DigestBytes).AsSpan().SequenceEqual(PreviousDigest(previous)))
            throw new CryptographicException("Selection predecessor digest differs.");
        var next = new VaultRecoveryPolicyPin(workspace, revision, Convert.ToHexString(reader.ReadBytes(DigestBytes)));
        byte[] signature = reader.ReadBytes(64);
        ValidateSuccessor(previous, next);
        if (next.Workspace != _workspace || file.ReadByte() != -1)
            throw new CryptographicException("Selection slot has an unexpected workspace or tail.");
        VaultRecoverySignatures.Verify(_publicKey, signature, Domain, writer => Write(writer, previous, next));
        return next;
    }

    private static void ValidateSuccessor(VaultRecoveryPolicyPin? expected, VaultRecoveryPolicyPin next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.Workspace == Guid.Empty || next.Revision <= 0 || expected?.Revision == long.MaxValue ||
            next.Revision != (expected?.Revision ?? 0) + 1 ||
            (expected is not null && (expected.Workspace != next.Workspace || expected.Revision <= 0 ||
                VaultRekeyInventory.NormalizeDigest(expected.Sha256) != expected.Sha256)) ||
            VaultRekeyInventory.NormalizeDigest(next.Sha256) != next.Sha256)
            throw new CryptographicException("Invalid selection successor.");
    }

    private static byte[] PreviousDigest(VaultRecoveryPolicyPin? previous) =>
        previous is null ? Genesis : Convert.FromHexString(previous.Sha256);

    private static void Write(BinaryWriter writer, VaultRecoveryPolicyPin? expected, VaultRecoveryPolicyPin next)
    {
        writer.Write(next.Workspace.ToByteArray());
        writer.Write(next.Revision);
        writer.Write(PreviousDigest(expected));
        VaultRecoverySignatures.WriteDigest(writer, next.Sha256);
    }

    private static string Name(long revision) => revision.ToString("D19", CultureInfo.InvariantCulture) + ".eps";
}
