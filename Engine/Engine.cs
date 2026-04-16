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
    private const int CellSize = 16;
    private const int BorderThickness = 8;
    private const int WorldPercent = 100;
    private const int ButtonHeight = 32;
    private const int ButtonWidth = 140;
    private const int ButtonPadding = 8;

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
    private static readonly SKColor DangerPheromoneColor = new SKColor(249, 115, 22);

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
    private int _gridX;
    private int _gridY;

    private List<UiButton> _buttons = new List<UiButton>();
    private PlacingMode _placingMode = PlacingMode.None;
    private int _nextColorIndex;
    private int _mouseX;
    private int _mouseY;
    private bool _isDrawingFood;
    private bool _showPheromones;

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
    private SKPicture _buttonsPicture = null!;
    private SKPicture _hudPicture = null!;
    private SKPath _nestPath = null!;
    private SKRect[] _antBodySprites = Array.Empty<SKRect>();
    private SKRotationScaleMatrix[] _antBodyTransforms = Array.Empty<SKRotationScaleMatrix>();
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

    private SKColor _backgroundSkColor;
    private SKColor _buttonBorderSkColor;
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
        _antPaint.FilterQuality = SKFilterQuality.High;

        _textMetrics = _textPaint.FontMetrics;
        _textHeight = -_textMetrics.Ascent + _textMetrics.Descent;

        _nestPath = Own(new SKPath());

        _backgroundSkColor = new SKColor(BackColor.R, BackColor.G, BackColor.B);
        _buttonBorderSkColor = new SKColor(120, 120, 120);
        _foodSkColor = new SKColor(FoodColor.R, FoodColor.G, FoodColor.B);

        _skControl = new FastSKGLControl();
        _skControl.Dock = DockStyle.Fill;
        _skControl.PaintSurface += OnSkPaintSurface;
        _skControl.MouseDown += OnSkMouseDown;
        _skControl.MouseMove += OnSkMouseMove;
        _skControl.MouseUp += OnSkMouseUp;
        Controls.Add(_skControl);

        InitializeWorld();
        RecalculateLayout();

        _fpsStopwatch.Start();
        _hudStopwatch.Start();
        RecordHudPicture();
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
        int worldPixelWidth = ClientSize.Width * WorldPercent / 100;
        int worldPixelHeight = ClientSize.Height * WorldPercent / 100;

        int cellsX = worldPixelWidth / CellSize;
        int cellsY = worldPixelHeight / CellSize;

        _world = new World(cellsX, cellsY);

        RecordGridPicture();
    }

    private void RecordGridPicture()
    {
        int gridWidth = _world.Width * CellSize;
        int gridHeight = _world.Height * CellSize;

        int borderHalf = BorderThickness / 2;
        SKRect cullRect = new SKRect(-borderHalf, -borderHalf, gridWidth + borderHalf, gridHeight + borderHalf);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);

        using (SKPaint borderPaint = new SKPaint())
        {
            borderPaint.Style = SKPaintStyle.Stroke;
            borderPaint.IsAntialias = false;
            borderPaint.Color = new SKColor(128, 128, 128);
            borderPaint.StrokeWidth = BorderThickness;

            int borderX = -borderHalf;
            int borderY = -borderHalf;
            int borderWidth = gridWidth + BorderThickness;
            int borderHeight = gridHeight + BorderThickness;
            recordingCanvas.DrawRect(borderX, borderY, borderWidth, borderHeight, borderPaint);
        }

        using (SKPaint gridPaint = new SKPaint())
        {
            gridPaint.Style = SKPaintStyle.Stroke;
            gridPaint.IsAntialias = false;
            gridPaint.Color = new SKColor(45, 45, 45);
            gridPaint.StrokeWidth = 1;

            using (SKPath gridPath = new SKPath())
            {
                for (int x = 0; x <= _world.Width; x++)
                {
                    float lineX = x * CellSize + 0.5f;
                    gridPath.MoveTo(lineX, 0);
                    gridPath.LineTo(lineX, gridHeight);
                }

                for (int y = 0; y <= _world.Height; y++)
                {
                    float lineY = y * CellSize + 0.5f;
                    gridPath.MoveTo(0, lineY);
                    gridPath.LineTo(gridWidth, lineY);
                }

                recordingCanvas.DrawPath(gridPath, gridPaint);
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
        _simAccumulatorTicks += simDeltaTicks;
        while (_simAccumulatorTicks >= _ticksPerSimStep)
        {
            _world.Update();
            _simAccumulatorTicks -= _ticksPerSimStep;
        }
        long simEndTicks = Stopwatch.GetTimestamp();
        UpdateStageEma(ref _simStageMs, TicksToMilliseconds(simEndTicks - simStartTicks));

        if (_hudStopwatch.ElapsedMilliseconds >= HudUpdateIntervalMs)
        {
            RecordHudPicture();
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

        int gridWidth = _world.Width * CellSize;
        int gridHeight = _world.Height * CellSize;

        _gridX = (ClientSize.Width - gridWidth) / 2;
        _gridY = (ClientSize.Height - gridHeight) / 2;

        RebuildButtons();
    }

    private void RebuildButtons()
    {
        _buttons.Clear();

        int buttonY = ClientSize.Height - ButtonHeight - ButtonPadding;
        int addColonyX = ButtonPadding;
        int addFoodX = addColonyX + ButtonWidth + ButtonPadding;

        Color buttonBackground = Color.FromArgb(55, 65, 81);

        Rectangle addColonyBounds = new Rectangle(addColonyX, buttonY, ButtonWidth, ButtonHeight);
        UiButton addColonyButton = new UiButton(addColonyBounds, "Add Colony", buttonBackground, StartPlacingColony);
        CacheButtonTextPosition(addColonyButton);
        _buttons.Add(addColonyButton);

        Rectangle addFoodBounds = new Rectangle(addFoodX, buttonY, ButtonWidth, ButtonHeight);
        UiButton addFoodButton = new UiButton(addFoodBounds, "Add Food", buttonBackground, StartPlacingFood);
        CacheButtonTextPosition(addFoodButton);
        _buttons.Add(addFoodButton);

        int pheromoneX = addFoodX + ButtonWidth + ButtonPadding;
        Rectangle pheromoneBounds = new Rectangle(pheromoneX, buttonY, ButtonWidth, ButtonHeight);
        UiButton pheromoneButton = new UiButton(pheromoneBounds, "Pheromones", buttonBackground, TogglePheromones);
        CacheButtonTextPosition(pheromoneButton);
        _buttons.Add(pheromoneButton);

        RecordButtonsPicture();
    }

    private void RecordButtonsPicture()
    {
        SKRect cullRect = new SKRect(0, 0, ClientSize.Width, ClientSize.Height);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);

        _strokePaint.Color = _buttonBorderSkColor;
        _strokePaint.StrokeWidth = 1;

        int buttonCount = _buttons.Count;
        for (int i = 0; i < buttonCount; i++)
        {
            UiButton button = _buttons[i];
            _fillPaint.Color = ToSkColor(button.BackgroundColor);
            recordingCanvas.DrawRect(button.Bounds.X, button.Bounds.Y, button.Bounds.Width, button.Bounds.Height, _fillPaint);
            recordingCanvas.DrawRect(button.Bounds.X, button.Bounds.Y, button.Bounds.Width - 1, button.Bounds.Height - 1, _strokePaint);
            recordingCanvas.DrawText(button.Label, button.TextBaselineX, button.TextBaselineY, _textPaint);
        }

        Replace(ref _buttonsPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    private void RecordHudPicture()
    {
        SKRect cullRect = new SKRect(0, 0, 300, 90);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);

        float lineStep = 18f;
        float firstBaselineY = 4f - _textMetrics.Ascent;
        float baselineY = firstBaselineY;

        recordingCanvas.DrawText("FPS:  " + _fps, 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("FrameTime:  " + _lastFrameMs.ToString("F3") + " ms", 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Sim:  " + _simStageMs.ToString("F3") + " ms", 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Ants:  " + _antStageMs.ToString("F3") + " ms", 8, baselineY, _textPaint);

        Replace(ref _hudPicture!, recorder.EndRecording());
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
            CancelPlacing();
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        foreach (UiButton button in _buttons)
        {
            if (button.Contains(e.X, e.Y))
            {
                button.OnClick();
                return;
            }
        }

        if (_placingMode == PlacingMode.Colony)
        {
            int cellX = (e.X - _gridX) / CellSize;
            int cellY = (e.Y - _gridY) / CellSize;

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

        if (_isDrawingFood)
        {
            PaintFoodAtMouse(e.X, e.Y);
        }
    }

    private void OnSkMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDrawingFood = false;
        }
    }

    private void PaintFoodAtMouse(int pixelX, int pixelY)
    {
        int cellX = (pixelX - _gridX) / CellSize;
        int cellY = (pixelY - _gridY) / CellSize;

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

        int gridOriginX = _gridX;
        int gridOriginY = _gridY;

        for (int i = 0; i < antCount; i++)
        {
            Ant ant = antsSpan[i];
            float centerX = gridOriginX + ant.X * CellSize;
            float centerY = gridOriginY + ant.Y * CellSize;
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

        for (int x = 0; x < worldWidth; x++)
        {
            for (int y = 0; y < worldHeight; y++)
            {
                float bestHome = 0f;
                float bestFood = 0f;
                float bestEnemy = 0f;
                float bestDanger = 0f;
                byte homeR = 0;
                byte homeG = 0;
                byte homeB = 0;

                for (int c = 0; c < colonyCount; c++)
                {
                    Colony colony = colonies[c];
                    PheromoneGrid grid = colony.PheromoneGrid;

                    float h = grid.Get(PheromoneChannel.HomeTrail, x, y);
                    if (h > bestHome)
                    {
                        bestHome = h;
                        homeR = colony.CachedSkColor.Red;
                        homeG = colony.CachedSkColor.Green;
                        homeB = colony.CachedSkColor.Blue;
                    }

                    float f = grid.Get(PheromoneChannel.FoodTrail, x, y);
                    if (f > bestFood)
                    {
                        bestFood = f;
                    }

                    float e = grid.Get(PheromoneChannel.EnemyTrail, x, y);
                    if (e > bestEnemy)
                    {
                        bestEnemy = e;
                    }

                    float d = grid.Get(PheromoneChannel.DangerTrail, x, y);
                    if (d > bestDanger)
                    {
                        bestDanger = d;
                    }
                }

                float pixelX = _gridX + x * CellSize;
                float pixelY = _gridY + y * CellSize;

                if (bestHome > PheromoneOverlayCutoff)
                {
                    byte alpha = ScaleIntensityToAlpha(bestHome);
                    _fillPaint.Color = new SKColor(homeR, homeG, homeB, alpha);
                    canvas.DrawRect(pixelX, pixelY, CellSize, CellSize, _fillPaint);
                }

                if (bestFood > PheromoneOverlayCutoff)
                {
                    byte alpha = ScaleIntensityToAlpha(bestFood);
                    _fillPaint.Color = new SKColor(FoodPheromoneColor.Red, FoodPheromoneColor.Green, FoodPheromoneColor.Blue, alpha);
                    canvas.DrawRect(pixelX, pixelY, CellSize, CellSize, _fillPaint);
                }

                if (bestEnemy > PheromoneOverlayCutoff)
                {
                    byte alpha = ScaleIntensityToAlpha(bestEnemy);
                    _fillPaint.Color = new SKColor(EnemyPheromoneColor.Red, EnemyPheromoneColor.Green, EnemyPheromoneColor.Blue, alpha);
                    canvas.DrawRect(pixelX, pixelY, CellSize, CellSize, _fillPaint);
                }

                if (bestDanger > PheromoneOverlayCutoff)
                {
                    byte alpha = ScaleIntensityToAlpha(bestDanger);
                    _fillPaint.Color = new SKColor(DangerPheromoneColor.Red, DangerPheromoneColor.Green, DangerPheromoneColor.Blue, alpha);
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

        int panelX = ClientSize.Width - StatsPanelWidth - ButtonPadding;
        int panelY = ButtonPadding;
        int row = 0;

        for (int i = 0; i < aliveCount; i++)
        {
            Colony colony = colonies[i];
            int cardY = panelY + row * (StatsCardHeight + StatsCardSpacing);
            DrawStatsCard(canvas, colony, panelX, cardY);
            row++;
        }

        for (int i = 0; i < deadCount; i++)
        {
            Colony colony = dead[i];
            int cardY = panelY + row * (StatsCardHeight + StatsCardSpacing);
            DrawStatsCard(canvas, colony, panelX, cardY);
            DrawDeadCardOverlay(canvas, colony, panelX, cardY);
            row++;
        }
    }

    private void DrawDeadCardOverlay(SKCanvas canvas, Colony colony, int x, int y)
    {
        _fillPaint.Color = new SKColor(0, 0, 0, 160);
        canvas.DrawRect(x, y, StatsPanelWidth, StatsCardHeight, _fillPaint);

        float elapsed = _world.SimulationTime - colony.DeathTime;
        if (elapsed < 0f)
        {
            elapsed = 0f;
        }
        string label = "DEAD - " + colony.DeathReason + " (" + ((int)elapsed) + "s ago)";
        float textWidth = _textPaint.MeasureText(label);
        float labelX = x + (StatsPanelWidth - textWidth) / 2f;
        float labelY = y + StatsCardHeight / 2f - _textMetrics.Ascent / 2f;
        canvas.DrawText(label, labelX, labelY, _textPaint);
    }

    private void DrawStatsCard(SKCanvas canvas, Colony colony, int x, int y)
    {
        _fillPaint.Color = new SKColor(30, 30, 30, 230);
        canvas.DrawRect(x, y, StatsPanelWidth, StatsCardHeight, _fillPaint);

        _fillPaint.Color = new SKColor(45, 45, 45, 220);
        canvas.DrawRect(x, y, StatsPanelWidth, StatsHeaderHeight, _fillPaint);

        float healthFraction = colony.NestHealthFraction;
        if (healthFraction < 0f)
        {
            healthFraction = 0f;
        }
        if (healthFraction > 1f)
        {
            healthFraction = 1f;
        }
        int healthWidth = (int)(StatsPanelWidth * healthFraction);
        if (healthWidth > 0)
        {
            _fillPaint.Color = colony.CachedSkColor;
            canvas.DrawRect(x, y, healthWidth, StatsHeaderHeight, _fillPaint);
        }

        int textX = x + StatsCardPadding;
        int textY = y + StatsHeaderHeight + 16;

        string headerLine = "Ants " + colony.Ants.Count + "   Food " + colony.NestFood;
        canvas.DrawText(headerLine, textX, textY, _textPaint);

        int graphX = textX;
        int graphY = textY + 6;
        int graphWidth = StatsPanelWidth - StatsCardPadding * 2;
        DrawPopulationGraph(canvas, colony, graphX, graphY, graphWidth, StatsGraphHeight);

        int rolesY = graphY + StatsGraphHeight + 12;
        DrawRoleBreakdown(canvas, colony, textX, rolesY);

        int intentY = rolesY + StatsLineHeight * 4 + 6;
        DrawQueenIntent(canvas, colony, textX, intentY);
    }

    private void DrawRoleBreakdown(SKCanvas canvas, Colony colony, int x, int y)
    {
        int total = colony.Ants.Count;
        if (total < 1)
        {
            total = 1;
        }
        DrawRoleRow(canvas, "Scouts:    ", colony.ScoutCount, total, ScoutBarColor, x, y);
        DrawRoleRow(canvas, "Foragers:  ", colony.ForagerCount, total, ForagerBarColor, x, y + StatsLineHeight);
        DrawRoleRow(canvas, "Defenders: ", colony.DefenderCount, total, DefenderBarColor, x, y + StatsLineHeight * 2);
        DrawRoleRow(canvas, "Attackers: ", colony.AttackerCount, total, AttackerBarColor, x, y + StatsLineHeight * 3);
    }

    private void DrawRoleRow(SKCanvas canvas, string label, int count, int total, SKColor barColor, int x, int y)
    {
        string text = label + count;
        canvas.DrawText(text, x, y + 11, _textPaint);

        int barX = x + 110;
        int barY = y + 4;
        _fillPaint.Color = new SKColor(55, 55, 55, 220);
        canvas.DrawRect(barX, barY, StatsRoleBarWidth, StatsRoleBarHeight, _fillPaint);

        float fraction = (float)count / (float)total;
        int filledWidth = (int)(fraction * StatsRoleBarWidth);
        if (filledWidth > 0)
        {
            _fillPaint.Color = barColor;
            canvas.DrawRect(barX, barY, filledWidth, StatsRoleBarHeight, _fillPaint);
        }
    }

    private void DrawQueenIntent(SKCanvas canvas, Colony colony, int x, int y)
    {
        QueenIntent intent = colony.RoleQuota.GetCurrentIntent(colony);
        string intentLine = "Queen: " + intent.Plan;
        canvas.DrawText(intentLine, x, y + 11, _textPaint);

        int defenseY = y + StatsLineHeight;
        DrawSignalBar(canvas, "Defense:", colony.Defense, DefenseBarColor, x, defenseY);

        int offenseY = defenseY + StatsLineHeight;
        DrawSignalBar(canvas, "Offense:", colony.Offense, OffenseBarColor, x, offenseY);
    }

    private void DrawSignalBar(SKCanvas canvas, string label, float value, SKColor barColor, int x, int y)
    {
        canvas.DrawText(label, x, y + 11, _textPaint);
        int barX = x + 56;
        int barY = y + 4;
        int barWidth = StatsRoleBarWidth + 34;
        _fillPaint.Color = new SKColor(55, 55, 55, 220);
        canvas.DrawRect(barX, barY, barWidth, StatsRoleBarHeight, _fillPaint);

        int filled = (int)(value * barWidth);
        if (filled > 0)
        {
            _fillPaint.Color = barColor;
            canvas.DrawRect(barX, barY, filled, StatsRoleBarHeight, _fillPaint);
        }
    }

    private void DrawPopulationGraph(SKCanvas canvas, Colony colony, int x, int y, int width, int height)
    {
        _fillPaint.Color = new SKColor(20, 20, 20, 200);
        canvas.DrawRect(x, y, width, height, _fillPaint);

        ColonyStats stats = colony.Stats;
        int samples = stats.ValidSamples;
        if (samples < 2)
        {
            return;
        }

        int maxPopulation = stats.GetMaxPopulation();
        if (maxPopulation < 1)
        {
            maxPopulation = 1;
        }

        using SKPath fillPath = new SKPath();
        using SKPath linePath = new SKPath();

        float stepX = (float)width / (float)(ColonyStats.SampleCount - 1);
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
        _fillPaint.Color = new SKColor(colonyColor.Red, colonyColor.Green, colonyColor.Blue, 90);
        canvas.DrawPath(fillPath, _fillPaint);

        _strokePaint.Color = colony.CachedSkColor;
        _strokePaint.StrokeWidth = 2f;
        _strokePaint.IsAntialias = true;
        canvas.DrawPath(linePath, _strokePaint);

        int maxFood = stats.GetMaxFood();
        if (maxFood < 1)
        {
            maxFood = 1;
        }
        using SKPath foodLinePath = new SKPath();
        for (int i = 0; i < samples; i++)
        {
            float px = x + i * stepX;
            int food = stats.GetFoodAt(i);
            float py = bottom - (food / (float)maxFood) * height;
            if (i == 0)
            {
                foodLinePath.MoveTo(px, py);
            }
            else
            {
                foodLinePath.LineTo(px, py);
            }
        }
        _strokePaint.Color = _foodSkColor;
        _strokePaint.StrokeWidth = 1.5f;
        canvas.DrawPath(foodLinePath, _strokePaint);

        _strokePaint.IsAntialias = false;
        _strokePaint.StrokeWidth = 1f;
        _strokePaint.Color = _buttonBorderSkColor;
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

                float pixelX = _gridX + cellX * CellSize;
                float pixelY = _gridY + cellY * CellSize;

                _nestPath.AddRect(new SKRect(pixelX, pixelY, pixelX + CellSize, pixelY + CellSize));
            }
        }

        _fillPaint.Color = color;
        canvas.DrawPath(_nestPath, _fillPaint);
    }

    private void OnSkPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;

        canvas.Clear(_backgroundSkColor);

        canvas.Save();
        canvas.Translate(_gridX, _gridY);
        canvas.DrawPicture(_gridPicture);
        canvas.Restore();

        if (_showPheromones)
        {
            DrawPheromoneOverlay(canvas);
        }

        int foodCount = _world.FoodCount;
        if (foodCount > 0)
        {
            Point[] foodCells = _world.FoodCells;
            for (int i = 0; i < foodCount; i++)
            {
                int cellX = foodCells[i].X;
                int cellY = foodCells[i].Y;
                float amount = _world.GetFoodAmount(cellX, cellY);
                float rawAlpha = amount * 255f;
                if (rawAlpha > 255f)
                {
                    rawAlpha = 255f;
                }
                byte alpha = (byte)rawAlpha;
                if (alpha < 10)
                {
                    alpha = 10;
                }
                _fillPaint.Color = _foodSkColor.WithAlpha(alpha);
                float pixelX = _gridX + cellX * CellSize;
                float pixelY = _gridY + cellY * CellSize;
                canvas.DrawRect(pixelX, pixelY, CellSize, CellSize, _fillPaint);
            }
        }

        IReadOnlyList<Colony> colonies = _world.Colonies;
        int colonyCount = colonies.Count;
        for (int i = 0; i < colonyCount; i++)
        {
            Colony colony = colonies[i];
            DrawNest(canvas, colony.CachedSkColor, colony.NestX, colony.NestY);
        }

        long antStartTicks = Stopwatch.GetTimestamp();
        for (int i = 0; i < colonyCount; i++)
        {
            Colony colony = colonies[i];
            DrawAnts(canvas, colony);
        }
        long antEndTicks = Stopwatch.GetTimestamp();
        UpdateStageEma(ref _antStageMs, TicksToMilliseconds(antEndTicks - antStartTicks));

        if (_placingMode == PlacingMode.Colony)
        {
            int hoverCellX = (_mouseX - _gridX) / CellSize;
            int hoverCellY = (_mouseY - _gridY) / CellSize;

            if (NestFitsInWorld(hoverCellX, hoverCellY))
            {
                Color ghostBase = ColonyColors[_nextColorIndex % ColonyColors.Length];
                SKColor ghostColor = new SKColor(ghostBase.R, ghostBase.G, ghostBase.B, 140);
                DrawNest(canvas, ghostColor, hoverCellX, hoverCellY);
            }
        }

        if (_placingMode == PlacingMode.Food)
        {
            int hoverCellX = (_mouseX - _gridX) / CellSize;
            int hoverCellY = (_mouseY - _gridY) / CellSize;

            bool hoverInsideX = hoverCellX >= 0 && hoverCellX < _world.Width;
            bool hoverInsideY = hoverCellY >= 0 && hoverCellY < _world.Height;

            if (hoverInsideX && hoverInsideY)
            {
                _fillPaint.Color = _foodSkColor.WithAlpha(140);
                int ghostPixelX = _gridX + hoverCellX * CellSize;
                int ghostPixelY = _gridY + hoverCellY * CellSize;
                canvas.DrawRect(ghostPixelX, ghostPixelY, CellSize, CellSize, _fillPaint);
            }
        }

        DrawStatsPanel(canvas);

        canvas.DrawPicture(_buttonsPicture);
        canvas.DrawPicture(_hudPicture);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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