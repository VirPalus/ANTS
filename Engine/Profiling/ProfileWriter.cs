namespace ANTS;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

/// <summary>
/// Background writer for <see cref="FrameProfiler"/> samples.
///
/// Owns a dedicated <see cref="Thread"/> that drains the SPSC ring
/// buffer every 500 ms, formats samples as CSV rows, and appends
/// them to a rotating set of files in the user's temp directory
/// (<see cref="Path.GetTempPath"/>). fase-4.12-fixup moved the
/// output location there so the files are easy to locate via
/// %TEMP% and don't clutter the install directory.
///
/// Lifecycle:
///   * Construct (in <see cref="FrameProfiler.Enable"/>, lazy).
///   * <see cref="Start"/> — opens the first CSV file, spawns the
///     writer thread. Idempotent (second call is a no-op).
///   * Hot path: FrameProfiler calls <see cref="RingBuffer{T}.Write"/>
///     from the UI thread; this class is NOT called on hot path.
///   * <see cref="Stop"/> — signals the thread, waits (bounded) for
///     it to drain remaining samples and close the file.
///   * <see cref="Dispose"/> — Stop + resource release.
///
/// File naming / rotation:
///   * Base path is %TEMP%/pheromone_profile_{N}.txt where {N} is
///     the 1-based rotation index.
///   * A new file is opened when the current one exceeds
///     <see cref="FileSizeRotateBytes"/> (default 100 MB).
///   * <see cref="CurrentFileName"/> and <see cref="RotationIndex"/>
///     are exposed to the HUD so the user can see where the active
///     log is being written.
/// </summary>
public sealed class ProfileWriter : IDisposable
{
    /// <summary>Hard cap per CSV file before rotation (100 MB).</summary>
    public const long FileSizeRotateBytes = 100L * 1024L * 1024L;

    /// <summary>Writer-thread sleep interval between drain passes.</summary>
    public const int DrainIntervalMs = 500;

    /// <summary>Bounded wait for the writer thread to join on Stop/Dispose.</summary>
    public const int ShutdownJoinTimeoutMs = 1500;

    /// <summary>Drain batch size (one stackalloc span per tick).</summary>
    private const int DrainBatch = 2048;

    private static readonly string CsvHeaderLine =
        "frame,ts_ms,sim_us,world_us,overlay_us,ants_us,stats_us,hud_us,paint_us," +
        "grid_us,food_us,nests_us,clear_us,inner_us,marshal_us,bitmap_us," +
        "canvasSetup_us,placement_us,selection_us,buttons_us,profilerWindow_us";

    private readonly RingBuffer<ProfileSample> _ring;
    private readonly object _ioLock = new object();

    private Thread? _thread;
    private ManualResetEventSlim? _wakeEvent;
    private volatile bool _stopFlag;
    private bool _started;
    private bool _disposed;

    // Session-scoped CSV state. _currentRotation starts at 0; OpenNextFile
    // increments BEFORE naming, so the first file is pheromone_profile_1.txt
    // and the <see cref="RotationIndex"/> getter returns the index of the
    // currently-open file (not the next one).
    private int _currentRotation;
    private string _logDirectory = string.Empty;
    private string _currentFileName = string.Empty;

    // Active file. null when Start has not been called or after Dispose.
    private FileStream? _currentStream;
    private StreamWriter? _currentWriter;
    private long _currentSize;

    // Session reference point: we report ts_ms as milliseconds elapsed
    // since the first sample's TimestampTicks (stamped on first write).
    private long _sessionEpochTicks;
    private bool _sessionEpochSet;

    /// <summary>
    /// Wraps the given ring buffer. Does NOT spawn a thread — call
    /// <see cref="Start"/> for that.
    /// </summary>
    public ProfileWriter(RingBuffer<ProfileSample> ring)
    {
        _ring = ring ?? throw new ArgumentNullException(nameof(ring));
    }

    /// <summary>Current CSV output basename (e.g. "pheromone_profile_3.txt"). Empty before <see cref="Start"/>.</summary>
    public string CurrentFileName
    {
        get
        {
            lock (_ioLock)
            {
                return _currentFileName;
            }
        }
    }

    /// <summary>Current 1-based rotation index. Zero before <see cref="Start"/>.</summary>
    public int RotationIndex
    {
        get
        {
            lock (_ioLock)
            {
                return _currentRotation;
            }
        }
    }

    /// <summary>
    /// Spawns the background drain thread and opens the first CSV
    /// file. Idempotent. Throws on a disposed instance.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;

        _currentRotation = 0;
        _logDirectory = Path.GetTempPath();
        Directory.CreateDirectory(_logDirectory);
        OpenNextFile();

