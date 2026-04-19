namespace ANTS;

using System;
using System.Globalization;
using SkiaSharp;

/// <summary>
/// HUD-adjacent status panel for the <see cref="FrameProfiler"/>.
///
/// Drawn only when <see cref="FrameProfiler.IsEnabled"/> is true.
/// Sits just below the HUD panel on the top-left, matches HUD width
/// (well, wider — 220 px) to fit the 6 status rows:
///
///   1. ● REC  rot N          (red dot + active-file rotation index)
///   2. Frames  &lt;global&gt;    (frame counter from Engine, never reset)
///   3. AvgFr   &lt;ms&gt;         (EMA of wall-clock per-frame duration)
///   4. AvgRd   &lt;ms&gt;         (EMA of OnPaint total)
///   5. AvgOv   &lt;ms&gt;         (EMA of pheromone overlay draw)
///   6. File    &lt;basename&gt;   (current CSV file in %TEMP% incl. _N suffix)
///
/// The renderer is immediate-mode (no SKPicture cache) because the
/// panel is only visible while profiling is active and its visual
/// state changes every frame via the EMAs — a picture cache would
/// be rebuilt every frame anyway.
/// </summary>
public sealed class ProfilerUI
{
    /// <summary>Panel x (same as HUD).</summary>
    public const float PanelX = 8f;

    /// <summary>Panel width — wider than the HUD so filenames fit.</summary>
    public const float PanelW = 220f;

    /// <summary>Panel height — sized for 6 rows of 28px + paddings.</summary>
    public const float PanelH = 196f;

    /// <summary>Vertical gap between HUD and the profiler status panel.</summary>
    private const float HudGap = 8f;

    /// <summary>Assumed HUD height (kept in sync with HudRenderer hudH=84f).</summary>
    private const float HudHeight = 84f;

    private static readonly SKColor AccentRed = new SKColor(220, 70, 70);

    private readonly FrameProfiler _profiler;
    private readonly Func<long> _frameCounterGetter;

    public ProfilerUI(FrameProfiler profiler, Func<long> frameCounterGetter)
    {
        _profiler = profiler;
        _frameCounterGetter = frameCounterGetter;
    }

    /// <summary>
    /// Draws the status panel if profiling is enabled. No-op when
    /// disabled (zero allocations, single field read).
    /// </summary>
    public void Draw(SKCanvas canvas)
    {
        if (!_profiler.IsEnabled) return;

        float px = PanelX;
        float py = UiTopBar.BarHeight + HudGap + HudHeight + HudGap;

        // perf-rule-5/8 exempt: ProfilerUI.Draw is dev-only (profiler UI is off in release benchmarks).
        using (SKPaint bgPaint = UiTheme.NewFillPaint(UiTheme.BgPanel))
        using (SKPaint brPaint = UiTheme.NewStrokePaint(UiTheme.BorderSubtle, UiTheme.BorderThin))
        {
            UiPanel.DrawWithBorder(canvas, bgPaint, brPaint, px, py, PanelW, PanelH, UiTheme.CornerMedium);
        }

        using (SKPaint text = new SKPaint())
        using (SKPaint dot = new SKPaint())
        {
            text.Style = SKPaintStyle.Fill;
            text.IsAntialias = true;
            text.TextSize = UiTheme.FontSmall;

            dot.Style = SKPaintStyle.Fill;
            dot.IsAntialias = true;
            dot.Color = AccentRed;

            float lineStep = 26f;
            float baseX = px + 12f;
            float labelW = 60f;
            float valueX = baseX + labelW;
            float baseY = py + 10f - text.FontMetrics.Ascent;

            // Row 1: ● REC   rot N
            canvas.DrawCircle(baseX + 5f, baseY - 5f, 5f, dot);
            text.Color = AccentRed;
            canvas.DrawText("REC", baseX + 14f, baseY, text);
            text.Color = UiTheme.TextMuted;
            canvas.DrawText("rot", valueX, baseY, text);
            text.Color = UiTheme.TextStrong;
            canvas.DrawText(
                _profiler.WriterRotationIndex.ToString(CultureInfo.InvariantCulture),
                valueX + 28f, baseY, text);

            // Row 2: Frames  <global>
            baseY += lineStep;
            text.Color = UiTheme.TextMuted;
            canvas.DrawText("Frames", baseX, baseY, text);
            text.Color = UiTheme.TextBody;
            canvas.DrawText(
                _frameCounterGetter().ToString(CultureInfo.InvariantCulture),
                valueX, baseY, text);

            // Row 3: AvgFr  <ms>
            baseY += lineStep;
            text.Color = UiTheme.TextMuted;
            canvas.DrawText("AvgFr", baseX, baseY, text);
            text.Color = UiTheme.TextBody;
            canvas.DrawText(
                _profiler.AvgFrameMs.ToString("F2", CultureInfo.InvariantCulture) + " ms",
                valueX, baseY, text);

            // Row 4: AvgRd  <ms>
            baseY += lineStep;
            text.Color = UiTheme.TextMuted;
            canvas.DrawText("AvgRd", baseX, baseY, text);
            text.Color = UiTheme.TextBody;
            canvas.DrawText(
                _profiler.AvgRenderMs.ToString("F2", CultureInfo.InvariantCulture) + " ms",
                valueX, baseY, text);

            // Row 5: AvgOv  <ms>
            baseY += lineStep;
            text.Color = UiTheme.TextMuted;
            canvas.DrawText("AvgOv", baseX, baseY, text);
            text.Color = UiTheme.TextBody;
            canvas.DrawText(
                _profiler.AvgOverlayMs.ToString("F3", CultureInfo.InvariantCulture) + " ms",
                valueX, baseY, text);

            // Row 6: File  <basename> (truncated to fit panel width)
            baseY += lineStep;
            text.Color = UiTheme.TextMuted;
            canvas.DrawText("File", baseX, baseY, text);
            text.Color = UiTheme.TextBody;
            string fname = _profiler.WriterCurrentFileName;
            if (fname.Length > 20)
            {
                fname = fname.Substring(0, 20);
            }
            canvas.DrawText(fname, valueX, baseY, text);
        }
    }
}
