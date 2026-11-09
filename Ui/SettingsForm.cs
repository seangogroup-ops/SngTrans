using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Lt = SngTrans.Ui.Theme.Light;

namespace SngTrans.Ui;

interface ILayoutRow
{
    int DoLayout(int width, float s);
}

/// <summary>Nhãn chữ nền trong suốt.</summary>
sealed class Lbl : Label
{
    public Lbl(string text, float px, Color color, bool bold = false, float s = 1f)
    {
        AutoSize = false;
        UseMnemonic = false;
        BackColor = Color.Transparent;
        ForeColor = color;
        Text = text;
        Font = Theme.Ui(px, bold, s);
    }

    public void Restyle(float px, bool bold, float s) => Font = Theme.Ui(px, bold, s);

    public int HeightFor(int width) =>
        Math.Max(Font.Height, Gfx.Measure(Text, Font, Math.Max(10, width)).Height) + 1;
}

/// <summary>Thẻ trắng bo góc có biểu tượng bên trái (nền chung cho mọi hàng cài đặt).</summary>
abstract class CardCtl : Ctl
{
    protected string IconGlyph = "";
    protected int IconCy = -1;           // tâm dọc của biểu tượng (px), -1 = giữa thẻ

    protected void PaintCard(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(g, Lt.Card, r, Px(8));
        Gfx.DrawRound(g, Lt.Border, r, Px(8));
        if (IconGlyph.Length > 0)
        {
            int cy = IconCy >= 0 ? IconCy : Height / 2;
            Gfx.Text(g, IconGlyph, Theme.Icon(18, S), new Rectangle(Px(16), cy - Px(14), Px(26), Px(28)), Lt.Accent,
                Gfx.Single | TextFormatFlags.HorizontalCenter);
        }
    }

    protected override void OnPaint(PaintEventArgs e) => PaintCard(e.Graphics);
}

/// <summary>Một hàng cài đặt: biểu tượng, tiêu đề + mô tả bên trái, điều khiển bên phải.</summary>
sealed class SettingRow : CardCtl, ILayoutRow
{
    readonly Lbl _title, _sub;
    readonly Control _right;

    public SettingRow(string glyph, string title, string sub, Control right)
    {
        IconGlyph = glyph;
        _title = new Lbl(title, 14, Lt.Text);
        _sub = new Lbl(sub, 12, Lt.Sub);
        _right = right;
        Controls.Add(_title);
        if (sub.Length > 0) Controls.Add(_sub);
        Controls.Add(right);
    }

    public int DoLayout(int width, float s)
    {
        S = s;
        _title.Restyle(14, false, s);
        _sub.Restyle(12, false, s);
        int padL = Px(50), padR = Px(14), vpad = Px(10);
        int textW = Math.Max(Px(80), width - padL - padR - _right.Width - Px(16));
        int th = _title.HeightFor(textW);
        int sh = _sub.Text.Length > 0 ? _sub.HeightFor(textW) : 0;
        int textH = th + (sh > 0 ? sh + Px(1) : 0);
        int h = Math.Max(Math.Max(textH, _right.Height) + 2 * vpad, Px(52));
        _title.SetBounds(padL, (h - textH) / 2, textW, th);
        _sub.SetBounds(padL, (h - textH) / 2 + th + Px(1), textW, sh);
        _right.Location = new Point(width - padR - _right.Width, (h - _right.Height) / 2);
        Size = new Size(width, h);
        IconCy = h / 2;
        return h;
    }
}

/// <summary>Khung thông báo (lời chào lần đầu).</summary>
sealed class InfoBox : Ctl, ILayoutRow
{
    readonly Lbl _a = new("Chào mừng bạn!", 14, Lt.Text, true);
    readonly Lbl _b = new("Chọn ngôn ngữ bên dưới (mặc định Việt ↔ Anh), rồi bôi đen chữ ở bất kỳ đâu để dịch.", 13, Lt.Sub);

    public InfoBox() { Controls.Add(_a); Controls.Add(_b); }

