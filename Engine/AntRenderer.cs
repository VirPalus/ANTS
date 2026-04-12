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

    private static readonly SKPath _bodyFillTemplate = BuildBodyFillTemplate();
    private static readonly SKPath _bodyStrokeTemplate = BuildBodyStrokeTemplate();

    private static SKPath BuildBodyFillTemplate()
    {
        SKPath path = new SKPath();
        path.AddOval(new SKRect(AbdomenCenterX - AbdomenRadiusX, -AbdomenRadiusY, AbdomenCenterX + AbdomenRadiusX, AbdomenRadiusY));
        path.AddOval(new SKRect(ThoraxCenterX - ThoraxRadiusX, -ThoraxRadiusY, ThoraxCenterX + ThoraxRadiusX, ThoraxRadiusY));
        path.AddOval(new SKRect(HeadCenterX - HeadRadiusX, -HeadRadiusY, HeadCenterX + HeadRadiusX, HeadRadiusY));
        return path;
    }

    private static SKPath BuildBodyStrokeTemplate()
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
        return path;
    }

    public const int LegPointsPerAnt = 12;

    public static void AddAnt(SKPath fillBatch, SKPath strokeBatch, SKPoint[] legPointsBuffer, int legPointsWriteIndex, float centerX, float centerY, float headingRadians, float stridePhase)
    {
        SKMatrix bodyTransform = SKMatrix.CreateRotation(headingRadians);
        bodyTransform.TransX = centerX;
        bodyTransform.TransY = centerY;

        fillBatch.AddPath(_bodyFillTemplate, ref bodyTransform);
        strokeBatch.AddPath(_bodyStrokeTemplate, ref bodyTransform);

        float headingCos = bodyTransform.ScaleX;
        float headingSin = bodyTransform.SkewY;

        float legSwing = (float)Math.Sin(stridePhase) * LegSwingAmplitude;
        float legSwingOpposite = -legSwing;

        WriteLegPoints(legPointsBuffer, legPointsWriteIndex, headingCos, headingSin, centerX, centerY, LegFrontBaseX, -LegBaseY, LegFrontTipX + legSwing, -LegTipY);
        WriteLegPoints(legPointsBuffer, legPointsWriteIndex + 2, headingCos, headingSin, centerX, centerY, LegMiddleBaseX, -LegBaseY, LegMiddleTipX + legSwingOpposite, -LegTipY);
        WriteLegPoints(legPointsBuffer, legPointsWriteIndex + 4, headingCos, headingSin, centerX, centerY, LegBackBaseX, -LegBaseY, LegBackTipX + legSwing, -LegTipY);
        WriteLegPoints(legPointsBuffer, legPointsWriteIndex + 6, headingCos, headingSin, centerX, centerY, LegFrontBaseX, LegBaseY, LegFrontTipX + legSwingOpposite, LegTipY);
        WriteLegPoints(legPointsBuffer, legPointsWriteIndex + 8, headingCos, headingSin, centerX, centerY, LegMiddleBaseX, LegBaseY, LegMiddleTipX + legSwing, LegTipY);
        WriteLegPoints(legPointsBuffer, legPointsWriteIndex + 10, headingCos, headingSin, centerX, centerY, LegBackBaseX, LegBaseY, LegBackTipX + legSwingOpposite, LegTipY);
    }

    private static void WriteLegPoints(SKPoint[] buffer, int writeIndex, float headingCos, float headingSin, float antCenterX, float antCenterY, float localStartX, float localStartY, float localEndX, float localEndY)
    {
        float startWorldX = headingCos * localStartX - headingSin * localStartY + antCenterX;
        float startWorldY = headingSin * localStartX + headingCos * localStartY + antCenterY;
        float endWorldX = headingCos * localEndX - headingSin * localEndY + antCenterX;
        float endWorldY = headingSin * localEndX + headingCos * localEndY + antCenterY;
        buffer[writeIndex] = new SKPoint(startWorldX, startWorldY);
        buffer[writeIndex + 1] = new SKPoint(endWorldX, endWorldY);
    }
}
