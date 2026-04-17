namespace ANTS;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

public partial class Engine : Form
{
    // Render-pixel size of one simulation cell at Zoom = 1.
    // This is *only* a rendering constant. The simulation treats a
    // cell as its atomic unit (see Ant.X / Ant.Y, PheromoneGrid, etc.).
    private const int CellSize = 16;
    private const int BorderThickness = 16;
    private const int ButtonHeight = 32;
    private const int ButtonWidth = 140;
    private const int ButtonPadding = 8;

    // Default world size in cells, used when no map is loaded yet.
    // A map loader (Phase 2) may replace the world with one sized
    // from an input PNG; this default just keeps the engine runnable
    // standalone.
    private const int DefaultWorldWidthCells = 256;
    private const int DefaultWorldHeightCells = 160;

    // Pan speed for WASD, in screen pixels per second.
    private const float KeyboardPanPxPerSecond = 900f;

    // Margin to keep around the world when fitting it on start, so
    // the edges don't touch the window border.
    private const float FitMarginPx = 40f;

    // Minimum on-screen area of the world that must stay visible when
    // the camera is panned/clamped, so you can't lose the map.
    private const float ClampKeepVisiblePx = 60f;

    private const float FoodGreenR = 34f / 255f;
    private const float FoodGreenG = 197f / 255f;
    private const float FoodGreenB = 94f / 255f;

    private const float PheromoneOverlayCutoff = 0.03f;
    private const float PheromoneOverlayMaxAlpha = 160f;

    private const int StatsPanelWidth = 260;
    private const int StatsCardHeight = 230;
    private const int StatsCardSpacing = 8;
    private const int StatsCardPadding = 10;
    private const int StatsHeaderHeight = 10;
    private const int StatsGraphHeight = 44;
    private const int StatsLineHeight = 16;
    private const int StatsRoleBarHeight = 6;
    private const int StatsRoleBarWidth = 80;

    private static readonly SKColor ScoutBarColor = new SKColor(96, 165, 250);
    private static readonly SKColor ForagerBarColor = new SKColor(251, 191, 36);
    private static readonly SKColor DefenderBarColor = new SKColor(239, 68, 68);
    private static readonly SKColor AttackerBarColor = new SKColor(168, 85, 247);
    private static readonly SKColor DefenseBarColor = new SKColor(220, 38, 38);
    private static readonly SKColor OffenseBarColor = new SKColor(234, 179, 8);

    private static readonly SKColor FoodPheromoneColor = new SKColor(34, 197, 94);
    private static readonly SKColor EnemyPheromoneColor = new SKColor(239, 68, 68);

    private static readonly Color[] ColonyColors = new Color[]
    {
        Color.FromArgb(239, 68, 68),
        Color.FromArgb(59, 130, 246),
        Color.FromArgb(234, 179, 8),
        Color.FromArgb(249, 115, 22),
        Color.FromArgb(168, 85, 247),
        Color.FromArgb(236, 72, 153),
    };

    private static readonly Color FoodColor = Color.FromArgb(34, 197, 94);

    private World _world = null!;
    private Camera _camera = new Camera();

    private List<UiButton> _buttons = new List<UiButton>();
    private PlacingMode _placingMode = PlacingMode.None;
    private int _nextColorIndex;
    private int _mouseX;
    private int _mouseY;
    private bool _isDrawingFood;
    private bool _showPheromones;

    // Right-mouse drag pan state.
    private bool _isRightDragging;
    private int _rightDragLastX;
    private int _rightDragLastY;

    // Latched pan keys (WASD). Checked every Tick so the speed is
    // frame-rate-independent and key-repeat-free.
    private bool _keyPanLeft;
    private bool _keyPanRight;
    private bool _keyPanUp;
    private bool _keyPanDown;
    private long _lastPanTicks;

    private readonly Stopwatch _fpsStopwatch = new Stopwatch();
    private int _framesThisSecond;
    private int _fps;
    private double _lastFrameMs;

    private double _simStageMs;
    private double _antStageMs;
    private const double StageEmaAlpha = 0.05;

    private FastSKGLControl _skControl = null!;
    private SKPaint _fillPaint = null!;
    private SKPaint _strokePaint = null!;
    private SKPaint _textPaint = null!;
    private SKPaint _antPaint = null!;
    private SKPicture _gridPicture = null!;
    private SKPicture? _gridLinesPicture;  // separate picture for grid lines (zoom-dependent)
    private SKPicture _hudPicture = null!;
    private SKPicture _statsPicture = null!;
    private SKPicture _buttonsPicture = null!;
    private bool _buttonsDirty = true;

    // Cached food picture — only rebuilt when food actually changes.
    private SKPicture? _foodPicture;
    private int _foodPictureCachedVersion = -1;

    // Cached TopBar picture — only rebuilt when pause/speed/resize.
    private SKPicture? _topBarPicture;
    private bool _topBarDirty = true;

    // Cached nests picture — rebuilt when colonies are added/removed.
    private SKPicture? _nestsPicture;
    private int _nestsPictureCachedCount = -1;

    // New UI components -- owned by the engine, wired in constructor.
    private UiTopBar _topBar = null!;
    private UiStartOverlay _startOverlay = null!;
    private SKPaint _titlePaint = null!;
    private SKPaint _borderPaint = null!;
    private SKPaint _smallTextPaint = null!;
    private SKPath _nestPath = null!;
    private SKPath _foodPath = null!;
    private SKRect[] _antBodySprites = Array.Empty<SKRect>();
    private SKRotationScaleMatrix[] _antBodyTransforms = Array.Empty<SKRotationScaleMatrix>();
    // Reusable buffer for batched ant dot positions (zoom-out mode).
    private SKPoint[] _antDotPoints = Array.Empty<SKPoint>();
    private SKColorFilter? _antBodyColorFilter;
    private uint _antBodyColorFilterCachedColor;
    private SKFontMetrics _textMetrics;
    private float _textHeight;

    private readonly Stopwatch _hudStopwatch = new Stopwatch();
    private const int HudUpdateIntervalMs = 50;

    private int _frameCap = 10000;
    private long _ticksPerFrame;
    private long _nextFrameTicks;

    private long _ticksPerSimStep;
    private long _simAccumulatorTicks;
    private long _lastSimTimestamp;

    // Sim control state. Speed scales the wall-clock delta fed to the
    // fixed-timestep accumulator, so 2.0 runs the sim at double speed
    // while 0.5 runs it at half. Paused freezes the simulation but
    // leaves rendering / panning / zooming responsive.
    private bool _paused;
    private double _speedMultiplier = 1.0;
    private static readonly double[] SpeedChoices = new double[] { 1.0, 2.0, 5.0, 10.0 };

    private SKColor _foodSkColor;

    private readonly List<IDisposable> _ownedDisposables = new List<IDisposable>();

