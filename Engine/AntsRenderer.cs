namespace ANTS;

using System.Collections.Generic;
using System.Runtime.InteropServices;
using SkiaSharp;

/// <summary>
/// Encapsulates per-colony ant batch rendering: sprite DrawAtlas path
/// (Zoom &gt;= 0.5f) and dot fallback (Zoom &lt; 0.5f). Also owns the
/// per-colony color-tint ColorFilter cache (applied to
/// PaintCache.AntPaint on demand).
///
/// Colonies are passed per-call via IReadOnlyList&lt;Colony&gt; — no World
/// reference held (consistent with PlacementController pattern, robust
/// against map reload).
///
/// NOTE: this class is exclusively responsible for mutating
/// PaintCache.AntPaint.ColorFilter. It also transiently mutates
/// PaintCache.SharedFill (Color/StrokeCap/StrokeWidth) during dot draw
/// and resets StrokeCap at the end of DrawAntDots — see REFACTOR_BACKLOG.md
/// for the shared-paint cross-mutation tech-debt entry.
/// </summary>
public sealed class AntsRenderer : IDisposable
{
    private const int CellSize = 16;

    private const float FoodGreenR = 34f / 255f;
    private const float FoodGreenG = 197f / 255f;
    private const float FoodGreenB = 94f / 255f;

    private readonly PaintCache _paints;
    private readonly Camera _camera;
    private readonly List<IDisposable> _ownedDisposables = new List<IDisposable>();

    private SKRect[] _antBodySprites = Array.Empty<SKRect>();
    private SKRotationScaleMatrix[] _antBodyTransforms = Array.Empty<SKRotationScaleMatrix>();
    private SKPoint[] _antDotPoints = Array.Empty<SKPoint>();
    private SKColorFilter? _antBodyColorFilter;
    private uint _antBodyColorFilterCachedColor;

