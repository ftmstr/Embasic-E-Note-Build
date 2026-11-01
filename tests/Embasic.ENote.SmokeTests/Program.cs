using Embasic.ENote.Core.Notes;
using Embasic.ENote.Storage;

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
        try
        {
            await new PlainMarkdownReader().ReadAsync("secret.emd");
            throw new Exception("The plaintext reader accepted .emd.");
        }
        catch (NotSupportedException)
        {
            // Expected: no plaintext fallback.
        }
    }),
    ("UTF-8 markdown round-trip", async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"enote-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(path, "Türkçe — 日本語 ✨");
            var text = await new PlainMarkdownReader().ReadAsync(path);
            await Check(text == "Türkçe — 日本語 ✨");
        }
        finally
        {
            File.Delete(path);
        }
    })
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
        Console.Error.WriteLine($"FAIL {name}: {error.Message}");
    }
}

Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;

static Task Check(bool condition)
{
    if (!condition)
    {
        throw new Exception("Assertion failed.");
    }

    return Task.CompletedTask;
}
