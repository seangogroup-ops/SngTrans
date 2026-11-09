using System.Drawing.Drawing2D;

namespace SngTrans.Ui;

/// <summary>Danh sách ngôn ngữ cuộn được: cờ, tên, mã ở bên phải.</summary>
public sealed class LangList : Ctl
{
    public sealed class Row
    {
        public string Code = "", Name = "";
        public bool Current, Separator, Info;
    }

    List<Row> _rows = new();
    int _scroll, _sel = -1, _hover = -1;
    Color _bg = Theme.PickerBg, _selBg = Theme.C("#232f5c"), _hoverBg = Theme.C("#1b2548"), _fore = Theme.C("#f1f4ff"),
          _muted = Theme.C("#7c88aa"), _sep = Color.FromArgb(22, 255, 255, 255), _thumb = Theme.C("#34415f");

    /// <summary>Đổi bảng màu: sáng (hộp Cài đặt) hoặc tối (thẻ dịch).</summary>
    public void SetLight(bool light)
    {
        if (light)
        {
            _bg = Theme.Light.Card; _selBg = Theme.C("#e1ecf7"); _hoverBg = Theme.Light.FieldHover; _fore = Theme.Light.Text;
            _muted = Theme.Light.Muted; _sep = Color.FromArgb(30, 0, 0, 0); _thumb = Theme.C("#c1c1c1");
        }
        else
        {
            _bg = Theme.PickerBg; _selBg = Theme.C("#232f5c"); _hoverBg = Theme.C("#1b2548"); _fore = Theme.C("#f1f4ff");
            _muted = Theme.C("#7c88aa"); _sep = Color.FromArgb(22, 255, 255, 255); _thumb = Theme.C("#34415f");
        }
        BackColor = _bg;
        Invalidate();
    }
    public event Action<string>? Picked;

    int RowH(Row r) => r.Separator ? Px(10) : Px(40);
    int ContentH => _rows.Sum(RowH);
    int MaxScroll => Math.Max(0, ContentH - Height);

    public LangList() { BackColor = Theme.PickerBg; }

    public void SetRows(List<Row> rows)
    {
        _rows = rows;
        _scroll = 0;
        _hover = -1;
        _sel = _rows.FindIndex(r => !r.Separator && !r.Info);
        Invalidate();
    }

    public void MoveSel(int step)
    {
        int i = _sel + step;
        while (i >= 0 && i < _rows.Count && (_rows[i].Separator || _rows[i].Info)) i += step;
        if (i < 0 || i >= _rows.Count) return;
        _sel = i;
        int top = 0;
        for (int k = 0; k < i; k++) top += RowH(_rows[k]);
        int bottom = top + RowH(_rows[i]);
        if (top < _scroll) _scroll = top;
        else if (bottom > _scroll + Height) _scroll = bottom - Height;
        Invalidate();
    }

    public void ActivateSelected()
    {
        if (_sel >= 0 && _sel < _rows.Count && !_rows[_sel].Separator && !_rows[_sel].Info) Picked?.Invoke(_rows[_sel].Code);
    }

    int IndexAt(int y)
    {
        int acc = -_scroll;
        for (int i = 0; i < _rows.Count; i++)
        {
            int h = RowH(_rows[i]);
            if (y >= acc && y < acc + h) return i;
            acc += h;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexAt(e.Y);
        if (i >= 0 && (_rows[i].Separator || _rows[i].Info)) i = -1;
        if (i != _hover) { _hover = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int i = IndexAt(e.Y);
            if (i >= 0 && !_rows[i].Separator && !_rows[i].Info) Picked?.Invoke(_rows[i].Code);
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - e.Delta / 120 * Px(60)));
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int y = -_scroll;
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            int h = RowH(r);
            if (y + h >= 0 && y <= Height)
            {
                var rect = new Rectangle(0, y, Width, h);
                if (r.Info)
                    Gfx.Text(g, r.Name, Theme.Ui(14, false, S), new Rectangle(Px(14), y, Width - Px(28), h), _muted, Gfx.Single);
                else if (r.Separator)
                    using (var b = new SolidBrush(_sep))
                        g.FillRectangle(b, Px(6), y + h / 2, Width - Px(12), 1);
                else
                {
                    if (i == _sel || i == _hover)
                        Gfx.FillRound(g, i == _sel ? _selBg : _hoverBg,
                            new RectangleF(Px(2), y + 1, Width - Px(4), h - 2), Px(10));
                    int fw = Px(22), fh = Px(15);
                    g.DrawImageUnscaled(Flags.Get(r.Code == "auto" ? null : r.Code, fw, fh), Px(12), y + (h - fh) / 2);
                    Gfx.Text(g, r.Name, Theme.Ui(15, r.Current, S), new Rectangle(Px(46), y, Width - Px(46 + 70), h),
                        _fore, Gfx.Single | TextFormatFlags.EndEllipsis);
                    Gfx.Text(g, r.Code == "auto" ? "AUTO" : r.Code.ToUpperInvariant(), Theme.Ui(12, false, S),
                        new Rectangle(Width - Px(72), y, Px(60), h), _muted, Gfx.Single | TextFormatFlags.Right);
                }
            }
            y += h;
        }
        if (ContentH > Height)
        {
            float th = Math.Max(Px(28), Height * (float)Height / ContentH);
            float ty = MaxScroll == 0 ? 0 : (Height - th) * _scroll / MaxScroll;
            Gfx.FillRound(g, _thumb, new RectangleF(Width - Px(6), ty, Px(4), th), Px(2));
        }
    }
}

