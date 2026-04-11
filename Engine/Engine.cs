namespace ANTS;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

public partial class Engine : Form
{
    private const int CellSize = 16;
    private const int BorderThickness = 8;
    private const int WorldPercent = 75;
    private const int ButtonHeight = 32;
    private const int ButtonWidth = 140;
    private const int ButtonPadding = 8;
    private const int NestRadius = 2;

    private static readonly Color[] ColonyColors = new Color[]
    {
        Color.FromArgb(239, 68, 68),
        Color.FromArgb(249, 115, 22),
        Color.FromArgb(234, 179, 8),
        Color.FromArgb(59, 130, 246),
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

    private long _frame;
    private readonly Stopwatch _fpsStopwatch = new Stopwatch();
    private int _framesThisSecond;
    private int _fps;
    private double _lastFrameMs;

    private FastSKGLControl _skControl = null!;
    private SKPaint _fillPaint = null!;
    private SKPaint _strokePaint = null!;
    private SKPaint _textPaint = null!;
    private SKPicture _gridPicture = null!;
    private SKPicture _buttonsPicture = null!;
    private SKPicture? _hudPicture;
    private SKPath _foodPath = null!;
    private SKPath _nestPath = null!;
    private SKFontMetrics _textMetrics;
    private float _textHeight;

    private readonly Stopwatch _hudStopwatch = new Stopwatch();
    private const int HudUpdateIntervalMs = 50;

    private SKColor _backgroundSkColor;
    private SKColor _foodSkColor;
    private SKColor _buttonBorderSkColor;

    public Engine()
    {
        InitializeComponent();

        _fillPaint = new SKPaint();
        _fillPaint.Style = SKPaintStyle.Fill;
        _fillPaint.IsAntialias = false;

        _strokePaint = new SKPaint();
        _strokePaint.Style = SKPaintStyle.Stroke;
        _strokePaint.IsAntialias = false;
        _strokePaint.StrokeWidth = 1;

        _textPaint = new SKPaint();
        _textPaint.Style = SKPaintStyle.Fill;
        _textPaint.IsAntialias = true;
        _textPaint.Color = SKColors.White;
        _textPaint.TextSize = 14;

        _textMetrics = _textPaint.FontMetrics;
        _textHeight = -_textMetrics.Ascent + _textMetrics.Descent;

        _foodPath = new SKPath();
        _nestPath = new SKPath();

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
        Application.Idle += OnApplicationIdle;
    }

    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        while (IsApplicationIdle())
        {
            Tick();
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
        if (_gridPicture != null)
        {
            _gridPicture.Dispose();
        }

        int gridWidth = _world.Width * CellSize;
        int gridHeight = _world.Height * CellSize;

        int borderHalf = BorderThickness / 2;
        SKRect cullRect = new SKRect(
            -borderHalf,
            -borderHalf,
            gridWidth + borderHalf,
            gridHeight + borderHalf);

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

        _gridPicture = recorder.EndRecording();
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

        if (_hudPicture == null || _hudStopwatch.ElapsedMilliseconds >= HudUpdateIntervalMs)
        {
            RecordHudPicture();
            _hudStopwatch.Restart();
        }

        _skControl.RenderFrameDirect();

        long endTicks = Stopwatch.GetTimestamp();
        _lastFrameMs = (endTicks - startTicks) * 1000.0 / Stopwatch.Frequency;
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
        if (_buttonsPicture != null)
        {
            _buttonsPicture.Dispose();
        }

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

        _buttonsPicture = recorder.EndRecording();
        recorder.Dispose();
    }

    private void RecordHudPicture()
    {
        if (_hudPicture != null)
        {
            _hudPicture.Dispose();
        }

        SKRect cullRect = new SKRect(0, 0, 400, 60);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);

        float hudBaselineY1 = 4 - _textMetrics.Ascent;
        float hudBaselineY2 = 22 - _textMetrics.Ascent;
        float hudBaselineY3 = 40 - _textMetrics.Ascent;
        recordingCanvas.DrawText("Frame: " + _frame, 8, hudBaselineY1, _textPaint);
        recordingCanvas.DrawText("Frame time: " + _lastFrameMs.ToString("F3") + " ms", 8, hudBaselineY2, _textPaint);
        recordingCanvas.DrawText("FPS: " + _fps, 8, hudBaselineY3, _textPaint);

        _hudPicture = recorder.EndRecording();
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
        if (centerCellX - NestRadius < 0)
        {
            return false;
        }
        if (centerCellX + NestRadius >= _world.Width)
        {
            return false;
        }
        if (centerCellY - NestRadius < 0)
        {
            return false;
        }
        if (centerCellY + NestRadius >= _world.Height)
        {
            return false;
        }
        return true;
    }

    private static SKColor ToSkColor(Color color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    private void DrawNest(SKCanvas canvas, SKColor color, int centerCellX, int centerCellY)
    {
        _nestPath.Reset();

        for (int dy = -NestRadius; dy <= NestRadius; dy++)
        {
            for (int dx = -NestRadius; dx <= NestRadius; dx++)
            {
                int manhattan = Math.Abs(dx) + Math.Abs(dy);
                if (manhattan > NestRadius)
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

        IReadOnlyList<Colony> colonies = _world.Colonies;
        int colonyCount = colonies.Count;
        for (int i = 0; i < colonyCount; i++)
        {
            Colony colony = colonies[i];
            DrawNest(canvas, ToSkColor(colony.Color), colony.NestX, colony.NestY);
        }

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

        if (_hudPicture != null)
        {
            canvas.DrawPicture(_hudPicture);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _textPaint.Dispose();
        _foodPath.Dispose();
        _nestPath.Dispose();
        if (_gridPicture != null)
        {
            _gridPicture.Dispose();
        }
        if (_buttonsPicture != null)
        {
            _buttonsPicture.Dispose();
        }
        if (_hudPicture != null)
        {
            _hudPicture.Dispose();
        }
        base.OnFormClosed(e);
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
