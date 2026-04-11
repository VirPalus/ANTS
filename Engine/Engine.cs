namespace ANTS;
using Timer = System.Windows.Forms.Timer;

public partial class Engine : Form
{
    private const int CellSize = 16;
    private const int BorderThickness = 8;
    private const int WorldPercent = 75;

    private readonly Timer _timer = new Timer();

    private World _world = null!;
    private int _gridX;
    private int _gridY;
    private int _frame;

    public Engine()
    {
        InitializeComponent();
        components.Add(_timer);
        DoubleBuffered = true;

        RecalculateLayout();

        _timer.Interval = 1000 / 60;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _frame++;
        Invalidate();
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

        using (Brush frameTextBrush = new SolidBrush(Color.White))
        {
            graphics.DrawString("Frame: " + _frame, Font, frameTextBrush, 8, 4);
        }
    }
}
