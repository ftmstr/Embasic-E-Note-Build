using System.IO.Pipes;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using static Embasic.ENote.Storage.VaultAuthorityPipeProtocol;

namespace Embasic.ENote.Storage;

/// <summary>
/// Single-workspace local IPC reference. The host retains the provider, snapshot key and
/// prepared plan. It offers no raw provider mutations, credential slots or key export.
/// </summary>
public sealed class VaultAuthorityPipeService : IDisposable
{
    private readonly string _pipeName;
    private readonly Guid _workspace;
    private readonly byte[] _readKey;
    private readonly byte[] _publishKey;
    private readonly ECDsa _signer;
    private readonly VaultGenerationRecovery _recovery;
    private readonly VaultRekeyPublication _publication;
    private readonly LayerSecret _root;
    private readonly VaultWindowsPipeBoundary? _windowsBoundary;
    private bool _disposed;

    public VaultAuthorityPipeService(string pipeName, Guid workspace, ReadOnlySpan<byte> readKey,
        ReadOnlySpan<byte> publishKey, ECDsa signer, VaultGenerationRecovery recovery,
        VaultRekeyPublication publication, LayerSecret root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (workspace == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspace));
        RequireKey(readKey);
        RequireKey(publishKey);
        if (CryptographicOperations.FixedTimeEquals(readKey, publishKey))
            throw new ArgumentException("Reader and publisher require distinct role keys.");
        ArgumentNullException.ThrowIfNull(signer);
        if (!IsP256(signer)) throw new ArgumentException("Expected a P-256 service key.", nameof(signer));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _publication = publication ?? throw new ArgumentNullException(nameof(publication));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _pipeName = pipeName;
        _workspace = workspace;
        _readKey = readKey.ToArray();
        _publishKey = publishKey.ToArray();
        _signer = signer;
    }

    public VaultAuthorityPipeService(string pipeName, Guid workspace, ReadOnlySpan<byte> readKey,
        ReadOnlySpan<byte> publishKey, ECDsa signer, VaultGenerationRecovery recovery,
        VaultRekeyPublication publication, VaultWindowsPipeBoundary boundary, LayerSecret root)
        : this(pipeName, workspace, readKey, publishKey, signer, recovery, publication, root)
    {
        _windowsBoundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
    }

    public async Task ServeOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var pipe = CreateEndpoint();
        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var io = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        io.CancelAfter(TimeSpan.FromSeconds(15));
        byte[] challenge = RandomNumberGenerator.GetBytes(ChallengeSize);
        await pipe.WriteAsync(challenge, io.Token).ConfigureAwait(false);
        byte[] request = new byte[RequestSize];
        await pipe.ReadExactlyAsync(request, io.Token).ConfigureAwait(false);

        byte operation = request[1];
        if (request[0] != 1 || operation is not (Observe or Publish) ||
            new Guid(request.AsSpan(2, 16)) != _workspace ||
            !VerifyRequest(request, challenge, operation == Publish ? _publishKey : _readKey))
            return;

        byte status;
        VaultGenerationStamp? stamp = null;
        try
        {
            if (operation == Observe)
            {
                if (request.AsSpan(18, 40).ContainsAnyExcept((byte)0))
                    throw new InvalidDataException("Observe does not take a candidate.");
                stamp = _recovery.ReadCommitted(_workspace).Stamp;
                status = Observed;
            }
            else
            {
                var candidate = ReadStamp(request.AsSpan(18, 40), _workspace);
                status = _publication.Resume(_recovery, candidate, _root) switch
                {
                    VaultGenerationAdvanceState.Committed => Committed,
                    VaultGenerationAdvanceState.PriorObserved => PriorObserved,
                    VaultGenerationAdvanceState.Superseded => Superseded,
                    _ => Unknown
                };
                if (status == Committed) stamp = candidate;
            }
        }
        catch (VaultGenerationOutcomeUnknownException) { status = Unknown; }
        catch (Exception error) when (error is IOException or InvalidDataException or CryptographicException or
            ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            status = operation == Publish ? Unknown : Rejected;
        }

        byte[] response = Response(status, stamp, challenge, request, _signer);
        await pipe.WriteAsync(response, io.Token).ConfigureAwait(false);
        await pipe.FlushAsync(io.Token).ConfigureAwait(false);
    }

    private NamedPipeServerStream CreateEndpoint()
    {
        if (_windowsBoundary is null)
            return new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows pipe ACLs require Windows.");
        return _windowsBoundary.CreateServer(_pipeName);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_readKey);
        CryptographicOperations.ZeroMemory(_publishKey);
        _disposed = true;
    }
}
