using Embasic.ENote.App.Preview;
using Embasic.ENote.App.Printing;
using System.ComponentModel;
using System.IO;
using System.Printing;
using Embasic.ENote.Core.Printing;
using System.Windows;
using System.Windows.Documents;

namespace Embasic.ENote.App;

internal sealed partial class A4PrintPreviewWindow : Window
{
    private readonly DocumentPaginator _paginator;
    private readonly string _markdown;
    private readonly PageLayoutSettings _layout;

    public A4PrintPreviewWindow(string markdown, PageLayoutSettings layout)
    {
        _markdown = markdown;
        _layout = layout;
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

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        int current = PreviewPage.PageNumber + 1;
        PrintStatus.Text = "";
        try
        {
            var dialog = LocalA4Print.CreateDialog(_paginator.PageCount, current);
            bool submitted = LocalA4Print.ShowAndSubmit(dialog, current, _layout.Landscape,
                () => A4MarkdownPagination.Create(_markdown, _layout));
            PrintStatus.Text = submitted
                ? "Yazdırma işi Windows kuyruğuna gönderildi."
                : "Yazdırma iptal edildi.";
        }
        catch (Exception error) when (error is PrintSystemException or Win32Exception or IOException or
                                     UnauthorizedAccessException or ArgumentException or InvalidOperationException or
                                     NotSupportedException)
        {
            PrintStatus.Text = "Yazdırma tamamlanamadı: " + error.Message;
        }
    }

    private void ShowPage(int index)
    {
        PreviewPage.PageNumber = index;
        PreviousPageButton.IsEnabled = index > 0;
        NextPageButton.IsEnabled = index + 1 < _paginator.PageCount;
        PageCounter.Text = $"Sayfa {index + 1} / {_paginator.PageCount}";
    }
}
