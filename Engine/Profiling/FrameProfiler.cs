namespace ANTS;

using System;
using System.Diagnostics;
using System.Threading;

/// <summary>
/// Phase slots measured per frame. Integer values are contiguous
/// (0..Count-1) to enable direct array indexing in the hot path
/// (BeginPhase/EndPhase) instead of switch-case.
/// </summary>
public enum ProfilePhase
{
    /// <summary>Simulation advance (inside Engine.Tick, around _sim.Advance).</summary>
    Sim = 0,

    /// <summary>Legacy aggregate WorldDraw (base+food+nests+lines). Not populated in variant-A (kept for CSV-column stability); see <see cref="GridDraw"/>/<see cref="FoodDraw"/>/<see cref="NestsDraw"/>.</summary>
    WorldDraw = 1,

    /// <summary>Pheromone overlay draw (inside OnPaint, conditional on _showPheromones).</summary>
    OverlayDraw = 2,

    /// <summary>All-colony ants draw (inside OnPaint).</summary>
    AntsDraw = 3,

    /// <summary>Stats panel picture draw (inside OnPaint).</summary>
    StatsDraw = 4,

    /// <summary>HUD picture draw (inside OnPaint).</summary>
    HudDraw = 5,

    /// <summary>OnPaint wall-clock total (inside OnPaint outer wrap).</summary>
    PaintTotal = 6,

    /// <summary>Grid base + walls + lines (WorldRenderer sub-phase).</summary>
    GridDraw = 7,

    /// <summary>Food cells (WorldRenderer sub-phase).</summary>
    FoodDraw = 8,

    /// <summary>Colony nests (WorldRenderer sub-phase).</summary>
    NestsDraw = 9,

    /// <summary>Pheromone overlay Array.Clear (OverlayRenderer sub-phase).</summary>
    ArrayClear = 10,

    /// <summary>Pheromone overlay nested for x/y loop (OverlayRenderer sub-phase).</summary>
    InnerLoop = 11,

    /// <summary>Pheromone overlay Marshal.Copy (OverlayRenderer sub-phase).</summary>
    MarshalCopy = 12,

    /// <summary>Pheromone overlay canvas.DrawBitmap (OverlayRenderer sub-phase).</summary>
    DrawBitmap = 13,

    /// <summary>Canvas setup (canvas.Save + _camera.Apply in OnSkPaintSurface). fase-4.12-diag.</summary>
    CanvasSetup = 14,

    /// <summary>Placement ghost draw (PlacementController.DrawGhost). fase-4.12-diag.</summary>
    Placement = 15,

    /// <summary>Selection overlay + Restore + info-panel (SelectionController). fase-4.12-diag.</summary>
    Selection = 16,

    /// <summary>Top-bar picture + buttons picture record/draw (Accumulate). fase-4.12-diag.</summary>
    Buttons = 17,

    /// <summary>ProfilerUI HUD panel + ProfilerGraphWindow draw. fase-4.12-diag.</summary>
    ProfilerWindow = 18,
}

