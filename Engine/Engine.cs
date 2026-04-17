namespace ANTS;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

public partial class Engine : Form
{
    private const int CellSize = 16;
    private const int BorderThickness = 16;
    private const int ButtonHeight = 32;
    private const int ButtonWidth = 140;
    private const int ButtonPadding = 8;
    private const float KeyboardPanPxPerSecond = 900f;
    private const float FitMarginPx = 40f;
    private const float FoodGreenR = 34f / 255f;
    private const float FoodGreenG = 197f / 255f;
    private const float FoodGreenB = 94f / 255f;

    private const float PheromoneOverlayCutoff = 0.03f;
    private const float PheromoneOverlayMaxAlpha = 160f;
    private const int StatsPanelWidth = 260;
    private const int StatsCardHeight = 230;
    private const int StatsCardSpacing = 8;
    private const int StatsCardPadding = 10;
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
    private Ant? _selectedAnt;
    private Colony? _selectedAntColony;
    private bool _followSelectedAnt;
    private bool _isRightDragging;
    private int _rightDragLastX;
    private int _rightDragLastY;
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
    private SKPicture? _gridLinesPicture;
    private SKPicture _hudPicture = null!;
    private SKPicture _statsPicture = null!;
    private SKPicture _buttonsPicture = null!;
    private bool _buttonsDirty = true;
    private SKPicture? _foodPicture;
    private int _foodPictureCachedVersion = -1;
    private SKPicture? _topBarPicture;
    private bool _topBarDirty = true;
    private SKPicture? _nestsPicture;
    private int _nestsPictureCachedCount = -1;
    private UiTopBar _topBar = null!;
    private UiStartOverlay _startOverlay = null!;
    private SKPaint _titlePaint = null!;
    private SKPaint _borderPaint = null!;
    private SKPaint _smallTextPaint = null!;
    private SKPath _nestPath = null!;
    private SKPath _foodPath = null!;
    private SKRect[] _antBodySprites = Array.Empty<SKRect>();
    private SKRotationScaleMatrix[] _antBodyTransforms = Array.Empty<SKRotationScaleMatrix>();
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
        KeyPreview = true;

        _topBar = new UiTopBar(SpeedChoices, () => _paused, () => _speedMultiplier, TogglePause, SetSpeed);
        _startOverlay = new UiStartOverlay(OnStartOverlayPick);
        string mapsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Maps");
        _startOverlay.Scan(mapsDir, 180);

        InitializeWorld();
        RecalculateLayout();
        _camera.FitWorld(_world.Width * CellSize, _world.Height * CellSize, ClientSize.Width, ClientSize.Height, FitMarginPx);
        _lastPanTicks = Stopwatch.GetTimestamp();

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
        MapDefinition map = LoadFirstMapOrDemo();

        _world = new World(map.Width, map.Height);
        _world.ApplyMapLayout(map);

        int seedCount = map.ColonySeeds.Count;
        for (int i = 0; i < seedCount; i++)
        {
            ColonySeed seed = map.ColonySeeds[i];
            _world.AddColony(seed.X, seed.Y, seed.Color);
        }
        _nextColorIndex = seedCount;

