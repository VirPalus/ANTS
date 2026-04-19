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
    private SKPicture _statsPicture = null!;
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

        _topBar = new UiTopBar(SimDriver.SpeedChoices, () => _sim.IsPaused, () => _sim.Speed, OnPauseToggled, OnSpeedChanged);
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
            () => _skControl.Focus(),
            () => _topBarDirty = true,
            () => _buttonsDirty = true,
            OnPauseToggled);
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
        _worldRenderer = Own(new WorldRenderer(() => _world, _camera, _foodSkColor));
        _overlayRenderer = Own(new OverlayRenderer(_paints, _camera, () => _world));
        _hudRenderer = Own(new HudRenderer(_paints));
        _hudRenderer.Start();
        RecordStatsPicture();
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

        long simStartTicks = Stopwatch.GetTimestamp();
        _sim.Advance();
        long simEndTicks = Stopwatch.GetTimestamp();
        _hudRenderer.ReportSimStageTicks(simEndTicks - simStartTicks);

        _input.ApplyKeyboardPan();

        if (_hudRenderer.MaybeRebuild())
        {
            RecordStatsPicture();
        }

        _skControl.RenderFrameDirect();

        long endTicks = Stopwatch.GetTimestamp();
        _hudRenderer.ReportFrameTicks(endTicks - startTicks);
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

    private void RecordStatsPicture()
    {
        // perf-rule-5/8 exempt: all SK* allocs below run inside SKPictureRecorder (one-time per dirty rebuild)

        SKRect cullRect = new SKRect(0, 0, ClientSize.Width + 20, ClientSize.Height + 20);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        DrawStatsPanel(rc);

        Replace(ref _statsPicture!, recorder.EndRecording());
        recorder.Dispose();
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
        _paints.SharedFill.Color = UiTheme.BgPanel;
        _paints.SharedBorder.Color = UiTheme.BorderSubtle;
        _paints.SharedBorder.StrokeWidth = UiTheme.BorderThin;
        UiPanel.DrawWithBorder(canvas, _paints.SharedFill, _paints.SharedBorder, x, y, StatsPanelWidth, StatsCardHeight, UiTheme.CornerMedium);
        float healthFraction = colony.NestHealthFraction;

        if (healthFraction < 0f) healthFraction = 0f;
        if (healthFraction > 1f) healthFraction = 1f;

        float barWidth = (StatsPanelWidth - 2f) * healthFraction;

        if (barWidth > 0f)
        {
            _paints.SharedFill.Color = colonyColor;
            canvas.DrawRect(x + 1f, y + 1f, barWidth, 6f, _paints.SharedFill);
        }

        float pad = StatsCardPadding;
        float textX = x + pad;
        float curY = y + 24f;

        _paints.SharedText.Color = UiTheme.TextStrong;
        string headerLine = "Ants " + colony.Ants.Count + "   Food " + colony.NestFood;
        canvas.DrawText(headerLine, textX, curY, _paints.SharedText);
        curY += 10f;

        float graphW = StatsPanelWidth - pad * 2f;
        float graphH = StatsGraphHeight + 8f;
        _paints.SharedFill.Color = UiTheme.BgPanelAlt;
        canvas.DrawRect(textX, curY, graphW, graphH, _paints.SharedFill);
        DrawPopulationGraph(canvas, colony, textX + 2f, curY + 2f, graphW - 4f, graphH - 4f);
        curY += graphH + 6f;

        _paints.SharedText.Color = UiTheme.TextBody;
        DrawRoleBreakdown(canvas, colony, textX, curY);
        curY += StatsLineHeight * 4f + 4f;

        DrawQueenIntent(canvas, colony, textX, curY);

        if (isDead)
        {
            _paints.SharedFill.Color = new SKColor(0, 0, 0, 160);
            UiPanel.Draw(canvas, _paints.SharedFill, x, y, StatsPanelWidth, StatsCardHeight, UiTheme.CornerMedium);

            float elapsed = _world.SimulationTime - colony.DeathTime;
            if (elapsed < 0f) elapsed = 0f;
            string label = "DEAD - " + colony.DeathReason + " (" + ((int)elapsed) + "s ago)";
            _paints.SharedText.Color = UiTheme.TextStrong;
            float tw = _paints.SharedText.MeasureText(label);
            canvas.DrawText(label, x + (StatsPanelWidth - tw) / 2f, y + StatsCardHeight / 2f - _textMetrics.Ascent / 2f, _paints.SharedText);
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
        _paints.SharedText.Color = UiTheme.TextBody;
        canvas.DrawText(label, x, y + 11, _paints.SharedText);

        string countStr = count.ToString(CultureInfo.InvariantCulture);
        float labelW = _paints.SharedText.MeasureText(label);
        _paints.SharedText.Color = UiTheme.TextStrong;
        canvas.DrawText(countStr, x + labelW + 6f, y + 11, _paints.SharedText);

        float barX = x + 120f;
        float barY = y + 3f;
        float barH = StatsRoleBarHeight + 2f;

        _paints.SharedFill.Color = UiTheme.BgPanelHover;
        canvas.DrawRect(barX, barY, StatsRoleBarWidth, barH, _paints.SharedFill);

        float fraction = (float)count / (float)total;
        float filledW = fraction * StatsRoleBarWidth;
        if (filledW > 1f)
        {
            _paints.SharedFill.Color = barColor;
            canvas.DrawRect(barX, barY, filledW, barH, _paints.SharedFill);
        }
    }

    private void DrawQueenIntent(SKCanvas canvas, Colony colony, float x, float y)
    {
        QueenIntent intent = colony.RoleQuota.GetCurrentIntent(colony);
        _paints.SharedText.Color = UiTheme.TextMuted;
        canvas.DrawText("Queen: " + intent.Plan, x, y + 11, _paints.SharedText);

        float defenseY = y + StatsLineHeight;
        DrawSignalBar(canvas, "Defense:", colony.Defense, DefenseBarColor, x, defenseY);

        float offenseY = defenseY + StatsLineHeight;
        DrawSignalBar(canvas, "Offense:", colony.Offense, OffenseBarColor, x, offenseY);
    }

    private void DrawSignalBar(SKCanvas canvas, string label, float value, SKColor barColor, float x, float y)
    {
        _paints.SharedText.Color = UiTheme.TextMuted;
        canvas.DrawText(label, x, y + 11, _paints.SharedText);

        float barX = x + 60f;
        float barY = y + 3f;
        float barW = StatsRoleBarWidth + 34f;
        float barH = StatsRoleBarHeight + 2f;

        _paints.SharedFill.Color = UiTheme.BgPanelHover;
        canvas.DrawRect(barX, barY, barW, barH, _paints.SharedFill);

        float filled = value * barW;
        if (filled > 1f)
        {
            _paints.SharedFill.Color = barColor;
            canvas.DrawRect(barX, barY, filled, barH, _paints.SharedFill);
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
        _paints.SharedFill.Color = new SKColor(colonyColor.Red, colonyColor.Green, colonyColor.Blue, 70);
        canvas.DrawPath(fillPath, _paints.SharedFill);

        _paints.SharedStroke.Color = colony.CachedSkColor;
        _paints.SharedStroke.StrokeWidth = 2f;
        _paints.SharedStroke.IsAntialias = true;
        canvas.DrawPath(linePath, _paints.SharedStroke);

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
        _paints.SharedStroke.Color = _foodSkColor;
        _paints.SharedStroke.StrokeWidth = 1.5f;
        canvas.DrawPath(foodLinePath, _paints.SharedStroke);

        _paints.SharedStroke.IsAntialias = false;
        _paints.SharedStroke.StrokeWidth = 1f;
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

        canvas.Save();
        _camera.Apply(canvas);
        _worldRenderer.DrawBase(canvas);

        if (_showPheromones)
        {
            _overlayRenderer.Draw(canvas, ClientSize.Width, ClientSize.Height);
        }

        _worldRenderer.DrawFoodNestsAndGridLines(canvas);

        IReadOnlyList<Colony> colonies = _world.Colonies;
        long antStartTicks = Stopwatch.GetTimestamp();
        if (_antsRenderer.DrawAllColonies(canvas, colonies))
        {
            long antEndTicks = Stopwatch.GetTimestamp();
            _hudRenderer.ReportAntStageTicks(antEndTicks - antStartTicks);
        }

        _placement.DrawGhost(canvas, _world);

        _selection.DrawOverlay(canvas, ClientSize.Width, ClientSize.Height);
        canvas.Restore();
        _selection.DrawInfoPanel(canvas);

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

        _hudRenderer.Draw(canvas);
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
