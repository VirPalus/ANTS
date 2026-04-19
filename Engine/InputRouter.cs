namespace ANTS;

using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

public sealed class InputRouter
{
    private const float KeyboardPanPxPerSecond = 900f;

    private readonly Camera _camera;
    private readonly Func<World> _worldGetter;
    private readonly UiStartOverlay _startOverlay;
    private readonly UiTopBar _topBar;
    private readonly IReadOnlyList<UiButton> _buttons;
    private readonly SelectionController _selection;
    private readonly PlacementController _placement;
    private readonly ProfilerGraphWindow _profilerGraphWindow;
    private readonly Action _focusCanvas;
    private readonly Action _markTopBarDirty;
    private readonly Action _markButtonsDirty;
    private readonly Action _onPauseToggled;
    private readonly Action _onProfilerToggled;

    private bool _isRightDragging;
    private int _rightDragLastX;
    private int _rightDragLastY;
    private bool _keyPanLeft;
    private bool _keyPanRight;
    private bool _keyPanUp;
    private bool _keyPanDown;
    private long _lastPanTicks;

    public InputRouter(
        Camera camera,
        Func<World> worldGetter,
        UiStartOverlay startOverlay,
        UiTopBar topBar,
        IReadOnlyList<UiButton> buttons,
        SelectionController selection,
        PlacementController placement,
        ProfilerGraphWindow profilerGraphWindow,
        Action focusCanvas,
        Action markTopBarDirty,
        Action markButtonsDirty,
        Action onPauseToggled,
        Action onProfilerToggled)
    {
        _camera = camera;
        _worldGetter = worldGetter;
        _startOverlay = startOverlay;
        _topBar = topBar;
        _buttons = buttons;
        _selection = selection;
        _placement = placement;
        _profilerGraphWindow = profilerGraphWindow;
        _focusCanvas = focusCanvas;
        _markTopBarDirty = markTopBarDirty;
        _markButtonsDirty = markButtonsDirty;
        _onPauseToggled = onPauseToggled;
        _onProfilerToggled = onProfilerToggled;
        _lastPanTicks = Stopwatch.GetTimestamp();
    }

    public void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _isRightDragging = true;
            _rightDragLastX = e.X;
            _rightDragLastY = e.Y;
            _focusCanvas();
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // Profiler graph window has top-most capture priority when visible:
        // drag/resize/close/zoom must intercept before any world-layer input.
        if (_profilerGraphWindow.IsVisible && _profilerGraphWindow.HandleMouseDown(e.X, e.Y))
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
            _markTopBarDirty();
            return;
        }

        for (int i = 0; i < _buttons.Count; i++)
        {
            if (_buttons[i].Contains(e.X, e.Y))
            {
                _buttons[i].OnClick();
                _markButtonsDirty();
                return;
            }
        }

        World world = _worldGetter();

        if (_placement.HandleMouseDown(e.X, e.Y, world))
        {
            return;
        }

        _selection.TrySelect(e.X, e.Y, world);
    }

    public void OnMouseMove(object? sender, MouseEventArgs e)
    {
        // Forward to graph window unconditionally so in-flight drag/resize
        // continues even when the pointer leaves the window bounds mid-gesture.
        _profilerGraphWindow.HandleMouseMove(e.X, e.Y);

        _placement.UpdateMouseCoords(e.X, e.Y);

        bool wasPauseHovered = _topBar.PauseButton.IsHovered;
        _topBar.UpdateHover(e.X, e.Y);
        if (_topBar.PauseButton.IsHovered != wasPauseHovered)
        {
            _markTopBarDirty();
        }
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool wasHovered = _buttons[i].IsHovered;
            _buttons[i].IsHovered = _buttons[i].Contains(e.X, e.Y);
            if (_buttons[i].IsHovered != wasHovered)
            {
                _markButtonsDirty();
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

        _placement.HandleMouseMoveDrag(_worldGetter());
    }

    public void OnMouseUp(object? sender, MouseEventArgs e)
    {
        // Always clear any in-flight graph-window drag/resize state on release.
        _profilerGraphWindow.HandleMouseUp();

        if (e.Button == MouseButtons.Right)
        {
            _isRightDragging = false;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            _placement.HandleMouseUp();
        }
    }

    public void OnMouseWheel(object? sender, MouseEventArgs e)
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

    public void HandleKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _selection.Clear();
            _placement.Cancel();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Space)
        {
            _onPauseToggled();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.F2)
        {
            _onProfilerToggled();
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

    public void HandleKeyUp(KeyEventArgs e)
    {
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

    public void ApplyKeyboardPan()
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
}
