using System.Drawing.Drawing2D;

namespace SngTrans.Ui;

/// <summary>Hàm vẽ nhỏ dùng chung.</summary>
public static class Gfx
{
    public const TextFormatFlags Single =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
    public const TextFormatFlags Wrap =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top;

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        if (c.IsEmpty || c.A == 0) return;
        using var p = Round(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    public static void DrawRound(Graphics g, Color c, RectangleF r, float radius, float width = 1f)
    {
        using var p = Round(r, radius);
        using var pen = new Pen(c, width);
        g.DrawPath(pen, p);
    }

    public static Size Measure(string text, Font font, int width = int.MaxValue, TextFormatFlags flags = Wrap)
    {
        if (string.IsNullOrEmpty(text)) return new Size(0, 0);
        return TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), flags);
    }

    public static int TextWidth(string text, Font font) =>
        string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;

    public static void Text(Graphics g, string text, Font font, Rectangle r, Color c, TextFormatFlags flags) =>
        TextRenderer.DrawText(g, text, font, r, c, flags);

    /// <summary>Cắt chữ cho vừa maxLines dòng (đo bằng phông thật). Trả về (chữ đã cắt, số dòng).</summary>
    public static (string Text, int Lines) FitLines(string text, Font font, int width, int maxLines)
    {
        int probe = Math.Max(20, width - 6);
        int lineH = Math.Max(1, font.Height);

        int Lines(string s)
        {
            int h = Measure(s, font, probe).Height;
            return Math.Max(1, (int)Math.Round(h / (double)lineH));
        }

        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        int n = Lines(text);
        if (n <= maxLines) return (text, n);
        int lo = 1, hi = text.Length;
        while (lo < hi)                                  // tìm đoạn đầu dài nhất còn vừa
        {
            int mid = (lo + hi + 1) / 2;
            if (Lines(text[..mid].TrimEnd() + "…") <= maxLines) lo = mid;
            else hi = mid - 1;
        }
        return (text[..lo].TrimEnd() + "…", maxLines);
    }
}