        _wakeEvent = new ManualResetEventSlim(initialState: false);
        _stopFlag = false;
        _thread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "ANTS-ProfileWriter",
        };
        _started = true;
        _thread.Start();
    }

    /// <summary>
    /// Signals the writer thread to drain remaining samples and
    /// exit. Blocks (bounded by <see cref="ShutdownJoinTimeoutMs"/>)
    /// until the thread has joined. Idempotent.
    /// </summary>
    public void Stop()
    {
        if (!_started) return;
        _stopFlag = true;
        _wakeEvent?.Set();
        try
        {
            _thread?.Join(ShutdownJoinTimeoutMs);
        }
        catch (ThreadStateException)
        {
            // Never started — ignore.
        }
        _started = false;
    }

    /// <summary>Closes the current file and releases the wake-event.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        lock (_ioLock)
        {
            _currentWriter?.Flush();
            _currentWriter?.Dispose();
            _currentWriter = null;
            _currentStream?.Dispose();
            _currentStream = null;
        }
        _wakeEvent?.Dispose();
        _wakeEvent = null;
        _disposed = true;
    }

    /// <summary>
    /// Drain loop. Runs on the writer thread. Polls the ring every
    /// <see cref="DrainIntervalMs"/> ms; on stop, performs one final
    /// drain before exiting.
    /// </summary>
    private void WriterLoop()
    {
        Span<ProfileSample> batch = stackalloc ProfileSample[DrainBatch];
        StringBuilder sb = new StringBuilder(DrainBatch * 120);

        while (!_stopFlag)
        {
            _wakeEvent?.Wait(DrainIntervalMs);
            _wakeEvent?.Reset();
            DrainOnce(batch, sb);
        }

        // Final drain after stop signal so in-flight samples aren't lost.
        DrainOnce(batch, sb);
    }

    private void DrainOnce(Span<ProfileSample> batch, StringBuilder sb)
    {
        // Drain may need multiple passes if the ring has more than
        // DrainBatch unread samples (common during shutdown).
        while (true)
        {
            int got = _ring.Drain(batch);
            if (got == 0) return;

            sb.Clear();
            double tickToUs = 1_000_000.0 / Stopwatch.Frequency;
            double tickToMs = 1_000.0 / Stopwatch.Frequency;

            if (!_sessionEpochSet && got > 0)
            {
                _sessionEpochTicks = batch[0].TimestampTicks;
                _sessionEpochSet = true;
            }

            for (int i = 0; i < got; i++)
            {
                ref ProfileSample s = ref batch[i];
                long relTicks = s.TimestampTicks - _sessionEpochTicks;
                sb.Append(s.FrameNumber.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((relTicks * tickToMs).ToString("F3", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.SimTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.WorldDrawTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.OverlayDrawTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.AntsDrawTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.StatsDrawTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.HudDrawTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.PaintTotalTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.GridDrawTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.FoodDrawTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.NestsDrawTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.ArrayClearTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.InnerLoopTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.MarshalCopyTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.DrawBitmapTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.CanvasSetupTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.PlacementTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.SelectionTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.ButtonsTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((s.ProfilerWindowTicks * tickToUs).ToString("F1", CultureInfo.InvariantCulture));
                sb.Append('\n');
            }

            WriteChunk(sb);

            if (_currentSize >= FileSizeRotateBytes)
            {
                OpenNextFile();
            }

            // If fewer than DrainBatch returned, the ring is empty.
            if (got < batch.Length) return;
        }
    }

    private void WriteChunk(StringBuilder sb)
    {
        lock (_ioLock)
        {
            if (_currentWriter == null) return;
            string chunk = sb.ToString();
            _currentWriter.Write(chunk);
            _currentWriter.Flush();
            _currentSize += Encoding.UTF8.GetByteCount(chunk);
        }
    }

    private void OpenNextFile()
    {
        lock (_ioLock)
        {
            _currentWriter?.Flush();
            _currentWriter?.Dispose();
            _currentStream?.Dispose();

            _currentRotation++;
            string name = string.Format(
                CultureInfo.InvariantCulture,
                "pheromone_profile_{0}.txt",
                _currentRotation);
            string path = Path.Combine(_logDirectory, name);
            _currentFileName = name;

            _currentStream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            _currentWriter = new StreamWriter(_currentStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                NewLine = "\n",
            };

            long droppedSoFar = _ring.DroppedCount;
            _currentWriter.Write("# ANTS FrameProfiler rotation=");
            _currentWriter.Write(_currentRotation.ToString("D3", CultureInfo.InvariantCulture));
            _currentWriter.Write(" dropped_so_far=");
            _currentWriter.Write(droppedSoFar.ToString(CultureInfo.InvariantCulture));
            _currentWriter.Write('\n');
            _currentWriter.Write(CsvHeaderLine);
            _currentWriter.Write('\n');
            _currentWriter.Flush();

            _currentSize = _currentStream.Position;
        }
    }
}
