namespace ANTS;
using SkiaSharp;

/// <summary>
/// Shared SKPaint instances reused across frames to avoid per-frame allocations.
/// Owned by Engine; disposed via IDisposable when Engine tears down.
/// </summary>
public sealed class PaintCache : IDisposable
{
    public SKPaint SharedFill { get; }
    public SKPaint SharedStroke { get; }
    public SKPaint SharedText { get; }
    public SKPaint AntPaint { get; }
    public SKPaint SharedBorder { get; }
    public SKPaint TitlePaint { get; }
    public SKPaint PheromoneBitmapPaint { get; }

    public PaintCache()
    {
        SharedFill = new SKPaint();
        SharedFill.Style = SKPaintStyle.Fill;
        SharedFill.IsAntialias = false;

        SharedStroke = new SKPaint();
        SharedStroke.Style = SKPaintStyle.Stroke;
        SharedStroke.IsAntialias = false;
        SharedStroke.StrokeWidth = 1;

        SharedText = new SKPaint();
        SharedText.Style = SKPaintStyle.Fill;
        SharedText.IsAntialias = true;
        SharedText.Color = SKColors.White;
        SharedText.TextSize = 14;

        AntPaint = new SKPaint();
        AntPaint.Style = SKPaintStyle.Fill;
        AntPaint.IsAntialias = true;
        AntPaint.FilterQuality = SKFilterQuality.Low;

        SharedBorder = new SKPaint();
        SharedBorder.Style = SKPaintStyle.Stroke;
        SharedBorder.IsAntialias = false;
        SharedBorder.StrokeWidth = 1;

        TitlePaint = new SKPaint();
        TitlePaint.Style = SKPaintStyle.Fill;
        TitlePaint.IsAntialias = true;
        TitlePaint.Color = UiTheme.TextStrong;
        TitlePaint.TextSize = UiTheme.FontDisplay;

        PheromoneBitmapPaint = new SKPaint();
        PheromoneBitmapPaint.FilterQuality = SKFilterQuality.None;
        PheromoneBitmapPaint.IsAntialias = false;
    }

    public void Dispose()
    {
        SharedFill.Dispose();
        SharedStroke.Dispose();
        SharedText.Dispose();
        AntPaint.Dispose();
        SharedBorder.Dispose();
        TitlePaint.Dispose();
        PheromoneBitmapPaint.Dispose();
    }
}
