using Embasic.ENote.Core.Printing;
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
,
    ("A4 default page setup", () => Check(PageLayoutSettings.Default.Paper.Id == "A4" &&
        PageLayoutSettings.Default.WidthMm == 210m && PageLayoutSettings.Default.HeightMm == 297m)),
    ("A4 landscape dimensions swap", () => Check(PageLayoutSettings.Default with { Landscape = true } is var p &&
        p.WidthMm == 297m && p.HeightMm == 210m)),
    ("all research paper sizes present", () => Check(new[] {"A2","A3","A4","A5","A6","ISO-B4","ISO-B5","JIS-B5","Letter","Legal","Tabloid","Executive","Custom"}
        .All(id => PageLayoutCatalog.Presets.Any(p => p.Id == id)))),
    ("ISO and Japanese B5 differ", () => Check(PageLayoutCatalog.Get("ISO-B5").WidthMm == 176m &&
        PageLayoutCatalog.Get("JIS-B5").WidthMm == 182m)),
    ("US Letter dimensions", () => Check(PageLayoutCatalog.Get("Letter").WidthMm == 215.9m &&
        PageLayoutCatalog.Get("Letter").HeightMm == 279.4m)),
    ("custom paper within bounds", () => Check(PageLayoutCatalog.Custom(230m, 340m).Id == "Custom")),
    ("reject too-small custom paper", async () => await ExpectThrows<ArgumentOutOfRangeException>(() =>
        { _ = PageLayoutCatalog.Custom(20m, 297m); return Task.CompletedTask; })),
    ("reject too-large custom paper", async () => await ExpectThrows<ArgumentOutOfRangeException>(() =>
        { _ = PageLayoutCatalog.Custom(210m, 2500m); return Task.CompletedTask; })),
    ("printable area subtracts margins", () => Check(PageLayoutSettings.Default.PrintableWidthMm == 170m &&
        PageLayoutSettings.Default.PrintableHeightMm == 257m)),
    ("invalid margins refuse content overflow", async () => await ExpectThrows<ArgumentOutOfRangeException>(() =>
        { _ = PageLayoutSettings.Default with { LeftMarginMm = 120m, RightMarginMm = 120m }; return Task.CompletedTask; })),
    ("invalid zoom below minimum", async () => await ExpectThrows<ArgumentOutOfRangeException>(() =>
        { _ = PageLayoutSettings.Default with { ZoomPercent = 45 }; return Task.CompletedTask; })),
    ("zoom upper bound accepted", () => Check((PageLayoutSettings.Default with { ZoomPercent = 150 }).ZoomPercent == 150)),
    ("page settings are per-document immutable snapshots", () => Check((PageLayoutSettings.Default with { Landscape = true }).Landscape &&
        !PageLayoutSettings.Default.Landscape)),
    ("unknown page size rejected", async () => await ExpectThrows<KeyNotFoundException>(() =>
        { _ = PageLayoutCatalog.Get("Unknown"); return Task.CompletedTask; }))
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
