namespace ANTS;

using System;

/// <summary>
/// Bounded in-memory time series for profiler graph rendering.
///
/// Owns 13 parallel float ring buffers (one per visualizable metric),
/// each holding <see cref="SampleCapacity"/> most-recent samples.
/// Sized for 5 minutes at 60 FPS at 1x zoom (18000 samples).
///
/// Hot-path contract: <see cref="AddFrame"/> is called from the UI
/// thread inside <see cref="FrameProfiler.EndFrame"/>. It writes
/// exactly 13 float stores + one index increment. Zero allocations.
///
/// Readers call <see cref="CopyLast"/> for a metric index to obtain
/// the last <c>dst.Length</c> samples in chronological order (oldest
/// first). Returns the number of samples actually copied, which may
/// be less than <c>dst.Length</c> when the buffer is not yet full.
///
/// Metric indices are fixed (see <see cref="MetricCount"/> comment):
///   0 = FrameMs, 1 = SimMs, 2 = GridMs, 3 = OverlayMs, 4 = FoodMs,
///   5 = NestsMs, 6 = AntsMs, 7 = StatsMs, 8 = HudMs,
///   9 = ArrayClearUs, 10 = InnerLoopUs, 11 = MarshalCopyUs, 12 = DrawBitmapUs.
///
/// Memory budget: 13 * 18000 * 4 bytes = ~912 KiB. Pre-allocated in
/// the constructor and held for the process lifetime.
/// </summary>
public sealed class ProfilerSeries
{
    /// <summary>Per-metric ring buffer capacity (5 minutes at 60 FPS).</summary>
    public const int SampleCapacity = 18000;

    /// <summary>Number of metrics tracked (must match the enum above).</summary>
    public const int MetricCount = 13;

    private readonly float[][] _series;
    private int _writeIndex;
    private int _count;

    public ProfilerSeries()
    {
        _series = new float[MetricCount][];
        for (int i = 0; i < MetricCount; i++)
        {
            _series[i] = new float[SampleCapacity];
        }
    }

    /// <summary>Number of valid samples available (saturates at <see cref="SampleCapacity"/>).</summary>
    public int Count => _count;

    /// <summary>Total buffer capacity per metric.</summary>
    public static int Capacity => SampleCapacity;

    /// <summary>
    /// Appends a single 13-metric sample to every per-metric ring.
    /// All 13 arguments must be in the correct unit (ms for 0..8,
    /// microseconds for 9..12) — conversion is done by the caller.
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
        int i = _writeIndex;
        _series[0][i] = frameMs;
        _series[1][i] = simMs;
        _series[2][i] = gridMs;
        _series[3][i] = overlayMs;
        _series[4][i] = foodMs;
        _series[5][i] = nestsMs;
        _series[6][i] = antsMs;
        _series[7][i] = statsMs;
        _series[8][i] = hudMs;
        _series[9][i] = arrayClearUs;
        _series[10][i] = innerLoopUs;
        _series[11][i] = marshalCopyUs;
        _series[12][i] = drawBitmapUs;

        _writeIndex = (i + 1) % SampleCapacity;
        if (_count < SampleCapacity)
        {
            _count++;
        }
    }

    /// <summary>
    /// Copies the most recent samples for <paramref name="metricIndex"/>
    /// into <paramref name="dst"/> in chronological order (oldest to
    /// newest). Returns the number of samples copied.
    /// </summary>
    /// <remarks>
    /// If <c>dst.Length</c> exceeds <see cref="Count"/>, only
    /// <c>Count</c> samples are written; <c>dst[Count..]</c> is
    /// untouched. If <c>dst.Length</c> is less than <c>Count</c>, the
    /// oldest samples are skipped and only the last <c>dst.Length</c>
    /// are returned.
    /// </remarks>
    public int CopyLast(int metricIndex, Span<float> dst)
    {
        if ((uint)metricIndex >= (uint)MetricCount)
        {
            throw new ArgumentOutOfRangeException(nameof(metricIndex));
        }
        if (_count == 0 || dst.Length == 0)
        {
            return 0;
        }

        float[] src = _series[metricIndex];
        int available = _count;
        int want = dst.Length < available ? dst.Length : available;

        // "newest index + 1" wraps to the oldest valid sample position.
        // When count<capacity, oldest is at 0; else at _writeIndex.
        int oldest;
        if (_count < SampleCapacity)
        {
            oldest = _count - want;
        }
        else
        {
            // _writeIndex points at the slot to be overwritten next =
            // the oldest. Walk forward (count - want) slots to skip.
            oldest = (_writeIndex + (SampleCapacity - want)) % SampleCapacity;
        }

        if (oldest + want <= SampleCapacity)
        {
            new Span<float>(src, oldest, want).CopyTo(dst);
        }
        else
        {
            int first = SampleCapacity - oldest;
            new Span<float>(src, oldest, first).CopyTo(dst);
            new Span<float>(src, 0, want - first).CopyTo(dst.Slice(first));
        }

        return want;
    }
}
