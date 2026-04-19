namespace ANTS;

using System;
using System.Diagnostics;
using System.Drawing;
using SkiaSharp;

/// <summary>
/// Draggable, resizable floating window that hosts 3 profiler
/// sub-graphs (frame timing, world-render split, overlay internals).
///
/// Lifecycle:
///   * Constructed once in <see cref="Engine"/>.
///   * Hidden by default; <see cref="Show"/> is called when the user
///     toggles the profiler on via the top-bar Profile button or F2.
///   * Subsequent shows restore the prior position (in-memory only).
///
/// Input routing:
///   * <see cref="InputRouter"/> calls
///     <see cref="HandleMouseDown"/>/<see cref="HandleMouseMove"/>/<see cref="HandleMouseUp"/>
///     before any other UI target when the window is visible.
///   * Returns <c>true</c> from HandleMouseDown when the click is
///     consumed (inside window / on title bar / on a button).
///
/// Render cache:
///   * Hot-path <see cref="Draw"/> replays a cached
///     <see cref="SKPicture"/> most frames. The picture is rebuilt
///     only when the window moves/resizes, the zoom level changes,
///     or the profiler series has advanced AND the 50 ms throttle
///     has elapsed. Reduces profiler paint cost from ~44 us/frame
///     (rebuild every frame) to ~15 us/frame (rebuild 1-in-3 at
///     60 FPS, replay between).
///
/// Y-axis lock:
///   * The Pin/Lock button in the titlebar toggles
///     <see cref="_yAxisLocked"/>. When locked, each panel freezes
///     its EMA-smoothed ceiling so cross-session comparisons are
///     stable. Spikes above the frozen ceiling clip — intentional.
///
/// Constraints:
///   * Min 600x400, bounded by the current client size.
///   * First-show position centers the window in the client area.
/// </summary>
public sealed class ProfilerGraphWindow : IDisposable
{
    public const float MinWidth = 600f;
    public const float MinHeight = 400f;
    public const float TitleBarHeight = 24f;
    public const float ResizeHandleSize = 14f;

    private readonly FrameProfiler _profiler;
    private readonly Func<Size> _clientSizeGetter;

    private readonly ProfilerGraphConfig _config = new ProfilerGraphConfig();
    private readonly ProfilerGraphPanel _panel1 = new ProfilerGraphPanel();
    private readonly ProfilerGraphPanel _panel2 = new ProfilerGraphPanel();
    private readonly ProfilerGraphPanel _panel3 = new ProfilerGraphPanel();

    // Pre-allocated metric arrays (reused every frame).
    private readonly ProfilerGraphPanel.MetricLine[] _panel1Metrics = new ProfilerGraphPanel.MetricLine[7];
    private int _panel1Count;
    private readonly ProfilerGraphPanel.MetricLine[] _panel2Metrics = new ProfilerGraphPanel.MetricLine[3];
    private int _panel2Count;
    private readonly ProfilerGraphPanel.MetricLine[] _panel3Metrics = new ProfilerGraphPanel.MetricLine[4];
    private int _panel3Count;

    // Window-scoped paints for title / buttons / resize grip only.
    // Panel-scoped paints live on ProfilerGraphPanel (isolation).
    private readonly SKPaint _fillPaint;
    private readonly SKPaint _strokePaint;
    private readonly SKPaint _textPaint;

    // Window state.
    private float _x;
    private float _y;
    private float _w = MinWidth;
    private float _h = MinHeight;
    private bool _visible;
    private bool _firstShow = true;

    // Drag / resize state.
    private bool _dragging;
    private float _dragOffsetX;
    private float _dragOffsetY;
    private bool _resizing;
    private float _resizeAnchorX;
    private float _resizeAnchorY;
    private float _resizeStartW;
    private float _resizeStartH;

    // Button rects recomputed every full rebuild.
    private SKRect _closeRect;
    private SKRect _zoomOutRect;
    private SKRect _zoomInRect;
    private SKRect _lockRect;

