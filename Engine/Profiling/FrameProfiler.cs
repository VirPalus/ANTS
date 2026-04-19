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

    /// <summary>World base + food/nests/grid-lines draw (inside OnPaint).</summary>
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
}

/// <summary>
/// Per-frame timing collector.
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
///     EndFrame commits a single <see cref="ProfileSample"/> to the
///     ring buffer, which the background <see cref="ProfileWriter"/>
///     thread drains every 500 ms.
///
/// fase-4.11 note: this type is constructed and exercised by
/// fase-4.12 only. fase-4.11 lands the infrastructure without
/// touching Engine.cs or any renderer.
/// </summary>
public sealed class FrameProfiler : IDisposable
{
    /// <summary>Number of phase slots (matches <see cref="ProfilePhase"/> enum values).</summary>
    public const int PhaseCount = 7;

    /// <summary>Ring buffer capacity (power of two, ~68 seconds at 240 FPS).</summary>
    public const int RingCapacity = 16384;

    // Per-phase scratch for the currently-in-flight frame.
    // Start ticks are captured on BeginPhase, duration ticks on
    // EndPhase. Indexed by (int)ProfilePhase.
    private readonly long[] _scratchStart = new long[PhaseCount];
    private readonly long[] _scratchDuration = new long[PhaseCount];

    private readonly RingBuffer<ProfileSample> _ring;

    // Lazy writer: not created until the first Enable() call, so a
    // never-enabled profiler has zero threads and zero file handles.
    private ProfileWriter? _writer;

    private long _currentFrame;
    private long _currentFrameTimestamp;

    private bool _isEnabled;
    private bool _disposed;

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
    }

    /// <summary>True while the profiler is actively collecting samples.</summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>Exposes the ring buffer for in-process readers (e.g. a future HUD mini-chart).</summary>
    public RingBuffer<ProfileSample> Ring => _ring;

    /// <summary>
    /// Starts collection. Lazily constructs the <see cref="ProfileWriter"/>
    /// (which opens its output file and spawns the drain thread) on
    /// the first call. Subsequent calls are no-ops.
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

        if (_writer == null)
        {
            _writer = new ProfileWriter(_ring);
        }
        _writer.Start();
        _isEnabled = true;
    }

    /// <summary>
    /// Stops collection, drains remaining samples, flushes the CSV
    /// file, and joins the writer thread (bounded). Safe to call
    /// when already disabled.
    /// </summary>
    public void Disable()
    {
        if (!_isEnabled) return;
        _isEnabled = false;
        _writer?.Stop();
    }

    /// <summary>
    /// Marks the start of a new frame. Resets per-phase scratch
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
    /// Commits the assembled frame sample to the ring buffer.
    /// This is the only hot-path call that touches shared state
    /// (the ring-buffer Volatile write cursor).
    /// </summary>
    public void EndFrame()
    {
        if (!_isEnabled) return;
        AssertUiThread();

        ProfileSample s;
        s.FrameNumber = _currentFrame;
        s.TimestampTicks = _currentFrameTimestamp;
        s.SimTicks = _scratchDuration[(int)ProfilePhase.Sim];
        s.WorldDrawTicks = _scratchDuration[(int)ProfilePhase.WorldDraw];
        s.OverlayDrawTicks = _scratchDuration[(int)ProfilePhase.OverlayDraw];
        s.AntsDrawTicks = _scratchDuration[(int)ProfilePhase.AntsDraw];
        s.StatsDrawTicks = _scratchDuration[(int)ProfilePhase.StatsDraw];
        s.HudDrawTicks = _scratchDuration[(int)ProfilePhase.HudDraw];
        s.PaintTotalTicks = _scratchDuration[(int)ProfilePhase.PaintTotal];

        _ring.Write(in s);
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