/// <summary>
/// Per-frame timing collector with 60 Hz MAX-aggregating sampler.
///
/// fase-4.12-fix3-v2 redesign:
///   * Sampling cadence is <b>decoupled</b> from render FPS via
///     <see cref="ProfilerSampleClock"/>. The clock ticks at a fixed
///     60 Hz (16.67 ms) regardless of how fast the game renders.
///   * Between clock ticks, per-phase durations from every rendered
///     frame are aggregated with <see cref="Math.Max(long, long)"/>
///     into a pending "bucket". This preserves worst-case spikes
///     rather than averaging them away.
///   * On a clock tick, the bucket is flushed: one
///     <see cref="ProfileSample"/> is pushed to the ring buffer and
///     one row appended to the <see cref="ProfilerSeries"/>. The
///     sample records the number of render frames aggregated into
///     the bucket via <see cref="ProfileSample.RenderFramesAggregated"/>.
///   * Under render FPS &gt; 60 Hz: multiple render frames merge
///     into a single 60 Hz bucket.
///   * Under render FPS &lt; 60 Hz: each render frame triggers a
///     flush (sample rate is capped by render rate — expected).
///
/// Contract:
///   * Begin/End* called ONLY from the UI thread (Engine.Tick +
///     OnPaint). Single producer; no synchronization on hot path.
///     DEBUG builds assert this with Environment.CurrentManagedThreadId.
///   * Zero-cost when <see cref="IsEnabled"/> is false: every public
///     method early-returns after a single bool check. No
///     <see cref="Stopwatch"/> calls, no array writes, no file I/O.
///   * When enabled, hot-path overhead is ~100 ns per Begin/End
///     pair (one Stopwatch.GetTimestamp + one array store).
///     EndFrame performs MAX aggregation and occasionally (~60 Hz)
///     flushes the aggregated bucket to the ring buffer.
///
/// Accumulate pair (fase-4.12): <see cref="AccumulatePhaseBegin"/>
/// + <see cref="AccumulatePhaseEnd"/> ADD to the per-phase scratch
/// duration rather than overwriting it. Used for phases split into
/// multiple non-contiguous segments (e.g. WorldDraw which wraps
/// around the optional pheromone OverlayDraw segment).
///
/// fase-4.12-fixup carry-overs:
///   * EMA frame/render/overlay averages (alpha 0.05) for the HUD
///     status panel (AvgFrameMs/AvgRenderMs/AvgOverlayMs).
///   * <see cref="ReportFrameTicks(long)"/> — called from Engine.Tick
///     once per frame with the wall-clock frame duration.
///   * <see cref="Series"/> — unbounded in-memory series for graph
///     rendering. fix3-v2: grows without bound; user opted in.
///   * <see cref="WriterCurrentFileName"/>/<see cref="WriterRotationIndex"/>
///     — proxy accessors to the ProfileWriter's current output file
///     metadata (for the HUD status panel).
/// </summary>
public sealed class FrameProfiler : IDisposable
{
    /// <summary>Number of phase slots (matches <see cref="ProfilePhase"/> enum values).</summary>
    public const int PhaseCount = 19;

    /// <summary>Ring buffer capacity (power of two, ~4.5 minutes at 60 Hz).</summary>
    public const int RingCapacity = 16384;

    /// <summary>EMA smoothing factor for the HUD-facing averages.</summary>
    private const double EmaAlpha = 0.05;

    // Per-phase scratch for the currently-in-flight render frame.
    // Start ticks are captured on BeginPhase, duration ticks on
    // EndPhase. Indexed by (int)ProfilePhase.
    private readonly long[] _scratchStart = new long[PhaseCount];
    private readonly long[] _scratchDuration = new long[PhaseCount];

    // Per-phase MAX aggregation across all render frames merged into
    // the current 60 Hz bucket. Reset after FlushBucket.
    private readonly long[] _maxTicks = new long[PhaseCount];
    private int _renderFramesInBucket;
    private long _bucketFirstFrameTimestamp;

    // Logical 60 Hz profiler frame counter — incremented on every
    // FlushBucket. Used as FrameNumber on emitted samples.
    private long _logicalProfilerFrame;

    private readonly RingBuffer<ProfileSample> _ring;
    private readonly ProfilerSeries _series;
    private readonly ProfilerSampleClock _sampleClock = new ProfilerSampleClock();

    // Lazy writer: not created until the first Enable() call, so a
    // never-enabled profiler has zero threads and zero file handles.
    private ProfileWriter? _writer;

    private long _currentFrame;
    private long _currentFrameTimestamp;

    private bool _isEnabled;
    private bool _disposed;
    private string? _lastError;

    // EMAs surfaced to the HUD status panel.
    private double _avgFrameMs;
    private double _avgRenderMs;
    private double _avgOverlayMs;

#if DEBUG
    private int _uiThreadId;
#endif

    /// <summary>
    /// Creates a profiler with the default ring capacity
    /// (<see cref="RingCapacity"/> = 16384).
    /// </summary>
    public FrameProfiler()
    {
        _ring = new RingBuffer<ProfileSample>(RingCapacity);
        _series = new ProfilerSeries();
    }

