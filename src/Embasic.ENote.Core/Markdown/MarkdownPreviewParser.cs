namespace Embasic.ENote.Core.Markdown;

/// <summary>Offline, display-only parser. No HTML, scripts, links or network IO.
/// Source Markdown is never rewritten; this is NOT a WYSIWYG editor.</summary>
public static class MarkdownPreviewParser
{
    public const int MaximumPreviewCharacters = 200_000;
    public const int MaximumPreviewLines = 3_000;
    public const int MaximumPreviewTableColumns = 32;
    public const int MaximumPreviewTableRows = 256;

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
            if (i + 1 < limit && BalancedCodeSpans(line) &&
                Separator(lines[i+1], out int count))
            {
                var headers = Cells(line);
                if (headers.Count == count && count <= MaximumPreviewTableColumns)
                {
                    var rows = new List<string> { string.Join("\t", headers) };
                    var parsedCells = new List<IReadOnlyList<string>> { headers.AsReadOnly() };
                    int sourceStart = i;
                    bool valid = true;
                    i += 2;
                    while (i < limit && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Contains('|'))
                    {
                        string rawRow = lines[i++];
                        var cells = Cells(rawRow);
                        // Fail to literal preview on unclosed code spans and
                        // oversized tables; never silently absorb a delimiter.
                        if (!BalancedCodeSpans(rawRow) ||
                            rows.Count >= MaximumPreviewTableRows)
                            valid = false;
                        // Never silently discard extra columns: malformed tables
                        // fall back to literal text with their content preserved.
                        if (cells.Count > count) valid = false;
                        while (cells.Count < count) cells.Add("");
                        parsedCells.Add(cells.AsReadOnly());
                        rows.Add(string.Join("\t", cells));
                    }
                    if (valid)
                        blocks.Add(new(MarkdownBlockKind.Table, "", 0, rows, parsedCells));
                    else
                        blocks.Add(new(MarkdownBlockKind.Paragraph,
                            string.Join("\n", lines[sourceStart..i])));
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
        string input = row.Trim();
        if (input.StartsWith('|')) input = input[1..];
        if (input.EndsWith('|'))
        {
            int slashRun = 0;
            for (int p = input.Length - 2; p >= 0 && input[p] == '\\'; p--)
                slashRun++;
            // A trailing pipe is a table frame only when unescaped.
            if ((slashRun & 1) == 0) input = input[..^1];
        }
        var result = new List<string>();
        var cell = new System.Text.StringBuilder();
        int codeRun = 0;
        for (int i = 0; i < input.Length;)
        {
            if (input[i] == '\\')
            {
                int start = i;
                while (i < input.Length && input[i] == '\\') i++;
                int count = i-start;
                if (i < input.Length && input[i] == '|')
                {
                    // Odd parity escapes the pipe, even parity leaves a delimiter.
                    cell.Append('\\', count/2);
                    if (count % 2 == 1) { cell.Append('|'); i++; }
                    else if (codeRun != 0) { cell.Append('|'); i++; }
                    else { result.Add(cell.ToString().Trim()); cell.Clear(); i++; }
                }
                else if (i < input.Length && input[i] == '\u0060' &&
                         (count & 1) == 1)
                {
                    // Escaped backtick is literal, not a code-span toggle.
                    cell.Append('\\', count / 2);
                    cell.Append('\u0060');
                    i++;
                }
                else cell.Append('\\', count);
                continue;
            }
            if (input[i] == '\u0060')
            {
                int length=0;
                while (i+length < input.Length && input[i+length] == '\u0060')
                    length++;
                if (codeRun == 0) codeRun = length;
                else if (codeRun == length) codeRun = 0;
                cell.Append('\u0060', length);
                i += length;
                continue;
            }
            if (input[i] == '|' && codeRun == 0)
            {
                result.Add(cell.ToString().Trim());
                cell.Clear();
                i++;
                continue;
            }
            cell.Append(input[i++]);
        }
        result.Add(cell.ToString().Trim());
        return result;
    }
    private static bool BalancedCodeSpans(string source)
    {
        int openRun = 0;
        for (int i = 0; i < source.Length;)
        {
            if (source[i] == '\\')
            {
                int start = i;
                while (i < source.Length && source[i] == '\\') i++;
                if (i < source.Length && source[i] == '\u0060' &&
                    ((i - start) & 1) == 1)
                    i++; // escaped backtick; not a delimiter
                continue;
            }
            if (source[i] == '\u0060')
            {
                int run = 0;
                while (i + run < source.Length && source[i + run] == '\u0060')
                    run++;
                if (openRun == 0) openRun = run;
                else if (run == openRun) openRun = 0;
                i += run;
                continue;
            }
            i++;
        }
        return openRun == 0;
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
    int HeadingLevel = 0, IReadOnlyList<string>? Rows = null,
    IReadOnlyList<IReadOnlyList<string>>? TableCells = null);
public sealed record MarkdownPreviewResult(IReadOnlyList<MarkdownPreviewBlock> Blocks, bool IsTruncated);
