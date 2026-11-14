using Embasic.ENote.Core.Markdown;
using Embasic.ENote.Core.Printing;
using System.Windows;
using System.Windows.Documents;

namespace Embasic.ENote.App.Preview;

internal static class A4MarkdownPagination
{
    public static DynamicDocumentPaginator Create(string markdown, PageLayoutSettings layout)
    {
        if (layout.Paper != PageLayoutCatalog.Get("A4"))
            throw new NotSupportedException("Bu önizleme A4 kâğıdı destekler.");
        layout.Validate();

        var parsed = MarkdownPreviewParser.Parse(markdown);
        if (parsed.IsTruncated)
            throw new NotSupportedException("Not, baskı önizlemesi için desteklenen metin sınırını aşıyor.");

        var document = MarkdownPreviewDocumentFactory.Create(parsed);
        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());

        document.PageWidth = ToDip(layout.WidthMm);
        document.PageHeight = ToDip(layout.HeightMm);
        document.PagePadding = new Thickness(ToDip(layout.LeftMarginMm), ToDip(layout.TopMarginMm),
            ToDip(layout.RightMarginMm), ToDip(layout.BottomMarginMm));

        var paginator = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.IsBackgroundPaginationEnabled = false;
        paginator.PageSize = new Size(document.PageWidth, document.PageHeight);
        paginator.ComputePageCount();
        return paginator;
    }

    private static double ToDip(decimal mm) => (double)(mm * 96m / 25.4m);
}
