namespace ANTS;
using System.IO;
using SkiaSharp;

public static class MapLoader
{
    private const float ColonyFoodDefaultAmount = 0f;
    private const float FoodDefaultAmount = 0.4f;

    public static readonly Color RedTeamColor = Color.FromArgb(239, 68, 68);
    public static readonly Color BlueTeamColor = Color.FromArgb(59, 130, 246);
    public static readonly Color YellowTeamColor = Color.FromArgb(234, 179, 8);
    public static readonly Color OrangeTeamColor = Color.FromArgb(249, 115, 22);
    public static readonly Color PurpleTeamColor = Color.FromArgb(168, 85, 247);
    public static readonly Color PinkTeamColor = Color.FromArgb(236, 72, 153);


    private enum PixelClass
    {
        Empty,
        Wall,
        Food,
        ColonyRed,
        ColonyBlue,
        ColonyYellow,
        ColonyOrange,
        ColonyPurple,
        ColonyPink,
    }

    public static MapDefinition Load(string pngPath)
    {
        using FileStream stream = File.OpenRead(pngPath);
        using SKBitmap bitmap = SKBitmap.Decode(stream);
        if (bitmap == null)
        {
            throw new InvalidDataException("Could not decode map PNG: " + pngPath);
        }

        string name = Path.GetFileNameWithoutExtension(pngPath);
        return BuildFromBitmap(bitmap, name);
    }

    public static MapDefinition LoadFromBitmap(SKBitmap bitmap, string name)
    {
        return BuildFromBitmap(bitmap, name);
    }

    private static MapDefinition BuildFromBitmap(SKBitmap bitmap, string name)
    {
        int w = bitmap.Width;
        int h = bitmap.Height;
        MapDefinition def = new MapDefinition(name, w, h);

        PixelClass[,] classified = new PixelClass[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                SKColor px = bitmap.GetPixel(x, y);
                PixelClass cls = Classify(px);
                classified[x, y] = cls;

                switch (cls)
                {
                    case PixelClass.Wall:
                        def.Cells[x, y] = CellType.Wall;
                        break;
                    case PixelClass.Food:
                        def.Cells[x, y] = CellType.Food;
                        def.FoodCells.Add(new FoodSeed(x, y, FoodDefaultAmount));
                        break;
                    default:
                        def.Cells[x, y] = CellType.Empty;
                        break;
                }
            }
        }

        FindColonySeeds(classified, PixelClass.ColonyRed, RedTeamColor, def);
        FindColonySeeds(classified, PixelClass.ColonyBlue, BlueTeamColor, def);
        FindColonySeeds(classified, PixelClass.ColonyYellow, YellowTeamColor, def);
        FindColonySeeds(classified, PixelClass.ColonyOrange, OrangeTeamColor, def);
        FindColonySeeds(classified, PixelClass.ColonyPurple, PurpleTeamColor, def);
        FindColonySeeds(classified, PixelClass.ColonyPink, PinkTeamColor, def);

        return def;
    }


    private static PixelClass Classify(SKColor px)
    {
        if (px.Alpha < 16)
        {
            return PixelClass.Empty;
        }

        float r = px.Red / 255f;
        float g = px.Green / 255f;
        float b = px.Blue / 255f;

        float max = r;
        if (g > max) max = g;
        if (b > max) max = b;

        float min = r;
        if (g < min) min = g;
        if (b < min) min = b;

        float v = max;
        float delta = max - min;
        float s = max <= 0.0001f ? 0f : delta / max;

        if (v < 0.22f)
        {
            return PixelClass.Wall;
        }

        if (s < 0.05f && v >= 0.4f && v <= 0.65f)
        {
            return PixelClass.Wall;
        }

        if (s < 0.28f)
        {
            return PixelClass.Empty;
        }

        float h;
        if (delta <= 0.0001f)
        {
            h = 0f;
        }
        else if (max == r)
        {
            h = 60f * (((g - b) / delta) % 6f);
        }
        else if (max == g)
        {
            h = 60f * (((b - r) / delta) + 2f);
        }
        else
        {
            h = 60f * (((r - g) / delta) + 4f);
        }
        if (h < 0f) h += 360f;

        if (h >= 340f || h < 20f)
        {
            return PixelClass.ColonyRed;
        }
        if (h >= 20f && h < 40f)
        {
            return PixelClass.ColonyOrange;
        }
        if (h >= 40f && h <= 70f)
        {
            return PixelClass.ColonyYellow;
        }
        if (h >= 85f && h <= 170f)
        {
            return PixelClass.Food;
        }
        if (h >= 195f && h <= 260f)
        {
            return PixelClass.ColonyBlue;
        }
        if (h >= 260f && h <= 300f)
        {
            return PixelClass.ColonyPurple;
        }
        if (h >= 300f && h < 340f)
        {
            return PixelClass.ColonyPink;
        }
        return PixelClass.Empty;
    }


    private static void FindColonySeeds(PixelClass[,] classified, PixelClass target, Color color, MapDefinition def)
    {
        int w = def.Width;
        int h = def.Height;
        bool[,] visited = new bool[w, h];
        Queue<(int x, int y)> queue = new Queue<(int x, int y)>();

        for (int y0 = 0; y0 < h; y0++)
        {
            for (int x0 = 0; x0 < w; x0++)
            {
                if (visited[x0, y0]) continue;
                if (classified[x0, y0] != target) continue;

                queue.Clear();
                queue.Enqueue((x0, y0));
                visited[x0, y0] = true;

                long sumX = 0;
                long sumY = 0;
                int count = 0;

                while (queue.Count > 0)
                {
                    (int cx, int cy) = queue.Dequeue();
                    sumX += cx;
                    sumY += cy;
                    count++;

                    if (cx > 0 && !visited[cx - 1, cy] && classified[cx - 1, cy] == target)
                    {
                        visited[cx - 1, cy] = true;
                        queue.Enqueue((cx - 1, cy));
                    }
                    if (cx < w - 1 && !visited[cx + 1, cy] && classified[cx + 1, cy] == target)
                    {
                        visited[cx + 1, cy] = true;
                        queue.Enqueue((cx + 1, cy));
                    }
                    if (cy > 0 && !visited[cx, cy - 1] && classified[cx, cy - 1] == target)
                    {
                        visited[cx, cy - 1] = true;
                        queue.Enqueue((cx, cy - 1));
                    }
                    if (cy < h - 1 && !visited[cx, cy + 1] && classified[cx, cy + 1] == target)
                    {
                        visited[cx, cy + 1] = true;
                        queue.Enqueue((cx, cy + 1));
                    }
                }

                if (count < 6) continue;

                int centerX = (int)(sumX / count);
                int centerY = (int)(sumY / count);
                def.ColonySeeds.Add(new ColonySeed(centerX, centerY, color));
            }
        }
    }
}
