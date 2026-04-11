namespace ANTS;

public class Colony
{
    public int NestX { get; }
    public int NestY { get; }
    public Color Color { get; }

    public Colony(int nestX, int nestY, Color color)
    {
        NestX = nestX;
        NestY = nestY;
        Color = color;
    }
}
