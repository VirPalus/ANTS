namespace ANTS;
using SkiaSharp;

public static class UiPanel
{
    public static void Draw(SKCanvas canvas, SKPaint fillPaint, float x, float y, float w, float h, float radius)
    {
        using SKRoundRect rr = new SKRoundRect(new SKRect(x, y, x + w, y + h), radius, radius);
        fillPaint.IsAntialias = true;
        canvas.DrawRoundRect(rr, fillPaint);
        fillPaint.IsAntialias = false;
    }

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

    public static void DrawFullScreenDim(SKCanvas canvas, float width, float height, SKColor color, SKPaint fillPaint)
    {
        SKColor prev = fillPaint.Color;
        fillPaint.Color = color;
        canvas.DrawRect(0, 0, width, height, fillPaint);
        fillPaint.Color = prev;
    }
}
