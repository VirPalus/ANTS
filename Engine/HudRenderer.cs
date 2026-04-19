namespace ANTS;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using SkiaSharp;

/// <summary>
/// Encapsulates the top-left HUD panel rendering (FPS, Frame ms,
/// Sim ms, Ants ms). Owns the HUD SKPicture cache plus all frame
/// timing state (FPS counter, rebuild stopwatch, EMA sim/ant stage
/// timers, last frame time).
///
/// Engine delegates timing measurements via a Ticks-based API
/// (ReportSimStageTicks, ReportAntStageTicks, ReportFrameTicks) —
/// this class is the sole authority for tick-to-ms conversion and
/// EMA smoothing.
///
/// Rebuild cadence is controlled by _hudStopwatch (throttled to
/// HudUpdateIntervalMs = 50 ms). MaybeRebuild() rebuilds the HUD
/// picture when the interval has elapsed. After fase-4.10 the stats
/// panel owns its own independent rebuild cadence, so this method no
/// longer needs to signal coupled rebuilds and returns void.
/// </summary>
public sealed class HudRenderer : IDisposable
{
    private const int HudUpdateIntervalMs = 50;
    private const double StageEmaAlpha = 0.05;

    // Stored for ctor API consistency with other renderers (AntsRenderer,
    // WorldRenderer, OverlayRenderer) and forward-looking toward fase-5.x
    // shared paint adoption. Not read by current HUD paint path.
#pragma warning disable CS0414
    private readonly PaintCache _paints;
#pragma warning restore CS0414
    private readonly List<IDisposable> _ownedDisposables = new List<IDisposable>();

    private SKPicture? _hudPicture;

    private readonly Stopwatch _hudStopwatch = new Stopwatch();
    private readonly Stopwatch _fpsStopwatch = new Stopwatch();

    private int _framesThisSecond;
    private int _fps;
    private double _lastFrameMs;
    private double _simStageMs;
    private double _antStageMs;

    public HudRenderer(PaintCache paints)
    {
        _paints = paints;
    }

    /// <summary>
    /// Starts both timing stopwatches and records the initial HUD
    /// picture so the first frame has something to draw.
    /// </summary>
    public void Start()
    {
        _fpsStopwatch.Start();
        _hudStopwatch.Start();
        RecordHudPicture();
    }

    /// <summary>
    /// Increments the per-frame counter and, once per 1000 ms,
    /// rolls the running count into the displayed FPS number.
    /// Must be called as the first thing in Engine.Tick().
    /// </summary>
    public void TickFrameStart()
    {
        _framesThisSecond++;

        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
        {
            _fps = _framesThisSecond;
            _framesThisSecond = 0;
            _fpsStopwatch.Restart();
        }
    }

    /// <summary>
    /// Updates the sim-stage EMA with a Stopwatch tick delta.
    /// </summary>
    public void ReportSimStageTicks(long deltaTicks)
    {
        UpdateStageEma(ref _simStageMs, TicksToMilliseconds(deltaTicks));
    }

    /// <summary>
    /// Updates the ant-stage EMA with a Stopwatch tick delta.
    /// Only called when the ant-render path actually executed.
    /// </summary>
    public void ReportAntStageTicks(long deltaTicks)
    {
        UpdateStageEma(ref _antStageMs, TicksToMilliseconds(deltaTicks));
    }

    /// <summary>
    /// Stamps the last-frame time (no EMA — raw latest value).
    /// </summary>
    public void ReportFrameTicks(long deltaTicks)
    {
        _lastFrameMs = TicksToMilliseconds(deltaTicks);
    }

    /// <summary>
    /// If the rebuild interval has elapsed, rebuilds the HUD picture
    /// and restarts the rebuild stopwatch.
    /// </summary>
    public void MaybeRebuild()
    {
        if (_hudStopwatch.ElapsedMilliseconds >= HudUpdateIntervalMs)
        {
            RecordHudPicture();
            _hudStopwatch.Restart();
        }
    }

    /// <summary>
    /// Paints the cached HUD picture to the given canvas.
    /// </summary>
    public void Draw(SKCanvas canvas)
    {
        if (_hudPicture != null)
        {
            canvas.DrawPicture(_hudPicture);
        }
    }

    private void RecordHudPicture()
    {
        // perf-rule-5/8 exempt: all SK* allocs below run inside SKPictureRecorder (one-time per dirty rebuild)
        float hudW = 150f;
        float hudH = 84f;
        SKRect cullRect = new SKRect(0, 0, hudW + 20f, hudH + 20f);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);

        float px = 8f;
        float py = UiTopBar.BarHeight + 8f;
        using (SKPaint bgPaint = UiTheme.NewFillPaint(UiTheme.BgPanel))
        using (SKPaint brPaint = UiTheme.NewStrokePaint(UiTheme.BorderSubtle, UiTheme.BorderThin))
        {
            UiPanel.DrawWithBorder(recordingCanvas, bgPaint, brPaint, px, py, hudW, hudH, UiTheme.CornerMedium);
        }

        using (SKPaint hudText = new SKPaint())
        {
            hudText.Style = SKPaintStyle.Fill;
            hudText.IsAntialias = true;
            hudText.TextSize = UiTheme.FontSmall;

            float lineStep = 16f;
            float baseX = px + 10f;
            float baseY = py + 6f - hudText.FontMetrics.Ascent;

            float valX = baseX + 50f;

            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("FPS", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextStrong;
            recordingCanvas.DrawText(_fps.ToString(CultureInfo.InvariantCulture), valX, baseY, hudText);

            baseY += lineStep;
            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("Frame", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextBody;
            recordingCanvas.DrawText(_lastFrameMs.ToString("F2", CultureInfo.InvariantCulture) + " ms", valX, baseY, hudText);

            baseY += lineStep;
            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("Sim", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextBody;
            recordingCanvas.DrawText(_simStageMs.ToString("F2", CultureInfo.InvariantCulture) + " ms", valX, baseY, hudText);

            baseY += lineStep;
            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("Ants", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextBody;
            recordingCanvas.DrawText(_antStageMs.ToString("F3", CultureInfo.InvariantCulture) + " ms", valX, baseY, hudText);
        }

        Replace(ref _hudPicture, recorder.EndRecording());
        recorder.Dispose();
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static void UpdateStageEma(ref double stored, double sample)
    {
        stored = stored * (1.0 - StageEmaAlpha) + sample * StageEmaAlpha;
    }

    private void Replace<T>(ref T? field, T? newValue) where T : class, IDisposable
    {
        if (field != null)
        {
            _ownedDisposables.Remove(field);
            field.Dispose();
        }
        field = newValue;
        if (newValue != null)
        {
            _ownedDisposables.Add(newValue);
        }
    }

    public void Dispose()
    {
        for (int i = _ownedDisposables.Count - 1; i >= 0; i--)
        {
            _ownedDisposables[i].Dispose();
        }
        _ownedDisposables.Clear();
        _hudPicture = null;
    }
}
