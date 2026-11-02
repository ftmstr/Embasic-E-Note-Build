namespace Embasic.ENote.Core.Printing;

/// <summary>Paper dimensions in millimetres; independent of DPI, screen size, printer or UI.</summary>
public sealed record PagePaper(string Id, string Name, decimal WidthMm, decimal HeightMm)
{
    public bool IsCustom => Id == "Custom";
}

public static class PageLayoutCatalog
{
    public static IReadOnlyList<PagePaper> Presets { get; } = Array.AsReadOnly(new[]
    {
        new PagePaper("A4", "A4 (210 × 297 mm)", 210m, 297m),
        new PagePaper("A2", "A2 (420 × 594 mm)", 420m, 594m),
        new PagePaper("A3", "A3 (297 × 420 mm)", 297m, 420m),
        new PagePaper("A5", "A5 (148 × 210 mm)", 148m, 210m),
        new PagePaper("A6", "A6 (105 × 148 mm)", 105m, 148m),
        new PagePaper("ISO-B4", "ISO B4 (250 × 353 mm)", 250m, 353m),
        new PagePaper("ISO-B5", "ISO B5 (176 × 250 mm)", 176m, 250m),
        new PagePaper("JIS-B5", "Japon B5 (182 × 257 mm)", 182m, 257m),
        new PagePaper("Letter", "Letter (215,9 × 279,4 mm)", 215.9m, 279.4m),
        new PagePaper("Legal", "Legal (215,9 × 355,6 mm)", 215.9m, 355.6m),
        new PagePaper("Tabloid", "Tabloid (279,4 × 431,8 mm)", 279.4m, 431.8m),
        new PagePaper("Executive", "Executive (184,15 × 266,7 mm)", 184.15m, 266.7m),
        new PagePaper("Custom", "Özel Ölçü (210 × 297 mm)", 210m, 297m)
    });

    public static PagePaper Get(string id) =>
        Presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException("Unknown paper preset.");

    public static PagePaper Custom(decimal widthMm, decimal heightMm)
    {
        if (widthMm is < 40m or > 2000m) throw new ArgumentOutOfRangeException(nameof(widthMm));
        if (heightMm is < 40m or > 2000m) throw new ArgumentOutOfRangeException(nameof(heightMm));
        return new PagePaper("Custom", $"Özel Ölçü ({widthMm} × {heightMm} mm)", widthMm, heightMm);
    }
}

/// <summary>Immutable, per-document geometry only. No printing or persistence implied.</summary>
public sealed record PageLayoutSettings
{
    private PagePaper _paper = PageLayoutCatalog.Get("A4");
    private int _zoomPercent = 100;
    private decimal _left = 20m, _right = 20m, _top = 20m, _bottom = 20m;

    public static PageLayoutSettings Default => new();
    public PagePaper Paper { get => _paper; init => _paper = value ?? throw new ArgumentNullException(nameof(value)); }
    public bool Landscape { get; init; }
    public int ZoomPercent
    {
        get => _zoomPercent;
        init => _zoomPercent = value is >= 50 and <= 150 ? value :
            throw new ArgumentOutOfRangeException(nameof(value), "50–150% zoom only.");
    }
    public decimal LeftMarginMm
    {
        get => _left;
        init { CheckMargin(value, _right, Paper.WidthMm); _left = value; }
    }
    public decimal RightMarginMm
    {
        get => _right;
        init { CheckMargin(value, _left, Paper.WidthMm); _right = value; }
    }
    public decimal TopMarginMm
    {
        get => _top;
        init { CheckMargin(value, _bottom, Paper.HeightMm); _top = value; }
    }
    public decimal BottomMarginMm
    {
        get => _bottom;
        init { CheckMargin(value, _top, Paper.HeightMm); _bottom = value; }
    }
    public decimal WidthMm => Landscape ? Paper.HeightMm : Paper.WidthMm;
    public decimal HeightMm => Landscape ? Paper.WidthMm : Paper.HeightMm;
    public decimal PrintableWidthMm => WidthMm - LeftMarginMm - RightMarginMm;
    public decimal PrintableHeightMm => HeightMm - TopMarginMm - BottomMarginMm;

    public void Validate()
    {
        CheckMargin(LeftMarginMm, RightMarginMm, WidthMm);
        CheckMargin(TopMarginMm, BottomMarginMm, HeightMm);
    }

    private static void CheckMargin(decimal value, decimal other, decimal dimension)
    {
        if (value < 0 || value > 100 || value + other >= dimension)
            throw new ArgumentOutOfRangeException(nameof(value), "Margins must leave printable paper area.");
    }
}
