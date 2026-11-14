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
UdfParagraph[] rich =
[
    new("EMBASIC E-NOTE TUR37", Bold: true) { FontFamily = "Liberation Serif", FontSizePoints = 18 },
    new("Çığ ve İğdır — ğüşöçı") { FontFamily = "Liberation Mono", FontSizePoints = 14 },
    new("SON PARAGRAF TUR37")
];
var table = new UdfTable([
    [new("Öğe"), new("Değer")],
    [new("Türkçe"), new("Çığ")]
]);
if (args is ["--rich-sample", var richPath])
{
    UnsignedUdfWriter.WriteNew(richPath, rich, PageLayoutSettings.Default, table);
    return;
}
const string MarkdownSample = "# EMBASIC E-NOTE TUR37\r\n\r\nÇığ ve İğdır — **Türkçe**\r\nSON PARAGRAF TUR37\r\n";
if (args is ["--markdown-sample", var markdownPath])
{
    MarkdownUdfCopy.WriteNew("synthetic.md", MarkdownSample, markdownPath, PageLayoutSettings.Default);
    return;
}
if (args.Length != 0) throw new ArgumentException("Expected a sample option with a new .udf path, or no arguments.");

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
        foreach (string text in new[] { "\ud800", "\udc00", "\t", "\0", "x\ny", "\uFFFC" })
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
    passed = 0;
    Check("original Turkish text offsets cover paragraphs and every cell", () =>
    {
        string path = Target("unicode.udf");
        UnsignedUdfWriter.WriteNew(path, rich, PageLayoutSettings.Default, table);
        var xml = ReadPackage(path);
        string pool = xml.Element("content")!.Value;
        var runs = xml.Element("elements")!.Descendants("content").ToArray();
        var expected = rich.Concat(table.Rows.SelectMany(row => row)).Append(new UdfParagraph("")).ToArray();
        Require(runs.Length == expected.Length);
        int next = 0;
        foreach (var (run, paragraph) in runs.Zip(expected))
        {
            int start = (int)run.Attribute("startOffset")!, length = (int)run.Attribute("length")!;
            Require(start == next && pool.Substring(start, length) == paragraph.Text + "\n");
            next += length;
        }
        Require(next == pool.Length);
    });

    Check("explicit run fonts and two-by-two cell structure are emitted", () =>
    {
        var xml = ReadPackage(Target("unicode.udf"));
        var elements = xml.Element("elements")!;
        var heading = elements.Element("paragraph")!.Element("content")!;
        Require((string?)heading.Attribute("family") == "Liberation Serif");
        Require((int?)heading.Attribute("size") == 18);
        var grid = elements.Element("table")!;
        Require((int?)grid.Attribute("columnCount") == 2 && (string?)grid.Attribute("columnSpans") == "100,100");
        Require(grid.Elements("row").Count() == 2 && grid.Elements("row").All(row => row.Elements("cell").Count() == 2));
    });

    Check("ragged and oversized tables and invalid font attributes leave no output", () =>
    {
        string path = Target("rejected-rich.udf");
        Throws<ArgumentException>(() => UnsignedUdfWriter.WriteNew(path, rich, new(), new([[new("1"), new("2")], [new("3")]])));
        var oversized = Enumerable.Repeat<IReadOnlyList<UdfParagraph>>([new("x")], UnsignedUdfWriter.MaximumTableRows + 1).ToArray();
        Throws<ArgumentOutOfRangeException>(() => UnsignedUdfWriter.WriteNew(path, rich, new(), new(oversized)));
        Throws<ArgumentException>(() => UnsignedUdfWriter.WriteNew(path, [new("x") { FontFamily = "<style>" }], new()));
        Throws<ArgumentOutOfRangeException>(() => UnsignedUdfWriter.WriteNew(path, [new("x") { FontSizePoints = 5 }], new()));
        Require(!File.Exists(path) && !Directory.EnumerateFiles(directory.FullName, "*.enote-udf-tmp").Any());
    });
    Check("supplementary Unicode is refused in both body and table before creating an output", () =>
    {
        string path = Target("unsupported-unicode.udf");
        Throws<ArgumentException>(() => UnsignedUdfWriter.WriteNew(path, [new("Çığ 😀")], new()));
        Throws<ArgumentException>(() => UnsignedUdfWriter.WriteNew(path, rich, new(), new([[new("𐐷")]])));
        Require(!File.Exists(path));
    });
    Console.WriteLine($"{passed}/4 Tur37 rich UDF writer checks passed.");

    passed = 0;
    Check("Markdown copy preserves literal markup, empty lines, Turkish and line endings", () =>
    {
        string path = Target("literal-copy.udf");
        MarkdownUdfCopy.WriteNew("synthetic.MD", MarkdownSample, path, new() { LeftMarginMm = 25m });
        var xml = ReadPackage(path);
        Require(xml.Element("content")!.Value == MarkdownSample.Replace("\r\n", "\n"));
        Require(xml.Element("elements")!.Elements("paragraph").Count() == 4);
        Require(!xml.Descendants("table").Any());
        Require((string?)xml.Element("properties")!.Element("pageFormat")!.Attribute("leftMargin") == "70.87");
    });

    Check("copy uses current editor text without changing Markdown or its sidecar", () =>
    {
        string source = Target("edit.md"), path = Target("edit.udf");
        string sidecar = source + ".enote-layout.json";
        byte[] sourceBytes = [0xEF, 0xBB, 0xBF, 65];
        File.WriteAllBytes(source, sourceBytes);
        File.WriteAllText(sidecar, "saved layout");
        MarkdownUdfCopy.WriteNew(source, "  yenİ\rikinci\r\r", path, new());
        Require(ReadPackage(path).Element("content")!.Value == "  yenİ\nikinci\n\n");
        Require(File.ReadAllBytes(source).SequenceEqual(sourceBytes) && File.ReadAllText(sidecar) == "saved layout");
        byte[] existing = File.ReadAllBytes(path);
        Throws<IOException>(() => MarkdownUdfCopy.WriteNew(source, "replacement", path, new()));
        Require(File.ReadAllBytes(path).SequenceEqual(existing));
    });

    Check("invalid source, page, Unicode and size produce no partial UDF", () =>
    {
        string path = Target("guarded-copy.udf");
        Throws<NotSupportedException>(() => MarkdownUdfCopy.WriteNew("secret.emd", "Çığ", path, new()));
        Throws<NotSupportedException>(() => MarkdownUdfCopy.WriteNew("plain.md", "Çığ", path, new() { Landscape = true }));
        Throws<ArgumentException>(() => MarkdownUdfCopy.WriteNew("plain.md", "Çığ 😀", path, new()));
        Throws<ArgumentOutOfRangeException>(() => MarkdownUdfCopy.WriteNew("plain.md",
            new string('a', UnsignedUdfWriter.MaximumTextCharacters), path, new()));
        Throws<ArgumentOutOfRangeException>(() => MarkdownUdfCopy.WriteNew("plain.md",
            new string('\n', UnsignedUdfWriter.MaximumParagraphs + 1), path, new()));
        Require(!File.Exists(path) && !Directory.EnumerateFiles(directory.FullName, "*.enote-udf-tmp").Any());
    });
    Console.WriteLine($"{passed}/3 Tur38 Markdown UDF copy checks passed.");
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
