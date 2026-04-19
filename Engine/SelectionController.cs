namespace ANTS;

using System.Collections.Generic;
using SkiaSharp;

/// <summary>
/// Encapsulates ant selection state and related rendering:
/// - Click-based selection via TrySelect.
/// - Follow-camera tracking (mutates Camera in DrawOverlay — tech-debt).
/// - Vision cone + sensor circle + heading arrow overlay.
/// - Info panel rendering (per-frame SKPaint allocs — tech-debt).
/// World is passed per-call to avoid stale-world-reference bugs on map reload
/// (lesson from fase-4.2).
/// </summary>
public sealed class SelectionController
{
    private const int CellSize = 16;
    private const float AntClickRadiusCells = 1.5f;

    private readonly Camera _camera;
    private readonly PaintCache _paints;

    private Ant? _selectedAnt;
    private Colony? _selectedAntColony;
    private bool _followSelectedAnt;

    public SelectionController(Camera camera, PaintCache paints)
    {
        _camera = camera;
        _paints = paints;
    }

    public bool HasSelection => _selectedAnt != null;

    public void Clear()
    {
        _selectedAnt = null;
        _selectedAntColony = null;
        _followSelectedAnt = false;
    }

    public void TrySelect(int screenX, int screenY, World world)
    {
        _camera.ScreenToWorld(screenX, screenY, out float wx, out float wy);
        float worldX = wx / CellSize;
        float worldY = wy / CellSize;

        Ant? bestAnt = null;
        Colony? bestColony = null;
        float bestDistSq = AntClickRadiusCells * AntClickRadiusCells;

        IReadOnlyList<Colony> colonies = world.Colonies;
        int colonyCount = colonies.Count;
        for (int c = 0; c < colonyCount; c++)
        {
            Colony colony = colonies[c];
            IReadOnlyList<Ant> ants = colony.Ants;
            int antCount = ants.Count;
            for (int a = 0; a < antCount; a++)
            {
                Ant ant = ants[a];
                if (ant.IsDead) continue;
                float dx = ant.X - worldX;
                float dy = ant.Y - worldY;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestAnt = ant;
                    bestColony = colony;
                }
            }
        }

