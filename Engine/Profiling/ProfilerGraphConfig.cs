namespace ANTS;

/// <summary>
/// Zoom levels for the profiler graph x-axis (time range shown).
/// The sample count maps to the last N frames out of
/// <see cref="ProfilerSeries.SampleCapacity"/>.
/// </summary>
public enum ProfilerZoomLevel
{
    /// <summary>Last 600 frames (~10 s at 60 FPS).</summary>
    X1 = 0,

    /// <summary>Last 3000 frames (~50 s at 60 FPS).</summary>
    X5 = 1,

    /// <summary>Last 9000 frames (~2.5 min at 60 FPS).</summary>
    X15 = 2,

    /// <summary>Last 18000 frames (~5 min at 60 FPS).</summary>
    X30 = 3,
}

/// <summary>
/// Mutable graph-window configuration: which metrics to draw in each
/// sub-graph and the x-axis zoom level. Lives on
/// <see cref="ProfilerGraphWindow"/> and mutated via its UI buttons.
/// </summary>
public sealed class ProfilerGraphConfig
{
    public ProfilerZoomLevel Zoom = ProfilerZoomLevel.X1;

    // Sub-graph 1: frame timing (ms scale).
    public bool ShowFrameMs = true;
    public bool ShowSimMs = true;
    public bool ShowGridMs = true;
    public bool ShowOverlayMs = true;
    public bool ShowAntsMs = true;
    public bool ShowStatsMs = true;
    public bool ShowHudMs = true;

    // Sub-graph 2: world-render split (ms scale).
    public bool ShowFoodMs = true;
    public bool ShowNestsMs = true;

    // Sub-graph 3: overlay internals (µs scale).
    public bool ShowArrayClearUs = true;
    public bool ShowInnerLoopUs = true;
    public bool ShowMarshalCopyUs = true;
    public bool ShowDrawBitmapUs = true;

    /// <summary>
    /// Returns the number of most-recent samples to display for the
    /// current <see cref="Zoom"/> level.
    /// </summary>
    public int GetSampleCount()
    {
        return Zoom switch
        {
            ProfilerZoomLevel.X1 => 600,
            ProfilerZoomLevel.X5 => 3000,
            ProfilerZoomLevel.X15 => 9000,
            ProfilerZoomLevel.X30 => ProfilerSeries.SampleCapacity,
            _ => 600,
        };
    }

    /// <summary>Cycles to the next zoom level (X1 → X5 → X15 → X30 → X1).</summary>
    public void ZoomIn()
    {
        Zoom = Zoom switch
        {
            ProfilerZoomLevel.X1 => ProfilerZoomLevel.X5,
            ProfilerZoomLevel.X5 => ProfilerZoomLevel.X15,
            ProfilerZoomLevel.X15 => ProfilerZoomLevel.X30,
            _ => ProfilerZoomLevel.X1,
        };
    }

    /// <summary>Cycles to the previous zoom level (X30 → X15 → X5 → X1 → X30).</summary>
    public void ZoomOut()
    {
        Zoom = Zoom switch
        {
            ProfilerZoomLevel.X30 => ProfilerZoomLevel.X15,
            ProfilerZoomLevel.X15 => ProfilerZoomLevel.X5,
            ProfilerZoomLevel.X5 => ProfilerZoomLevel.X1,
            _ => ProfilerZoomLevel.X30,
        };
    }

    public string ZoomLabel => Zoom switch
    {
        ProfilerZoomLevel.X1 => "1x",
        ProfilerZoomLevel.X5 => "5x",
        ProfilerZoomLevel.X15 => "15x",
        ProfilerZoomLevel.X30 => "30x",
        _ => "?",
    };
}
