using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SngTrans.Ui;

/// <summary>Màu, phông chữ và hàm vẽ dùng chung.</summary>
public static class Theme
{
    public static Color C(string hex) => ColorTranslator.FromHtml(hex);

    // thẻ dịch
    public static readonly Color CardBg = C("#121a30");
    public static readonly Color CardBorder = C("#252d47");
    public static readonly Color Txt = C("#f4f6fb");
    public static readonly Color Muted = C("#8f9bb8");
    public static readonly Color Dim = C("#6b7799");
    public static readonly Color PillBg = C("#1a2342");
    public static readonly Color PillHover = C("#232f57");
    public static readonly Color BtnHover = C("#1d2748");
    public static readonly Color Skel1 = C("#1a2342");
    public static readonly Color Skel2 = C("#2c3a69");
    public static readonly Color PickerBg = C("#101831");
    public static readonly Color Accent = C("#3b82f6");
    public static readonly Color Link = C("#93c5fd");
    public static readonly Color Err = C("#fca5a5");

    // hộp cài đặt
    public static readonly Color Bg = C("#0b1222");
    public static readonly Color Card = C("#121b33");
    public static readonly Color Line = C("#1f2b4d");
    public static readonly Color Field = C("#1b2542");
    public static readonly Color FieldBorder = C("#2a3558");
    public static readonly Color FieldHover = C("#26335c");
    public static readonly Color Label = C("#eef2ff");
    public static readonly Color SubLabel = C("#8b94ad");
    public static readonly Color Section = C("#6f7fa8");

    static readonly string[] UiFamilies = { "Segoe UI Variable Text", "Segoe UI", "Inter", "Helvetica Neue", "Arial" };
    static string? _ui, _icon;
    static readonly Dictionary<(int, bool, string), Font> Cache = new();

    static bool Has(InstalledFontCollection fc, string name) =>
        fc.Families.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string UiFamily
    {
        get
        {
            if (_ui != null) return _ui;
            using var fc = new InstalledFontCollection();
            _ui = UiFamilies.FirstOrDefault(n => Has(fc, n)) ?? "Segoe UI";
            return _ui;
        }
    }

    /// <summary>Phông biểu tượng: Segoe Fluent Icons (Win11) hoặc Segoe MDL2 Assets (Win10).</summary>
    public static string IconFamily
    {
        get
        {
            if (_icon != null) return _icon;
            using var fc = new InstalledFontCollection();
            _icon = Has(fc, "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            return _icon;
        }
    }

    static Font Make(string family, float px, bool bold, float scale, string tag)
    {
        var size = Math.Max(4f, px * scale);
        var key = ((int)Math.Round(size * 10), bold, tag);
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var f))
                Cache[key] = f = new Font(family, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            return f;
        }
    }

    public static Font Ui(float px, bool bold = false, float scale = 1f) => Make(UiFamily, px, bold, scale, "ui");
    public static Font Icon(float px, float scale = 1f) => Make(IconFamily, px, false, scale, "icon");
    public static Font Mono(float px, float scale = 1f) => Make("Consolas", px, false, scale, "mono");
    public static Font Symbol(float px, float scale = 1f) => Make("Segoe UI Symbol", px, false, scale, "sym");

    // Ký tự trong phông biểu tượng
    public const string GCancel = "\uE711", GPin = "\uE718", GVolume = "\uE767", GCopy = "\uE8C8",
        GCheck = "\uE73E", GChevron = "\uE70D", GForward = "\uE72A", GDelete = "\uE74D";

    /// <summary>Biểu tượng ứng dụng: ô vuông bo góc xanh với chữ A trắng.</summary>
    public static Bitmap LogoBitmap(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var path = Gfx.Round(new RectangleF(0, 0, size, size), size * 0.26f))
        using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(
                   new PointF(0, 0), new PointF(size, size), C("#60a5fa"), C("#3b5bdb")))
            g.FillPath(br, path);
        using var f = new Font(UiFamily, size * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("A", f, Brushes.White, new RectangleF(0, size * 0.03f, size, size), sf);
        return bmp;
    }

    public static System.Drawing.Icon LogoIcon(int size = 32)
    {
        using var bmp = LogoBitmap(size);
        var h = bmp.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
}
