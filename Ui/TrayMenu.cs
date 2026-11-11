using System.Drawing.Drawing2D;
using Lt = SngTrans.Ui.Theme.Light;

namespace SngTrans.Ui;

/// <summary>Một mục của menu khay.</summary>
public sealed class TrayMenuItem
{
    public string Text = "";
    public bool Checked, Separator, Danger;
    public Action? Click;
}

/// <summary>Danh sách các mục, tự vẽ.</summary>
sealed class TrayMenuList : Ctl
{
    public List<TrayMenuItem> Items = new();
    int _hover = -1;
    public event Action<TrayMenuItem>? Picked;

    public TrayMenuList() { BackColor = Lt.Card; }

    const float FontPx = 12.5f;
    int RowH(TrayMenuItem it) => it.Separator ? Px(7) : Px(28);
    public int ContentH => Items.Sum(it => RowH(it));

    int IndexAt(int y)
    {
        int acc = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            int h = RowH(Items[i]);
            if (y >= acc && y < acc + h) return Items[i].Separator ? -1 : i;
            acc += h;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexAt(e.Y);
        if (i != _hover) { _hover = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int i = IndexAt(e.Y);
            if (i >= 0) Picked?.Invoke(Items[i]);
        }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Lt.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int y = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            var it = Items[i];
            int h = RowH(it);
            if (it.Separator)
            {
                using var b = new SolidBrush(Lt.Border);
                g.FillRectangle(b, Px(8), y + h / 2, Width - Px(16), 1);
            }
            else
            {
                if (i == _hover)
                    Gfx.FillRound(g, Lt.NavHover, new RectangleF(0, y + 1, Width, h - 2), Px(8));
                if (it.Checked)
                    Gfx.Text(g, Theme.GCheck, Theme.Icon(11, S), new Rectangle(Px(6), y, Px(18), h), Lt.Accent,
                        Gfx.Single | TextFormatFlags.HorizontalCenter);
                Gfx.Text(g, it.Text, Theme.Ui(FontPx, false, S), new Rectangle(Px(28), y, Math.Max(1, Width - Px(36)), h),
                    it.Danger ? Lt.Danger : Lt.Text, Gfx.Single | TextFormatFlags.EndEllipsis);
            }
            y += h;
        }
    }
}

/// <summary>
/// Menu khay hệ thống tự vẽ (thay ContextMenuStrip). Tự đo kích thước theo DPI thật của cửa sổ
/// nên luôn hiện đủ mọi mục, và có nền trắng cùng kiểu với phần còn lại của app.
/// </summary>
public sealed class TrayMenu : Form
{
    readonly TrayMenuList _list = new();
    readonly Point _anchor;
    float _s;
    int _shownAt;
    readonly System.Windows.Forms.Timer _watch = new() { Interval = 40 };

    TrayMenu(List<TrayMenuItem> items, Point anchor)
    {
        _anchor = anchor;
        _s = Win32.DpiAt(anchor.X, anchor.Y) / 96f;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Lt.Card;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        _list.Items = items;
        Controls.Add(_list);
        _list.Picked += it =>
        {
            var act = it.Click;
            Close();
            act?.Invoke();
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        // Tự ẩn khi bấm ra ngoài (không phụ thuộc việc menu có giành được focus hay không)
        _watch.Tick += (_, _) =>
        {
            if (IsDisposed) return;
            if (Environment.TickCount - _shownAt > 250 && Win32.AnyMouseButtonDown() && !Bounds.Contains(Cursor.Position))
                Close();
        };
        Location = anchor;            // đặt trước khi tạo cửa sổ để nó nằm đúng màn hình
    }

    /// <summary>Mở menu tại điểm anchor (toạ độ màn hình). Mỗi lần mở là một cửa sổ mới.</summary>
    public static TrayMenu Open(List<TrayMenuItem> items, Point anchor)
    {
        var m = new TrayMenu(items, anchor);
        m.Show();
        Win32.ForceForeground(m.Handle);
        return m;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;       // CS_DROPSHADOW
            cp.ExStyle |= 0x80;             // WS_EX_TOOLWINDOW (không hiện ở Alt+Tab)
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _s = Win32.DpiOfWindow(Handle) / 96f;
        Win32.SetRoundedBorder(Handle, 0xE3E3E3);     // viền #e3e3e3 (BGR)
        Fit();
    }

    void Fit()
    {
        int P(float v) => (int)Math.Round(v * _s);
        _list.S = _s;
        var tf = Theme.Ui(12.5f, false, _s);
        int textW = 0;
        foreach (var it in _list.Items)
            if (!it.Separator) textW = Math.Max(textW, Gfx.TextWidth(it.Text, tf));
        int pad = P(4);
        int w = Math.Max(P(150), textW + P(28 + 10) + 2 * pad);
        int h = _list.ContentH + 2 * pad;
        ClientSize = new Size(w, h);
        _list.SetBounds(pad, pad, w - 2 * pad, h - 2 * pad);

        if (!Win32.IsWin11)
        {
            using var path = Gfx.Round(new RectangleF(0, 0, Width, Height), P(12));
            Region = new Region(path);
        }

        var wa = Screen.FromPoint(_anchor).WorkingArea;
        int x = Math.Max(wa.Left, Math.Min(_anchor.X, wa.Right - Width));
        int y = _anchor.Y > wa.Top + wa.Height / 2 ? _anchor.Y - Height : _anchor.Y;   // gần đáy màn hình thì mở lên trên
        y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height));
        Location = new Point(x, y);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _shownAt = Environment.TickCount;
        _watch.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _watch.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!IsDisposed && Environment.TickCount - _shownAt > 200) Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Win32.IsWin11) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Gfx.DrawRound(g, Lt.Border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1),
            Math.Max(2, (int)(12 * _s)));
    }
}
