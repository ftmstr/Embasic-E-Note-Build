namespace Embasic.ENote.Core.Markdown;

/// <summary>
/// A bounded, inert subset for nested Markdown preview. It creates only
/// literal text nodes and styling metadata. No URL/HTML/image execution.
/// Unsupported or excessive nesting is shown as literal text, never rewritten.
/// </summary>
public static class MarkdownNestedInlineParser
{
    public const int MaximumDepth = 8;
    public const int MaximumNodes = 4096;

    public static IReadOnlyList<MarkdownInlineNode> Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MarkdownPreviewParser.MaximumPreviewCharacters)
            throw new ArgumentOutOfRangeException(nameof(source), "Preview length exceeded.");
        int remaining = MaximumNodes;
        return Level(source, 0, source.Length, 0, ref remaining).AsReadOnly();
    }

    private static List<MarkdownInlineNode> Level(string source, int begin, int end,
        int depth, ref int remaining)
    {
        var nodes = new List<MarkdownInlineNode>();
        var plain = new System.Text.StringBuilder();
        void Flush()
        {
            if (plain.Length == 0) return;
            nodes.Add(new MarkdownInlineNode(MarkdownInlineKind.Text, plain.ToString()));
            plain.Clear();
            remaining--;
        }
        if (depth >= MaximumDepth || remaining <= 1)
        {
            nodes.Add(new MarkdownInlineNode(MarkdownInlineKind.Text, source[begin..end]));
            return nodes;
        }

        for (int i = begin; i < end;)
        {
            if (remaining <= 2)
            {
                plain.Append(source.AsSpan(i, end - i));
                break;
            }
            if (source[i] == '\\' && i + 1 < end && Escapable(source[i+1]))
            {
                plain.Append(source[i+1]);
                i += 2;
                continue;
            }
            string marker = "";
            MarkdownInlineKind kind = MarkdownInlineKind.Text;
            if (source[i] == '\u0060')
            {
                int run = 0;
                while (i + run < end && source[i+run] == '\u0060' && run < 16) run++;
                marker = new string('\u0060', run);
                kind = MarkdownInlineKind.Code;
            }
            else if (Match(source, i, end, "**"))
            { marker = "**"; kind = MarkdownInlineKind.Strong; }
            else if (Match(source, i, end, "~~"))
            { marker = "~~"; kind = MarkdownInlineKind.Strikethrough; }
            else if (source[i] == '*' || (source[i] == '_' &&
                     (i == begin || !char.IsLetterOrDigit(source[i-1]))))
            { marker = source[i].ToString(); kind = MarkdownInlineKind.Emphasis; }

            if (marker.Length > 0)
            {
                int start = i + marker.Length;
                int close = FindClose(source, marker, start, end);
                if (close > start && source[start] != ' ' && source[close-1] != ' ' &&
                    !source.AsSpan(start, close-start).Contains('\n'))
                {
                    Flush();
                    string raw = source[start..close];
                    IReadOnlyList<MarkdownInlineNode>? children = null;
                    if (kind != MarkdownInlineKind.Code)
                        children = Level(source, start, close, depth + 1, ref remaining).AsReadOnly();
                    nodes.Add(new MarkdownInlineNode(kind, raw, children));
                    remaining--;
                    i = close + marker.Length;
                    continue;
                }
            }
            plain.Append(source[i++]);
        }
        Flush();
        return nodes;
    }

    private static int FindClose(string source, string marker, int start, int end)
    {
        for (int i = start; i + marker.Length <= end; i++)
        {
            if (source[i] == '\\' && i + 1 < end && Escapable(source[i+1]))
            { i++; continue; }
            if (!Match(source, i, end, marker)) continue;
            if (marker[0] == '\u0060' &&
                ((i > 0 && source[i-1] == '\u0060') ||
                 (i + marker.Length < end && source[i+marker.Length] == '\u0060')))
                continue;
            if (marker == "_" && i + 1 < end &&
                char.IsLetterOrDigit(source[i+1])) continue;
            return i;
        }
        return -1;
    }

    private static bool Match(string source, int index, int end, string marker) =>
        index + marker.Length <= end &&
        source.AsSpan(index, marker.Length).SequenceEqual(marker.AsSpan());

    private static bool Escapable(char c) => c is '\\' or '*' or '_' or '~' or
        '\u0060' or '[' or ']' or '(' or ')' or '!' or '|' or '#' or
        '{' or '}' or '<' or '>';
}
public sealed record MarkdownInlineNode(MarkdownInlineKind Kind, string Text,
    IReadOnlyList<MarkdownInlineNode>? Children = null);
