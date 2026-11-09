using System.Drawing.Drawing2D;

namespace SngTrans.Ui;

/// <summary>Cờ quốc gia vẽ bằng GDI+ (không cần thư viện SVG). Toạ độ cờ theo khung 30x20.</summary>
public static class Flags
{
    static readonly Dictionary<(string, int, int), Bitmap> Cache = new();

    public static Bitmap Get(string? code, int w, int h)
    {
        var key = (code ?? "", w, h);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var bmp)) return bmp;
            bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var clip = Gfx.Round(new RectangleF(0, 0, w, h), Math.Max(2f, h * 0.2f)))
                    g.SetClip(clip);
                g.ScaleTransform(w / 30f, h / 20f);
                Paint(g, code);
            }
            return Cache[key] = bmp;
        }
    }

    static Color C(string hex) => ColorTranslator.FromHtml(hex);

    static void R(Graphics g, string hex, float x, float y, float w, float h)
    {
        using var b = new SolidBrush(C(hex));
        g.FillRectangle(b, x, y, w, h);
    }

    static void Circle(Graphics g, string hex, float cx, float cy, float r)
    {
        using var b = new SolidBrush(C(hex));
        g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
    }

    static void Ring(Graphics g, string hex, float cx, float cy, float r, float width)
    {
        using var p = new Pen(C(hex), width);
        g.DrawEllipse(p, cx - r, cy - r, 2 * r, 2 * r);
    }

    static void Line(Graphics g, string hex, float width, float x1, float y1, float x2, float y2)
    {
        using var p = new Pen(C(hex), width);
        g.DrawLine(p, x1, y1, x2, y2);
    }

    static void Poly(Graphics g, string hex, params float[] xy)
    {
        var pts = new PointF[xy.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[2 * i], xy[2 * i + 1]);
        using var b = new SolidBrush(C(hex));
        g.FillPolygon(b, pts);
    }

    static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "vi", "en", "ja", "ko", "zh", "zh-CN", "zh-TW", "fr", "de", "es", "pt", "it", "ru", "th", "id", "ar", "hi",
        "tr", "nl", "pl", "uk", "sv", "ms", "tl", "el", "cs", "he",
    };

    static string Resolve(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "globe";
        if (Known.Contains(code)) return code;
        var b = Langs.Base(code);
        if (b == "iw") b = "he";
        return Known.Contains(b) ? b : "globe";
    }

    static void Paint(Graphics g, string? code)
    {
        switch (Resolve(code))
        {
            case "vi":
                R(g, "#da251d", 0, 0, 30, 20);
                Poly(g, "#ffeb00", 15, 4, 16.53f, 8.4f, 21.18f, 8.49f, 17.47f, 11.3f, 18.82f, 15.76f, 15, 13.1f,
                    11.18f, 15.76f, 12.53f, 11.3f, 8.82f, 8.49f, 13.47f, 8.4f);
                break;
            case "en":
                R(g, "#012169", 0, 0, 30, 20);
                Line(g, "#ffffff", 4f, 0, 0, 30, 20); Line(g, "#ffffff", 4f, 30, 0, 0, 20);
                Line(g, "#c8102e", 1.6f, 0, 0, 30, 20); Line(g, "#c8102e", 1.6f, 30, 0, 0, 20);
                Line(g, "#ffffff", 6f, 15, 0, 15, 20); Line(g, "#ffffff", 6f, 0, 10, 30, 10);
                Line(g, "#c8102e", 3.4f, 15, 0, 15, 20); Line(g, "#c8102e", 3.4f, 0, 10, 30, 10);
                break;
            case "ja":
                R(g, "#ffffff", 0, 0, 30, 20);
                Circle(g, "#bc002d", 15, 10, 6);
                break;
            case "ko":
                R(g, "#ffffff", 0, 0, 30, 20);
                Circle(g, "#cd2e3a", 15, 10, 5);
                using (var b = new SolidBrush(C("#0047a0"))) g.FillPie(b, 10, 5, 10, 10, 0, 180);
                foreach (var (x1, y1, x2, y2) in new[]
                         { (4f, 4f, 7f, 6f), (4.8f, 2.8f, 7.8f, 4.8f), (26f, 4f, 23f, 6f), (25.2f, 2.8f, 22.2f, 4.8f),
                           (4f, 16f, 7f, 14f), (4.8f, 17.2f, 7.8f, 15.2f), (26f, 16f, 23f, 14f), (25.2f, 17.2f, 22.2f, 15.2f) })
                    Line(g, "#000000", 1f, x1, y1, x2, y2);
                break;
            case "zh": case "zh-CN":
                R(g, "#de2910", 0, 0, 30, 20);
                Poly(g, "#ffde00", 6, 2.4f, 6.85f, 4.83f, 9.42f, 4.89f, 7.38f, 6.45f, 8.12f, 8.91f, 6, 7.45f,
                    3.88f, 8.91f, 4.62f, 6.45f, 2.58f, 4.89f, 5.15f, 4.83f);
                Circle(g, "#ffde00", 11, 2.5f, .9f); Circle(g, "#ffde00", 13, 5, .9f);
                Circle(g, "#ffde00", 13, 8, .9f); Circle(g, "#ffde00", 11, 10.5f, .9f);
                break;
            case "zh-TW":
                R(g, "#fe0000", 0, 0, 30, 20);
                R(g, "#000095", 0, 0, 15, 10);
                Circle(g, "#ffffff", 7.5f, 5, 3.2f); Circle(g, "#000095", 7.5f, 5, 2.2f); Circle(g, "#ffffff", 7.5f, 5, 1.8f);
                break;
            case "fr":
                R(g, "#0055a4", 0, 0, 10, 20); R(g, "#ffffff", 10, 0, 10, 20); R(g, "#ef4135", 20, 0, 10, 20);
                break;
            case "de":
                R(g, "#000000", 0, 0, 30, 7); R(g, "#dd0000", 0, 6.67f, 30, 6.67f); R(g, "#ffce00", 0, 13.33f, 30, 6.67f);
                break;
            case "es":
                R(g, "#aa151b", 0, 0, 30, 20); R(g, "#f1bf00", 0, 5, 30, 10);
                break;
            case "pt":
                R(g, "#ff0000", 0, 0, 30, 20); R(g, "#006600", 0, 0, 12, 20);
                Circle(g, "#ffe000", 12, 10, 3.4f); Circle(g, "#ffffff", 12, 10, 2);
                break;
            case "it":
                R(g, "#009246", 0, 0, 10, 20); R(g, "#ffffff", 10, 0, 10, 20); R(g, "#ce2b37", 20, 0, 10, 20);
                break;
            case "ru":
                R(g, "#ffffff", 0, 0, 30, 20); R(g, "#0039a6", 0, 6.67f, 30, 6.67f); R(g, "#d52b1e", 0, 13.33f, 30, 6.67f);
                break;
            case "th":
                R(g, "#a51931", 0, 0, 30, 20); R(g, "#f4f5f8", 0, 3.3f, 30, 13.4f); R(g, "#2d2a4a", 0, 6.6f, 30, 6.8f);
                break;
            case "id":
                R(g, "#ff0000", 0, 0, 30, 10); R(g, "#ffffff", 0, 10, 30, 10);
                break;
            case "ar":
                R(g, "#006c35", 0, 0, 30, 20);
                Gfx.FillRound(g, C("#ffffff"), new RectangleF(7, 7, 16, 1.6f), .8f);
                Gfx.FillRound(g, C("#ffffff"), new RectangleF(8, 11, 14, 1.4f), .7f);
                break;
            case "hi":
                R(g, "#ff9933", 0, 0, 30, 7); R(g, "#ffffff", 0, 6.67f, 30, 6.67f); R(g, "#138808", 0, 13.33f, 30, 6.67f);
                Ring(g, "#000080", 15, 10, 2.2f, .8f);
                break;
            case "tr":
                R(g, "#e30a17", 0, 0, 30, 20);
                Circle(g, "#ffffff", 11, 10, 5); Circle(g, "#e30a17", 12.4f, 10, 4f); Circle(g, "#ffffff", 17.5f, 10, 1.7f);
                break;
            case "nl":
                R(g, "#ae1c28", 0, 0, 30, 7); R(g, "#ffffff", 0, 6.67f, 30, 6.67f); R(g, "#21468b", 0, 13.33f, 30, 6.67f);
                break;
            case "pl":
                R(g, "#ffffff", 0, 0, 30, 10); R(g, "#dc143c", 0, 10, 30, 10);
                break;
            case "uk":
                R(g, "#0057b7", 0, 0, 30, 10); R(g, "#ffd700", 0, 10, 30, 10);
                break;
            case "sv":
                R(g, "#006aa7", 0, 0, 30, 20); R(g, "#fecc02", 9, 0, 3.6f, 20); R(g, "#fecc02", 0, 8.2f, 30, 3.6f);
                break;
            case "ms":
                R(g, "#cc0001", 0, 0, 30, 20);
                for (int i = 1; i < 14; i += 2) R(g, "#ffffff", 0, i * 20f / 14, 30, 20f / 14);
                R(g, "#010066", 0, 0, 15, 10.7f);
                Circle(g, "#ffcc00", 7, 5.3f, 3.2f); Circle(g, "#010066", 8, 5.3f, 2.6f);
                break;
            case "tl":
                R(g, "#0038a8", 0, 0, 30, 10); R(g, "#ce1126", 0, 10, 30, 10);
                Poly(g, "#ffffff", 0, 0, 13, 10, 0, 20);
                Circle(g, "#fcd116", 4, 10, 1.9f);
                break;
            case "el":
                R(g, "#0d5eaf", 0, 0, 30, 20);
                for (int i = 1; i < 9; i += 2) R(g, "#ffffff", 0, i * 20f / 9, 30, 20f / 9);
                R(g, "#0d5eaf", 0, 0, 11.1f, 11.1f);
                R(g, "#ffffff", 4.4f, 0, 2.3f, 11.1f); R(g, "#ffffff", 0, 4.4f, 11.1f, 2.3f);
                break;
            case "cs":
                R(g, "#ffffff", 0, 0, 30, 10); R(g, "#d7141a", 0, 10, 30, 10);
                Poly(g, "#11457e", 0, 0, 14, 10, 0, 20);
                break;
            case "he":
                R(g, "#ffffff", 0, 0, 30, 20);
                R(g, "#0038b8", 0, 2.5f, 30, 2.8f); R(g, "#0038b8", 0, 14.7f, 30, 2.8f);
                Ring(g, "#0038b8", 15, 10, 2.8f, .9f);
                break;
            default:    // địa cầu (Tự động)
                R(g, "#2a3a6b", 0, 0, 30, 20);
                Ring(g, "#c7d2fe", 15, 10, 6, 1.4f);
                using (var p = new Pen(C("#c7d2fe"), 1.1f))
                {
                    g.DrawEllipse(p, 15 - 2.6f, 10 - 6f, 5.2f, 12f);
                    g.DrawLine(p, 9, 10, 21, 10);
                }
                break;
        }
    }
}
