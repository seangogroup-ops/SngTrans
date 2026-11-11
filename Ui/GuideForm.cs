using System.Drawing.Drawing2D;

namespace SngTrans.Ui;

/// <summary>Màn hướng dẫn MỘT trang, nền trắng, khung cửa sổ Windows bình thường: phím dịch ngay (mặc định Alt+1) bên trái,
/// phím bật/tắt tự dịch (mặc định Alt+2) bên phải, nút Next ở dưới. Hiện đúng một lần cho mỗi phiên bản (TrayApp lo việc đánh dấu).
/// Phím hiển thị lấy từ cài đặt thật. Tự co cho vừa màn hình và canh giữa vùng làm việc (không tràn mép, không đè thanh tác vụ).
/// Nhẹ RAM: không dùng ảnh nào, toàn bộ vẽ bằng GDI nên đóng là giải phóng hết.</summary>
public sealed class GuideForm : Form
{
    // kích thước thiết kế (đơn vị 96 dpi, phần nội dung bên trong khung cửa sổ)
    const int FormW = 820, FormH = 385, ColW = 340, ColCx = 205;
    const int TitleY = 22, BadgeY = 77, BadgeD = 24, KeysY = 111, KeyH = 46;
    const int PillPadX = 18, PillPadY = 11, PlusW = 32, KeyMinW = 60, KeyPadX = 14;
    const int HeadY = 195, DescY = 227, DividerTop = 80, DividerBottom = 295;
    const int BtnW = 150, BtnH = 42, BtnBottom = 22;

    const string Title = "Hai phím tắt cần nhớ";
    static readonly (string Head, string Desc)[] Cols =
    {
        ("Dịch ngay phần đang bôi đen", "Bôi đen chữ ở app nào cũng được, bấm phím này là dịch ngay."),
        ("Bật / tắt tự dịch khi bôi đen", "Mỗi lần bấm là đổi giữa bật và tắt, app báo ngay ở góc màn hình."),
    };

    static readonly Color Ink = Theme.C("#111111"), Sub = Theme.C("#2b2b2b"), Line = Theme.Light.Border,
                          Accent = Theme.Light.Accent, PillBg = Theme.C("#f2f6fb"), KeyEdge = Theme.C("#9aa7b8");

    const TextFormatFlags Center = TextFormatFlags.HorizontalCenter;
    const TextFormatFlags DescFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak |
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.Top;

    readonly Settings _settings;
    readonly FlatBtn _next = new();
    readonly string[][] _keys = { Array.Empty<string>(), Array.Empty<string>() };
    readonly int[][] _keyW = { Array.Empty<int>(), Array.Empty<int>() };
    readonly int[] _pillW = new int[2];
    Font _fTitle = null!, _fHead = null!, _fDesc = null!, _fKey = null!, _fBadge = null!;
    float S = 1f;

    int Px(float v) => (int)Math.Round(v * S);

    public GuideForm(Settings settings)
    {
        _settings = settings;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;      // tự canh giữa vùng làm việc ở OnHandleCreated
        ShowInTaskbar = true;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.White;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "SngTrans";
        try { Icon = Theme.LogoIcon(32); } catch { }

        _next.Caption = "Next";
        _next.Bold = true;
        _next.FontPx = 17;
        _next.Radius = 7;
        _next.Fore = _next.HoverFore = Color.White;
        _next.Back = Accent;
        _next.HoverBack = Theme.Light.AccentHover;
        _next.Cursor = Cursors.Hand;
        _next.Click += (_, _) => Close();
        Controls.Add(_next);
    }

