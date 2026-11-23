using System.Text.Json;
using System.Text.Json.Nodes;
using Embasic.ENote.Storage.Workspaces;

int passed = 0;
const string suffix = WorkspaceFileOperations.LayoutSuffix;
Check("rename preserves exact UTF-8 BOM note and opaque old layout bytes", f =>
{
    var (p, bytes, layout) = f.Note("Çığ.md");
    var c = f.Ops.Rename(f.Root, p, "İğdır.md");
    Same(bytes, File.ReadAllBytes(c.Target)); Same(layout, File.ReadAllBytes(c.Target + suffix));
    Require(!File.Exists(p) && !File.Exists(p + suffix));
});
Check("move carries the note and its old sidecar without saving text", f =>
{
    var (p, bytes, layout) = f.Note("note.md"); string folder = f.Folder("Hedef");
    var c = f.Ops.Move(f.Root, p, folder);
    Same(bytes, File.ReadAllBytes(c.Target)); Same(layout, File.ReadAllBytes(c.Target + suffix));
    Require(!File.Exists(p) && !File.Exists(p + suffix));
});
Check("nested folder rename keeps relative notes, orphan sidecars and empty folders", f =>
{
    string folder = f.Folder("Eski/İç/Boş"); var (p, bytes, _) = f.Note("Eski/İç/note.md");
    File.WriteAllText(f.PathOf("Eski/old.md" + suffix), "opaque legacy");
    var c = f.Ops.Rename(f.Root, f.PathOf("Eski"), "Yeni");
    Same(bytes, File.ReadAllBytes(f.PathOf("Yeni/İç/note.md")));
    Require(Directory.Exists(f.PathOf("Yeni/İç/Boş")) && File.ReadAllText(f.PathOf("Yeni/old.md" + suffix)) == "opaque legacy");
    Require(WorkspaceFileOperations.MapPath(p, c.Source, c.Target) == f.PathOf("Yeni/İç/note.md"));
});
Check("folder move preserves nested content and segment boundaries", f =>
{
    f.Folder("A/İç"); var (_, bytes, _) = f.Note("A/İç/note.md"); string destination = f.Folder("B");
    f.Ops.Move(f.Root, f.PathOf("A"), destination);
    Same(bytes, File.ReadAllBytes(f.PathOf("B/A/İç/note.md")));
    Require(!WorkspaceFileOperations.ContainsPath(f.PathOf("A"), f.PathOf("AB/note.md")));
});
Check("trash restore persists across service restart and uses latest-first order", f =>
{
    var (p, bytes, layout) = f.Note("one.md"); var (q, other, _) = f.Note("two.md");
    f.Ops.Trash(f.Root, p); var second = f.Ops.Trash(f.Root, q);
    var restarted = new WorkspaceFileOperations(); var latest = restarted.LatestTrash(f.Root)!;
    Require(latest.Id == second.TrashId); restarted.Restore(f.Root, latest); Same(other, File.ReadAllBytes(q));
    restarted.Restore(f.Root, restarted.LatestTrash(f.Root)!);
    Same(bytes, File.ReadAllBytes(p)); Same(layout, File.ReadAllBytes(p + suffix));
    Require(restarted.LatestTrash(f.Root) is null);
    File.Delete(p); File.CreateSymbolicLink(p, q);
    try { Require(restarted.LatestTrash(f.Root) is null); }
    finally { File.Delete(p); }
});
Check("folder trash and restore preserve a complete plain tree", f =>
{
    f.Folder("A/Boş"); var (_, bytes, _) = f.Note("A/note.md"); f.Ops.Trash(f.Root, f.PathOf("A"));
    Require(!Directory.Exists(f.PathOf("A")));
    f.Ops.Restore(f.Root, f.Ops.LatestTrash(f.Root)!);
    Require(Directory.Exists(f.PathOf("A/Boş"))); Same(bytes, File.ReadAllBytes(f.PathOf("A/note.md")));
});
Check("restore collision keeps both current target and trashed source unchanged", f =>
{
    var (p, bytes, _) = f.Note("note.md"); f.Ops.Trash(f.Root, p); var item = f.Ops.LatestTrash(f.Root)!;
    File.WriteAllText(p, "NEW TARGET"); Throws(() => f.Ops.Restore(f.Root, item));
    Require(File.ReadAllText(p) == "NEW TARGET"); Same(bytes, File.ReadAllBytes(item.StoredPath));
    File.Delete(p); f.Ops.Restore(f.Root, item); Same(bytes, File.ReadAllBytes(p));
});
Check("orphan destination layout blocks a note move without changing any source", f =>
{
    var (p, bytes, layout) = f.Note("note.md"); f.Folder("B"); string orphan = f.PathOf("B/note.md" + suffix);
    File.WriteAllText(orphan, "OLD SIDECAR"); Throws(() => f.Ops.Move(f.Root, p, f.PathOf("B")));
    Same(bytes, File.ReadAllBytes(p)); Same(layout, File.ReadAllBytes(p + suffix)); Require(File.ReadAllText(orphan) == "OLD SIDECAR");
});
Check("existing note or folder destination is never overwritten", f =>
{
    var (p, bytes, _) = f.Note("note.md"); File.WriteAllText(f.PathOf("exists.md"), "TARGET");
    Throws(() => f.Ops.Rename(f.Root, p, "exists.md")); Same(bytes, File.ReadAllBytes(p));
    Require(File.ReadAllText(f.PathOf("exists.md")) == "TARGET");
    f.Folder("A"); f.Folder("B/A"); Throws(() => f.Ops.Move(f.Root, f.PathOf("A"), f.PathOf("B")));
    Require(Directory.Exists(f.PathOf("A")) && Directory.Exists(f.PathOf("B/A")));
});
Check("encrypted and mixed folders are refused before mutation", f =>
{
    f.Folder("A"); var (p, bytes, _) = f.Note("A/note.md"); string secret = f.PathOf("A/secure.emd");
    File.WriteAllText(secret, "opaque ciphertext"); Throws(() => f.Ops.Trash(f.Root, f.PathOf("A")));
    Throws(() => f.Ops.Rename(f.Root, secret, "open.md")); Throws(() => f.Ops.Rename(f.Root, p, "secure.emd"));
    Same(bytes, File.ReadAllBytes(p)); Require(File.ReadAllText(secret) == "opaque ciphertext");
    File.Delete(secret); File.WriteAllText(f.PathOf("A/reference.docx"), "opaque document");
    Throws(() => f.Ops.Move(f.Root, f.PathOf("A"), f.Folder("B")));
    Require(File.Exists(f.PathOf("A/reference.docx")));
});
Check("root, traversal, invalid Windows names and self moves are refused", f =>
{
    var (p, bytes, _) = f.Note("note.md"); f.Folder("A/B");
    Throws(() => f.Ops.Trash(f.Root, f.Root.FullPath));
    Throws(() => f.Ops.Move(f.Root, p, Path.GetDirectoryName(f.Root.FullPath)!));
    Throws(() => f.Ops.Move(f.Root, f.PathOf("A"), f.PathOf("A/B")));
    foreach (string name in new[] { "../bad.md", "CON.md", "bad?.md", "bad.md.", " bad.md", "LPT1.md" })
        Throws(() => f.Ops.Rename(f.Root, p, name));
    Same(bytes, File.ReadAllBytes(p));
});
Check("forged receipt cannot restore outside the workspace", f =>
{
    var (p, bytes, _) = f.Note("note.md"); f.Ops.Trash(f.Root, p); var item = f.Ops.LatestTrash(f.Root)!;
    string path = Path.Combine(Path.GetDirectoryName(item.StoredPath)!, "receipt.json");
    var json = JsonNode.Parse(File.ReadAllText(path))!; json["RelativePath"] = "../outside.md";
    File.WriteAllText(path, json.ToJsonString()); Throws(() => f.Ops.LatestTrash(f.Root));
    Same(bytes, File.ReadAllBytes(item.StoredPath));
});
Check("user-owned reserved trash directory is not adopted or overwritten", f =>
{
    var (p, bytes, _) = f.Note("note.md"); f.Folder(WorkspaceFileOperations.TrashName);
    string userFile = f.PathOf(WorkspaceFileOperations.TrashName + "/mine.txt"); File.WriteAllText(userFile, "USER");
    Throws(() => f.Ops.Trash(f.Root, p)); Same(bytes, File.ReadAllBytes(p)); Require(File.ReadAllText(userFile) == "USER");
    File.WriteAllText(f.PathOf(WorkspaceFileOperations.TrashName + "/format.txt"), "unrelated format");
    Require(!WorkspaceFileOperations.IsOwnedTrash(f.PathOf(WorkspaceFileOperations.TrashName)));
});
Check("restore refuses a missing parent without changing the trash", f =>
{
    f.Folder("A"); var (p, bytes, _) = f.Note("A/note.md"); f.Ops.Trash(f.Root, p); Directory.Delete(f.PathOf("A"));
    var item = f.Ops.LatestTrash(f.Root)!; Throws(() => f.Ops.Restore(f.Root, item)); Same(bytes, File.ReadAllBytes(item.StoredPath));
});
Check("interrupted sidecar-only trash is visible as recovery required, never silently skipped", f =>
{
    var (p, bytes, layout) = f.Note("note.md"); f.Ops.Trash(f.Root, p); var item = f.Ops.LatestTrash(f.Root)!;
    File.Move(item.StoredPath, p); // State after sidecar move but before primary move.
    Throws(() => f.Ops.LatestTrash(f.Root)); Same(bytes, File.ReadAllBytes(p)); Same(layout, File.ReadAllBytes(item.StoredPath + suffix));
});
Check("directory at sidecar path and identical renames are harmless refusals", f =>
{
    var (p, bytes, _) = f.Note("note.md"); File.Delete(p + suffix); Directory.CreateDirectory(p + suffix);
    Throws(() => f.Ops.Rename(f.Root, p, "new.md")); Throws(() => f.Ops.Rename(f.Root, p, "note.md"));
    Same(bytes, File.ReadAllBytes(p)); Require(Directory.Exists(p + suffix));
});
Check("navigation hides owned trash but retains ordinary note and folder entries", f =>
{
    var (p, _, _) = f.Note("note.md"); f.Folder("Visible"); f.Ops.Trash(f.Root, p);
    var entries = new WorkspaceStorage().ListChildrenAsync(f.Root, f.Root.FullPath).GetAwaiter().GetResult();
    Require(entries.Any(x => x.Name == "Visible") && entries.All(x => x.Name != WorkspaceFileOperations.TrashName));
});
Check("directory links and descendants are rejected before a folder mutation", f =>
{
    f.Folder("A/Real"); var (p, bytes, _) = f.Note("A/Real/note.md");
    string link = f.PathOf("A/Link"); Directory.CreateSymbolicLink(link, f.PathOf("A/Real"));
    try
    {
        Throws(() => f.Ops.Move(f.Root, f.PathOf("A"), f.Folder("B")));
        Throws(() => f.Ops.Rename(f.Root, Path.Combine(link, "note.md"), "new.md"));
        Same(bytes, File.ReadAllBytes(p));
    }
    finally { Directory.Delete(link); }
});
if (OperatingSystem.IsWindows())
    Check("native Windows sharing denial rolls back the already-moved sidecar", f =>
    {
        var (p, bytes, layout) = f.Note("note.md");
        using (var held = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read))
            Throws(() => f.Ops.Rename(f.Root, p, "blocked.md"));
        Same(bytes, File.ReadAllBytes(p)); Same(layout, File.ReadAllBytes(p + suffix));
        Require(!File.Exists(f.PathOf("blocked.md")) && !File.Exists(f.PathOf("blocked.md" + suffix)));
    });
