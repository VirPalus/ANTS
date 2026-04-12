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
    private const int WorldPercent = 80;
    private const int ButtonHeight = 32;
    private const int ButtonWidth = 140;
    private const int ButtonPadding = 8;

    private static readonly Color[] ColonyColors = new Color[]
    {
        Color.FromArgb(239, 68, 68),
        Color.FromArgb(59, 130, 246),
        Color.FromArgb(249, 115, 22),
        Color.FromArgb(168, 85, 247),
        Color.FromArgb(234, 179, 8),
        Color.FromArgb(236, 72, 153),
    };

    private static readonly Color FoodColor = Color.FromArgb(34, 197, 94);
    private static readonly SKColor AntSkColor = new SKColor(210, 210, 210);

    private World _world = null!;
    private int _gridX;
    private int _gridY;

    private List<UiButton> _buttons = new List<UiButton>();
    private PlacingMode _placingMode = PlacingMode.None;
    private int _nextColorIndex;
    private int _mouseX;
    private int _mouseY;
    private bool _isDrawingFood;

    private long _frame;
    private readonly Stopwatch _fpsStopwatch = new Stopwatch();
    private int _framesThisSecond;
    private int _fps;
    private double _lastFrameMs;

    private double _simStageMs;
    private double _foodStageMs;
    private double _nestStageMs;
    private double _antStageMs;
    private double _drawStageMs;
    private double _renderStageMs;
    private double _presentStageMs;
    private double _lastDrawStageSampleMs;
    private const double StageEmaAlpha = 0.05;

    private FastSKGLControl _skControl = null!;
    private SKPaint _fillPaint = null!;
    private SKPaint _strokePaint = null!;
    private SKPaint _textPaint = null!;
    private SKPaint _antPaint = null!;
    private SKPicture _gridPicture = null!;
    private SKPicture _buttonsPicture = null!;
    private SKPicture _hudPicture = null!;
    private SKPath _foodPath = null!;
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
    private SKColor _foodSkColor;
    private SKColor _buttonBorderSkColor;

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

        _foodPath = Own(new SKPath());
        _nestPath = Own(new SKPath());

        _backgroundSkColor = new SKColor(BackColor.R, BackColor.G, BackColor.B);
        _foodSkColor = new SKColor(FoodColor.R, FoodColor.G, FoodColor.B);
        _buttonBorderSkColor = new SKColor(120, 120, 120);

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

        _frame++;
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

        long renderStartTicks = Stopwatch.GetTimestamp();
        _skControl.RenderFrameDirect();
        long renderEndTicks = Stopwatch.GetTimestamp();
        double renderSampleMs = TicksToMilliseconds(renderEndTicks - renderStartTicks);
        UpdateStageEma(ref _renderStageMs, renderSampleMs);
        double presentSampleMs = renderSampleMs - _lastDrawStageSampleMs;
        if (presentSampleMs < 0.0)
        {
            presentSampleMs = 0.0;
        }
        UpdateStageEma(ref _presentStageMs, presentSampleMs);

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
        SKRect cullRect = new SKRect(0, 0, 400, 220);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);

        float lineStep = 18f;
        float firstBaselineY = 4f - _textMetrics.Ascent;
        float baselineY = firstBaselineY;

        recordingCanvas.DrawText("Frame: " + _frame, 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("FPS: " + _fps, 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Frame: " + _lastFrameMs.ToString("F3") + " ms", 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Sim:    " + _simStageMs.ToString("F3") + " ms", 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Food:   " + _foodStageMs.ToString("F3") + " ms", 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Nest:   " + _nestStageMs.ToString("F3") + " ms", 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Ants:   " + _antStageMs.ToString("F3") + " ms", 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Draw:   " + _drawStageMs.ToString("F3") + " ms", 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Render: " + _renderStageMs.ToString("F3") + " ms", 8, baselineY, _textPaint);
        baselineY += lineStep;
        recordingCanvas.DrawText("Present:" + _presentStageMs.ToString("F3") + " ms", 8, baselineY, _textPaint);

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
            float scos = headingCos * invSup;
            float ssin = headingSin * invSup;
            float tx = centerX - scos * anchor + ssin * anchor;
            float ty = centerY - ssin * anchor - scos * anchor;
            bodyTransforms[i] = new SKRotationScaleMatrix(scos, ssin, tx, ty);

            int frameIndex = AntRenderer.GetFrameIndex(ant.StridePhase);
            bodySprites[i] = frameRects[frameIndex];
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
        _antBodyColorFilter = SKColorFilter.CreateBlendMode(colonyColor, SKBlendMode.Modulate);
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
        long paintStartTicks = Stopwatch.GetTimestamp();

        SKCanvas canvas = e.Surface.Canvas;

        canvas.Clear(_backgroundSkColor);

        canvas.Save();
        canvas.Translate(_gridX, _gridY);
        canvas.DrawPicture(_gridPicture);
        canvas.Restore();

        long foodStartTicks = Stopwatch.GetTimestamp();
        int foodCount = _world.FoodCount;
        if (foodCount > 0)
        {
            _foodPath.Reset();
            Point[] foodCells = _world.FoodCells;
            for (int i = 0; i < foodCount; i++)
            {
                int cellX = foodCells[i].X;
                int cellY = foodCells[i].Y;
                float pixelX = _gridX + cellX * CellSize;
                float pixelY = _gridY + cellY * CellSize;
                _foodPath.AddRect(new SKRect(pixelX, pixelY, pixelX + CellSize, pixelY + CellSize));
            }

            _fillPaint.Color = _foodSkColor;
            canvas.DrawPath(_foodPath, _fillPaint);
        }
        long foodEndTicks = Stopwatch.GetTimestamp();
        UpdateStageEma(ref _foodStageMs, TicksToMilliseconds(foodEndTicks - foodStartTicks));

        long nestStartTicks = Stopwatch.GetTimestamp();
        IReadOnlyList<Colony> colonies = _world.Colonies;
        int colonyCount = colonies.Count;
        for (int i = 0; i < colonyCount; i++)
        {
            Colony colony = colonies[i];
            DrawNest(canvas, colony.CachedSkColor, colony.NestX, colony.NestY);
        }
        long nestEndTicks = Stopwatch.GetTimestamp();
        UpdateStageEma(ref _nestStageMs, TicksToMilliseconds(nestEndTicks - nestStartTicks));

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
                _fillPaint.Color = new SKColor(FoodColor.R, FoodColor.G, FoodColor.B, 140);
                int ghostPixelX = _gridX + hoverCellX * CellSize;
                int ghostPixelY = _gridY + hoverCellY * CellSize;
                canvas.DrawRect(ghostPixelX, ghostPixelY, CellSize, CellSize, _fillPaint);
            }
        }

        canvas.DrawPicture(_buttonsPicture);
        canvas.DrawPicture(_hudPicture);

        long paintEndTicks = Stopwatch.GetTimestamp();
        double drawSampleMs = TicksToMilliseconds(paintEndTicks - paintStartTicks);
        _lastDrawStageSampleMs = drawSampleMs;
        UpdateStageEma(ref _drawStageMs, drawSampleMs);
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
