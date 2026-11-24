using System.Text.Json;

namespace Embasic.ENote.Storage.Workspaces;

/// <summary>
/// Explicit, no-overwrite operations for plain Markdown workspaces. No key or .emd access.
/// Directory renames stay on one filesystem. A note and its layout are two OS moves:
/// ordinary failure rolls the sidecar back; this is not a crash-atomic or hostile-race CAS.
/// Nothing is permanently deleted. Completed trash entries survive application restart.
/// </summary>
public sealed class WorkspaceFileOperations
{
    public const string TrashName = ".enote-trash";
    public const string LayoutSuffix = ".enote-layout.json";
    private const string Marker = "Embasic.ENote.PlainTrash/v1";
    private static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>Creates an empty plain note without replacing an existing path or reusing old layout.</summary>
    public static string CreateMarkdown(WorkspaceRoot workspace, string folderPath, string name,
        IReadOnlyCollection<string> openNotePaths)
    {
        ValidateName(name);
        if (Path.GetExtension(name).Equals(".emd", Comparison))
            throw new ArgumentException("Güvenli .emd oluşturma kapalı. Düz not için .md kullanın.", nameof(name));
        string filename = name.EndsWith(".md", Comparison) ? name : name + ".md";
        if (filename.Length <= 3)
            throw new ArgumentException("Dosya uzantısından önce bir not adı yazın.", nameof(name));

        string folder = WorkspaceStorage.CheckDirectory(workspace, folderPath);
        if (Path.GetRelativePath(workspace.FullPath, folder).Split(Path.DirectorySeparatorChar)
            .Any(segment => segment.Equals(TrashName, Comparison) || segment.StartsWith(".enote-draft-", Comparison)))
            throw new UnauthorizedAccessException("Bu klasörde yeni not oluşturulamaz.");

        string path = Path.Combine(folder, filename);
        if (openNotePaths.Any(open => Equal(open, path)))
            throw new IOException("Bu yolda zaten açık bir not sekmesi var.");
        if (EntryExists(path))
            throw new IOException("Bu adda bir dosya veya klasör zaten var.");
        if (EntryExists(path + LayoutSuffix))
            throw new IOException("Bu ada ait eski sayfa düzeni dosyası var. Başka bir ad seçin.");

        // The OS checks existence again when it opens the new file.
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        return path;
    }

