namespace ANTS;

/// <summary>
/// Zoom levels for the profiler graph x-axis (time range shown).
/// The sample count maps to the last N profiler samples
/// (60 Hz / ProfilerSampleClock) to display.
/// fase-4.12-fix3-v2 expanded the set from 4 to 9 levels.
/// </summary>
public enum ProfilerZoomLevel
{
    /// <summary>Last 60 samples (~1 s at 60 Hz).</summary>
    Sec1 = 0,

    /// <summary>Last 300 samples (~5 s at 60 Hz).</summary>
    Sec5 = 1,

    /// <summary>Last 900 samples (~15 s at 60 Hz).</summary>
    Sec15 = 2,

    /// <summary>Last 1800 samples (~30 s at 60 Hz).</summary>
    Sec30 = 3,

    /// <summary>Last 3600 samples (~1 min at 60 Hz).</summary>
    Min1 = 4,

    /// <summary>Last 18000 samples (~5 min at 60 Hz).</summary>
    Min5 = 5,

    /// <summary>Last 54000 samples (~15 min at 60 Hz).</summary>
    Min15 = 6,

    /// <summary>Last 216000 samples (~1 h at 60 Hz).</summary>
    Hour1 = 7,

    /// <summary>Entire recorded history (oldest to newest).</summary>
    All = 8,
}

/// <summary>
/// Mutable graph-window configuration: which metrics to draw in each
/// sub-graph and the x-axis zoom level. Lives on
/// <see cref="ProfilerGraphWindow"/> and mutated via its UI buttons.
/// </summary>
public sealed class ProfilerGraphConfig
{
    /// <summary>Sentinel returned by <see cref="GetSampleCount"/> for <see cref="ProfilerZoomLevel.All"/>.</summary>
    public const int AllSentinel = int.MaxValue;

    public ProfilerZoomLevel Zoom = ProfilerZoomLevel.Sec30;

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
    /// current <see cref="Zoom"/> level. <see cref="ProfilerZoomLevel.All"/>
    /// returns <see cref="AllSentinel"/> — callers should clamp to the
    /// actual series size.
    /// </summary>
    public int GetSampleCount()
    {
        return Zoom switch
        {
            ProfilerZoomLevel.Sec1   => 60,
            ProfilerZoomLevel.Sec5   => 300,
            ProfilerZoomLevel.Sec15  => 900,
            ProfilerZoomLevel.Sec30  => 1800,
            ProfilerZoomLevel.Min1   => 3600,
            ProfilerZoomLevel.Min5   => 18000,
            ProfilerZoomLevel.Min15  => 54000,
            ProfilerZoomLevel.Hour1  => 216000,
            ProfilerZoomLevel.All    => AllSentinel,
            _ => 1800,
        };
    }

    /// <summary>Cycles to the next zoom level (Sec1 -&gt; Sec5 -&gt; ... -&gt; All -&gt; Sec1).</summary>
    public void ZoomIn()
    {
        Zoom = Zoom switch
        {
            ProfilerZoomLevel.Sec1   => ProfilerZoomLevel.Sec5,
            ProfilerZoomLevel.Sec5   => ProfilerZoomLevel.Sec15,
            ProfilerZoomLevel.Sec15  => ProfilerZoomLevel.Sec30,
            ProfilerZoomLevel.Sec30  => ProfilerZoomLevel.Min1,
            ProfilerZoomLevel.Min1   => ProfilerZoomLevel.Min5,
            ProfilerZoomLevel.Min5   => ProfilerZoomLevel.Min15,
            ProfilerZoomLevel.Min15  => ProfilerZoomLevel.Hour1,
            ProfilerZoomLevel.Hour1  => ProfilerZoomLevel.All,
            _                         => ProfilerZoomLevel.Sec1,
        };
    }

    /// <summary>Cycles to the previous zoom level (All -&gt; Hour1 -&gt; ... -&gt; Sec1 -&gt; All).</summary>
    public void ZoomOut()
    {
        Zoom = Zoom switch
        {
            ProfilerZoomLevel.All    => ProfilerZoomLevel.Hour1,
            ProfilerZoomLevel.Hour1  => ProfilerZoomLevel.Min15,
            ProfilerZoomLevel.Min15  => ProfilerZoomLevel.Min5,
            ProfilerZoomLevel.Min5   => ProfilerZoomLevel.Min1,
            ProfilerZoomLevel.Min1   => ProfilerZoomLevel.Sec30,
            ProfilerZoomLevel.Sec30  => ProfilerZoomLevel.Sec15,
            ProfilerZoomLevel.Sec15  => ProfilerZoomLevel.Sec5,
            ProfilerZoomLevel.Sec5   => ProfilerZoomLevel.Sec1,
            _                         => ProfilerZoomLevel.All,
        };
    }

    public string ZoomLabel => Zoom switch
    {
        ProfilerZoomLevel.Sec1   => "1s",
        ProfilerZoomLevel.Sec5   => "5s",
        ProfilerZoomLevel.Sec15  => "15s",
        ProfilerZoomLevel.Sec30  => "30s",
        ProfilerZoomLevel.Min1   => "1m",
        ProfilerZoomLevel.Min5   => "5m",
        ProfilerZoomLevel.Min15  => "15m",
        ProfilerZoomLevel.Hour1  => "1h",
        ProfilerZoomLevel.All    => "all",
        _                         => "?",
    };
}
