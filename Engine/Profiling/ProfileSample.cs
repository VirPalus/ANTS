namespace ANTS;

/// <summary>
/// Per-frame timing data captured by <see cref="FrameProfiler"/>.
///
/// Fixed 128-byte value type (16 x long). Stored inline in a
/// <see cref="RingBuffer{T}"/> on the hot path so that BeginFrame/
/// EndFrame produce zero heap allocations.
///
/// All tick fields are raw <see cref="System.Diagnostics.Stopwatch"/>
/// tick deltas. Conversion to microseconds happens off the hot path
/// (in <see cref="ProfileWriter"/> when formatting CSV rows).
///
/// fase-4.12-fixup (variant A) extends this with 7 extra fields:
///   * GridDrawTicks/FoodDrawTicks/NestsDrawTicks split the legacy
///     WorldDraw phase inside <see cref="WorldRenderer"/> into 3
///     independent measurements.
///   * ArrayClearTicks/InnerLoopTicks/MarshalCopyTicks/DrawBitmapTicks
///     are 4 inner-phase timings measured inside
///     <see cref="OverlayRenderer.Draw"/> so the graph window can
///     visualize pheromone-overlay internal bottlenecks.
/// WorldDrawTicks remains in the struct layout for CSV-column
/// stability but is not populated by the variant-A pipeline (set to
/// 0). Consumers reading the ring buffer should prefer the
/// Grid/Food/Nests triple instead.
/// </summary>
public struct ProfileSample
{
    /// <summary>Monotonically increasing frame counter assigned by BeginFrame.</summary>
    public long FrameNumber;

    /// <summary>Stopwatch.GetTimestamp() captured at BeginFrame. Used to compute wall-clock offset from session start.</summary>
    public long TimestampTicks;

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
