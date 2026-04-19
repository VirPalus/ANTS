namespace ANTS;

/// <summary>
/// Per-frame timing data captured by <see cref="FrameProfiler"/>.
///
/// Fixed 72-byte value type (9 x long). Stored inline in a
/// <see cref="RingBuffer{T}"/> on the hot path so that BeginFrame/
/// EndFrame produce zero heap allocations.
///
/// All tick fields are raw <see cref="System.Diagnostics.Stopwatch"/>
/// tick deltas. Conversion to microseconds happens off the hot path
/// (in <see cref="ProfileWriter"/> when formatting CSV rows).
///
/// Field order is deliberate: FrameNumber and TimestampTicks first
/// so the writer can stamp "when" before the "how-long" columns.
/// </summary>
public struct ProfileSample
{
    /// <summary>Monotonically increasing frame counter assigned by BeginFrame.</summary>
    public long FrameNumber;

    /// <summary>Stopwatch.GetTimestamp() captured at BeginFrame. Used to compute wall-clock offset from session start.</summary>
    public long TimestampTicks;

    /// <summary>Simulation advance duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.Sim"/>.</summary>
    public long SimTicks;

    /// <summary>World-layer draw duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.WorldDraw"/>.</summary>
    public long WorldDrawTicks;

    /// <summary>Pheromone overlay draw duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.OverlayDraw"/>.</summary>
    public long OverlayDrawTicks;

    /// <summary>Ants draw duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.AntsDraw"/>.</summary>
    public long AntsDrawTicks;

    /// <summary>Stats panel draw duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.StatsDraw"/>.</summary>
    public long StatsDrawTicks;

    /// <summary>HUD draw duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.HudDraw"/>.</summary>
    public long HudDrawTicks;

    /// <summary>OnPaint wall-clock duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.PaintTotal"/>.</summary>
    public long PaintTotalTicks;
}
