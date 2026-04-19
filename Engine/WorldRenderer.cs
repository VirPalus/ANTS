namespace ANTS;

using System.Collections.Generic;
using SkiaSharp;

/// <summary>
/// Encapsulates world-layer rendering (base/walls/grid-lines, food cells,
/// colony nests) via cached SKPictures. Rebuilds are triggered explicitly
/// (Rebuild, RebuildGrid) on known mutations (world load, map reload) and
/// implicitly inside DrawWorld via version/count-mismatch checks for food
/// and nests (matches legacy Engine paint-path semantics).
///
/// World is accessed via Func&lt;World&gt; getter so that map reload in
/// Engine.OnStartOverlayPick (which reassigns Engine._world) stays
/// transparent to this renderer (InputRouter-consistent pattern).
/// Camera is referenced only to read Zoom for the grid-lines visibility
/// gate inside DrawWorld. UiTheme is a static class and consumed directly.
/// </summary>
public sealed class WorldRenderer : IDisposable
{
    private const int CellSize = 16;
    private const int BorderThickness = 16;

    private readonly Func<World> _worldGetter;
    private readonly Camera _camera;
    private readonly SKColor _foodSkColor;
    private readonly FrameProfiler _profiler;
    private readonly List<IDisposable> _ownedDisposables = new List<IDisposable>();

    private SKPicture _gridPicture = null!;
    private SKPicture? _gridLinesPicture;
    private SKPicture? _foodPicture;
    private int _foodPictureCachedVersion = -1;
    private SKPicture? _nestsPicture;
    private int _nestsPictureCachedCount = -1;
    private SKPath _nestPath;

    public WorldRenderer(Func<World> worldGetter, Camera camera, SKColor foodSkColor, FrameProfiler profiler)
    {
        _worldGetter = worldGetter;
        _camera = camera;
        _foodSkColor = foodSkColor;
        _profiler = profiler;
        _nestPath = Own(new SKPath());
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

    /// <summary>Rebuild all three cached pictures (grid/walls/grid-lines,
    /// food, nests). Use on world load / map reload.</summary>
    public void Rebuild()
    {
        RebuildGrid();
        RecordFoodPicture();
        RecordNestsPicture();
    }

    /// <summary>Rebuild only the grid picture (base + walls + grid-lines).
    /// Use when only wall/grid layout changed.</summary>
    public void RebuildGrid()
    {
        RecordGridPicture();
    }

    /// <summary>Draw the base grid picture (border frame + world background
    /// + walls) onto the given canvas. Canvas is expected to already have the
    /// camera transform applied. Caller is responsible for drawing any
    /// intermediate layers (e.g. pheromone overlay) between DrawBase and
    /// DrawFoodNestsAndGridLines.</summary>
    public void DrawBase(SKCanvas canvas)
    {
        _profiler.AccumulatePhaseBegin(ProfilePhase.GridDraw);
        canvas.DrawPicture(_gridPicture);
        _profiler.AccumulatePhaseEnd(ProfilePhase.GridDraw);
    }

    /// <summary>Draw food cells, nest colonies, and grid lines onto the given
    /// canvas. Auto-rebuilds food/nests on version/count mismatch. Applies the
    /// grid-lines zoom-gate (Zoom &gt;= 0.5f).</summary>
    public void DrawFoodNestsAndGridLines(SKCanvas canvas)
    {
        World world = _worldGetter();

        _profiler.BeginPhase(ProfilePhase.FoodDraw);
        if (world.FoodVersion != _foodPictureCachedVersion)
        {
            RecordFoodPicture();
        }
        if (_foodPicture != null)
        {
            canvas.DrawPicture(_foodPicture);
        }
        _profiler.EndPhase(ProfilePhase.FoodDraw);

        _profiler.BeginPhase(ProfilePhase.NestsDraw);
        int colonyCount = world.Colonies.Count;
        if (colonyCount != _nestsPictureCachedCount)
        {
            RecordNestsPicture();
        }
        if (_nestsPicture != null)
        {
            canvas.DrawPicture(_nestsPicture);
        }
        _profiler.EndPhase(ProfilePhase.NestsDraw);

        _profiler.AccumulatePhaseBegin(ProfilePhase.GridDraw);
        if (_gridLinesPicture != null && _camera.Zoom >= 0.5f)
        {
            canvas.DrawPicture(_gridLinesPicture);
        }
        _profiler.AccumulatePhaseEnd(ProfilePhase.GridDraw);
    }

    private void RecordGridPicture()
    {
        // perf-rule-5/8 exempt: all SK* allocs below run inside SKPictureRecorder (one-time per dirty rebuild)
        World world = _worldGetter();
        int gridWidth = world.Width * CellSize;
        int gridHeight = world.Height * CellSize;
        const int Margin = 16;
        SKRect cullRect = new SKRect(-Margin, -Margin, gridWidth + Margin, gridHeight + Margin);

        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas recordingCanvas = recorder.BeginRecording(cullRect);
        using (SKPaint basePaint = new SKPaint())
        {
            basePaint.Style = SKPaintStyle.Fill;
            basePaint.IsAntialias = true;
            basePaint.Color = UiTheme.WallColor;

            const float cornerRadius = 16f;
            SKRect baseRect = new SKRect(
                -BorderThickness, -BorderThickness,
                gridWidth + BorderThickness, gridHeight + BorderThickness);
            using SKRoundRect baseRr = new SKRoundRect(baseRect, cornerRadius, cornerRadius);
            recordingCanvas.DrawRoundRect(baseRr, basePaint);
        }

        using (SKPaint worldBgPaint = new SKPaint())
        {
            worldBgPaint.Style = SKPaintStyle.Fill;
            worldBgPaint.IsAntialias = false;
            worldBgPaint.Color = UiTheme.BgWorld;
            recordingCanvas.DrawRect(0, 0, gridWidth, gridHeight, worldBgPaint);
        }

        {
            SKPictureRecorder gridLinesRecorder = new SKPictureRecorder();
            SKCanvas glCanvas = gridLinesRecorder.BeginRecording(cullRect);

            using (SKPaint linePaint = new SKPaint())
            {
                linePaint.Style = SKPaintStyle.Stroke;
                linePaint.IsAntialias = false;
                linePaint.Color = UiTheme.GridLine;
                linePaint.StrokeWidth = 0;

                using (SKPath linePath = new SKPath())
                {
                    int w = world.Width;
                    int h = world.Height;
                    for (int x = 1; x < w; x++)
                    {
                        float px = x * CellSize;
                        linePath.MoveTo(px, 0);
                        linePath.LineTo(px, gridHeight);
                    }
                    for (int y = 1; y < h; y++)
                    {
                        float py = y * CellSize;
                        linePath.MoveTo(0, py);
                        linePath.LineTo(gridWidth, py);
                    }
                    if (linePath.PointCount > 0)
                    {
                        glCanvas.DrawPath(linePath, linePaint);
                    }
                }
            }

            Replace(ref _gridLinesPicture!, gridLinesRecorder.EndRecording());
            gridLinesRecorder.Dispose();
        }

        using (SKPaint wallPaint = new SKPaint())
        {
            wallPaint.Style = SKPaintStyle.Fill;
            wallPaint.IsAntialias = false;
            wallPaint.Color = UiTheme.WallColor;

            using (SKPath wallPath = new SKPath())
            {
                int w = world.Width;
                int h = world.Height;
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        if (world.GetCell(x, y) == CellType.Wall)
                        {
                            wallPath.AddRect(new SKRect(
                                x * CellSize, y * CellSize,
                                (x + 1) * CellSize, (y + 1) * CellSize));
                        }
                    }
                }
                if (wallPath.PointCount > 0)
                {
                    recordingCanvas.DrawPath(wallPath, wallPaint);
                }
            }
        }

