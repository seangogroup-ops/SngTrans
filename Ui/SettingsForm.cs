using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

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

/// <summary>Một hàng cài đặt: tiêu đề + mô tả bên trái, điều khiển bên phải (hoặc bên dưới nếu Stacked).</summary>
sealed class RowCtl : Ctl, ILayoutRow
{
    readonly Lbl _title, _sub;
    readonly Control _right;
    public bool Stacked;

    public RowCtl(string title, string sub, Control right)
    {
        _title = new Lbl(title, 14, Theme.Label);
        _sub = new Lbl(sub, 12, Theme.SubLabel);
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
        int vpad = Px(12);
        int rightW = Stacked ? 0 : _right.Width + Px(12);
        int textW = width - rightW;
        int th = _title.HeightFor(textW);
        int sh = _sub.Text.Length > 0 ? _sub.HeightFor(textW) : 0;
        int textH = th + (sh > 0 ? sh + Px(1) : 0);
        int h;
        if (Stacked)
        {
            _title.SetBounds(0, vpad, textW, th);
            _sub.SetBounds(0, vpad + th + Px(1), textW, sh);
            _right.Location = new Point(0, vpad + textH + Px(8));
            h = vpad + textH + Px(8) + _right.Height + vpad;
        }
        else
        {
            h = Math.Max(textH, _right.Height) + 2 * vpad;
            _title.SetBounds(0, (h - textH) / 2, textW, th);
            _sub.SetBounds(0, (h - textH) / 2 + th + Px(1), textW, sh);
            _right.Location = new Point(width - _right.Width, (h - _right.Height) / 2);
        }
        Size = new Size(width, h);
        return h;
    }
}

/// <summary>Khối có tiêu đề nhỏ viết hoa và thẻ bo góc chứa các hàng.</summary>
sealed class Section : Ctl
{
    readonly Lbl _head;
    readonly List<Control> _rows;
    readonly List<int> _lines = new();
    int _cardTop, _cardH;

    public Section(string title, params Control[] rows)
    {
        _head = new Lbl(title.ToUpperInvariant(), 11, Theme.Section, true);
        _rows = rows.ToList();
        Controls.Add(_head);
        foreach (var r in rows) Controls.Add(r);
    }

    public int DoLayout(int width, float s)
    {
        S = s;
        _head.Restyle(11, true, s);
        int hh = _head.Font.Height + 2;
        _head.SetBounds(0, 0, width, hh);
        _cardTop = hh + Px(7);
        int padX = Px(16);
        int y = _cardTop + Px(2);
        _lines.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            if (i > 0) { _lines.Add(y); y += 1; }
            var r = _rows[i];
            int rh = r is ILayoutRow lr ? lr.DoLayout(width - 2 * padX, s) : r.Height;
            r.SetBounds(padX, y, width - 2 * padX, rh);
            y += rh;
        }
        y += Px(2);
        _cardH = y - _cardTop;
        Size = new Size(width, y);
        Invalidate();
        return y;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, _cardTop + 0.5f, Width - 1, _cardH - 1);
        Gfx.FillRound(g, Theme.Card, r, Px(14));
        Gfx.DrawRound(g, Theme.Line, r, Px(14));
        using var b = new SolidBrush(Theme.Line);
        foreach (var y in _lines) g.FillRectangle(b, Px(16), y, Width - Px(32), 1);
    }
}

/// <summary>Khung thông báo xanh (lời chào lần đầu).</summary>
sealed class InfoBox : Ctl, ILayoutRow
{
    readonly Lbl _a = new("Chào mừng bạn!", 14, Color.White, true);
    readonly Lbl _b = new("Chọn ngôn ngữ bên dưới (mặc định Việt ↔ Anh), rồi bôi đen chữ ở bất kỳ đâu để dịch.", 13, Theme.C("#cfe0ff"));

    public InfoBox() { Controls.Add(_a); Controls.Add(_b); }

    public int DoLayout(int width, float s)
    {
        S = s;
        _a.Restyle(14, true, s);
        _b.Restyle(13, false, s);
        int pad = Px(14), v = Px(10);
        int ah = _a.HeightFor(width - 2 * pad), bh = _b.HeightFor(width - 2 * pad);
        _a.SetBounds(pad, v, width - 2 * pad, ah);
        _b.SetBounds(pad, v + ah + Px(2), width - 2 * pad, bh);
        Size = new Size(width, v * 2 + ah + bh + Px(2));
        return Height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(e.Graphics, Theme.C("#10244a"), r, Px(12));
        Gfx.DrawRound(e.Graphics, Theme.C("#1e3a6e"), r, Px(12));
    }
}