    private static bool EntryExists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
    }

    public WorkspaceChange Rename(WorkspaceRoot workspace, string source, string name)
    {
        ValidateName(name);
        string from = ValidateSource(workspace, source);
        return MoveTo(workspace, from, Path.Combine(Path.GetDirectoryName(from)!, name));
    }

    public WorkspaceChange Move(WorkspaceRoot workspace, string source, string destinationFolder)
    {
        string from = ValidateSource(workspace, source);
        string parent = CheckPath(workspace, destinationFolder);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        return MoveTo(workspace, from, Path.Combine(parent, Path.GetFileName(from)));
    }

    public WorkspaceChange Trash(WorkspaceRoot workspace, string source)
    {
        string from = ValidateSource(workspace, source);
        bool directory = Directory.Exists(from);
        string trash = EnsureTrash(workspace);
        string id = Guid.NewGuid().ToString("N");
        string entry = Path.Combine(trash, id);
        Directory.CreateDirectory(entry);
        var receipt = new TrashReceipt(1, id, Path.GetRelativePath(workspace.FullPath, from),
            directory, DateTimeOffset.UtcNow);
        // Retain the original path before the first move, including on interruption.
        using (var stream = new FileStream(Path.Combine(entry, "receipt.json"), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, receipt);
            stream.Flush(flushToDisk: true);
        }
        string stored = Path.Combine(entry, directory ? "content" : "content.md");
        MoveCore(from, stored, directory);
        return new WorkspaceChange(from, stored, directory, WorkspaceChangeKind.Trash, id);
    }

    public WorkspaceTrashEntry? LatestTrash(WorkspaceRoot workspace)
    {
        string root = CheckPath(workspace, workspace.FullPath);
        string trash = Path.Combine(root, TrashName);
        if (!Directory.Exists(trash)) return null;
        AssertTrash(trash);
        WorkspaceTrashEntry? latest = null;
        foreach (string entry in Directory.EnumerateDirectories(trash))
        {
            CheckPath(workspace, entry, allowTrash: true);
            string id = Path.GetFileName(entry);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            string record = Path.Combine(entry, "receipt.json");
            if (!File.Exists(record)) continue;
            CheckPath(workspace, record, allowTrash: true);
            if (new FileInfo(record).Length > 65536) throw new InvalidDataException("Silme kaydı çok büyük.");
            var receipt = JsonSerializer.Deserialize<TrashReceipt>(File.ReadAllBytes(record))
                ?? throw new InvalidDataException("Silme kaydı okunamadı.");
            if (receipt.Version != 1 || receipt.Id != id || Path.IsPathRooted(receipt.RelativePath))
                throw new InvalidDataException("Silme kaydı kimliği veya sürümü geçersiz.");
            string stored = Path.Combine(entry, receipt.Directory ? "content" : "content.md");
            // An empty retained receipt is a completed restore or a failed move rolled back.
            if (!File.Exists(stored) && !Directory.Exists(stored))
            {
                if (!receipt.Directory && File.Exists(stored + LayoutSuffix))
                    throw new IOException($"Kesintiye uğramış silme: yan dosya {stored + LayoutSuffix} konumunda korundu. Elle kurtarma gerekiyor; başka silme geri alınmadı.");
                continue;
            }
            string original = CheckPath(workspace, Path.Combine(root, receipt.RelativePath));
            if (Equal(original, root)) throw new UnauthorizedAccessException("Çalışma alanı kökü geri yüklenemez.");
            string checkedSource = ValidateSource(workspace, stored, allowTrash: true);
            if (Directory.Exists(checkedSource) != receipt.Directory)
                throw new InvalidDataException("Silme kaydı türü içerikle eşleşmiyor.");
            var item = new WorkspaceTrashEntry(id, original, stored, receipt.Directory, receipt.CreatedAt);
            if (latest is null || item.CreatedAt > latest.CreatedAt) latest = item;
        }
        return latest;
    }

    public WorkspaceChange Restore(WorkspaceRoot workspace, WorkspaceTrashEntry expected)
    {
        var current = LatestTrash(workspace) ?? throw new IOException("Geri alınacak silme yok.");
        if (current != expected) throw new IOException("Silme listesi değişti; yeniden deneyin.");
        string target = CheckPath(workspace, current.OriginalPath);
        if (!Directory.Exists(Path.GetDirectoryName(target)))
            throw new DirectoryNotFoundException("Özgün üst klasör yok. Önce üst klasörün silinmesini geri alın.");
        if (!current.IsDirectory && !target.EndsWith(".md", Comparison))
            throw new NotSupportedException("Yalnız düz .md notları geri yüklenebilir.");
        ValidateName(Path.GetFileName(target));
        MoveCore(current.StoredPath, target, current.IsDirectory);
        return new WorkspaceChange(current.StoredPath, target, current.IsDirectory,
            WorkspaceChangeKind.Restore, current.Id);
    }

    public static bool ContainsPath(string parent, string candidate) =>
        Equal(parent, candidate) || candidate.StartsWith(
            Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, Comparison);

    public static string MapPath(string path, string from, string to) =>
        Equal(path, from) ? to : Path.Combine(to, Path.GetRelativePath(from, path));

    public static bool IsOwnedTrash(string path)
    {
        if (!Path.GetFileName(path).Equals(TrashName, Comparison)) return false;
        try { AssertTrash(path); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static WorkspaceChange MoveTo(WorkspaceRoot workspace, string from, string destination)
    {
        string to = CheckPath(workspace, destination);
        ValidateName(Path.GetFileName(to));
        bool directory = Directory.Exists(from);
        if (Equal(from, to)) throw new IOException("Yeni ad veya hedef, mevcut yoldan farklı olmalı.");
        if (directory && ContainsPath(from, to)) throw new IOException("Klasör kendi içine taşınamaz.");
        if (!directory && !to.EndsWith(".md", Comparison))
            throw new NotSupportedException("Not uzantısı .md olarak kalmalı; .emd dönüşümü yapılamaz.");
        if (!Directory.Exists(Path.GetDirectoryName(to))) throw new DirectoryNotFoundException(to);
        MoveCore(from, to, directory);
        return new WorkspaceChange(from, to, directory, WorkspaceChangeKind.Move);
    }

    internal static void MoveCore(string from, string to, bool directory)
    {
        if (File.Exists(to) || Directory.Exists(to)) throw new IOException("Hedef zaten var; üzerine yazılmadı.");
        if (directory) { Directory.Move(from, to); return; }
        string layout = from + LayoutSuffix, targetLayout = to + LayoutSuffix;
        if (Directory.Exists(layout)) throw new IOException("Sayfa yan dosyası yolunda bir klasör var.");
        if (File.Exists(targetLayout) || Directory.Exists(targetLayout))
            throw new IOException("Hedefte eski sayfa yan dosyası var; işlem reddedildi.");
        bool hasLayout = File.Exists(layout);
        if (hasLayout)
        {
            if ((File.GetAttributes(layout) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Bağlantılı sayfa yan dosyası reddedildi.");
            File.Move(layout, targetLayout, overwrite: false);
        }
        try { File.Move(from, to, overwrite: false); }
        catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
        {
            if (hasLayout)
            {
                try { File.Move(targetLayout, layout, overwrite: false); }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"Not taşınmadı; yan dosya geri alınamadı. Veriler korundu: {layout} ve {targetLayout}",
                        new AggregateException(moveError, rollbackError));
                }
            }
            throw;
        }
    }

    private static string ValidateSource(WorkspaceRoot workspace, string path, bool allowTrash = false)
    {
        string source = CheckPath(workspace, path, allowTrash);
        ValidateName(Path.GetFileName(source));
        if (Equal(source, Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.FullPath))))
            throw new UnauthorizedAccessException("Çalışma alanı kökü değiştirilemez.");
        if (File.Exists(source))
        {
            if (!source.EndsWith(".md", Comparison))
                throw new NotSupportedException("Bu işlem düz .md notları içindir; .emd ve diğer belgeler korunur.");
            return source;
        }
        if (!Directory.Exists(source)) throw new FileNotFoundException("Kaynak artık yok.", source);
        // Iterative traversal avoids a call-stack depth limit. Encrypted/unknown files
        // make the complete folder operation fail before the first mutation.
        var pending = new Stack<string>(); pending.Push(source);
        while (pending.TryPop(out string? folder))
            foreach (string item in Directory.EnumerateFileSystemEntries(folder))
            {
                CheckPath(workspace, item, allowTrash);
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(item);
                else if (!item.EndsWith(".md", Comparison) && !item.EndsWith(".md" + LayoutSuffix, Comparison))
                    throw new NotSupportedException("Klasörde .md/sayfa yan dosyası dışı içerik var; klasör değiştirilmedi.");
            }
        return source;
    }

    private static string CheckPath(WorkspaceRoot workspace, string path, bool allowTrash = false)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.FullPath));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!ContainsPath(root, full)) throw new UnauthorizedAccessException("Yol çalışma alanı dışında.");
        if (!allowTrash && ContainsPath(Path.Combine(root, TrashName), full))
            throw new UnauthorizedAccessException("Silinenler yalnız geri alma işlemiyle yönetilir.");
        string cursor = root;
        AssertNotLink(cursor);
        string relative = Path.GetRelativePath(root, full);
        if (relative != ".")
            foreach (string segment in relative.Split(Path.DirectorySeparatorChar))
            {
                cursor = Path.Combine(cursor, segment);
                AssertNotLink(cursor);
            }
        return full;
    }

    private static void AssertNotLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Bağlantılı dosya/klasör reddedildi.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static string EnsureTrash(WorkspaceRoot workspace)
    {
        string path = CheckPath(workspace, Path.Combine(workspace.FullPath, TrashName), allowTrash: true);
        if (File.Exists(path)) throw new IOException("Silinenler yolu başka bir dosya tarafından kullanılıyor.");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            using var stream = new FileStream(Path.Combine(path, "format.txt"), FileMode.CreateNew);
            using var writer = new StreamWriter(stream); writer.Write(Marker);
        }
        AssertTrash(path);
        return path;
    }

    private static void AssertTrash(string path)
    {
        AssertNotLink(path);
        string marker = Path.Combine(path, "format.txt"); AssertNotLink(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length > 128 || File.ReadAllText(marker) != Marker)
            throw new IOException("Var olan .enote-trash klasörü bu uygulamanın silinenler deposu değil; değiştirilmedi.");
    }

    private static bool Equal(string a, string b) => string.Equals(a, b, Comparison);

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name != name.Trim() || name is "." or ".." || name.EndsWith('.') ||
            name.Equals(TrashName, Comparison) || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
            throw new ArgumentException("Geçersiz Windows dosya/klasör adı.");
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3])))
            throw new ArgumentException("Ayrılmış Windows aygıt adı.");
    }

    private sealed record TrashReceipt(int Version, string Id, string RelativePath,
        bool Directory, DateTimeOffset CreatedAt);
}

public enum WorkspaceChangeKind { Move, Trash, Restore }
public sealed record WorkspaceChange(string Source, string Target, bool IsDirectory,
    WorkspaceChangeKind Kind, string? TrashId = null);
public sealed record WorkspaceTrashEntry(string Id, string OriginalPath, string StoredPath,
    bool IsDirectory, DateTimeOffset CreatedAt);