        Replace(ref _gridPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    private void RecordFoodPicture()
    {
        // perf-rule-5/8 exempt: all SK* allocs below run inside SKPictureRecorder (one-time per dirty rebuild)
        World world = _worldGetter();
        int foodCount = world.FoodCount;
        _foodPictureCachedVersion = world.FoodVersion;

        if (foodCount == 0)
        {
            if (_foodPicture != null) { Replace(ref _foodPicture!, null!); }
            return;
        }

        int gridWidth = world.Width * CellSize;
        int gridHeight = world.Height * CellSize;
        SKRect cullRect = new SKRect(0, 0, gridWidth, gridHeight);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        using (SKPaint foodPaint = new SKPaint())
        {
            foodPaint.Style = SKPaintStyle.Fill;
            foodPaint.IsAntialias = false;

            Point[] foodCells = world.FoodCells;
            for (int i = 0; i < foodCount; i++)
            {
                int cellX = foodCells[i].X;
                int cellY = foodCells[i].Y;
                float amount = world.GetFoodAmount(cellX, cellY);
                int step = (int)(amount * 10f);
                if (step > 4) step = 4;
                byte alpha = (byte)(step * 64);
                if (alpha < 64) alpha = 64;
                if (step == 4) alpha = 255;
                foodPaint.Color = _foodSkColor.WithAlpha(alpha);
                rc.DrawRect(cellX * CellSize, cellY * CellSize, CellSize, CellSize, foodPaint);
            }
        }

        Replace(ref _foodPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    private void RecordNestsPicture()
    {
        // perf-rule-5/8 exempt: all SK* allocs below run inside SKPictureRecorder (one-time per dirty rebuild)
        World world = _worldGetter();
        IReadOnlyList<Colony> colonies = world.Colonies;
        int colonyCount = colonies.Count;
        _nestsPictureCachedCount = colonyCount;

        if (colonyCount == 0)
        {
            if (_nestsPicture != null) { Replace(ref _nestsPicture!, null!); }
            return;
        }

        int gridWidth = world.Width * CellSize;
        int gridHeight = world.Height * CellSize;
        SKRect cullRect = new SKRect(0, 0, gridWidth, gridHeight);
        SKPictureRecorder recorder = new SKPictureRecorder();
        SKCanvas rc = recorder.BeginRecording(cullRect);

        using (SKPaint nestFill = new SKPaint())
        {
            nestFill.Style = SKPaintStyle.Fill;
            nestFill.IsAntialias = false;

            for (int i = 0; i < colonyCount; i++)
            {
                Colony colony = colonies[i];
                nestFill.Color = colony.CachedSkColor;

                _nestPath.Reset();
                for (int dy = -World.NestRadius; dy <= World.NestRadius; dy++)
                {
                    for (int dx = -World.NestRadius; dx <= World.NestRadius; dx++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) > World.NestRadius) continue;
                        int cellX = colony.NestX + dx;
                        int cellY = colony.NestY + dy;
                        _nestPath.AddRect(new SKRect(
                            cellX * CellSize, cellY * CellSize,
                            (cellX + 1) * CellSize, (cellY + 1) * CellSize));
                    }
                }
                rc.DrawPath(_nestPath, nestFill);
            }
        }

        Replace(ref _nestsPicture!, recorder.EndRecording());
        recorder.Dispose();
    }

    public void Dispose()
    {
        for (int i = _ownedDisposables.Count - 1; i >= 0; i--)
        {
            _ownedDisposables[i].Dispose();
        }
        _ownedDisposables.Clear();
    }
}