    public AntsRenderer(PaintCache paints, Camera camera)
    {
        _paints = paints;
        _camera = camera;
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

    /// <summary>Draw all colonies' ants onto the given canvas. Returns true
    /// iff at least one ant was actually drawn (so callers can skip timing
    /// measurements on empty frames — matches legacy Engine paint semantics).
    /// Applies the Zoom &lt; 0.5f dots-vs-sprites threshold internally.</summary>
    public bool DrawAllColonies(SKCanvas canvas, IReadOnlyList<Colony> colonies)
    {
        int colonyCount = colonies.Count;
        if (colonyCount == 0)
        {
            return false;
        }

        bool hasAnyAnts = false;
        for (int i = 0; i < colonyCount; i++)
        {
            if (colonies[i].AntsList.Count > 0)
            {
                hasAnyAnts = true;
                break;
            }
        }
        if (!hasAnyAnts)
        {
            return false;
        }

        bool drawAsDots = _camera.Zoom < 0.5f;
        for (int i = 0; i < colonyCount; i++)
        {
            Colony colony = colonies[i];
            if (drawAsDots)
            {
                DrawAntDots(canvas, colony);
            }
            else
            {
                DrawAnts(canvas, colony);
            }
        }
        return true;
    }

    private void DrawAnts(SKCanvas canvas, Colony colony)
    {
        List<Ant> antsList = colony.AntsList;
        int antCount = antsList.Count;
        if (antCount == 0)
        {
            return;
        }

        SKColor colonyColor = colony.CachedSkColor;
        EnsureAntBuffersCapacity(antCount);

        Span<Ant> antsSpan = CollectionsMarshal.AsSpan(antsList);
        SKRect[] frameRects = AntRenderer.FrameSpriteRects;
        SKRect[] frameRectsFood = AntRenderer.FrameSpriteRectsWithFood;
        SKRect[] bodySprites = _antBodySprites;
        SKRotationScaleMatrix[] bodyTransforms = _antBodyTransforms;

        const float invSup = AntRenderer.AtlasInverseSupersample;
        const float anchor = AntRenderer.AtlasAnchor;

        for (int i = 0; i < antCount; i++)
        {
            Ant ant = antsSpan[i];
            float centerX = ant.X * CellSize;
            float centerY = ant.Y * CellSize;

            if (ant.LungeTimer > 0f)
            {
                float t = ant.LungeTimer / CombatTuning.LungeDuration;
                float offset = t > 0.5f ? (1f - t) * 2f : t * 2f;
                float lungePixels = offset * CombatTuning.LungeDistance * CellSize;
                centerX += ant.LungeDirX * lungePixels;
                centerY += ant.LungeDirY * lungePixels;
            }

            float heading = ant.Heading;
            float headingCos = (float)Math.Cos(heading);
            float headingSin = (float)Math.Sin(heading);
            float scale = invSup * ant.Role.VisualScale;
            float scos = headingCos * scale;
            float ssin = headingSin * scale;
            float tx = centerX - scos * anchor + ssin * anchor;
            float ty = centerY - ssin * anchor - scos * anchor;
            bodyTransforms[i] = new SKRotationScaleMatrix(scos, ssin, tx, ty);

            int frameIndex = AntRenderer.GetFrameIndex(ant.StridePhase);
            bool hasFood = ant.CarryingFood > 0;
            bodySprites[i] = hasFood ? frameRectsFood[frameIndex] : frameRects[frameIndex];
        }

        ApplyBodyTint(colonyColor);
        canvas.DrawAtlas(AntRenderer.BodyAtlasImage, _antBodySprites, _antBodyTransforms, _paints.AntPaint);
    }

    private void DrawAntDots(SKCanvas canvas, Colony colony)
    {
        List<Ant> antsList = colony.AntsList;
        int antCount = antsList.Count;
        if (antCount == 0) return;

        if (_antDotPoints.Length != antCount)
        {
            _antDotPoints = new SKPoint[antCount];
        }

        Span<Ant> antsSpan = CollectionsMarshal.AsSpan(antsList);
        for (int i = 0; i < antCount; i++)
        {
            _antDotPoints[i] = new SKPoint(antsSpan[i].X * CellSize, antsSpan[i].Y * CellSize);
        }

        float dotDiameter = Math.Max(3f, CellSize * 0.6f);

        _paints.SharedFill.Color = colony.CachedSkColor;
        _paints.SharedFill.StrokeCap = SKStrokeCap.Round;
        _paints.SharedFill.StrokeWidth = dotDiameter;
        canvas.DrawPoints(SKPointMode.Points, _antDotPoints, _paints.SharedFill);
        _paints.SharedFill.StrokeCap = SKStrokeCap.Butt;
    }

    private void ApplyBodyTint(SKColor colonyColor)
    {
        uint packedColor = (uint)colonyColor;
        if (_antBodyColorFilter != null && _antBodyColorFilterCachedColor == packedColor)
        {
            return;
        }

        float colR = colonyColor.Red / 255f;
        float colG = colonyColor.Green / 255f;
        float colB = colonyColor.Blue / 255f;

        float[] matrix = new float[]
        {
            FoodGreenR, colR - FoodGreenR, 0f, 0f, 0f,
            FoodGreenG, colG - FoodGreenG, 0f, 0f, 0f,
            FoodGreenB, colB - FoodGreenB, 0f, 0f, 0f,
            0f,         0f,                0f, 1f, 0f
        };

        Replace(ref _antBodyColorFilter, SKColorFilter.CreateColorMatrix(matrix));
        _antBodyColorFilterCachedColor = packedColor;
        _paints.AntPaint.ColorFilter = _antBodyColorFilter;
    }

    private void EnsureAntBuffersCapacity(int antCount)
    {
        if (_antBodyTransforms.Length != antCount)
        {
            _antBodyTransforms = new SKRotationScaleMatrix[antCount];
        }
        if (_antBodySprites.Length != antCount)
        {
            _antBodySprites = new SKRect[antCount];
        }
    }

    public void Dispose()
    {
        for (int i = _ownedDisposables.Count - 1; i >= 0; i--)
        {
            _ownedDisposables[i].Dispose();
        }
        _ownedDisposables.Clear();
        _antBodyColorFilter = null;
    }
}
