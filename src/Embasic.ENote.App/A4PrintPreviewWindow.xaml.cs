using Embasic.ENote.App.Preview;
using Embasic.ENote.Core.Printing;
using System.Windows;
using System.Windows.Documents;

namespace Embasic.ENote.App;

internal sealed partial class A4PrintPreviewWindow : Window
{
    private readonly DocumentPaginator _paginator;

    public A4PrintPreviewWindow(string markdown, PageLayoutSettings layout)
    {
        _paginator = A4MarkdownPagination.Create(markdown, layout);
        InitializeComponent();
        PreviewPage.Width = _paginator.PageSize.Width;
        PreviewPage.Height = _paginator.PageSize.Height;
        PreviewPage.DocumentPaginator = _paginator;
        PrintGeometry.Text = $"A4 · {(layout.Landscape ? "Yatay" : "Dikey")} · " +
            $"{layout.WidthMm} × {layout.HeightMm} mm · " +
            $"Kenar boşlukları (sol, sağ, üst, alt) {layout.LeftMarginMm}, " +
            $"{layout.RightMarginMm}, {layout.TopMarginMm}, {layout.BottomMarginMm} mm";
        ShowPage(0);
    }

    protected override void OnClosed(EventArgs e)
    {
        ((IDisposable)PreviewPage).Dispose();
        base.OnClosed(e);
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e) => ShowPage(PreviewPage.PageNumber - 1);
    private void NextPage_Click(object sender, RoutedEventArgs e) => ShowPage(PreviewPage.PageNumber + 1);

    private void ShowPage(int index)
    {
        PreviewPage.PageNumber = index;
        PreviousPageButton.IsEnabled = index > 0;
        NextPageButton.IsEnabled = index + 1 < _paginator.PageCount;
        PageCounter.Text = $"Sayfa {index + 1} / {_paginator.PageCount}";
    }
}