    /// <summary>True while the profiler is actively collecting samples.</summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>Exposes the ring buffer for in-process readers (e.g. a future HUD mini-chart).</summary>
    public RingBuffer<ProfileSample> Ring => _ring;

    /// <summary>Exposes the in-memory series ring (for <see cref="ProfilerGraphWindow"/>).</summary>
    public ProfilerSeries Series => _series;

    /// <summary>
    /// Last error message captured during <see cref="Enable"/> (or null
    /// if the most recent Enable succeeded / Enable was never called).
    /// Cleared on successful Enable.
    /// </summary>
    public string? LastError => _lastError;

    /// <summary>EMA-smoothed per-frame wall-clock duration (ms) for the HUD panel.</summary>
    public double AvgFrameMs => _avgFrameMs;

    /// <summary>EMA-smoothed per-frame render duration (ms) = PaintTotal, for the HUD panel.</summary>
    public double AvgRenderMs => _avgRenderMs;

    /// <summary>EMA-smoothed per-frame pheromone overlay duration (ms) for the HUD panel.</summary>
    public double AvgOverlayMs => _avgOverlayMs;

    /// <summary>Current ProfileWriter output file basename (with rotation suffix). Empty string when no writer is active.</summary>
    public string WriterCurrentFileName => _writer?.CurrentFileName ?? string.Empty;

    /// <summary>Current ProfileWriter rotation index (1-based). 0 when no writer is active.</summary>
    public int WriterRotationIndex => _writer?.RotationIndex ?? 0;

    /// <summary>
    /// Starts collection. Lazily constructs the <see cref="ProfileWriter"/>
    /// (which opens its output file and spawns the drain thread) on
    /// the first call. Subsequent calls are no-ops. Exceptions from
    /// writer construction/start are caught and surfaced via
    /// <see cref="LastError"/>; the profiler stays disabled.
    /// </summary>
    public void Enable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isEnabled) return;

#if DEBUG
        // Pin the "owning" thread on the first Enable so subsequent
        // Begin/End* calls can be asserted against it.
        _uiThreadId = Environment.CurrentManagedThreadId;
