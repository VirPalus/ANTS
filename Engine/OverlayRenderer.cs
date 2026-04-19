namespace ANTS;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SkiaSharp;

/// <summary>
/// Pheromone overlay rendering. Extracted from Engine.cs in fase-4.8.
/// Preserves early-exit and direct-array-access optimizations from fase-6.6.
/// </summary>
public sealed class OverlayRenderer : IDisposable
{
    private const int CellSize = 16;
    private const float PheromoneOverlayCutoff = 0.03f;
    private const float PheromoneOverlayMaxAlpha = 160f;

    private static readonly SKColor FoodPheromoneColor = new SKColor(34, 197, 94);

    private readonly PaintCache _paints;
    private readonly Camera _camera;
    private readonly Func<World> _worldGetter;
    private readonly FrameProfiler _profiler;

    private readonly List<IDisposable> _ownedDisposables = new();

    private SKBitmap? _pheromoneHomeBitmap;
    private SKBitmap? _pheromoneFoodBitmap;
    private byte[]? _pheromoneHomeBuffer;
    private byte[]? _pheromoneFoodBuffer;
    private float[][,]? _overlayHomeArrays;
    private float[][,]? _overlayFoodArrays;
    private SKColor[]? _overlayColonyColors;

    public OverlayRenderer(PaintCache paints, Camera camera, Func<World> worldGetter, FrameProfiler profiler)
    {
        _paints = paints;
        _camera = camera;
        _worldGetter = worldGetter;
        _profiler = profiler;
    }

