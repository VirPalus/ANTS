namespace ANTS;
using SkiaSharp;

public static class AntRenderer
{
    private const float HeadCenterX = 4.2f;
    private const float HeadRadiusX = 2.2f;
    private const float HeadRadiusY = 1.9f;

    private const float ThoraxCenterX = 0.4f;
    private const float ThoraxRadiusX = 1.9f;
    private const float ThoraxRadiusY = 1.5f;

    private const float AbdomenCenterX = -4.2f;
    private const float AbdomenRadiusX = 3.6f;
    private const float AbdomenRadiusY = 2.6f;

    private const float NeckStartX = 2.4f;
    private const float NeckEndX = 2.9f;
    private const float WaistStartX = -1.4f;
    private const float WaistEndX = -1.9f;

    private const float AntennaBaseX = 5.6f;
    private const float AntennaMidX = 7.4f;
    private const float AntennaMidY = 1.6f;
    private const float AntennaTipX = 9.0f;
    private const float AntennaTipY = 3.2f;

    private const float LegFrontBaseX = 1.6f;
    private const float LegMiddleBaseX = 0.4f;
    private const float LegBackBaseX = -0.8f;
    private const float LegBaseY = 1.3f;

    private const float LegFrontTipX = 4.4f;
    private const float LegMiddleTipX = 0.4f;
    private const float LegBackTipX = -3.6f;
    private const float LegTipY = 4.8f;

    private const float LegSwingAmplitude = 2.0f;

    public const float BodyStroke = 0.8f;

    public const int StrideFrameCount = 16;
    private const int StrideFrameIndexMask = StrideFrameCount - 1;
    private const float TwoPi = 6.28318548f;
    private const float FrameIndexScale = StrideFrameCount / TwoPi;

    private const int AtlasSupersample = 8;
    private const int AtlasFrameSizePixels = 192;
    public const float AtlasAnchor = 96f;
    public const float AtlasInverseSupersample = 1f / AtlasSupersample;

    private const float FoodDotX = 6.8f;
    private const float FoodDotRadius = 1.8f;

    private static readonly SKRect[] _frameSpriteRects = BuildFrameSpriteRects(0);
    private static readonly SKRect[] _frameSpriteRectsWithFood = BuildFrameSpriteRects(1);
    private static readonly SKImage _bodyAtlasImage = BuildBodyAtlas();

    public static SKImage BodyAtlasImage
    {
        get { return _bodyAtlasImage; }
    }

    public static SKRect[] FrameSpriteRects
    {
        get { return _frameSpriteRects; }
    }

    public static SKRect[] FrameSpriteRectsWithFood
    {
        get { return _frameSpriteRectsWithFood; }
    }

    private static SKRect[] BuildFrameSpriteRects(int row)
    {
        SKRect[] rects = new SKRect[StrideFrameCount];
        float top = row * AtlasFrameSizePixels;
        float bottom = top + AtlasFrameSizePixels;
        for (int i = 0; i < StrideFrameCount; i++)
        {
            float left = i * AtlasFrameSizePixels;
            float right = left + AtlasFrameSizePixels;
            rects[i] = new SKRect(left, top, right, bottom);
        }
        return rects;
    }

    private static SKPath BuildBodyFillTemplate()
    {
        SKPath path = new SKPath();
        path.AddOval(new SKRect(AbdomenCenterX - AbdomenRadiusX, -AbdomenRadiusY, AbdomenCenterX + AbdomenRadiusX, AbdomenRadiusY));
        path.AddOval(new SKRect(ThoraxCenterX - ThoraxRadiusX, -ThoraxRadiusY, ThoraxCenterX + ThoraxRadiusX, ThoraxRadiusY));
        path.AddOval(new SKRect(HeadCenterX - HeadRadiusX, -HeadRadiusY, HeadCenterX + HeadRadiusX, HeadRadiusY));
        return path;
    }