    // Y-axis Pin/Lock toggle — frozen EMA ceiling in every panel
    // when true. Toggled via the titlebar Lock button.
    private bool _yAxisLocked;

    // Render cache: rebuild only when the layout or data changes,
    // and at most once per 50 ms.
    private SKPicture? _cachedPicture;
    private long _lastBuildTimestampTicks;
    private int _lastCachedSeriesCount = -1;
    private float _lastCachedX = float.NaN;
    private float _lastCachedY = float.NaN;
    private float _lastCachedW = float.NaN;
    private float _lastCachedH = float.NaN;
    private ProfilerZoomLevel _lastCachedZoom = (ProfilerZoomLevel)(-1);
    private bool _lastCachedYAxisLocked;
    private static readonly long RebuildIntervalStopwatchTicks = Stopwatch.Frequency / 20; // 50 ms

    private bool _disposed;

    public ProfilerGraphWindow(FrameProfiler profiler, Func<Size> clientSizeGetter)
    {
        _profiler = profiler;
        _clientSizeGetter = clientSizeGetter;

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
        _textPaint.TextSize = UiTheme.FontSmall;
    }

    public bool IsVisible => _visible;

    /// <summary>
    /// Shows the window. First invocation centers it in the client
    /// area; subsequent invocations reuse the last position.
    /// </summary>
    public void Show()
    {
        if (_firstShow)
        {
            Size size = _clientSizeGetter();
            _x = MathF.Max(8f, (size.Width - _w) / 2f);
            _y = MathF.Max(8f, (size.Height - _h) / 2f);
            _firstShow = false;
        }
        _visible = true;
        ClampToClient();
    }

    public void Hide()
    {
        _visible = false;
        _dragging = false;
        _resizing = false;
    }

    public void Toggle()
    {
        if (_visible) Hide(); else Show();
    }

    /// <summary>
    /// Consumes mouse-down inside the window, its title bar, any of
    /// its buttons, or the resize grip. Returns true if consumed.
    /// </summary>
    public bool HandleMouseDown(int mx, int my)
    {
        if (!_visible) return false;
        if (!ContainsPoint(mx, my)) return false;

        if (_closeRect.Contains(mx, my))
        {
            Hide();
            return true;
        }
        if (_zoomInRect.Contains(mx, my))
        {
            _config.ZoomIn();
            return true;
        }
        if (_zoomOutRect.Contains(mx, my))
        {
            _config.ZoomOut();
            return true;
        }
        if (_lockRect.Contains(mx, my))
        {
            _yAxisLocked = !_yAxisLocked;
            return true;
        }
        if (InResizeGrip(mx, my))
        {
            _resizing = true;
            _resizeAnchorX = mx;
            _resizeAnchorY = my;
            _resizeStartW = _w;
            _resizeStartH = _h;
            return true;
        }
        if (my < _y + TitleBarHeight)
        {
            _dragging = true;
            _dragOffsetX = mx - _x;
            _dragOffsetY = my - _y;
            return true;
        }
        // Click inside body — consume so it doesn't propagate to the
        // selection controller underneath.
        return true;
    }

    public void HandleMouseMove(int mx, int my)
    {
        if (!_visible) return;
        if (_dragging)
        {
            _x = mx - _dragOffsetX;
            _y = my - _dragOffsetY;
            ClampToClient();
        }
        else if (_resizing)
        {
            float dx = mx - _resizeAnchorX;
            float dy = my - _resizeAnchorY;
            _w = MathF.Max(MinWidth, _resizeStartW + dx);
            _h = MathF.Max(MinHeight, _resizeStartH + dy);
            ClampSize();
        }
    }

    public void HandleMouseUp()
    {
        _dragging = false;
        _resizing = false;
    }