#endif

        try
        {
            if (_writer == null)
            {
                _writer = new ProfileWriter(_ring);
            }
            _writer.Start();
            _sampleClock.Start();
            ResetBucket();
            _isEnabled = true;
            _lastError = null;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _isEnabled = false;
        }
    }

    /// <summary>
    /// Stops collection, flushes any pending bucket, drains remaining
    /// samples, flushes the CSV file, and joins the writer thread
    /// (bounded). Safe to call when already disabled.
    /// </summary>
    public void Disable()
    {
        if (!_isEnabled) return;
        // Flush pending bucket so the last partial interval doesn't
        // vanish when the user toggles the profiler off.
        if (_renderFramesInBucket > 0)
        {
            FlushBucket();
        }
        _sampleClock.Stop();
        _isEnabled = false;
        _writer?.Stop();
    }

    /// <summary>
    /// Marks the start of a new render frame. Resets per-phase scratch
    /// and stamps the frame timestamp.
    /// </summary>
    public void BeginFrame(long frameNumber)
    {
        if (!_isEnabled) return;
        AssertUiThread();

        _currentFrame = frameNumber;
        _currentFrameTimestamp = Stopwatch.GetTimestamp();

        // Zero the per-phase scratch so a phase that never runs in
        // this frame (e.g. OverlayDraw when _showPheromones is false)
        // ends up as 0 ticks in the emitted sample rather than
        // carrying over from the previous frame.
        for (int i = 0; i < PhaseCount; i++)
        {
            _scratchDuration[i] = 0;
        }
    }

    /// <summary>
    /// Stamps the start of a phase. Paired with a subsequent
    /// <see cref="EndPhase"/> call for the same phase.
    /// </summary>
    public void BeginPhase(ProfilePhase phase)
    {
        if (!_isEnabled) return;
        AssertUiThread();
        _scratchStart[(int)phase] = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Stamps the end of a phase and stores the elapsed ticks.
    /// </summary>
    public void EndPhase(ProfilePhase phase)
    {
        if (!_isEnabled) return;
        AssertUiThread();
        _scratchDuration[(int)phase] = Stopwatch.GetTimestamp() - _scratchStart[(int)phase];
    }

    /// <summary>
    /// Stamps the start of a phase segment whose duration will be
    /// ADDED to the per-phase total in <see cref="AccumulatePhaseEnd"/>.
    /// Use for phases split into multiple non-contiguous segments
    /// (e.g. WorldDraw wrapping around the optional OverlayDraw).
    /// </summary>
    public void AccumulatePhaseBegin(ProfilePhase phase)
    {
        if (!_isEnabled) return;
        AssertUiThread();
        _scratchStart[(int)phase] = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Stamps the end of an accumulated phase segment and ADDS the
    /// elapsed ticks to the per-phase total (rather than overwriting
    /// as <see cref="EndPhase"/> does).
    /// </summary>
    public void AccumulatePhaseEnd(ProfilePhase phase)
    {
        if (!_isEnabled) return;
        AssertUiThread();
        _scratchDuration[(int)phase] += Stopwatch.GetTimestamp() - _scratchStart[(int)phase];
    }

    /// <summary>
    /// Reports the wall-clock render-frame duration. Used to drive
    /// the HUD-facing <see cref="AvgFrameMs"/> EMA. Called from
    /// Engine.Tick once per render frame regardless of phase
    /// instrumentation and regardless of 60 Hz bucket boundaries.
    /// </summary>
    public void ReportFrameTicks(long deltaTicks)
    {
        if (!_isEnabled) return;
        AssertUiThread();
        double ms = deltaTicks * 1000.0 / Stopwatch.Frequency;
        _avgFrameMs = _avgFrameMs * (1.0 - EmaAlpha) + ms * EmaAlpha;
    }

    /// <summary>
    /// Called once per render frame. Aggregates the current scratch
    /// durations into the pending 60 Hz bucket via per-phase MAX
    /// and, when the sample clock says it's time, flushes the bucket
    /// as a single <see cref="ProfileSample"/>.
    /// </summary>
    public void EndFrame()
    {
        if (!_isEnabled) return;
        AssertUiThread();

        // MAX aggregation: merge this render frame's phase durations
        // into the pending bucket. Spike-preserving — one outlier
        // render frame within a 60 Hz interval is retained.
        if (_renderFramesInBucket == 0)
        {
            _bucketFirstFrameTimestamp = _currentFrameTimestamp;
        }
        for (int i = 0; i < PhaseCount; i++)
        {
            long d = _scratchDuration[i];
            if (d > _maxTicks[i]) _maxTicks[i] = d;
        }
        _renderFramesInBucket++;

        // Update render/overlay EMAs off the raw render-frame ticks
        // (not bucket MAX) so HUD averages reflect true render cost.
        double freq = Stopwatch.Frequency;
        double renderMs = _scratchDuration[(int)ProfilePhase.PaintTotal] * 1000.0 / freq;
        double overlayMs = _scratchDuration[(int)ProfilePhase.OverlayDraw] * 1000.0 / freq;
        _avgRenderMs = _avgRenderMs * (1.0 - EmaAlpha) + renderMs * EmaAlpha;
        _avgOverlayMs = _avgOverlayMs * (1.0 - EmaAlpha) + overlayMs * EmaAlpha;

        if (_sampleClock.ShouldTick())
        {
            FlushBucket();
        }
    }

    /// <summary>
    /// Emits a single <see cref="ProfileSample"/> from the current
    /// MAX-aggregated bucket and appends a corresponding row to the
    /// in-memory <see cref="ProfilerSeries"/>. Resets the bucket.
    /// </summary>
    private void FlushBucket()
    {
        ProfileSample s;
        s.FrameNumber = _logicalProfilerFrame;
        s.TimestampTicks = _bucketFirstFrameTimestamp;
        s.RenderFramesAggregated = _renderFramesInBucket;
        s.SimTicks = _maxTicks[(int)ProfilePhase.Sim];
        s.WorldDrawTicks = _maxTicks[(int)ProfilePhase.WorldDraw];
        s.OverlayDrawTicks = _maxTicks[(int)ProfilePhase.OverlayDraw];
        s.AntsDrawTicks = _maxTicks[(int)ProfilePhase.AntsDraw];
        s.StatsDrawTicks = _maxTicks[(int)ProfilePhase.StatsDraw];
        s.HudDrawTicks = _maxTicks[(int)ProfilePhase.HudDraw];
        s.PaintTotalTicks = _maxTicks[(int)ProfilePhase.PaintTotal];
        s.GridDrawTicks = _maxTicks[(int)ProfilePhase.GridDraw];
        s.FoodDrawTicks = _maxTicks[(int)ProfilePhase.FoodDraw];
        s.NestsDrawTicks = _maxTicks[(int)ProfilePhase.NestsDraw];
        s.ArrayClearTicks = _maxTicks[(int)ProfilePhase.ArrayClear];
        s.InnerLoopTicks = _maxTicks[(int)ProfilePhase.InnerLoop];
        s.MarshalCopyTicks = _maxTicks[(int)ProfilePhase.MarshalCopy];
        s.DrawBitmapTicks = _maxTicks[(int)ProfilePhase.DrawBitmap];
        s.CanvasSetupTicks = _maxTicks[(int)ProfilePhase.CanvasSetup];
        s.PlacementTicks = _maxTicks[(int)ProfilePhase.Placement];
        s.SelectionTicks = _maxTicks[(int)ProfilePhase.Selection];
        s.ButtonsTicks = _maxTicks[(int)ProfilePhase.Buttons];
        s.ProfilerWindowTicks = _maxTicks[(int)ProfilePhase.ProfilerWindow];

        _ring.Write(in s);

        // Feed the unbounded series used by the graph window.
        double freq = Stopwatch.Frequency;
        double tickToUs = 1_000_000.0 / freq;
        _series.AddFrame(
            frameMs: (float)(s.PaintTotalTicks * 1000.0 / freq + (s.SimTicks * 1000.0 / freq)),
            simMs: (float)(s.SimTicks * 1000.0 / freq),
            gridMs: (float)(s.GridDrawTicks * 1000.0 / freq),
            overlayMs: (float)(s.OverlayDrawTicks * 1000.0 / freq),
            foodMs: (float)(s.FoodDrawTicks * 1000.0 / freq),
            nestsMs: (float)(s.NestsDrawTicks * 1000.0 / freq),
            antsMs: (float)(s.AntsDrawTicks * 1000.0 / freq),
            statsMs: (float)(s.StatsDrawTicks * 1000.0 / freq),
            hudMs: (float)(s.HudDrawTicks * 1000.0 / freq),
            arrayClearUs: (float)(s.ArrayClearTicks * tickToUs),
            innerLoopUs: (float)(s.InnerLoopTicks * tickToUs),
            marshalCopyUs: (float)(s.MarshalCopyTicks * tickToUs),
            drawBitmapUs: (float)(s.DrawBitmapTicks * tickToUs));

        _logicalProfilerFrame++;
        ResetBucket();
    }

    private void ResetBucket()
    {
        for (int i = 0; i < PhaseCount; i++)
        {
            _maxTicks[i] = 0;
        }
        _renderFramesInBucket = 0;
        _bucketFirstFrameTimestamp = 0;
    }

    /// <summary>Disposes the writer (flushes + joins + closes files).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Disable();
        _writer?.Dispose();
        _writer = null;
        _disposed = true;
    }

    [Conditional("DEBUG")]
    private void AssertUiThread()
    {
#if DEBUG
        Debug.Assert(
            _uiThreadId == 0 || Environment.CurrentManagedThreadId == _uiThreadId,
            "FrameProfiler Begin/End* must be called from the UI thread (the one that first called Enable).");
#endif
    }
}
