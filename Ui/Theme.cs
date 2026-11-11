using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SngTrans.Ui;

/// <summary>Màu, phông chữ và hàm vẽ dùng chung.</summary>
public static class Theme
{
    public static Color C(string hex) => ColorTranslator.FromHtml(hex);

    // thẻ dịch
    // (thẻ dịch và menu khay dùng chung bảng màu tối xanh navy này; chỉnh sáng/tối ở CardBg)
    public static readonly Color CardBg = C("#151c33");
    public static readonly Color CardBorder = C("#2a3454");
    public static readonly Color Txt = C("#f4f6fb");
    public static readonly Color CardBody = C("#d5dcf5");
    public static readonly Color CardMuted = C("#9aa6c4");
    public static readonly Color CardDim = C("#7b87a8");
    public static readonly Color CardHover = C("#212b4d");
    public static readonly Color CardErr = C("#fca5a5");
    public static readonly Color SrcBg = C("#202a4a");
    public static readonly Color SrcHover = C("#2a3660");
    public static readonly Color SrcFore = C("#e8ecf8");
    public static readonly Color Primary = C("#2563eb");
    public static readonly Color PrimaryHover = C("#1d4ed8");
    public static readonly Color Skel1 = C("#202a4a");
    public static readonly Color Skel2 = C("#2f3c6a");

    public static readonly Color Muted = C("#8f9bb8");
    public static readonly Color Dim = C("#6b7799");
    public static readonly Color PillBg = C("#1a2342");
    public static readonly Color PillHover = C("#232f57");
    public static readonly Color BtnHover = C("#1d2748");
    public static readonly Color PickerBg = C("#101831");
    public static readonly Color Accent = C("#3b82f6");
    public static readonly Color Link = C("#93c5fd");

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

    /// <summary>Bảng màu nền sáng kiểu Windows 11 (hộp Cài đặt).</summary>
    public static class Light
    {
        public static readonly Color Bg = C("#f3f3f3");
        public static readonly Color Card = C("#ffffff");
        public static readonly Color Border = C("#e3e3e3");
        public static readonly Color NavSel = C("#e6e6e6");
        public static readonly Color NavHover = C("#ebebeb");
        public static readonly Color Text = C("#1b1b1b");
        public static readonly Color Sub = C("#5f5f5f");
        public static readonly Color Muted = C("#8a8a8a");
        public static readonly Color Accent = C("#0f6cbd");
        public static readonly Color AccentHover = C("#115ea3");
        public static readonly Color Field = C("#fbfbfb");
        public static readonly Color FieldHover = C("#f0f0f0");
        public static readonly Color FieldBorder = C("#d1d1d1");
        public static readonly Color Danger = C("#c42b1c");
        public static readonly Color DangerBack = C("#fde7e9");
        public static readonly Color Info = C("#eaf3fc");
        public static readonly Color InfoBorder = C("#c4dcf2");
        public static readonly Color Ok = C("#0f7b0f");
    }

    static readonly string[] UiFamilies = { "Segoe UI Variable Text", "Segoe UI", "Inter", "Helvetica Neue", "Arial" };
    static string? _ui, _icon;
    static readonly Dictionary<(int, bool, string), Font> Cache = new();

    /// <summary>Kiểm tra phông có cài không bằng cách mở thẳng theo tên (nhanh hơn nhiều so với liệt kê
    /// toàn bộ phông trên máy bằng InstalledFontCollection, vốn chậm lúc vừa đăng nhập).</summary>
    static bool Has(string name)
    {
        try { using var f = new FontFamily(name); return true; }
        catch (ArgumentException) { return false; }
    }

    public static string UiFamily
    {
        get
        {
            if (_ui != null) return _ui;
            _ui = UiFamilies.FirstOrDefault(Has) ?? "Segoe UI";
            return _ui;
        }
    }

    /// <summary>Phông biểu tượng: Segoe Fluent Icons (Win11) hoặc Segoe MDL2 Assets (Win10).</summary>
    public static string IconFamily
    {
        get
        {
            if (_icon != null) return _icon;
            _icon = Has("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
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
        GCheck = "\uE73E", GChevron = "\uE70D", GForward = "\uE72A", GDelete = "\uE74D",
        GSettings = "\uE713", GBlock = "\uE733", GNote = "\uE736", GPower = "\uE7E8", GGlobe = "\uE774",
        GCursor = "\uE8B3", GSwap = "\uE8AB", GKeyboard = "\uE765", GPlay = "\uE768", GEdit = "\uE70F", GInfo = "\uE946", GHelp = "\uE9CE";

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
