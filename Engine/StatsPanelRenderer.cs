namespace ANTS;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using SkiaSharp;

/// <summary>
/// Encapsulates the right-side colony stats panel: per-colony cards
/// with health bar, ants/food header line, population+food line graph,
/// role breakdown bars (Scouts/Foragers/Defenders/Attackers), and
/// queen intent + defense/offense signals. Dead colonies show a
/// translucent overlay with death-reason + elapsed time.
///
/// Owns the stats SKPicture cache plus an independent rebuild
/// throttle stopwatch. Rebuild cadence is decoupled from the HUD
/// (fase-4.10): the HUD rebuilds at 20 Hz (50 ms), stats rebuilds
/// at 4 Hz (StatsUpdateIntervalMs = 250 ms). Rationale: ColonyStats
/// samples at 1 Hz, so 4 Hz is 4x the data rate and well above the
/// ~5-10 Hz threshold at which the human eye perceives updates as
/// live.
///
/// World data is accessed via a Func&lt;World&gt; getter so the
/// renderer is robust against map reload (consistent with
/// WorldRenderer / OverlayRenderer pattern).
/// </summary>
public sealed class StatsPanelRenderer : IDisposable
{
    private const int StatsUpdateIntervalMs = 250;

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

    private readonly PaintCache _paints;
    private readonly Func<World> _worldGetter;
    private readonly SKColor _foodSkColor;
    private readonly List<IDisposable> _ownedDisposables = new List<IDisposable>();

    private SKPicture? _statsPicture;
    private readonly Stopwatch _statsStopwatch = new Stopwatch();

    public StatsPanelRenderer(PaintCache paints, Func<World> worldGetter, SKColor foodSkColor)
    {
        _paints = paints;
        _worldGetter = worldGetter;
        _foodSkColor = foodSkColor;
    }

    /// <summary>
    /// Starts the rebuild-throttle stopwatch and records the initial
    /// stats picture so the first frame has something to draw.
    /// </summary>
    public void Start(int clientWidth, int clientHeight)
    {
        _statsStopwatch.Start();
        RecordStatsPicture(clientWidth, clientHeight);
    }

    /// <summary>
    /// If the rebuild interval has elapsed, rebuilds the stats picture
    /// and restarts the rebuild stopwatch. Returns true when a
    /// rebuild happened.
    /// </summary>
    public bool MaybeRebuild(int clientWidth, int clientHeight)
    {
        if (_statsStopwatch.ElapsedMilliseconds >= StatsUpdateIntervalMs)
        {
            RecordStatsPicture(clientWidth, clientHeight);
            _statsStopwatch.Restart();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Forces an immediate rebuild of the stats picture (bypasses the
    /// throttle) and resets the rebuild stopwatch. Intended for
    /// placement-triggered refresh or map-reload flows — currently
    /// exposed but not invoked (forward-looking).
    /// </summary>
    public void RebuildNow(int clientWidth, int clientHeight)
    {
        RecordStatsPicture(clientWidth, clientHeight);
        _statsStopwatch.Restart();
    }

    /// <summary>
    /// Paints the cached stats picture to the canvas.
    /// </summary>
    public void Draw(SKCanvas canvas)
    {
        if (_statsPicture != null)
        {
            canvas.DrawPicture(_statsPicture);
        }
    }

    private void RecordStatsPicture(int clientWidth, int clientHeight)
    {
        // perf-rule-5/8 exempt: all SK* allocs below run inside SKPictureRecorder (one-time per dirty rebuild)

        SKRect cullRect = new SKRect(0, 0, clientWidth + 20, clientHeight + 20);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        DrawStatsPanel(rc, clientWidth);

        Replace(ref _statsPicture, recorder.EndRecording());
        recorder.Dispose();
    }

    private void DrawStatsPanel(SKCanvas canvas, int clientWidth)
    {
        World world = _worldGetter();
        IReadOnlyList<Colony> colonies = world.Colonies;
        IReadOnlyList<Colony> dead = world.DeadColonies;
        int aliveCount = colonies.Count;
        int deadCount = dead.Count;
        if (aliveCount + deadCount == 0)
        {
            return;
        }

        int panelX = clientWidth - StatsPanelWidth - 12;
        int panelY = UiTopBar.BarHeight + 8;
        int row = 0;

        for (int i = 0; i < aliveCount; i++)
        {
            Colony colony = colonies[i];
            int cardY = panelY + row * (StatsCardHeight + StatsCardSpacing);
            DrawStatsCard(canvas, colony, panelX, cardY, false, world);
            row++;
        }

        for (int i = 0; i < deadCount; i++)
        {
            Colony colony = dead[i];
            int cardY = panelY + row * (StatsCardHeight + StatsCardSpacing);
            DrawStatsCard(canvas, colony, panelX, cardY, true, world);
            row++;
        }
    }

    private void DrawStatsCard(SKCanvas canvas, Colony colony, int x, int y, bool isDead, World world)
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

            float elapsed = world.SimulationTime - colony.DeathTime;
            if (elapsed < 0f) elapsed = 0f;
            string label = "DEAD - " + colony.DeathReason + " (" + ((int)elapsed) + "s ago)";
            _paints.SharedText.Color = UiTheme.TextStrong;
            float tw = _paints.SharedText.MeasureText(label);
            canvas.DrawText(label, x + (StatsPanelWidth - tw) / 2f, y + StatsCardHeight / 2f - _paints.SharedText.FontMetrics.Ascent / 2f, _paints.SharedText);
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

    private void Replace<T>(ref T? field, T? newValue) where T : class, IDisposable
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

    public void Dispose()
    {
        for (int i = _ownedDisposables.Count - 1; i >= 0; i--)
        {
            _ownedDisposables[i].Dispose();
        }
        _ownedDisposables.Clear();
        _statsPicture = null;
    }
}