    public Engine()
    {
        InitializeComponent();

        _fillPaint = Own(new SKPaint());
        _fillPaint.Style = SKPaintStyle.Fill;
        _fillPaint.IsAntialias = false;

        _strokePaint = Own(new SKPaint());
        _strokePaint.Style = SKPaintStyle.Stroke;
        _strokePaint.IsAntialias = false;
        _strokePaint.StrokeWidth = 1;

        _textPaint = Own(new SKPaint());
        _textPaint.Style = SKPaintStyle.Fill;
        _textPaint.IsAntialias = true;
        _textPaint.Color = SKColors.White;
        _textPaint.TextSize = 14;

        _antPaint = Own(new SKPaint());
        _antPaint.Style = SKPaintStyle.Fill;
        _antPaint.IsAntialias = true;
        _antPaint.FilterQuality = SKFilterQuality.Low;

        _textMetrics = _textPaint.FontMetrics;
        _textHeight = -_textMetrics.Ascent + _textMetrics.Descent;

        _nestPath = Own(new SKPath());
        _foodPath = Own(new SKPath());

        _borderPaint = Own(new SKPaint());
        _borderPaint.Style = SKPaintStyle.Stroke;
        _borderPaint.IsAntialias = false;
        _borderPaint.StrokeWidth = 1;

        _titlePaint = Own(new SKPaint());
        _titlePaint.Style = SKPaintStyle.Fill;
        _titlePaint.IsAntialias = true;
        _titlePaint.Color = UiTheme.TextStrong;
        _titlePaint.TextSize = UiTheme.FontDisplay;

        _smallTextPaint = Own(new SKPaint());
        _smallTextPaint.Style = SKPaintStyle.Fill;
        _smallTextPaint.IsAntialias = true;
        _smallTextPaint.Color = UiTheme.TextMuted;
        _smallTextPaint.TextSize = UiTheme.FontSmall;

        _foodSkColor = new SKColor(FoodColor.R, FoodColor.G, FoodColor.B);

        _skControl = new FastSKGLControl();
        _skControl.Dock = DockStyle.Fill;
        _skControl.PaintSurface += OnSkPaintSurface;
        _skControl.MouseDown += OnSkMouseDown;
        _skControl.MouseMove += OnSkMouseMove;
        _skControl.MouseUp += OnSkMouseUp;
        _skControl.MouseWheel += OnSkMouseWheel;
        Controls.Add(_skControl);

        // Route keyboard through the Form so WASD / ESC fire no matter
        // which child control has focus.
        KeyPreview = true;

        // --- UI top bar: pause / speed controls ---
        _topBar = new UiTopBar(
            SpeedChoices,
            () => _paused,
            () => _speedMultiplier,
            TogglePause,
            SetSpeed);

        // --- Start overlay: map selection screen ---
        _startOverlay = new UiStartOverlay(OnStartOverlayPick);
        string mapsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Maps");
        _startOverlay.Scan(mapsDir, 180);

        InitializeWorld();
        RecalculateLayout();
        _camera.FitWorld(_world.Width * CellSize, _world.Height * CellSize, ClientSize.Width, ClientSize.Height, FitMarginPx);
        _lastPanTicks = Stopwatch.GetTimestamp();

        // If maps were found, show the start overlay. Otherwise skip
        // straight to the demo map.
        if (_startOverlay.Entries.Count > 0)
        {
            _paused = true;
        }
        else
        {
            _startOverlay.Visible = false;
        }

        _fpsStopwatch.Start();
        _hudStopwatch.Start();
        RecordHudPicture();
        RecordStatsPicture();
        RecordFoodPicture();
        RecordNestsPicture();
        RecordTopBarPicture();
        UpdateFrameCapTiming();
        _ticksPerSimStep = Stopwatch.Frequency / World.SimHz;
        _lastSimTimestamp = Stopwatch.GetTimestamp();
        Application.Idle += OnApplicationIdle;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int FrameCap
    {
        get { return _frameCap; }
        set
        {
            _frameCap = value;
            UpdateFrameCapTiming();
        }
    }

    private void UpdateFrameCapTiming()
    {
        if (_frameCap > 0)
        {
            _ticksPerFrame = Stopwatch.Frequency / _frameCap;
        }
        else
        {
            _ticksPerFrame = 0;
        }
        _nextFrameTicks = Stopwatch.GetTimestamp();
    }

    private T Own<T>(T item) where T : class, IDisposable
    {
        _ownedDisposables.Add(item);
        return item;
    }

    private void Replace<T>(ref T field, T newValue) where T : class, IDisposable
    {
        if (field != null)
        {
            _ownedDisposables.Remove(field);
            field.Dispose();
        }
        field = newValue;
        if (newValue != null)
        {
            _ownedDisposables.Add(newValue);
        }
    }

    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        while (IsApplicationIdle())
        {
            if (_ticksPerFrame <= 0)
            {
                Tick();
                continue;
            }

            long nowTicks = Stopwatch.GetTimestamp();
            if (nowTicks >= _nextFrameTicks)
            {
                _nextFrameTicks = nowTicks + _ticksPerFrame;
                Tick();
            }
            else
            {
                Thread.Sleep(0);
            }
        }
    }

    private void InitializeWorld()
    {
        // Try to pick up a PNG from the Maps/ folder next to the
        // executable. Falling back to the in-code DemoMap means the
        // game always boots even on a fresh checkout with no assets.
        MapDefinition map = LoadFirstMapOrDemo();

        _world = new World(map.Width, map.Height);
        _world.ApplyMapLayout(map);

        int seedCount = map.ColonySeeds.Count;
        for (int i = 0; i < seedCount; i++)
        {
            ColonySeed seed = map.ColonySeeds[i];
            _world.AddColony(seed.X, seed.Y, seed.Color);
        }
        // Manual "Add Colony" picks up after whatever the map used so
        // we don't re-use the team palette for user-placed nests.
        _nextColorIndex = seedCount;

        RecordGridPicture();
        // Food and nests are built later in the constructor after
        // all paints are ready, via the RecordFoodPicture / RecordNestsPicture
        // calls after _hudStopwatch.Start().
    }

    // Scan <exe>/Maps/*.png for map images in a stable order. First hit
    // wins; if the folder is missing or empty we fall through to the
    // hard-coded DemoMap so the engine still runs.
    private static MapDefinition LoadFirstMapOrDemo()
    {
        string exeDir = AppContext.BaseDirectory;
        string mapsDir = System.IO.Path.Combine(exeDir, "Maps");
        if (System.IO.Directory.Exists(mapsDir))
        {
            string[] pngs = System.IO.Directory.GetFiles(mapsDir, "*.png");
            Array.Sort(pngs, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pngs.Length; i++)
            {
                try
                {
                    return MapLoader.Load(pngs[i]);
                }
                catch
                {
                    // Malformed PNG or unreadable file -- fall through
                    // to the next candidate rather than crashing the
                    // whole engine on a bad map.
                }
            }
        }
        return DemoMap.Build();
    }

    // Called when the user picks a map in the start overlay. Swaps in
    // the chosen world, hides the overlay, stays paused so the player
    // can orient before pressing Play.
    private void OnStartOverlayPick(UiStartOverlay.Entry entry)
    {
        MapDefinition map = MapLoader.Load(entry.Path);
        _world = new World(map.Width, map.Height);
        _world.ApplyMapLayout(map);

        for (int i = 0; i < map.ColonySeeds.Count; i++)
        {
            ColonySeed seed = map.ColonySeeds[i];
            _world.AddColony(seed.X, seed.Y, seed.Color);
        }
        _nextColorIndex = map.ColonySeeds.Count;

        RecordGridPicture();
        RecordFoodPicture();
        RecordNestsPicture();
        _camera.FitWorld(_world.Width * CellSize, _world.Height * CellSize, ClientSize.Width, ClientSize.Height, FitMarginPx);
        _startOverlay.Visible = false;
        _topBar.MapName = entry.DisplayName;
        _topBarDirty = true;
        RecalculateLayout();
    }

    private void RecordGridPicture()
    {
        int gridWidth = _world.Width * CellSize;
        int gridHeight = _world.Height * CellSize;

        // Extra margin around the world so the thick border is fully
        // visible inside the SKPicture.
        const int Margin = 16;
        SKRect cullRect = new SKRect(-Margin, -Margin, gridWidth + Margin, gridHeight + Margin);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);

