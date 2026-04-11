namespace ANTS;

public class UiButton
{
    public Rectangle Bounds { get; }
    public string Label { get; }
    public Color BackgroundColor { get; }
    public Action OnClick { get; }

    public float TextBaselineX;
    public float TextBaselineY;

    public UiButton(Rectangle bounds, string label, Color backgroundColor, Action onClick)
    {
        Bounds = bounds;
        Label = label;
        BackgroundColor = backgroundColor;
        OnClick = onClick;
    }

    public bool Contains(int x, int y)
    {
        return Bounds.Contains(x, y);
    }
}
