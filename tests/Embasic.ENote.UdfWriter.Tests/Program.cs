using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Export.UdfWriter;

UdfParagraph[] sample =
[
    new("EMBASIC E-NOTE — SENTETİK UDF DENEMESİ", Bold: true),
    new("Çığ, gölge, ışık, ölçü, şüphe ve İğdır."),
    new(""),
    new("Düzenleme denemesi için bu satıra ‘YENİ’ ekleyin.")
];
if (args is ["--sample", var path])
{
    UnsignedUdfWriter.WriteNew(path, sample, PageLayoutSettings.Default);
    return;
}
if (args.Length != 0) throw new ArgumentException("Expected --sample <new .udf path> or no arguments.");

var directory = Directory.CreateTempSubdirectory("enote-udf-tests-");
int passed = 0;
try
{
    string Target(string name) => Path.Combine(directory.FullName, name);

    Check("unsigned package includes only content.xml and an exact Turkish text pool", () =>
    {
        string path = Target("sample.udf");
        UnsignedUdfWriter.WriteNew(path, sample, PageLayoutSettings.Default);
        var xml = ReadPackage(path);
        Require(xml.Name == "template" && (string?)xml.Attribute("format_id") == "1.8");
        Require(xml.Element("content")!.Value == string.Concat(sample.Select(p => p.Text + "\n")));
        var paragraphs = xml.Element("elements")!.Elements("paragraph").ToArray();
        Require(paragraphs.Length == sample.Length);
        int offset = 0;
        for (int i = 0; i < sample.Length; i++)
        {
            var run = paragraphs[i].Element("content")!;
            Require((int?)run.Attribute("startOffset") == offset);
            Require((int?)run.Attribute("length") == sample[i].Text.Length + 1);
            Require(((bool?)run.Attribute("bold") ?? false) == sample[i].Bold);
            offset += sample[i].Text.Length + 1;
        }
    });

    Check("A4 portrait uses invariant point conversion and the configured margins", () =>
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            string path = Target("geometry.udf");
            UnsignedUdfWriter.WriteNew(path, sample, new() { RightMarginMm = 25m });
            var page = ReadPackage(path).Element("properties")!.Element("pageFormat")!;
            Require((string?)page.Attribute("mediaSizeName") == "1");
            Require((string?)page.Attribute("paperOrientation") == "1");
            Require((string?)page.Attribute("leftMargin") == "56.69");
            Require((string?)page.Attribute("rightMargin") == "70.87");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    });

    Check("XML-like text and CDATA delimiter remain literal", () =>
    {
        string path = Target("literal.udf");
        const string text = "<öğe> & ]]> Çığ";
        UnsignedUdfWriter.WriteNew(path, [new(text)], PageLayoutSettings.Default);
        Require(ReadPackage(path).Element("content")!.Value == text + "\n");
    });

    Check("unsupported input fails before creating any output", () =>
    {
        string path = Target("rejected.udf");
        Throws<NotSupportedException>(() => UnsignedUdfWriter.WriteNew(path, sample, new() { Landscape = true }));
        Throws<NotSupportedException>(() => UnsignedUdfWriter.WriteNew(path, sample, new() { Paper = PageLayoutCatalog.Get("A5") }));
        Throws<ArgumentOutOfRangeException>(() => UnsignedUdfWriter.WriteNew(path, [], PageLayoutSettings.Default));
        Throws<ArgumentOutOfRangeException>(() => UnsignedUdfWriter.WriteNew(path,
            [new(new string('a', UnsignedUdfWriter.MaximumTextCharacters))], PageLayoutSettings.Default));
        foreach (string text in new[] { "😀", "\t", "\0", "x\ny", "\uFFFC" })
            Throws<ArgumentException>(() => UnsignedUdfWriter.WriteNew(path, [new(text)], PageLayoutSettings.Default));
        Require(!File.Exists(path) && !Directory.EnumerateFiles(directory.FullName, "*.enote-udf-tmp").Any());
    });

    Check("existing destination and Markdown source remain byte-identical", () =>
    {
        string source = Target("kaynak.md"), destination = Target("existing.udf");
        byte[] bytes = [1, 3, 5, 7];
        File.WriteAllBytes(source, bytes);
        File.WriteAllBytes(destination, bytes);
        Throws<NotSupportedException>(() => UnsignedUdfWriter.WriteNew(source, sample, PageLayoutSettings.Default));
        Throws<IOException>(() => UnsignedUdfWriter.WriteNew(destination, sample, PageLayoutSettings.Default));
        Require(File.ReadAllBytes(source).SequenceEqual(bytes) && File.ReadAllBytes(destination).SequenceEqual(bytes));
    });

    Check("two competing exports give one complete file and clean staging", () =>
    {
        string path = Target("contended.udf");
        int winners = 0, rejected = 0;
        Parallel.For(0, 2, _ =>
        {
            try { UnsignedUdfWriter.WriteNew(path, sample, PageLayoutSettings.Default); Interlocked.Increment(ref winners); }
            catch (IOException) { Interlocked.Increment(ref rejected); }
        });
        Require(winners == 1 && rejected == 1);
        Require(ReadPackage(path).Element("elements")!.Elements("paragraph").Count() == sample.Length);
        Require(!Directory.EnumerateFiles(directory.FullName, "*.enote-udf-tmp").Any());
    });
    Console.WriteLine($"{passed}/6 Tur36 unsigned UDF writer checks passed.");
}
finally { directory.Delete(recursive: true); }

void Check(string message, Action check)
{
    check();
    passed++;
    Console.WriteLine($"PASS {message}");
}

static XElement ReadPackage(string path)
{
    using var archive = ZipFile.OpenRead(path);
    Require(archive.Entries.Count == 1 && archive.Entries[0].FullName == "content.xml");
    using var entry = archive.Entries[0].Open();
    return XElement.Load(entry, LoadOptions.PreserveWhitespace);
}

static void Require(bool condition)
{
    if (!condition) throw new InvalidOperationException("UDF assertion failed.");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
