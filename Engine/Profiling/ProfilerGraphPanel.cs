namespace ANTS;

using System;
using System.Globalization;
using SkiaSharp;

/// <summary>
/// Renders a single profiler sub-graph inside the
/// <see cref="ProfilerGraphWindow"/>: panel background, title,
/// y-axis max label, 3 grid lines, one polyline (or decimated
/// min/max column) per visible metric, and a value-legend along
/// the right edge.
///
/// fase-4.12-fix3-v2 changes:
///   * <see cref="Draw"/> takes an absolute sample window
///     <c>(windowStart, windowCount)</c> into
///     <see cref="ProfilerSeries"/> instead of a last-N count.
///     This lets <see cref="ProfilerGraphWindow"/> pan through
///     history via the scrollbar.
///   * When the visible window is wider than ~2x the plot pixel
///     width, the renderer switches to min/max decimation per
///     pixel column (vertical line from min to max). Polyline is
///     used otherwise. Both paths read series data via
///     <see cref="ProfilerSeries.GetSpan"/> / a small scratch
///     buffer — never allocates per-draw.
///
/// This class is not hot-path for the simulation — it runs only
/// while the profiler graph window is open. The three owned paints
/// (<c>_fillPaint</c>, <c>_strokePaint</c>, <c>_textPaint</c>) are
/// reused across metrics and across frames to keep draw-time
/// allocations at zero.
///
/// Y-axis stability: an EMA-smoothed <c>_yMaxEma</c> replaces the
/// raw per-frame max to eliminate the visual flicker that the old
/// code exhibited whenever a spike entered or left the visible
/// sample window. The EMA follows spikes up instantly so real
/// outliers are never clipped, and decays slowly (alpha=0.05) when
/// the data calms. When the caller passes <c>yAxisLocked=true</c>
/// the EMA is frozen at its current value — spikes above the frozen
/// ceiling are clipped. This is intentional and matches the Pin/Lock
/// button behavior in <see cref="ProfilerGraphWindow"/>.
/// </summary>
public sealed class ProfilerGraphPanel : IDisposable
{
    /// <summary>One line to plot on this panel.</summary>
    public readonly struct MetricLine
    {
        public readonly int MetricIndex;
        public readonly string Label;
        public readonly SKColor Color;

        public MetricLine(int metricIndex, string label, SKColor color)
        {
            MetricIndex = metricIndex;
            Label = label;
            Color = color;
        }
    }

    /// <summary>
    /// Polyline path is only used when the window fits comfortably
    /// into a small scratch buffer. Windows larger than this fall
    /// through to the decimation path which reads the series span
    /// directly without copying.
    /// </summary>
    private const int PolylineScratchCap = 8192;

    private readonly float[] _scratch = new float[PolylineScratchCap];
    private readonly SKPath _path = new SKPath();

    // Paint isolation: each panel owns its own paint instances so
    // that sibling panels cannot leak Color/TextSize/StrokeWidth
    // state across one another within a single Draw cycle.
    private readonly SKPaint _fillPaint;
    private readonly SKPaint _strokePaint;
    private readonly SKPaint _textPaint;

    // EMA-smoothed Y-axis ceiling. Initialized on first sample.
    private float _yMaxEma;

    private bool _disposed;

    public ProfilerGraphPanel()
    {
        _fillPaint = new SKPaint();
        _fillPaint.Style = SKPaintStyle.Fill;
        _fillPaint.IsAntialias = true;

        _strokePaint = new SKPaint();
        _strokePaint.Style = SKPaintStyle.Stroke;
        _strokePaint.IsAntialias = true;
        _strokePaint.StrokeWidth = 1f;

        _textPaint = new SKPaint();
        _textPaint.Style = SKPaintStyle.Fill;
        _textPaint.IsAntialias = true;
    }

