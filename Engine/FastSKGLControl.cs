namespace ANTS;
using SkiaSharp.Views.Desktop;

public class FastSKGLControl : SKGLControl
{
    private Graphics? _cachedGraphics;
    private PaintEventArgs? _cachedPaintArgs;
    private Rectangle _cachedRectangle;

    public FastSKGLControl()
    {
        SetStyle(ControlStyles.Opaque, true);
        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        SetStyle(ControlStyles.ResizeRedraw, false);
        VSync = false;
    }

    public void RenderFrameDirect()
    {
        if (!IsHandleCreated)
        {
            return;
        }
        if (DesignMode)
        {
            return;
        }

        Rectangle currentRectangle = ClientRectangle;
        if (_cachedPaintArgs == null || _cachedRectangle != currentRectangle)
        {
            DisposeCachedPaintArgs();
            _cachedGraphics = Graphics.FromHwnd(Handle);
            _cachedPaintArgs = new PaintEventArgs(_cachedGraphics, currentRectangle);
            _cachedRectangle = currentRectangle;
        }

        MakeCurrent();
        OnPaint(_cachedPaintArgs);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        DisposeCachedPaintArgs();
        base.OnHandleDestroyed(e);
    }

    private void DisposeCachedPaintArgs()
    {
        if (_cachedPaintArgs != null)
        {
            _cachedPaintArgs.Dispose();
            _cachedPaintArgs = null;
        }
        if (_cachedGraphics != null)
        {
            _cachedGraphics.Dispose();
            _cachedGraphics = null;
        }
    }
}
