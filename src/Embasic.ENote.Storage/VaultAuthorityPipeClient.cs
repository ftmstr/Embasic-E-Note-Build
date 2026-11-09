using System.IO.Pipes;
using System.Security.Cryptography;
using static Embasic.ENote.Storage.VaultAuthorityPipeProtocol;

namespace Embasic.ENote.Storage;

/// <summary>Local client for signed observation and explicit publication of an exact registered candidate.</summary>
public sealed class VaultAuthorityPipeClient
{
    private readonly string _pipeName;
    private readonly byte[] _serverPublicKey;

    public VaultAuthorityPipeClient(string pipeName, ReadOnlySpan<byte> serverPublicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(serverPublicKey, out int consumed);
        if (consumed != serverPublicKey.Length || !IsP256(verifier))
            throw new CryptographicException("Expected one P-256 service public key.");
        _pipeName = pipeName;
        _serverPublicKey = serverPublicKey.ToArray();
    }

    public async Task<VaultGenerationStamp> ObserveAsync(Guid workspace, ReadOnlyMemory<byte> readerKey,
        CancellationToken cancellationToken = default)
    {
        if (workspace == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspace));
        byte[] response = await SendAsync(Observe, workspace, null, readerKey, cancellationToken).ConfigureAwait(false);
        if (response[0] != Observed) throw new CryptographicException("Service rejected the observation.");
        return ReadStamp(response.AsSpan(1, 40), workspace);
    }

    public async Task<VaultGenerationAdvanceState> PublishAsync(VaultGenerationStamp candidate,
        ReadOnlyMemory<byte> publisherKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        byte[] response = await SendAsync(Publish, candidate.WorkspaceId, candidate,
            publisherKey, cancellationToken).ConfigureAwait(false);
        return response[0] switch
        {
            Committed when ReadStamp(response.AsSpan(1, 40), candidate.WorkspaceId) == candidate =>
                VaultGenerationAdvanceState.Committed,
            PriorObserved => VaultGenerationAdvanceState.PriorObserved,
            Superseded => VaultGenerationAdvanceState.Superseded,
            Rejected => throw new CryptographicException("Service rejected the registered publication."),
            _ => throw new VaultGenerationOutcomeUnknownException("Publication did not yield a verified exact receipt.")
        };
    }

    private async Task<byte[]> SendAsync(byte operation, Guid workspace, VaultGenerationStamp? candidate,
        ReadOnlyMemory<byte> key, CancellationToken cancellationToken)
    {
        RequireKey(key.Span);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        byte[] challenge = new byte[ChallengeSize];
        await pipe.ReadExactlyAsync(challenge, timeout.Token).ConfigureAwait(false);
        byte[] request = Request(operation, workspace, candidate, challenge, key.Span);
        byte[] response = new byte[ResponseSize];
        try
        {
            await pipe.WriteAsync(request, timeout.Token).ConfigureAwait(false);
            await pipe.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (operation == Publish && error is (IOException or OperationCanceledException))
        {
            throw new VaultGenerationOutcomeUnknownException("Publication acknowledgement was lost or unreadable.");
        }
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(_serverPublicKey, out _);
        if (!VerifyResponse(response, challenge, request, verifier))
        {
            if (operation == Publish)
                throw new VaultGenerationOutcomeUnknownException("Publication response identity or binding was rejected.");
            throw new CryptographicException("Service response identity or binding was rejected.");
        }
        return response;
    }
}
