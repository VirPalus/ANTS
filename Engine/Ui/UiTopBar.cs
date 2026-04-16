namespace ANTS;
using System.Collections.Generic;
using SkiaSharp;

// Top bar: a dark panel spanning the top of the window that holds the
// pause / play button, the speed segmented-control, and (later) the
// active map name. Owns layout, hit-testing and rendering as a unit
// so Engine.cs doesn't have to thread state through a dozen fields.
//
// The top bar is stateless w.r.t. pause/speed -- it pulls from
// callbacks the engine passes in, so there's no duplicate source of
// truth. When the user clicks, the engine mutates its own state and
// calls Layout again to refresh labels.
public class UiTopBar
{
    public const int BarHeight = 48;
    public const int OuterPadding = 8;
    public const int PauseButtonWidth = 88;
    public const int PauseButtonHeight = 32;
    public const int SpeedWidth = 220;
    public const int SpeedHeight = 32;

    public SKRect Bounds;              // outer panel
    public UiButton PauseButton;       // owns its own Draw
    public UiSegmentedControl Speed;   // owns its own Draw
    public string MapName = "";

    // Speed choices in the same order as the engine's SpeedChoices
    // array. Kept as labels here so the segmented control stays
    // visual-only.
    public double[] SpeedValues;

    private readonly Action _onPauseToggle;
    private readonly Action<double> _onSpeedChange;
    private readonly Func<bool> _pausedGetter;
    private readonly Func<double> _currentSpeedGetter;

    public UiTopBar(double[] speedValues, Func<bool> pausedGetter, Func<double> currentSpeedGetter, Action onPauseToggle, Action<double> onSpeedChange)
    {
        SpeedValues = speedValues;
        _pausedGetter = pausedGetter;
        _currentSpeedGetter = currentSpeedGetter;
        _onPauseToggle = onPauseToggle;
        _onSpeedChange = onSpeedChange;

        // Placeholders -- Layout() fills in real rects.
        Bounds = new SKRect(0, 0, 0, BarHeight);
        PauseButton = new UiButton(new Rectangle(0, 0, PauseButtonWidth, PauseButtonHeight), "Pause", onPauseToggle);
        PauseButton.IsActive = () => _pausedGetter();

        string[] labels = new string[speedValues.Length];
        int activeIdx = 0;
        for (int i = 0; i < speedValues.Length; i++)
        {
            labels[i] = FormatSpeedLabel(speedValues[i]);
            if (Math.Abs(speedValues[i] - _currentSpeedGetter()) < 0.001) activeIdx = i;
        }
        Speed = new UiSegmentedControl(new SKRect(0, 0, SpeedWidth, SpeedHeight), labels, activeIdx);
    }

    // Compute rects for current client size. Call on Resize.
    public void Layout(int clientWidth)
    {
        Bounds = new SKRect(0, 0, clientWidth, BarHeight);

        int innerY = (BarHeight - PauseButtonHeight) / 2;
        int speedY = (BarHeight - SpeedHeight) / 2;

        // Pause sits on the left.
        PauseButton = new UiButton(
            new Rectangle(OuterPadding, innerY, PauseButtonWidth, PauseButtonHeight),
            _pausedGetter() ? "Play" : "Pause",
            _onPauseToggle);
        PauseButton.IsActive = () => _pausedGetter();

        // Speed control just right of pause.
        int speedX = OuterPadding + PauseButtonWidth + UiTopBar.OuterPadding;
        Speed.Bounds = new SKRect(speedX, speedY, speedX + SpeedWidth, speedY + SpeedHeight);

        // Refresh active index from engine.
        Speed.ActiveIndex = 0;
        for (int i = 0; i < SpeedValues.Length; i++)
        {
            if (Math.Abs(SpeedValues[i] - _currentSpeedGetter()) < 0.001)
            {
                Speed.ActiveIndex = i;
                break;
            }
        }
    }

    // Cache text positions for the Pause button (called by engine
    // after Layout so it can use the engine's font metrics).
    public void CacheTextPositions(SKPaint textPaint, SKFontMetrics metrics, float textHeight)
    {
        float tw = textPaint.MeasureText(PauseButton.Label);
        PauseButton.TextBaselineX = PauseButton.Bounds.X + (PauseButton.Bounds.Width - tw) / 2f;
        PauseButton.TextBaselineY = PauseButton.Bounds.Y + (PauseButton.Bounds.Height - textHeight) / 2f - metrics.Ascent;
    }

    // Returns true if the click was consumed by a widget in the bar.
    public bool HandleClick(int x, int y)
    {
        if (!Bounds.Contains(x, y)) return false;

        if (PauseButton.Contains(x, y))
        {
            PauseButton.OnClick();
            return true;
        }

        int segIdx = Speed.HitTest(x, y);
        if (segIdx >= 0)
        {
            _onSpeedChange(SpeedValues[segIdx]);
            return true;
        }

        // Click on the bar chrome but not on a widget -- still swallow
        // so it doesn't bleed through to world-placement.
        return true;
    }

    public void UpdateHover(int x, int y)
    {
        PauseButton.IsHovered = PauseButton.Contains(x, y);
    }

    public void Draw(SKCanvas canvas, SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint)
    {
        // --- bar background ---
        fillPaint.Color = UiTheme.BgPanel;
        canvas.DrawRect(Bounds, fillPaint);

        // subtle bottom border
        borderPaint.Color = UiTheme.BorderSubtle;
        borderPaint.StrokeWidth = UiTheme.BorderThin;
        canvas.DrawLine(Bounds.Left, Bounds.Bottom - 0.5f, Bounds.Right, Bounds.Bottom - 0.5f, borderPaint);

        // --- pause / play ---
        PauseButton.Draw(canvas, fillPaint, borderPaint, textPaint);

        // --- speed selector ---
        Speed.Draw(canvas, fillPaint, borderPaint, textPaint);

        // --- map name, centered ---
        if (!string.IsNullOrEmpty(MapName))
        {
            SKColor prev = textPaint.Color;
            textPaint.Color = UiTheme.TextMuted;
            float tw = textPaint.MeasureText(MapName);
            float tx = Bounds.MidX - tw * 0.5f;
            float ty = Bounds.MidY + textPaint.TextSize * 0.35f;
            canvas.DrawText(MapName, tx, ty, textPaint);
            textPaint.Color = prev;
        }
    }

    private static string FormatSpeedLabel(double speed)
    {
        if (Math.Abs(speed - Math.Round(speed)) < 0.001)
        {
            return ((int)Math.Round(speed)).ToString() + "x";
        }
        return speed.ToString("0.##") + "x";
    }
}
