namespace ANTS;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

    public Engine()
    {
        InitializeComponent();
        DoubleBuffered = true;

        InitializeWorld();
        RecalculateLayout();

        _fpsStopwatch.Start();
        Application.Idle += OnApplicationIdle;
    }

    private void InitializeWorld()
    {
        int worldPixelWidth = ClientSize.Width * WorldPercent / 100;
        int worldPixelHeight = ClientSize.Height * WorldPercent / 100;

        int cellsX = worldPixelWidth / CellSize;
        int cellsY = worldPixelHeight / CellSize;

        _world = new World(cellsX, cellsY);
    }

    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        while (IsApplicationIdle())
        {
            Tick();
        }
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

        Invalidate();
        Update();

        long endTicks = Stopwatch.GetTimestamp();
        _lastFrameMs = (endTicks - startTicks) * 1000.0 / Stopwatch.Frequency;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
        Invalidate();
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
        _buttons.Add(addColonyButton);

        Rectangle addFoodBounds = new Rectangle(addFoodX, buttonY, ButtonWidth, ButtonHeight);
        UiButton addFoodButton = new UiButton(addFoodBounds, "Add Food", buttonBackground, StartPlacingFood);
        _buttons.Add(addFoodButton);
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouseX = e.X;
        _mouseY = e.Y;

        if (_isDrawingFood)
        {
            PaintFoodAtMouse(e.X, e.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

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

    private void DrawNest(Graphics graphics, Brush brush, int centerCellX, int centerCellY)
    {
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

                int pixelX = _gridX + cellX * CellSize;
                int pixelY = _gridY + cellY * CellSize;

                graphics.FillRectangle(brush, pixelX, pixelY, CellSize, CellSize);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics graphics = e.Graphics;

        int gridWidth = _world.Width * CellSize;
        int gridHeight = _world.Height * CellSize;

        using (Pen borderPen = new Pen(Color.Gray, BorderThickness))
        {
            int borderX = _gridX - BorderThickness / 2;
            int borderY = _gridY - BorderThickness / 2;
            int borderWidth = gridWidth + BorderThickness;
            int borderHeight = gridHeight + BorderThickness;
            graphics.DrawRectangle(borderPen, borderX, borderY, borderWidth, borderHeight);
        }

        using (Pen gridPen = new Pen(Color.FromArgb(45, 45, 45)))
        {
            for (int x = 0; x <= _world.Width; x++)
            {
                int lineX = _gridX + x * CellSize;
                graphics.DrawLine(gridPen, lineX, _gridY, lineX, _gridY + gridHeight);
            }

            for (int y = 0; y <= _world.Height; y++)
            {
                int lineY = _gridY + y * CellSize;
                graphics.DrawLine(gridPen, _gridX, lineY, _gridX + gridWidth, lineY);
            }
        }

        using (Brush foodBrush = new SolidBrush(FoodColor))
        {
            for (int cellX = 0; cellX < _world.Width; cellX++)
            {
                for (int cellY = 0; cellY < _world.Height; cellY++)
                {
                    if (_world.GetCell(cellX, cellY) != CellType.Food)
                    {
                        continue;
                    }

                    int pixelX = _gridX + cellX * CellSize;
                    int pixelY = _gridY + cellY * CellSize;
                    graphics.FillRectangle(foodBrush, pixelX, pixelY, CellSize, CellSize);
                }
            }
        }

        foreach (Colony colony in _world.Colonies)
        {
            using (Brush nestBrush = new SolidBrush(colony.Color))
            {
                DrawNest(graphics, nestBrush, colony.NestX, colony.NestY);
            }
        }

        if (_placingMode == PlacingMode.Colony)
        {
            int hoverCellX = (_mouseX - _gridX) / CellSize;
            int hoverCellY = (_mouseY - _gridY) / CellSize;

            if (NestFitsInWorld(hoverCellX, hoverCellY))
            {
                Color ghostBase = ColonyColors[_nextColorIndex % ColonyColors.Length];
                Color ghostColor = Color.FromArgb(140, ghostBase);

                using (Brush ghostBrush = new SolidBrush(ghostColor))
                {
                    DrawNest(graphics, ghostBrush, hoverCellX, hoverCellY);
                }
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
                Color foodGhost = Color.FromArgb(140, FoodColor);

                using (Brush foodGhostBrush = new SolidBrush(foodGhost))
                {
                    int ghostPixelX = _gridX + hoverCellX * CellSize;
                    int ghostPixelY = _gridY + hoverCellY * CellSize;
                    graphics.FillRectangle(foodGhostBrush, ghostPixelX, ghostPixelY, CellSize, CellSize);
                }
            }
        }

        foreach (UiButton button in _buttons)
        {
            using (Brush buttonBrush = new SolidBrush(button.BackgroundColor))
            {
                graphics.FillRectangle(buttonBrush, button.Bounds);
            }

            using (Pen buttonBorderPen = new Pen(Color.FromArgb(120, 120, 120)))
            {
                int borderRectX = button.Bounds.X;
                int borderRectY = button.Bounds.Y;
                int borderRectWidth = button.Bounds.Width - 1;
                int borderRectHeight = button.Bounds.Height - 1;
                graphics.DrawRectangle(buttonBorderPen, borderRectX, borderRectY, borderRectWidth, borderRectHeight);
            }

            using (Brush labelBrush = new SolidBrush(Color.White))
            {
                SizeF labelSize = graphics.MeasureString(button.Label, Font);
                int labelWidth = (int)labelSize.Width;
                int labelHeight = (int)labelSize.Height;
                int labelX = button.Bounds.X + (button.Bounds.Width - labelWidth) / 2;
                int labelY = button.Bounds.Y + (button.Bounds.Height - labelHeight) / 2;
                graphics.DrawString(button.Label, Font, labelBrush, labelX, labelY);
            }
        }

        using (Brush hudBrush = new SolidBrush(Color.White))
        {
            graphics.DrawString("Frame: " + _frame, Font, hudBrush, 8, 4);
            graphics.DrawString("Frame time: " + _lastFrameMs.ToString("F3") + " ms", Font, hudBrush, 8, 22);
            graphics.DrawString("FPS: " + _fps, Font, hudBrush, 8, 40);
        }
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
