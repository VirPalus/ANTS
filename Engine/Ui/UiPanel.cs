namespace ANTS;
using SkiaSharp;

// Stateless rounded-rect drawing helpers. Every dark surface in the
// UI (buttons, cards, overlays, sidebar) funnels through here so we
// get one consistent rounded / bordered look.
//
// PERF NOTE: SKRoundRect is fine here because UiPanel is ONLY called
// inside cached SKPictures (buttons, stats, HUD, topbar) that rebuild
// infrequently. It must NEVER be used in per-frame drawing paths.
//
// AA NOTE: Shared paints have IsAntialias = false for axis-aligned
// rect performance. We flip AA on before DrawRoundRect and restore
// it after, so rounded corners are always smooth.
public static class UiPanel
{
    // Solid rounded-rect panel.
    public static void Draw(SKCanvas canvas, SKPaint fillPaint, float x, float y, float w, float h, float radius)
    {
        using SKRoundRect rr = new SKRoundRect(new SKRect(x, y, x + w, y + h), radius, radius);
        fillPaint.IsAntialias = true;
        canvas.DrawRoundRect(rr, fillPaint);
        fillPaint.IsAntialias = false;
    }

    // Rounded rect with a 1-pixel subtle border so panels don't melt
    // into each other on the dark background.
    public static void DrawWithBorder(SKCanvas canvas, SKPaint fillPaint, SKPaint borderPaint, float x, float y, float w, float h, float radius)
    {
        SKRect rect = new SKRect(x, y, x + w, y + h);
        using SKRoundRect rr = new SKRoundRect(rect, radius, radius);
        fillPaint.IsAntialias = true;
        borderPaint.IsAntialias = true;
        canvas.DrawRoundRect(rr, fillPaint);
        canvas.DrawRoundRect(rr, borderPaint);
        fillPaint.IsAntialias = false;
        borderPaint.IsAntialias = false;
    }

    // A "card" -- panel with a small header band and body area. Returns
    // the body rect so the caller can place content inside.
    public static SKRect DrawCard(SKCanvas canvas, SKPaint bodyFill, SKPaint borderPaint, SKPaint titlePaint, string title, float x, float y, float w, float h)
    {
        DrawWithBorder(canvas, bodyFill, borderPaint, x, y, w, h, UiTheme.CornerMedium);

        if (!string.IsNullOrEmpty(title))
        {
            float baseline = y + UiTheme.SpacingUnit + titlePaint.TextSize * 0.8f;
            canvas.DrawText(title, x + UiTheme.SpacingUnit + 2f, baseline, titlePaint);
        }

        float bodyTop = y + UiTheme.SpacingUnit + titlePaint.TextSize + UiTheme.SpacingHalf;
        return new SKRect(x + UiTheme.SpacingUnit, bodyTop, x + w - UiTheme.SpacingUnit, y + h - UiTheme.SpacingUnit);
    }

    // Shortcut for filling the whole screen with a translucent dim.
    public static void DrawFullScreenDim(SKCanvas canvas, float width, float height, SKColor color, SKPaint fillPaint)
    {
        SKColor prev = fillPaint.Color;
        fillPaint.Color = color;
        canvas.DrawRect(0, 0, width, height, fillPaint);
        fillPaint.Color = prev;
    }
}