/// <summary>Hàng chọn hai ngôn ngữ (mẹ đẻ ⇄ ngoại ngữ) và nút khôi phục mặc định.</summary>
sealed class LangRow : Ctl, ILayoutRow
{
    public readonly Pill Native = new() { AutoWidth = false, FontPx = 14, Bg = Theme.Field, BgHover = Theme.FieldHover };
    public readonly Pill Foreign = new() { AutoWidth = false, FontPx = 14, Bg = Theme.Field, BgHover = Theme.FieldHover };
    public readonly FlatBtn Swap = new() { Glyph = "⇄", Back = Theme.Field, HoverBack = Theme.FieldHover, Fore = Theme.C("#9fb0d9"), HoverFore = Color.White, Cursor = Cursors.Hand };
    public readonly FlatBtn Reset = new() { Caption = "Khôi phục mặc định: Tiếng Việt ↔ English", LeftAlign = true, Fore = Theme.C("#60a5fa"), HoverFore = Theme.C("#93c5fd"), HoverBack = Color.Empty, Cursor = Cursors.Hand, FontPx = 13 };
    readonly Lbl _l1 = new("Tiếng mẹ đẻ", 12, Theme.SubLabel), _l2 = new("Ngoại ngữ", 12, Theme.SubLabel);

    public LangRow()
    {
        Native.Cursor = Foreign.Cursor = Cursors.Hand;
        Controls.AddRange(new Control[] { _l1, _l2, Native, Foreign, Swap, Reset });
    }

    public int DoLayout(int width, float s)
    {
        S = s;
        foreach (var c in new Ctl[] { Native, Foreign, Swap, Reset }) c.S = s;
        _l1.Restyle(12, false, s); _l2.Restyle(12, false, s);
        Swap.IconPx = 16;
        int top = Px(14), lh = _l1.Font.Height + 2, gap = Px(10), sw = Px(36), ph = Px(40);
        int pw = (width - sw - 2 * gap) / 2;
        _l1.SetBounds(0, top, pw, lh);
        _l2.SetBounds(pw + gap + sw + gap, top, pw, lh);
        int py = top + lh + Px(6);
        Native.SetBounds(0, py, pw, ph);
        Swap.SetBounds(pw + gap, py + (ph - sw) / 2, sw, sw);
        Swap.Radius = sw / 2f;
        Foreign.SetBounds(pw + gap + sw + gap, py, pw, ph);
        Reset.SetBounds(0, py + ph + Px(6), width, Px(28));
        int h = py + ph + Px(6) + Px(28) + Px(6);
        Size = new Size(width, h);
        return h;
    }
}