        // 0. Filled rounded rect in WallColor — the unified base for
        //    border + walls. Walls (same color) merge on top. Only the
        //    4 rounded corners peek through where no wall cell covers them.
        using (SKPaint basePaint = new SKPaint())
        {
            basePaint.Style = SKPaintStyle.Fill;
            basePaint.IsAntialias = true;
            basePaint.Color = UiTheme.WallColor;

            const float cornerRadius = 16f;
            SKRect baseRect = new SKRect(
                -BorderThickness, -BorderThickness,
                gridWidth + BorderThickness, gridHeight + BorderThickness);
            using SKRoundRect baseRr = new SKRoundRect(baseRect, cornerRadius, cornerRadius);
            recordingCanvas.DrawRoundRect(baseRr, basePaint);
        }

        // 1. World background fill.
        using (SKPaint worldBgPaint = new SKPaint())
        {
            worldBgPaint.Style = SKPaintStyle.Fill;
            worldBgPaint.IsAntialias = false;
            worldBgPaint.Color = UiTheme.BgWorld;
            recordingCanvas.DrawRect(0, 0, gridWidth, gridHeight, worldBgPaint);
        }

        // 3. Grid lines — recorded into a SEPARATE SKPicture so we can
        //    skip the replay when zoomed out (lines invisible anyway).
        //    All lines are batched into a single SKPath = 1 GPU draw call.
        {
            SKPictureRecorder gridLinesRecorder = new SKPictureRecorder();
            SKCanvas glCanvas = gridLinesRecorder.BeginRecording(cullRect);

            using (SKPaint linePaint = new SKPaint())
            {
                linePaint.Style = SKPaintStyle.Stroke;
                linePaint.IsAntialias = false;
                linePaint.Color = UiTheme.GridLine;
                linePaint.StrokeWidth = 0;  // hairline = always 1 device pixel, any zoom

                using (SKPath linePath = new SKPath())
                {
                    int w = _world.Width;
                    int h = _world.Height;
                    for (int x = 1; x < w; x++)
                    {
                        float px = x * CellSize;
                        linePath.MoveTo(px, 0);
                        linePath.LineTo(px, gridHeight);
                    }
                    for (int y = 1; y < h; y++)
                    {
                        float py = y * CellSize;
                        linePath.MoveTo(0, py);
                        linePath.LineTo(gridWidth, py);
                    }
                    if (linePath.PointCount > 0)
                    {
                        glCanvas.DrawPath(linePath, linePaint);
                    }
                }
            }

            Replace(ref _gridLinesPicture!, gridLinesRecorder.EndRecording());
            gridLinesRecorder.Dispose();
        }

