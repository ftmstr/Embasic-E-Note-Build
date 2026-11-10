using System.Diagnostics;
using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>
/// Cooperative, authenticated file reference for the independent provider protocol.
/// A protected directory and an external 32-byte key are caller requirements.
/// Restoring the directory can roll back this prototype. It is not a production anchor.
/// </summary>
public sealed partial class VaultTransitionFilePrototype :
    IExternalVaultTransitionAnchor, IVaultKeyUseFence, IVaultRootRecoveryLifecycle, IDisposable
{
    private readonly string _directory;
    private readonly byte[] _key;
    private readonly Guid? _providerIdentity;
    private readonly object _keyGate = new();
    private bool _disposed;

    public VaultTransitionFilePrototype(string existingDirectory, ReadOnlySpan<byte> authenticationKey)
        : this(existingDirectory, authenticationKey, null) { }

    public VaultTransitionFilePrototype(string existingDirectory, ReadOnlySpan<byte> authenticationKey,
        Guid? providerIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingDirectory);
        if (authenticationKey.Length != 32)
            throw new ArgumentException("Expected a 32-byte authentication key.", nameof(authenticationKey));
        if (providerIdentity == Guid.Empty)
            throw new ArgumentException("A pinned provider identity must be nonempty.", nameof(providerIdentity));
        _directory = Path.GetFullPath(existingDirectory);
        if (!Directory.Exists(_directory)) throw new DirectoryNotFoundException(_directory);
        _providerIdentity = providerIdentity;
        CheckDirectory();
        _key = authenticationKey.ToArray();
    }

    /// <summary>Explicit first use. A missing state cannot be re-created over an existing marker.</summary>
    public void Provision(VaultGenerationStamp initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (initial.Epoch != 1) throw new ArgumentException("Provisioning requires epoch one.", nameof(initial));
        Initialize(initial, 0);
    }

    private void Initialize(VaultGenerationStamp initial, long revision, Action? authorize = null)
    {
        lock (_keyGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CheckDirectory();
            authorize?.Invoke();
            using var marker = new FileStream(Slot(initial.WorkspaceId, "lock"), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None);
            marker.Flush(flushToDisk: true);
            Publish(new ProviderState(new VaultTransitionState(initial), new VaultRootRecoveryAuthorization(revision, null)),
                replace: false);
        }
    }

    public VaultTransitionState ReadTransition(Guid workspaceId)
    {
        using var marker = Enter(workspaceId);
        return Read(workspaceId).Transition;
    }

    public void ReserveTransition(VaultRetryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        using var marker = Enter(intent.Expected.WorkspaceId);
        var state = Read(intent.Expected.WorkspaceId);
        var transition = state.Transition;
        if (transition.Pending == intent || transition.LastCommitted == intent) return;
        if (transition.Current != intent.Expected || transition.Pending is not null)
            throw new CryptographicException("Reservation conflicts with independent state.");
        Publish(state with { Transition = transition with { Pending = intent } });
    }

    public void CommitTransition(VaultRetryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        using var marker = Enter(intent.Expected.WorkspaceId);
        var state = Read(intent.Expected.WorkspaceId);
        if (state.Transition.LastCommitted == intent) return;
        if (state.Transition.Current != intent.Expected || state.Transition.Pending != intent)
            throw new CryptographicException("Commit requires the exact reserved intent.");

        var recovery = new VaultRootRecoveryAuthorization(checked(state.Recovery.Revision + 1), null);
        Publish(new ProviderState(new VaultTransitionState(intent.Candidate, LastCommitted: intent), recovery));
    }

    public IDisposable EnterKeyUse(VaultGenerationStamp expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var marker = Enter(expected.WorkspaceId);
        try
        {
            var state = Read(expected.WorkspaceId).Transition;
            RequireQuiescent(expected, state);
            return marker;
        }
        catch
        {
            marker.Dispose();
            throw;
        }
    }

    public VaultRootRecoveryAuthorization ReadAuthorization(Guid workspaceId)
    {
        using var marker = Enter(workspaceId);
        return Read(workspaceId).Recovery;
    }

    public VaultRootRecoveryAuthorization ObserveAuthorization(VaultGenerationStamp expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var marker = Enter(expected.WorkspaceId);
        var state = Read(expected.WorkspaceId);
        RequireQuiescent(expected, state.Transition);
        return state.Recovery;
    }

    public VaultRootRecoveryAuthorization CompareAndSetAuthorization(VaultGenerationStamp expected,
        long expectedRevision, VaultRootRecoveryProof? replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (replacement is not null) ValidateProof(expected, replacement);
        using var marker = Enter(expected.WorkspaceId);
        var state = Read(expected.WorkspaceId);
        RequireQuiescent(expected, state.Transition);
        if (state.Recovery.Revision != expectedRevision)
            throw new CryptographicException("Recovery authorization revision changed.");

        var authorization = new VaultRootRecoveryAuthorization(checked(expectedRevision + 1), replacement);
        Publish(state with { Recovery = authorization });
        return authorization;
    }

    private static void RequireQuiescent(VaultGenerationStamp expected, VaultTransitionState observed)
    {
        if (observed.Current != expected || observed.Pending is not null)
            throw new CryptographicException("Generation changed or an independent transition is pending.");
    }

    private FileStream Enter(Guid workspaceId)
    {
        string path = Slot(workspaceId, "lock");
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            CheckDirectory();
            CheckFile(path);
            try { return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException e) when (e is not (FileNotFoundException or DirectoryNotFoundException) &&
                Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2))
            {
                Thread.Sleep(15);
            }
        }
    }

    private void CheckDirectory()
    {
        if (_providerIdentity is null) return;
        for (DirectoryInfo? folder = new(_directory); folder is not null; folder = folder.Parent)
        {
            if ((File.GetAttributes(folder.FullName) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) !=
                FileAttributes.Directory)
                throw new IOException("Pinned provider requires an ordinary directory path.");
        }
    }

    private void CheckFile(string path)
    {
        if (_providerIdentity is not null &&
            (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("Pinned provider requires ordinary marker and state files.");
    }

    private string Slot(Guid workspaceId, string suffix)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspaceId));
        return Path.Combine(_directory, $"{workspaceId:N}.{suffix}");
    }

    public void Dispose()
    {
        lock (_keyGate)
        {
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
    }

    private sealed record ProviderState(VaultTransitionState Transition, VaultRootRecoveryAuthorization Recovery);
}