/// <summary>Hộp chọn ngôn ngữ nổi: ô tìm kiếm + danh sách (ngôn ngữ đang chọn ở đầu, mã bên phải).</summary>
public sealed class LangPicker : Form
{
    public event Action<string>? Chosen;
    public event Action? Dismissed;

    readonly FieldBox _search = new() { FontPx = 15, Placeholder = "Tìm ngôn ngữ…" };
    readonly LangList _list = new();
    List<(string Code, string Name)> _items = new();
    string _current = "";
    float _s = 1f;
    int _shownAt;
    bool _light;

    /// <summary>Nền sáng cho hộp Cài đặt; mặc định tối cho thẻ dịch.</summary>
    public bool Light
    {
        get => _light;
        set
        {
            _light = value;
            BackColor = value ? Theme.Light.Card : Theme.PickerBg;
            _list.SetLight(value);
            _search.Fill = value ? Theme.Light.Field : Theme.Field;
            _search.Border = value ? Theme.Light.FieldBorder : Theme.FieldBorder;
            _search.FocusBorder = value ? Theme.Light.Accent : Theme.Accent;
            _search.Fore = value ? Theme.Light.Text : Color.White;
            _search.ApplyColors();
            if (IsHandleCreated) Win32.SetRoundedBorder(Handle, value ? 0xD1D1D1 : 0x4A3A22);
        }
    }

    public LangPicker()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.PickerBg;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = false;
        Controls.Add(_search);
        Controls.Add(_list);
        _search.Inner.TextChanged += (_, _) => Fill(_search.Text);
        _search.Inner.KeyDown += OnSearchKey;
        _list.Picked += code => { Chosen?.Invoke(code); Hide(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;       // CS_DROPSHADOW
            cp.ExStyle |= 0x80;             // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win32.SetRoundedBorder(Handle, _light ? 0xD1D1D1 : 0x4A3A22);
    }

    /// <summary>entries: (mã, tên); mã "auto" hiện cờ địa cầu.</summary>
    public void OpenAt(IEnumerable<(string Code, string Name)> entries, string current, Point screenPos, float scale, int width)
    {
        _items = entries.ToList();
        _current = current;
        _s = scale;
        int P(float v) => (int)Math.Round(v * scale);
        _search.S = scale; _list.S = scale;
        ClientSize = new Size(width, P(10 + 40 + 8 + 250 + 10));
        _search.SetBounds(P(10), P(10), width - P(20), P(40));
        _list.SetBounds(P(10), P(58), width - P(20), P(250));
        _search.Inner.Text = "";
        Fill("");
        if (!Win32.IsWin11)
        {
            using var path = Gfx.Round(new RectangleF(0, 0, Width, Height), P(14));
            Region = new Region(path);
        }
        var wa = Screen.FromPoint(screenPos).WorkingArea;
        Location = new Point(Math.Max(wa.Left, Math.Min(screenPos.X, wa.Right - Width)),
                             Math.Max(wa.Top, Math.Min(screenPos.Y, wa.Bottom - Height)));
        _shownAt = Environment.TickCount;
        Show();
        Win32.ForceForeground(Handle);
        _search.Inner.Focus();
    }

    void Fill(string text)
    {
        var q = Langs.Plain(text.Trim());
        var rows = _items
            .Where(it => q.Length == 0 || Langs.Plain(it.Name).Contains(q) || it.Code.ToLowerInvariant().Contains(q)
                         || (Langs.Aliases.TryGetValue(it.Code, out var al) && al.Contains(q)))
            .OrderBy(it => it.Code != _current)                // đang chọn lên đầu (OrderBy ổn định)
            .ToList();
        var list = new List<LangList.Row>();
        for (int i = 0; i < rows.Count; i++)
        {
            list.Add(new LangList.Row { Code = rows[i].Code, Name = rows[i].Name, Current = rows[i].Code == _current });
            if (i == 0 && rows.Count > 1 && rows[i].Code == _current)
                list.Add(new LangList.Row { Separator = true });
        }
        if (list.Count == 0) list.Add(new LangList.Row { Name = "Không tìm thấy ngôn ngữ", Info = true });
        _list.SetRows(list);
    }

    void OnSearchKey(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down: _list.MoveSel(1); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Up: _list.MoveSel(-1); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Enter: _list.ActivateSelected(); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Escape: Hide(); e.Handled = e.SuppressKeyPress = true; break;
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (Environment.TickCount - _shownAt > 300) Hide();     // bỏ qua lần mất focus ngay lúc vừa mở
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) Dismissed?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (!Win32.IsWin11)
            Gfx.DrawRound(g, _light ? Color.FromArgb(60, 0, 0, 0) : Color.FromArgb(70, 255, 255, 255), new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Math.Max(2, (int)(14 * _s)));
    }
}
