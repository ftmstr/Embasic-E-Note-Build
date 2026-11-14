using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Embasic.ENote.Core.Printing;

namespace Embasic.ENote.Export.UdfWriter;

public sealed record UdfParagraph(string Text, bool Bold = false);

/// <summary>Experimental, unsigned UDF 1.8 copy export for explicitly supplied paragraphs.</summary>
public static class UnsignedUdfWriter
{
    public const int MaximumParagraphs = 3_000;
    public const int MaximumTextCharacters = 200_000;
    private const string DefaultStyle = "hvl-default";

    public static void WriteNew(string destination, IReadOnlyList<UdfParagraph> paragraphs,
        PageLayoutSettings layout)
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
        var snapshot = new UdfParagraph[paragraphs.Count];
        var pool = new StringBuilder();
        for (int i = 0; i < snapshot.Length; i++)
        {
            var paragraph = paragraphs[i] ?? throw new ArgumentException("Missing paragraph.", nameof(paragraphs));
            ArgumentNullException.ThrowIfNull(paragraph.Text);
            if (paragraph.Text.Length >= MaximumTextCharacters - pool.Length)
                throw new ArgumentOutOfRangeException(nameof(paragraphs), "UDF text budget exceeded.");
            foreach (char character in paragraph.Text)
            {
                // Offset semantics outside the BMP need a reference-editor round trip.
                if (character is '\n' or '\r' or '\t' or '\uFFFC' || !XmlConvert.IsXmlChar(character))
                    throw new ArgumentException("Unsupported paragraph character.", nameof(paragraphs));
            }
            snapshot[i] = paragraph;
            pool.Append(paragraph.Text).Append('\n');
        }

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
                    WriteContent(xml, pool.ToString(), snapshot, layout);

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

    private static void WriteContent(XmlWriter xml, string pool, UdfParagraph[] paragraphs, PageLayoutSettings layout)
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
        {
            int length = paragraph.Text.Length + 1;
            xml.WriteStartElement("paragraph");
            xml.WriteAttributeString("Alignment", "0");
            xml.WriteStartElement("content");
            xml.WriteAttributeString("startOffset", XmlConvert.ToString(offset));
            xml.WriteAttributeString("length", XmlConvert.ToString(length));
            if (paragraph.Bold) xml.WriteAttributeString("bold", "true");
            xml.WriteEndElement();
            xml.WriteEndElement();
            offset += length;
        }
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

    private static string Points(decimal millimetres) =>
        (millimetres * 72m / 25.4m).ToString("0.00", CultureInfo.InvariantCulture);
}
