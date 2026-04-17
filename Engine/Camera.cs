namespace ANTS;
using SkiaSharp;

public class Camera
{
    public const float MinZoom = 0.125f;
    public const float MaxZoom = 12f;
    public const float ZoomWheelStep = 1.15f;

    public float OffsetX;
    public float OffsetY;
    public float Zoom = 1f;

    public void SetZoom(float zoom)
    {
        if (zoom < MinZoom)
        {
            zoom = MinZoom;
        }
        if (zoom > MaxZoom)
        {
            zoom = MaxZoom;
        }
        Zoom = zoom;
    }

    public void ZoomAt(float screenX, float screenY, float factor)
    {
        float oldZoom = Zoom;
        float newZoom = oldZoom * factor;
        if (newZoom < MinZoom)
        {
            newZoom = MinZoom;
        }
        if (newZoom > MaxZoom)
        {
            newZoom = MaxZoom;
        }
        float ratio = newZoom / oldZoom;
        OffsetX = screenX - (screenX - OffsetX) * ratio;
        OffsetY = screenY - (screenY - OffsetY) * ratio;
        Zoom = newZoom;
    }

    public void PanScreen(float dScreenX, float dScreenY)
    {
        OffsetX += dScreenX;
        OffsetY += dScreenY;
    }

    public void ScreenToWorld(float screenX, float screenY, out float worldX, out float worldY)
    {
        worldX = (screenX - OffsetX) / Zoom;
        worldY = (screenY - OffsetY) / Zoom;
    }

    public void WorldToScreen(float worldX, float worldY, out float screenX, out float screenY)
    {
        screenX = worldX * Zoom + OffsetX;
        screenY = worldY * Zoom + OffsetY;
    }

    public void Apply(SKCanvas canvas)
    {
        canvas.Translate(OffsetX, OffsetY);
        canvas.Scale(Zoom, Zoom);
    }

    public void FitWorld(float worldPixelWidth, float worldPixelHeight, int screenWidth, int screenHeight, float margin)
    {
        if (worldPixelWidth <= 0 || worldPixelHeight <= 0 || screenWidth <= 0 || screenHeight <= 0)
        {
            Zoom = 1f;
            OffsetX = 0;
            OffsetY = 0;
            return;
        }

        float usableW = screenWidth - 2 * margin;
        float usableH = screenHeight - 2 * margin;
        if (usableW < 1) usableW = 1;
        if (usableH < 1) usableH = 1;

        float zoomX = usableW / worldPixelWidth;
        float zoomY = usableH / worldPixelHeight;
        float z = zoomX < zoomY ? zoomX : zoomY;
        if (z < MinZoom) z = MinZoom;
        if (z > MaxZoom) z = MaxZoom;
        Zoom = z;

        OffsetX = (screenWidth - worldPixelWidth * Zoom) / 2f;
        OffsetY = (screenHeight - worldPixelHeight * Zoom) / 2f;
    }

    public void ClampToWorld(float worldPixelWidth, float worldPixelHeight, int screenWidth, int screenHeight, float keepVisible)
    {
        float renderedW = worldPixelWidth * Zoom;
        float renderedH = worldPixelHeight * Zoom;

        float maxOffsetX;
        float minOffsetX;
        if (renderedW <= screenWidth)
        {
            maxOffsetX = screenWidth - keepVisible;
            minOffsetX = keepVisible - renderedW;
        }
        else
        {
            maxOffsetX = keepVisible;
            minOffsetX = screenWidth - renderedW - keepVisible;
        }
        if (OffsetX > maxOffsetX) OffsetX = maxOffsetX;
        if (OffsetX < minOffsetX) OffsetX = minOffsetX;

        float maxOffsetY;
        float minOffsetY;
        if (renderedH <= screenHeight)
        {
            maxOffsetY = screenHeight - keepVisible;
            minOffsetY = keepVisible - renderedH;
        }
        else
        {
            maxOffsetY = keepVisible;
            minOffsetY = screenHeight - renderedH - keepVisible;
        }
        if (OffsetY > maxOffsetY) OffsetY = maxOffsetY;
        if (OffsetY < minOffsetY) OffsetY = minOffsetY;
    }
}
