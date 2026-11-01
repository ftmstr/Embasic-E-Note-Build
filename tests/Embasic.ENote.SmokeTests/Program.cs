using Embasic.ENote.Core.Notes;
using Embasic.ENote.Storage;
using Embasic.ENote.Storage.Workspaces;

var checks = new (string Name, Func<Task> Run)[]
{
    ("md extension is plaintext", () => Check(
        NoteFilePolicy.Detect("example.md") == NoteFileFormat.PlainMarkdown)),
    ("uppercase EMD recognized", () => Check(
        NoteFilePolicy.Detect("Example.EMD") == NoteFileFormat.EncryptedEmd)),
    ("emd cannot be read as plaintext", () => Check(
        !NoteFilePolicy.CanReadAsPlaintext("secret.emd"))),
    ("unrecognized extension denied", () => Check(
        !NoteFilePolicy.CanReadAsPlaintext("secret.txt"))),
    ("blank paths denied", () => Check(
        NoteFilePolicy.Detect("  ") == NoteFileFormat.Unsupported)),
    ("encrypted reader fails closed", async () =>
    {
        await ExpectThrows<NotSupportedException>(
            () => new PlainMarkdownReader().ReadAsync("secret.emd"));
    }),
    ("UTF-8 markdown round-trip", async () =>
        await InWorkspace(async (_, path) =>
        {
            var file = Path.Combine(path, "unicode.md");
            await File.WriteAllTextAsync(file, "Türkçe — 日本語 ✨");
            await Check(await new PlainMarkdownReader().ReadAsync(file) == "Türkçe — 日本語 ✨");
        })),
    ("existing workspace accepted", () =>
        InWorkspace((storage, path) => Check(storage.OpenExisting(path).FullPath ==
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))))),
    ("missing workspace refused", async () =>
        await ExpectThrows<FileNotFoundException>(() =>
        {
            _ = new WorkspaceStorage().OpenExisting(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            return Task.CompletedTask;
        })),
    ("one-level directory listing is lazy", () =>
        InWorkspace(async (storage, path) =>
        {
            Directory.CreateDirectory(Path.Combine(path, "Sub"));
            Directory.CreateDirectory(Path.Combine(path, "Sub", "Grandchild"));
            var nodes = await storage.ListChildrenAsync(storage.OpenExisting(path), path);
            await Check(nodes.Count == 1 && nodes[0].Name == "Sub" &&
                nodes[0].Kind == WorkspaceEntryKind.Folder);
        })),
    ("directory and supported notes sorted; other files ignored", () =>
        InWorkspace(async (storage, path) =>
        {
            Directory.CreateDirectory(Path.Combine(path, "b"));
            Directory.CreateDirectory(Path.Combine(path, "A"));
            await File.WriteAllTextAsync(Path.Combine(path, "b.EMD"), "opaque fixture");
            await File.WriteAllTextAsync(Path.Combine(path, "a.md"), "plain");
            await File.WriteAllTextAsync(Path.Combine(path, "ignored.txt"), "not a note");
            var nodes = await storage.ListChildrenAsync(storage.OpenExisting(path), path);
            await Check(nodes.Count == 4 && nodes[0].Name == "A" &&
                nodes[1].Name == "b" &&
                nodes[2].Kind == WorkspaceEntryKind.PlainMarkdown &&
                nodes[3].Kind == WorkspaceEntryKind.EncryptedEmd);
        })),
    ("nested folder creation with Unicode name", () =>
        InWorkspace((storage, path) =>
        {
            var root = storage.OpenExisting(path);
            var sub = storage.CreateFolder(root, path, "Türkçe 日本語");
            var deep = storage.CreateFolder(root, sub, "Derin");
            return Check(Directory.Exists(deep));
        })),
    ("path outside workspace is refused", async () =>
        await InWorkspace(async (storage, path) =>
        {
            var root = storage.OpenExisting(path);
            await ExpectThrows<UnauthorizedAccessException>(() =>
            {
                storage.CreateFolder(root, Path.GetTempPath(), "NotAllowed");
                return Task.CompletedTask;
            });
        })),
    ("traversal name is refused", async () =>
        await InWorkspace(async (storage, path) =>
        {
            await ExpectThrows<ArgumentException>(() =>
            {
                storage.CreateFolder(storage.OpenExisting(path), path, "..");
                return Task.CompletedTask;
            });
        })),
    ("Windows reserved device name is refused", async () =>
        await InWorkspace(async (storage, path) =>
        {
            await ExpectThrows<ArgumentException>(() =>
            {
                storage.CreateFolder(storage.OpenExisting(path), path, "CON.txt");
                return Task.CompletedTask;
            });
        })),
    ("duplicate folder fails without modification", async () =>
        await InWorkspace(async (storage, path) =>
        {
            var root = storage.OpenExisting(path);
            storage.CreateFolder(root, path, "Same");
            await ExpectThrows<IOException>(() =>
            {
                storage.CreateFolder(root, path, "Same");
                return Task.CompletedTask;
            });
        })),
    ("directory enumeration cancellation propagates", async () =>
        await InWorkspace(async (storage, path) =>
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await ExpectThrows<OperationCanceledException>(() =>
                storage.ListChildrenAsync(storage.OpenExisting(path), path, cancelled.Token));
        }))
};

var failures = 0;
foreach (var (name, run) in checks)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {error}");
    }
}

Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;

static Task Check(bool condition)
{
    if (!condition) throw new Exception("Assertion failed.");
    return Task.CompletedTask;
}

static async Task ExpectThrows<T>(Func<Task> operation) where T : Exception
{
    try
    {
        await operation();
    }
    catch (T)
    {
        return;
    }
    throw new Exception($"Expected exception {typeof(T).Name}.");
}

static async Task InWorkspace(Func<IWorkspaceStorage, string, Task> operation)
{
    var folder = Path.Combine(Path.GetTempPath(), "enote-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        await operation(new WorkspaceStorage(), folder);
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }
}
