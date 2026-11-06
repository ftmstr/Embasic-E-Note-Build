using System.Text;

namespace Embasic.ENote.Core.Markdown;

/// <summary>
/// Small, conservative inline preview tokenizer. It returns inert text spans,
/// never HTML, URLs, external images, embedded controls or executable content.
/// It does NOT rewrite or interpret the original Markdown file.
/// Deliberately not a full CommonMark implementation.
/// </summary>
public static class MarkdownInlineParser
{
    public static IReadOnlyList<MarkdownInlineSpan> Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MarkdownPreviewParser.MaximumPreviewCharacters)
            throw new ArgumentOutOfRangeException(nameof(source), "Preview input too large.");
        var result = new List<MarkdownInlineSpan>();
        var plain = new StringBuilder();
        void Flush()
        {
            if (plain.Length == 0) return;
            result.Add(new MarkdownInlineSpan(MarkdownInlineKind.Text, plain.ToString()));
            plain.Clear();
        }

        for (int i = 0; i < source.Length;)
        {
            char c = source[i];
            if (c == '\\' && i + 1 < source.Length && Escapable(source[i + 1]))
            {
                plain.Append(source[i + 1]);
                i += 2;
                continue;
            }

            string? marker = null;
            MarkdownInlineKind kind = MarkdownInlineKind.Text;
            if (c == '\u0060') { marker = "\u0060"; kind = MarkdownInlineKind.Code; }
            else if (i + 1 < source.Length && source[i] == '*' && source[i + 1] == '*')
            { marker = "**"; kind = MarkdownInlineKind.Strong; }
            else if (i + 1 < source.Length && source[i] == '~' && source[i + 1] == '~')
            { marker = "~~"; kind = MarkdownInlineKind.Strikethrough; }
            else if (c == '*' || (c == '_' && (i == 0 || !char.IsLetterOrDigit(source[i - 1]))))
            { marker = c.ToString(); kind = MarkdownInlineKind.Emphasis; }

            if (marker is not null)
            {
                int start = i + marker.Length;
                int end = FindClosing(source, marker, start);
                if (end > start && source[start] != ' ' && source[end - 1] != ' ' &&
                    !source.AsSpan(start, end - start).Contains('\n'))
                {
                    Flush();
                    string inner = source[start..end];
                    result.Add(new MarkdownInlineSpan(kind, kind == MarkdownInlineKind.Code
                        ? inner : Unescape(inner)));
                    i = end + marker.Length;
                    continue;
                }
            }

            plain.Append(c);
            i++;
        }
        Flush();
        return result.AsReadOnly();
    }

    private static int FindClosing(string value, string marker, int start)
    {
        for (int i = start; i + marker.Length <= value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && Escapable(value[i + 1]))
            { i++; continue; }
            if (!value.AsSpan(i).StartsWith(marker.AsSpan(), StringComparison.Ordinal)) continue;
            if (marker == "_" && i + 1 < value.Length && char.IsLetterOrDigit(value[i + 1]))
                continue;
            return i;
        }
        return -1;
    }

    private static string Unescape(string value)
    {
        var output = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && Escapable(value[i + 1]))
                i++;
            output.Append(value[i]);
        }
        return output.ToString();
    }

    private static bool Escapable(char c) =>
        c is '\\' or '*' or '_' or '~' or '\u0060' or '[' or ']' or
            '(' or ')' or '!' or '|' or '#' or '{' or '}' or '<' or '>';
}

public enum MarkdownInlineKind { Text, Strong, Emphasis, Strikethrough, Code, StrongEmphasis }
public sealed record MarkdownInlineSpan(MarkdownInlineKind Kind, string Text);
