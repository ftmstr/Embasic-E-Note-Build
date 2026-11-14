using Embasic.ENote.Core.Printing;

namespace Embasic.ENote.Export.UdfWriter;

/// <summary>Exports the current plaintext Markdown source as literal editable UDF paragraphs.</summary>
public static class MarkdownUdfCopy
{
    public static void WriteNew(string sourceMarkdownPath, string markdown, string destination,
        PageLayoutSettings layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMarkdownPath);
        ArgumentNullException.ThrowIfNull(markdown);
        if (!Path.GetExtension(sourceMarkdownPath).Equals(".md", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Yalnız açık düz Markdown (.md) notu aktarılabilir.");
        if (markdown.Length >= UnsignedUdfWriter.MaximumTextCharacters)
            throw new ArgumentOutOfRangeException(nameof(markdown), "UDF metin sınırı aşıldı.");

        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        // The UDF writer adds a separator after each paragraph, including the last one.
        if (normalized.EndsWith('\n')) normalized = normalized[..^1];
        var paragraphs = normalized.Split('\n').Select(line => new UdfParagraph(line)).ToArray();
        UnsignedUdfWriter.WriteNew(destination, paragraphs, layout);
    }
}