    public void Draw(SKCanvas canvas, int clientWidth, int clientHeight)
    {
        World world = _worldGetter();
        IReadOnlyList<Colony> colonies = world.Colonies;
        int colonyCount = colonies.Count;
        int worldWidth = world.Width;
        int worldHeight = world.Height;

        // === FASE 6.6 STAP A: early-exit when no renderable trails (O(1) per colony) ===
        bool anyTrail = false;
        for (int ci = 0; ci < colonyCount; ci++)
        {
            if (colonies[ci].PheromoneGrid.HasAnyRenderableTrail())
            {
                anyTrail = true;
                break;
            }
        }
        if (!anyTrail)
        {
            return;
        }
        // === end early-exit guard ================================================

        if (_pheromoneHomeBitmap == null
            || _pheromoneHomeBitmap.Width != worldWidth
            || _pheromoneHomeBitmap.Height != worldHeight)
        {
            SKImageInfo bmpInfo = new SKImageInfo(worldWidth, worldHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            Replace(ref _pheromoneHomeBitmap, new SKBitmap(bmpInfo));
            Replace(ref _pheromoneFoodBitmap, new SKBitmap(bmpInfo));
            _pheromoneHomeBuffer = new byte[_pheromoneHomeBitmap!.ByteCount];
            _pheromoneFoodBuffer = new byte[_pheromoneFoodBitmap!.ByteCount];
        }

        byte[] homeBuf = _pheromoneHomeBuffer!;
        byte[] foodBuf = _pheromoneFoodBuffer!;
        int bufSize = homeBuf.Length;
        int stride = _pheromoneHomeBitmap!.RowBytes;
        // === FASE 6.6 STAP B — hoist per-colony raw arrays + colors ==============
        // perf-rule-8 exempt: scratch arrays only grow when colony count grows (rare, once/game)
        if (_overlayHomeArrays == null || _overlayHomeArrays.Length < colonyCount)
        {
            _overlayHomeArrays   = new float[colonyCount][,];
            _overlayFoodArrays   = new float[colonyCount][,];
            _overlayColonyColors = new SKColor[colonyCount];
        }
        for (int ci = 0; ci < colonyCount; ci++)
        {
            Colony col = colonies[ci];
            _overlayHomeArrays![ci]   = col.PheromoneGrid.GetRawIntensityArray(PheromoneChannel.HomeTrail);
            _overlayFoodArrays![ci]   = col.PheromoneGrid.GetRawIntensityArray(PheromoneChannel.FoodTrail);
            _overlayColonyColors![ci] = col.CachedSkColor;
        }
        float[][,] homeArrs = _overlayHomeArrays!;
        float[][,] foodArrs = _overlayFoodArrays!;
        SKColor[]  colCols  = _overlayColonyColors!;
        // === end hoist block ==========================================================
        _profiler.BeginPhase(ProfilePhase.ArrayClear);
        Array.Clear(homeBuf, 0, bufSize);
        Array.Clear(foodBuf, 0, bufSize);
        _profiler.EndPhase(ProfilePhase.ArrayClear);

        _camera.ScreenToWorld(0, 0, out float tlx, out float tly);
        _camera.ScreenToWorld(clientWidth, clientHeight, out float brx, out float bry);
        int minCX = Math.Max(0, (int)Math.Floor(tlx / CellSize) - 1);
        int minCY = Math.Max(0, (int)Math.Floor(tly / CellSize) - 1);
        int maxCX = Math.Min(worldWidth - 1, (int)Math.Ceiling(brx / CellSize) + 1);
        int maxCY = Math.Min(worldHeight - 1, (int)Math.Ceiling(bry / CellSize) + 1);

        byte foodColorR = FoodPheromoneColor.Red;
        byte foodColorG = FoodPheromoneColor.Green;
        byte foodColorB = FoodPheromoneColor.Blue;

        _profiler.BeginPhase(ProfilePhase.InnerLoop);
        for (int x = minCX; x <= maxCX; x++)
        {
            for (int y = minCY; y <= maxCY; y++)
            {
                float Home = 0f;
                float Food = 0f;
                byte homeR = 0;
                byte homeG = 0;
                byte homeB = 0;

                for (int c = 0; c < colonyCount; c++)
                {
                    // direct array access: bypass grid.Get() (no InBounds, no switch)
                    float h = homeArrs[c][x, y];
                    if (h > Home)
                    {
                        Home = h;
                        SKColor cc = colCols[c];
                        homeR = cc.Red;
                        homeG = cc.Green;
                        homeB = cc.Blue;
                    }

                    float f = foodArrs[c][x, y];
                    if (f > Food)
                    {
                        Food = f;
                    }
                }

                int off = y * stride + x * 4;

                if (Home > PheromoneOverlayCutoff)
                {
                    byte alpha = ScaleIntensityToAlpha(Home);
                    homeBuf[off    ] = SkMulDiv255Round(homeR, alpha);
                    homeBuf[off + 1] = SkMulDiv255Round(homeG, alpha);
                    homeBuf[off + 2] = SkMulDiv255Round(homeB, alpha);
                    homeBuf[off + 3] = alpha;
                }

                if (Food > PheromoneOverlayCutoff)
                {
                    byte alpha = ScaleIntensityToAlpha(Food);
                    foodBuf[off    ] = SkMulDiv255Round(foodColorR, alpha);
                    foodBuf[off + 1] = SkMulDiv255Round(foodColorG, alpha);
                    foodBuf[off + 2] = SkMulDiv255Round(foodColorB, alpha);
                    foodBuf[off + 3] = alpha;
                }
            }
        }
        _profiler.EndPhase(ProfilePhase.InnerLoop);

        _profiler.BeginPhase(ProfilePhase.MarshalCopy);
        Marshal.Copy(homeBuf, 0, _pheromoneHomeBitmap.GetPixels(), bufSize);
        Marshal.Copy(foodBuf, 0, _pheromoneFoodBitmap!.GetPixels(), bufSize);
        _profiler.EndPhase(ProfilePhase.MarshalCopy);

        _profiler.BeginPhase(ProfilePhase.DrawBitmap);
        SKRect dest = new SKRect(0, 0, worldWidth * CellSize, worldHeight * CellSize);
        canvas.DrawBitmap(_pheromoneHomeBitmap, dest, _paints.PheromoneBitmapPaint);
        canvas.DrawBitmap(_pheromoneFoodBitmap, dest, _paints.PheromoneBitmapPaint);
        _profiler.EndPhase(ProfilePhase.DrawBitmap);
    }

    private static byte SkMulDiv255Round(byte a, byte b)
    {
        int prod = a * b + 128;
        return (byte)((prod + (prod >> 8)) >> 8);
    }

    private static byte ScaleIntensityToAlpha(float intensity)
    {
        int raw = (int)(intensity * PheromoneOverlayMaxAlpha);
        if (raw > PheromoneOverlayMaxAlpha)
        {
            raw = (int)PheromoneOverlayMaxAlpha;
        }
        return (byte)raw;
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
        _pheromoneHomeBitmap = null;
        _pheromoneFoodBitmap = null;
    }
}
