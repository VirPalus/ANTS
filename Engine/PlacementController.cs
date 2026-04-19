namespace ANTS;

using SkiaSharp;

/// <summary>
/// Encapsulates colony/food placement state and interaction:
/// - Button-triggered placement modes (Colony, Food) with cursor feedback.
/// - Click-to-place colony (with bounds check) and click-drag-to-paint food.
/// - Ghost-preview rendering (semi-transparent nest or food cell under cursor).
/// World is passed per-call to avoid stale-world-reference bugs on map reload
/// (lesson from fase-4.2). Cursor mutation is delegated via Action&lt;Cursor&gt;
/// callback to keep Form-dependency out of the controller.
/// </summary>
public sealed class PlacementController : IDisposable
{
    private const int CellSize = 16;

    private static readonly Color[] ColonyColors = new Color[]
    {
        Color.FromArgb(239, 68, 68),
        Color.FromArgb(59, 130, 246),
        Color.FromArgb(234, 179, 8),
        Color.FromArgb(249, 115, 22),
        Color.FromArgb(168, 85, 247),
        Color.FromArgb(236, 72, 153),
    };

    private readonly Camera _camera;
    private readonly PaintCache _paints;
    private readonly SKColor _foodSkColor;
    private readonly Action<Cursor> _setCursor;
    private readonly SKPath _ghostNestPath = new SKPath();

    private PlacingMode _placingMode = PlacingMode.None;
    private bool _isDrawingFood;
    private int _nextColorIndex;
    private int _mouseX;
    private int _mouseY;

    public PlacementController(Camera camera, PaintCache paints, SKColor foodSkColor, Action<Cursor> setCursor)
    {
        _camera = camera;
        _paints = paints;
        _foodSkColor = foodSkColor;
        _setCursor = setCursor;
    }

    public bool IsPlacingColony => _placingMode == PlacingMode.Colony;
    public bool IsPlacingFood => _placingMode == PlacingMode.Food;

    public void StartPlacingColony()
    {
        _placingMode = PlacingMode.Colony;
        _isDrawingFood = false;
        _setCursor(Cursors.Cross);
    }

    public void StartPlacingFood()
    {
        _placingMode = PlacingMode.Food;
        _isDrawingFood = false;
        _setCursor(Cursors.Cross);
    }

    public void Cancel()
    {
        _placingMode = PlacingMode.None;
        _isDrawingFood = false;
        _setCursor(Cursors.Default);
    }

    public void SetNextColorIndex(int value)
    {
        _nextColorIndex = value;
    }

    public void UpdateMouseCoords(int x, int y)
    {
        _mouseX = x;
        _mouseY = y;
    }

    /// <summary>
    /// Returns true if the controller consumed the mouse-down (colony placed,
    /// out-of-bounds click ignored while in Colony mode, or food-drag started).
    /// Returns false when no placement mode is active, letting Engine route the
    /// click elsewhere (e.g. ant selection).
    /// </summary>
    public bool HandleMouseDown(int screenX, int screenY, World world)
    {
        if (_placingMode == PlacingMode.Colony)
        {
            ScreenToCell(screenX, screenY, out int cellX, out int cellY);
            if (!NestFitsInWorld(world, cellX, cellY))
            {
                return true;
            }

            Color nextColor = ColonyColors[_nextColorIndex % ColonyColors.Length];
            _nextColorIndex++;

            world.AddColony(cellX, cellY, nextColor);
            Cancel();
            return true;
        }

        if (_placingMode == PlacingMode.Food)
        {
            _isDrawingFood = true;
            PaintFoodAtMouse(world, screenX, screenY);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Called from Engine.OnSkMouseMove *after* the right-drag early-return, so
    /// food-painting does not fire while the user is panning the camera.
    /// Mouse coords for ghost preview are updated separately via
    /// <see cref="UpdateMouseCoords"/>, which Engine invokes unconditionally
    /// (matching pre-refactor behavior where _mouseX/_mouseY updated during
    /// right-drag too).
    /// </summary>
    public void HandleMouseMoveDrag(World world)
    {
        if (_isDrawingFood)
        {
            PaintFoodAtMouse(world, _mouseX, _mouseY);
        }
    }

    public void HandleMouseUp()
    {
        _isDrawingFood = false;
    }

    public void DrawGhost(SKCanvas canvas, World world)
    {
        if (_placingMode == PlacingMode.Colony)
        {
            ScreenToCell(_mouseX, _mouseY, out int hoverCellX, out int hoverCellY);
            if (NestFitsInWorld(world, hoverCellX, hoverCellY))
            {
                Color ghostBase = ColonyColors[_nextColorIndex % ColonyColors.Length];
                SKColor ghostColor = new SKColor(ghostBase.R, ghostBase.G, ghostBase.B, 140);
                DrawNest(canvas, ghostColor, hoverCellX, hoverCellY);
            }
        }
        if (_placingMode == PlacingMode.Food)
        {
            ScreenToCell(_mouseX, _mouseY, out int hoverCellX, out int hoverCellY);
            if (hoverCellX >= 0 && hoverCellX < world.Width && hoverCellY >= 0 && hoverCellY < world.Height)
            {
                _paints.SharedFill.Color = _foodSkColor.WithAlpha(140);
                canvas.DrawRect(hoverCellX * CellSize, hoverCellY * CellSize, CellSize, CellSize, _paints.SharedFill);
            }
        }
    }

    private void ScreenToCell(int screenX, int screenY, out int cellX, out int cellY)
    {
        _camera.ScreenToWorld(screenX, screenY, out float wx, out float wy);
        cellX = (int)Math.Floor(wx / CellSize);
        cellY = (int)Math.Floor(wy / CellSize);
    }

    private void PaintFoodAtMouse(World world, int pixelX, int pixelY)
    {
        ScreenToCell(pixelX, pixelY, out int cellX, out int cellY);

        if (cellX < 0 || cellX >= world.Width)
        {
            return;
        }
        if (cellY < 0 || cellY >= world.Height)
        {
            return;
        }

        world.SetCell(cellX, cellY, CellType.Food);
    }

    private static bool NestFitsInWorld(World world, int centerCellX, int centerCellY)
    {
        if (centerCellX - World.NestRadius < 0)
        {
            return false;
        }
        if (centerCellX + World.NestRadius >= world.Width)
        {
            return false;
        }
        if (centerCellY - World.NestRadius < 0)
        {
            return false;
        }
        if (centerCellY + World.NestRadius >= world.Height)
        {
            return false;
        }
        return true;
    }

    private void DrawNest(SKCanvas canvas, SKColor color, int centerCellX, int centerCellY)
    {
        _ghostNestPath.Reset();

        for (int dy = -World.NestRadius; dy <= World.NestRadius; dy++)
        {
            for (int dx = -World.NestRadius; dx <= World.NestRadius; dx++)
            {
                int manhattan = Math.Abs(dx) + Math.Abs(dy);
                if (manhattan > World.NestRadius)
                {
                    continue;
                }

                int cellX = centerCellX + dx;
                int cellY = centerCellY + dy;

                float pixelX = cellX * CellSize;
                float pixelY = cellY * CellSize;

                _ghostNestPath.AddRect(new SKRect(pixelX, pixelY, pixelX + CellSize, pixelY + CellSize));
            }
        }

        _paints.SharedFill.Color = color;
        canvas.DrawPath(_ghostNestPath, _paints.SharedFill);
    }

    public void Dispose()
    {
        _ghostNestPath.Dispose();
    }
}
