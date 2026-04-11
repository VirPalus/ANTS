namespace ANTS;

public class World
{
    private int[,] _cells;
    public int Width { get; }
    public int Height { get; }

    public World(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new int[Width, Height];
    }
}