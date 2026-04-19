namespace ANTS;

/// <summary>
/// Per-flush profiler sample captured by <see cref="FrameProfiler"/>.
///
/// fase-4.12-fix3-v2: a sample now represents one 60 Hz bucket
/// (~16.67 ms of wall-clock time). Each tick field stores the
/// <b>MAX</b> value observed across all render frames that fell
/// inside the bucket — spike-preserving aggregation. The new
/// <see cref="RenderFramesAggregated"/> field records how many
/// render frames contributed to this bucket.
///
/// All tick fields are raw <see cref="System.Diagnostics.Stopwatch"/>
/// tick deltas. Conversion to microseconds happens off the hot path
/// (in <see cref="ProfileWriter"/> when formatting CSV rows).
///
/// Pre-v2 this struct was captured once per render frame.
/// </summary>
public struct ProfileSample
{
    /// <summary>Logical profiler frame counter (60 Hz), assigned on flush.</summary>
    public long FrameNumber;

    /// <summary>Stopwatch.GetTimestamp() captured at flush. Used to compute wall-clock offset from session start.</summary>
    public long TimestampTicks;

    /// <summary>Number of render frames aggregated into this bucket (MAX over them).</summary>
    public int RenderFramesAggregated;

    /// <summary>Simulation advance duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.Sim"/>.</summary>
    public long SimTicks;

    /// <summary>Legacy aggregate world-layer draw duration. Not populated in variant-A; kept for CSV-column stability.</summary>
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

    /// <summary>Grid base+walls+lines draw (Stopwatch ticks). Phase index <see cref="ProfilePhase.GridDraw"/>.</summary>
    public long GridDrawTicks;

    /// <summary>Food cells draw (Stopwatch ticks). Phase index <see cref="ProfilePhase.FoodDraw"/>.</summary>
    public long FoodDrawTicks;

    /// <summary>Colony nests draw (Stopwatch ticks). Phase index <see cref="ProfilePhase.NestsDraw"/>.</summary>
    public long NestsDrawTicks;

    /// <summary>Overlay buffer Array.Clear duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.ArrayClear"/>.</summary>
    public long ArrayClearTicks;

    /// <summary>Overlay per-cell inner loop duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.InnerLoop"/>.</summary>
    public long InnerLoopTicks;

    /// <summary>Overlay Marshal.Copy duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.MarshalCopy"/>.</summary>
    public long MarshalCopyTicks;

    /// <summary>Overlay canvas.DrawBitmap duration (Stopwatch ticks). Phase index <see cref="ProfilePhase.DrawBitmap"/>.</summary>
    public long DrawBitmapTicks;

    /// <summary>Canvas setup (Save + Camera.Apply) duration. Phase index <see cref="ProfilePhase.CanvasSetup"/>. fase-4.12-diag.</summary>
    public long CanvasSetupTicks;

    /// <summary>Placement ghost draw duration. Phase index <see cref="ProfilePhase.Placement"/>. fase-4.12-diag.</summary>
    public long PlacementTicks;

    /// <summary>Selection overlay + canvas.Restore + info-panel duration. Phase index <see cref="ProfilePhase.Selection"/>. fase-4.12-diag.</summary>
    public long SelectionTicks;

    /// <summary>Top-bar + buttons picture record/draw duration (Accumulate). Phase index <see cref="ProfilePhase.Buttons"/>. fase-4.12-diag.</summary>
    public long ButtonsTicks;

    /// <summary>ProfilerUI + ProfilerGraphWindow draw duration. Phase index <see cref="ProfilePhase.ProfilerWindow"/>. fase-4.12-diag.</summary>
    public long ProfilerWindowTicks;
}
