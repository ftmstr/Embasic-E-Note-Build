using Embasic.ENote.Core.Markdown;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Embasic.ENote.App.Preview;

/// <summary>FlowDocument from literal, inert strings only. No HTML renderer,
/// clickable links, WebBrowser, external image loads, or on-disk changes.</summary>
internal static class MarkdownPreviewDocumentFactory
{
    public static FlowDocument Create(string markdown)
    {
        var parsed = MarkdownPreviewParser.Parse(markdown);
        var document = new FlowDocument
        {
            PagePadding = new Thickness(24),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = Brushes.Black,
            Background = Brushes.White
        };
        foreach (var block in parsed.Blocks)
        {
            switch (block.Kind)
            {
                case MarkdownBlockKind.Heading:
                    int level = Math.Clamp(block.HeadingLevel, 1, 6);
                    var heading = Paragraph(block.Text);
                    heading.FontWeight = FontWeights.SemiBold;
                    heading.FontSize = new double[]{0, 25, 22, 19, 17, 15, 14}[level];
                    heading.Margin = new Thickness(0, 13, 0, 8);
                    document.Blocks.Add(heading);
                    break;
                case MarkdownBlockKind.Paragraph:
                    document.Blocks.Add(Paragraph(block.Text));
                    break;
                case MarkdownBlockKind.Quote:
                    var quote = Paragraph(block.Text);
                    quote.FontStyle = FontStyles.Italic;
                    quote.Foreground = Brushes.DimGray;
                    quote.BorderBrush = Brushes.SteelBlue;
                    quote.BorderThickness = new Thickness(3, 0, 0, 0);
                    quote.Padding = new Thickness(12, 7, 4, 7);
                    quote.Margin = new Thickness(0, 6, 0, 9);
                    document.Blocks.Add(quote);
                    break;
                case MarkdownBlockKind.Code:
                    document.Blocks.Add(new Paragraph(new Run(block.Text))
                    {
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        Background = new SolidColorBrush(Color.FromRgb(241,245,249)),
                        Padding = new Thickness(10),
                        Margin = new Thickness(0, 6, 0, 10)
                    });
                    break;
                case MarkdownBlockKind.BulletList:
                case MarkdownBlockKind.NumberedList:
                    var list = new System.Windows.Documents.List
                    {
                        MarkerStyle = block.Kind == MarkdownBlockKind.BulletList ?
                            TextMarkerStyle.Disc : TextMarkerStyle.Decimal,
                        Margin = new Thickness(14, 5, 0, 10)
                    };
                    foreach (string text in block.Rows ?? Array.Empty<string>())
                        list.ListItems.Add(new ListItem(Paragraph(text)));
                    document.Blocks.Add(list);
                    break;
                case MarkdownBlockKind.Table:
                    var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 10) };
                    var group = new TableRowGroup();
                    table.RowGroups.Add(group);
                    bool first = true;
                    foreach (string rowText in block.Rows ?? Array.Empty<string>())
                    {
                        var row = new TableRow();
                        foreach (string cell in rowText.Split('\t'))
                            row.Cells.Add(new TableCell(Paragraph(cell))
                            {
                                BorderBrush = Brushes.LightGray,
                                BorderThickness = new Thickness(1),
                                Padding = new Thickness(6),
                                Background = first ? Brushes.AliceBlue : Brushes.White
                            });
                        first = false;
                        group.Rows.Add(row);
                    }
                    document.Blocks.Add(table);
                    break;
                case MarkdownBlockKind.Rule:
                    document.Blocks.Add(new Paragraph(new Run("────────────────"))
                    {
                        Foreground = Brushes.LightSlateGray,
                        Margin = new Thickness(0, 7, 0, 11)
                    });
                    break;
                case MarkdownBlockKind.Notice:
                    document.Blocks.Add(new Paragraph(new Run(block.Text))
                    {
                        Foreground = Brushes.DarkGoldenrod,
                        FontStyle = FontStyles.Italic
                    });
                    break;
            }
        }
        return document;
    }

    private static Paragraph Paragraph(string text)
    {
        var result = new Paragraph { Margin = new Thickness(0, 0, 0, 9) };
        foreach (var span in MarkdownInlineParser.Parse(text))
        {
            // Run is inert literal text; never create Hyperlink, UIElement,
            // Image, WebBrowser, executable HTML or remotely fetched content.
            var run = new Run(span.Text);
            switch (span.Kind)
            {
                case MarkdownInlineKind.Strong:
                    run.FontWeight = FontWeights.Bold;
                    break;
                case MarkdownInlineKind.Emphasis:
                    run.FontStyle = FontStyles.Italic;
                    break;
                case MarkdownInlineKind.Strikethrough:
                    run.TextDecorations = TextDecorations.Strikethrough;
                    break;
                case MarkdownInlineKind.Code:
                    run.FontFamily = new FontFamily("Consolas");
                    run.Background = Brushes.AliceBlue;
                    break;
            }
            result.Inlines.Add(run);
        }
        return result;
    }
}
