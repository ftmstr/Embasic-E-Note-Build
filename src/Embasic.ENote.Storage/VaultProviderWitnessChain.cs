using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

public sealed record VaultProviderWitnessLink(VaultProviderRecoveryCheckpoint Checkpoint, string PreviousSha256,
    byte[] Signature);

/// <summary>Verification of recovery history against a tip supplied outside all provider directories.</summary>
public sealed class VaultProviderWitnessChain : IVaultProviderRecoveryWitness
{
    private const string Domain = "Embasic.VaultProviderWitnessChain.v1";
    private readonly VaultProviderRecoveryCheckpoint _current;
    public string TipSha256 { get; }

    public VaultProviderWitnessChain(VaultGenerationStamp genesis, Guid genesisProvider, string genesisSha256,
        IEnumerable<VaultProviderWitnessLink> links, string independentTipSha256, ReadOnlySpan<byte> witnessKey)
    {
        ArgumentNullException.ThrowIfNull(genesis);
        ArgumentNullException.ThrowIfNull(links);
        if (genesisProvider == Guid.Empty) throw new ArgumentException("Missing genesis provider.");
        string previousHash = VaultRekeyInventory.NormalizeDigest(genesisSha256);
        var knownProviders = new HashSet<Guid> { genesisProvider };
        var selected = genesis;
        var provider = genesisProvider;
        long revision = 0;
        VaultProviderRecoveryCheckpoint? current = null;
        foreach (var link in links)
        {
            var checkpoint = link.Checkpoint;
            if (knownProviders.Count >= 1024 || link.PreviousSha256 != previousHash ||
                checkpoint.LostProvider != provider || checkpoint.ReplacementProvider == Guid.Empty ||
                !knownProviders.Add(checkpoint.ReplacementProvider) ||
                checkpoint.Selected.WorkspaceId != genesis.WorkspaceId ||
                checkpoint.Selected.Epoch < selected.Epoch ||
                (checkpoint.Selected.Epoch == selected.Epoch && checkpoint.Selected != selected) ||
                checkpoint.VacantRevision <= revision || checkpoint.VacantRevision == long.MaxValue)
                throw new CryptographicException("Invalid recovery witness continuity.");
            byte[] signature = link.Signature.ToArray();
            VaultRecoverySignatures.Verify(witnessKey, signature, Domain, writer => Write(writer, checkpoint, previousHash));
            previousHash = Digest(link with { Signature = signature });
            selected = checkpoint.Selected;
            provider = checkpoint.ReplacementProvider;
            revision = checkpoint.VacantRevision;
            current = checkpoint;
        }
        if (current is null || previousHash != VaultRekeyInventory.NormalizeDigest(independentTipSha256))
            throw new CryptographicException("Missing independently selected recovery witness tip.");
        _current = current;
        TipSha256 = previousHash;
    }

    public static VaultProviderWitnessLink Sign(VaultProviderRecoveryCheckpoint checkpoint, string previousSha256, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        string previous = VaultRekeyInventory.NormalizeDigest(previousSha256);
        return new(checkpoint, previous, VaultRecoverySignatures.Sign(key, Domain, writer => Write(writer, checkpoint, previous)));
    }

    public static string Digest(VaultProviderWitnessLink link)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(Domain);
        Write(writer, link.Checkpoint, link.PreviousSha256);
        writer.Write(link.Signature);
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    public VaultProviderRecoveryCheckpoint? Read(Guid workspace) => _current.Selected.WorkspaceId == workspace ? _current : null;

    private static void Write(BinaryWriter writer, VaultProviderRecoveryCheckpoint checkpoint, string previousSha256)
    {
        VaultRecoverySignatures.WriteDigest(writer, previousSha256);
        writer.Write(checkpoint.LostProvider.ToByteArray());
        writer.Write(checkpoint.ReplacementProvider.ToByteArray());
        VaultRecoverySignatures.WriteStamp(writer, checkpoint.Selected);
        writer.Write(checkpoint.VacantRevision);
    }
}

/// <summary>Reference provider fence using an external, live witness reader.</summary>
public interface IVaultWitnessedAuthority : IExternalVaultTransitionAnchor, IVaultRootRecoveryLifecycle
{
}

public sealed partial class VaultTransitionFilePrototype
{
    public IExternalVaultTransitionAnchor RequireActiveWitness(IVaultProviderRecoveryWitness witness)
    {
        return RequireActiveAuthority(witness);
    }

    public IVaultRootRecoveryLifecycle RequireActiveRootWitness(IVaultProviderRecoveryWitness witness)
    {
        return RequireActiveAuthority(witness);
    }

    public IVaultWitnessedAuthority RequireActiveAuthority(IVaultProviderRecoveryWitness witness)
    {
        ArgumentNullException.ThrowIfNull(witness);
        return new WitnessedAnchor(this, witness);
    }

    private sealed class WitnessedAnchor(VaultTransitionFilePrototype provider, IVaultProviderRecoveryWitness witness)
        : IVaultWitnessedAuthority
    {
        public VaultTransitionState? ReadTransition(Guid workspaceId) =>
            Run(workspaceId, state => state);

        public void ReserveTransition(VaultRetryIntent intent) =>
            Run(intent.Expected.WorkspaceId, _ => { provider.ReserveTransition(intent); return true; });

        public void CommitTransition(VaultRetryIntent intent) =>
            Run(intent.Expected.WorkspaceId, _ => { provider.CommitTransition(intent); return true; });

        public VaultRootRecoveryAuthorization ReadAuthorization(Guid workspaceId) =>
            Run(workspaceId, _ => provider.ReadAuthorization(workspaceId));

        public VaultRootRecoveryAuthorization ObserveAuthorization(VaultGenerationStamp expected) =>
            Run(expected.WorkspaceId, _ => provider.ObserveAuthorization(expected));

        public VaultRootRecoveryAuthorization CompareAndSetAuthorization(VaultGenerationStamp expected,
            long expectedRevision, VaultRootRecoveryProof? replacement) =>
            Run(expected.WorkspaceId, _ => provider.CompareAndSetAuthorization(expected, expectedRevision, replacement));

        private T Run<T>(Guid workspace, Func<VaultTransitionState, T> action)
        {
            var before = ReadWitness(workspace);
            var state = provider.ReadTransition(workspace);
            var selected = before.Selected;
            if (state is null || state.Current.Epoch < selected.Epoch ||
                (state.Current.Epoch == selected.Epoch && state.Current != selected))
                throw new CryptographicException("Provider state precedes or forks its selected recovery checkpoint.");
            T result = action(state);
            if (witness.Read(workspace) != before)
                throw new VaultGenerationOutcomeUnknownException("The external provider witness changed during the operation.");
            return result;
        }

        private VaultProviderRecoveryCheckpoint ReadWitness(Guid workspace) =>
            witness.Read(workspace) is { } selected && selected.ReplacementProvider == provider._providerIdentity &&
                selected.Selected.WorkspaceId == workspace ? selected :
                throw new CryptographicException("This provider is not selected by the external recovery witness.");
    }
}