/// <summary>Công tắc/nút chọn phím tắt: bấm rồi nhấn tổ hợp phím mới. Esc huỷ, Backspace/Delete về mặc định.</summary>
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
        Gfx.FillRound(g, Hover ? Theme.FieldHover : Theme.Field, r, Px(10));
        Gfx.DrawRound(g, _capturing ? Theme.Accent : Theme.FieldBorder, r, Px(10));
        string text = _capturing ? (_hint.Length > 0 ? _hint : "Nhấn phím mới… (Esc: huỷ)") : HotkeySpec.Pretty(Value);
        var font = _capturing ? Theme.Ui(13, false, S) : Theme.Mono(12, S);
        Gfx.Text(g, text, font, new Rectangle(Px(12), 0, Width - Px(24), Height),
            _capturing ? Theme.Link : Color.White, Gfx.Single | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>Thanh chuyển trang: ba tab trong thẻ bo góc.</summary>
sealed class TabBar : Ctl
{
    readonly string[] _names;
    int _sel, _hover = -1;
    public event Action<int>? Selected;

    public TabBar(params string[] names) { _names = names; Cursor = Cursors.Hand; }

    int TabW(int i) => Gfx.TextWidth(_names[i], Theme.Ui(13, true, S)) + Px(40);
    public int PreferredWidth => Enumerable.Range(0, _names.Length).Sum(TabW) + Px(4 * 2 + 4 * (_names.Length - 1));

    int At(int x)
    {
        int cx = Px(4);
        for (int i = 0; i < _names.Length; i++)
        {
            int w = TabW(i);
            if (x >= cx && x < cx + w) return i;
            cx += w + Px(4);
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e) { _hover = At(e.X); Invalidate(); base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        int i = At(e.X);
        if (i >= 0 && i != _sel) { _sel = i; Invalidate(); Selected?.Invoke(i); }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Gfx.FillRound(g, Theme.Card, new RectangleF(0, 0, Width, Height), Px(12));
        int cx = Px(4);
        for (int i = 0; i < _names.Length; i++)
        {
            int w = TabW(i);
            var r = new Rectangle(cx, Px(4), w, Height - Px(8));
            if (i == _sel) Gfx.FillRound(g, Theme.C("#2a3a6b"), r, Px(9));
            Gfx.Text(g, _names[i], Theme.Ui(13, i == _sel, S), r, i == _sel ? Color.White : Theme.C("#9fb0d9"),
                Gfx.Single | TextFormatFlags.HorizontalCenter);
            cx += w + Px(4);
        }
    }
}

/// <summary>Một ghi chú trong tab Ghi chú.</summary>
sealed class VocabRow : Ctl, ILayoutRow
{
    readonly Lbl _w, _t;
    public readonly FlatBtn Speak = new() { Glyph = Theme.GVolume, IconPx = 16, Cursor = Cursors.Hand };
    public readonly FlatBtn Delete = new() { Glyph = Theme.GDelete, IconPx = 16, Cursor = Cursors.Hand };

    public VocabRow(VocabItem v)
    {
        _w = new Lbl(v.Text, 15, Color.White, true);
        _t = new Lbl(v.Translation, 13, Theme.Link);
        Controls.AddRange(new Control[] { _w, _t, Speak, Delete });
    }

    public int DoLayout(int width, float s)
    {
        S = s;
        Speak.S = Delete.S = s;
        _w.Restyle(15, true, s); _t.Restyle(13, false, s);
        int btn = Px(30), left = Px(14), tw = width - left - 2 * btn - Px(10);
        int wh = _w.HeightFor(tw), th = _t.HeightFor(tw);
        int h = Math.Max(wh + th + Px(2), btn) + Px(20);
        _w.SetBounds(left, Px(10), tw, wh);
        _t.SetBounds(left, Px(10) + wh + Px(2), tw, th);
        Speak.SetBounds(width - 2 * btn - Px(8), Px(6), btn, btn);
        Delete.SetBounds(width - btn - Px(4), Px(6), btn, btn);
        Size = new Size(width, h);
        return h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(e.Graphics, Theme.Card, r, Px(12));
        Gfx.DrawRound(e.Graphics, Theme.Line, r, Px(12));
    }
}

/// <summary>Danh sách app bị chặn.</summary>
sealed class BlockedList : Ctl, ILayoutRow
{
    readonly List<Control> _kids = new();
    readonly IReadOnlyList<string> _apps;
    readonly Action<string> _unblock;

    public BlockedList(IReadOnlyList<string> apps, Action<string> unblock)
    {
        _apps = apps;
        _unblock = unblock;
        if (apps.Count == 0)
            Add(new Lbl("Chưa chặn app nào.\nTick “Không dịch app này” trên thẻ dịch để thêm.", 12, Theme.SubLabel));
        foreach (var exe in apps)
        {
            Add(new Lbl(exe, 14, Theme.Label));
            var x = new FlatBtn { Glyph = Theme.GCancel, IconPx = 14, Cursor = Cursors.Hand, Fore = Theme.C("#9fb0d9") };
            var name = exe;
            x.Click += (_, _) => _unblock(name);
            Add(x);
        }
    }

    void Add(Control c) { _kids.Add(c); Controls.Add(c); }

    public int DoLayout(int width, float s)
    {
        S = s;
        int y = Px(6);
        if (_apps.Count == 0)
        {
            var l = (Lbl)_kids[0];
            l.Restyle(12, false, s);
            int h = l.HeightFor(width) ;
            l.SetBounds(0, y + Px(8), width, h);
            y += Px(8) + h + Px(8);
        }
        else
            for (int i = 0; i < _apps.Count; i++)
            {
                var l = (Lbl)_kids[2 * i];
                var x = (FlatBtn)_kids[2 * i + 1];
                l.Restyle(14, false, s);
                x.S = s;
                int rh = Px(36);
                l.SetBounds(0, y, width - Px(36), rh);
                l.TextAlign = ContentAlignment.MiddleLeft;
                x.SetBounds(width - Px(32), y + Px(2), Px(32), Px(32));
                y += rh;
            }
        y += Px(6);
        Size = new Size(width, y);
        return y;
    }
}

/// <summary>Hộp cài đặt: ngôn ngữ, hành vi, phím tắt, app bị chặn và ghi chú đã lưu.</summary>
public sealed class SettingsForm : Form
{
    public event Action? Changed;
    public event Action? ForceQuit;
    public event Action<bool>? CapturingHotkey;

    readonly Settings _s;
    readonly Action<string, string> _speak;
    float _scale;
    readonly ToolTip _tip = new();
    int Px(float v) => (int)Math.Round(v * _scale);

    readonly PictureBox _logo = new();
    readonly Lbl _title, _subtitle;
    readonly TabBar _tabs = new("Cài đặt", "Chặn app", "Ghi chú");
    readonly FlatBtn _done, _kill;
    readonly ScrollPage _general = new(), _blockedPage = new(), _vocabList = new();
    readonly Panel _vocabPage = new() { BackColor = Theme.Bg };
    readonly List<Control> _pages = new();

    // trang cài đặt
    readonly List<Control> _genItems = new();
    LangRow _lang = null!;
    readonly Dictionary<string, Toggle> _toggles = new();
    Toggle _autoStart = null!;
    HotkeyButton _hotkey = null!;
    // trang chặn app
    Section? _blockedSection;
    // trang ghi chú
    readonly FieldBox _search = new() { FontPx = 14, Placeholder = "Tìm trong ghi chú…" };
    readonly Lbl _count = new("", 12, Theme.SubLabel);
    FlatBtn _export = null!, _clear = null!;
    readonly List<VocabRow> _vocabRows = new();
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
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.Manual;
        // Kích thước tạm theo màn hình chứa con trỏ; kích thước chính xác đặt lại khi cửa sổ có handle (xem OnHandleCreated).
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = ClientFor(wa);
        MinimumSize = new Size(Px(480), Px(560));
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);

        _logo.Image = Theme.LogoBitmap(Px(44) * 2);
        _logo.SizeMode = PictureBoxSizeMode.Zoom;
        _logo.BackColor = Color.Transparent;
        _title = new Lbl("SngTrans", 21, Color.White, true, _scale);
        _subtitle = new Lbl("Bôi đen chữ ở bất kỳ đâu để dịch", 12, Theme.SubLabel, false, _scale);
        _tabs.S = _scale;
        _done = new FlatBtn
        {
            Caption = "Xong", Bold = true, FontPx = 14, Radius = 10, Fore = Color.White, HoverFore = Color.White,
            Back = Theme.Accent, HoverBack = Theme.C("#2f6fe0"), Cursor = Cursors.Hand, S = _scale,
        };
        _done.Click += (_, _) => Close();
        _kill = new FlatBtn
        {
            Caption = "Kết thúc tác vụ", Bold = false, FontPx = 13, Radius = 10, Fore = Theme.Err, HoverFore = Theme.Err,
            Back = Theme.Field, HoverBack = Theme.FieldHover, Cursor = Cursors.Hand, S = _scale,
        };
        _kill.Click += (_, _) => ForceQuit?.Invoke();
        _tip.SetToolTip(_kill, "Dừng mọi tiến trình của app, giống Kết thúc tác vụ ở Task Manager.\nPhím tắt dùng ở mọi nơi: Ctrl+Alt+Shift+Q");
        AcceptButton = null;

        BuildGeneral(welcome);
        BuildBlocked();
        BuildVocab();
        _pages.AddRange(new Control[] { _general, _blockedPage, _vocabPage });
        Controls.AddRange(new Control[] { _logo, _title, _subtitle, _tabs, _general, _blockedPage, _vocabPage, _kill, _done });
        _blockedPage.Visible = _vocabPage.Visible = false;
        _tabs.Selected += i =>
        {
            for (int k = 0; k < _pages.Count; k++) _pages[k].Visible = k == i;
            RelayoutAll();
        };
        Resize += (_, _) => RelayoutAll();
    }

    // ---------------------------------------------------------------- cấu trúc trang
    void Set(Action<SettingsData> mutate)
    {
        _s.Update(mutate);
        Changed?.Invoke();
    }

    Toggle MakeToggle(string key, Func<SettingsData, bool> get, Action<SettingsData, bool> put)
    {
        var t = new Toggle { S = _scale, Size = new Size(Px(44), Px(26)) };
        t.SetSilently(get(_s.Data));
        t.CheckedChanged += (_, _) => Set(d => put(d, t.Checked));
        _toggles[key] = t;
        return t;
    }

    void BuildGeneral(bool welcome)
    {
        if (welcome) _genItems.Add(new InfoBox());

        _lang = new LangRow();
        _lang.Native.Cursor = _lang.Foreign.Cursor = Cursors.Hand;
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
        _genItems.Add(new Section("Ngôn ngữ", _lang));

        _genItems.Add(new Section("Cách dịch",
            new RowCtl("Tự dịch khi bôi đen", "Hiện thẻ ngay sau khi bôi đen", MakeToggle("auto", d => d.Auto, (d, v) => d.Auto = v)),
            new RowCtl("Tự đảo chiều", "Tiếng mẹ đẻ ⇄ ngoại ngữ", MakeToggle("smart", d => d.Smart, (d, v) => d.Smart = v)),
            new RowCtl("Chỉ khi con trỏ là chữ I", "Tránh dịch nhầm khi kéo thả file", MakeToggle("ibeam", d => d.IbeamOnly, (d, v) => d.IbeamOnly = v))));

        _autoStart = new Toggle { S = _scale, Size = new Size(Px(44), Px(26)) };
        _autoStart.SetSilently(Win32.IsAutostart());
        _autoStart.CheckedChanged += (_, _) => BootToggled(_autoStart.Checked);
        _hotkey = new HotkeyButton(_s.Hotkey) { S = _scale, Size = new Size(Px(190), Px(36)) };
        _hotkey.Picked += v => Set(d => d.Hotkey = v);
        _hotkey.CaptureChanged += v => CapturingHotkey?.Invoke(v);
        _genItems.Add(new Section("Hệ thống",
            new RowCtl("Chạy cùng Windows", "Tự chạy ngầm khi bật máy", _autoStart),
            new RowCtl("Phím tắt dịch ngay", "Dịch phần đang bôi đen · Backspace = mặc định", _hotkey)));

        foreach (var c in _genItems) _general.Controls.Add(c);
    }

    void BuildBlocked()
    {
        FillBlocked();
    }

    void FillBlocked()
    {
        if (_blockedSection != null)
        {
            _blockedPage.Controls.Remove(_blockedSection);
            _blockedSection.Dispose();
        }
        _blockedSection = new Section("Danh sách chặn", new BlockedList(_s.BlockedApps.ToList(), Unblock));
        _blockedPage.Controls.Add(_blockedSection);
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
        _export = new FlatBtn { Caption = "Xuất CSV", FontPx = 13, Radius = 10, Fore = Color.White, HoverFore = Color.White, Back = Theme.Field, HoverBack = Theme.FieldHover, Cursor = Cursors.Hand };
        _clear = new FlatBtn { Caption = "Xoá hết", FontPx = 13, Radius = 10, Fore = Theme.Err, HoverFore = Theme.Err, Back = Theme.Field, HoverBack = Theme.FieldHover, Cursor = Cursors.Hand };
        _export.Click += (_, _) => ExportCsv();
        _clear.Click += (_, _) => ClearAll();
        _vocabPage.Controls.AddRange(new Control[] { _search, _count, _vocabList, _export, _clear });
    }

    // ---------------------------------------------------------------- hành động
    void PickLang(bool native)
    {
        _picker ??= new LangPicker();
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
        var q = _search.Text.Trim().ToLowerInvariant();
        var shown = items.Where(v => q.Length == 0 || v.Text.ToLowerInvariant().Contains(q) || v.Translation.ToLowerInvariant().Contains(q)).ToList();
        _count.Text = q.Length > 0 ? $"{shown.Count}/{items.Count} ghi chú" : $"{items.Count} ghi chú";

        _vocabList.SuspendLayout();
        foreach (var r in _vocabRows) { _vocabList.Controls.Remove(r); r.Dispose(); }
        _vocabRows.Clear();
        if (_vocabEmpty != null) { _vocabList.Controls.Remove(_vocabEmpty); _vocabEmpty.Dispose(); _vocabEmpty = null; }
        if (shown.Count == 0)
        {
            _vocabEmpty = new Lbl(q.Length > 0 ? "Không tìm thấy ghi chú nào." : "Chưa có ghi chú nào.\nBấm “Ghi chú” trên thẻ dịch để lưu.", 13, Theme.Section, false, _scale)
            { TextAlign = ContentAlignment.MiddleCenter };
            _vocabList.Controls.Add(_vocabEmpty);
        }
        foreach (var v in shown.Take(150))
        {
            var row = new VocabRow(v);
            var item = v;
            row.Speak.Click += (_, _) => _speak(item.Text, string.IsNullOrEmpty(item.Source) ? "en" : item.Source);
            row.Delete.Click += (_, _) => { _s.RemoveVocab(item.Ts); RefreshVocab(); };
            _vocabRows.Add(row);
            _vocabList.Controls.Add(row);
        }
        _vocabList.ResumeLayout(false);
        LayoutVocabList();
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
        int W = ClientSize.Width, H = ClientSize.Height, m = Px(22);
        int top = Px(20), logo = Px(44);
        _logo.SetBounds(m, top, logo, logo);
        int tx = m + logo + Px(12);
        _title.Restyle(21, true, _scale); _subtitle.Restyle(12, false, _scale);
        _title.SetBounds(tx, top, Math.Max(10, W - tx - m), _title.Font.Height + 2);
        _subtitle.SetBounds(tx, top + _title.Height, Math.Max(10, W - tx - m), _subtitle.Font.Height + 2);

        int ty = top + logo + Px(14);
        _tabs.SetBounds(m, ty, _tabs.PreferredWidth, Px(40));

        // chân hộp: "Tắt hẳn" bên trái, "Xong" bên phải
        int bh = Px(42), bw = Px(110);
        int by = H - Px(18) - bh;
        _done.SetBounds(W - m - bw, by, bw, bh);
        int kw = Gfx.TextWidth(_kill.Caption, Theme.Ui(13, false, _scale)) + Px(40);
        _kill.SetBounds(m, by, kw, bh);

        int py = ty + Px(40) + Px(14);
        var area = new Rectangle(m, py, Math.Max(100, W - 2 * m), Math.Max(100, by - Px(14) - py));
        foreach (var p in _pages) p.Bounds = area;

        // trang cài đặt
        if (_general.Visible)
            LayoutScroll(_general, cw =>
            {
                int y = 0;
                foreach (var c in _genItems)
                {
                    int h = c is Section s ? s.DoLayout(cw, _scale) : ((ILayoutRow)c).DoLayout(cw, _scale);
                    c.SetBounds(0, y, cw, h);
                    y += h + Px(16);
                }
                return y;
            });
        if (_blockedPage.Visible && _blockedSection != null)
            LayoutScroll(_blockedPage, cw =>
            {
                int h = _blockedSection.DoLayout(cw, _scale);
                _blockedSection.SetBounds(0, 0, cw, h);
                return h;
            });
        if (_vocabPage.Visible)
        {
            int pw = area.Width, ph = area.Height, fh = Px(40);
            _search.S = _scale;
            _search.SetBounds(0, 0, pw - Px(110), fh);
            _count.Restyle(12, false, _scale);
            _count.TextAlign = ContentAlignment.MiddleRight;
            _count.SetBounds(pw - Px(100), 0, Px(100), fh);
            int btnH = Px(38);
            _export.S = _clear.S = _scale;
            _export.SetBounds(0, ph - btnH, Px(110), btnH);
            _clear.SetBounds(pw - Px(110), ph - btnH, Px(110), btnH);
            _vocabList.SetBounds(0, fh + Px(10), pw, ph - btnH - fh - Px(20));
            LayoutVocabList();
        }
    }

    Size ClientFor(Rectangle workArea) =>
        new(Px(500), Math.Max(Px(560), Math.Min(Px(720), workArea.Height - Px(120))));

    /// <summary>Áp lại mọi kích thước cố định (công tắc, ô phím tắt, logo) theo tỉ lệ hiện tại.</summary>
    void ApplyScale()
    {
        _tabs.S = _done.S = _kill.S = _scale;
        var old = _logo.Image;
        _logo.Image = Theme.LogoBitmap(Px(44) * 2);
        old?.Dispose();
        var tsz = new Size(Px(44), Px(26));
        foreach (var t in _toggles.Values) { t.S = _scale; t.Size = tsz; }
        _autoStart.S = _scale; _autoStart.Size = tsz;
        _hotkey.S = _scale; _hotkey.Size = new Size(Px(190), Px(36));
        MinimumSize = new Size(Px(480), Px(560));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win32.SetDarkTitlebar(Handle);
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
        if (disposing) { _picker?.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }
}
