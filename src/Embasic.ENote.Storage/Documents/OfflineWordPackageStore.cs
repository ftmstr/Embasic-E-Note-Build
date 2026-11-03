using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Embasic.ENote.Storage.Documents;

/// <summary>
/// An intentionally conservative, entirely offline paragraph-editing boundary.
/// This is not a Word/Writer layout or full OOXML/ODF editing engine.
/// Complex files are readable as extracted text but cannot be saved by this API.
/// No network, third-party SDK or account is required.
/// </summary>
public sealed class OfflineWordPackageStore
{
    private const long MaxFileBytes = 32L * 1024 * 1024;
    private const long MaxPartBytes = 16L * 1024 * 1024;
    private const long MaxExpandedBytes = 64L * 1024 * 1024;
    private const int MaxEntries = 512;
    private const int MaxParagraphs = 4096;
    private const int MaxParagraphLength = 200_000;

    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string OdtMime = "application/vnd.oasis.opendocument.text";

    public OfflineWordSnapshot Read(string path)
    {
        var full = ValidatePath(path, mustExist: true);
        var format = FormatOf(full);
        var bytes = ReadLimited(full);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var package = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            InspectPackage(package, format);
            var document = ReadXml(RequiredEntry(package, MainPart(format)));
            var analysis = Analyze(document, format);
            bool unsafeParts = package.Entries.Any(e => IsUnsupportedPart(e.FullName, format));
            var editable = analysis.Editable && !unsafeParts;
            var reason = unsafeParts
                ? "İmza, makro, ek, görsel veya gelişmiş belge parçası var; kopyaya kayıt kapalı."
                : analysis.Reason;
            return new OfflineWordSnapshot(full, format,
                Convert.ToHexString(SHA256.HashData(bytes)),
                Array.AsReadOnly(analysis.Paragraphs.ToArray()), editable,
                editable ? null : reason);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>
    /// Save only to a new file of the SAME extension. No source replacement,
    /// no loss-prone conversion, no race-free OS-wide CAS claim.
    /// </summary>
    public string SaveEditedCopy(OfflineWordSnapshot source, string destination,
        IReadOnlyList<string> newParagraphs)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(newParagraphs);
        if (!source.CanEdit)
            throw new NotSupportedException(source.ReadOnlyReason ??
                "Complex document editing is disabled.");
        var outputPath = ValidatePath(destination, mustExist: false);
        if (FormatOf(outputPath) != source.Format ||
            !Path.GetExtension(outputPath).Equals(
                Path.GetExtension(source.FilePath), StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Save-as must retain the original DOCX/ODT extension.");
        if (string.Equals(outputPath, source.FilePath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Original document cannot be overwritten.");
        if (newParagraphs.Count != source.Paragraphs.Count)
            throw new NotSupportedException("Paragraph insertion/deletion is not safe in this editor.");
        foreach (var p in newParagraphs)
        {
            if (p is null || p.Length > MaxParagraphLength ||
                p.Contains('\r') || p.Contains('\n'))
                throw new NotSupportedException("Each paragraph must remain a single XML text run.");
            XmlConvert.VerifyXmlChars(p);
        }

        // Readback and hash validation against the original before creating temp output.
        var current = Read(source.FilePath);
        if (!current.CanEdit ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(source.Sha256),
                Convert.FromHexString(current.Sha256)))
            throw new IOException("Source changed or became unsupported; save refused.");

        var temp = Path.Combine(Path.GetDirectoryName(outputPath)!,
            "." + Path.GetFileName(outputPath) + "." +
            Guid.NewGuid().ToString("N") + ".enote-tmp");
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.WriteThrough))
            {
                using (var sourceFile = new FileStream(source.FilePath,
                    FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var original = new ZipArchive(sourceFile, ZipArchiveMode.Read))
                using (var copy = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    InspectPackage(original, source.Format);
                    var main = MainPart(source.Format);
                    foreach (var entry in original.Entries)
                    {
                        var compression = source.Format == OfflineWordFormat.Odt &&
                            entry.FullName == "mimetype"
                                ? CompressionLevel.NoCompression
                                : CompressionLevel.Optimal;
                        var targetPart = copy.CreateEntry(entry.FullName, compression);
                        targetPart.LastWriteTime = entry.LastWriteTime;
                        targetPart.ExternalAttributes = entry.ExternalAttributes;
                        using var targetStream = targetPart.Open();
                        if (entry.FullName == main)
                        {
                            var xml = ReadXml(entry);
                            var analyzed = Analyze(xml, source.Format);
                            if (!analyzed.Editable ||
                                analyzed.Paragraphs.Count != newParagraphs.Count)
                                throw new InvalidDataException("Document structure changed during save.");
                            ReplaceSimpleParagraphs(xml, source.Format, newParagraphs);
                            xml.Save(targetStream, SaveOptions.DisableFormatting);
                        }
                        else
                        {
                            using var input = entry.Open();
                            input.CopyTo(targetStream);
                        }
                    }
                }
                output.Flush(flushToDisk: true);
            }
            var finalSource = Read(source.FilePath);
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(source.Sha256),
                Convert.FromHexString(finalSource.Sha256)))
                throw new IOException("Source changed during export; output was not committed.");
            // Validate the output as a new package before making it visible.
            var copyCheck = ReadTemporaryPackage(temp, source.Format);
            if (!copyCheck.CanEdit || !copyCheck.Paragraphs.SequenceEqual(newParagraphs))
                throw new InvalidDataException("Export round-trip validation failed.");
            File.Move(temp, outputPath, overwrite: false);
            return copyCheck.Sha256;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static OfflineWordSnapshot ReadTemporaryPackage(string path, OfflineWordFormat format)
    {
        var bytes = ReadLimited(path);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var package = new ZipArchive(stream, ZipArchiveMode.Read);
            InspectPackage(package, format);
            var analysis = Analyze(ReadXml(RequiredEntry(package, MainPart(format))), format);
            return new OfflineWordSnapshot(path, format,
                Convert.ToHexString(SHA256.HashData(bytes)),
                Array.AsReadOnly(analysis.Paragraphs.ToArray()),
                analysis.Editable, analysis.Reason);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static (List<string> Paragraphs, bool Editable, string Reason)
        Analyze(XDocument xml, OfflineWordFormat format)
    {
        if (format == OfflineWordFormat.Docx)
        {
            XNamespace w = WordNs;
            var body = xml.Root?.Element(w + "body");
            if (xml.Root?.Name != w + "document" || body is null)
                throw new InvalidDataException("Missing WordprocessingML document/body.");
            var paragraphs = body.Descendants(w + "p")
                .Take(MaxParagraphs + 1)
                .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)))
                .ToList();
            bool valid = paragraphs.Count is > 0 and <= MaxParagraphs &&
                body.Elements().All(e => e.Name == w + "p" || e.Name == w + "sectPr") &&
                body.Elements(w + "p").All(p =>
                {
                    var contents = p.Elements().Where(e => e.Name != w + "pPr").ToArray();
                    if (contents.Length != 1 || contents[0].Name != w + "r") return false;
                    var run = contents[0];
                    var inner = run.Elements().Where(e => e.Name != w + "rPr").ToArray();
                    return inner.Length == 1 && inner[0].Name == w + "t" &&
                        !run.Descendants().Any(e => e.Name == w + "drawing" || e.Name == w + "object");
                });
            return (paragraphs, valid, valid ? "" :
                "Multiple runs, tables, drawings, revisions or complex Word content detected.");
        }
        else
        {
            XNamespace office = OfficeNs, text = TextNs;
            var container = xml.Root?.Element(office + "body")?.Element(office + "text");
            if (xml.Root?.Name != office + "document-content" || container is null)
                throw new InvalidDataException("Missing OpenDocument office:text content.");
            var paragraphs = container.Descendants()
                .Where(e => e.Name == text + "p" || e.Name == text + "h")
                .Take(MaxParagraphs + 1).Select(e => e.Value).ToList();
            var direct = container.Elements().Where(e =>
                e.Name == text + "p" || e.Name == text + "h").ToArray();
            bool valid = paragraphs.Count is > 0 and <= MaxParagraphs &&
                direct.Length == paragraphs.Count &&
                container.Elements().All(e => e.Name == text + "p" ||
                    e.Name == text + "h" || e.Name == text + "sequence-decls") &&
                direct.All(e => !e.Elements().Any());
            return (paragraphs, valid, valid ? "" :
                "ODF spans, lists, tables, drawings, fields or nested content detected.");
        }
    }

    private static void ReplaceSimpleParagraphs(XDocument xml, OfflineWordFormat format,
        IReadOnlyList<string> textValues)
    {
        if (format == OfflineWordFormat.Docx)
        {
            XNamespace w = WordNs;
            var paragraphs = xml.Root!.Element(w + "body")!.Elements(w + "p").ToArray();
            if (paragraphs.Length != textValues.Count)
                throw new InvalidDataException("Word paragraph count mismatch.");
            for (int i = 0; i < paragraphs.Length; i++)
            {
                var node = paragraphs[i].Element(w + "r")!.Element(w + "t")!;
                node.Value = textValues[i];
                if (textValues[i] != textValues[i].Trim())
                    node.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }
        }
        else
        {
            XNamespace office = OfficeNs, text = TextNs;
            var paragraphs = xml.Root!.Element(office + "body")!.Element(office + "text")!
                .Elements().Where(e => e.Name == text + "p" || e.Name == text + "h").ToArray();
            if (paragraphs.Length != textValues.Count)
                throw new InvalidDataException("ODT paragraph count mismatch.");
            for (int i = 0; i < paragraphs.Length; i++)
                paragraphs[i].ReplaceNodes(new XText(textValues[i]));
        }
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartBytes) throw new InvalidDataException("XML part too large.");
        using var part = entry.Open();
        using var reader = XmlReader.Create(part, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxPartBytes,
            MaxCharactersFromEntities = 0,
            CloseInput = false
        });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static void InspectPackage(ZipArchive package, OfflineWordFormat format)
    {
        if (package.Entries.Count is < 2 or > MaxEntries)
            throw new InvalidDataException("Invalid package entry count.");
        long expanded = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in package.Entries)
        {
            var name = e.FullName;
            if (string.IsNullOrWhiteSpace(name) ||
                name.StartsWith('/') || name.Contains('\\') || name.Contains(':') ||
                name.Split('/').Any(segment => segment is "." or ".." ||
                    (segment.Length == 0 && !name.EndsWith('/'))) ||
                !paths.Add(name))
                throw new InvalidDataException("Unsafe/duplicate package entry name.");
            if (e.Length > MaxPartBytes ||
                e.Length > Math.Max(1, e.CompressedLength) * 250L ||
                (expanded += e.Length) > MaxExpandedBytes)
                throw new InvalidDataException("Oversized or excessive-compression package.");
        }
        _ = RequiredEntry(package, MainPart(format));
        if (format == OfflineWordFormat.Docx)
            _ = RequiredEntry(package, "[Content_Types].xml");
        else
        {
            using var mime = RequiredEntry(package, "mimetype").Open();
            using var buffer = new MemoryStream();
            mime.CopyTo(buffer);
            if (!buffer.ToArray().SequenceEqual(Encoding.ASCII.GetBytes(OdtMime)))
                throw new InvalidDataException("ODT mimetype mismatch.");
        }
    }

    private static bool IsUnsupportedPart(string name, OfflineWordFormat format)
    {
        var path = name.ToLowerInvariant();
        if (path.Contains("signature") || path.Contains("vba") ||
            path.Contains("activex") || path.Contains("embeddings/") ||
            path.Contains("scripts/") || path.StartsWith("basic/"))
            return true;
        return format == OfflineWordFormat.Docx
            ? path.StartsWith("word/media/") || path.StartsWith("word/header") ||
              path.StartsWith("word/footer") || path.StartsWith("word/footnotes") ||
              path.StartsWith("word/endnotes") || path.StartsWith("word/comments")
            : path.StartsWith("pictures/") || path.StartsWith("object") ||
              path.StartsWith("meta-inf/documentsignatures");
    }

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string name) =>
        archive.GetEntry(name) ??
        throw new InvalidDataException("Missing package part: " + name);

    private static string MainPart(OfflineWordFormat format) =>
        format == OfflineWordFormat.Docx ? "word/document.xml" : "content.xml";

    private static OfflineWordFormat FormatOf(string file) =>
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".docx" => OfflineWordFormat.Docx,
            ".odt" => OfflineWordFormat.Odt,
            _ => throw new NotSupportedException("Only DOCX and ODT are enabled. ODF .odf is a formula format.")
        };

    private static byte[] ReadLimited(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
            throw new InvalidDataException("Package exceeds offline editor's 32 MiB safety bound.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new MemoryStream();
        input.CopyTo(output);
        if (output.Length > MaxFileBytes) throw new InvalidDataException("Package grew beyond limit.");
        return output.ToArray();
    }

    private static string ValidatePath(string path, bool mustExist)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        _ = FormatOf(full);
        var directory = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var root = Path.GetPathRoot(directory)!;
        var cursor = root;
        foreach (var part in directory[root.Length..].Split(
            Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, part);
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked parent folders are refused.");
        }
        if (File.Exists(full))
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked document refused.");
        }
        else if (mustExist) throw new FileNotFoundException("Document not found.", full);
        return full;
    }
}

public enum OfflineWordFormat { Docx, Odt }

public sealed record OfflineWordSnapshot(
    string FilePath, OfflineWordFormat Format, string Sha256,
    IReadOnlyList<string> Paragraphs, bool CanEdit, string? ReadOnlyReason);