        _selectedAnt = bestAnt;
        _selectedAntColony = bestColony;
        _followSelectedAnt = bestAnt != null;
    }

    public void DrawOverlay(SKCanvas canvas, int clientWidth, int clientHeight)
    {
        Ant? ant = _selectedAnt;
        Colony? colony = _selectedAntColony;
        if (ant == null || colony == null || ant.IsDead)
        {
            _selectedAnt = null;
            _selectedAntColony = null;
            return;
        }

        float cx = ant.X * CellSize;
        float cy = ant.Y * CellSize;

        if (_followSelectedAnt)
        {
            _camera.ScreenToWorld(clientWidth / 2, clientHeight / 2, out float camCx, out float camCy);
            float targetX = cx;
            float targetY = cy;
            float lerpedX = camCx + (targetX - camCx) * 0.08f;
            float lerpedY = camCy + (targetY - camCy) * 0.08f;
            float diffX = lerpedX - camCx;
            float diffY = lerpedY - camCy;
            _camera.OffsetX -= diffX * _camera.Zoom;
            _camera.OffsetY -= diffY * _camera.Zoom;
        }

        AntRole role = ant.Role;
        SKColor colonyColor = colony.CachedSkColor;
        float heading = ant.Heading;
        _paints.SharedFill.IsAntialias = true;
        float smellRadius = role.SensorDistance * CellSize;
        _paints.SharedFill.Style = SKPaintStyle.Stroke;
        _paints.SharedFill.StrokeWidth = 1.5f / _camera.Zoom;
        _paints.SharedFill.Color = new SKColor(colonyColor.Red, colonyColor.Green, colonyColor.Blue, 70);
        canvas.DrawCircle(cx, cy, smellRadius, _paints.SharedFill);
        _paints.SharedFill.Style = SKPaintStyle.Fill;
        float visionDist = role.VisionRange * CellSize;
        float sensorAngle = role.SensorAngleRad;

        if (visionDist > 0)
        {
            DrawVisionCone(canvas, cx, cy, heading, sensorAngle, visionDist, colonyColor);
        }

        float arrowLen = role.VisionRange * CellSize;
        float ax = cx + (float)Math.Cos(heading) * arrowLen;
        float ay = cy + (float)Math.Sin(heading) * arrowLen;
        _paints.SharedFill.Style = SKPaintStyle.Stroke;
        _paints.SharedFill.StrokeWidth = 2f / _camera.Zoom;
        _paints.SharedFill.Color = new SKColor(255, 255, 255, 200);
        canvas.DrawLine(cx, cy, ax, ay, _paints.SharedFill);
        float arrowHeadLen = 0.8f * CellSize;
        float arrowSpread = 0.4f;
        canvas.DrawLine(ax, ay,
            ax - (float)Math.Cos(heading - arrowSpread) * arrowHeadLen,
            ay - (float)Math.Sin(heading - arrowSpread) * arrowHeadLen, _paints.SharedFill);
        canvas.DrawLine(ax, ay,
            ax - (float)Math.Cos(heading + arrowSpread) * arrowHeadLen,
            ay - (float)Math.Sin(heading + arrowSpread) * arrowHeadLen, _paints.SharedFill);
        _paints.SharedFill.Style = SKPaintStyle.Fill;
        _paints.SharedFill.StrokeWidth = 0f;
        _paints.SharedFill.StrokeCap = SKStrokeCap.Butt;
        _paints.SharedFill.IsAntialias = false;
    }

    private void DrawVisionCone(SKCanvas canvas, float cx, float cy, float heading, float halfAngle, float radius, SKColor colonyColor)
    {

        float startAngle = heading - halfAngle;
        float endAngle = heading + halfAngle;
        const int arcSegments = 20;
        float angleStep = (endAngle - startAngle) / arcSegments;

        using SKPath conePath = new SKPath();
        conePath.MoveTo(cx, cy);

        for (int i = 0; i <= arcSegments; i++)
        {
            float a = startAngle + angleStep * i;
            float px = cx + (float)Math.Cos(a) * radius;
            float py = cy + (float)Math.Sin(a) * radius;
            conePath.LineTo(px, py);
        }

        conePath.Close();

        _paints.SharedFill.Style = SKPaintStyle.Fill;
        _paints.SharedFill.Color = new SKColor(255, 255, 255, 16);
        canvas.DrawPath(conePath, _paints.SharedFill);
        _paints.SharedFill.Style = SKPaintStyle.Stroke;
        _paints.SharedFill.StrokeWidth = 1.5f / _camera.Zoom;
        _paints.SharedFill.Color = new SKColor(255, 255, 255, 80);
        canvas.DrawPath(conePath, _paints.SharedFill);

        _paints.SharedFill.Style = SKPaintStyle.Fill;
    }

    public void DrawInfoPanel(SKCanvas canvas)
    {
        Ant? ant = _selectedAnt;
        Colony? colony = _selectedAntColony;
        if (ant == null || colony == null) return;

        float panelW = 220f;
        float panelH = 230f;
        float panelX = 8f;
        float panelY = UiTopBar.BarHeight + 8f + 84f + 8f;
        using SKPaint bgPaint = new SKPaint();
        bgPaint.Color = new SKColor(30, 30, 36, 230);
        bgPaint.IsAntialias = true;
        SKRect panelRect = new SKRect(panelX, panelY, panelX + panelW, panelY + panelH);
        canvas.DrawRoundRect(panelRect, 8, 8, bgPaint);
        bgPaint.Style = SKPaintStyle.Stroke;
        bgPaint.StrokeWidth = 2f;
        bgPaint.Color = colony.CachedSkColor;
        canvas.DrawRoundRect(panelRect, 8, 8, bgPaint);

        using SKPaint textPaint = new SKPaint();
        textPaint.IsAntialias = true;
        textPaint.Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        float lineH = 18f;
        float x = panelX + 10f;
        float valX = panelX + 130f;
        float y = panelY + 22f;
        textPaint.TextSize = 14f;
        textPaint.Color = colony.CachedSkColor;
        canvas.DrawText(ant.Role.RoleName, x, y, textPaint);
        y += lineH + 4f;
        textPaint.TextSize = 12f;
        textPaint.Color = new SKColor(200, 200, 210);

        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Goal", ant.Goal.ToString());
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Health", $"{ant.Health} / {Ant.DefaultHealth}");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Food", ant.CarryingFood > 0 ? $"{ant.CarryingFood}" : "-");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Speed", $"{ant.Role.MaxSpeed:F1} c/s");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Vision", $"{ant.Role.VisionRange:F0} cells");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "VisionStr", $"{ant.VisionStrength:P0}");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Engaged", ant.EngagementTimer > 0 ? $"{ant.EngagementTimer:F2}s" : "-");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Age", $"{ant.Age:F0}s");
        DrawInfoLine(canvas, textPaint, x, valX, ref y, lineH, "Autonomy", $"{ant.InternalClock:F0} / {ant.Role.AutonomyMax:F0}");

        y += 6f;
        textPaint.TextSize = 10f;
        textPaint.Color = new SKColor(255, 255, 255, 120);
        float lx = x;
        canvas.DrawText("VisionCone", lx, y, textPaint);
        lx += textPaint.MeasureText("VisionCone") + 6f;
        textPaint.Color = new SKColor(255, 255, 255, 200);
        canvas.DrawText("HeadingArrow", lx, y, textPaint);
        lx += textPaint.MeasureText("HeadingArrow") + 6f;
        textPaint.Color = new SKColor(255, 80, 80, 200);
        canvas.DrawText("SensorCircle", lx, y, textPaint);
    }

    private static void DrawInfoLine(SKCanvas canvas, SKPaint textPaint, float x, float valX, ref float y, float lineH, string label, string value)
    {
        SKColor saved = textPaint.Color;
        textPaint.Color = new SKColor(140, 140, 155);
        canvas.DrawText(label, x, y, textPaint);
        textPaint.Color = new SKColor(220, 220, 230);
        canvas.DrawText(value, valX, y, textPaint);
        textPaint.Color = saved;
        y += lineH;
    }
}
