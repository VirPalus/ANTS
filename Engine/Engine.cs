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
    private const int ButtonHeight = 32;
    private const int ButtonWidth = 140;
    private const int ButtonPadding = 8;
    private const float FitMarginPx = 40f;

    private static readonly Color FoodColor = Color.FromArgb(34, 197, 94);
    private World _world = null!;
    private Camera _camera = new Camera();
    private List<UiButton> _buttons = new List<UiButton>();
    private bool _showPheromones;
    private SelectionController _selection = null!;
    private PlacementController _placement = null!;
    private InputRouter _input = null!;
    private FastSKGLControl _skControl = null!;
    private PaintCache _paints = null!;
    private SKPicture _buttonsPicture = null!;
    private bool _buttonsDirty = true;
    private SKPicture? _topBarPicture;
    private bool _topBarDirty = true;
    private UiTopBar _topBar = null!;
    private UiStartOverlay _startOverlay = null!;
    private WorldRenderer _worldRenderer = null!;
    private AntsRenderer _antsRenderer = null!;
    private OverlayRenderer _overlayRenderer = null!;
    private HudRenderer _hudRenderer = null!;
    private StatsPanelRenderer _statsRenderer = null!;
    private FrameProfiler _profiler = null!;
    private ProfilerUI _profilerUI = null!;
    private ProfilerGraphWindow _profilerGraphWindow = null!;
    private long _frameCounter;
    private SKFontMetrics _textMetrics;
    private float _textHeight;
    private int _frameCap = 10000;
    private long _ticksPerFrame;
    private long _nextFrameTicks;
    private SimDriver _sim = null!;
    private SKColor _foodSkColor;
    private readonly List<IDisposable> _ownedDisposables = new List<IDisposable>();

    public Engine()
    {
        InitializeComponent();

        _paints = Own(new PaintCache());
        _selection = new SelectionController(_camera, _paints);

        _textMetrics = _paints.SharedText.FontMetrics;
        _textHeight = -_textMetrics.Ascent + _textMetrics.Descent;

        _foodSkColor = new SKColor(FoodColor.R, FoodColor.G, FoodColor.B);

        _placement = Own(new PlacementController(_camera, _paints, _foodSkColor, c => Cursor = c));

        _skControl = new FastSKGLControl();
        _skControl.Dock = DockStyle.Fill;
        _skControl.PaintSurface += OnSkPaintSurface;
        Controls.Add(_skControl);
        KeyPreview = true;

        InitializeWorld();
        _sim = new SimDriver(_world);

        // Profiler must exist BEFORE _topBar (isProfilerActive getter)
        // and BEFORE WorldRenderer/OverlayRenderer (ctor dependencies).
        _profiler = Own(new FrameProfiler());
        _profilerGraphWindow = Own(new ProfilerGraphWindow(_profiler, () => ClientSize));

        _topBar = new UiTopBar(
            SimDriver.SpeedChoices,
            () => _sim.IsPaused,
            () => _sim.Speed,
            OnPauseToggled,
            OnSpeedChanged,
            ToggleProfiler,
            () => _profiler.IsEnabled);
        _startOverlay = new UiStartOverlay(OnStartOverlayPick);
        string mapsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Maps");
        _startOverlay.Scan(mapsDir, 180);

        _input = new InputRouter(
            _camera,
            () => _world,
            _startOverlay,
            _topBar,
            _buttons,
            _selection,
            _placement,
            _profilerGraphWindow,
            () => _skControl.Focus(),
            () => _topBarDirty = true,
            () => _buttonsDirty = true,
            OnPauseToggled,
            ToggleProfiler);
        _skControl.MouseDown += _input.OnMouseDown;
        _skControl.MouseMove += _input.OnMouseMove;
        _skControl.MouseUp += _input.OnMouseUp;
        _skControl.MouseWheel += _input.OnMouseWheel;

        RecalculateLayout();
        _camera.FitWorld(_world.Width * CellSize, _world.Height * CellSize, ClientSize.Width, ClientSize.Height, FitMarginPx);

        if (_startOverlay.Entries.Count > 0)
        {
            _sim.TogglePause();
        }
        else
        {
            _startOverlay.Visible = false;
        }

        _antsRenderer = Own(new AntsRenderer(_paints, _camera));
        _worldRenderer = Own(new WorldRenderer(() => _world, _camera, _foodSkColor, _profiler));
        _overlayRenderer = Own(new OverlayRenderer(_paints, _camera, () => _world, _profiler));
        _hudRenderer = Own(new HudRenderer(_paints));
        _hudRenderer.Start();
        _statsRenderer = Own(new StatsPanelRenderer(_paints, () => _world, _foodSkColor));
        _statsRenderer.Start(ClientSize.Width, ClientSize.Height);
        _profilerUI = new ProfilerUI(_profiler, () => _frameCounter);
        _worldRenderer.Rebuild();
        RecordTopBarPicture();
        UpdateFrameCapTiming();
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
        _placement.SetNextColorIndex(seedCount);

        _worldRenderer?.RebuildGrid();
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
        _sim.SetWorld(_world);
        _selection.Clear();
        _placement.Cancel();
        _world.ApplyMapLayout(map);

        for (int i = 0; i < map.ColonySeeds.Count; i++)
        {
            ColonySeed seed = map.ColonySeeds[i];
            _world.AddColony(seed.X, seed.Y, seed.Color);
        }
        _placement.SetNextColorIndex(map.ColonySeeds.Count);

        _worldRenderer.Rebuild();
        _camera.FitWorld(_world.Width * CellSize, _world.Height * CellSize, ClientSize.Width, ClientSize.Height, FitMarginPx);
        _startOverlay.Visible = false;
        _topBar.MapName = entry.DisplayName;
        _topBarDirty = true;
        RecalculateLayout();
    }

    private void Tick()
    {
        long startTicks = Stopwatch.GetTimestamp();

        _hudRenderer.TickFrameStart();

        _frameCounter++;
        _profiler.BeginFrame(_frameCounter);

        _profiler.BeginPhase(ProfilePhase.Sim);
        long simStartTicks = Stopwatch.GetTimestamp();
        _sim.Advance();
        long simEndTicks = Stopwatch.GetTimestamp();
        _profiler.EndPhase(ProfilePhase.Sim);
        _hudRenderer.ReportSimStageTicks(simEndTicks - simStartTicks);

        _input.ApplyKeyboardPan();

        _hudRenderer.MaybeRebuild();
        _statsRenderer.MaybeRebuild(ClientSize.Width, ClientSize.Height);

        _skControl.RenderFrameDirect();

        long endTicks = Stopwatch.GetTimestamp();
        _hudRenderer.ReportFrameTicks(endTicks - startTicks);
        _profiler.ReportFrameTicks(endTicks - startTicks);
        _profiler.EndFrame();
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
            "Add Colony", _placement.StartPlacingColony);
        addColonyButton.IsActive = () => _placement.IsPlacingColony;
        CacheButtonTextPosition(addColonyButton);
        _buttons.Add(addColonyButton);

        UiButton addFoodButton = new UiButton(
            new Rectangle(addFoodX, buttonY, ButtonWidth, ButtonHeight),
            "Add Food", _placement.StartPlacingFood);
        addFoodButton.IsActive = () => _placement.IsPlacingFood;
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
        _topBar.CacheTextPositions(_paints.SharedText, _textMetrics, _textHeight);

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

    private void OnPauseToggled()
    {
        _sim.TogglePause();
        _topBar.Layout(ClientSize.Width);
        _topBar.CacheTextPositions(_paints.SharedText, _textMetrics, _textHeight);
        _topBarDirty = true;
    }

    private void OnSpeedChanged(double speed)
    {
        _sim.SetSpeed(speed);
        _topBarDirty = true;
    }


    private void RecordButtonsPicture()
    {
        // perf-rule-5/8 exempt: all SK* allocs below run inside SKPictureRecorder (one-time per dirty rebuild)
        int w = ClientSize.Width;
        int h = ClientSize.Height;
        SKRect cullRect = new SKRect(0, 0, w + 20, h + 20);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].Draw(rc, _paints.SharedFill, _paints.SharedBorder, _paints.SharedText);
        }

        Replace(ref _buttonsPicture!, recorder.EndRecording());
        recorder.Dispose();
        _buttonsDirty = false;
    }

    private void RecordTopBarPicture()
    {
        // perf-rule-5/8 exempt: all SK* allocs below run inside SKPictureRecorder (one-time per dirty rebuild)
        int w = ClientSize.Width;
        _topBar.Layout(w);
        _topBar.CacheTextPositions(_paints.SharedText, _paints.SharedText.FontMetrics, -_paints.SharedText.FontMetrics.Ascent + _paints.SharedText.FontMetrics.Descent);

        SKRect cullRect = new SKRect(0, 0, w + 20, UiTopBar.BarHeight + 4);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        _topBar.Draw(rc, _paints.SharedFill, _paints.SharedBorder, _paints.SharedText);

        Replace(ref _topBarPicture!, recorder.EndRecording());
        recorder.Dispose();
        _topBarDirty = false;
    }
    private void CacheButtonTextPosition(UiButton button)
    {
        float textWidth = _paints.SharedText.MeasureText(button.Label);
        float labelTopX = button.Bounds.X + (button.Bounds.Width - textWidth) / 2;
        float labelTopY = button.Bounds.Y + (button.Bounds.Height - _textHeight) / 2;
        button.TextBaselineX = labelTopX;
        button.TextBaselineY = labelTopY - _textMetrics.Ascent;
    }

    private void TogglePheromones()
    {
        _showPheromones = !_showPheromones;
    }

    private void ToggleProfiler()
    {
        if (_profiler == null) return;
        try
        {
            if (_profiler.IsEnabled)
            {
                _profiler.Disable();
                _profilerGraphWindow?.Hide();
            }
            else
            {
                _profiler.Enable();
                if (_profiler.IsEnabled)
                {
                    _profilerGraphWindow?.Show();
                }
            }
        }
        catch
        {
            // Surface only via _profiler.LastError; never crash UI.
        }
        // Profile button lives in the top bar now (fase-4.12-fixup),
        // so the top-bar picture cache is the one that needs invalidating.
        _topBarDirty = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _input.HandleKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _input.HandleKeyUp(e);
    }
    private static SKColor ToSkColor(Color color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    private void OnSkPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(UiTheme.BgRoot);

        if (_startOverlay.Visible)
        {
            _startOverlay.Draw(canvas, ClientSize.Width, ClientSize.Height, _paints.SharedFill, _paints.SharedBorder, _paints.SharedText, _paints.TitlePaint);
            return;
        }

        _profiler.BeginPhase(ProfilePhase.PaintTotal);

        _profiler.BeginPhase(ProfilePhase.CanvasSetup);
        canvas.Save();
        _camera.Apply(canvas);
        _profiler.EndPhase(ProfilePhase.CanvasSetup);

        // fase-4.12-fixup variant A: WorldDraw phase replaced by
        // GridDraw/FoodDraw/NestsDraw, instrumented inside WorldRenderer.
        _worldRenderer.DrawBase(canvas);

        if (_showPheromones)
        {
            _profiler.BeginPhase(ProfilePhase.OverlayDraw);
            _overlayRenderer.Draw(canvas, ClientSize.Width, ClientSize.Height);
            _profiler.EndPhase(ProfilePhase.OverlayDraw);
        }

        _worldRenderer.DrawFoodNestsAndGridLines(canvas);

        IReadOnlyList<Colony> colonies = _world.Colonies;
        _profiler.BeginPhase(ProfilePhase.AntsDraw);
        long antStartTicks = Stopwatch.GetTimestamp();
        if (_antsRenderer.DrawAllColonies(canvas, colonies))
        {
            long antEndTicks = Stopwatch.GetTimestamp();
            _hudRenderer.ReportAntStageTicks(antEndTicks - antStartTicks);
        }
        _profiler.EndPhase(ProfilePhase.AntsDraw);

        _profiler.BeginPhase(ProfilePhase.Placement);
        _placement.DrawGhost(canvas, _world);
        _profiler.EndPhase(ProfilePhase.Placement);

        _profiler.BeginPhase(ProfilePhase.Selection);
        _selection.DrawOverlay(canvas, ClientSize.Width, ClientSize.Height);
        canvas.Restore();
        _selection.DrawInfoPanel(canvas);
        _profiler.EndPhase(ProfilePhase.Selection);

        _profiler.AccumulatePhaseBegin(ProfilePhase.Buttons);
        if (_topBarDirty)
        {
            RecordTopBarPicture();
        }
        if (_topBarPicture != null)
        {
            canvas.DrawPicture(_topBarPicture);
        }
        _profiler.AccumulatePhaseEnd(ProfilePhase.Buttons);

        _profiler.BeginPhase(ProfilePhase.StatsDraw);
        _statsRenderer.Draw(canvas);
        _profiler.EndPhase(ProfilePhase.StatsDraw);

        _profiler.AccumulatePhaseBegin(ProfilePhase.Buttons);
        if (_buttonsDirty)
        {
            RecordButtonsPicture();
        }
        if (_buttonsPicture != null)
        {
            canvas.DrawPicture(_buttonsPicture);
        }
        _profiler.AccumulatePhaseEnd(ProfilePhase.Buttons);

        _profiler.BeginPhase(ProfilePhase.HudDraw);
        _hudRenderer.Draw(canvas);
        _profiler.EndPhase(ProfilePhase.HudDraw);

        _profiler.BeginPhase(ProfilePhase.ProfilerWindow);
        _profilerUI.Draw(canvas);
        _profilerGraphWindow.Draw(canvas);
        _profiler.EndPhase(ProfilePhase.ProfilerWindow);

        _profiler.EndPhase(ProfilePhase.PaintTotal);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _startOverlay?.Dispose();

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
