using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Embasic.ENote.Core.Printing;

namespace Embasic.ENote.Export.UdfWriter;

public sealed record UdfParagraph(string Text, bool Bold = false)
{
    public string? FontFamily { get; init; }
    public int? FontSizePoints { get; init; }
}

public sealed record UdfTable(IReadOnlyList<IReadOnlyList<UdfParagraph>> Rows);

/// <summary>Experimental, unsigned UDF 1.8 copy export for explicitly supplied paragraphs.</summary>
public static class UnsignedUdfWriter
{
    public const int MaximumParagraphs = 3_000;
    public const int MaximumTextCharacters = 200_000;
    public const int MaximumTableRows = 64;
    public const int MaximumTableColumns = 8;
    private const string DefaultStyle = "hvl-default";

    public static void WriteNew(string destination, IReadOnlyList<UdfParagraph> paragraphs,
        PageLayoutSettings layout) => WriteNew(destination, paragraphs, layout, null);

    public static void WriteNew(string destination, IReadOnlyList<UdfParagraph> paragraphs,
        PageLayoutSettings layout, UdfTable? trailingTable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(layout);
        string target = Path.GetFullPath(destination);
        if (!Path.GetExtension(target).Equals(".udf", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(target).Contains(':'))
            throw new NotSupportedException("The output must be a new .udf file.");

        layout.Validate();
        if (layout.Paper.Id != "A4" || layout.Paper.WidthMm != 210m || layout.Paper.HeightMm != 297m ||
            layout.Landscape)
            throw new NotSupportedException("This UDF profile supports A4 portrait only.");

        if (paragraphs.Count is < 1 or > MaximumParagraphs)
            throw new ArgumentOutOfRangeException(nameof(paragraphs));
        var pool = new StringBuilder();
        var snapshot = paragraphs.Select(p => AddToPool(pool, p)).ToArray();
        var table = trailingTable is null ? null : CaptureTable(pool, trailingTable);

        if (File.Exists(target)) throw new IOException("The destination already exists.");
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Guid.NewGuid():N}.enote-udf-tmp");
        bool ownsTemporary = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                using (var content = archive.CreateEntry("content.xml", CompressionLevel.Optimal).Open())
                using (var xml = XmlWriter.Create(content, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                    WriteContent(xml, pool.ToString(), snapshot, layout, table);

                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: false);
            ownsTemporary = false;
        }
        finally
        {
            if (ownsTemporary) File.Delete(temporary);
        }
    }

    private static UdfParagraph AddToPool(StringBuilder pool, UdfParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(paragraph.Text);
        if (paragraph.Text.Length >= MaximumTextCharacters - pool.Length)
            throw new ArgumentOutOfRangeException(nameof(paragraph), "UDF text budget exceeded.");
        if (paragraph.Text.AsSpan().IndexOfAny("\n\r\t\uFFFC") >= 0)
            throw new ArgumentException("Unsupported paragraph character.", nameof(paragraph));
        try { XmlConvert.VerifyXmlChars(paragraph.Text); }
        catch (XmlException error) { throw new ArgumentException("Invalid XML text.", nameof(paragraph), error); }
        // UYAP Editor 5.4.20 rejects supplementary characters in the CDATA text pool.
        if (paragraph.Text.Any(char.IsSurrogate))
            throw new ArgumentException("UYAP 5.4.20 cannot open supplementary Unicode in UDF text.", nameof(paragraph));

        if (paragraph.FontFamily is { } family && (family.Length is < 1 or > 80 ||
            string.IsNullOrWhiteSpace(family) || family.Any(character => !char.IsLetterOrDigit(character) &&
                character is not (' ' or '-' or '_'))))
            throw new ArgumentException("Invalid font family.", nameof(paragraph));
        if (paragraph.FontSizePoints is < 6 or > 72)
            throw new ArgumentOutOfRangeException(nameof(paragraph), "Font size must be 6–72 points.");

        pool.Append(paragraph.Text).Append('\n');
        return paragraph;
    }

