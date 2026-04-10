namespace ANTS;
using System.Windows.Forms;

public partial class Engine : Form
{
    private Timer _timer;
    private int _tick;

    public Engine()
    {
        InitializeComponent();
        DoubleBuffered = true;
        _timer = new Timer();
        _timer.Interval = 1000/60;
        _timer.Tick += OnTick;
        _timer.Start();
    }
    private void OnTick(object? sender, EventArgs e)
    {
        _tick++;
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.DrawString("Frame: " + _tick.ToString(), Font, Brushes.White, 10, 10);
    }
}
