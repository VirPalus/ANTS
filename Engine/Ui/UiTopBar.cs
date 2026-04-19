namespace ANTS;
using System.Globalization;
using SkiaSharp;

public class UiTopBar
{
    public const int BarHeight = 48;
    public const int OuterPadding = 8;
    public const int PauseButtonWidth = 88;
    public const int PauseButtonHeight = 32;
    public const int SpeedWidth = 220;
    public const int SpeedHeight = 32;
    public const int ProfileButtonWidth = 88;
    public const int ProfileButtonHeight = 32;

    /// <summary>Red tint used for the Profile button's active state
    /// (fase-4.12-fixup). Deliberately different from BgPanelActive so
    /// the user can spot-check that the profiler is recording.</summary>
    public static readonly SKColor ProfileActiveBg = new SKColor(140, 40, 40);

    public SKRect Bounds;
    public UiButton PauseButton;
    public UiButton ProfileButton;
    public UiSegmentedControl Speed;
    public string MapName = "";

    public double[] SpeedValues;

    private readonly Action _onPauseToggle;
    private readonly Action<double> _onSpeedChange;
    private readonly Action _onProfilerToggle;
    private readonly Func<bool> _pausedGetter;
    private readonly Func<double> _currentSpeedGetter;
    private readonly Func<bool> _isProfilerActive;

    public UiTopBar(
        double[] speedValues,
        Func<bool> pausedGetter,
        Func<double> currentSpeedGetter,
        Action onPauseToggle,
        Action<double> onSpeedChange,
        Action onProfilerToggle,
        Func<bool> isProfilerActive)
    {
        SpeedValues = speedValues;
        _pausedGetter = pausedGetter;
        _currentSpeedGetter = currentSpeedGetter;
        _onPauseToggle = onPauseToggle;
        _onSpeedChange = onSpeedChange;
        _onProfilerToggle = onProfilerToggle;
        _isProfilerActive = isProfilerActive;

        Bounds = new SKRect(0, 0, 0, BarHeight);
        PauseButton = new UiButton(new Rectangle(0, 0, PauseButtonWidth, PauseButtonHeight), "Pause", onPauseToggle);
        PauseButton.IsActive = () => _pausedGetter();

        ProfileButton = new UiButton(new Rectangle(0, 0, ProfileButtonWidth, ProfileButtonHeight), "Profile", onProfilerToggle);
        ProfileButton.IsActive = () => _isProfilerActive();

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
        int profileY = (BarHeight - ProfileButtonHeight) / 2;

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

        // Flush-right Profile button. MapName (drawn in Draw()) is shifted
        // left by the button's reserved width so the two never overlap.
        int profileX = clientWidth - OuterPadding - ProfileButtonWidth;
        if (profileX < speedX + SpeedWidth + OuterPadding)
        {
            profileX = speedX + SpeedWidth + OuterPadding;
        }
        ProfileButton = new UiButton(
            new Rectangle(profileX, profileY, ProfileButtonWidth, ProfileButtonHeight),
            "Profile",
            _onProfilerToggle);
        ProfileButton.IsActive = () => _isProfilerActive();
    }

    public void CacheTextPositions(SKPaint textPaint, SKFontMetrics metrics, float textHeight)
    {
        float tw = textPaint.MeasureText(PauseButton.Label);
        PauseButton.TextBaselineX = PauseButton.Bounds.X + (PauseButton.Bounds.Width - tw) / 2f;
        PauseButton.TextBaselineY = PauseButton.Bounds.Y + (PauseButton.Bounds.Height - textHeight) / 2f - metrics.Ascent;

        float twp = textPaint.MeasureText(ProfileButton.Label);
        ProfileButton.TextBaselineX = ProfileButton.Bounds.X + (ProfileButton.Bounds.Width - twp) / 2f;
        ProfileButton.TextBaselineY = ProfileButton.Bounds.Y + (ProfileButton.Bounds.Height - textHeight) / 2f - metrics.Ascent;
    }

    public bool HandleClick(int x, int y)
    {
        if (!Bounds.Contains(x, y)) return false;

        if (PauseButton.Contains(x, y))
        {
            PauseButton.OnClick();
            return true;
        }

        if (ProfileButton.Contains(x, y))
        {
            ProfileButton.OnClick();
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
        ProfileButton.IsHovered = ProfileButton.Contains(x, y);
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

        DrawProfileButton(canvas, fillPaint, borderPaint, textPaint);

        if (!string.IsNullOrEmpty(MapName))
        {
            SKColor prev = textPaint.Color;
            textPaint.Color = UiTheme.TextMuted;
            float tw = textPaint.MeasureText(MapName);
            // Center MapName in the gap between Speed control and ProfileButton,
            // so the text stays readable no matter how wide the window gets.
            float leftEdge = Speed.Bounds.Right + OuterPadding;
            float rightEdge = ProfileButton.Bounds.X - OuterPadding;
            float centerX = (leftEdge + rightEdge) * 0.5f;
            float tx = centerX - tw * 0.5f;
            if (tx < leftEdge) tx = leftEdge;
            float ty = Bounds.MidY + textPaint.TextSize * 0.35f;
            canvas.DrawText(MapName, tx, ty, textPaint);
            textPaint.Color = prev;
        }
    }

    /// <summary>
    /// Renders the Profile button with a red-tint active state
    /// (<see cref="ProfileActiveBg"/>) instead of the default
    /// <see cref="UiTheme.BgPanelActive"/>. Drawing is inlined here
    /// so <see cref="UiButton"/> stays untouched.
    /// </summary>
    private void DrawProfileButton(SKCanvas canvas, SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint)
    {
        bool active = _isProfilerActive();

        SKColor tile;
        if (active) tile = ProfileActiveBg;
        else if (ProfileButton.IsHovered) tile = UiTheme.BgPanelHover;
        else tile = UiTheme.BgPanel;

        float rx = ProfileButton.Bounds.X;
        float ry = ProfileButton.Bounds.Y;
        float rw = ProfileButton.Bounds.Width;
        float rh = ProfileButton.Bounds.Height;

        fillPaint.Color = tile;
        SKRect rect = new SKRect(rx, ry, rx + rw, ry + rh);
        // perf-rule-5 exempt: Draw runs inside the top-bar cached recorder (rebuilt only on hover/state changes)
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
        canvas.DrawText(ProfileButton.Label, ProfileButton.TextBaselineX, ProfileButton.TextBaselineY, textPaint);
        textPaint.Color = prevText;
    }

    private static string FormatSpeedLabel(double speed)
    {
        if (Math.Abs(speed - Math.Round(speed)) < 0.001)
        {
            return ((int)Math.Round(speed)).ToString(CultureInfo.InvariantCulture) + "x";
        }
        return speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
    }
}
