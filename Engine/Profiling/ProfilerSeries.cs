namespace ANTS;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// Unbounded in-memory time series for profiler graph rendering.
///
/// fase-4.12-fix3-v2 rewrite: the previous 18000-slot ring buffer has
/// been replaced with a per-metric <see cref="List{T}"/> that grows
/// without bound. The user explicitly opted in to this trade-off
/// (RAM vs history depth) — the profiler is never auto-stopped and
/// must retain a lossless view of every sample for scrollback.
///
/// Owns 13 parallel <see cref="float"/> lists (one per visualizable
/// metric). A single monotonically-increasing <see cref="Revision"/>
/// counter is bumped on every <see cref="AddFrame"/> so consumers
/// (e.g. <see cref="ProfilerGraphWindow"/>'s SKPicture cache) can
/// detect "new data available" without a polling compare of
/// <see cref="Count"/> — which was broken in fix3-v1 because the old
/// ring saturated <c>Count</c> at <c>SampleCapacity</c>.
///
/// Hot-path contract: <see cref="AddFrame"/> is called from the UI
/// thread at a fixed 60 Hz (driven by
/// <see cref="ProfilerSampleClock"/>). Each call appends 13 floats
/// and increments <see cref="Revision"/>. Amortised zero allocations
/// once each inner list's capacity has grown to match steady-state
/// demand.
///
/// Readers prefer <see cref="CopyRange"/> for windowed rendering
/// (scrollbar) and <see cref="GetSpan"/> for read-only iteration
/// over the full metric series (e.g. decimation min/max pass).
/// <see cref="CopyLast"/> is retained as a thin wrapper over
/// <see cref="CopyRange"/> for back-compat with existing call sites.
///
/// Metric indices are fixed (see <see cref="MetricCount"/>):
///   0 = FrameMs, 1 = SimMs, 2 = GridMs, 3 = OverlayMs, 4 = FoodMs,
///   5 = NestsMs, 6 = AntsMs, 7 = StatsMs, 8 = HudMs,
///   9 = ArrayClearUs, 10 = InnerLoopUs, 11 = MarshalCopyUs,
///   12 = DrawBitmapUs.
/// </summary>
public sealed class ProfilerSeries
{
    /// <summary>Number of metrics tracked (must match the index table in the class docs).</summary>
    public const int MetricCount = 13;

    /// <summary>
    /// Initial per-metric list capacity. Sized to cover ~5 minutes at
    /// 60 Hz without a single reallocation, matching the steady-state
    /// history that fix3-v1 used to cap at. Growth beyond this is
    /// handled by the standard <see cref="List{T}"/> doubling strategy.
    /// </summary>
    private const int InitialCapacity = 18000;

    private readonly List<float>[] _series;
    private long _revision;

    public ProfilerSeries()
    {
        _series = new List<float>[MetricCount];
        for (int i = 0; i < MetricCount; i++)
        {
            _series[i] = new List<float>(InitialCapacity);
        }
    }

    /// <summary>Number of valid samples available (unbounded; grows with each <see cref="AddFrame"/>).</summary>
    public int Count => _series[0].Count;

    /// <summary>
    /// Monotonically-increasing counter bumped once per
    /// <see cref="AddFrame"/> call. Consumers use this to detect
    /// "new data arrived" in a way that keeps working after the
    /// sample count would have saturated in the old ring design.
    /// </summary>
    public long Revision => _revision;

    /// <summary>
    /// Appends a single 13-metric sample across all per-metric
    /// lists. All 13 arguments must be in the correct unit
    /// (ms for 0..8, microseconds for 9..12) — conversion is done
    /// by the caller.
    /// </summary>
    public void AddFrame(
        float frameMs,
        float simMs,
        float gridMs,
        float overlayMs,
        float foodMs,
        float nestsMs,
        float antsMs,
        float statsMs,
        float hudMs,
        float arrayClearUs,
        float innerLoopUs,
        float marshalCopyUs,
        float drawBitmapUs)
    {
        _series[0].Add(frameMs);
        _series[1].Add(simMs);
        _series[2].Add(gridMs);
        _series[3].Add(overlayMs);
        _series[4].Add(foodMs);
        _series[5].Add(nestsMs);
        _series[6].Add(antsMs);
        _series[7].Add(statsMs);
        _series[8].Add(hudMs);
        _series[9].Add(arrayClearUs);
        _series[10].Add(innerLoopUs);
        _series[11].Add(marshalCopyUs);
        _series[12].Add(drawBitmapUs);

        _revision++;
    }

    /// <summary>
    /// Copies <paramref name="count"/> samples starting at
    /// <paramref name="startIndex"/> (0-based, oldest sample first)
    /// for <paramref name="metricIndex"/> into <paramref name="dst"/>.
    /// Returns the number of samples actually copied; may be less
    /// than requested when the window extends past
    /// <see cref="Count"/>.
    /// </summary>
    public int CopyRange(int metricIndex, int startIndex, int count, Span<float> dst)
    {
        if ((uint)metricIndex >= (uint)MetricCount)
        {
            throw new ArgumentOutOfRangeException(nameof(metricIndex));
        }
        if (count <= 0 || dst.Length == 0)
        {
            return 0;
        }

        List<float> list = _series[metricIndex];
        int total = list.Count;
        if (startIndex < 0) startIndex = 0;
        if (startIndex >= total) return 0;

        int available = total - startIndex;
        int want = count;
        if (want > available) want = available;
        if (want > dst.Length) want = dst.Length;

        ReadOnlySpan<float> src = CollectionsMarshal.AsSpan(list).Slice(startIndex, want);
        src.CopyTo(dst);
        return want;
    }

    /// <summary>
    /// Copies the most recent samples for <paramref name="metricIndex"/>
    /// into <paramref name="dst"/> in chronological order (oldest to
    /// newest). Back-compat wrapper over <see cref="CopyRange"/>.
    /// </summary>
    public int CopyLast(int metricIndex, Span<float> dst)
    {
        int total = Count;
        if (total == 0 || dst.Length == 0) return 0;
        int want = dst.Length < total ? dst.Length : total;
        int startIndex = total - want;
        return CopyRange(metricIndex, startIndex, want, dst);
    }

    /// <summary>
    /// Returns a read-only span over the entire stored series for
    /// <paramref name="metricIndex"/>. The span is valid only until
    /// the next <see cref="AddFrame"/> call; callers on the UI thread
    /// can rely on this within a single Draw pass.
    /// </summary>
    public ReadOnlySpan<float> GetSpan(int metricIndex)
    {
        if ((uint)metricIndex >= (uint)MetricCount)
        {
            throw new ArgumentOutOfRangeException(nameof(metricIndex));
        }
        return CollectionsMarshal.AsSpan(_series[metricIndex]);
    }
}