    public int DoLayout(int width, float s)
    {
        S = s;
        _a.Restyle(14, true, s);
        _b.Restyle(13, false, s);
        int padL = Px(54), padR = Px(16), v = Px(12);
        int tw = width - padL - padR;
        int ah = _a.HeightFor(tw), bh = _b.HeightFor(tw);
        _a.SetBounds(padL, v, tw, ah);
        _b.SetBounds(padL, v + ah + Px(2), tw, bh);
        Size = new Size(width, v * 2 + ah + bh + Px(2));
        return Height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(g, Lt.Info, r, Px(8));
        Gfx.DrawRound(g, Lt.InfoBorder, r, Px(8));
        Gfx.Text(g, Theme.GInfo, Theme.Icon(18, S), new Rectangle(Px(16), Px(10), Px(26), Px(28)), Lt.Accent,
            Gfx.Single | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>Hàng chọn hai ngôn ngữ (mẹ đẻ ⇄ ngoại ngữ); nút khôi phục chỉ hiện khi khác mặc định.</summary>
sealed class LangRow : CardCtl, ILayoutRow
{
    public readonly Pill Native = MakePill(), Foreign = MakePill();
    public readonly FlatBtn Swap = new()
    {
        Glyph = Theme.GSwap, IconPx = 14, Radius = 6, Back = Color.Empty, HoverBack = Lt.FieldHover,
        Fore = Lt.Sub, HoverFore = Lt.Text, Cursor = Cursors.Hand,
    };
    public readonly FlatBtn Reset = new()
    {
        Caption = "Khôi phục mặc định: Tiếng Việt ↔ English", LeftAlign = true, Fore = Lt.Accent, HoverFore = Lt.AccentHover,
        HoverBack = Color.Empty, Cursor = Cursors.Hand, FontPx = 12, Visible = false,
    };
    readonly Lbl _title = new("Ngôn ngữ", 14, Lt.Text), _sub = new("Tiếng mẹ đẻ và ngoại ngữ", 12, Lt.Sub);

    public bool ShowReset { get => Reset.Visible; set => Reset.Visible = value; }

    static Pill MakePill() => new()
    {
        AutoWidth = false, Compact = true, FontPx = 13, Fore = Lt.Text, Bg = Lt.Field, BgHover = Lt.FieldHover,
        Border = Lt.FieldBorder, Chevron = Lt.Sub, Cursor = Cursors.Hand,
    };

    public LangRow()
    {
        IconGlyph = Theme.GGlobe;
        Controls.AddRange(new Control[] { _title, _sub, Native, Foreign, Swap, Reset });
    }

    public int DoLayout(int width, float s)
    {
        S = s;
        foreach (var c in new Ctl[] { Native, Foreign, Swap, Reset }) c.S = s;
        _title.Restyle(14, false, s); _sub.Restyle(12, false, s);
        int padL = Px(50), padR = Px(14), vpad = Px(10);
        int pw = Px(108), sw = Px(28), ph = Px(30), gap = Px(4);
        int ctlW = pw + gap + sw + gap + pw;
        int textW = width - padL - padR - ctlW - Px(16);
        bool stacked = textW < Px(150);
        if (stacked) textW = width - padL - padR;
        int th = _title.HeightFor(textW), sh = _sub.HeightFor(textW);
        int textH = th + sh + Px(1);
        int h, ctlY, ctlX;
        if (!stacked)
        {
            int block = Math.Max(textH, ph);
            _title.SetBounds(padL, vpad + (block - textH) / 2, textW, th);
            _sub.SetBounds(padL, vpad + (block - textH) / 2 + th + Px(1), textW, sh);
            ctlY = vpad + (block - ph) / 2;
            ctlX = width - padR - ctlW;
            h = vpad + block + vpad;
            IconCy = vpad + block / 2;
        }
        else
        {
            _title.SetBounds(padL, vpad, textW, th);
            _sub.SetBounds(padL, vpad + th + Px(1), textW, sh);
            ctlY = vpad + textH + Px(10);
            ctlX = padL;
            h = ctlY + ph + vpad;
            IconCy = vpad + textH / 2;
        }
        Native.SetBounds(ctlX, ctlY, pw, ph);
        Swap.SetBounds(ctlX + pw + gap, ctlY + (ph - Px(28)) / 2, sw, Px(28));
        Foreign.SetBounds(ctlX + pw + gap + sw + gap, ctlY, pw, ph);
        if (Reset.Visible)
        {
            int ry = h - vpad + Px(2);
            Reset.SetBounds(padL, ry, width - padL - padR, Px(22));
            h = ry + Px(22) + Px(10);
        }
        Size = new Size(width, h);
        return h;
    }
}

/// <summary>Ô chọn phím tắt: bấm rồi nhấn tổ hợp phím mới. Esc huỷ, Backspace/Delete về mặc định.</summary>
sealed class HotkeyButton : Ctl
{
    public string Value;
    bool _capturing;
    string _hint = "";
    public event Action<string>? Picked;
    public event Action<bool>? CaptureChanged;

    public HotkeyButton(string value)
    {
        Value = value;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public void SetValue(string v) { Value = v; Stop(); }

    void Start()
    {
        if (_capturing) { Stop(); return; }
        _capturing = true;
        _hint = "";
        CaptureChanged?.Invoke(true);
        Focus();
        Invalidate();
    }

    void Stop()
    {
        bool was = _capturing;
        _capturing = false;
        _hint = "";
        Invalidate();
        if (was) CaptureChanged?.Invoke(false);
    }

    protected override void OnClick(EventArgs e) { Start(); base.OnClick(e); }
    protected override void OnLostFocus(EventArgs e) { Stop(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) => _capturing || base.IsInputKey(keyData);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing) return base.ProcessCmdKey(ref msg, keyData);
        var code = keyData & Keys.KeyCode;
        var mod = keyData & Keys.Modifiers;
        if (code == Keys.F4 && (mod & Keys.Alt) != 0) return base.ProcessCmdKey(ref msg, keyData);   // Alt+F4 vẫn đóng cửa sổ
        if (code == Keys.Escape) { Stop(); return true; }
        if (code is Keys.Back or Keys.Delete)
        {
            Value = SettingsData.DefaultHotkey;
            Stop();
            Picked?.Invoke(Value);
            return true;
        }
        if (code is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None) return true;

        string? tok = null;
        if (code >= Keys.A && code <= Keys.Z) tok = ((char)('a' + (code - Keys.A))).ToString();
        else if (code >= Keys.D0 && code <= Keys.D9) tok = ((char)('0' + (code - Keys.D0))).ToString();
        else if (code >= Keys.F1 && code <= Keys.F12) tok = $"<f{code - Keys.F1 + 1}>";
        else
            tok = code switch
            {
                Keys.Space => "<space>", Keys.Enter => "<enter>", Keys.Tab => "<tab>", Keys.Left => "<left>",
                Keys.Right => "<right>", Keys.Up => "<up>", Keys.Down => "<down>", Keys.Home => "<home>",
                Keys.End => "<end>", Keys.PageUp => "<page_up>", Keys.PageDown => "<page_down>", Keys.Insert => "<insert>",
                _ => null,
            };
        if (tok == null) return true;
        bool isF = tok.StartsWith("<f");
        if (!isF && (mod & (Keys.Control | Keys.Alt)) == 0)
        {
            _hint = "Cần kèm Ctrl hoặc Alt…";
            Invalidate();
            return true;
        }
        var parts = new List<string>();
        if ((mod & Keys.Control) != 0) parts.Add("<ctrl>");
        if ((mod & Keys.Alt) != 0) parts.Add("<alt>");
        if ((mod & Keys.Shift) != 0) parts.Add("<shift>");
        parts.Add(tok);
        Value = string.Join("+", parts);
        Stop();
        Picked?.Invoke(Value);
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(g, Hover ? Lt.FieldHover : Lt.Field, r, Px(6));
        Gfx.DrawRound(g, _capturing ? Lt.Accent : Lt.FieldBorder, r, Px(6), _capturing ? 1.5f : 1f);
        string text = _capturing ? (_hint.Length > 0 ? _hint : "Nhấn phím mới… (Esc: huỷ)") : HotkeySpec.Pretty(Value);
        Gfx.Text(g, text, Theme.Ui(13, false, S), new Rectangle(Px(12), 0, Width - Px(24), Height),
            _capturing ? Lt.Accent : Lt.Text, Gfx.Single | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>Thanh bên trái: menu dọc có vạch xanh ở mục đang chọn, nút "Tắt app" màu đỏ ở đáy.</summary>
sealed class SideNav : Ctl
{
    readonly (string Glyph, string Text)[] _items;
    readonly (string Glyph, string Text) _footer;
    readonly ToolTip _tip = new();
    int _sel, _hover = -2;                 // -1 = không có, -2 = chưa rê, -3 = footer
    public string FooterTip = "";
    public event Action<int>? Selected;
    public event Action? FooterClicked;

    public SideNav((string, string)[] items, (string, string) footer)
    {
        _items = items;
        _footer = footer;
        Cursor = Cursors.Default;
    }

    int ItemH => Px(36);
    Rectangle ItemRect(int i) => new(Px(10), i * (ItemH + Px(2)), Width - Px(20), ItemH);
    Rectangle FooterRect => new(Px(10), Height - ItemH, Width - Px(20), ItemH);

    int At(Point p)
    {
        for (int i = 0; i < _items.Length; i++) if (ItemRect(i).Contains(p)) return i;
        return FooterRect.Contains(p) ? -3 : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = At(e.Location);
        if (h != _hover)
        {
            _hover = h;
            Cursor = h == -1 ? Cursors.Default : Cursors.Hand;
            _tip.SetToolTip(this, h == -3 ? FooterTip : "");
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -2; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int h = At(e.Location);
            if (h == -3) FooterClicked?.Invoke();
            else if (h >= 0 && h != _sel) { _sel = h; Invalidate(); Selected?.Invoke(h); }
        }
        base.OnMouseUp(e);
    }

    void DrawItem(Graphics g, Rectangle r, string glyph, string text, Color fore, Color iconFore)
    {
        Gfx.Text(g, glyph, Theme.Icon(16, S), new Rectangle(r.X + Px(10), r.Y, Px(26), r.Height), iconFore,
            Gfx.Single | TextFormatFlags.HorizontalCenter);
        Gfx.Text(g, text, Theme.Ui(14, false, S), new Rectangle(r.X + Px(44), r.Y, Math.Max(1, r.Width - Px(50)), r.Height), fore,
            Gfx.Single | TextFormatFlags.EndEllipsis);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < _items.Length; i++)
        {
            var r = ItemRect(i);
            bool sel = i == _sel;
            if (sel) Gfx.FillRound(g, Lt.NavSel, r, Px(6));
            else if (i == _hover) Gfx.FillRound(g, Lt.NavHover, r, Px(6));
            if (sel)
                Gfx.FillRound(g, Lt.Accent, new RectangleF(r.X, r.Y + (r.Height - Px(16)) / 2f, Px(3), Px(16)), Px(2));
            DrawItem(g, r, _items[i].Glyph, _items[i].Text, Lt.Text, sel ? Lt.Text : Lt.Sub);
        }
        var fr = FooterRect;
        if (_hover == -3) Gfx.FillRound(g, Lt.DangerBack, fr, Px(6));
        DrawItem(g, fr, _footer.Glyph, _footer.Text, Lt.Danger, Lt.Danger);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Một ghi chú trong tab Ghi chú.</summary>
sealed class VocabRow : Ctl, ILayoutRow
{
    readonly Lbl _w, _t;
    public readonly FlatBtn Speak = Icon(Theme.GVolume), Copy = Icon(Theme.GCopy), Delete = Icon(Theme.GDelete);
    readonly System.Windows.Forms.Timer _copied = new() { Interval = 1200 };

    static FlatBtn Icon(string glyph) => new()
    {
        Glyph = glyph, IconPx = 16, Radius = 6, Fore = Lt.Sub, HoverFore = Lt.Text, HoverBack = Lt.FieldHover, Cursor = Cursors.Hand,
    };

    /// <summary>Báo đã chép: đổi sang dấu tích một lúc rồi trả lại biểu tượng cũ.</summary>
    public void FlashCopied()
    {
        Copy.Glyph = Theme.GCheck; Copy.Fore = Lt.Ok; Copy.Invalidate();
        _copied.Stop(); _copied.Start();
    }

    public VocabRow(VocabItem v)
    {
        Delete.HoverFore = Lt.Danger; Delete.HoverBack = Lt.DangerBack;
        _copied.Tick += (_, _) => { _copied.Stop(); Copy.Glyph = Theme.GCopy; Copy.Fore = Lt.Sub; Copy.Invalidate(); };
        _w = new Lbl(v.Text, 15, Lt.Text, true);
        _t = new Lbl(v.Translation, 13, Lt.Accent);
        Controls.AddRange(new Control[] { _w, _t, Speak, Copy, Delete });
    }

    public int DoLayout(int width, float s)
    {
        S = s;
        Speak.S = Copy.S = Delete.S = s;
        _w.Restyle(15, true, s); _t.Restyle(13, false, s);
        int btn = Px(30), left = Px(16), tw = width - left - 3 * btn - Px(14);
        int wh = _w.HeightFor(tw), th = _t.HeightFor(tw);
        int h = Math.Max(wh + th + Px(2), btn) + Px(24);
        _w.SetBounds(left, Px(12), tw, wh);
        _t.SetBounds(left, Px(12) + wh + Px(2), tw, th);
        Speak.SetBounds(width - 3 * btn - Px(12), Px(8), btn, btn);
        Copy.SetBounds(width - 2 * btn - Px(9), Px(8), btn, btn);
        Delete.SetBounds(width - btn - Px(6), Px(8), btn, btn);
        Size = new Size(width, h);
        return h;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _copied.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(e.Graphics, Lt.Card, r, Px(8));
        Gfx.DrawRound(e.Graphics, Lt.Border, r, Px(8));
    }
}

/// <summary>Một app bị chặn: tên file .exe và nút bỏ chặn.</summary>
sealed class BlockedRow : CardCtl, ILayoutRow
{
    readonly Lbl _name;
    readonly FlatBtn _x = new()
    {
        Glyph = Theme.GCancel, IconPx = 13, Radius = 6, Fore = Lt.Sub, HoverFore = Lt.Danger, HoverBack = Lt.DangerBack, Cursor = Cursors.Hand,
    };

    public BlockedRow(string exe, Action<string> unblock)
    {
        IconGlyph = Theme.GBlock;
        _name = new Lbl(exe, 14, Lt.Text);
        _name.TextAlign = ContentAlignment.MiddleLeft;
        _x.Click += (_, _) => unblock(exe);
        Controls.Add(_name);
        Controls.Add(_x);
    }

    public int DoLayout(int width, float s)
    {
        S = s; _x.S = s;
        _name.Restyle(14, false, s);
        int h = Px(52), padL = Px(54), btn = Px(32);
        _name.SetBounds(padL, 0, Math.Max(10, width - padL - btn - Px(16)), h);
        _x.SetBounds(width - btn - Px(10), (h - btn) / 2, btn, btn);
        Size = new Size(width, h);
        IconCy = h / 2;
        return h;
    }
}

/// <summary>Hộp cài đặt kiểu Windows 11 nền sáng: thanh bên (Cài đặt / Chặn app / Ghi chú) và các thẻ cài đặt bên phải.</summary>
public sealed class SettingsForm : Form
{
    public event Action? Changed;
    public event Action? ForceQuit;
    public event Action<bool>? CapturingHotkey;

    readonly Settings _s;
    readonly Action<string, string> _speak;
    float _scale;
    int Px(float v) => (int)Math.Round(v * _scale);

    static readonly string[] PageTitles = { "Cài đặt", "Chặn app", "Ghi chú" };

    readonly PictureBox _logo = new();
    readonly Lbl _brand, _pageTitle;
    readonly SideNav _nav = new(
        new[] { (Theme.GSettings, "Cài đặt"), (Theme.GBlock, "Chặn app"), (Theme.GNote, "Ghi chú") },
        (Theme.GPower, "Tắt app"));
    readonly FlatBtn _done;
    readonly ScrollPage _general = new(Lt.Bg, true), _blockedPage = new(Lt.Bg, true), _vocabList = new(Lt.Bg, true);
    readonly Panel _vocabPage = new() { BackColor = Lt.Bg };
    readonly List<Control> _pages = new();

    // trang cài đặt
    readonly List<Control> _genItems = new();
    LangRow _lang = null!;
    readonly Dictionary<string, Toggle> _toggles = new();
    Toggle _autoStart = null!;
    HotkeyButton _hotkey = null!;
    // trang chặn app
    readonly List<BlockedRow> _blockedRows = new();
    Lbl? _blockedEmpty;
    // trang ghi chú
    readonly FieldBox _search = new()
    {
        FontPx = 14, Placeholder = "Tìm trong ghi chú…",
        Fill = Lt.Field, Border = Lt.FieldBorder, FocusBorder = Lt.Accent, Fore = Lt.Text,
    };
    readonly Lbl _count = new("", 12, Lt.Sub);
    FlatBtn _export = null!, _clear = null!;
    readonly List<VocabRow> _vocabRows = new();
    readonly System.Windows.Forms.Timer _watch = new() { Interval = 1000 };   // tự làm mới danh sách khi có ghi chú mới
    (int Count, long FirstTs) _vocabSig;
    Label? _vocabEmpty;
    LangPicker? _picker;
    bool _pickNative;

    public SettingsForm(Settings settings, Action<string, string> speak, bool welcome)
    {
        _s = settings;
        _speak = speak;
        _scale = Win32.DpiAt(Cursor.Position.X, Cursor.Position.Y) / 96f;

        AutoScaleMode = AutoScaleMode.None;
        Text = "SngTrans";
        Icon = Theme.LogoIcon(32);
        BackColor = Lt.Bg;
        StartPosition = FormStartPosition.Manual;
        // Kích thước tạm theo màn hình chứa con trỏ; kích thước chính xác đặt lại khi cửa sổ có handle (xem OnHandleCreated).
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = ClientFor(wa);
        MinimumSize = new Size(Px(580), Px(400));
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);

        _search.ApplyColors();
        _logo.Image = Theme.LogoBitmap(Px(32) * 2);
        _logo.SizeMode = PictureBoxSizeMode.Zoom;
        _logo.BackColor = Color.Transparent;
        _brand = new Lbl("SngTrans", 16, Lt.Text, true, _scale);
        _pageTitle = new Lbl(PageTitles[0], 22, Lt.Text, true, _scale);
        _nav.S = _scale;
        _nav.FooterTip = "Dừng mọi tiến trình của app, giống Kết thúc tác vụ ở Task Manager.\nPhím tắt dùng ở mọi nơi: Ctrl+Alt+Shift+Q";
        _nav.FooterClicked += () => ForceQuit?.Invoke();
        _done = new FlatBtn
        {
            Caption = "Xong", Bold = false, FontPx = 14, Radius = 6, Fore = Color.White, HoverFore = Color.White,
            Back = Lt.Accent, HoverBack = Lt.AccentHover, Cursor = Cursors.Hand, S = _scale,
        };
        _done.Click += (_, _) => Close();
        AcceptButton = null;

        BuildGeneral(welcome);
        BuildVocab();
        _pages.AddRange(new Control[] { _general, _blockedPage, _vocabPage });
        Controls.AddRange(new Control[] { _logo, _brand, _nav, _pageTitle, _general, _blockedPage, _vocabPage, _done });
        _blockedPage.Visible = _vocabPage.Visible = false;
        _nav.Selected += i =>
        {
            for (int k = 0; k < _pages.Count; k++) _pages[k].Visible = k == i;
            _pageTitle.Text = PageTitles[i];
            if (i == 2) RefreshVocab();
            RelayoutAll();
        };
        Resize += (_, _) => RelayoutAll();
        Activated += (_, _) => SyncVocabIfChanged();
        _watch.Tick += (_, _) => SyncVocabIfChanged();
        _watch.Start();
    }

    // ---------------------------------------------------------------- cấu trúc trang
    void Set(Action<SettingsData> mutate)
    {
        _s.Update(mutate);
        Changed?.Invoke();
    }

    Toggle NewToggle() => new()
    {
        S = _scale, Size = new Size(Px(40), Px(20)),
        OnBack = Lt.Accent, OffBack = Lt.Field, OffBorder = Theme.C("#8a8a8a"), OffKnob = Theme.C("#5c5c5c"), OnKnob = Color.White,
    };

    Toggle MakeToggle(string key, Func<SettingsData, bool> get, Action<SettingsData, bool> put)
    {
        var t = NewToggle();
        t.SetSilently(get(_s.Data));
        t.CheckedChanged += (_, _) => Set(d => put(d, t.Checked));
        _toggles[key] = t;
        return t;
    }

    void BuildGeneral(bool welcome)
    {
        if (welcome) _genItems.Add(new InfoBox());

        _lang = new LangRow();
        _lang.Native.Click += (_, _) => PickLang(true);
        _lang.Foreign.Click += (_, _) => PickLang(false);
        _lang.Swap.Click += (_, _) =>
        {
            var n = _s.Native; var f = _s.Foreign;
            Set(d => { d.Native = f; d.Foreign = n; });
            RefreshLangs();
        };
        _lang.Reset.Click += (_, _) =>
        {
            Set(d => { d.Native = "vi"; d.Foreign = "en"; });
            RefreshLangs();
        };
        _genItems.Add(_lang);

        _genItems.Add(new SettingRow(Theme.GCursor, "Tự dịch khi bôi đen", "Hiện thẻ ngay sau khi bôi đen",
            MakeToggle("auto", d => d.Auto, (d, v) => d.Auto = v)));
        _genItems.Add(new SettingRow(Theme.GSwap, "Tự đảo chiều", "Tiếng mẹ đẻ ⇄ ngoại ngữ",
            MakeToggle("smart", d => d.Smart, (d, v) => d.Smart = v)));
        _genItems.Add(new SettingRow(Theme.GEdit, "Chỉ khi con trỏ là chữ I", "Tránh dịch nhầm khi kéo thả file",
            MakeToggle("ibeam", d => d.IbeamOnly, (d, v) => d.IbeamOnly = v)));

        _hotkey = new HotkeyButton(_s.Hotkey) { S = _scale, Size = new Size(Px(170), Px(30)) };
        _hotkey.Picked += v => Set(d => d.Hotkey = v);
        _hotkey.CaptureChanged += v => CapturingHotkey?.Invoke(v);
        _genItems.Add(new SettingRow(Theme.GKeyboard, "Phím tắt dịch ngay", "Dịch phần đang bôi đen · Backspace: mặc định", _hotkey));

        _autoStart = NewToggle();
        _autoStart.SetSilently(Win32.IsAutostart());
        _autoStart.CheckedChanged += (_, _) => BootToggled(_autoStart.Checked);
        _genItems.Add(new SettingRow(Theme.GPlay, "Chạy cùng Windows", "Tự chạy ngầm khi bật máy", _autoStart));

        foreach (var c in _genItems) _general.Controls.Add(c);
    }

    void FillBlocked()
    {
        foreach (var r in _blockedRows) { _blockedPage.Controls.Remove(r); r.Dispose(); }
        _blockedRows.Clear();
        if (_blockedEmpty != null) { _blockedPage.Controls.Remove(_blockedEmpty); _blockedEmpty.Dispose(); _blockedEmpty = null; }
        var apps = _s.BlockedApps.ToList();
        if (apps.Count == 0)
        {
            _blockedEmpty = new Lbl("Chưa chặn app nào.\nTick “Không dịch app này” trên thẻ dịch để thêm.", 13, Lt.Muted, false, _scale)
            { TextAlign = ContentAlignment.MiddleCenter };
            _blockedPage.Controls.Add(_blockedEmpty);
        }
        foreach (var exe in apps)
        {
            var row = new BlockedRow(exe, Unblock);
            _blockedRows.Add(row);
            _blockedPage.Controls.Add(row);
        }
    }

    void Unblock(string exe)
    {
        Set(d => d.BlockedApps.RemoveAll(x => x == exe));
        FillBlocked();
        RelayoutAll();
    }

    void BuildVocab()
    {
        _search.Inner.TextChanged += (_, _) => RefreshVocab();
        _export = new FlatBtn { Glyph = "\uE896", IconPx = 13, Caption = "Xuất CSV", FontPx = 12, Radius = 6, Fore = Lt.Accent, HoverFore = Lt.AccentHover, Back = Color.Empty, HoverBack = Lt.FieldHover, Cursor = Cursors.Hand };
        _clear = new FlatBtn { Glyph = Theme.GDelete, IconPx = 13, Caption = "Xoá hết", FontPx = 12, Radius = 6, Fore = Lt.Danger, HoverFore = Lt.Danger, Back = Color.Empty, HoverBack = Lt.DangerBack, Cursor = Cursors.Hand };
        _export.Click += (_, _) => ExportCsv();
        _clear.Click += (_, _) => ClearAll();
        _vocabPage.Controls.AddRange(new Control[] { _search, _count, _vocabList, _export, _clear });
    }

    // ---------------------------------------------------------------- hành động
    void PickLang(bool native)
    {
        _picker ??= new LangPicker { Light = true };
        _picker.Chosen -= OnLangChosen;
        _picker.Chosen += OnLangChosen;
        _pickNative = native;
        var pill = native ? _lang.Native : _lang.Foreign;
        var entries = Langs.All.Select(l => (l.Code, l.Name));
        var cur = native ? _s.Native : _s.Foreign;
        _picker.OpenAt(entries, cur, pill.PointToScreen(new Point(0, pill.Height + Px(4))), _scale, Math.Max(pill.Width, Px(320)));
    }

    void OnLangChosen(string code)
    {
        if (_pickNative) Set(d => d.Native = code); else Set(d => d.Foreign = code);
        RefreshLangs();
    }

    void RefreshLangs()
    {
        _lang.Native.SetLang(_s.Native, Langs.Name(_s.Native), _s.Native);
        _lang.Foreign.SetLang(_s.Foreign, Langs.Name(_s.Foreign), _s.Foreign);
        bool custom = _s.Native != "vi" || _s.Foreign != "en";
        if (_lang.ShowReset != custom)
        {
            _lang.ShowReset = custom;
            if (IsHandleCreated) RelayoutAll();
        }
    }

    void BootToggled(bool on)
    {
        if (!Win32.SetAutostart(on))
            MessageBox.Show(this, "Không đổi được cài đặt chạy cùng Windows.", "SngTrans", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _autoStart.SetSilently(Win32.IsAutostart());          // lấy lại trạng thái thật từ Windows
        Changed?.Invoke();
    }

    void ExportCsv()
    {
        using var dlg = new SaveFileDialog { Title = "Xuất ghi chú", FileName = "ghi_chu.csv", Filter = "CSV (*.csv)|*.csv" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try { _s.ExportCsv(dlg.FileName); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "SngTrans", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    void ClearAll()
    {
        if (MessageBox.Show(this, "Xoá toàn bộ ghi chú đã lưu?", "Xoá hết", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _s.ClearVocab();
            RefreshVocab();
        }
    }

    /// <summary>Gọi lại khi cài đặt bị đổi từ bên ngoài (ví dụ menu khay).</summary>
    public void SyncFromSettings()
    {
        _toggles["auto"].SetSilently(_s.Auto);
        _toggles["smart"].SetSilently(_s.Smart);
        _toggles["ibeam"].SetSilently(_s.IbeamOnly);
        _autoStart.SetSilently(Win32.IsAutostart());
        RefreshLangs();
    }

    // ---------------------------------------------------------------- ghi chú
    void RefreshVocab()
    {
        var items = _s.Vocab();
        _vocabSig = (items.Count, items.Count > 0 ? items[0].Ts : 0L);
        var q = _search.Text.Trim().ToLowerInvariant();
        var shown = items.Where(v => q.Length == 0 || v.Text.ToLowerInvariant().Contains(q) || v.Translation.ToLowerInvariant().Contains(q)).ToList();
        _count.Text = q.Length > 0 ? $"{shown.Count}/{items.Count} ghi chú" : $"{items.Count} ghi chú";

        _vocabList.SuspendLayout();
        foreach (var r in _vocabRows) { _vocabList.Controls.Remove(r); r.Dispose(); }
        _vocabRows.Clear();
        if (_vocabEmpty != null) { _vocabList.Controls.Remove(_vocabEmpty); _vocabEmpty.Dispose(); _vocabEmpty = null; }
        if (shown.Count == 0)
        {
            _vocabEmpty = new Lbl(q.Length > 0 ? "Không tìm thấy ghi chú nào." : "Chưa có ghi chú nào.\nBấm “Ghi nhớ” trên thẻ dịch để lưu.", 13, Lt.Muted, false, _scale)
            { TextAlign = ContentAlignment.MiddleCenter };
            _vocabList.Controls.Add(_vocabEmpty);
        }
        foreach (var v in shown.Take(150))
        {
            var row = new VocabRow(v);
            var item = v;
            row.Speak.Click += (_, _) => _speak(item.Text, string.IsNullOrEmpty(item.Source) ? "en" : item.Source);
            row.Copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(item.Text + "\r\n" + item.Translation); row.FlashCopied(); }
                catch { /* clipboard đang bị app khác giữ */ }
            };
            row.Delete.Click += (_, _) => { _s.RemoveVocab(item.Ts); RefreshVocab(); };
            _vocabRows.Add(row);
            _vocabList.Controls.Add(row);
        }
        _vocabList.ResumeLayout(false);
        LayoutVocabList();
    }

    /// <summary>Ghi chú vừa được thêm từ thẻ dịch lúc hộp này đang mở: nạp lại danh sách nếu file đã đổi.</summary>
    void SyncVocabIfChanged()
    {
        if (!IsHandleCreated || !_vocabPage.Visible) return;
        var items = _s.Vocab();
        var sig = (items.Count, items.Count > 0 ? items[0].Ts : 0L);
        if (sig != _vocabSig) RefreshVocab();
    }

    void LayoutVocabList()
    {
        LayoutScroll(_vocabList, w =>
        {
            int y = 0;
            if (_vocabEmpty != null)
            {
                _vocabEmpty.SetBounds(0, Px(30), w, Px(80));
                y = Px(120);
            }
            foreach (var r in _vocabRows)
            {
                int h = r.DoLayout(w, _scale);
                r.SetBounds(0, y, w, h);
                y += h + Px(8);
            }
            return y;
        });
    }

    /// <summary>
    /// Bố trí một trang cuộn. Bề ngang lấy từ vùng hiển thị THẬT của trang (đã trừ thanh cuộn nếu có),
    /// bố trí hai lượt vì thanh cuộn xuất hiện/biến mất làm vùng này đổi.
    /// </summary>
    void LayoutScroll(ScrollPage page, Func<int, int> layout)
    {
        int Avail() => Math.Max(100, page.ClientSize.Width - Px(8));
        int w = Avail();
        page.Relayout(() => page.SetContentHeight(layout(w)));
        int w2 = Avail();
        if (w2 != w) page.Relayout(() => page.SetContentHeight(layout(w2)));
    }

    // ---------------------------------------------------------------- bố trí
    void RelayoutAll()
    {
        if (!IsHandleCreated && ClientSize.Width <= 0) return;
        int W = ClientSize.Width, H = ClientSize.Height;
        int sideW = Px(160), logo = Px(32);

        // thanh bên: logo + tên, menu, "Tắt app" ở đáy
        _logo.SetBounds(Px(20), Px(20), logo, logo);
        _brand.Restyle(16, true, _scale);
        int bh = _brand.Font.Height + 2;
        _brand.SetBounds(Px(20) + logo + Px(10), Px(20) + (logo - bh) / 2, Math.Max(10, sideW - Px(20) - logo - Px(14)), bh);
        int navTop = Px(20) + logo + Px(22);
        _nav.S = _scale;
        _nav.SetBounds(0, navTop, sideW, Math.Max(100, H - navTop - Px(16)));

        // bên phải: tiêu đề trang, vùng nội dung, nút Xong
        int cx = sideW + Px(8), right = Px(20);
        _pageTitle.Restyle(22, true, _scale);
        int th = _pageTitle.Font.Height + 2;
        _pageTitle.SetBounds(cx, Px(20), Math.Max(10, W - cx - right), th);

        int btnH = Px(32), btnW = Px(96);
        int by = H - Px(16) - btnH;
        _done.SetBounds(W - right - btnW, by, btnW, btnH);

        int py = Px(20) + th + Px(16);
        var area = new Rectangle(cx, py, Math.Max(100, W - cx - right), Math.Max(100, by - Px(14) - py));
        foreach (var p in _pages) p.Bounds = area;

        if (_general.Visible)
            LayoutScroll(_general, cw =>
            {
                int y = 0;
                foreach (var c in _genItems)
                {
                    int h = ((ILayoutRow)c).DoLayout(cw, _scale);
                    c.SetBounds(0, y, cw, h);
                    y += h + Px(6);
                }
                return y;
            });
        if (_blockedPage.Visible)
            LayoutScroll(_blockedPage, cw =>
            {
                int y = 0;
                if (_blockedEmpty != null)
                {
                    _blockedEmpty.Restyle(13, false, _scale);
                    _blockedEmpty.SetBounds(0, Px(30), cw, Px(80));
                    y = Px(120);
                }
                foreach (var r in _blockedRows)
                {
                    int h = r.DoLayout(cw, _scale);
                    r.SetBounds(0, y, cw, h);
                    y += h + Px(8);
                }
                return y;
            });
        if (_vocabPage.Visible)
        {
            int pw = area.Width, ph = area.Height, fh = Px(40);
            _search.S = _scale;
            _search.SetBounds(0, 0, pw, fh);
            // hàng công cụ: số ghi chú bên trái, Xuất CSV / Xoá hết dạng chữ nhỏ bên phải
            int rh = Px(30), ry = fh + Px(8);
            _count.Restyle(12, false, _scale);
            _count.TextAlign = ContentAlignment.MiddleLeft;
            _count.SetBounds(Px(4), ry, Math.Max(10, pw / 3), rh);
            _export.S = _clear.S = _scale;
            int cw = Gfx.TextWidth(_clear.Caption, Theme.Ui(12, false, _scale)) + Px(13 + 6 + 20);
            int ew = Gfx.TextWidth(_export.Caption, Theme.Ui(12, false, _scale)) + Px(13 + 6 + 20);
            _clear.SetBounds(pw - cw, ry, cw, rh);
            _export.SetBounds(pw - cw - Px(4) - ew, ry, ew, rh);
            int ly = ry + rh + Px(6);
            _vocabList.SetBounds(0, ly, pw, Math.Max(50, ph - ly));
            LayoutVocabList();
        }
    }

    Size ClientFor(Rectangle workArea) =>
        new(Math.Min(Px(700), Math.Max(Px(580), workArea.Width - Px(60))),
            Math.Max(Px(400), Math.Min(Px(440), workArea.Height - Px(100))));

    /// <summary>Áp lại mọi kích thước cố định (công tắc, ô phím tắt, logo) theo tỉ lệ hiện tại.</summary>
    void ApplyScale()
    {
        _nav.S = _done.S = _scale;
        var old = _logo.Image;
        _logo.Image = Theme.LogoBitmap(Px(32) * 2);
        old?.Dispose();
        var tsz = new Size(Px(40), Px(20));
        foreach (var t in _toggles.Values) { t.S = _scale; t.Size = tsz; }
        _autoStart.S = _scale; _autoStart.Size = tsz;
        _hotkey.S = _scale; _hotkey.Size = new Size(Px(170), Px(30));
        MinimumSize = new Size(Px(580), Px(400));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win32.SetLightTitlebar(Handle);
        // Lấy tỉ lệ từ DPI THẬT của cửa sổ (theo màn hình nó nằm), không đoán theo vị trí con trỏ,
        // rồi đặt lại kích thước và canh giữa. Nhờ vậy bố cục luôn khớp với khung hiển thị.
        _scale = Win32.DpiOfWindow(Handle) / 96f;
        ApplyScale();
        var wa = Screen.FromHandle(Handle).WorkingArea;
        ClientSize = ClientFor(wa);
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshLangs();
        _hotkey.SetValue(_s.Hotkey);
        FillBlocked();
        RefreshVocab();
        RelayoutAll();
        Win32.ForceForeground(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x2E0 && IsHandleCreated)       // WM_DPICHANGED: kéo sang màn hình khác tỉ lệ
        {
            int dpi = (int)(m.WParam.ToInt64() & 0xFFFF);
            if (dpi > 0)
            {
                _scale = dpi / 96f;
                var r = Marshal.PtrToStructure<Win32.RECT>(m.LParam);
                SetBounds(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                ApplyScale();
                RefreshVocab();
                RelayoutAll();
            }
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _watch.Dispose(); _picker?.Dispose(); }
        base.Dispose(disposing);
    }
}
