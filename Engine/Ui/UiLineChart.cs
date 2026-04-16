namespace ANTS;
using SkiaSharp;

// Fixed-capacity ring buffer + draw routine for a mini line chart.
// Replaces the ad-hoc DrawPopulationGraph that used to hand-roll a
// path inline. The chart auto-rescales its Y axis to the current
// min/max of the buffer, draws a soft filled area and a crisp line
// on top.
//
// A single UiLineChart instance keeps its own sample buffer, so you
// can have several side-by-side charts (e.g. one per colony, or one
// per performance metric) without them stepping on each other.
public class UiLineChart
{
    private readonly float[] _samples;
    private int _count;
    private int _head;   // next write index; ring buffer

    public int Capacity { get { return _samples.Length; } }
    public int Count { get { return _count; } }

    public UiLineChart(int capacity)
    {
        if (capacity < 2) capacity = 2;
        _samples = new float[capacity];
        _count = 0;
        _head = 0;
    }

    public void Push(float value)
    {
        _samples[_head] = value;
        _head = (_head + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
    }

    public void Clear()
    {
        _count = 0;
        _head = 0;
    }

    // Read the i-th oldest sample (0 = oldest still in buffer, Count-1
    // = most recent). Useful for alignment between charts.
    public float Get(int i)
    {
        int start = _count < _samples.Length ? 0 : _head;
        return _samples[(start + i) % _samples.Length];
    }

    // Y-scale hint: if you want to peg the chart to a known range
    // (e.g. 0..100 for percentages) instead of auto-scaling to the
    // buffer min/max, pass yMin/yMax != -1.
    public void Draw(SKCanvas canvas, SKRect bounds, SKColor lineColor, SKColor fillColor, float yMin = float.NaN, float yMax = float.NaN)
    {
        if (_count < 2) return;

        // Figure out Y range.
        float lo, hi;
        if (!float.IsNaN(yMin) && !float.IsNaN(yMax))
        {
            lo = yMin;
            hi = yMax;
        }
        else
        {
            lo = float.PositiveInfinity;
            hi = float.NegativeInfinity;
            for (int i = 0; i < _count; i++)
            {
                float v = Get(i);
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
        }
        if (hi - lo < 0.0001f)
        {
            hi = lo + 1f;  // avoid /0 on flat series
        }

        float x = bounds.Left;
        float y = bounds.Top;
        float w = bounds.Width;
        float h = bounds.Height;
        float stepX = w / (_samples.Length - 1);

        // Compose line path + filled-area path in one walk.
        using SKPath linePath = new SKPath();
        using SKPath fillPath = new SKPath();

        // Left padding: if buffer is not yet full, chart starts at
        // a later X so the animation "grows in" from the right.
        int pad = _samples.Length - _count;

        float firstX = x + pad * stepX;
        float firstY = MapY(Get(0), lo, hi, y, h);
        linePath.MoveTo(firstX, firstY);
        fillPath.MoveTo(firstX, y + h);
        fillPath.LineTo(firstX, firstY);

        for (int i = 1; i < _count; i++)
        {
            float px = x + (pad + i) * stepX;
            float py = MapY(Get(i), lo, hi, y, h);
            linePath.LineTo(px, py);
            fillPath.LineTo(px, py);
        }

        float lastX = x + (pad + _count - 1) * stepX;
        fillPath.LineTo(lastX, y + h);
        fillPath.Close();

        using SKPaint fillPaint = new SKPaint();
        fillPaint.Style = SKPaintStyle.Fill;
        fillPaint.IsAntialias = true;
        fillPaint.Color = fillColor;
        canvas.DrawPath(fillPath, fillPaint);

        using SKPaint linePaint = new SKPaint();
        linePaint.Style = SKPaintStyle.Stroke;
        linePaint.IsAntialias = true;
        linePaint.Color = lineColor;
        linePaint.StrokeWidth = 1.6f;
        linePaint.StrokeJoin = SKStrokeJoin.Round;
        canvas.DrawPath(linePath, linePaint);
    }

    private static float MapY(float v, float lo, float hi, float top, float height)
    {
        float t = (v - lo) / (hi - lo);
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        return top + (1f - t) * height;
    }
}
