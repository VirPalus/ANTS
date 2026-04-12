namespace ANTS;
using System.Collections.Generic;
using SkiaSharp;

public class Colony
{
    public int Id { get; }
    public int NestX { get; }
    public int NestY { get; }
    public Color Color { get; }
    public SKColor CachedSkColor { get; }

    private List<Ant> _ants;
    public int SpawnCounter;
    public int MaxAnts;

    public IReadOnlyList<Ant> Ants
    {
        get { return _ants; }
    }

    internal List<Ant> AntsList
    {
        get { return _ants; }
    }

    public Colony(int id, int nestX, int nestY, Color color)
    {
        Id = id;
        NestX = nestX;
        NestY = nestY;
        Color = color;
        CachedSkColor = new SKColor(color.R, color.G, color.B, color.A);
        _ants = new List<Ant>();
        MaxAnts = 200;
        SpawnCounter = 0;
    }

    public Ant SpawnAnt(float x, float y, float heading, AntRole role)
    {
        Ant ant = new Ant(x, y, heading, role);
        _ants.Add(ant);
        return ant;
    }
}
