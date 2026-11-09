using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace SngTrans.Ui;

/// <summary>Nền cho điều khiển tự vẽ: hai lớp đệm, nền trong suốt, không nhận focus.</summary>
public class Ctl : Control
{
    /// <summary>Hệ số theo DPI (1.0 = 96 dpi). Chủ sở hữu đặt khi bố trí.</summary>
    public float S { get; set; } = 1f;
    /// <summary>Cho phép kéo cả cửa sổ cha khi bấm lên điều khiển này (dùng cho thẻ dịch).</summary>
    public bool PassThrough { get; set; }
    protected bool Hover;

    public Ctl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Color.Transparent;
    }

    protected int Px(float v) => (int)Math.Round(v * S);

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void WndProc(ref Message m)
    {
        if (PassThrough && m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; }     // WM_NCHITTEST -> HTTRANSPARENT
        base.WndProc(ref m);
    }
}

/// <summary>Nút phẳng: biểu tượng (phông Segoe icons) và/hoặc chữ, nền bo góc, đổi màu khi rê chuột.</summary>
public class FlatBtn : Ctl
{
    public string Glyph = "", Caption = "";
    public Color Fore = Theme.Muted, HoverFore = Color.White, Back = Color.Empty, HoverBack = Theme.BtnHover;
    public Color DisabledFore = Theme.Dim, DisabledBack = Color.Empty, CheckedFore = Color.Empty;
    public float Radius = 9, FontPx = 13, IconPx = 16;
    public bool Bold, LeftAlign, Checkable;
    bool _checked;
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
    }

    protected override void OnClick(EventArgs e)
    {
        if (Checkable) Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var back = !Enabled ? DisabledBack : Hover ? HoverBack : Back;
        Gfx.FillRound(g, back, new RectangleF(0, 0, Width, Height), Px(Radius));
        var fore = !Enabled ? DisabledFore
            : _checked && !CheckedFore.IsEmpty ? CheckedFore
            : Hover ? HoverFore : Fore;

        var gf = Theme.Icon(IconPx, S);
        var tf = Theme.Ui(FontPx, Bold, S);
        int gw = Glyph.Length > 0 ? Gfx.TextWidth(Glyph, gf) : 0;
        int tw = Caption.Length > 0 ? Gfx.TextWidth(Caption, tf) : 0;
        int gap = gw > 0 && tw > 0 ? Px(6) : 0;
        int x = LeftAlign ? 0 : Math.Max(0, (Width - (gw + gap + tw)) / 2);
        if (gw > 0)
        {
            Gfx.Text(g, Glyph, gf, new Rectangle(x, 0, gw + 2, Height), fore, Gfx.Single);
            x += gw + gap;
        }
        if (tw > 0)
            Gfx.Text(g, Caption, tf, new Rectangle(x, 0, Math.Max(1, Width - x), Height), fore, Gfx.Single | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Viên chọn ngôn ngữ: cờ + tên + mũi tên.</summary>
public sealed class Pill : Ctl
{
    public string Code = "auto", LangName = "";
    public string? FlagCode;
    public float FontPx = 14;
    public bool AutoWidth = true;
    public Color Bg = Theme.PillBg, BgHover = Theme.PillHover;
    /// <summary>Kiểu gọn (nền sáng): không cờ, chữ canh trái, có viền và mũi tên.</summary>
    public bool Compact;
    public Color Fore = Color.White, Chevron = Theme.Muted, Border = Color.Empty;

    public void SetLang(string code, string name, string? flag)
    {
        Code = code; LangName = name; FlagCode = flag;
        Remeasure();
        Invalidate();
    }

    public void Remeasure()
    {
        if (AutoWidth)
            Width = Gfx.TextWidth(LangName, Theme.Ui(FontPx, false, S)) + Px(9 + 22 + 8 + 8 + 12 + 9 + 4);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float rad = Px(Compact ? 6 : 11);
        Gfx.FillRound(g, Hover ? BgHover : Bg, new RectangleF(0, 0, Width, Height), rad);
        if (!Border.IsEmpty)
            Gfx.DrawRound(g, Border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), rad);
        int tx;
        if (Compact) tx = Px(12);
        else
        {
            int fw = Px(22), fh = Px(15);
            g.DrawImageUnscaled(Flags.Get(FlagCode, fw, fh), Px(9), (Height - fh) / 2);
            tx = Px(9 + 22 + 8);
        }
        Gfx.Text(g, LangName, Theme.Ui(FontPx, false, S), new Rectangle(tx, 0, Math.Max(1, Width - tx - Px(28)), Height),
            Fore, Gfx.Single | TextFormatFlags.EndEllipsis);
        var ic = Theme.Icon(11, S);
        Gfx.Text(g, Theme.GChevron, ic, new Rectangle(Width - Px(9 + 14), 0, Px(14), Height), Chevron,
            Gfx.Single | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>Công tắc bật/tắt kiểu iOS.</summary>
public sealed class Toggle : Ctl
{
    bool _on;
    public event EventHandler? CheckedChanged;
    public Color OnBack = Theme.Accent, OffBack = Theme.C("#2f3b60"), OnKnob = Color.White, OffKnob = Color.White, OffBorder = Color.Empty;

    public bool Checked
    {
        get => _on;
        set { if (_on != value) { _on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
    }

    public void SetSilently(bool v) { _on = v; Invalidate(); }

    public Toggle() { Cursor = Cursors.Hand; }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Gfx.FillRound(g, _on ? OnBack : OffBack, new RectangleF(0, 0, Width, Height), Height / 2f);
        if (!_on && !OffBorder.IsEmpty)
            Gfx.DrawRound(g, OffBorder, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), (Height - 1) / 2f);
        int m = OffBorder.IsEmpty ? 3 : 4;
        int d = Height - Px(2 * m);
        using var b = new SolidBrush(_on ? OnKnob : OffKnob);
        g.FillEllipse(b, _on ? Width - d - Px(m) : Px(m), Px(m), d, d);
    }
}

/// <summary>Ô tick có nhãn (dùng ở chân thẻ dịch).</summary>
public sealed class Check : Ctl
{
    bool _on;
    string _text = "";
    public event EventHandler? CheckedChanged;
    public float FontPx = 12;

    public Check() { Cursor = Cursors.Hand; }

    public string Caption { get => _text; set { _text = value; Invalidate(); } }
    public bool Checked
    {
        get => _on;
        set { if (_on != value) { _on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
    }
    public void SetSilently(bool v) { _on = v; Invalidate(); }

    public int PreferredWidth => Px(16 + 8) + Gfx.TextWidth(_text, Theme.Ui(FontPx, false, S)) + Px(2);

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int box = Px(16);
        var r = new RectangleF(0.5f, (Height - box) / 2f + 0.5f, box - 1, box - 1);
        if (_on) Gfx.FillRound(g, Theme.Accent, r, Px(5));
        Gfx.DrawRound(g, _on ? Theme.Accent : (Hover ? Theme.C("#6a7bb0") : Theme.C("#3a4668")), r, Px(5));
        if (_on)
            Gfx.Text(g, Theme.GCheck, Theme.Icon(10, S), Rectangle.Round(r), Color.White,
                Gfx.Single | TextFormatFlags.HorizontalCenter);
        Gfx.Text(g, _text, Theme.Ui(FontPx, false, S), new Rectangle(box + Px(8), 0, Math.Max(1, Width - box - Px(8)), Height),
            Theme.Dim, Gfx.Single | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Khung chờ khi đang dịch: vài thanh chữ mờ có vệt sáng chạy ngang.</summary>
public sealed class Skeleton : Ctl
{
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    float _t;

    public Skeleton()
    {
        BackColor = Theme.CardBg;
        _timer.Tick += (_, _) => { _t = (_t + 0.02f) % 1f; Invalidate(); };
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.CardBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float w = Width;
        float x0 = -w * 0.7f + _t * w * 2.1f;
        using var br = new LinearGradientBrush(new PointF(x0, 0), new PointF(x0 + w * 0.7f, 0), Theme.Skel1, Theme.Skel1)
        {
            InterpolationColors = new ColorBlend
            {
                Positions = new[] { 0f, 0.5f, 1f },
                Colors = new[] { Theme.Skel1, Theme.Skel2, Theme.Skel1 },
            },
        };
        float s = S;
        foreach (var (x, y, ww, h, r) in new (float, float, float, float, float)[]
                 { (0, 0, 112, 32, 11), (122, 0, 112, 32, 11), (0, 50, w / s * 0.52f, 19, 8),
                   (0, 80, w / s * 0.97f, 13, 6.5f), (0, 100, w / s * 0.68f, 13, 6.5f) })
        {
            using var p = Gfx.Round(new RectangleF(x * s, y * s, ww * s, h * s), r * s);
            g.FillPath(br, p);
        }
    }
}

/// <summary>Một đoạn chữ trong vùng hiển thị bản dịch.</summary>
public sealed class Block
{
    public string Text = "";
    public string Lead = "";          // phần dẫn đầu (từ loại, dấu •), vẽ thụt vào bên trái
    public float Px = 14;
    public bool Bold, Italic;
    public Color Color = Theme.Txt, LeadColor = Theme.Dim;
    public float Before, After;       // khoảng cách trên/dưới (đơn vị 96 dpi)
}

/// <summary>Vùng hiển thị bản dịch: nhiều đoạn có kiểu khác nhau, cuộn bằng con lăn, thanh cuộn mảnh.</summary>
public sealed class RichView : Ctl
{
    List<Block> _blocks = new();
    int _scroll, _contentH, _laidWidth = -1;
    readonly List<(int Y, int H, int LeadW)> _lay = new();
    public int RightPad = 14;

    public RichView() { BackColor = Theme.CardBg; }

    static readonly Dictionary<Font, Font> ItalicCache = new();

    Font Fnt(Block b, bool lead)
    {
        var f = Theme.Ui(b.Px, b.Bold, S);
        if (!(lead && b.Italic)) return f;
        lock (ItalicCache)
        {
            if (!ItalicCache.TryGetValue(f, out var it)) ItalicCache[f] = it = new Font(f, FontStyle.Italic);
            return it;
        }
    }

    /// <summary>Đặt nội dung, trả về tổng chiều cao khi bề ngang là width.</summary>
    public int SetBlocks(List<Block> blocks, int width)
    {
        _blocks = blocks;
        _scroll = 0;
        _laidWidth = -1;
        return DoLayout(width);
    }

    int DoLayout(int width)
    {
        _lay.Clear();
        int textW = Math.Max(20, width - Px(RightPad));
        int y = 0;
        foreach (var b in _blocks)
        {
            y += Px(b.Before);
            int leadW = b.Lead.Length > 0 ? Gfx.TextWidth(b.Lead, Fnt(b, true)) + Px(8) : 0;
            int h = Math.Max(Fnt(b, false).Height, Gfx.Measure(b.Text, Fnt(b, false), Math.Max(10, textW - leadW)).Height);
            _lay.Add((y, h, leadW));
            y += h + Px(b.After);
        }
        _laidWidth = width;
        _contentH = y;
        return y;
    }

    int MaxScroll => Math.Max(0, _contentH - Height);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - e.Delta / 120 * Px(40)));
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.CardBg);
        if (_laidWidth != Width) DoLayout(Width);
        int textW = Math.Max(20, Width - Px(RightPad));
        for (int i = 0; i < _blocks.Count; i++)
        {
            var b = _blocks[i];
            var (y, h, leadW) = _lay[i];
            int top = y - _scroll;
            if (top + h < 0 || top > Height) continue;
            if (leadW > 0)
                Gfx.Text(g, b.Lead, Fnt(b, true), new Rectangle(0, top, leadW, h), b.LeadColor, Gfx.Wrap);
            Gfx.Text(g, b.Text, Fnt(b, false), new Rectangle(leadW, top, Math.Max(10, textW - leadW), h), b.Color, Gfx.Wrap);
        }
        if (_contentH > Height)
        {
            float th = Math.Max(Px(28), Height * (float)Height / _contentH);
            float ty = MaxScroll == 0 ? 0 : (Height - th) * _scroll / MaxScroll;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Gfx.FillRound(g, Theme.C("#34415f"), new RectangleF(Width - Px(6), ty, Px(4), th), Px(2));
        }
    }
}

/// <summary>Ô nhập chữ nền tối, bo góc, viền sáng khi đang gõ.</summary>
public sealed class FieldBox : Ctl
{
    public readonly TextBox Inner = new();
    public float FontPx = 14;
    public string Placeholder = "";
    public Color Fill = Theme.Field, Border = Theme.FieldBorder, FocusBorder = Theme.Accent, Fore = Color.White;

    /// <summary>Áp lại màu sau khi đổi Fill/Fore (dùng cho nền sáng).</summary>
    public void ApplyColors()
    {
        Inner.BackColor = Fill;
        Inner.ForeColor = Fore;
        Invalidate();
    }

    public FieldBox()
    {
        Inner.BorderStyle = BorderStyle.None;
        Inner.BackColor = Theme.Field;
        Inner.ForeColor = Color.White;
        Controls.Add(Inner);
        Inner.GotFocus += (_, _) => Invalidate();
        Inner.LostFocus += (_, _) => Invalidate();
        Inner.TextChanged += (_, _) => Invalidate();
        SetStyle(ControlStyles.Selectable, false);
    }

    [AllowNull]
    public override string Text { get => Inner.Text; set => Inner.Text = value ?? ""; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Inner.Font = Theme.Ui(FontPx, false, S);
        Inner.PlaceholderText = Placeholder;
        int h = Inner.PreferredHeight;
        Inner.SetBounds(Px(12), Math.Max(0, (Height - h) / 2), Math.Max(10, Width - Px(24)), h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(g, Fill, r, Px(10));
        Gfx.DrawRound(g, Inner.Focused ? FocusBorder : Border, r, Px(10), Inner.Focused ? 1.5f : 1f);
    }
}

/// <summary>Panel cuộn dọc có thanh cuộn tối.</summary>
public sealed class ScrollPage : Panel
{
    readonly bool _light;

    public ScrollPage() : this(Theme.Bg, false) { }

    public ScrollPage(Color back, bool light)
    {
        AutoScroll = true;
        DoubleBuffered = true;
        BackColor = back;
        _light = light;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000;       // WS_EX_COMPOSITED: vẽ cả cây điều khiển qua một lớp đệm, hết vệt mờ khi cuộn
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x115 || m.Msg == 0x20A)       // WM_VSCROLL / WM_MOUSEWHEEL: vẽ lại ngay, không để sót hình cũ
        {
            Invalidate(true);
            Update();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_light) Win32.LightScrollbars(Handle); else Win32.DarkScrollbars(Handle);
    }

    /// <summary>Đặt chiều cao nội dung, giữ nguyên vị trí cuộn.</summary>
    public void SetContentHeight(int h)
    {
        AutoScrollMinSize = new Size(0, h);
    }

    /// <summary>Chạy bố trí lại mà không làm lệch vị trí cuộn hiện tại.</summary>
    public void Relayout(Action layout)
    {
        int y = -AutoScrollPosition.Y;
        SuspendLayout();
        AutoScrollPosition = Point.Empty;
        layout();
        ResumeLayout(true);
        AutoScrollPosition = new Point(0, y);
    }
}

/// <summary>Nhãn chữ mà bấm vào sẽ kéo được cửa sổ cha (thẻ dịch).</summary>
public sealed class DragLabel : Label
{
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; }     // HTTRANSPARENT
        base.WndProc(ref m);
    }
}