else Console.WriteLine("NOT RUN native Windows file-sharing denial on Linux.");
Console.WriteLine($"{passed}/{passed} workspace file operation checks passed.");

void Check(string name, Action<Fixture> test)
{ using var fixture = new Fixture(); test(fixture); passed++; Console.WriteLine("PASS " + name); }
static void Require(bool value) { if (!value) throw new Exception("Acceptance assertion failed."); }
static void Same(byte[] a, byte[] b) => Require(a.SequenceEqual(b));
static void Throws(Action action)
{
    try { action(); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return; }
    throw new Exception("Expected a safe refusal.");
}
sealed class Fixture : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("enote-files-");
    public WorkspaceRoot Root => new(_dir.FullName);
    public WorkspaceFileOperations Ops { get; } = new();
    public string PathOf(string relative) => Path.Combine(_dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    public string Folder(string relative) { string path = PathOf(relative); Directory.CreateDirectory(path); return path; }
    public (string Path, byte[] Bytes, byte[] Layout) Note(string name)
    {
        string path = PathOf(name); byte[] bytes = [0xef, 0xbb, 0xbf, .. System.Text.Encoding.UTF8.GetBytes("# Çığ ve İğdır 😀\r\nKaynak")];
        byte[] layout = System.Text.Encoding.UTF8.GetBytes("{old opaque layout bytes}");
        File.WriteAllBytes(path, bytes); File.WriteAllBytes(path + WorkspaceFileOperations.LayoutSuffix, layout); return (path, bytes, layout);
    }
    public void Dispose() => _dir.Delete(recursive: true);
}
