namespace Embasic.ENote.Core.Markdown;

/// <summary>Offline, display-only parser. No HTML, scripts, links or network IO.
/// Source Markdown is never rewritten; this is NOT a WYSIWYG editor.</summary>
public static class MarkdownPreviewParser
{
    public const int MaximumPreviewCharacters = 200_000;
    public const int MaximumPreviewLines = 3_000;

    public static MarkdownPreviewResult Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        bool clipped = markdown.Length > MaximumPreviewCharacters;
        string source = clipped ? markdown[..MaximumPreviewCharacters] : markdown;
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int limit = Math.Min(lines.Length, MaximumPreviewLines);
        clipped |= lines.Length > limit;
        var blocks = new List<MarkdownPreviewBlock>();
        for (int i = 0; i < limit;)
        {
            string line = lines[i], trimmed = line.TrimStart();
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }
            if (Fence(trimmed, out string mark))
            {
                var collected = new List<string>();
                i++;
                while (i < limit && !lines[i].TrimStart().StartsWith(mark, StringComparison.Ordinal))
                    collected.Add(lines[i++]);
                if (i < limit) i++;
                blocks.Add(new(MarkdownBlockKind.Code, string.Join("\n", collected)));
                continue;
            }
            if (Heading(trimmed, out int level, out string title))
            {
                blocks.Add(new(MarkdownBlockKind.Heading, title, level));
                i++; continue;
            }
            if (Rule(trimmed))
            {
                blocks.Add(new(MarkdownBlockKind.Rule, ""));
                i++; continue;
            }
            if (trimmed.StartsWith('>'))
            {
                var quote = new List<string>();
                while (i < limit && lines[i].TrimStart().StartsWith('>'))
                {
                    string value = lines[i++].TrimStart()[1..];
                    quote.Add(value.StartsWith(' ') ? value[1..] : value);
                }
                blocks.Add(new(MarkdownBlockKind.Quote, string.Join("\n", quote)));
                continue;
            }
            if (i + 1 < limit && Separator(lines[i+1], out int count))
            {
                var headers = Cells(line);
                if (headers.Count == count)
                {
                    var rows = new List<string> { string.Join("\t", headers) };
                    i += 2;
                    while (i < limit && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Contains('|'))
                    {
                        var cells = Cells(lines[i++]);
                        while (cells.Count < count) cells.Add("");
                        rows.Add(string.Join("\t", cells.Take(count)));
                    }
                    blocks.Add(new(MarkdownBlockKind.Table, "", 0, rows));
                    continue;
                }
            }
            if (Item(trimmed, out bool numbered, out _))
            {
                var items = new List<string>();
                while (i < limit && Item(lines[i].TrimStart(), out bool nextNumbered, out string item)
                    && nextNumbered == numbered)
                { items.Add(item); i++; }
                blocks.Add(new(numbered ? MarkdownBlockKind.NumberedList : MarkdownBlockKind.BulletList,
                    "", 0, items));
                continue;
            }
            var paragraph = new List<string> { line.Trim() };
            i++;
            while (i < limit && !string.IsNullOrWhiteSpace(lines[i]) &&
                   !BlockStart(lines[i].TrimStart()))
                paragraph.Add(lines[i++].Trim());
            blocks.Add(new(MarkdownBlockKind.Paragraph, string.Join("\n", paragraph)));
        }
        if (clipped) blocks.Add(new(MarkdownBlockKind.Notice,
            "Önizleme sınırına ulaşıldı; kaynak Markdown metni değişmedi."));
        return new MarkdownPreviewResult(blocks.AsReadOnly(), clipped);
    }
    private static bool BlockStart(string line) =>
        Fence(line, out _) || Heading(line, out _, out _) ||
        line.StartsWith('>') || Rule(line) || Item(line, out _, out _);
    private static bool Fence(string line, out string token)
    {
        token = line.StartsWith("\u0060\u0060\u0060", StringComparison.Ordinal)
            ? "\u0060\u0060\u0060"
            : line.StartsWith("~~~", StringComparison.Ordinal) ? "~~~" : "";
        return token.Length != 0;
    }
    private static bool Heading(string line, out int level, out string title)
    {
        level = 0; title = "";
        while (level < line.Length && level < 7 && line[level] == '#') level++;
        if (level is < 1 or > 6 || line.Length <= level || line[level] != ' ') return false;
        title = line[(level + 1)..].Trim();
        return true;
    }
    private static bool Rule(string line)
    {
        string x = line.Replace(" ", "");
        return x.Length >= 3 && (x.All(c => c == '-') ||
            x.All(c => c == '*') || x.All(c => c == '_'));
    }
    private static bool Item(string line, out bool numbered, out string text)
    {
        numbered = false; text = "";
        if (line.Length > 2 && line[0] is '-' or '*' or '+' && line[1] == ' ')
        { text = line[2..]; return true; }
        int n = 0;
        while (n < line.Length && n < 10 && char.IsAsciiDigit(line[n])) n++;
        if (n > 0 && line.Length > n+1 && (line[n] is '.' or ')') && line[n+1] == ' ')
        { numbered = true; text = line[(n+2)..]; return true; }
        return false;
    }
    private static List<string> Cells(string row)
    {
        string text = row.Trim();
        if (text.StartsWith('|')) text = text[1..];
        if (text.EndsWith('|') && (text.Length < 2 || text[^2] != '\\'))
            text = text[..^1];
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        bool inCode = false;
        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current == '\\' && i + 1 < text.Length && text[i + 1] == '|')
            {
                cell.Append('|');
                i++;
                continue;
            }
            if (current == '\u0060')
            {
                inCode = !inCode;
                cell.Append(current);
                continue;
            }
            if (current == '|' && !inCode)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else cell.Append(current);
        }
        cells.Add(cell.ToString().Trim());
        return cells;
    }
    private static bool Separator(string line, out int count)
    {
        count = 0;
        if (!line.Contains('|')) return false;
        var pieces = Cells(line);
        if (pieces.Count < 2) return false;
        foreach (string part in pieces)
        {
            string dashes = part.Trim(':');
            if (dashes.Length < 3 || dashes.Any(c => c != '-')) return false;
        }
        count = pieces.Count;
        return true;
    }
}
public enum MarkdownBlockKind { Heading, Paragraph, Quote, BulletList, NumberedList, Code, Table, Rule, Notice }
public sealed record MarkdownPreviewBlock(MarkdownBlockKind Kind, string Text,
    int HeadingLevel = 0, IReadOnlyList<string>? Rows = null);
public sealed record MarkdownPreviewResult(IReadOnlyList<MarkdownPreviewBlock> Blocks, bool IsTruncated);
