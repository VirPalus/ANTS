namespace ANTS;

using System;
using System.Diagnostics;
using System.Drawing;
using SkiaSharp;

/// <summary>
/// Draggable, resizable floating window that hosts 3 profiler
/// sub-graphs (frame timing, world-render split, overlay internals).
///
/// fase-4.12-fix3-v2 overhaul:
///   * Cache invalidation keyed on
///     <see cref="ProfilerSeries.Revision"/>, not
///     <c>Series.Count</c>. The old check broke once the fix3-v1
///     ring buffer saturated at 18000 because <c>Count</c> stopped
///     advancing — the picture cache was never rebuilt and the
///     graph visibly froze.
///   * Horizontal scrollbar + Live button below the content area.
///     Users can drag the thumb to pan through the unbounded
///     history; Live re-enables auto-follow (re-pins to newest).
///   * 9 zoom levels (1s/5s/15s/30s/1m/5m/15m/1h/all) via the
///     expanded <see cref="ProfilerZoomLevel"/> enum.
///   * Panels receive an absolute <c>(windowStart, windowCount)</c>
///     range — scrollback shows historical slices losslessly.
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
///     consumed (inside window / title bar / a button / the
///     scrollbar thumb or track).
///
/// Render cache:
///   * Hot-path <see cref="Draw"/> replays a cached
///     <see cref="SKPicture"/> most frames. The picture is rebuilt
///     only when the window moves/resizes, zoom/lock/scroll state
///     changes, or the series Revision has advanced AND the 50 ms
///     throttle has elapsed.
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

    /// <summary>Height of the scrollbar strip at the bottom of the content area.</summary>
    public const float ScrollbarHeight = 18f;

    /// <summary>Width of the "Live" button flush-right of the scrollbar track.</summary>
    public const float LiveButtonWidth = 44f;

    /// <summary>Minimum rendered thumb width (px) regardless of visible fraction.</summary>
    public const float MinThumbWidth = 24f;

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

    // Scrollbar + Live button geometry (recomputed every rebuild).
    private SKRect _scrollbarTrackRect;
    private SKRect _scrollbarThumbRect;
    private SKRect _liveButtonRect;

    // Y-axis Pin/Lock toggle — frozen EMA ceiling in every panel
    // when true. Toggled via the titlebar Lock button.
    private bool _yAxisLocked;

    // Scrollback state. _autoFollow == true pins the window end to
    // the latest sample. When false, _windowEndIndex is an absolute
    // index into ProfilerSeries fixed by the scrollbar drag.
    private bool _autoFollow = true;
    private long _windowEndIndex;

    // Scrollbar drag state.
    private bool _scrollbarDragging;
    private float _scrollbarDragStartX;
    private long _scrollbarDragStartWindowEnd;
    private long _scrollbarDragMaxEnd;
    private int _scrollbarDragWindowSize;

    // Render cache: rebuild only when layout/data/state changes,
    // and at most once per 50 ms.
    private SKPicture? _cachedPicture;
    private long _lastBuildTimestampTicks;
    private long _lastCachedRevision = -1;
    private float _lastCachedX = float.NaN;
    private float _lastCachedY = float.NaN;
    private float _lastCachedW = float.NaN;
    private float _lastCachedH = float.NaN;
    private ProfilerZoomLevel _lastCachedZoom = (ProfilerZoomLevel)(-1);
    private bool _lastCachedYAxisLocked;
    private bool _lastCachedAutoFollow;
    private long _lastCachedWindowEnd = -1;
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
        _scrollbarDragging = false;
    }

    public void Toggle()
    {
        if (_visible) Hide(); else Show();
    }

    /// <summary>
    /// Consumes mouse-down inside the window, its title bar, any of
    /// its buttons, the scrollbar, the Live button, or the resize
    /// grip. Returns true if consumed.
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
        if (_liveButtonRect.Contains(mx, my))
        {
            _autoFollow = true;
            return true;
        }
        if (_scrollbarThumbRect.Contains(mx, my))
        {
            BeginScrollbarDrag(mx);
            return true;
        }
        if (_scrollbarTrackRect.Contains(mx, my))
        {
            // Click-in-track jumps the thumb to the click position.
            BeginScrollbarDrag(mx);
            JumpScrollbarTo(mx);
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
        else if (_scrollbarDragging)
        {
            UpdateScrollbarDrag(mx);
        }
    }

    public void HandleMouseUp()
    {
        _dragging = false;
        _resizing = false;
        _scrollbarDragging = false;
    }

    /// <summary>
    /// Paints the window via the cached <see cref="SKPicture"/> when
    /// possible. Rebuilds only on layout / Revision / scroll state
    /// changes, throttled to 50 ms. Zero-cost early-out when hidden
    /// or profiler is off.
    /// </summary>
    public void Draw(SKCanvas canvas)
    {
        if (!_visible) return;
        if (!_profiler.IsEnabled) return;

        // Resolve current window indices. autoFollow pins to newest;
        // otherwise _windowEndIndex is honoured (clamped to valid).
        int totalCount = _profiler.Series.Count;
        int wantSamples = _config.GetSampleCount();
        int windowSize = (wantSamples == ProfilerGraphConfig.AllSentinel) ? totalCount : wantSamples;
        if (windowSize > totalCount) windowSize = totalCount;
        if (windowSize < 1) windowSize = 1;

        long windowEnd;
        if (_autoFollow)
        {
            windowEnd = totalCount;
            _windowEndIndex = windowEnd;
        }
        else
        {
            windowEnd = _windowEndIndex;
            if (windowEnd > totalCount) windowEnd = totalCount;
            if (windowEnd < windowSize) windowEnd = windowSize;
            _windowEndIndex = windowEnd;
        }

        long now = Stopwatch.GetTimestamp();
        long revision = _profiler.Series.Revision;

        bool sameLayout = _cachedPicture != null
                       && _lastCachedX == _x
                       && _lastCachedY == _y
                       && _lastCachedW == _w
                       && _lastCachedH == _h
                       && _lastCachedZoom == _config.Zoom
                       && _lastCachedYAxisLocked == _yAxisLocked
                       && _lastCachedAutoFollow == _autoFollow
                       && _lastCachedWindowEnd == windowEnd;
        bool withinThrottle = (now - _lastBuildTimestampTicks) < RebuildIntervalStopwatchTicks;
        bool dataUnchanged = revision == _lastCachedRevision;

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
            DrawFrame(rec, windowEnd, windowSize, totalCount);
            newPicture = recorder.EndRecording();
        }

        _cachedPicture?.Dispose();
        _cachedPicture = newPicture;
        _lastBuildTimestampTicks = now;
        _lastCachedRevision = revision;
        _lastCachedX = _x;
        _lastCachedY = _y;
        _lastCachedW = _w;
        _lastCachedH = _h;
        _lastCachedZoom = _config.Zoom;
        _lastCachedYAxisLocked = _yAxisLocked;
        _lastCachedAutoFollow = _autoFollow;
        _lastCachedWindowEnd = windowEnd;

        canvas.DrawPicture(_cachedPicture);
    }

    /// <summary>
    /// Records one full frame of the window: background, title bar,
    /// buttons, sub-graphs, scrollbar, resize grip. Called into an
    /// <see cref="SKPictureRecorder"/> canvas, never directly to the
    /// screen — <see cref="Draw"/> replays the resulting picture.
    /// </summary>
    private void DrawFrame(SKCanvas canvas, long windowEnd, int windowSize, int totalCount)
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

        // Content layout: below title, above scrollbar + resize grip.
        float contentTop = _y + TitleBarHeight + 4f;
        float scrollbarTop = _y + _h - ResizeHandleSize - ScrollbarHeight - 2f;
        float contentBottom = scrollbarTop - 2f;
        float contentH = contentBottom - contentTop;
        if (contentH < 60f) return;

        float interPanelGap = 4f;
        float panelH = (contentH - 2f * interPanelGap) / 3f;
        float panelX = _x + 6f;
        float panelW = _w - 12f;

        long windowStart = windowEnd - windowSize;
        if (windowStart < 0) windowStart = 0;
        int windowCount = (int)(windowEnd - windowStart);
        int startInt = (int)windowStart;

        SKRect rect1 = new SKRect(panelX, contentTop, panelX + panelW, contentTop + panelH);
        _panel1.Draw(canvas, rect1, "Frame timing", "ms", _profiler.Series, startInt, windowCount,
            _panel1Metrics, _panel1Count, _yAxisLocked);

        SKRect rect2 = new SKRect(panelX, rect1.Bottom + interPanelGap, panelX + panelW, rect1.Bottom + interPanelGap + panelH);
        _panel2.Draw(canvas, rect2, "World renderers", "ms", _profiler.Series, startInt, windowCount,
            _panel2Metrics, _panel2Count, _yAxisLocked);

        SKRect rect3 = new SKRect(panelX, rect2.Bottom + interPanelGap, panelX + panelW, rect2.Bottom + interPanelGap + panelH);
        _panel3.Draw(canvas, rect3, "Overlay internals", "us", _profiler.Series, startInt, windowCount,
            _panel3Metrics, _panel3Count, _yAxisLocked);

        // Scrollbar strip.
        DrawScrollbar(canvas, scrollbarTop, windowEnd, windowSize, totalCount);

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

    /// <summary>
    /// Renders the bottom scrollbar strip: track, thumb, and the
    /// flush-right Live button. Thumb width encodes the visible
    /// fraction of total history; position encodes the scroll
    /// offset. Live highlights green while <see cref="_autoFollow"/>
    /// is engaged.
    /// </summary>
    private void DrawScrollbar(SKCanvas canvas, float stripTop, long windowEnd, int windowSize, int totalCount)
    {
        float leftPad = 6f;
        float rightPad = 6f;
        float liveButtonGap = 4f;
        float stripLeft = _x + leftPad;
        float stripRight = _x + _w - rightPad;
        float liveLeft = stripRight - LiveButtonWidth;
        float trackLeft = stripLeft;
        float trackRight = liveLeft - liveButtonGap;
        if (trackRight <= trackLeft + MinThumbWidth)
        {
            trackRight = trackLeft + MinThumbWidth;
        }

        _scrollbarTrackRect = new SKRect(trackLeft, stripTop, trackRight, stripTop + ScrollbarHeight);
        _liveButtonRect = new SKRect(liveLeft, stripTop, liveLeft + LiveButtonWidth, stripTop + ScrollbarHeight);

        // Track background.
        _fillPaint.Color = UiTheme.BgPanelHover;
        canvas.DrawRect(_scrollbarTrackRect, _fillPaint);
        _strokePaint.Color = UiTheme.BorderSubtle;
        _strokePaint.StrokeWidth = UiTheme.BorderThin;
        canvas.DrawRect(_scrollbarTrackRect, _strokePaint);

        // Thumb geometry.
        float trackWidth = trackRight - trackLeft;
        float thumbW;
        float thumbLeft;
        if (totalCount <= 0)
        {
            thumbW = trackWidth;
            thumbLeft = trackLeft;
        }
        else
        {
            float frac = (float)windowSize / (float)totalCount;
            if (frac > 1f) frac = 1f;
            thumbW = trackWidth * frac;
            if (thumbW < MinThumbWidth) thumbW = MinThumbWidth;
            if (thumbW > trackWidth) thumbW = trackWidth;

            // Scroll position: where along the track does the
            // window end sit? Range [windowSize .. totalCount].
            long minEnd = windowSize;
            long maxEnd = totalCount;
            float denom = (float)(maxEnd - minEnd);
            float pos = denom <= 0f ? 1f : (float)(windowEnd - minEnd) / denom;
            if (pos < 0f) pos = 0f;
            if (pos > 1f) pos = 1f;
            thumbLeft = trackLeft + (trackWidth - thumbW) * pos;
        }
        _scrollbarThumbRect = new SKRect(thumbLeft, stripTop + 2f, thumbLeft + thumbW, stripTop + ScrollbarHeight - 2f);

        _fillPaint.Color = _scrollbarDragging ? UiTheme.BgPanelActive : UiTheme.BorderStrong;
        canvas.DrawRect(_scrollbarThumbRect, _fillPaint);

        // Live button.
        _fillPaint.Color = _autoFollow ? new SKColor(70, 140, 70) : UiTheme.BgPanelHover;
        canvas.DrawRect(_liveButtonRect, _fillPaint);
        _strokePaint.Color = UiTheme.BorderSubtle;
        canvas.DrawRect(_liveButtonRect, _strokePaint);
        _textPaint.TextSize = UiTheme.FontTiny;
        _textPaint.Color = _autoFollow ? UiTheme.TextStrong : UiTheme.TextMuted;
        canvas.DrawText("Live", _liveButtonRect.Left + 10f, _liveButtonRect.Top + 12f, _textPaint);
    }

    /// <summary>
    /// Begins dragging the scrollbar thumb — captures the anchor
    /// and current window geometry so subsequent
    /// <see cref="UpdateScrollbarDrag"/> calls can map cursor delta
    /// to a new <see cref="_windowEndIndex"/>.
    /// </summary>
    private void BeginScrollbarDrag(int mx)
    {
        _scrollbarDragging = true;
        _autoFollow = false;
        _scrollbarDragStartX = mx;
        _scrollbarDragStartWindowEnd = _windowEndIndex;

        int totalCount = _profiler.Series.Count;
        int wantSamples = _config.GetSampleCount();
        int windowSize = (wantSamples == ProfilerGraphConfig.AllSentinel) ? totalCount : wantSamples;
        if (windowSize > totalCount) windowSize = totalCount;
        if (windowSize < 1) windowSize = 1;
        _scrollbarDragWindowSize = windowSize;
        _scrollbarDragMaxEnd = totalCount;
    }

    /// <summary>
    /// Maps a mouse-move delta since <see cref="BeginScrollbarDrag"/>
    /// into a new <see cref="_windowEndIndex"/>. Clamped so the
    /// window never exceeds the bounds of the recorded series.
    /// </summary>
    private void UpdateScrollbarDrag(int mx)
    {
        float trackWidth = _scrollbarTrackRect.Width;
        if (trackWidth <= 0f) return;
        float dx = mx - _scrollbarDragStartX;
        long minEnd = _scrollbarDragWindowSize;
        long maxEnd = _scrollbarDragMaxEnd;
        if (maxEnd < minEnd) maxEnd = minEnd;
        long span = maxEnd - minEnd;
        if (span <= 0)
        {
            _windowEndIndex = maxEnd;
            return;
        }
        long delta = (long)(dx / trackWidth * span);
        long newEnd = _scrollbarDragStartWindowEnd + delta;
        if (newEnd < minEnd) newEnd = minEnd;
        if (newEnd > maxEnd) newEnd = maxEnd;
        _windowEndIndex = newEnd;
    }

    /// <summary>
    /// Jumps the window end so that the thumb center aligns with
    /// the click position inside the track. Used for click-in-track
    /// paging.
    /// </summary>
    private void JumpScrollbarTo(int mx)
    {
        float trackWidth = _scrollbarTrackRect.Width;
        if (trackWidth <= 0f) return;
        float rel = (mx - _scrollbarTrackRect.Left) / trackWidth;
        if (rel < 0f) rel = 0f;
        if (rel > 1f) rel = 1f;
        long minEnd = _scrollbarDragWindowSize;
        long maxEnd = _scrollbarDragMaxEnd;
        if (maxEnd < minEnd) maxEnd = minEnd;
        long span = maxEnd - minEnd;
        long newEnd = minEnd + (long)(rel * span);
        if (newEnd < minEnd) newEnd = minEnd;
        if (newEnd > maxEnd) newEnd = maxEnd;
        _windowEndIndex = newEnd;
        _scrollbarDragStartX = mx;
        _scrollbarDragStartWindowEnd = newEnd;
    }

    /// <summary>
    /// Populates the three panel metric arrays from the current
    /// <see cref="_config"/> toggles. Only metrics whose Show flag
    /// is true are drawn. Indices map to
    /// <see cref="ProfilerSeries"/> metric columns.
    /// </summary>
    private void BuildActiveMetrics()
    {
        _panel1Count = 0;
        if (_config.ShowFrameMs)   _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(0, "frame", UiTheme.ChartLine1);
        if (_config.ShowSimMs)     _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(1, "sim",   UiTheme.ChartLine2);
        if (_config.ShowGridMs)    _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(2, "grid",  UiTheme.ChartLine3);
        if (_config.ShowOverlayMs) _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(3, "ovly",  UiTheme.ChartLine4);
        if (_config.ShowAntsMs)    _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(6, "ants",  UiTheme.ChartLine5);
        if (_config.ShowStatsMs)   _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(7, "stats", UiTheme.ChartLine6);
        if (_config.ShowHudMs)     _panel1Metrics[_panel1Count++] = new ProfilerGraphPanel.MetricLine(8, "hud",   UiTheme.ChartLine7);

        _panel2Count = 0;
        if (_config.ShowGridMs)    _panel2Metrics[_panel2Count++] = new ProfilerGraphPanel.MetricLine(2, "grid",  UiTheme.ChartLine3);
        if (_config.ShowFoodMs)    _panel2Metrics[_panel2Count++] = new ProfilerGraphPanel.MetricLine(4, "food",  UiTheme.ChartLine4);
        if (_config.ShowNestsMs)   _panel2Metrics[_panel2Count++] = new ProfilerGraphPanel.MetricLine(5, "nests", UiTheme.ChartLine5);

        _panel3Count = 0;
        if (_config.ShowArrayClearUs)  _panel3Metrics[_panel3Count++] = new ProfilerGraphPanel.MetricLine(9,  "clear",  UiTheme.ChartLine1);
        if (_config.ShowInnerLoopUs)   _panel3Metrics[_panel3Count++] = new ProfilerGraphPanel.MetricLine(10, "inner",  UiTheme.ChartLine2);
        if (_config.ShowMarshalCopyUs) _panel3Metrics[_panel3Count++] = new ProfilerGraphPanel.MetricLine(11, "copy",   UiTheme.ChartLine3);
        if (_config.ShowDrawBitmapUs)  _panel3Metrics[_panel3Count++] = new ProfilerGraphPanel.MetricLine(12, "bitmap", UiTheme.ChartLine4);
    }

    private bool ContainsPoint(int mx, int my)
    {
        return mx >= _x && mx <= _x + _w && my >= _y && my <= _y + _h;
    }

    private bool InResizeGrip(int mx, int my)
    {
        return mx >= _x + _w - ResizeHandleSize
            && mx <= _x + _w
            && my >= _y + _h - ResizeHandleSize
            && my <= _y + _h;
    }

    private void ClampToClient()
    {
        Size size = _clientSizeGetter();
        if (_x < 0f) _x = 0f;
        if (_y < 0f) _y = 0f;
        if (_x + _w > size.Width) _x = MathF.Max(0f, size.Width - _w);
        if (_y + _h > size.Height) _y = MathF.Max(0f, size.Height - _h);
    }

    private void ClampSize()
    {
        Size size = _clientSizeGetter();
        if (_x + _w > size.Width) _w = MathF.Max(MinWidth, size.Width - _x);
        if (_y + _h > size.Height) _h = MathF.Max(MinHeight, size.Height - _y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cachedPicture?.Dispose();
        _panel1.Dispose();
        _panel2.Dispose();
        _panel3.Dispose();
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _textPaint.Dispose();
        _disposed = true;
    }
}