    /// <summary>
    /// Draws one sub-graph for the absolute window
    /// <c>[windowStart, windowStart + windowCount)</c> of
    /// <paramref name="series"/>. Paints are owned by this instance.
    /// When <paramref name="yAxisLocked"/> is <c>true</c>, the
    /// EMA-smoothed Y ceiling is frozen at its current value (the
    /// Pin/Lock button state in the window).
    /// </summary>
    public void Draw(
        SKCanvas canvas,
        SKRect bounds,
        string title,
        string unitSuffix,
        ProfilerSeries series,
        int windowStart,
        int windowCount,
        MetricLine[] metrics,
        int metricCount,
        bool yAxisLocked)
    {
        // Panel background + border.
        _fillPaint.Color = UiTheme.BgPanelAlt;
        canvas.DrawRect(bounds, _fillPaint);
        _strokePaint.Style = SKPaintStyle.Stroke;
        _strokePaint.StrokeWidth = UiTheme.BorderThin;
        _strokePaint.Color = UiTheme.BorderSubtle;
        canvas.DrawRect(bounds, _strokePaint);

        // Layout.
        float pad = 6f;
        float titleH = 16f;
        float axisH = 12f;
        float plotX = bounds.Left + pad;
        float plotTop = bounds.Top + titleH;
        float plotRight = bounds.Right - pad;
        float plotBottom = bounds.Bottom - axisH;
        float plotW = plotRight - plotX;
        float plotH = plotBottom - plotTop;
        if (plotW <= 4f || plotH <= 4f)
        {
            return;
        }

        // Title (top-left).
        _textPaint.TextSize = UiTheme.FontSmall;
        _textPaint.Color = UiTheme.TextStrong;
        canvas.DrawText(title, bounds.Left + pad, bounds.Top + 12f, _textPaint);

        // Clamp the requested window to what the series actually has.
        int totalAvail = series.Count;
        if (windowStart < 0) windowStart = 0;
        if (windowStart >= totalAvail)
        {
            windowStart = totalAvail;
            windowCount = 0;
        }
        else if (windowStart + windowCount > totalAvail)
        {
            windowCount = totalAvail - windowStart;
        }

        // Find max across visible metrics over the window via GetSpan.
        float max = 0.01f;
        for (int mi = 0; mi < metricCount; mi++)
        {
            ReadOnlySpan<float> src = series.GetSpan(metrics[mi].MetricIndex);
            int len = windowCount;
            if (windowStart + len > src.Length) len = src.Length - windowStart;
            for (int i = 0; i < len; i++)
            {
                float v = src[windowStart + i];
                if (v > max) max = v;
            }
        }
        max *= 1.1f;

        // EMA smoothing of the Y-axis ceiling.
        //   * First-ever frame: seed directly from max.
        //   * Locked:            keep frozen (spikes may clip — intentional).
        //   * Spike up:          follow instantly so outliers are visible.
        //   * Decay:              alpha=0.05 so leaving spikes fade slowly.
        if (_yMaxEma <= 0f)
        {
            _yMaxEma = max;
        }
        else if (!yAxisLocked)
        {
            if (max > _yMaxEma)
            {
                _yMaxEma = max;
            }
            else
            {
                _yMaxEma = _yMaxEma * 0.95f + max * 0.05f;
            }
        }
        float scale = _yMaxEma;
        if (scale < 0.01f) scale = 0.01f;

        // Horizontal grid lines (at 25/50/75% of range) + axis line.
        _strokePaint.Color = UiTheme.ChartGrid;
        for (int g = 1; g <= 3; g++)
        {
            float gy = plotBottom - plotH * g / 4f;
            canvas.DrawLine(plotX, gy, plotRight, gy, _strokePaint);
        }
        _strokePaint.Color = UiTheme.ChartAxis;
        canvas.DrawLine(plotX, plotBottom, plotRight, plotBottom, _strokePaint);

        // Y-axis max label (top-right of plot).
        _textPaint.TextSize = UiTheme.FontTiny;
        _textPaint.Color = UiTheme.TextMuted;
        string maxLabel = scale.ToString("F2", CultureInfo.InvariantCulture) + " " + unitSuffix;
        canvas.DrawText(maxLabel, plotRight - 64f, bounds.Top + 12f, _textPaint);

        int pixels = (int)plotW;
        if (pixels < 1) pixels = 1;

        // Decimation threshold: when the window spans more than
        // ~2x the plot width, min/max bucketing preserves spikes
        // better than a stride-skipping polyline.
        bool decimate = windowCount > pixels * 2;

        _strokePaint.StrokeWidth = 1.2f;
        for (int mi = 0; mi < metricCount; mi++)
        {
            MetricLine m = metrics[mi];
            ReadOnlySpan<float> src = series.GetSpan(m.MetricIndex);
            int len = windowCount;
            if (windowStart + len > src.Length) len = src.Length - windowStart;
            if (len <= 1) continue;

            _strokePaint.Color = m.Color;

            if (decimate)
            {
                DrawDecimated(canvas, src, windowStart, len, plotX, plotW, plotBottom, plotH, scale, pixels);
            }
            else
            {
                DrawPolyline(canvas, src, windowStart, len, plotX, plotW, plotBottom, plotH, scale);
            }
        }

        // Legend along the right side (inside plot area).
        _textPaint.TextSize = UiTheme.FontTiny;
        float legendX = plotRight - 88f;
        float legendY = plotTop + 12f;
        for (int mi = 0; mi < metricCount; mi++)
        {
            MetricLine m = metrics[mi];
            ReadOnlySpan<float> src = series.GetSpan(m.MetricIndex);
            float lastVal = 0f;
            int lastIdx = windowStart + windowCount - 1;
            if (lastIdx >= 0 && lastIdx < src.Length)
            {
                lastVal = src[lastIdx];
            }
            _fillPaint.Color = m.Color;
            canvas.DrawRect(new SKRect(legendX, legendY - 7f, legendX + 7f, legendY - 1f), _fillPaint);
            _textPaint.Color = UiTheme.TextBody;
            string lbl = m.Label + " " + lastVal.ToString("F2", CultureInfo.InvariantCulture);
            canvas.DrawText(lbl, legendX + 11f, legendY, _textPaint);
            legendY += 11f;
            if (legendY + 4f > plotBottom) break;
        }
    }