    /// <summary>
    /// Paints the window via the cached <see cref="SKPicture"/> when
    /// possible (see class-level Render cache note). Rebuilds the
    /// picture only on layout/data/throttle changes. Zero-cost
    /// early-out when hidden or profiler is off.
    /// </summary>
    public void Draw(SKCanvas canvas)
    {
        if (!_visible) return;
        if (!_profiler.IsEnabled) return;

        long now = Stopwatch.GetTimestamp();
        int seriesCount = _profiler.Series.Count;

        bool sameLayout = _cachedPicture != null
                       && _lastCachedX == _x
                       && _lastCachedY == _y
                       && _lastCachedW == _w
                       && _lastCachedH == _h
                       && _lastCachedZoom == _config.Zoom
                       && _lastCachedYAxisLocked == _yAxisLocked;
        bool withinThrottle = (now - _lastBuildTimestampTicks) < RebuildIntervalStopwatchTicks;
        bool dataUnchanged = seriesCount == _lastCachedSeriesCount;

        if (sameLayout && (dataUnchanged || withinThrottle))
        {
            canvas.DrawPicture(_cachedPicture);
            return;
        }

        // Rebuild picture.
        SKRect winRect = new SKRect(_x, _y, _x + _w, _y + _h);
        SKPicture newPicture;
        using (SKPictureRecorder recorder = new SKPictureRecorder())
        {
            SKCanvas rec = recorder.BeginRecording(winRect);
            DrawFrame(rec);
            newPicture = recorder.EndRecording();
        }

        _cachedPicture?.Dispose();
        _cachedPicture = newPicture;
        _lastBuildTimestampTicks = now;
        _lastCachedSeriesCount = seriesCount;
        _lastCachedX = _x;
        _lastCachedY = _y;
        _lastCachedW = _w;
        _lastCachedH = _h;
        _lastCachedZoom = _config.Zoom;
        _lastCachedYAxisLocked = _yAxisLocked;

        canvas.DrawPicture(_cachedPicture);
    }

