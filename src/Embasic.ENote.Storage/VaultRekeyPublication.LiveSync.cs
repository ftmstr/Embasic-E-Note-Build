using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

public sealed record VaultLiveSyncResult(int Replaced, int Remaining);

public sealed partial class VaultRekeyPublication
{
    /// <summary>
    /// Advance the cooperating live tree from the exact parent toward a registered selected rekey.
    /// Each note is replaceable independently. A later call may resume a mixture of exact parent and selected notes.
    /// </summary>
    public VaultLiveSyncResult SynchronizeSelectedLiveTree(VaultGenerationRecovery recovery, VaultGenerationStamp candidate,
        LayerSecret sourceRoot, LayerSecret selectedRoot, int maximumReplacements = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReplacements);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(candidate.WorkspaceId);
        if (state.Current != candidate || state.Pending is not null || state.LastCommitted?.Candidate != candidate)
            throw new CryptographicException("Live synchronization requires the exact selected rekey receipt.");

        var (parent, source, commitment) = ReadPublicationPlan(candidate, sourceRoot);
        if (parent.Stamp != state.LastCommitted.Expected)
            throw new CryptographicException("Synchronization source differs from the selected receipt.");
        var manifest = ReadExact(candidate);
        commitment.RequireMatch(parent, manifest);
        var selected = OpenRegisteredSnapshot(manifest, selectedRoot)
            ?? throw new InvalidDataException("Register the complete selected snapshot before live synchronization.");
        var live = selected.ObserveLiveDocuments(_liveRoot);
        if (live.Count != selected.Documents.Count || live.Count != source.Documents.Count)
            throw new CryptographicException("The live document set differs from the selected catalog.");

        var remaining = new List<(VaultRekeyDocument Prior, VaultRekeyDocument Next)>();
        for (int i = 0; i < live.Count; i++)
        {
            var prior = source.Documents[i];
            var next = selected.Documents[i];
            var observed = live[i];
            if (prior.RelativePath != next.RelativePath || next.RelativePath != observed.RelativePath ||
                (observed.SourceSha256 != prior.SourceSha256 && observed.SourceSha256 != next.SourceSha256))
                throw new CryptographicException("Live document differs from both authenticated generations.");
            if (observed.SourceSha256 != next.SourceSha256) remaining.Add((prior, next));
        }

        VaultWorkspaceSnapshot.VerifyLiveVaults(manifest);
        int replacements = Math.Min(maximumReplacements, remaining.Count);
        for (int i = 0; i < replacements; i++)
        {
            var pair = remaining[i];
            string path = selected.LiveDocumentPath(_liveRoot, pair.Next);
            string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Guid.NewGuid():N}.enote-sync");
            bool created = false;
            try
            {
                var target = new VaultRekeyArtifact("note:" + pair.Next.RelativePath, VaultRekeyArtifactKind.Document,
                    pair.Next.RelativePath, pair.Next.SourceSha256, []);
                using (var input = selected.OpenSource(target))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough))
                {
                    created = true;
                    VaultRekeyStaging.Verify(target, input, pair.Next.SourceSha256, output);
                    output.Flush(flushToDisk: true);
                }
                _ = selected.LiveDocumentPath(_liveRoot, pair.Next);
                using (var old = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    VaultRekeyStaging.Verify(target, old, pair.Prior.SourceSha256);
                RequireCurrent();
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                File.Replace(temporary, path, destinationBackupFileName: null);
                created = false;
                RequireCurrent();
            }
            finally
            {
                if (created) File.Delete(temporary);
            }
        }

        if (remaining.Count == replacements) selected.VerifyLive(_liveRoot, manifest);
        RequireCurrent();
        return new VaultLiveSyncResult(replacements, remaining.Count - replacements);

        void RequireCurrent()
        {
            if (recovery.ReadAuthorityState(candidate.WorkspaceId) != state)
                throw new VaultGenerationOutcomeUnknownException("Independent selection changed during live synchronization.");
        }
    }
}
