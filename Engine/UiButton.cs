namespace ANTS;
using SkiaSharp;

// A single rounded dark-theme button. All visual state comes from the
// theme (no accent colour); hover / active differ only by value-step.
// The same class handles push-buttons (Add Colony, Add Food) and
// toggle-buttons (Pheromones on/off, Pause/Play) -- toggles just set
// an IsActive predicate so the renderer can pick the "active" tile.
public class UiButton
{
    public Rectangle Bounds { get; }
    public string Label { get; set; }

    // Optional predicate: when it returns true the button paints in
    // its active state (lighter tile, brighter text). Null means a
    // plain push-button.
    public Func<bool>? IsActive;

    public Action OnClick { get; }

    // Hover is driven externally by the engine's mouse-move handler.
    public bool IsHovered;

    // Cached text position, recomputed whenever the label or bounds
    // change. The engine does this in CacheButtonTextPosition.
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

    // Draw a single button onto the recording canvas. Uses the
    // passed-in shared paints to avoid allocating per draw.
    public void Draw(SKCanvas canvas, SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint)
    {
        bool active = IsActive != null && IsActive();

        // Tile colour: active > hovered > idle, same hue family.
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
