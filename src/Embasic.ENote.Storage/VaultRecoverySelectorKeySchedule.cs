using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>A dual-signed selector key change, authorized separately from the EPS1 journal.</summary>
public sealed record VaultRecoverySelectorKeyTransition(long FirstRevision, byte[] NewPublicKey,
    byte[] OutgoingSignature, byte[] IncomingSignature);

internal sealed class VaultRecoverySelectorKeySchedule
{
    private const string SelectorDomain = "Embasic.VaultRecoverySelectorRotation.v1";
    private readonly List<(long FirstRevision, byte[] Key)> _keys;

    internal VaultRecoverySelectorKeySchedule(Guid workspace, ReadOnlySpan<byte> initialKey,
        IEnumerable<VaultRecoverySelectorKeyTransition> transitions, string domain = SelectorDomain)
    {
        _keys = [(1, initialKey.ToArray())];
        foreach (var transition in transitions)
        {
            var previous = _keys[^1];
            byte[] next = transition.NewPublicKey.ToArray();
            if (transition.FirstRevision <= previous.FirstRevision ||
                _keys.Any(entry => entry.Key.AsSpan().SequenceEqual(next)))
                throw new CryptographicException("Selector rotation is non-monotonic or reuses a key.");
            VaultRecoverySignatures.Verify(previous.Key, transition.OutgoingSignature, domain,
                writer => Write(writer, workspace, transition.FirstRevision, previous.Key, next));
            VaultRecoverySignatures.Verify(next, transition.IncomingSignature, domain,
                writer => Write(writer, workspace, transition.FirstRevision, previous.Key, next));
            _keys.Add((transition.FirstRevision, next));
        }
    }

    internal byte[] KeyAt(long revision)
    {
        for (int i = _keys.Count - 1; i > 0; i--)
            if (revision >= _keys[i].FirstRevision) return _keys[i].Key;
        return _keys[0].Key;
    }

    internal static VaultRecoverySelectorKeyTransition Sign(Guid workspace, long firstRevision, ECDsa outgoing, ECDsa incoming, string domain = SelectorDomain)
    {
        if (workspace == Guid.Empty || firstRevision < 2) throw new ArgumentException("Invalid selector rotation position.");
        byte[] old = outgoing.ExportSubjectPublicKeyInfo(), next = incoming.ExportSubjectPublicKeyInfo();
        void Encode(BinaryWriter writer) => Write(writer, workspace, firstRevision, old, next);
        return new(firstRevision, next, VaultRecoverySignatures.Sign(outgoing, domain, Encode),
            VaultRecoverySignatures.Sign(incoming, domain, Encode));
    }

    private static void Write(BinaryWriter writer, Guid workspace, long firstRevision, byte[] prior, byte[] next)
    {
        writer.Write(workspace.ToByteArray());
        writer.Write(firstRevision);
        writer.Write(SHA256.HashData(prior));
        writer.Write(SHA256.HashData(next));
    }
}
