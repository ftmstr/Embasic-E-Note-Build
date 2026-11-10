namespace Embasic.ENote.Storage;

/// <summary>Owns a provider while exposing only its witness-bound operations.</summary>
public sealed class VaultManagedAuthority : IDisposable
{
    private readonly VaultTransitionFilePrototype _provider;
    public IVaultWitnessedAuthority Authority { get; }

    internal VaultManagedAuthority(VaultRecoveryPolicyReader policy, string directory, ReadOnlySpan<byte> key,
        Guid providerIdentity)
    {
        _provider = new(directory, key, providerIdentity);
        Authority = policy.BindProvider(_provider);
    }

    public void Dispose() => _provider.Dispose();
}