    /// <summary>
    /// Polyline path for small windows. Reads directly from the
    /// series span — no allocation, no copy.
    /// </summary>
    private void DrawPolyline(
        SKCanvas canvas,
        ReadOnlySpan<float> src,
        int windowStart,
        int len,
        float plotX,
        float plotW,
        float plotBottom,
        float plotH,
        float scale)
    {
        _path.Reset();
        float denom = len - 1;
        if (denom < 1f) denom = 1f;
        for (int i = 0; i < len; i++)
        {
            float v = src[windowStart + i];
            float px = plotX + plotW * i / denom;
            float py = plotBottom - (v / scale) * plotH;
            if (i == 0) _path.MoveTo(px, py);
            else _path.LineTo(px, py);
        }
        canvas.DrawPath(_path, _strokePaint);
    }

    /// <summary>
    /// Min/max column decimation for windows wider than ~2x the
    /// plot pixel count. Each pixel column becomes a vertical line
    /// from the min to max value of the samples that fall into
    /// that column. Spikes are preserved because the max bucket
    /// captures them.
    /// </summary>
    private void DrawDecimated(
        SKCanvas canvas,
        ReadOnlySpan<float> src,
        int windowStart,
        int len,
        float plotX,
        float plotW,
        float plotBottom,
        float plotH,
        float scale,
        int pixels)
    {
        // Samples per pixel column (at least 1).
        int step = len / pixels;
        if (step < 1) step = 1;

        for (int col = 0; col < pixels; col++)
        {
            int from = col * step;
            int to = from + step;
            if (to > len) to = len;
            if (from >= to) break;

            float mn = float.PositiveInfinity;
            float mx = float.NegativeInfinity;
            int baseIdx = windowStart + from;
            int count = to - from;
            for (int j = 0; j < count; j++)
            {
                float v = src[baseIdx + j];
                if (v < mn) mn = v;
                if (v > mx) mx = v;
            }

            float px = plotX + plotW * col / (float)pixels;
            float pyMin = plotBottom - (mn / scale) * plotH;
            float pyMax = plotBottom - (mx / scale) * plotH;
            // Draw vertical line from min to max; DrawLine also
            // handles the degenerate equal case (single-pixel dot).
            canvas.DrawLine(px, pyMin, px, pyMax, _strokePaint);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _path.Dispose();
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _textPaint.Dispose();
        _disposed = true;
    }
}
