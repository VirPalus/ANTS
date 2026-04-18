namespace ANTS;
using SkiaSharp;

public class UiButton
{
    public Rectangle Bounds { get; }
    public string Label { get; set; }

    public Func<bool>? IsActive;

    public Action OnClick { get; }

    public bool IsHovered;

    public float TextBaselineX;
    public float TextBaselineY;

    public UiButton(Rectangle bounds, string label, Action onClick)
    {
        Bounds = bounds;
        Label = label;
        OnClick = onClick;
    }

    public bool Contains(int x, int y)
    {
        return Bounds.Contains(x, y);
    }

    public void Draw(SKCanvas canvas, SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint)
    {
        bool active = IsActive != null && IsActive();

        SKColor tile;
        if (active) tile = UiTheme.BgPanelActive;
        else if (IsHovered) tile = UiTheme.BgPanelHover;
        else tile = UiTheme.BgPanel;

        float rx = Bounds.X;
        float ry = Bounds.Y;
        float rw = Bounds.Width;
        float rh = Bounds.Height;

        fillPaint.Color = tile;
        SKRect rect = new SKRect(rx, ry, rx + rw, ry + rh);
        // perf-rule-5 exempt: Draw runs inside RecordButtonsPicture (cached until hover/click)
        using SKRoundRect rr = new SKRoundRect(rect, UiTheme.CornerMedium, UiTheme.CornerMedium);
        fillPaint.IsAntialias = true;
        borderPaint.IsAntialias = true;
        canvas.DrawRoundRect(rr, fillPaint);

        borderPaint.Color = active ? UiTheme.BorderStrong : UiTheme.BorderSubtle;
        borderPaint.StrokeWidth = UiTheme.BorderThin;
        canvas.DrawRoundRect(rr, borderPaint);
        fillPaint.IsAntialias = false;
        borderPaint.IsAntialias = false;

        SKColor prevText = textPaint.Color;
        textPaint.Color = active ? UiTheme.TextStrong : UiTheme.TextBody;
        canvas.DrawText(Label, TextBaselineX, TextBaselineY, textPaint);
        textPaint.Color = prevText;
    }
}