    /// <summary>
    /// Records one full frame of the window: background, title bar,
    /// buttons, sub-graphs, resize grip. Called into an
    /// <see cref="SKPictureRecorder"/> canvas, never directly to the
    /// screen — <see cref="Draw"/> replays the resulting picture.
    /// </summary>
    private void DrawFrame(SKCanvas canvas)
    {
        // Window background + border.
        SKRect winRect = new SKRect(_x, _y, _x + _w, _y + _h);
        _fillPaint.Color = UiTheme.BgPanel;
        canvas.DrawRect(winRect, _fillPaint);
        _strokePaint.Color = UiTheme.BorderMedium;
        _strokePaint.StrokeWidth = UiTheme.BorderNormal;
        canvas.DrawRect(winRect, _strokePaint);

        // Title bar.
        SKRect titleRect = new SKRect(_x, _y, _x + _w, _y + TitleBarHeight);
        _fillPaint.Color = UiTheme.BgPanelActive;
        canvas.DrawRect(titleRect, _fillPaint);
        _textPaint.Color = UiTheme.TextStrong;
        _textPaint.TextSize = UiTheme.FontSmall;
        canvas.DrawText("Profiler Graphs", _x + 8f, _y + 16f, _textPaint);

        // Buttons (close / zoom-in / zoom-out / lock from right to left).
        float btnY = _y + 3f;
        float btnH = TitleBarHeight - 6f;
        float btnW = 22f;
        _closeRect = new SKRect(_x + _w - btnW - 4f, btnY, _x + _w - 4f, btnY + btnH);
        _zoomInRect = new SKRect(_closeRect.Left - btnW - 4f, btnY, _closeRect.Left - 4f, btnY + btnH);
        _zoomOutRect = new SKRect(_zoomInRect.Left - btnW - 2f, btnY, _zoomInRect.Left - 2f, btnY + btnH);
        _lockRect = new SKRect(_zoomOutRect.Left - btnW - 6f, btnY, _zoomOutRect.Left - 6f, btnY + btnH);

        _fillPaint.Color = UiTheme.BgPanelHover;
        canvas.DrawRect(_closeRect, _fillPaint);
        canvas.DrawRect(_zoomInRect, _fillPaint);
        canvas.DrawRect(_zoomOutRect, _fillPaint);

        // Lock button: highlight background + amber text when engaged.
        _fillPaint.Color = _yAxisLocked ? UiTheme.BgPanelActive : UiTheme.BgPanelHover;
        canvas.DrawRect(_lockRect, _fillPaint);

        _textPaint.Color = UiTheme.TextStrong;
        _textPaint.TextSize = UiTheme.FontSmall;
        canvas.DrawText("X", _closeRect.Left + 7f, _closeRect.Top + 13f, _textPaint);
        canvas.DrawText("+", _zoomInRect.Left + 7f, _zoomInRect.Top + 13f, _textPaint);
        canvas.DrawText("-", _zoomOutRect.Left + 8f, _zoomOutRect.Top + 13f, _textPaint);

        _textPaint.Color = _yAxisLocked ? new SKColor(255, 180, 80) : UiTheme.TextStrong;
        canvas.DrawText("L", _lockRect.Left + 7f, _lockRect.Top + 13f, _textPaint);

        _textPaint.TextSize = UiTheme.FontTiny;
        _textPaint.Color = UiTheme.TextMuted;
        canvas.DrawText("zoom " + _config.ZoomLabel, _lockRect.Left - 64f, _y + 16f, _textPaint);

        // Populate metric arrays for the three panels.
        BuildActiveMetrics();

        // Content layout (below title, above resize grip).
        float contentTop = _y + TitleBarHeight + 4f;
        float contentBottom = _y + _h - ResizeHandleSize - 2f;
        float contentH = contentBottom - contentTop;
        if (contentH < 60f) return;

        float interPanelGap = 4f;
        float panelH = (contentH - 2f * interPanelGap) / 3f;
        float panelX = _x + 6f;
        float panelW = _w - 12f;

        int wantSamples = _config.GetSampleCount();

        SKRect rect1 = new SKRect(panelX, contentTop, panelX + panelW, contentTop + panelH);
        _panel1.Draw(canvas, rect1, "Frame timing", "ms", _profiler.Series, wantSamples,
            _panel1Metrics, _panel1Count, _yAxisLocked);

        SKRect rect2 = new SKRect(panelX, rect1.Bottom + interPanelGap, panelX + panelW, rect1.Bottom + interPanelGap + panelH);
        _panel2.Draw(canvas, rect2, "World renderers", "ms", _profiler.Series, wantSamples,
            _panel2Metrics, _panel2Count, _yAxisLocked);

        SKRect rect3 = new SKRect(panelX, rect2.Bottom + interPanelGap, panelX + panelW, rect2.Bottom + interPanelGap + panelH);
        _panel3.Draw(canvas, rect3, "Overlay internals", "us", _profiler.Series, wantSamples,
            _panel3Metrics, _panel3Count, _yAxisLocked);

        // Resize grip (bottom-right corner triangle).
        _fillPaint.Color = UiTheme.BorderStrong;
        SKPoint p0 = new SKPoint(_x + _w - ResizeHandleSize, _y + _h - 2f);
        SKPoint p1 = new SKPoint(_x + _w - 2f, _y + _h - ResizeHandleSize);
        SKPoint p2 = new SKPoint(_x + _w - 2f, _y + _h - 2f);
        using (SKPath grip = new SKPath())
        {
            grip.MoveTo(p0);
            grip.LineTo(p1);
            grip.LineTo(p2);
            grip.Close();
            canvas.DrawPath(grip, _fillPaint);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cachedPicture?.Dispose();
        _cachedPicture = null;
        _panel1.Dispose();
        _panel2.Dispose();
        _panel3.Dispose();
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _textPaint.Dispose();
        _disposed = true;
    }

    private bool ContainsPoint(float mx, float my)
    {
        return mx >= _x && mx <= _x + _w && my >= _y && my <= _y + _h;
    }

    private bool InResizeGrip(float mx, float my)
    {
        float right = _x + _w;
        float bottom = _y + _h;
        return mx >= right - ResizeHandleSize && my >= bottom - ResizeHandleSize
            && mx <= right && my <= bottom;
    }

    private void ClampToClient()
    {
        Size size = _clientSizeGetter();
        float maxX = size.Width - _w;
        float maxY = size.Height - _h;
        if (maxX < 0f) maxX = 0f;
        if (maxY < 0f) maxY = 0f;
        if (_x < 0f) _x = 0f;
        if (_y < 0f) _y = 0f;
        if (_x > maxX) _x = maxX;
        if (_y > maxY) _y = maxY;
    }

    private void ClampSize()
    {
        Size size = _clientSizeGetter();
        float maxW = size.Width - _x - 4f;
        float maxH = size.Height - _y - 4f;
        if (maxW < MinWidth) maxW = MinWidth;
        if (maxH < MinHeight) maxH = MinHeight;
        if (_w > maxW) _w = maxW;
        if (_h > maxH) _h = maxH;
    }

    private void BuildActiveMetrics()
    {
        _panel1Count = 0;
        _panel2Count = 0;
        _panel3Count = 0;

        // Panel 1 — frame timing (ms).
        SKColor frameCol   = new SKColor(230, 230, 240);
        SKColor simCol     = new SKColor(120, 200, 255);
        SKColor gridCol    = new SKColor(255, 180, 100);
        SKColor overlayCol = new SKColor(255, 120, 180);
        SKColor antsCol    = new SKColor(120, 255, 160);
        SKColor statsCol   = new SKColor(200, 140, 255);
        SKColor hudCol     = new SKColor(255, 220, 120);
        if (_config.ShowFrameMs)   _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(0, "Frame", frameCol);
        if (_config.ShowSimMs)     _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(1, "Sim", simCol);
        if (_config.ShowGridMs)    _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(2, "Grid", gridCol);
        if (_config.ShowOverlayMs) _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(3, "Overlay", overlayCol);
        if (_config.ShowAntsMs)    _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(6, "Ants", antsCol);
        if (_config.ShowStatsMs)   _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(7, "Stats", statsCol);
        if (_config.ShowHudMs)     _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(8, "Hud", hudCol);

        // Panel 2 — world renderers (Grid/Food/Nests) in ms.
        SKColor foodCol    = new SKColor(255, 140, 140);
        SKColor nestsCol   = new SKColor(200, 200, 120);
        if (_config.ShowGridMs)    _panel2Metrics[_panel2Count++] = new ProfilerGraphPanel.MetricLine(2, "Grid", gridCol);
        if (_config.ShowFoodMs)    _panel2Metrics[_panel2Count++] = new ProfilerGraphPanel.MetricLine(4, "Food", foodCol);
        if (_config.ShowNestsMs)   _panel2Metrics[_panel2Count++] = new ProfilerGraphPanel.MetricLine(5, "Nests", nestsCol);

        // Panel 3 — overlay internals (µs).
        SKColor clearCol   = new SKColor(120, 220, 220);
        SKColor innerCol   = new SKColor(255, 160, 60);
        SKColor marshalCol = new SKColor(180, 120, 255);
        SKColor bitmapCol  = new SKColor(120, 255, 220);
        if (_config.ShowArrayClearUs)  _panel3Metrics[_panel3Count++] = new ProfilerGraphPanel.MetricLine(9, "Clear", clearCol);
        if (_config.ShowInnerLoopUs)   _panel3Metrics[_panel3Count++] = new ProfilerGraphPanel.MetricLine(10, "Inner", innerCol);
        if (_config.ShowMarshalCopyUs) _panel3Metrics[_panel3Count++] = new ProfilerGraphPanel.MetricLine(11, "Marshal", marshalCol);
        if (_config.ShowDrawBitmapUs)  _panel3Metrics[_panel3Count++] = new ProfilerGraphPanel.MetricLine(12, "Bitmap", bitmapCol);
    }
}