    private static UdfParagraph[][] CaptureTable(StringBuilder pool, UdfTable table)
    {
        ArgumentNullException.ThrowIfNull(table.Rows);
        if (table.Rows.Count is < 1 or > MaximumTableRows)
            throw new ArgumentOutOfRangeException(nameof(table));
        int columns = table.Rows[0]?.Count ?? 0;
        if (columns is < 1 or > MaximumTableColumns)
            throw new ArgumentOutOfRangeException(nameof(table));

        var rows = new UdfParagraph[table.Rows.Count][];
        for (int i = 0; i < rows.Length; i++)
        {
            var row = table.Rows[i];
            if (row is null || row.Count != columns)
                throw new ArgumentException("The table must be rectangular.", nameof(table));
            rows[i] = new UdfParagraph[columns];
            for (int j = 0; j < columns; j++)
            {
                var cell = row[j];
                rows[i][j] = AddToPool(pool, cell is { Text.Length: 0 } ? cell with { Text = " " } : cell);
            }
        }
        AddToPool(pool, new UdfParagraph(""));
        return rows;
    }

    private static void WriteContent(XmlWriter xml, string pool, UdfParagraph[] paragraphs,
        PageLayoutSettings layout, UdfParagraph[][]? table)
    {
        xml.WriteStartDocument();
        xml.WriteStartElement("template");
        xml.WriteAttributeString("format_id", "1.8");
        xml.WriteStartElement("content");
        xml.WriteCData(pool);
        xml.WriteEndElement();

        xml.WriteStartElement("properties");
        xml.WriteStartElement("pageFormat");
        xml.WriteAttributeString("mediaSizeName", "1");
        xml.WriteAttributeString("paperOrientation", "1");
        xml.WriteAttributeString("leftMargin", Points(layout.LeftMarginMm));
        xml.WriteAttributeString("rightMargin", Points(layout.RightMarginMm));
        xml.WriteAttributeString("topMargin", Points(layout.TopMarginMm));
        xml.WriteAttributeString("bottomMargin", Points(layout.BottomMarginMm));
        xml.WriteEndElement();
        xml.WriteEndElement();

        xml.WriteStartElement("elements");
        xml.WriteAttributeString("resolver", DefaultStyle);
        int offset = 0;
        foreach (var paragraph in paragraphs)
            offset = WriteParagraph(xml, paragraph, offset);
        if (table is not null) WriteTable(xml, table, offset);
        xml.WriteEndElement();

        xml.WriteStartElement("styles");
        xml.WriteStartElement("style");
        xml.WriteAttributeString("name", DefaultStyle);
        xml.WriteAttributeString("family", "Times New Roman");
        xml.WriteAttributeString("size", "12");
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    private static void WriteTable(XmlWriter xml, UdfParagraph[][] rows, int offset)
    {
        int columns = rows[0].Length;
        xml.WriteStartElement("table");
        xml.WriteAttributeString("tableName", "Sabit");
        xml.WriteAttributeString("columnCount", XmlConvert.ToString(columns));
        xml.WriteAttributeString("columnSpans", string.Join(",", Enumerable.Repeat("100", columns)));
        xml.WriteAttributeString("border", "borderCell");
        for (int row = 0; row < rows.Length; row++)
        {
            xml.WriteStartElement("row");
            xml.WriteAttributeString("rowName", "row" + XmlConvert.ToString(row + 1));
            xml.WriteAttributeString("rowType", "dataRow");
            foreach (var cell in rows[row])
            {
                xml.WriteStartElement("cell");
                offset = WriteParagraph(xml, cell, offset);
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        // The desktop editor expects a body paragraph after the last table.
        WriteParagraph(xml, new UdfParagraph(""), offset);
    }

    private static int WriteParagraph(XmlWriter xml, UdfParagraph paragraph, int offset)
    {
        int length = paragraph.Text.Length + 1;
        xml.WriteStartElement("paragraph");
        xml.WriteAttributeString("Alignment", "0");
        xml.WriteStartElement("content");
        xml.WriteAttributeString("startOffset", XmlConvert.ToString(offset));
        xml.WriteAttributeString("length", XmlConvert.ToString(length));
        if (paragraph.Bold) xml.WriteAttributeString("bold", "true");
        if (paragraph.FontFamily is { } family) xml.WriteAttributeString("family", family);
        if (paragraph.FontSizePoints is { } size) xml.WriteAttributeString("size", XmlConvert.ToString(size));
        xml.WriteEndElement();
        xml.WriteEndElement();
        return offset + length;
    }

    private static string Points(decimal millimetres) =>
        (millimetres * 72m / 25.4m).ToString("0.00", CultureInfo.InvariantCulture);
}