    /// <summary>Chuẩn bị một lần: phông, các phím, độ rộng phím và khung nền phím.</summary>
    void BuildLayout()
    {
        _fTitle = Theme.Ui(27, true, S);
        _fHead = Theme.Ui(18.5f, true, S);
        _fDesc = Theme.Ui(15, false, S);
        _fKey = Theme.Ui(20, true, S);
        _fBadge = Theme.Ui(13, true, S);

        string[] specs = { _settings.Hotkey, _settings.ToggleHotkey };
        for (int i = 0; i < 2; i++)
        {
            _keys[i] = HotkeySpec.Pretty(specs[i]).Split('+').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            _keyW[i] = _keys[i].Select(p => Math.Max(Px(KeyMinW), Gfx.TextWidth(p, _fKey) + Px(KeyPadX * 2))).ToArray();
            _pillW[i] = _keyW[i].Sum() + Px(PlusW) * Math.Max(0, _keys[i].Length - 1) + Px(PillPadX * 2);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // chọn màn hình đang có con trỏ (nếu không lấy được thì màn chính), dùng vùng làm việc (trừ thanh tác vụ)
        Rectangle wa;
        try { wa = Screen.FromPoint(Cursor.Position).WorkingArea; }
        catch { wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720); }

        // tỉ lệ theo DPI, nhưng co lại nếu cửa sổ (cả khung tiêu đề ~ 60 px) không lọt vào 92% vùng làm việc
        float s = Math.Max(1f, DeviceDpi / 96f);
        s = Math.Min(s, (wa.Width * 0.92f) / FormW);
        s = Math.Min(s, (wa.Height * 0.92f - 60f * s) / FormH);
        S = Math.Max(0.6f, s);
        _next.S = S;

        ClientSize = new Size(Px(FormW), Px(FormH));
        _next.Size = new Size(Px(BtnW), Px(BtnH));
        _next.Location = new Point((Px(FormW) - _next.Width) / 2, Px(FormH - BtnBottom - BtnH));
        BuildLayout();

        int x = wa.X + (wa.Width - Width) / 2;
        int y = wa.Y + (wa.Height - Height) / 2;
        Location = new Point(Math.Max(wa.X, x), Math.Max(wa.Y, y));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape || keyData == Keys.Enter) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------ vẽ
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_fTitle == null) return;                   // chưa bố trí xong (handle chưa tạo)
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int W = ClientSize.Width;
        Gfx.Text(g, Title, _fTitle, new Rectangle(0, Px(TitleY), W, Px(40)), Ink, Gfx.Single | Center);

        using (var pen = new Pen(Line, Math.Max(1f, S)))
            g.DrawLine(pen, W / 2, Px(DividerTop), W / 2, Px(DividerBottom));

        DrawColumn(g, 0, Px(ColCx));
        DrawColumn(g, 1, W - Px(ColCx));
    }

    /// <summary>Một cột: chấm số, hàng phím, tiêu đề, mô tả. cx là tâm của cột.</summary>
    void DrawColumn(Graphics g, int i, int cx)
    {
        int colW = Px(ColW), d = Px(BadgeD);

        var badge = new Rectangle(cx - d / 2, Px(BadgeY), d, d);
        Gfx.FillRound(g, Accent, badge, d / 2f);
        Gfx.Text(g, (i + 1).ToString(), _fBadge, badge, Color.White, Gfx.Single | Center);

        DrawKeys(g, i, cx);

        Gfx.Text(g, Cols[i].Head, _fHead, new Rectangle(cx - colW / 2, Px(HeadY), colW, Px(28)), Ink, Gfx.Single | Center);
        Gfx.Text(g, Cols[i].Desc, _fDesc, new Rectangle(cx - colW / 2, Px(DescY), colW, Px(46)), Sub, DescFlags);
    }

    /// <summary>Vẽ tổ hợp phím thành các phím bấm nổi trên nền xanh nhạt: Alt + F1.</summary>
    void DrawKeys(Graphics g, int i, int cx)
    {
        var keys = _keys[i];
        var widths = _keyW[i];
        int h = Px(KeyH), plus = Px(PlusW);
        int edge = Math.Max(1, Px(2)), lip = Px(4), rad = Px(9);

        Gfx.FillRound(g, PillBg, new RectangleF(cx - _pillW[i] / 2f, Px(KeysY), _pillW[i], Px(KeyH + PillPadY * 2)), Px(14));

        int x = cx - _pillW[i] / 2 + Px(PillPadX), y = Px(KeysY + PillPadY);
        for (int k = 0; k < keys.Length; k++)
        {
            int w = widths[k];
            var face = new Rectangle(x, y + edge, w, h - edge - lip);       // phần mặt phím (không tính viền và gờ dưới)
            Gfx.FillRound(g, KeyEdge, new RectangleF(x, y, w, h), rad);
            Gfx.FillRound(g, Color.White, new RectangleF(x + edge, y + edge, w - edge * 2, h - edge - lip), Math.Max(1, rad - edge));
            Gfx.Text(g, keys[k], _fKey, face, Ink, Gfx.Single | Center);
            x += w;
            if (k < keys.Length - 1)
            {
                Gfx.Text(g, "+", _fKey, new Rectangle(x, face.Y, plus, face.Height), Accent, Gfx.Single | Center);
                x += plus;
            }
        }
    }
}
