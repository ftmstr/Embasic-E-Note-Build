using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Embasic.ENote.Storage.Documents;

var checks = new (string Name, Action Run)[]
{
    ("DOCX simple package reads offline", () => Fixture(OfflineWordFormat.Docx,
        (s, p, _) => Check(s.Read(p) is { CanEdit: true } snap &&
            snap.Paragraphs.SequenceEqual(new[] { "Merhaba", "İkinci satır" })))),
    ("ODT simple package reads offline", () => Fixture(OfflineWordFormat.Odt,
        (s, p, _) => Check(s.Read(p) is { CanEdit: true } snap &&
            snap.Paragraphs.SequenceEqual(new[] { "Başlık", "Merhaba ODT" })))),
    ("DOCX new copy round-trips Turkish Unicode and ampersand", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) =>
        {
            var source = s.Read(p);
            var target = Path.Combine(d, "copy.docx");
            s.SaveEditedCopy(source, target, new[] { "Çağrı & ümit", "日本語 ☆" });
            Check(s.Read(target).Paragraphs.SequenceEqual(new[] { "Çağrı & ümit", "日本語 ☆" }));
        })),
    ("ODT new copy round-trips Turkish Unicode", () => Fixture(OfflineWordFormat.Odt,
        (s, p, d) =>
        {
            var target = Path.Combine(d, "copy.odt");
            s.SaveEditedCopy(s.Read(p), target, new[] { "İşlem", "Türkçe 日本語" });
            Check(s.Read(target).Paragraphs[1] == "Türkçe 日本語");
        })),
    ("DOCX source hash remains identical after copy", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) =>
        {
            var before = SHA256.HashData(File.ReadAllBytes(p));
            s.SaveEditedCopy(s.Read(p), Path.Combine(d, "new.docx"), new[] { "Yeni", "Son" });
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(p))));
        })),
    ("ODT source hash remains identical after copy", () => Fixture(OfflineWordFormat.Odt,
        (s, p, d) =>
        {
            var before = SHA256.HashData(File.ReadAllBytes(p));
            s.SaveEditedCopy(s.Read(p), Path.Combine(d, "new.odt"), new[] { "Yeni", "Son" });
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(p))));
        })),
    ("DOCX other package parts retain exact contents", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) =>
        {
            var copy = Path.Combine(d, "new.docx");
            s.SaveEditedCopy(s.Read(p), copy, new[] { "A", "B" });
            Check(Part(copy, "word/styles.xml").SequenceEqual(Encoding.UTF8.GetBytes("<styles>keep-me</styles>")));
        })),
    ("DOCX paragraph and run style XML retained", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) =>
        {
            var copy = Path.Combine(d, "new.docx");
            s.SaveEditedCopy(s.Read(p), copy, new[] { "New", "Second" });
            var xml = XDocument.Parse(Encoding.UTF8.GetString(Part(copy, "word/document.xml")));
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            Check(xml.Descendants(w + "pStyle").Any(e => (string?)e.Attribute(w + "val") == "Heading1") &&
                xml.Descendants(w + "b").Any());
        })),
    ("ODT paragraph style attribute retained", () => Fixture(OfflineWordFormat.Odt,
        (s, p, d) =>
        {
            var copy = Path.Combine(d, "new.odt");
            s.SaveEditedCopy(s.Read(p), copy, new[] { "New", "Second" });
            var xml = XDocument.Parse(Encoding.UTF8.GetString(Part(copy, "content.xml")));
            XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
            Check(xml.Descendants(text + "h").Any(e =>
                (string?)e.Attribute(text + "style-name") == "Heading_20_1"));
        })),
    ("Whitespace round trip preserved in DOCX", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) =>
        {
            var dest = Path.Combine(d, "copy.docx");
            s.SaveEditedCopy(s.Read(p), dest, new[] { "  spaced  ", "X" });
            Check(s.Read(dest).Paragraphs[0] == "  spaced  ");
        })),
    ("Original overwrite refused", () => Fixture(OfflineWordFormat.Docx,
        (s, p, _) => Expect<IOException>(() =>
            s.SaveEditedCopy(s.Read(p), p, new[] { "Changed", "Other" })))),
    ("Existing destination refused", () => Fixture(OfflineWordFormat.Odt,
        (s, p, d) =>
        {
            var target = Path.Combine(d, "already.odt");
            File.WriteAllText(target, "untouched");
            Expect<IOException>(() => s.SaveEditedCopy(s.Read(p), target, new[] { "Changed", "Other" }));
            Check(File.ReadAllText(target) == "untouched");
        })),
    ("Externally modified source blocks export", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) =>
        {
            var old = s.Read(p);
            File.AppendAllText(p, "x");
            var dest = Path.Combine(d, "blocked.docx");
            Expect<IOException>(() => s.SaveEditedCopy(old, dest, new[] { "A", "B" }));
            Check(!File.Exists(dest));
        })),
    ("Paragraph count change refused", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) => Expect<NotSupportedException>(() =>
            s.SaveEditedCopy(s.Read(p), Path.Combine(d, "bad.docx"), new[] { "Only one" })))),
    ("Newline inside paragraph refused", () => Fixture(OfflineWordFormat.Odt,
        (s, p, d) => Expect<NotSupportedException>(() =>
            s.SaveEditedCopy(s.Read(p), Path.Combine(d, "bad.odt"), new[] { "A\nB", "C" })))),
    ("DOCTYPE/DTD external entities rejected", () => Fixture(OfflineWordFormat.Docx,
        (s, p, _) => Expect<XmlException>(() => s.Read(p)),
        DocxXml.Replace("<w:document", "<!DOCTYPE w:document [<!ENTITY ext SYSTEM 'file:///etc/passwd'>]><w:document"))),
    ("Corrupted primary XML rejected", () => Fixture(OfflineWordFormat.Odt,
        (s, p, _) => Expect<XmlException>(() => s.Read(p)),
        OdtXml.Replace("</office:document-content>", "</WRONG>"))),
    ("DOCX multi-run document is read-only", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) =>
        {
            var snap = s.Read(p);
            Check(!snap.CanEdit);
            var before = File.ReadAllBytes(p);
            Expect<NotSupportedException>(() =>
                s.SaveEditedCopy(snap, Path.Combine(d, "bad.docx"), new[] { "A", "B" }));
            Check(File.ReadAllBytes(p).SequenceEqual(before));
        }, DocxXml.Replace("<w:t>Merhaba</w:t>",
            "<w:t>Mer</w:t></w:r><w:r><w:t>haba</w:t>"))),
    ("DOCX table content is read-only and original preserved", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) =>
        {
            var snap = s.Read(p);
            Check(!snap.CanEdit && snap.Paragraphs.Count >= 3);
            var digest = SHA256.HashData(File.ReadAllBytes(p));
            Expect<NotSupportedException>(() =>
                s.SaveEditedCopy(snap, Path.Combine(d, "bad.docx"), snap.Paragraphs));
            Check(digest.SequenceEqual(SHA256.HashData(File.ReadAllBytes(p))));
        }, DocxXml.Replace("<w:sectPr />",
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:sectPr />"))),
    ("DOCX drawing is read-only", () => Fixture(OfflineWordFormat.Docx,
        (s, p, _) => Check(!s.Read(p).CanEdit),
        DocxXml.Replace("<w:t>Merhaba</w:t>", "<w:t>Merhaba</w:t><w:drawing/>"))),
    ("ODT styled span is read-only", () => Fixture(OfflineWordFormat.Odt,
        (s, p, _) => Check(!s.Read(p).CanEdit),
        OdtXml.Replace("<text:p>Merhaba ODT</text:p>",
            "<text:p><text:span text:style-name=\"Bold\">Merhaba ODT</text:span></text:p>"))),
    ("ODT table is read-only", () => Fixture(OfflineWordFormat.Odt,
        (s, p, _) => Check(!s.Read(p).CanEdit),
        OdtXml.Replace("</office:text>",
            "<table:table xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\"><table:table-row><table:table-cell><text:p>Hücre</text:p></table:table-cell></table:table-row></table:table></office:text>"))),
    ("Signed DOCX is read-only", () => Fixture(OfflineWordFormat.Docx,
        (s, p, _) => Check(!s.Read(p).CanEdit), extra:"_xmlsignatures/sig1.xml")),
    ("Macro-containing DOCX is read-only", () => Fixture(OfflineWordFormat.Docx,
        (s, p, _) => Check(!s.Read(p).CanEdit), extra:"word/vbaProject.bin")),
    ("ODT mimetype mismatch refused", () => Fixture(OfflineWordFormat.Odt,
        (s, p, _) => Expect<InvalidDataException>(() => s.Read(p)),
        wrongMime:true)),
    ("ZIP path traversal refused", () => Fixture(OfflineWordFormat.Docx,
        (s, p, _) => Expect<InvalidDataException>(() => s.Read(p)), extra:"../unsafe.xml")),
    ("Duplicate ZIP member names refused", () => Fixture(OfflineWordFormat.Docx,
        (s, p, _) => Expect<InvalidDataException>(() => s.Read(p)),
        extra:"word/document.xml")),
    ("Malformed ZIP fails closed", () => Fixture(OfflineWordFormat.Docx,
        (s, p, _) =>
        {
            File.WriteAllBytes(p, Encoding.ASCII.GetBytes("not a zip"));
            Expect<InvalidDataException>(() => s.Read(p));
        })),
    ("Legacy .odf extension is not ODT", () => Fixture(OfflineWordFormat.Odt,
        (s, p, d) =>
        {
            var other = Path.Combine(d, "formula.odf");
            File.Copy(p, other);
            Expect<NotSupportedException>(() => s.Read(other));
        })),
    ("Incorrect target extension refused", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) => Expect<NotSupportedException>(() =>
            s.SaveEditedCopy(s.Read(p), Path.Combine(d, "other.odt"), new[] { "A", "B" })))),
    ("Temporary files cleaned after refusal", () => Fixture(OfflineWordFormat.Docx,
        (s, p, d) =>
        {
            var target = Path.Combine(d, "existing.docx");
            File.WriteAllText(target, "Existing");
            Expect<IOException>(() => s.SaveEditedCopy(s.Read(p), target, new[] { "A", "B" }));
            Check(!Directory.EnumerateFiles(d, "*.enote-tmp").Any());
        })),
    ("No external SDK NuGet dependency in editor", () => Check(
        typeof(OfflineWordPackageStore).Assembly.GetReferencedAssemblies()
            .All(a => !a.Name!.Contains("OpenXml", StringComparison.OrdinalIgnoreCase))))
};
int fail = 0;
foreach (var (name, run) in checks)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { fail++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
Console.WriteLine($"{checks.Length - fail}/{checks.Length} Tur11 checks passed.");
return fail == 0 ? 0 : 1;

void Fixture(OfflineWordFormat format, Action<OfflineWordPackageStore, string, string> action,
    string? xml = null, string? extra = null, bool wrongMime = false)
{
    var folder = Path.Combine(Path.GetTempPath(), "enote-tur11-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    var file = Path.Combine(folder, format == OfflineWordFormat.Docx ? "source.docx" : "source.odt");
    try
    {
        using (var output = File.Create(file))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        {
            if (format == OfflineWordFormat.Docx)
            {
                Write(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                Write(archive, "word/document.xml", xml ?? DocxXml);
                Write(archive, "word/styles.xml", "<styles>keep-me</styles>");
            }
            else
            {
                Write(archive, "mimetype", wrongMime ? "application/x-unknown" : "application/vnd.oasis.opendocument.text",
                    CompressionLevel.NoCompression);
                Write(archive, "content.xml", xml ?? OdtXml);
                Write(archive, "META-INF/manifest.xml",
                    "<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\"/>");
            }
            if (extra is not null) Write(archive, extra, "opaque-extra-part");
        }
        action(new OfflineWordPackageStore(), file, folder);
    }
    finally { Directory.Delete(folder, recursive: true); }
}

static void Write(ZipArchive a, string path, string content,
    CompressionLevel level = CompressionLevel.Optimal)
{
    var e = a.CreateEntry(path, level);
    using var stream = e.Open();
    var bytes = Encoding.UTF8.GetBytes(content);
    stream.Write(bytes);
}
static byte[] Part(string file, string name)
{
    using var zip = ZipFile.OpenRead(file);
    using var input = zip.GetEntry(name)!.Open();
    using var output = new MemoryStream();
    input.CopyTo(output);
    return output.ToArray();
}
static void Check(bool value)
{
    if (!value) throw new Exception("Assertion failed.");
}
static void Expect<T>(Action a) where T : Exception
{
    try { a(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
const string DocxXml = """
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>Merhaba</w:t></w:r></w:p><w:p><w:r><w:t>İkinci satır</w:t></w:r></w:p><w:sectPr /></w:body></w:document>
""";
const string OdtXml = """
<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"><office:body><office:text><text:h text:style-name="Heading_20_1">Başlık</text:h><text:p>Merhaba ODT</text:p></office:text></office:body></office:document-content>
""";
