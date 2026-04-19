namespace ANTS;

using System.Diagnostics;

/// <summary>
/// Decouples profiler sampling cadence from render FPS.
///
/// The clock fires at a fixed 60 Hz (16.67 ms interval) regardless
/// of how fast the game is rendering. <see cref="FrameProfiler.EndFrame"/>
/// calls <see cref="ShouldTick"/> once per render frame:
///   * returns <c>true</c> when at least one 60 Hz interval has
///     elapsed since the previous successful tick, signalling the
///     profiler to flush the aggregated MAX bucket into the series /
///     CSV writer;
///   * otherwise returns <c>false</c> and the render frame's data
///     is merged into the current bucket via MAX aggregation.
///
/// Running at render FPS &gt; 60: multiple render frames are merged
/// into a single 60 Hz sample (worst-case spike via MAX).
/// Running at render FPS &lt; 60: each render frame triggers a flush
/// (sample rate is capped by render rate in this case — expected).
///
/// All operations are on the UI thread, no locks, no allocations.
/// </summary>
public sealed class ProfilerSampleClock
{
    /// <summary>Target sampling rate (Hz).</summary>
    public const int TargetHz = 60;

    /// <summary>
    /// Minimum spacing between successful ticks, expressed in
    /// <see cref="Stopwatch"/> ticks. Computed once at type init.
    /// </summary>
    private static readonly long TickIntervalStopwatchTicks =
        Stopwatch.Frequency / TargetHz;

    private long _lastTickTimestamp;
    private bool _running;

    /// <summary>True while <see cref="Start"/> has been called more recently than <see cref="Stop"/>.</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Starts the clock. The next <see cref="ShouldTick"/> call will
    /// only return <c>true</c> after one full 60 Hz interval has
    /// elapsed from this moment.
    /// </summary>
    public void Start()
    {
        _running = true;
        _lastTickTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Stops the clock. <see cref="ShouldTick"/> returns <c>false</c> while stopped.</summary>
    public void Stop()
    {
        _running = false;
    }

    /// <summary>
    /// Returns <c>true</c> once per 60 Hz interval. When it returns
    /// <c>true</c>, advances the last-tick timestamp so the next
    /// interval starts counting from now.
    /// </summary>
    public bool ShouldTick()
    {
        if (!_running) return false;
        long now = Stopwatch.GetTimestamp();
        if (now - _lastTickTimestamp >= TickIntervalStopwatchTicks)
        {
            _lastTickTimestamp = now;
            return true;
        }
        return false;
    }
}
