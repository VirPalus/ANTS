namespace ANTS;
using SkiaSharp;

// Central dark-theme palette and spacing grid for the whole UI layer.
// There is NO accent colour on purpose -- everything is a neutral dark
// gray with mild value-steps for hover / active states. Semantic
// colours (food green, danger red, pheromones, per-colony tints) still
// live where they're used; this file covers the UI chrome only.
//
// Any time you reach for "let's just use #something", route it through
// here instead so the whole app moves together when the theme changes.
public static class UiTheme
{
    // --- surfaces ---------------------------------------------------------
    // Root background (same as SKGL clear). The ladder runs from darkest
    // (world void) up through panel hover so overlapping surfaces read
    // cleanly without any hue.
    public static readonly SKColor BgRoot        = new SKColor(42, 42, 46);
    public static readonly SKColor BgPanel       = new SKColor(32, 32, 36);
    public static readonly SKColor BgPanelAlt    = new SKColor(38, 38, 42);
    public static readonly SKColor BgPanelHover  = new SKColor(48, 48, 54);
    public static readonly SKColor BgPanelActive = new SKColor(62, 62, 70);
    public static readonly SKColor BgOverlayDim  = new SKColor(0, 0, 0, 180);

    // --- world / simulation environment ------------------------------------
    public static readonly SKColor BgWorld       = new SKColor(52, 52, 56);
    // Walls and border use the SAME color so they read as one cohesive
    // frame around the playable space.
    public static readonly SKColor WallColor     = new SKColor(80, 80, 88);
    public static readonly SKColor WorldBorder   = new SKColor(80, 80, 88);
    public static readonly SKColor GridLine      = new SKColor(48, 48, 52);

    // --- borders ----------------------------------------------------------
    public static readonly SKColor BorderSubtle  = new SKColor(50, 50, 56);
    public static readonly SKColor BorderMedium  = new SKColor(70, 70, 78);
    public static readonly SKColor BorderStrong  = new SKColor(100, 100, 112);

    // --- text -------------------------------------------------------------
    public static readonly SKColor TextStrong    = new SKColor(240, 240, 240);
    public static readonly SKColor TextBody      = new SKColor(210, 210, 215);
    public static readonly SKColor TextMuted     = new SKColor(150, 155, 165);
    public static readonly SKColor TextDim       = new SKColor(100, 104, 112);

    // --- charts / graphs --------------------------------------------------
    // Chart strokes stay neutral; per-series colour comes from the data
    // (colony tint, food green, etc.).
    public static readonly SKColor ChartGrid     = new SKColor(44, 44, 50);
    public static readonly SKColor ChartAxis     = new SKColor(70, 70, 78);

    // --- layout / geometry ------------------------------------------------
    public const float CornerSmall  = 4f;
    public const float CornerMedium = 8f;
    public const float CornerLarge  = 12f;

    public const float SpacingUnit  = 8f;  // base 8px grid
    public const float SpacingHalf  = 4f;
    public const float SpacingDouble = 16f;

    // Stroke widths
    public const float BorderThin   = 1f;
    public const float BorderNormal = 1.5f;

    // --- typography -------------------------------------------------------
    public const float FontTiny     = 10f;
    public const float FontSmall    = 11f;
    public const float FontBody     = 13f;
    public const float FontHeader   = 15f;
    public const float FontTitle    = 22f;
    public const float FontDisplay  = 34f;

    // --- derived helpers --------------------------------------------------
    // Cheap-to-construct filled rect paint, useful for ad-hoc panels
    // outside the UiPanel helper. Callers are responsible for disposing.
    // These helpers are ONLY used inside cached SKPicture recordings
    // (HUD, stats), so AA = true is fine — no per-frame overhead.
    public static SKPaint NewFillPaint(SKColor color)
    {
        SKPaint p = new SKPaint();
        p.Style = SKPaintStyle.Fill;
        p.IsAntialias = true;
        p.Color = color;
        return p;
    }

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
