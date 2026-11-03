using System.IO;
using System.Security.Cryptography;
using System.Text;
using Embasic.ENote.Storage;

byte[] Example = Encoding.ASCII.GetBytes(@"{\rtf1\ansi\deff0 Original}");
byte[] Edited = Encoding.ASCII.GetBytes(@"{\rtf1\ansi\deff0 Edited}");
var tests = new (string Name, Func<Task> Run)[]
{
    ("RTF source loads with SHA-256", () => Temporary((store, source, _) =>
    {
        var snapshot = store.Read(source);
        Check(snapshot.Bytes.SequenceEqual(Example) &&
            snapshot.Sha256 == Convert.ToHexString(SHA256.HashData(Example)));
    })),
    ("RTF uppercase extension accepted", () => Temporary((store, source, folder) =>
    {
        var upper = Path.Combine(folder, "UPPER.RTF");
        File.Copy(source, upper);
        Check(store.Read(upper).Bytes.SequenceEqual(Example));
    })),
    ("wrong source extension refused", () => Temporary((store, source, folder) =>
    {
        var wrong = Path.Combine(folder, "test.md");
        File.Copy(source, wrong);
        Expect<NotSupportedException>(() => store.Read(wrong));
    })),
    ("non RTF contents refused", () => Temporary((store, source, _) =>
    {
        File.WriteAllText(source, "ordinary plaintext", Encoding.ASCII);
        Expect<InvalidDataException>(() => store.Read(source));
    })),
    ("new copy created with exact bytes", () => Temporary((store, source, folder) =>
    {
        var snapshot = store.Read(source);
        var target = Path.Combine(folder, "edited.rtf");
        var digest = store.SaveEditedCopy(snapshot, target, Edited);
        Check(File.ReadAllBytes(target).SequenceEqual(Edited) &&
              digest == Convert.ToHexString(SHA256.HashData(Edited)));
    })),
    ("original bytes never rewritten", () => Temporary((store, source, folder) =>
    {
        store.SaveEditedCopy(store.Read(source), Path.Combine(folder, "copy.rtf"), Edited);
        Check(File.ReadAllBytes(source).SequenceEqual(Example));
    })),
    ("same source path refused", () => Temporary((store, source, _) =>
    {
        Expect<IOException>(() => store.SaveEditedCopy(store.Read(source), source, Edited));
        Check(File.ReadAllBytes(source).SequenceEqual(Example));
    })),
    ("existing destination refused without overwrite", () => Temporary((store, source, folder) =>
    {
        var dest = Path.Combine(folder, "existing.rtf");
        File.WriteAllBytes(dest, Example);
        Expect<IOException>(() => store.SaveEditedCopy(store.Read(source), dest, Edited));
        Check(File.ReadAllBytes(dest).SequenceEqual(Example));
    })),
    ("external modification prevents copy", () => Temporary((store, source, folder) =>
    {
        var snapshot = store.Read(source);
        File.WriteAllBytes(source, Edited);
        var dest = Path.Combine(folder, "should-not-exist.rtf");
        Expect<IOException>(() => store.SaveEditedCopy(snapshot, dest, Edited));
        Check(!File.Exists(dest));
    })),
    ("deleted source prevents copy", () => Temporary((store, source, folder) =>
    {
        var snapshot = store.Read(source);
        File.Delete(source);
        var dest = Path.Combine(folder, "missing.rtf");
        Expect<FileNotFoundException>(() => store.SaveEditedCopy(snapshot, dest, Edited));
        Check(!File.Exists(dest));
    })),
    ("bad output header refused", () => Temporary((store, source, folder) =>
    {
        var dest = Path.Combine(folder, "bad.rtf");
        Expect<InvalidDataException>(() => store.SaveEditedCopy(
            store.Read(source), dest, Encoding.UTF8.GetBytes("NOT RTF")));
        Check(!File.Exists(dest));
    })),
    ("DOCX extension refused", () => Temporary((store, source, folder) =>
    {
        var dest = Path.Combine(folder, "bad.docx");
        Expect<NotSupportedException>(() => store.SaveEditedCopy(
            store.Read(source), dest, Edited));
        Check(!File.Exists(dest));
    })),
    ("missing output folder refused", () => Temporary((store, source, folder) =>
    {
        var dest = Path.Combine(folder, "missing", "bad.rtf");
        Expect<DirectoryNotFoundException>(() => store.SaveEditedCopy(
            store.Read(source), dest, Edited));
    })),
    ("Unicode nested copy permitted", () => Temporary((store, source, folder) =>
    {
        var nested = Path.Combine(folder, "Türkçe 日本語");
        Directory.CreateDirectory(nested);
        var dest = Path.Combine(nested, "Düzenlenmiş 📄.rtf");
        store.SaveEditedCopy(store.Read(source), dest, Edited);
        Check(File.ReadAllBytes(dest).SequenceEqual(Edited));
    })),
    ("no temp file left after successful copy", () => Temporary((store, source, folder) =>
    {
        store.SaveEditedCopy(store.Read(source), Path.Combine(folder, "new.rtf"), Edited);
        Check(!Directory.EnumerateFiles(folder, "*.enote-tmp").Any());
    })),
    ("no temp file left after destination conflict", () => Temporary((store, source, folder) =>
    {
        var dest = Path.Combine(folder, "collision.rtf");
        File.WriteAllBytes(dest, Example);
        Expect<IOException>(() => store.SaveEditedCopy(store.Read(source), dest, Edited));
        Check(!Directory.EnumerateFiles(folder, "*.enote-tmp").Any());
    }))
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} Tur10 checks passed.");
return failures == 0 ? 0 : 1;


async Task Temporary(Action<SafeRtfCopyStore, string, string> action)
{
    var folder = Path.Combine(Path.GetTempPath(), "enote-tur10-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    var source = Path.Combine(folder, "source.rtf");
    try
    {
        File.WriteAllBytes(source, Example);
        action(new SafeRtfCopyStore(), source, folder);
    }
    finally { Directory.Delete(folder, recursive: true); }
    await Task.CompletedTask;
}
static void Check(bool ok)
{
    if (!ok) throw new Exception("Assertion failed.");
}
static void Expect<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
