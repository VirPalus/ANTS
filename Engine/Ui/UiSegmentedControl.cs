namespace ANTS;
using SkiaSharp;

public class UiSegmentedControl
{
    public SKRect Bounds;
    public string[] Labels;
    public int ActiveIndex;

    public UiSegmentedControl(SKRect bounds, string[] labels, int activeIndex)
    {
        Bounds = bounds;
        Labels = labels;
        ActiveIndex = activeIndex;
    }

    public int SegmentCount { get { return Labels.Length; } }

    public SKRect GetSegmentRect(int i)
    {
        float w = Bounds.Width / Labels.Length;
        return new SKRect(Bounds.Left + i * w, Bounds.Top, Bounds.Left + (i + 1) * w, Bounds.Bottom);
    }

    public int HitTest(float x, float y)
    {
        if (!Bounds.Contains(x, y)) return -1;
        float w = Bounds.Width / Labels.Length;
        int i = (int)((x - Bounds.Left) / w);
        if (i < 0) i = 0;
        if (i >= Labels.Length) i = Labels.Length - 1;
        return i;
    }

    public void Draw(SKCanvas canvas, SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint)
    {
        float radius = UiTheme.CornerMedium;

        fillPaint.IsAntialias = true;
        borderPaint.IsAntialias = true;

        fillPaint.Color = UiTheme.BgPanel;
        using (SKRoundRect outerRr = new SKRoundRect(Bounds, radius, radius))
        {
            canvas.DrawRoundRect(outerRr, fillPaint);
        }

        if (ActiveIndex >= 0 && ActiveIndex < Labels.Length)
        {
            fillPaint.Color = UiTheme.BgPanelActive;
            SKRect seg = GetSegmentRect(ActiveIndex);
            seg.Inflate(-2f, -2f);
            using (SKRoundRect activeRr = new SKRoundRect(seg, radius - 2f, radius - 2f))
            {
                canvas.DrawRoundRect(activeRr, fillPaint);
            }
        }

        borderPaint.Color = UiTheme.BorderSubtle;
        borderPaint.StrokeWidth = UiTheme.BorderThin;
        using (SKRoundRect borderRr = new SKRoundRect(Bounds, radius, radius))
        {
            canvas.DrawRoundRect(borderRr, borderPaint);
        }

        fillPaint.IsAntialias = false;
        borderPaint.IsAntialias = false;

        SKColor prevColor = textPaint.Color;
        SKFontMetrics metrics = textPaint.FontMetrics;
        float textHeight = -metrics.Ascent + metrics.Descent;
        float baselineOffset = -metrics.Ascent;

        for (int i = 0; i < Labels.Length; i++)
        {
            SKRect seg = GetSegmentRect(i);
            textPaint.Color = (i == ActiveIndex) ? UiTheme.TextStrong : UiTheme.TextMuted;
            float tw = textPaint.MeasureText(Labels[i]);
            float tx = seg.MidX - tw * 0.5f;
            float ty = seg.MidY - textHeight * 0.5f + baselineOffset;
            canvas.DrawText(Labels[i], tx, ty, textPaint);
        }
        textPaint.Color = prevColor;
    }
}