        // 3. Walls — baked as a SINGLE batched SKPath (1 GPU draw call).
        using (SKPaint wallPaint = new SKPaint())
        {
            wallPaint.Style = SKPaintStyle.Fill;
            wallPaint.IsAntialias = false;
            wallPaint.Color = UiTheme.WallColor;

            using (SKPath wallPath = new SKPath())
            {
                int w = _world.Width;
                int h = _world.Height;
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        if (_world.GetCell(x, y) == CellType.Wall)
                        {
                            wallPath.AddRect(new SKRect(
                                x * CellSize, y * CellSize,
                                (x + 1) * CellSize, (y + 1) * CellSize));
                        }
                    }
                }
                if (wallPath.PointCount > 0)
                {
                    recordingCanvas.DrawPath(wallPath, wallPaint);
                }
            }
        }

        Replace(ref _gridPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    private void Tick()
    {
        long startTicks = Stopwatch.GetTimestamp();

        _framesThisSecond++;

        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
        {
            _fps = _framesThisSecond;
            _framesThisSecond = 0;
            _fpsStopwatch.Restart();
        }

        long simStartTicks = Stopwatch.GetTimestamp();
        long simDeltaTicks = simStartTicks - _lastSimTimestamp;
        _lastSimTimestamp = simStartTicks;

        if (_paused)
        {
            // Throw away the elapsed wall-clock so the accumulator
            // doesn't unleash a burst of catch-up ticks the moment we
            // un-pause. Rendering still runs so UI stays responsive.
            _simAccumulatorTicks = 0;
        }
        else
        {
            long scaledDelta = (long)(simDeltaTicks * _speedMultiplier);
            _simAccumulatorTicks += scaledDelta;
            while (_simAccumulatorTicks >= _ticksPerSimStep)
            {
                _world.Update();
                _simAccumulatorTicks -= _ticksPerSimStep;
            }
        }
        long simEndTicks = Stopwatch.GetTimestamp();
        UpdateStageEma(ref _simStageMs, TicksToMilliseconds(simEndTicks - simStartTicks));

        ApplyKeyboardPan();

        if (_hudStopwatch.ElapsedMilliseconds >= HudUpdateIntervalMs)
        {
            RecordHudPicture();
            RecordStatsPicture();
            _hudStopwatch.Restart();
        }

        _skControl.RenderFrameDirect();

        long endTicks = Stopwatch.GetTimestamp();
        _lastFrameMs = TicksToMilliseconds(endTicks - startTicks);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
    }

    private void RecalculateLayout()
    {
        if (_world == null)
        {
            return;
        }

        // The world is no longer glued to the window centre. The
        // camera keeps whatever pan/zoom the user has, clamped so the
        // world doesn't slide entirely off-screen after a resize.
        // Camera clamp removed — pan and zoom are fully free.

        RebuildButtons();
    }

    private void RebuildButtons()
    {
        _buttons.Clear();

        // Bottom toolbar: Add Colony, Add Food, Pheromones — centered
        // horizontally across the window.
        int buttonY = ClientSize.Height - ButtonHeight - ButtonPadding * 2;
        int totalButtonsWidth = ButtonWidth * 3 + ButtonPadding * 2;
        int startX = (ClientSize.Width - totalButtonsWidth) / 2;
        int addColonyX = startX;
        int addFoodX = addColonyX + ButtonWidth + ButtonPadding;
        int pheromoneX = addFoodX + ButtonWidth + ButtonPadding;

        UiButton addColonyButton = new UiButton(
            new Rectangle(addColonyX, buttonY, ButtonWidth, ButtonHeight),
            "Add Colony", StartPlacingColony);
        addColonyButton.IsActive = () => _placingMode == PlacingMode.Colony;
        CacheButtonTextPosition(addColonyButton);
        _buttons.Add(addColonyButton);

        UiButton addFoodButton = new UiButton(
            new Rectangle(addFoodX, buttonY, ButtonWidth, ButtonHeight),
            "Add Food", StartPlacingFood);
        addFoodButton.IsActive = () => _placingMode == PlacingMode.Food;
        CacheButtonTextPosition(addFoodButton);
        _buttons.Add(addFoodButton);

        UiButton pheromoneButton = new UiButton(
            new Rectangle(pheromoneX, buttonY, ButtonWidth, ButtonHeight),
            "Pheromones", TogglePheromones);
        pheromoneButton.IsActive = () => _showPheromones;
        CacheButtonTextPosition(pheromoneButton);
        _buttons.Add(pheromoneButton);
        _buttonsDirty = true;
        _topBarDirty = true;

        // Pause + speed are now owned by UiTopBar.
        _topBar.Layout(ClientSize.Width);
        _topBar.CacheTextPositions(_textPaint, _textMetrics, _textHeight);

        // Start overlay layout (if visible).
        _startOverlay.Layout(ClientSize.Width, ClientSize.Height);
    }

    private static string FormatSpeedLabel(double speed)
    {
        // Prefer "1x" / "2x" over "1.0x" when the value is an integer,
        // "0.5x" otherwise. Keeps the chips narrow.
        if (Math.Abs(speed - Math.Round(speed)) < 0.001)
        {
            return ((int)Math.Round(speed)).ToString() + "x";
        }
        return speed.ToString("0.##") + "x";
    }

    private void TogglePause()
    {
        _paused = !_paused;
        // UiTopBar pulls pause state via callback, so just re-layout
        // to flip the "Pause"/"Play" label.
        _topBar.Layout(ClientSize.Width);
        _topBar.CacheTextPositions(_textPaint, _textMetrics, _textHeight);
        _topBarDirty = true;
    }

    private void SetSpeed(double speed)
    {
        if (Math.Abs(_speedMultiplier - speed) < 0.001)
        {
            return;
        }
        _speedMultiplier = speed;
        // Discard pending accumulator so a big speed change doesn't
        // deliver a one-shot burst of ticks.
        _simAccumulatorTicks = 0;
        _topBarDirty = true;
    }

    private void RecordHudPicture()
    {
        float hudW = 150f;
        float hudH = 84f;
        SKRect cullRect = new SKRect(0, 0, hudW + 20f, hudH + 20f);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);

        // Rounded panel background + border.
        float px = 8f;
        float py = UiTopBar.BarHeight + 8f;
        using (SKPaint bgPaint = UiTheme.NewFillPaint(UiTheme.BgPanel))
        using (SKPaint brPaint = UiTheme.NewStrokePaint(UiTheme.BorderSubtle, UiTheme.BorderThin))
        {
            UiPanel.DrawWithBorder(recordingCanvas, bgPaint, brPaint, px, py, hudW, hudH, UiTheme.CornerMedium);
        }

        using (SKPaint hudText = new SKPaint())
        {
            hudText.Style = SKPaintStyle.Fill;
            hudText.IsAntialias = true;
            hudText.TextSize = UiTheme.FontSmall;

            float lineStep = 16f;
            float baseX = px + 10f;
            float baseY = py + 6f - hudText.FontMetrics.Ascent;

            float valX = baseX + 50f;  // value column — tight but readable

            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("FPS", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextStrong;
            recordingCanvas.DrawText(_fps.ToString(), valX, baseY, hudText);

            baseY += lineStep;
            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("Frame", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextBody;
            recordingCanvas.DrawText(_lastFrameMs.ToString("F2") + " ms", valX, baseY, hudText);

            baseY += lineStep;
            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("Sim", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextBody;
            recordingCanvas.DrawText(_simStageMs.ToString("F2") + " ms", valX, baseY, hudText);

            baseY += lineStep;
            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("Ants", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextBody;
            recordingCanvas.DrawText(_antStageMs.ToString("F3") + " ms", valX, baseY, hudText);
        }

        Replace(ref _hudPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    private void RecordButtonsPicture()
    {
        // Buttons are static between hovers/clicks, so we cache them
        // as an SKPicture and only rebuild when state changes.
        int w = ClientSize.Width;
        int h = ClientSize.Height;
        SKRect cullRect = new SKRect(0, 0, w + 20, h + 20);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].Draw(rc, _fillPaint, _borderPaint, _textPaint);
        }

        Replace(ref _buttonsPicture!, recorder.EndRecording());
        recorder.Dispose();
        _buttonsDirty = false;
    }

    private void RecordStatsPicture()
    {
        // Record the entire stats panel into an SKPicture so the
        // expensive per-colony drawing (rounded cards, population
        // graph paths, role bars) only runs every 50ms instead of
        // every frame. This was the #1 source of the perf regression.
        SKRect cullRect = new SKRect(0, 0, ClientSize.Width + 20, ClientSize.Height + 20);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        DrawStatsPanel(rc);

        Replace(ref _statsPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    // Cache food cells as an SKPicture. Only rebuilt when the food
    // count changes (food placed, food eaten). Eliminates 613 AddRect
    // + path rebuild every frame for static food.
    private void RecordFoodPicture()
    {
        int foodCount = _world.FoodCount;
        _foodPictureCachedVersion = _world.FoodVersion;

        if (foodCount == 0)
        {
            if (_foodPicture != null) { Replace(ref _foodPicture!, null!); }
            return;
        }

        int gridWidth = _world.Width * CellSize;
        int gridHeight = _world.Height * CellSize;
        SKRect cullRect = new SKRect(0, 0, gridWidth, gridHeight);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        using (SKPaint foodPaint = new SKPaint())
        {
            foodPaint.Style = SKPaintStyle.Fill;
            foodPaint.IsAntialias = false;

            Point[] foodCells = _world.FoodCells;
            for (int i = 0; i < foodCount; i++)
            {
                int cellX = foodCells[i].X;
                int cellY = foodCells[i].Y;
                float amount = _world.GetFoodAmount(cellX, cellY);
                // 4 pickup steps: 0.4=100%, 0.3=75%, 0.2=50%, 0.1=25%
                // Map amount (0..0.4) to alpha (0..255) in 4 discrete steps.
                int step = (int)(amount * 10f); // 4,3,2,1,0
                if (step > 4) step = 4;
                byte alpha = (byte)(step * 64); // 256,192,128,64,0
                if (alpha < 64) alpha = 64; // min visible
                if (step == 4) alpha = 255;
                foodPaint.Color = _foodSkColor.WithAlpha(alpha);
                rc.DrawRect(cellX * CellSize, cellY * CellSize, CellSize, CellSize, foodPaint);
            }
        }

        Replace(ref _foodPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    // Cache the top bar as an SKPicture. Only rebuilt when pause state,
    // speed, or window size changes. Eliminates ~18 live draw calls
    // per frame (bar bg, border, pause button, 4 speed segments, texts).
    private void RecordTopBarPicture()
    {
        int w = ClientSize.Width;
        // Re-layout so active speed index and pause label refresh.
        _topBar.Layout(w);
        _topBar.CacheTextPositions(_textPaint, _textPaint.FontMetrics, -_textPaint.FontMetrics.Ascent + _textPaint.FontMetrics.Descent);

        SKRect cullRect = new SKRect(0, 0, w + 20, UiTopBar.BarHeight + 4);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        _topBar.Draw(rc, _fillPaint, _borderPaint, _textPaint);

        Replace(ref _topBarPicture!, recorder.EndRecording());
        recorder.Dispose();
        _topBarDirty = false;
    }

    // Cache colony nests as an SKPicture. Only rebuilt when colonies
    // are added or removed. Nests don't move, so this is static.
    private void RecordNestsPicture()
    {
        IReadOnlyList<Colony> colonies = _world.Colonies;
        int colonyCount = colonies.Count;
        _nestsPictureCachedCount = colonyCount;

        if (colonyCount == 0)
        {
            if (_nestsPicture != null) { Replace(ref _nestsPicture!, null!); }
            return;
        }

        int gridWidth = _world.Width * CellSize;
        int gridHeight = _world.Height * CellSize;
        SKRect cullRect = new SKRect(0, 0, gridWidth, gridHeight);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        using (SKPaint nestFill = new SKPaint())
        {
            nestFill.Style = SKPaintStyle.Fill;
            nestFill.IsAntialias = false;

            for (int i = 0; i < colonyCount; i++)
            {
                Colony colony = colonies[i];
                nestFill.Color = colony.CachedSkColor;

                _nestPath.Reset();
                for (int dy = -World.NestRadius; dy <= World.NestRadius; dy++)
                {
                    for (int dx = -World.NestRadius; dx <= World.NestRadius; dx++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) > World.NestRadius) continue;
                        int cellX = colony.NestX + dx;
                        int cellY = colony.NestY + dy;
                        _nestPath.AddRect(new SKRect(
                            cellX * CellSize, cellY * CellSize,
                            (cellX + 1) * CellSize, (cellY + 1) * CellSize));
                    }
                }
                rc.DrawPath(_nestPath, nestFill);
            }
        }

        Replace(ref _nestsPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    private void CacheButtonTextPosition(UiButton button)
    {
        float textWidth = _textPaint.MeasureText(button.Label);
        float labelTopX = button.Bounds.X + (button.Bounds.Width - textWidth) / 2;
        float labelTopY = button.Bounds.Y + (button.Bounds.Height - _textHeight) / 2;
        button.TextBaselineX = labelTopX;
        button.TextBaselineY = labelTopY - _textMetrics.Ascent;
    }

    private void StartPlacingColony()
    {
        _placingMode = PlacingMode.Colony;
        _isDrawingFood = false;
        Cursor = Cursors.Cross;
    }

    private void StartPlacingFood()
    {
        _placingMode = PlacingMode.Food;
        _isDrawingFood = false;
        Cursor = Cursors.Cross;
    }

    private void TogglePheromones()
    {
        _showPheromones = !_showPheromones;
    }

    private void CancelPlacing()
    {
        _placingMode = PlacingMode.None;
        _isDrawingFood = false;
        Cursor = Cursors.Default;
    }

    private void OnSkMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _isRightDragging = true;
            _rightDragLastX = e.X;
            _rightDragLastY = e.Y;
            _skControl.Focus();
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_startOverlay.Visible)
        {
            _startOverlay.HandleClick(e.X, e.Y);
            return;
        }

        if (_topBar.HandleClick(e.X, e.Y))
        {
            _topBarDirty = true;
            return;
        }

        for (int i = 0; i < _buttons.Count; i++)
        {
            if (_buttons[i].Contains(e.X, e.Y))
            {
                _buttons[i].OnClick();
                _buttonsDirty = true;
                return;
            }
        }

        if (_placingMode == PlacingMode.Colony)
        {
            ScreenToCell(e.X, e.Y, out int cellX, out int cellY);
            if (!NestFitsInWorld(cellX, cellY))
            {
                return;
            }

            Color nextColor = ColonyColors[_nextColorIndex % ColonyColors.Length];
            _nextColorIndex++;

            _world.AddColony(cellX, cellY, nextColor);
            CancelPlacing();
            return;
        }

        if (_placingMode == PlacingMode.Food)
        {
            _isDrawingFood = true;
            PaintFoodAtMouse(e.X, e.Y);
        }
    }

    private void OnSkMouseMove(object? sender, MouseEventArgs e)
    {
        _mouseX = e.X;
        _mouseY = e.Y;

        // Hover tracking for all interactive UI.
        bool wasPauseHovered = _topBar.PauseButton.IsHovered;
        _topBar.UpdateHover(e.X, e.Y);
        if (_topBar.PauseButton.IsHovered != wasPauseHovered)
        {
            _topBarDirty = true;
        }
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool wasHovered = _buttons[i].IsHovered;
            _buttons[i].IsHovered = _buttons[i].Contains(e.X, e.Y);
            if (_buttons[i].IsHovered != wasHovered)
            {
                _buttonsDirty = true;
            }
        }

        if (_isRightDragging)
        {
            int dx = e.X - _rightDragLastX;
            int dy = e.Y - _rightDragLastY;
            _rightDragLastX = e.X;
            _rightDragLastY = e.Y;
            _camera.PanScreen(dx, dy);
            // Camera clamp removed — pan and zoom are fully free.
            return;
        }

        if (_isDrawingFood)
        {
            PaintFoodAtMouse(e.X, e.Y);
        }
    }

    private void OnSkMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _isRightDragging = false;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            _isDrawingFood = false;
        }
    }

    private void OnSkMouseWheel(object? sender, MouseEventArgs e)
    {
        // WheelDelta is 120 per notch on most mice. Positive = towards
        // user (zoom in). Use compound factors so repeated notches
        // scale smoothly.
        int steps = e.Delta / 120;
        if (steps == 0 && e.Delta != 0)
        {
            steps = e.Delta > 0 ? 1 : -1;
        }
        if (steps == 0)
        {
            return;
        }

        float factor = 1f;
        if (steps > 0)
        {
            for (int i = 0; i < steps; i++)
            {
                factor *= Camera.ZoomWheelStep;
            }
        }
        else
        {
            for (int i = 0; i < -steps; i++)
            {
                factor /= Camera.ZoomWheelStep;
            }
        }

        _camera.ZoomAt(e.X, e.Y, factor);
        // Camera clamp removed — pan and zoom are fully free.
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape)
        {
            CancelPlacing();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Space)
        {
            TogglePause();
            e.Handled = true;
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.A:
            case Keys.Left:
                _keyPanLeft = true;
                e.Handled = true;
                break;
            case Keys.D:
            case Keys.Right:
                _keyPanRight = true;
                e.Handled = true;
                break;
            case Keys.W:
            case Keys.Up:
                _keyPanUp = true;
                e.Handled = true;
                break;
            case Keys.S:
            case Keys.Down:
                _keyPanDown = true;
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        switch (e.KeyCode)
        {
            case Keys.A:
            case Keys.Left:
                _keyPanLeft = false;
                break;
            case Keys.D:
            case Keys.Right:
                _keyPanRight = false;
                break;
            case Keys.W:
            case Keys.Up:
                _keyPanUp = false;
                break;
            case Keys.S:
            case Keys.Down:
                _keyPanDown = false;
                break;
        }
    }
    private void ApplyKeyboardPan()
    {
        long nowTicks = Stopwatch.GetTimestamp();
        float dt = (float)((nowTicks - _lastPanTicks) / (double)Stopwatch.Frequency);
        _lastPanTicks = nowTicks;
        if (dt <= 0f || dt > 0.25f)
        {
            return;
        }

        float dx = 0f;
        float dy = 0f;
        if (_keyPanLeft) dx += 1f;
        if (_keyPanRight) dx -= 1f;
        if (_keyPanUp) dy += 1f;
        if (_keyPanDown) dy -= 1f;
        if (dx == 0f && dy == 0f)
        {
            return;
        }

        float step = KeyboardPanPxPerSecond * dt;
        _camera.PanScreen(dx * step, dy * step);
        // Camera clamp removed — pan and zoom are fully free.
    }

    // Convert a screen-space pixel to a world cell, undoing the
    // camera transform first.
    private void ScreenToCell(int screenX, int screenY, out int cellX, out int cellY)
    {
        _camera.ScreenToWorld(screenX, screenY, out float wx, out float wy);
        cellX = (int)Math.Floor(wx / CellSize);
        cellY = (int)Math.Floor(wy / CellSize);
    }

    private void PaintFoodAtMouse(int pixelX, int pixelY)
    {
        ScreenToCell(pixelX, pixelY, out int cellX, out int cellY);

        if (cellX < 0 || cellX >= _world.Width)
        {
            return;
        }
        if (cellY < 0 || cellY >= _world.Height)
        {
            return;
        }

        _world.SetCell(cellX, cellY, CellType.Food);
    }

    private bool NestFitsInWorld(int centerCellX, int centerCellY)
    {
        if (centerCellX - World.NestRadius < 0)
        {
            return false;
        }
        if (centerCellX + World.NestRadius >= _world.Width)
        {
            return false;
        }
        if (centerCellY - World.NestRadius < 0)
        {
            return false;
        }
        if (centerCellY + World.NestRadius >= _world.Height)
        {
            return false;
        }
        return true;
    }

    private static SKColor ToSkColor(Color color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static void UpdateStageEma(ref double stored, double sample)
    {
        stored = stored * (1.0 - StageEmaAlpha) + sample * StageEmaAlpha;
    }

    private void DrawAnts(SKCanvas canvas, Colony colony)
    {
        List<Ant> antsList = colony.AntsList;
        int antCount = antsList.Count;
        if (antCount == 0)
        {
            return;
        }

        SKColor colonyColor = colony.CachedSkColor;
        EnsureAntBuffersCapacity(antCount);

        Span<Ant> antsSpan = CollectionsMarshal.AsSpan(antsList);
        SKRect[] frameRects = AntRenderer.FrameSpriteRects;
        SKRect[] frameRectsFood = AntRenderer.FrameSpriteRectsWithFood;
        SKRect[] bodySprites = _antBodySprites;
        SKRotationScaleMatrix[] bodyTransforms = _antBodyTransforms;

        const float invSup = AntRenderer.AtlasInverseSupersample;
        const float anchor = AntRenderer.AtlasAnchor;

        // We draw inside the camera transform, so ant positions are
        // emitted in world-pixel coordinates and the canvas scale
        // takes care of the zoom.
        for (int i = 0; i < antCount; i++)
        {
            Ant ant = antsSpan[i];
            float centerX = ant.X * CellSize;
            float centerY = ant.Y * CellSize;
            float heading = ant.Heading;
            float headingCos = (float)Math.Cos(heading);
            float headingSin = (float)Math.Sin(heading);
            float scale = invSup * ant.Role.VisualScale;
            float scos = headingCos * scale;
            float ssin = headingSin * scale;
            float tx = centerX - scos * anchor + ssin * anchor;
            float ty = centerY - ssin * anchor - scos * anchor;
            bodyTransforms[i] = new SKRotationScaleMatrix(scos, ssin, tx, ty);

            int frameIndex = AntRenderer.GetFrameIndex(ant.StridePhase);
            bool hasFood = ant.CarryingFood > 0;
            bodySprites[i] = hasFood ? frameRectsFood[frameIndex] : frameRects[frameIndex];
        }

        ApplyBodyTint(colonyColor);
        canvas.DrawAtlas(AntRenderer.BodyAtlasImage, _antBodySprites, _antBodyTransforms, _antPaint);
    }

    private void DrawAntDots(SKCanvas canvas, Colony colony)
    {
        List<Ant> antsList = colony.AntsList;
        int antCount = antsList.Count;
        if (antCount == 0) return;

        // Resize buffer to exact ant count. DrawPoints uses the
        // FULL array, so the length must match exactly. Array only
        // reallocates when the count actually changes.
        if (_antDotPoints.Length != antCount)
        {
            _antDotPoints = new SKPoint[antCount];
        }

        // Fill the points buffer with ant world-pixel positions.
        Span<Ant> antsSpan = CollectionsMarshal.AsSpan(antsList);
        for (int i = 0; i < antCount; i++)
        {
            _antDotPoints[i] = new SKPoint(antsSpan[i].X * CellSize, antsSpan[i].Y * CellSize);
        }

        // Round stroke cap = circles, not squares. Diameter scales
        // with CellSize so dots stay visible at extreme zoom-out.
        float dotDiameter = Math.Max(3f, CellSize * 0.6f);

        _fillPaint.Color = colony.CachedSkColor;
        _fillPaint.StrokeCap = SKStrokeCap.Round;
        _fillPaint.StrokeWidth = dotDiameter;
        canvas.DrawPoints(SKPointMode.Points, _antDotPoints, _fillPaint);
        // Restore default stroke cap so other code isn't affected.
        _fillPaint.StrokeCap = SKStrokeCap.Butt;
    }

    private void ApplyBodyTint(SKColor colonyColor)
    {
        uint packedColor = (uint)colonyColor;
        if (_antBodyColorFilter != null && _antBodyColorFilterCachedColor == packedColor)
        {
            return;
        }
        _antBodyColorFilter?.Dispose();

        float colR = colonyColor.Red / 255f;
        float colG = colonyColor.Green / 255f;
        float colB = colonyColor.Blue / 255f;

        float[] matrix = new float[]
        {
            FoodGreenR, colR - FoodGreenR, 0f, 0f, 0f,
            FoodGreenG, colG - FoodGreenG, 0f, 0f, 0f,
            FoodGreenB, colB - FoodGreenB, 0f, 0f, 0f,
            0f,         0f,                0f, 1f, 0f
        };

        _antBodyColorFilter = SKColorFilter.CreateColorMatrix(matrix);
        _antBodyColorFilterCachedColor = packedColor;
        _antPaint.ColorFilter = _antBodyColorFilter;
    }

    private void EnsureAntBuffersCapacity(int antCount)
    {
        if (_antBodyTransforms.Length != antCount)
        {
            _antBodyTransforms = new SKRotationScaleMatrix[antCount];
        }
        if (_antBodySprites.Length != antCount)
        {
            _antBodySprites = new SKRect[antCount];
        }
    }


    private void DrawPheromoneOverlay(SKCanvas canvas)
    {
        IReadOnlyList<Colony> colonies = _world.Colonies;
        int colonyCount = colonies.Count;
        int worldWidth = _world.Width;
        int worldHeight = _world.Height;

        // Viewport culling.
        _camera.ScreenToWorld(0, 0, out float tlx, out float tly);
        _camera.ScreenToWorld(ClientSize.Width, ClientSize.Height, out float brx, out float bry);
        int minCX = Math.Max(0, (int)Math.Floor(tlx / CellSize) - 1);
        int minCY = Math.Max(0, (int)Math.Floor(tly / CellSize) - 1);
        int maxCX = Math.Min(worldWidth - 1, (int)Math.Ceiling(brx / CellSize) + 1);
        int maxCY = Math.Min(worldHeight - 1, (int)Math.Ceiling(bry / CellSize) + 1);

        for (int x = minCX; x <= maxCX; x++)
        {
            for (int y = minCY; y <= maxCY; y++)
            {
                float Home = 0f;
                float Food = 0f;
                float Enemy = 0f;
                byte homeR = 0;
                byte homeG = 0;
                byte homeB = 0;

                for (int c = 0; c < colonyCount; c++)
                {
                    Colony colony = colonies[c];
                    PheromoneGrid grid = colony.PheromoneGrid;

                    float h = grid.Get(PheromoneChannel.HomeTrail, x, y);
                    if (h > Home)
                    {
                        Home = h;
                        homeR = colony.CachedSkColor.Red;
                        homeG = colony.CachedSkColor.Green;
                        homeB = colony.CachedSkColor.Blue;
                    }

                    float f = grid.Get(PheromoneChannel.FoodTrail, x, y);
                    if (f > Food)
                    {
                        Food = f;
                    }

                    float e = grid.Get(PheromoneChannel.EnemyTrail, x, y);
                    if (e > Enemy)
                    {
                        Enemy = e;
                    }
                }

                float pixelX = x * CellSize;
                float pixelY = y * CellSize;

                if (Home > PheromoneOverlayCutoff)
                {
                    byte alpha = ScaleIntensityToAlpha(Home);
                    _fillPaint.Color = new SKColor(homeR, homeG, homeB, alpha);
                    canvas.DrawRect(pixelX, pixelY, CellSize, CellSize, _fillPaint);
                }

                if (Food > PheromoneOverlayCutoff)
                {
                    byte alpha = ScaleIntensityToAlpha(Food);
                    _fillPaint.Color = new SKColor(FoodPheromoneColor.Red, FoodPheromoneColor.Green, FoodPheromoneColor.Blue, alpha);
                    canvas.DrawRect(pixelX, pixelY, CellSize, CellSize, _fillPaint);
                }

                // EnemyTrail exists but is not visualized — too noisy.
            }
        }
    }

    private static byte ScaleIntensityToAlpha(float intensity)
    {
        int raw = (int)(intensity * PheromoneOverlayMaxAlpha);
        if (raw > PheromoneOverlayMaxAlpha)
        {
            raw = (int)PheromoneOverlayMaxAlpha;
        }
        return (byte)raw;
    }

    private void DrawStatsPanel(SKCanvas canvas)
    {
        IReadOnlyList<Colony> colonies = _world.Colonies;
        IReadOnlyList<Colony> dead = _world.DeadColonies;
        int aliveCount = colonies.Count;
        int deadCount = dead.Count;
        if (aliveCount + deadCount == 0)
        {
            return;
        }

        int panelX = ClientSize.Width - StatsPanelWidth - 12;
        int panelY = UiTopBar.BarHeight + 8;
        int row = 0;

        for (int i = 0; i < aliveCount; i++)
        {
            Colony colony = colonies[i];
            int cardY = panelY + row * (StatsCardHeight + StatsCardSpacing);
            DrawStatsCard(canvas, colony, panelX, cardY, false);
            row++;
        }

        for (int i = 0; i < deadCount; i++)
        {
            Colony colony = dead[i];
            int cardY = panelY + row * (StatsCardHeight + StatsCardSpacing);
            DrawStatsCard(canvas, colony, panelX, cardY, true);
            row++;
        }
    }

    private void DrawStatsCard(SKCanvas canvas, Colony colony, int x, int y, bool isDead)
    {
        SKColor colonyColor = colony.CachedSkColor;

        // --- card background: rounded rect with subtle border ---
        _fillPaint.Color = UiTheme.BgPanel;
        _borderPaint.Color = UiTheme.BorderSubtle;
        _borderPaint.StrokeWidth = UiTheme.BorderThin;
        UiPanel.DrawWithBorder(canvas, _fillPaint, _borderPaint, x, y, StatsPanelWidth, StatsCardHeight, UiTheme.CornerMedium);

        // --- colony color accent bar at top (4px tall) ---
        float healthFraction = colony.NestHealthFraction;
        if (healthFraction < 0f) healthFraction = 0f;
        if (healthFraction > 1f) healthFraction = 1f;
        float barWidth = (StatsPanelWidth - 2f) * healthFraction;
        if (barWidth > 0f)
        {
            _fillPaint.Color = colonyColor;
            canvas.DrawRect(x + 1f, y + 1f, barWidth, 6f, _fillPaint);
        }

        float pad = StatsCardPadding;
        float textX = x + pad;

        // Health bar ends at y+7. Leave clear gap before text.
        // With textSize 14 the ascent is ~-12, so baseline at y+24
        // puts the top of the text at y+12 — well below the bar.
        float curY = y + 24f;

        // --- header: "Ants ###  Food ###" in bold text ---
        _textPaint.Color = UiTheme.TextStrong;
        string headerLine = "Ants " + colony.Ants.Count + "   Food " + colony.NestFood;
        canvas.DrawText(headerLine, textX, curY, _textPaint);
        curY += 10f;

        // --- population + food graph ---
        float graphW = StatsPanelWidth - pad * 2f;
        float graphH = StatsGraphHeight + 8f;
        _fillPaint.Color = UiTheme.BgPanelAlt;
        canvas.DrawRect(textX, curY, graphW, graphH, _fillPaint);
        DrawPopulationGraph(canvas, colony, textX + 2f, curY + 2f, graphW - 4f, graphH - 4f);
        curY += graphH + 6f;

        // --- role breakdown ---
        _textPaint.Color = UiTheme.TextBody;
        DrawRoleBreakdown(canvas, colony, textX, curY);
        curY += StatsLineHeight * 4f + 4f;

        // --- queen intent ---
        DrawQueenIntent(canvas, colony, textX, curY);

        // --- dead overlay ---
        if (isDead)
        {
            _fillPaint.Color = new SKColor(0, 0, 0, 160);
            UiPanel.Draw(canvas, _fillPaint, x, y, StatsPanelWidth, StatsCardHeight, UiTheme.CornerMedium);

            float elapsed = _world.SimulationTime - colony.DeathTime;
            if (elapsed < 0f) elapsed = 0f;
            string label = "DEAD - " + colony.DeathReason + " (" + ((int)elapsed) + "s ago)";
            _textPaint.Color = UiTheme.TextStrong;
            float tw = _textPaint.MeasureText(label);
            canvas.DrawText(label, x + (StatsPanelWidth - tw) / 2f, y + StatsCardHeight / 2f - _textMetrics.Ascent / 2f, _textPaint);
        }
    }

    private void DrawRoleBreakdown(SKCanvas canvas, Colony colony, float x, float y)
    {
        int total = colony.Ants.Count;
        if (total < 1) total = 1;
        DrawRoleRow(canvas, "Scouts:", colony.ScoutCount, total, ScoutBarColor, x, y);
        DrawRoleRow(canvas, "Foragers:", colony.ForagerCount, total, ForagerBarColor, x, y + StatsLineHeight);
        DrawRoleRow(canvas, "Defenders:", colony.DefenderCount, total, DefenderBarColor, x, y + StatsLineHeight * 2);
        DrawRoleRow(canvas, "Attackers:", colony.AttackerCount, total, AttackerBarColor, x, y + StatsLineHeight * 3);
    }

    private void DrawRoleRow(SKCanvas canvas, string label, int count, int total, SKColor barColor, float x, float y)
    {
        _textPaint.Color = UiTheme.TextBody;
        canvas.DrawText(label, x, y + 11, _textPaint);

        string countStr = count.ToString();
        float labelW = _textPaint.MeasureText(label);
        _textPaint.Color = UiTheme.TextStrong;
        canvas.DrawText(countStr, x + labelW + 6f, y + 11, _textPaint);

        float barX = x + 120f;
        float barY = y + 3f;
        float barH = StatsRoleBarHeight + 2f;

        _fillPaint.Color = UiTheme.BgPanelHover;
        canvas.DrawRect(barX, barY, StatsRoleBarWidth, barH, _fillPaint);

        float fraction = (float)count / (float)total;
        float filledW = fraction * StatsRoleBarWidth;
        if (filledW > 1f)
        {
            _fillPaint.Color = barColor;
            canvas.DrawRect(barX, barY, filledW, barH, _fillPaint);
        }
    }

    private void DrawQueenIntent(SKCanvas canvas, Colony colony, float x, float y)
    {
        QueenIntent intent = colony.RoleQuota.GetCurrentIntent(colony);
        _textPaint.Color = UiTheme.TextMuted;
        canvas.DrawText("Queen: " + intent.Plan, x, y + 11, _textPaint);

        float defenseY = y + StatsLineHeight;
        DrawSignalBar(canvas, "Defense:", colony.Defense, DefenseBarColor, x, defenseY);

        float offenseY = defenseY + StatsLineHeight;
        DrawSignalBar(canvas, "Offense:", colony.Offense, OffenseBarColor, x, offenseY);
    }

    private void DrawSignalBar(SKCanvas canvas, string label, float value, SKColor barColor, float x, float y)
    {
        _textPaint.Color = UiTheme.TextMuted;
        canvas.DrawText(label, x, y + 11, _textPaint);

        float barX = x + 60f;
        float barY = y + 3f;
        float barW = StatsRoleBarWidth + 34f;
        float barH = StatsRoleBarHeight + 2f;

        _fillPaint.Color = UiTheme.BgPanelHover;
        canvas.DrawRect(barX, barY, barW, barH, _fillPaint);

        float filled = value * barW;
        if (filled > 1f)
        {
            _fillPaint.Color = barColor;
            canvas.DrawRect(barX, barY, filled, barH, _fillPaint);
        }
    }

    private void DrawPopulationGraph(SKCanvas canvas, Colony colony, float x, float y, float width, float height)
    {
        ColonyStats stats = colony.Stats;
        int samples = stats.ValidSamples;
        if (samples < 2) return;

        int maxPopulation = stats.GetMaxPopulation();
        if (maxPopulation < 1) maxPopulation = 1;

        using SKPath fillPath = new SKPath();
        using SKPath linePath = new SKPath();

        float stepX = width / (float)(ColonyStats.SampleCount - 1);
        float bottom = y + height;

        for (int i = 0; i < samples; i++)
        {
            float px = x + i * stepX;
            int pop = stats.GetPopulationAt(i);
            float py = bottom - (pop / (float)maxPopulation) * height;
            if (i == 0)
            {
                fillPath.MoveTo(px, bottom);
                fillPath.LineTo(px, py);
                linePath.MoveTo(px, py);
            }
            else
            {
                fillPath.LineTo(px, py);
                linePath.LineTo(px, py);
            }
        }

        float lastX = x + (samples - 1) * stepX;
        fillPath.LineTo(lastX, bottom);
        fillPath.Close();

        SKColor colonyColor = colony.CachedSkColor;
        _fillPaint.Color = new SKColor(colonyColor.Red, colonyColor.Green, colonyColor.Blue, 70);
        canvas.DrawPath(fillPath, _fillPaint);

        _strokePaint.Color = colony.CachedSkColor;
        _strokePaint.StrokeWidth = 2f;
        _strokePaint.IsAntialias = true;
        canvas.DrawPath(linePath, _strokePaint);

        // Food line in green, thinner.
        int maxFood = stats.GetMaxFood();
        if (maxFood < 1) maxFood = 1;
        using SKPath foodLinePath = new SKPath();
        for (int i = 0; i < samples; i++)
        {
            float px = x + i * stepX;
            int food = stats.GetFoodAt(i);
            float py = bottom - (food / (float)maxFood) * height;
            if (i == 0)
                foodLinePath.MoveTo(px, py);
            else
                foodLinePath.LineTo(px, py);
        }
        _strokePaint.Color = _foodSkColor;
        _strokePaint.StrokeWidth = 1.5f;
        canvas.DrawPath(foodLinePath, _strokePaint);

        _strokePaint.IsAntialias = false;
        _strokePaint.StrokeWidth = 1f;
    }

    private void DrawNest(SKCanvas canvas, SKColor color, int centerCellX, int centerCellY)
    {
        _nestPath.Reset();

        for (int dy = -World.NestRadius; dy <= World.NestRadius; dy++)
        {
            for (int dx = -World.NestRadius; dx <= World.NestRadius; dx++)
            {
                int manhattan = Math.Abs(dx) + Math.Abs(dy);
                if (manhattan > World.NestRadius)
                {
                    continue;
                }

                int cellX = centerCellX + dx;
                int cellY = centerCellY + dy;

                float pixelX = cellX * CellSize;
                float pixelY = cellY * CellSize;

                _nestPath.AddRect(new SKRect(pixelX, pixelY, pixelX + CellSize, pixelY + CellSize));
            }
        }

        _fillPaint.Color = color;
        canvas.DrawPath(_nestPath, _fillPaint);
    }

    private void OnSkPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;

        // Root background -- warm dark gray, not black.
        canvas.Clear(UiTheme.BgRoot);

        // --- Start overlay blocks all world rendering when visible. ---
        if (_startOverlay.Visible)
        {
            _startOverlay.Draw(canvas, ClientSize.Width, ClientSize.Height, _fillPaint, _borderPaint, _textPaint, _titlePaint);
            return;
        }

        // ------- world-space rendering (camera transform applied) -------
        canvas.Save();
        _camera.Apply(canvas);

        // Grid background + walls + border — single SKPicture replay.
        // Walls are batched as one SKPath inside the picture (1 draw call).
        canvas.DrawPicture(_gridPicture);

        if (_showPheromones)
        {
            DrawPheromoneOverlay(canvas);
        }

        // Food cells — cached as SKPicture, rebuilt when food changes
        // (pickup, new food placed). Per-cell alpha shows remaining amount.
        if (_world.FoodVersion != _foodPictureCachedVersion)
        {
            RecordFoodPicture();
        }
        if (_foodPicture != null)
        {
            canvas.DrawPicture(_foodPicture);
        }

        // Colony nests — cached as SKPicture, rebuilt only when
        // colonies are added/removed. Nests never move.
        IReadOnlyList<Colony> colonies = _world.Colonies;
        int colonyCount = colonies.Count;
        if (colonyCount != _nestsPictureCachedCount)
        {
            RecordNestsPicture();
        }
        if (_nestsPicture != null)
        {
            canvas.DrawPicture(_nestsPicture);
        }

        // Grid lines — drawn OVER food/nests so they're visible everywhere.
        // Separate SKPicture, only replayed when zoomed in enough (same
        // threshold as ant dots → full shapes).
        if (_gridLinesPicture != null && _camera.Zoom >= 0.5f)
        {
            canvas.DrawPicture(_gridLinesPicture);
        }

        // Ants — skip timing overhead when no ants exist.
        if (colonyCount > 0)
        {
            bool hasAnyAnts = false;
            for (int i = 0; i < colonyCount; i++)
            {
                if (colonies[i].AntsList.Count > 0) { hasAnyAnts = true; break; }
            }

            if (hasAnyAnts)
            {
                long antStartTicks = Stopwatch.GetTimestamp();
                bool drawAsDots = _camera.Zoom < 0.5f;
                for (int i = 0; i < colonyCount; i++)
                {
                    Colony colony = colonies[i];
                    if (drawAsDots)
                        DrawAntDots(canvas, colony);
                    else
                        DrawAnts(canvas, colony);
                }
                long antEndTicks = Stopwatch.GetTimestamp();
                UpdateStageEma(ref _antStageMs, TicksToMilliseconds(antEndTicks - antStartTicks));
            }
        }

        // Placement ghosts.
        if (_placingMode == PlacingMode.Colony)
        {
            ScreenToCell(_mouseX, _mouseY, out int hoverCellX, out int hoverCellY);
            if (NestFitsInWorld(hoverCellX, hoverCellY))
            {
                Color ghostBase = ColonyColors[_nextColorIndex % ColonyColors.Length];
                SKColor ghostColor = new SKColor(ghostBase.R, ghostBase.G, ghostBase.B, 140);
                DrawNest(canvas, ghostColor, hoverCellX, hoverCellY);
            }
        }
        if (_placingMode == PlacingMode.Food)
        {
            ScreenToCell(_mouseX, _mouseY, out int hoverCellX, out int hoverCellY);
            if (hoverCellX >= 0 && hoverCellX < _world.Width && hoverCellY >= 0 && hoverCellY < _world.Height)
            {
                _fillPaint.Color = _foodSkColor.WithAlpha(140);
                canvas.DrawRect(hoverCellX * CellSize, hoverCellY * CellSize, CellSize, CellSize, _fillPaint);
            }
        }

        canvas.Restore();

        // ------- screen-space UI (drawn OVER the world) -------

        // Top bar — cached as SKPicture, rebuilt only on pause/speed/resize.
        if (_topBarDirty)
        {
            RecordTopBarPicture();
        }
        if (_topBarPicture != null)
        {
            canvas.DrawPicture(_topBarPicture);
        }

        // Colony stats panel (right side) -- cached as SKPicture,
        // rebuilt every 50ms.
        if (_statsPicture != null)
        {
            canvas.DrawPicture(_statsPicture);
        }

        // Bottom toolbar buttons — cached as SKPicture, rebuilt only
        // when hover/active state changes.
        if (_buttonsDirty)
        {
            RecordButtonsPicture();
        }
        if (_buttonsPicture != null)
        {
            canvas.DrawPicture(_buttonsPicture);
        }

        // Performance HUD (top-left, below top bar).
        canvas.DrawPicture(_hudPicture);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _startOverlay?.Dispose();
            _antBodyColorFilter?.Dispose();
            _antBodyColorFilter = null;

            for (int i = _ownedDisposables.Count - 1; i >= 0; i--)
            {
                _ownedDisposables[i].Dispose();
            }
            _ownedDisposables.Clear();

            if (components != null)
            {
                components.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Handle;
        public uint Message;
        public IntPtr WParameter;
        public IntPtr LParameter;
        public uint Time;
        public Point Location;
    }

    [DllImport("user32.dll")]
    private static extern int PeekMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);

    private static bool IsApplicationIdle()
    {
        return PeekMessage(out NativeMessage _, IntPtr.Zero, 0, 0, 0) == 0;
    }
}