    private static SKPath BuildBodyStrokeTemplate(float stridePhase)
    {
        SKPath path = new SKPath();
        path.MoveTo(WaistStartX, 0f);
        path.LineTo(WaistEndX, 0f);
        path.MoveTo(NeckStartX, 0f);
        path.LineTo(NeckEndX, 0f);
        path.MoveTo(AntennaBaseX, 0f);
        path.QuadTo(AntennaMidX, -AntennaMidY, AntennaTipX, -AntennaTipY);
        path.MoveTo(AntennaBaseX, 0f);
        path.QuadTo(AntennaMidX, AntennaMidY, AntennaTipX, AntennaTipY);

        float legSwing = (float)Math.Sin(stridePhase) * LegSwingAmplitude;
        float legSwingOpposite = -legSwing;

        path.MoveTo(LegFrontBaseX, -LegBaseY);
        path.LineTo(LegFrontTipX + legSwing, -LegTipY);
        path.MoveTo(LegMiddleBaseX, -LegBaseY);
        path.LineTo(LegMiddleTipX + legSwingOpposite, -LegTipY);
        path.MoveTo(LegBackBaseX, -LegBaseY);
        path.LineTo(LegBackTipX + legSwing, -LegTipY);
        path.MoveTo(LegFrontBaseX, LegBaseY);
        path.LineTo(LegFrontTipX + legSwingOpposite, LegTipY);
        path.MoveTo(LegMiddleBaseX, LegBaseY);
        path.LineTo(LegMiddleTipX + legSwing, LegTipY);
        path.MoveTo(LegBackBaseX, LegBaseY);
        path.LineTo(LegBackTipX + legSwingOpposite, LegTipY);

        return path;
    }

    private static SKImage BuildBodyAtlas()
    {
        int totalWidth = AtlasFrameSizePixels * StrideFrameCount;
        int totalHeight = AtlasFrameSizePixels * 2;
        SKImageInfo info = new SKImageInfo(totalWidth, totalHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using SKPath fillTemplate = BuildBodyFillTemplate();

        using SKPaint fillPaint = new SKPaint();
        fillPaint.Style = SKPaintStyle.Fill;
        fillPaint.IsAntialias = true;
        fillPaint.Color = SKColors.White;

        using SKPaint strokePaint = new SKPaint();
        strokePaint.Style = SKPaintStyle.Stroke;
        strokePaint.StrokeWidth = BodyStroke;
        strokePaint.StrokeCap = SKStrokeCap.Round;
        strokePaint.IsAntialias = true;
        strokePaint.Color = SKColors.White;

        using SKPaint foodPaint = new SKPaint();
        foodPaint.Style = SKPaintStyle.Fill;
        foodPaint.IsAntialias = true;
        foodPaint.Color = new SKColor(255, 0, 0);

        for (int row = 0; row < 2; row++)
        {
            float rowOffsetY = row * AtlasFrameSizePixels;
            for (int frameIndex = 0; frameIndex < StrideFrameCount; frameIndex++)
            {
                canvas.Save();
                float frameOriginX = frameIndex * AtlasFrameSizePixels + AtlasAnchor;
                canvas.Translate(frameOriginX, rowOffsetY + AtlasAnchor);
                canvas.Scale(AtlasSupersample, AtlasSupersample);

                float stridePhase = frameIndex * TwoPi / StrideFrameCount;
                using SKPath strokeTemplate = BuildBodyStrokeTemplate(stridePhase);

                canvas.DrawPath(fillTemplate, fillPaint);
                canvas.DrawPath(strokeTemplate, strokePaint);

                if (row == 1)
                {
                    canvas.DrawCircle(FoodDotX, 0f, FoodDotRadius, foodPaint);
                }

                canvas.Restore();
            }
        }

        return surface.Snapshot();
    }

    public static int GetFrameIndex(float stridePhase)
    {
        float scaled = stridePhase * FrameIndexScale;
        int floored = (int)scaled;
        return floored & StrideFrameIndexMask;
    }

}
