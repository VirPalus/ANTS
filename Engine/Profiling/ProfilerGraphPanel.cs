namespace ANTS;

using System;
using System.Globalization;
using SkiaSharp;

/// <summary>
/// Renders a single profiler sub-graph inside the
/// <see cref="ProfilerGraphWindow"/>: panel background, title,
/// y-axis max label, 3 grid lines, one polyline per visible metric,
/// and a value-legend along the right edge.
///
/// This class is not hot-path for the simulation — it runs only
/// while the profiler graph window is open. Scratch buffers
/// (<c>_scratch</c> and <c>_path</c>) and the three owned paints
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

    private readonly float[] _scratch = new float[ProfilerSeries.SampleCapacity];
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
    /// Draws one sub-graph. Paints are owned by this instance.
    /// Metrics are passed as an (array, count) pair to avoid per-frame
    /// IReadOnlyList boxing. When <paramref name="yAxisLocked"/> is
    /// <c>true</c>, the EMA-smoothed Y ceiling is frozen at its
    /// current value (the Pin/Lock button state in the window).
    /// </summary>
    public void Draw(
        SKCanvas canvas,
        SKRect bounds,
        string title,
        string unitSuffix,
        ProfilerSeries series,
        int wantSamples,
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

        // Find max across visible metrics (last N frames each).
        int want = wantSamples < _scratch.Length ? wantSamples : _scratch.Length;
        float max = 0.01f;
        for (int mi = 0; mi < metricCount; mi++)
        {
            int got = series.CopyLast(metrics[mi].MetricIndex, new Span<float>(_scratch, 0, want));
            for (int i = 0; i < got; i++)
            {
                float v = _scratch[i];
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

        // Subsample stride if sample-count exceeds pixel width.
        int pixels = (int)plotW;
        if (pixels < 1) pixels = 1;

        // Draw each visible metric as a polyline.
        _strokePaint.StrokeWidth = 1.2f;
        for (int mi = 0; mi < metricCount; mi++)
        {
            MetricLine m = metrics[mi];
            int got = series.CopyLast(m.MetricIndex, new Span<float>(_scratch, 0, want));
            if (got <= 1) continue;

            int stride = got / pixels;
            if (stride < 1) stride = 1;

            _path.Reset();
            float denom = got - 1;
            if (denom < 1f) denom = 1f;
            bool first = true;
            for (int i = 0; i < got; i += stride)
            {
                float v = _scratch[i];
                float px = plotX + plotW * i / denom;
                float py = plotBottom - (v / scale) * plotH;
                if (first) { _path.MoveTo(px, py); first = false; }
                else _path.LineTo(px, py);
            }
            // Ensure the last point is drawn even if stride skipped it.
            int lastIdx = got - 1;
            if ((lastIdx % stride) != 0)
            {
                float v = _scratch[lastIdx];
                float px = plotX + plotW;
                float py = plotBottom - (v / scale) * plotH;
                _path.LineTo(px, py);
            }
            _strokePaint.Color = m.Color;
            canvas.DrawPath(_path, _strokePaint);
        }

        // Legend along the right side (inside plot area).
        _textPaint.TextSize = UiTheme.FontTiny;
        float legendX = plotRight - 88f;
        float legendY = plotTop + 12f;
        for (int mi = 0; mi < metricCount; mi++)
        {
            MetricLine m = metrics[mi];
            int got = series.CopyLast(m.MetricIndex, new Span<float>(_scratch, 0, 1));
            float lastVal = got == 0 ? 0f : _scratch[0];
            _fillPaint.Color = m.Color;
            canvas.DrawRect(new SKRect(legendX, legendY - 7f, legendX + 7f, legendY - 1f), _fillPaint);
            _textPaint.Color = UiTheme.TextBody;
            string lbl = m.Label + " " + lastVal.ToString("F2", CultureInfo.InvariantCulture);
            canvas.DrawText(lbl, legendX + 11f, legendY, _textPaint);
            legendY += 11f;
            if (legendY + 4f > plotBottom) break;
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
