using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Embasic.ENote.App.Printing;

internal static class LocalA4Print
{
    public static PrintDialog CreateDialog(int pageCount, int currentPage) => new()
    {
        MinPage = 1,
        MaxPage = (uint)pageCount,
        UserPageRangeEnabled = true,
        CurrentPageEnabled = true,
        PageRange = new PageRange(currentPage),
        PageRangeSelection = PageRangeSelection.AllPages
    };

    public static void Submit(PrintDialog dialog, DocumentPaginator source, int currentPage, bool landscape)
    {
        using var queue = dialog.PrintQueue
            ?? throw new InvalidOperationException("Yerel bir Windows yazıcı kuyruğu seçin.");
        using var server = new LocalPrintServer();
        using var localQueues = server.GetPrintQueues([EnumeratedPrintQueueTypes.Local]);
        if (!localQueues.Any(candidate => string.Equals(candidate.FullName, queue.FullName, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Yazdırma için Windows’a yerel olarak kurulu bir kuyruk seçin.");

        var pages = SelectPages(source, dialog.PageRangeSelection, dialog.PageRange, currentPage);
        var selected = dialog.PrintTicket;
        var orientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        var requested = new PrintTicket
        {
            PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4),
            PageOrientation = orientation
        };
        var ticket = queue.MergeAndValidatePrintTicket(selected, requested).ValidatedPrintTicket;
        var area = queue.GetPrintCapabilities(ticket).PageImageableArea;
        if (area is null || !CanPrint(ticket, orientation,
            new Rect(area.OriginWidth, area.OriginHeight, area.ExtentWidth, area.ExtentHeight), source))
            throw new NotSupportedException("Seçilen sürücü A4 yönünü veya belgenin yazdırılabilir alanını desteklemiyor.");

        dialog.PrintTicket = ticket;
        dialog.PrintDocument(pages, "Embasic E-Note — A4 Markdown");
    }

    internal static DocumentPaginator SelectPages(DocumentPaginator source, PageRangeSelection selection,
        PageRange range, int currentPage)
    {
        if (!source.IsPageCountValid || source.PageCount < 1)
            throw new InvalidOperationException("Sayfalama tamamlanmadı.");
        if (selection == PageRangeSelection.AllPages) return source;
        int first, last;
        switch (selection)
        {
            case PageRangeSelection.UserPages:
                first = range.PageFrom;
                last = range.PageTo;
                break;
            case PageRangeSelection.CurrentPage:
                first = last = currentPage;
                break;
            default:
                throw new NotSupportedException("Bu sayfa seçimi desteklenmiyor.");
        }
        if (first < 1 || last < first || last > source.PageCount)
            throw new ArgumentOutOfRangeException(nameof(range), "Sayfa aralığı önizlemedeki belgeye uymuyor.");

        return new PageRangePaginator(source, first - 1, last - first + 1);
    }

    internal static bool CanPrint(PrintTicket ticket, PageOrientation orientation, Rect area, DocumentPaginator source)
    {
        if (ticket.PageMediaSize?.PageMediaSizeName != PageMediaSizeName.ISOA4 ||
            ticket.PageOrientation != orientation)
            return false;
        using var page = source.GetPage(0);
        return area.Contains(page.ContentBox);
    }

    private sealed class PageRangePaginator(DocumentPaginator source, int offset, int count) : DocumentPaginator
    {
        public override int PageCount => count;
        public override bool IsPageCountValid => true;
        public override IDocumentPaginatorSource Source => source.Source;
        public override Size PageSize
        {
            get => source.PageSize;
            set
            {
                if (value != source.PageSize)
                    throw new NotSupportedException("Yazdırma sırasında A4 sayfa boyutu değiştirilemez.");
            }
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(pageNumber);
            return pageNumber < count ? source.GetPage(offset + pageNumber) : DocumentPage.Missing;
        }
    }
}
