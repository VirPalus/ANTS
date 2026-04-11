namespace ANTS;
using System.Diagnostics;
using System.Runtime.InteropServices;

public partial class Engine : Form
{
    private const int CellSize = 16;
    private const int BorderThickness = 8;
    private const int WorldPercent = 75;

    private World _world = null!;
    private int _gridX;
    private int _gridY;

    private long _frame;
    private readonly Stopwatch _fpsStopwatch = new Stopwatch();
    private int _framesThisSecond;
    private int _fps;
    private double _lastFrameMs;

    public Engine()
    {
        InitializeComponent();
        DoubleBuffered = true;

        RecalculateLayout();

        _fpsStopwatch.Start();
        Application.Idle += OnApplicationIdle;
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
        int worldPixelWidth = ClientSize.Width * WorldPercent / 100;
        int worldPixelHeight = ClientSize.Height * WorldPercent / 100;

        int cellsX = worldPixelWidth / CellSize;
        int cellsY = worldPixelHeight / CellSize;

        _world = new World(cellsX, cellsY);

        int gridWidth = cellsX * CellSize;
        int gridHeight = cellsY * CellSize;

        _gridX = (ClientSize.Width - gridWidth) / 2;
        _gridY = (ClientSize.Height - gridHeight) / 2;
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
