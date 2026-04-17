namespace ANTS;
using System.Collections.Generic;
using SkiaSharp;

public class UiTopBar
{
    public const int BarHeight = 48;
    public const int OuterPadding = 8;
    public const int PauseButtonWidth = 88;
    public const int PauseButtonHeight = 32;
    public const int SpeedWidth = 220;
    public const int SpeedHeight = 32;

    public SKRect Bounds;
    public UiButton PauseButton;
    public UiSegmentedControl Speed;
    public string MapName = "";

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

    public void Layout(int clientWidth)
    {
        Bounds = new SKRect(0, 0, clientWidth, BarHeight);

        int innerY = (BarHeight - PauseButtonHeight) / 2;
        int speedY = (BarHeight - SpeedHeight) / 2;

        PauseButton = new UiButton(
            new Rectangle(OuterPadding, innerY, PauseButtonWidth, PauseButtonHeight),
            _pausedGetter() ? "Play" : "Pause",
            _onPauseToggle);
        PauseButton.IsActive = () => _pausedGetter();

        int speedX = OuterPadding + PauseButtonWidth + UiTopBar.OuterPadding;
        Speed.Bounds = new SKRect(speedX, speedY, speedX + SpeedWidth, speedY + SpeedHeight);

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

    public void CacheTextPositions(SKPaint textPaint, SKFontMetrics metrics, float textHeight)
    {
        float tw = textPaint.MeasureText(PauseButton.Label);
        PauseButton.TextBaselineX = PauseButton.Bounds.X + (PauseButton.Bounds.Width - tw) / 2f;
        PauseButton.TextBaselineY = PauseButton.Bounds.Y + (PauseButton.Bounds.Height - textHeight) / 2f - metrics.Ascent;
    }

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

        return true;
    }

    public void UpdateHover(int x, int y)
    {
        PauseButton.IsHovered = PauseButton.Contains(x, y);
    }

    public void Draw(SKCanvas canvas, SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint)
    {
        fillPaint.Color = UiTheme.BgPanel;
        canvas.DrawRect(Bounds, fillPaint);

        borderPaint.Color = UiTheme.BorderSubtle;
        borderPaint.StrokeWidth = UiTheme.BorderThin;
        canvas.DrawLine(Bounds.Left, Bounds.Bottom - 0.5f, Bounds.Right, Bounds.Bottom - 0.5f, borderPaint);

        PauseButton.Draw(canvas, fillPaint, borderPaint, textPaint);

        Speed.Draw(canvas, fillPaint, borderPaint, textPaint);

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
