using System.IO;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>
/// Read-only bridge from an authorized ELH1 index to an EXISTING workspace
/// path. It does not unlock a vault, open an EMD, or provide TOCTOU safety.
/// </summary>
public static class WorkspaceLockPathBinding
{
    public static WorkspaceBoundNote Resolve(string workspaceRoot,
        ReadOnlySpan<byte> encryptedIndex, Guid expectedWorkspace, LayerSecret rootFactor,
        string targetRelativePath, IReadOnlyDictionary<Guid, LayerSecret> authorizedFactors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        IReadOnlyList<WorkspaceLockEntry> verifiedEntries =
            WorkspaceLockIndex.Open(encryptedIndex, expectedWorkspace, rootFactor);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);
        ArgumentNullException.ThrowIfNull(authorizedFactors);
        string root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Linked workspace root denied.");
        IReadOnlyList<Guid> chain = WorkspaceLockIndex.RequiredFactors(verifiedEntries, targetRelativePath);
        var top = verifiedEntries.Single(x => x.Id == chain[0]);
        string prefix = top.RelativePath + "/";
        if (!targetRelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !targetRelativePath.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Target must be a locked EMD under root.");
        var last = verifiedEntries.Single(x => x.Id == chain[^1]);
        if (last.Kind != LayerKind.NoteAdditional)
            throw new InvalidDataException("Target note must have a separate note factor.");
        var keys = new List<LayerSecret>();
        foreach (Guid id in chain)
        {
            var node = verifiedEntries.Single(x => x.Id == id);
            if (!authorizedFactors.TryGetValue(id, out LayerSecret? key) ||
                key.Id != id || key.Kind != node.Kind)
                throw new UnauthorizedAccessException("Independent layer factor not authorized.");
            keys.Add(key);
        }
        string relative = targetRelativePath[prefix.Length..].Replace('\\','/');
        string location = root;
        foreach (string segment in relative.Split('/'))
        {
            if (segment is "" or "." or ".." || segment.Contains(':'))
                throw new InvalidDataException("Unsafe path segment.");
            location = Path.Combine(location, segment);
            if (!File.Exists(location) && !Directory.Exists(location))
                throw new FileNotFoundException("Locked workspace node missing.", location);
            if ((File.GetAttributes(location) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked workspace segment denied.");
        }
        location = Path.GetFullPath(location);
        if (!location.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) || !File.Exists(location))
            throw new UnauthorizedAccessException("Target escaped workspace.");
        return new WorkspaceBoundNote(location, chain.ToArray(), keys.AsReadOnly());
    }
}
public sealed record WorkspaceBoundNote(string FullPath, IReadOnlyList<Guid> RequiredIds,
    IReadOnlyList<LayerSecret> AuthorizedFactors);
