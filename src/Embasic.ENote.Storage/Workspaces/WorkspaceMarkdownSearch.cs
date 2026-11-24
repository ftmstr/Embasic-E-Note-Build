using System.Globalization;
using System.Text;
using Embasic.ENote.Core.Notes;

namespace Embasic.ENote.Storage.Workspaces;

public sealed record WorkspaceMarkdownHit(string Path, string RelativePath, string MatchLabel);

public sealed record WorkspaceMarkdownSearchResult(
    IReadOnlyList<WorkspaceMarkdownHit> Hits, int Unreadable, int Oversized, bool Truncated);

/// <summary>Read-only, on-demand search over saved plain notes in the selected workspace.</summary>
public static class WorkspaceMarkdownSearch
{
    public const int MaximumResults = 200;
    public const int MaximumContentBytes = 2 * 1024 * 1024;
    private static readonly CompareInfo Turkish = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Task<WorkspaceMarkdownSearchResult> SearchAsync(
        WorkspaceRoot workspace, string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        string term = query.Trim();
        if (Turkish.Compare(term, "", CompareOptions.IgnoreCase) == 0)
            throw new ArgumentException("Bir arama terimi yazın.", nameof(query));
        return Task.Run(() => Search(workspace, term, cancellationToken), cancellationToken);
    }

    public static void ValidateResult(WorkspaceRoot workspace, string path)
    {
        var parent = Path.GetDirectoryName(path) ?? throw new IOException("Notun klasörü bulunamadı.");
        WorkspaceStorage.CheckDirectory(workspace, parent);
        string relative = Path.GetRelativePath(workspace.FullPath, path);
        if (relative.Split(Path.DirectorySeparatorChar).Any(IsInternalDirectory) ||
            NoteFilePolicy.Detect(path) != NoteFileFormat.PlainMarkdown ||
            (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("Sonuç artık açılabilir bir çalışma alanı notu değil.");
    }

    private static WorkspaceMarkdownSearchResult Search(WorkspaceRoot workspace, string query, CancellationToken cancellationToken)
    {
        var hits = new List<WorkspaceMarkdownHit>();
        var pending = new Stack<string>();
        pending.Push(WorkspaceStorage.CheckDirectory(workspace, workspace.FullPath));
        int unreadable = 0;
        int oversized = 0;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                WorkspaceStorage.CheckDirectory(workspace, directory);
                foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (!IsInternalDirectory(Path.GetFileName(path))) pending.Push(path);
                            continue;
                        }

                        if (NoteFilePolicy.Detect(path) != NoteFileFormat.PlainMarkdown) continue;
                        string? match = FindMatch(path, query, cancellationToken, ref oversized);
                        if (match is null) continue;
                        if (hits.Count == MaximumResults) return Result(truncated: true);
                        hits.Add(new WorkspaceMarkdownHit(path, Path.GetRelativePath(workspace.FullPath, path), match));
                    }
                    catch (Exception error) when (IsReadFailure(error)) { unreadable++; }
                }
            }
            catch (Exception error) when (IsReadFailure(error)) { unreadable++; }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Result(truncated: false);

        WorkspaceMarkdownSearchResult Result(bool truncated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hits.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
            return new WorkspaceMarkdownSearchResult(hits.AsReadOnly(), unreadable, oversized, truncated);
        }
    }

    private static string? FindMatch(string path, string query, CancellationToken token, ref int oversized)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.SequentialScan);
        bool nameMatch = Matches(Path.GetFileName(path), query);
        if (nameMatch) return "Dosya adı";

        long size = stream.Length;
        if (size > MaximumContentBytes) { oversized++; return null; }
        var bytes = new byte[(int)size];
        int offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.Read(bytes.AsSpan(offset, Math.Min(16384, bytes.Length - offset)));
            if (read == 0) throw new IOException("Not taranırken değişti.");
            offset += read;
        }
        token.ThrowIfCancellationRequested();
        if (stream.ReadByte() != -1) throw new IOException("Not taranırken değişti.");
        int bomLength = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        string content = Utf8.GetString(bytes.AsSpan(bomLength));
        return Matches(content, query) ? "İçerik" : null;
    }

    private static bool Matches(string text, string query) =>
        Turkish.IndexOf(text, query, CompareOptions.IgnoreCase) >= 0;

    private static bool IsInternalDirectory(string name) =>
        name.Equals(WorkspaceFileOperations.TrashName, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(".enote-draft-", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or DecoderFallbackException;
}
