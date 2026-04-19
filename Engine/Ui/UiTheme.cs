namespace ANTS;
using SkiaSharp;

public static class UiTheme
{
    public static readonly SKColor BgRoot        = new SKColor(42, 42, 46);
    public static readonly SKColor BgPanel       = new SKColor(32, 32, 36);
    public static readonly SKColor BgPanelAlt    = new SKColor(38, 38, 42);
    public static readonly SKColor BgPanelHover  = new SKColor(48, 48, 54);
    public static readonly SKColor BgPanelActive = new SKColor(62, 62, 70);
    public static readonly SKColor BgOverlayDim  = new SKColor(0, 0, 0, 180);

    public static readonly SKColor BgWorld       = new SKColor(52, 52, 56);
    public static readonly SKColor WallColor     = new SKColor(80, 80, 88);
    public static readonly SKColor WorldBorder   = new SKColor(80, 80, 88);
    public static readonly SKColor GridLine      = new SKColor(0, 0, 0, 30);

    public static readonly SKColor BorderSubtle  = new SKColor(50, 50, 56);
    public static readonly SKColor BorderMedium  = new SKColor(70, 70, 78);
    public static readonly SKColor BorderStrong  = new SKColor(100, 100, 112);

    public static readonly SKColor TextStrong    = new SKColor(240, 240, 240);
    public static readonly SKColor TextBody      = new SKColor(210, 210, 215);
    public static readonly SKColor TextMuted     = new SKColor(150, 155, 165);
    public static readonly SKColor TextDim       = new SKColor(100, 104, 112);

    public static readonly SKColor ChartGrid     = new SKColor(44, 44, 50);
    public static readonly SKColor ChartAxis     = new SKColor(70, 70, 78);

    // Distinguishable chart line colors (7 channels) used by the
    // profiler graph window to colour metric traces. Chosen for
    // contrast against BgPanelAlt.
    public static readonly SKColor ChartLine1    = new SKColor(110, 190, 255);
    public static readonly SKColor ChartLine2    = new SKColor(255, 160, 100);
    public static readonly SKColor ChartLine3    = new SKColor(130, 220, 140);
    public static readonly SKColor ChartLine4    = new SKColor(230, 130, 210);
    public static readonly SKColor ChartLine5    = new SKColor(240, 220, 110);
    public static readonly SKColor ChartLine6    = new SKColor(160, 150, 240);
    public static readonly SKColor ChartLine7    = new SKColor(210, 210, 210);

    public const float CornerSmall  = 4f;
    public const float CornerMedium = 8f;
    public const float CornerLarge  = 12f;

    public const float SpacingUnit  = 8f;
    public const float SpacingHalf  = 4f;
    public const float SpacingDouble = 16f;

    public const float BorderThin   = 1f;
    public const float BorderNormal = 1.5f;

    public const float FontTiny     = 10f;
    public const float FontSmall    = 11f;
    public const float FontBody     = 13f;
    public const float FontHeader   = 15f;
    public const float FontTitle    = 22f;
    public const float FontDisplay  = 34f;

    // perf-rule-8 exempt: factory only invoked inside RecordHudPicture (one-time alloc per HUD rebuild)
    public static SKPaint NewFillPaint(SKColor color)
    {
        SKPaint p = new SKPaint();
        p.Style = SKPaintStyle.Fill;
        p.IsAntialias = true;
        p.Color = color;
        return p;
    }

    // perf-rule-8 exempt: factory only invoked inside RecordHudPicture (one-time alloc per HUD rebuild)
    public static SKPaint NewStrokePaint(SKColor color, float width)
    {
        SKPaint p = new SKPaint();
        p.Style = SKPaintStyle.Stroke;
        p.IsAntialias = true;
        p.Color = color;
        p.StrokeWidth = width;
        return p;
    }
}