        RecordGridPicture();
    }

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
                }
            }
        }
        return DemoMap.Build();
    }

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
        const int Margin = 16;
        SKRect cullRect = new SKRect(-Margin, -Margin, gridWidth + Margin, gridHeight + Margin);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);
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

        using (SKPaint worldBgPaint = new SKPaint())
        {
            worldBgPaint.Style = SKPaintStyle.Fill;
            worldBgPaint.IsAntialias = false;
            worldBgPaint.Color = UiTheme.BgWorld;
            recordingCanvas.DrawRect(0, 0, gridWidth, gridHeight, worldBgPaint);
        }

        {
            SKPictureRecorder gridLinesRecorder = new SKPictureRecorder();
            SKCanvas glCanvas = gridLinesRecorder.BeginRecording(cullRect);

            using (SKPaint linePaint = new SKPaint())
            {
                linePaint.Style = SKPaintStyle.Stroke;
                linePaint.IsAntialias = false;
                linePaint.Color = UiTheme.GridLine;
                linePaint.StrokeWidth = 0;

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

        RebuildButtons();
    }

    private void RebuildButtons()
    {
        _buttons.Clear();

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

        _topBar.Layout(ClientSize.Width);
        _topBar.CacheTextPositions(_textPaint, _textMetrics, _textHeight);

        _startOverlay.Layout(ClientSize.Width, ClientSize.Height);
    }

    private static string FormatSpeedLabel(double speed)
    {
        if (Math.Abs(speed - Math.Round(speed)) < 0.001)
        {
            return ((int)Math.Round(speed)).ToString(CultureInfo.InvariantCulture) + "x";
        }
        return speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
    }

    private void TogglePause()
    {
        _paused = !_paused;
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

            float valX = baseX + 50f;

            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("FPS", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextStrong;
            recordingCanvas.DrawText(_fps.ToString(CultureInfo.InvariantCulture), valX, baseY, hudText);

            baseY += lineStep;
            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("Frame", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextBody;
            recordingCanvas.DrawText(_lastFrameMs.ToString("F2", CultureInfo.InvariantCulture) + " ms", valX, baseY, hudText);

            baseY += lineStep;
            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("Sim", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextBody;
            recordingCanvas.DrawText(_simStageMs.ToString("F2", CultureInfo.InvariantCulture) + " ms", valX, baseY, hudText);

            baseY += lineStep;
            hudText.Color = UiTheme.TextMuted;
            recordingCanvas.DrawText("Ants", baseX, baseY, hudText);
            hudText.Color = UiTheme.TextBody;
            recordingCanvas.DrawText(_antStageMs.ToString("F3", CultureInfo.InvariantCulture) + " ms", valX, baseY, hudText);
        }

        Replace(ref _hudPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    private void RecordButtonsPicture()
    {
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

        SKRect cullRect = new SKRect(0, 0, ClientSize.Width + 20, ClientSize.Height + 20);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        DrawStatsPanel(rc);

        Replace(ref _statsPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

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
                int step = (int)(amount * 10f);
                if (step > 4) step = 4;
                byte alpha = (byte)(step * 64);
                if (alpha < 64) alpha = 64;
                if (step == 4) alpha = 255;
                foodPaint.Color = _foodSkColor.WithAlpha(alpha);
                rc.DrawRect(cellX * CellSize, cellY * CellSize, CellSize, CellSize, foodPaint);
            }
        }

        Replace(ref _foodPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    private void RecordTopBarPicture()
    {
        int w = ClientSize.Width;
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
            return;
        }

        TrySelectAntAt(e.X, e.Y);
    }

    private void OnSkMouseMove(object? sender, MouseEventArgs e)
    {
        _mouseX = e.X;
        _mouseY = e.Y;

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
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape)
        {
            if (_selectedAnt != null)
            {
                _selectedAnt = null;
                _selectedAntColony = null;
                _followSelectedAnt = false;
            }
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
    }

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

        for (int i = 0; i < antCount; i++)
        {
            Ant ant = antsSpan[i];
            float centerX = ant.X * CellSize;
            float centerY = ant.Y * CellSize;

            if (ant.LungeTimer > 0f)
            {
                float t = ant.LungeTimer / CombatSystem.LungeDuration;
                float offset = t > 0.5f ? (1f - t) * 2f : t * 2f;
                float lungePixels = offset * CombatSystem.LungeDistance * CellSize;
                centerX += ant.LungeDirX * lungePixels;
                centerY += ant.LungeDirY * lungePixels;
            }

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

        if (_antDotPoints.Length != antCount)
        {
            _antDotPoints = new SKPoint[antCount];
        }

        Span<Ant> antsSpan = CollectionsMarshal.AsSpan(antsList);
        for (int i = 0; i < antCount; i++)
        {
            _antDotPoints[i] = new SKPoint(antsSpan[i].X * CellSize, antsSpan[i].Y * CellSize);
        }

        float dotDiameter = Math.Max(3f, CellSize * 0.6f);

        _fillPaint.Color = colony.CachedSkColor;
        _fillPaint.StrokeCap = SKStrokeCap.Round;
        _fillPaint.StrokeWidth = dotDiameter;
        canvas.DrawPoints(SKPointMode.Points, _antDotPoints, _fillPaint);
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
        _fillPaint.Color = UiTheme.BgPanel;
        _borderPaint.Color = UiTheme.BorderSubtle;
        _borderPaint.StrokeWidth = UiTheme.BorderThin;
        UiPanel.DrawWithBorder(canvas, _fillPaint, _borderPaint, x, y, StatsPanelWidth, StatsCardHeight, UiTheme.CornerMedium);
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
        float curY = y + 24f;

        _textPaint.Color = UiTheme.TextStrong;
        string headerLine = "Ants " + colony.Ants.Count + "   Food " + colony.NestFood;
        canvas.DrawText(headerLine, textX, curY, _textPaint);
        curY += 10f;

        float graphW = StatsPanelWidth - pad * 2f;
        float graphH = StatsGraphHeight + 8f;
        _fillPaint.Color = UiTheme.BgPanelAlt;
        canvas.DrawRect(textX, curY, graphW, graphH, _fillPaint);
        DrawPopulationGraph(canvas, colony, textX + 2f, curY + 2f, graphW - 4f, graphH - 4f);
        curY += graphH + 6f;

        _textPaint.Color = UiTheme.TextBody;
        DrawRoleBreakdown(canvas, colony, textX, curY);
        curY += StatsLineHeight * 4f + 4f;

        DrawQueenIntent(canvas, colony, textX, curY);

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

        string countStr = count.ToString(CultureInfo.InvariantCulture);
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
        canvas.Clear(UiTheme.BgRoot);

        if (_startOverlay.Visible)
        {
            _startOverlay.Draw(canvas, ClientSize.Width, ClientSize.Height, _fillPaint, _borderPaint, _textPaint, _titlePaint);
            return;
        }

        canvas.Save();
        _camera.Apply(canvas);
        canvas.DrawPicture(_gridPicture);

        if (_showPheromones)
        {
            DrawPheromoneOverlay(canvas);
        }

        if (_world.FoodVersion != _foodPictureCachedVersion)
        {
            RecordFoodPicture();
        }
        if (_foodPicture != null)
        {
            canvas.DrawPicture(_foodPicture);
        }

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

        if (_gridLinesPicture != null && _camera.Zoom >= 0.5f)
        {
            canvas.DrawPicture(_gridLinesPicture);
        }

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

        DrawSelectedAntOverlay(canvas);
        canvas.Restore();
        DrawSelectedAntInfoPanel(canvas);

        if (_topBarDirty)
        {
            RecordTopBarPicture();
        }
        if (_topBarPicture != null)
        {
            canvas.DrawPicture(_topBarPicture);
        }

        if (_statsPicture != null)
        {
            canvas.DrawPicture(_statsPicture);
        }

        if (_buttonsDirty)
        {
            RecordButtonsPicture();
        }
        if (_buttonsPicture != null)
        {
            canvas.DrawPicture(_buttonsPicture);
        }

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

    private const float AntClickRadiusCells = 1.5f;

    private void TrySelectAntAt(int screenX, int screenY)
    {
        _camera.ScreenToWorld(screenX, screenY, out float wx, out float wy);
        float worldX = wx / CellSize;
        float worldY = wy / CellSize;

        Ant? bestAnt = null;
        Colony? bestColony = null;
        float bestDistSq = AntClickRadiusCells * AntClickRadiusCells;

        IReadOnlyList<Colony> colonies = _world.Colonies;
        int colonyCount = colonies.Count;
        for (int c = 0; c < colonyCount; c++)
        {
            Colony colony = colonies[c];
            IReadOnlyList<Ant> ants = colony.Ants;
            int antCount = ants.Count;
            for (int a = 0; a < antCount; a++)
            {
                Ant ant = ants[a];
                if (ant.IsDead) continue;
                float dx = ant.X - worldX;
                float dy = ant.Y - worldY;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestAnt = ant;
                    bestColony = colony;
                }
            }
        }

        _selectedAnt = bestAnt;
        _selectedAntColony = bestColony;
        _followSelectedAnt = bestAnt != null;
    }

    private void DrawSelectedAntOverlay(SKCanvas canvas)
    {
        Ant? ant = _selectedAnt;
        Colony? colony = _selectedAntColony;
        if (ant == null || colony == null || ant.IsDead)
        {
            _selectedAnt = null;
            _selectedAntColony = null;
            return;
        }

        float cx = ant.X * CellSize;
        float cy = ant.Y * CellSize;

        if (_followSelectedAnt)
        {
            _camera.ScreenToWorld(ClientSize.Width / 2, ClientSize.Height / 2, out float camCx, out float camCy);
            float targetX = cx;
            float targetY = cy;
            float lerpedX = camCx + (targetX - camCx) * 0.08f;
            float lerpedY = camCy + (targetY - camCy) * 0.08f;
            float diffX = lerpedX - camCx;
            float diffY = lerpedY - camCy;
            _camera.OffsetX -= diffX * _camera.Zoom;
            _camera.OffsetY -= diffY * _camera.Zoom;
        }

        AntRole role = ant.Role;
        SKColor colonyColor = colony.CachedSkColor;
        float heading = ant.Heading;
        _fillPaint.IsAntialias = true;
        float smellRadius = role.SensorDistance * CellSize;
        _fillPaint.Style = SKPaintStyle.Stroke;
        _fillPaint.StrokeWidth = 1.5f / _camera.Zoom;
        _fillPaint.Color = new SKColor(colonyColor.Red, colonyColor.Green, colonyColor.Blue, 70);
        canvas.DrawCircle(cx, cy, smellRadius, _fillPaint);
        _fillPaint.Style = SKPaintStyle.Fill;
        float visionDist = role.VisionRange * CellSize;
        float sensorAngle = role.SensorAngleRad;

        if (visionDist > 0)
        {
            DrawVisionCone(canvas, cx, cy, heading, sensorAngle, visionDist, colonyColor);
        }

        float arrowLen = role.VisionRange * CellSize;
        float ax = cx + (float)Math.Cos(heading) * arrowLen;
        float ay = cy + (float)Math.Sin(heading) * arrowLen;
        _fillPaint.Style = SKPaintStyle.Stroke;
        _fillPaint.StrokeWidth = 2f / _camera.Zoom;
        _fillPaint.Color = new SKColor(255, 255, 255, 200);
        canvas.DrawLine(cx, cy, ax, ay, _fillPaint);
        float arrowHeadLen = 0.8f * CellSize;
        float arrowSpread = 0.4f;
        canvas.DrawLine(ax, ay,
            ax - (float)Math.Cos(heading - arrowSpread) * arrowHeadLen,
            ay - (float)Math.Sin(heading - arrowSpread) * arrowHeadLen, _fillPaint);
        canvas.DrawLine(ax, ay,
            ax - (float)Math.Cos(heading + arrowSpread) * arrowHeadLen,
            ay - (float)Math.Sin(heading + arrowSpread) * arrowHeadLen, _fillPaint);
        _fillPaint.Style = SKPaintStyle.Fill;
        _fillPaint.Style = SKPaintStyle.Fill;
        _fillPaint.StrokeWidth = 0f;
        _fillPaint.StrokeCap = SKStrokeCap.Butt;
        _fillPaint.IsAntialias = false;
    }

    private void DrawVisionCone(SKCanvas canvas, float cx, float cy, float heading, float halfAngle, float radius, SKColor colonyColor)
    {

        float startAngle = heading - halfAngle;
        float endAngle = heading + halfAngle;
        const int arcSegments = 20;
        float angleStep = (endAngle - startAngle) / arcSegments;

        using SKPath conePath = new SKPath();
        conePath.MoveTo(cx, cy);

        for (int i = 0; i <= arcSegments; i++)
        {
            float a = startAngle + angleStep * i;
            float px = cx + (float)Math.Cos(a) * radius;
            float py = cy + (float)Math.Sin(a) * radius;
            conePath.LineTo(px, py);
        }

        conePath.Close();

        _fillPaint.Style = SKPaintStyle.Fill;
        _fillPaint.Color = new SKColor(255, 255, 255, 16);
        canvas.DrawPath(conePath, _fillPaint);
        _fillPaint.Style = SKPaintStyle.Stroke;
        _fillPaint.StrokeWidth = 1.5f / _camera.Zoom;
        _fillPaint.Color = new SKColor(255, 255, 255, 80);
        canvas.DrawPath(conePath, _fillPaint);

        _fillPaint.Style = SKPaintStyle.Fill;
    }

    private void DrawSelectedAntInfoPanel(SKCanvas canvas)
    {
        Ant? ant = _selectedAnt;
        Colony? colony = _selectedAntColony;
        if (ant == null || colony == null) return;

        float panelW = 220f;
        float panelH = 230f;
        float panelX = 8f;
        float panelY = UiTopBar.BarHeight + 8f + 84f + 8f;
        using SKPaint bgPaint = new SKPaint();
        bgPaint.Color = new SKColor(30, 30, 36, 230);
        bgPaint.IsAntialias = true;
        SKRect panelRect = new SKRect(panelX, panelY, panelX + panelW, panelY + panelH);
        canvas.DrawRoundRect(panelRect, 8, 8, bgPaint);
        bgPaint.Style = SKPaintStyle.Stroke;
        bgPaint.StrokeWidth = 2f;
        bgPaint.Color = colony.CachedSkColor;
        canvas.DrawRoundRect(panelRect, 8, 8, bgPaint);

        using SKPaint textPaint = new SKPaint();
        textPaint.IsAntialias = true;
        textPaint.Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        float lineH = 18f;
        float x = panelX + 10f;
        float valX = panelX + 130f;
        float y = panelY + 22f;
        textPaint.TextSize = 14f;
        textPaint.Color = colony.CachedSkColor;
        canvas.DrawText(ant.Role.RoleName, x, y, textPaint);
        y += lineH + 4f;
        textPaint.TextSize = 12f;
        textPaint.Color = new SKColor(200, 200, 210);

        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Goal", ant.Goal.Type.ToString());
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Health", $"{ant.Health} / {Ant.DefaultHealth}");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Food", ant.CarryingFood > 0 ? $"{ant.CarryingFood}" : "-");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Speed", $"{ant.Role.MaxSpeed:F1} c/s");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Vision", $"{ant.Role.VisionRange:F0} cells");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "VisionStr", $"{ant.VisionStrength:P0}");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Engaged", ant.EngagementTimer > 0 ? $"{ant.EngagementTimer:F2}s" : "-");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Age", $"{ant.Age:F0}s");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Autonomy", $"{ant.InternalClock:F0} / {ant.Role.AutonomyMax:F0}");

        y += 6f;
        textPaint.TextSize = 10f;
        textPaint.Color = new SKColor(255, 255, 255, 120);
        float lx = x;
        canvas.DrawText("VisionCone", lx, y, textPaint);
        lx += textPaint.MeasureText("VisionCone") + 6f;
        textPaint.Color = new SKColor(255, 255, 255, 200);
        canvas.DrawText("HeadingArrow", lx, y, textPaint);
        lx += textPaint.MeasureText("HeadingArrow") + 6f;
        textPaint.Color = new SKColor(255, 80, 80, 200);
        canvas.DrawText("SensorCircle", lx, y, textPaint);
    }

    private static void DrawInfoLine(SKCanvas canvas, SKPaint textPaint, float x, float valX, ref float y, float lineH, string label, string value)
    {
        SKColor saved = textPaint.Color;
        textPaint.Color = new SKColor(140, 140, 155);
        canvas.DrawText(label, x, y, textPaint);
        textPaint.Color = new SKColor(220, 220, 230);
        canvas.DrawText(value, valX, y, textPaint);
        textPaint.Color = saved;
        y += lineH;
    }
}