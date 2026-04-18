namespace ANTS;
using System.IO;
using SkiaSharp;

public class UiStartOverlay : IDisposable
{
    public class Entry
    {
        public string Path = "";
        public string DisplayName = "";
        public SKBitmap? Bitmap;
        public int WorldWidth;
        public int WorldHeight;
    }

    public List<Entry> Entries = new List<Entry>();
    public int SelectedIndex = 0;
    public bool Visible = true;

    public SKRect StartButtonBounds;
    public List<SKRect> CardBounds = new List<SKRect>();

    private readonly Action<Entry> _onStart;

    public UiStartOverlay(Action<Entry> onStart)
    {
        _onStart = onStart;
    }

    public void Scan(string mapsDir, int thumbMaxDim)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            Entries[i].Bitmap?.Dispose();
        }
        Entries.Clear();

        if (!Directory.Exists(mapsDir)) return;

        string[] pngs = Directory.GetFiles(mapsDir, "*.png");
        Array.Sort(pngs, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < pngs.Length; i++)
        {
            try
            {
                using FileStream fs = File.OpenRead(pngs[i]);
                using SKBitmap src = SKBitmap.Decode(fs);
                if (src == null) continue;

                Entry e = new Entry();
                e.Path = pngs[i];
                e.DisplayName = PrettyName(Path.GetFileNameWithoutExtension(pngs[i]));
                e.WorldWidth = src.Width;
                e.WorldHeight = src.Height;

                int tw, th;
                if (src.Width >= src.Height)
                {
                    tw = thumbMaxDim;
                    th = (int)(src.Height * ((float)thumbMaxDim / src.Width));
                }
                else
                {
                    th = thumbMaxDim;
                    tw = (int)(src.Width * ((float)thumbMaxDim / src.Height));
                }
                if (tw < 1) tw = 1;
                if (th < 1) th = 1;

                SKBitmap thumb = new SKBitmap(tw, th);
                src.ScalePixels(thumb, SKFilterQuality.Medium);
                e.Bitmap = thumb;

                Entries.Add(e);
            }
            catch
            {
            }
        }
    }

    public void Layout(int clientWidth, int clientHeight)
    {
        CardBounds.Clear();
        if (Entries.Count == 0)
        {
            StartButtonBounds = new SKRect(
                clientWidth / 2f - 80,
                clientHeight / 2f + 60,
                clientWidth / 2f + 80,
                clientHeight / 2f + 100);
            return;
        }

        const int CardW = 220;
        const int CardH = 160;
        const int CardGap = 24;

        int maxCardsPerRow = Math.Max(1, (clientWidth - 80) / (CardW + CardGap));
        int cardsPerRow = Math.Min(maxCardsPerRow, Entries.Count);
        int rowCount = (Entries.Count + cardsPerRow - 1) / cardsPerRow;

        int totalWidth = cardsPerRow * CardW + (cardsPerRow - 1) * CardGap;
        int totalHeight = rowCount * CardH + (rowCount - 1) * CardGap;

        int startX = (clientWidth - totalWidth) / 2;
        int startY = (clientHeight - totalHeight) / 2 - 20;

        for (int i = 0; i < Entries.Count; i++)
        {
            int row = i / cardsPerRow;
            int col = i % cardsPerRow;
            int x = startX + col * (CardW + CardGap);
            int y = startY + row * (CardH + CardGap);
            CardBounds.Add(new SKRect(x, y, x + CardW, y + CardH));
        }

        int btnY = startY + totalHeight + 40;
        StartButtonBounds = new SKRect(
            clientWidth / 2f - 90,
            btnY,
            clientWidth / 2f + 90,
            btnY + 44);
    }

    public bool HandleClick(int x, int y)
    {
        if (!Visible) return false;

        for (int i = 0; i < CardBounds.Count; i++)
        {
            if (CardBounds[i].Contains(x, y))
            {
                SelectedIndex = i;
                return true;
            }
        }

        if (StartButtonBounds.Contains(x, y))
        {
            if (SelectedIndex >= 0 && SelectedIndex < Entries.Count)
            {
                _onStart(Entries[SelectedIndex]);
            }
            return true;
        }

        return true;
    }

    public void Draw(SKCanvas canvas, int clientWidth, int clientHeight, SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint, SKPaint titlePaint)
    {
        if (!Visible) return;

        UiPanel.DrawFullScreenDim(canvas, clientWidth, clientHeight, UiTheme.BgOverlayDim, fillPaint);

        SKColor prev = textPaint.Color;
        titlePaint.Color = UiTheme.TextStrong;
        float titleTw = titlePaint.MeasureText("ANTS");
        canvas.DrawText("ANTS", clientWidth / 2f - titleTw / 2f, 90, titlePaint);

        textPaint.Color = UiTheme.TextMuted;
        float subTw = textPaint.MeasureText("Choose a map");
        canvas.DrawText("Choose a map", clientWidth / 2f - subTw / 2f, 120, textPaint);

        if (Entries.Count == 0)
        {
            textPaint.Color = UiTheme.TextMuted;
            string msg = "No maps found in /Maps";
            float tw = textPaint.MeasureText(msg);
            canvas.DrawText(msg, clientWidth / 2f - tw / 2f, clientHeight / 2f, textPaint);
            textPaint.Color = prev;
            return;
        }

        for (int i = 0; i < Entries.Count; i++)
        {
            SKRect card = CardBounds[i];
            bool selected = (i == SelectedIndex);

            fillPaint.Color = selected ? UiTheme.BgPanelActive : UiTheme.BgPanel;
            SKRoundRect rr = new SKRoundRect(card, UiTheme.CornerMedium, UiTheme.CornerMedium);
            canvas.DrawRoundRect(rr, fillPaint);

            borderPaint.Color = selected ? UiTheme.BorderStrong : UiTheme.BorderSubtle;
            borderPaint.StrokeWidth = selected ? UiTheme.BorderNormal : UiTheme.BorderThin;
            canvas.DrawRoundRect(rr, borderPaint);
            rr.Dispose();

            if (Entries[i].Bitmap != null)
            {
                SKBitmap bmp = Entries[i].Bitmap!;
                float padding = 10f;
                float avail = card.Width - padding * 2f;
                float previewH = card.Height - padding * 2f - 22f;
                float scale = Math.Min(avail / bmp.Width, previewH / bmp.Height);
                float drawW = bmp.Width * scale;
                float drawH = bmp.Height * scale;
                float dx = card.Left + (card.Width - drawW) / 2f;
                float dy = card.Top + padding + (previewH - drawH) / 2f;
                canvas.DrawBitmap(bmp, new SKRect(dx, dy, dx + drawW, dy + drawH));
            }

            textPaint.Color = selected ? UiTheme.TextStrong : UiTheme.TextBody;
            float nameTw = textPaint.MeasureText(Entries[i].DisplayName);
            float nameX = card.MidX - nameTw / 2f;
            float nameY = card.Bottom - 8f;
            canvas.DrawText(Entries[i].DisplayName, nameX, nameY, textPaint);
        }

        bool canStart = (SelectedIndex >= 0 && SelectedIndex < Entries.Count);
        fillPaint.Color = canStart ? UiTheme.BgPanelActive : UiTheme.BgPanel;
        SKRoundRect sbRr = new SKRoundRect(StartButtonBounds, UiTheme.CornerLarge, UiTheme.CornerLarge);
        canvas.DrawRoundRect(sbRr, fillPaint);
        borderPaint.Color = canStart ? UiTheme.BorderStrong : UiTheme.BorderSubtle;
        borderPaint.StrokeWidth = UiTheme.BorderNormal;
        canvas.DrawRoundRect(sbRr, borderPaint);
        sbRr.Dispose();

        textPaint.Color = canStart ? UiTheme.TextStrong : UiTheme.TextMuted;
        float stw = textPaint.MeasureText("Start");
        canvas.DrawText("Start",
            StartButtonBounds.MidX - stw / 2f,
            StartButtonBounds.MidY + textPaint.TextSize * 0.35f,
            textPaint);

        textPaint.Color = prev;
    }

    public void Dispose()
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            Entries[i].Bitmap?.Dispose();
            Entries[i].Bitmap = null;
        }
        Entries.Clear();
    }

    private static string PrettyName(string fileStem)
    {
        if (string.IsNullOrEmpty(fileStem)) return fileStem;
        char[] chars = fileStem.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '_' || chars[i] == '-') chars[i] = ' ';
        }
        string cleaned = new string(chars);
        string[] parts = cleaned.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
        }
        return string.Join(" ", parts);
    }
}
