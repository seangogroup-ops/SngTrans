using System.Drawing.Drawing2D;

namespace SngTrans.Ui;

/// <summary>Thẻ dịch nổi: không giành focus của app đang bôi đen, kéo được, ghim được.</summary>
public sealed class Card : Form, ICardState
{
    const int BodyW = 388, Pad = 16, InnerW = BodyW - 2 * Pad;
    /// <summary>Thu nhỏ cả thẻ dịch so với kích thước chuẩn (1 = cũ, 0.8 = nhỏ hơn 20%). Muốn nhỏ nữa thì giảm số này.</summary>
    const float CardShrink = 0.8f;

    public event Action<(string Src, string Tgt)?>? TranslateRequested;   // null = dịch tự động
    public event Action<string, string>? SpeakRequested;
    public event Action<string>? CopyRequested;
    public event Action? AddRequested;
    public event Action<bool>? BlockToggled;
    public event Action? Hidden;

    readonly Settings _settings;
    float S = 1f;
    int Px(float v) => (int)Math.Round(v * S);

    // trạng thái
    string _text = "", _exe = "", _detected = "en", _srcCode = "auto", _tgtCode;
    TranslationResult? _res;
    (string Src, string Tgt)? _manual;
    Point _anchor;
    string? _side;
    bool _userMoved, _loading = true, _error, _showSpk1, _showSpk2, _noted;
    int _maxH = 300, _tbH = 24;
    volatile bool _pinned, _isOpen, _popupOpen;
    IntPtr _hwnd;

    public bool IsOpen => _isOpen;
    public bool PopupOpen => _popupOpen;
    public bool Pinned => _pinned;
    public IntPtr Hwnd => _hwnd;
    public TranslationResult? Res => _res;

    // điều khiển
    readonly Pill _srcPill = new(), _tgtPill = new();
    readonly FlatBtn _swap, _pin, _close, _spk1, _spk2, _copy, _add, _retry;
    readonly DragLabel _orig = new();
    readonly RichView _tb = new();
    readonly Skeleton _skel = new();
    readonly Check _block = new();
    readonly Ctl _sep = new() { BackColor = Color.FromArgb(18, 255, 255, 255), PassThrough = true };
    readonly List<Ctl> _ctls = new();
    readonly System.Windows.Forms.Timer _copyTimer = new() { Interval = 1300 };
    LangPicker? _picker;
    bool _pickIsSrc;

    public Card(Settings settings)
    {
        _settings = settings;
        _tgtCode = settings.Native;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.CardBg;
        DoubleBuffered = true;

        _swap = Btn(Theme.GForward, "Đổi chiều dịch", 15);
        _pin = Btn(Theme.GPin, "Ghim (không tự đóng)", 16);
        _pin.Checkable = true; _pin.CheckedFore = Theme.C("#60a5fa");
        _close = Btn(Theme.GCancel, "Đóng", 16);
        _spk1 = Btn(Theme.GVolume, "Nghe câu gốc", 16);
        _spk2 = Btn(Theme.GVolume, "Nghe bản dịch", 16);
        _copy = Btn(Theme.GCopy, "Sao chép bản dịch", 16);
        _add = new FlatBtn
        {
            Caption = "Ghi chú", Bold = true, FontPx = 13, Radius = 12, Fore = Theme.C("#0f1629"), HoverFore = Theme.C("#0f1629"),
            Back = Theme.C("#f5f7fc"), HoverBack = Theme.C("#e4e9f6"), DisabledBack = Theme.C("#222d4f"), DisabledFore = Theme.Dim,
            Cursor = Cursors.Hand,
        };
        _retry = new FlatBtn { Caption = "Thử lại", FontPx = 14, LeftAlign = true, Fore = Theme.Link, HoverFore = Color.White, HoverBack = Color.Empty, Cursor = Cursors.Hand };
        _block.Caption = "Không dịch app này";

        _orig.AutoSize = false;
        _orig.UseMnemonic = false;
        _orig.BackColor = Theme.CardBg;
        _orig.ForeColor = Theme.Muted;

        foreach (var c in new Ctl[] { _srcPill, _tgtPill, _swap, _pin, _close, _spk1, _spk2, _copy, _add, _retry, _tb, _skel, _block, _sep })
            _ctls.Add(c);
        Controls.AddRange(new Control[] { _skel, _srcPill, _swap, _tgtPill, _pin, _close, _orig, _spk1, _tb, _spk2, _copy, _retry, _sep, _block, _add });

        _srcPill.Cursor = _tgtPill.Cursor = Cursors.Hand;
        _srcPill.Click += (_, _) => Pick(_srcPill, true);
        _tgtPill.Click += (_, _) => Pick(_tgtPill, false);
        _swap.Click += (_, _) => Swap();
        _pin.CheckedChanged += (_, _) => _pinned = _pin.Checked;
        _close.Click += (_, _) => Hide();
        _spk1.Click += (_, _) => SpeakRequested?.Invoke(_text, _detected);
        _spk2.Click += (_, _) => { if (_res != null) SpeakRequested?.Invoke(_res.Translation, _res.Tl); };
        _copy.Click += (_, _) => CopyResult();
        _retry.Click += (_, _) => { ShowLoading(); TranslateRequested?.Invoke(_manual); };
        _add.Click += (_, _) => AddRequested?.Invoke();
        _block.CheckedChanged += (_, _) => BlockToggled?.Invoke(_block.Checked);
        _copyTimer.Tick += (_, _) => { _copyTimer.Stop(); _copy.Glyph = Theme.GCopy; _copy.Fore = Theme.Muted; _copy.Invalidate(); };
        _skel.PassThrough = true;

        _srcPill.SetLang("auto", "Tự động", null);
        _tgtPill.SetLang(_settings.Native, Langs.Name(_settings.Native), _settings.Native);
        SetNoted(false);
        ApplyScale(1f);
    }

    FlatBtn Btn(string glyph, string tip, float iconPx)
    {
        var b = new FlatBtn { Glyph = glyph, IconPx = iconPx, Cursor = Cursors.Hand };
        _tips.SetToolTip(b, tip);
        return b;
    }

    readonly ToolTip _tips = new();

    // ------------------------------------------------------------ kiểu cửa sổ
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80 | 0x8;     // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
            cp.ClassStyle |= 0x20000;                  // CS_DROPSHADOW
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hwnd = Handle;
        Win32.SetRoundedBorder(Handle, 0x472D25);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case 0x21:                                  // WM_MOUSEACTIVATE: bấm vào thẻ không kích hoạt nó
                m.Result = (IntPtr)3;                   // MA_NOACTIVATE
                return;
            case 0x84:                                  // WM_NCHITTEST: vùng trống của thẻ kéo được như thanh tiêu đề
                base.WndProc(ref m);
                if (m.Result == (IntPtr)1) m.Result = (IntPtr)2;     // HTCLIENT -> HTCAPTION
                return;
            case 0x2E0:                                 // WM_DPICHANGED: tự tính tỉ lệ theo màn hình nên bỏ qua
                return;
            case 0xA3: case 0xA4: case 0xA5:            // nhấp đúp / bấm phải lên "thanh tiêu đề": bỏ
                return;
            case 0x232:                                 // WM_EXITSIZEMOVE: người dùng vừa kéo thẻ
                _userMoved = true;
                break;
        }
        base.WndProc(ref m);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _isOpen = Visible;
        if (!Visible)
        {
            _skel.Stop();
            _picker?.Hide();
            Hidden?.Invoke();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Win32.IsWin11) return;                      // Win11 đã có viền + bo góc của hệ thống
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Gfx.DrawRound(e.Graphics, Theme.CardBorder, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Px(14));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _copyTimer.Dispose();
            _tips.Dispose();
            _picker?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------ tỉ lệ
    void ApplyScale(float s)
    {
        s *= CardShrink;
        S = s;
        foreach (var c in _ctls) c.S = s;
        _block.FontPx = 12;
        _srcPill.Remeasure();
        _tgtPill.Remeasure();
    }

    // ------------------------------------------------------------ sự kiện nội bộ
    void Pick(Pill pill, bool isSrc)
    {
        var entries = new List<(string, string)>();
        if (isSrc) entries.Add(("auto", "Tự động"));
        foreach (var (code, name) in Langs.All)
        {
            if (isSrc && _srcCode == "auto" && Langs.Same(code, _detected)) continue;   // ngôn ngữ vừa nhận diện đã hiện ở viên rồi
            entries.Add((code, name));
        }
        if (_picker == null)
        {
            _picker = new LangPicker();
            _picker.Dismissed += () => _popupOpen = false;
            _picker.Chosen += code => Picked(code, _pickIsSrc);
        }
        _pickIsSrc = isSrc;
        _popupOpen = true;
        var pos = PointToScreen(new Point(Px(6), pill.Bottom + Px(6)));
        _picker.OpenAt(entries, pill.Code, pos, S, Px(BodyW - 12));
    }

    void Picked(string code, bool isSrc)
    {
        if (isSrc) _srcCode = code; else _tgtCode = code;
        _manual = (_srcCode, _tgtCode);
        ShowLoading();
        TranslateRequested?.Invoke(_manual);
    }

    void Swap()
    {
        var nt = Langs.Same(_tgtCode, _settings.Native) ? _settings.Foreign : _settings.Native;
        _manual = ("auto", nt);
        ShowLoading();
        TranslateRequested?.Invoke(_manual);
    }

    void CopyResult()
    {
        if (_res == null) return;
        CopyRequested?.Invoke(_res.Translation);
        _copy.Glyph = Theme.GCheck;
        _copy.Fore = Theme.C("#4ade80");
        _copy.Invalidate();
        _copyTimer.Stop();
        _copyTimer.Start();
    }

    // ------------------------------------------------------------ hiển thị
    public void Present(string text, Point anchor, string exe = "")
    {
        _text = text; _exe = exe; _anchor = anchor; _manual = null; _res = null;
        if (!_pinned) { _userMoved = false; _side = null; }
        _block.SetSilently(false);
        _srcCode = "auto";
        _tgtCode = _settings.Native;
        ApplyScale(Win32.DpiAt(anchor.X, anchor.Y) / 96f);
        _srcPill.SetLang("auto", "Tự động", null);
        _tgtPill.SetLang(_tgtCode, Langs.Name(_tgtCode), _tgtCode);
        ShowLoading();
        if (!Visible) Show();
        Win32.TopMostNoActivate(Handle);
    }

    void SetNoted(bool added)
    {
        _noted = added;
        if (added)
        {
            _add.Glyph = Theme.GCheck; _add.IconPx = 13; _add.Caption = "Đã ghi chú";
            _add.DisabledBack = Theme.C("#22c55e"); _add.DisabledFore = Color.White;
            _add.Enabled = false;
        }
        else
        {
            _add.Glyph = ""; _add.Caption = "Ghi chú";
            _add.DisabledBack = Theme.C("#222d4f"); _add.DisabledFore = Theme.Dim;
            _add.Enabled = false;                       // chỉ bật khi đã có bản dịch
        }
        _add.Invalidate();
    }

    public void MarkAdded() => SetNoted(true);

    void SetOrig(bool word, bool speaker)
    {
        float px = word ? 18 : 13;
        var font = Theme.Ui(px, word, S);
        int w = Px(InnerW) - (speaker ? Px(32) : 0);
        var (shown, n) = Gfx.FitLines(_text, font, w, word ? 2 : 3);
        _orig.Font = font;
        _orig.ForeColor = word ? Theme.Txt : Theme.Muted;
        _orig.Text = shown;
        _orig.Size = new Size(w, font.Height * n + 2);
        _tips.SetToolTip(_orig, _text.Length > 600 ? _text[..600] : _text);
        _showSpk1 = speaker;
    }

    void ShowLoading()
    {
        _loading = true;
        _error = false;
        SetNoted(false);
        Relayout();
        _skel.Start();
    }

    List<Block> BuildBlocks(TranslationResult res)
    {
        var bl = new List<Block> { new() { Text = res.Translation, Px = 16, Color = Theme.Txt } };
        Block Title(string t) => new() { Text = t.ToUpperInvariant(), Px = 10, Bold = true, Color = Theme.Dim, Before = 14, After = 3 };
        var body = Theme.C("#d5dcf5");
        if (res.Meanings.Count > 0)
        {
            bl.Add(Title("Nghĩa"));
            foreach (var m in res.Meanings)
                bl.Add(new Block { Lead = m.Pos, Italic = true, LeadColor = Theme.Dim, Text = string.Join(" · ", m.Words), Px = 13, Color = body, Before = 3, After = 3 });
        }
        if (res.Synonyms.Count > 0)
        {
            bl.Add(Title("Từ đồng nghĩa"));
            bl.Add(new Block { Text = string.Join(" · ", res.Synonyms), Px = 13, Color = body, Before = 3, After = 3 });
        }
        if (res.Examples.Count > 0)
        {
            bl.Add(Title("Ví dụ"));
            foreach (var x in res.Examples)
                bl.Add(new Block { Lead = "•", LeadColor = Theme.Dim, Text = x, Px = 13, Color = Theme.Muted, Before = 4, After = 4 });
        }
        return bl;
    }

    public void ShowResult(TranslationResult res)
    {
        _res = res;
        _detected = res.Detected;
        _tgtCode = res.Tl;
        _tgtPill.SetLang(res.Tl, Langs.Name(res.Tl), res.Tl);
        if (_manual == null || _manual.Value.Src == "auto")
        {
            _srcCode = "auto";
            _srcPill.SetLang("auto", Langs.Name(_detected), _detected);
        }
        else
        {
            _srcCode = _manual.Value.Src;
            _srcPill.SetLang(_srcCode, Langs.Name(_srcCode), _srcCode);
        }
        bool srcNative = Langs.Same(_detected, _settings.Native);
        bool word = _text.Length <= 40 && !_text.Contains('\n') && !srcNative;
        _loading = false;
        _error = false;
        SetOrig(word, true);   // luôn có nút nghe câu gốc, kể cả khi nguồn là tiếng mẹ đẻ
        _showSpk2 = true;   // luôn cho nghe bản dịch, kể cả khi dịch sang tiếng mẹ đẻ
        _copy.Glyph = Theme.GCopy; _copy.Fore = Theme.Muted;
        _skel.Stop();
        SetNoted(false);
        _add.Enabled = true;
        _tbContentH = _tb.SetBlocks(BuildBlocks(res), Px(InnerW));
        Relayout();
    }

    public void ShowError(string msg)
    {
        _loading = false;
        _error = true;
        _skel.Stop();
        SetOrig(false, false);
        SetNoted(false);
        _tbContentH = _tb.SetBlocks(new List<Block> { new() { Text = msg, Px = 14, Color = Theme.Err } }, Px(InnerW));
        Relayout();
    }

    int _tbContentH;

    // ------------------------------------------------------------ kích thước / vị trí
    Rectangle ScreenGeo() => Screen.FromPoint(_anchor).WorkingArea;

    void Relayout()
    {
        var g = ScreenGeo();
        int room = Math.Max(_anchor.Y - g.Top, g.Bottom - _anchor.Y);
        _maxH = (int)(Math.Max(90, Math.Min(320, room / S - 250)) * S);

        int pad = Px(Pad), inner = Px(InnerW), W = Px(BodyW), btn = Px(28);
        SuspendLayout();
        int y = Px(14);

        var content = new Control[] { _srcPill, _swap, _tgtPill, _pin, _close, _orig, _tb, _sep, _block, _add };
        foreach (var c in content) c.Visible = !_loading;
        _skel.Visible = _loading;

        if (_loading)
        {
            _skel.SetBounds(pad, y, inner, Px(114));
            _spk1.Visible = _spk2.Visible = _copy.Visible = _retry.Visible = false;
            y += Px(114);
        }
        else
        {
            int ph = Px(32);
            int x = pad;
            _srcPill.Height = ph; _srcPill.Remeasure(); _srcPill.Location = new Point(x, y); x += _srcPill.Width + Px(2);
            _swap.SetBounds(x, y + (ph - btn) / 2, btn, btn); x += btn + Px(2);
            _tgtPill.Height = ph; _tgtPill.Remeasure(); _tgtPill.Location = new Point(x, y);
            _close.SetBounds(W - pad - btn, y + (ph - btn) / 2, btn, btn);
            _pin.SetBounds(_close.Left - btn - Px(2), y + (ph - btn) / 2, btn, btn);
            y += ph + Px(12);

            _orig.Location = new Point(pad, y);
            _spk1.Visible = _showSpk1;
            _spk1.SetBounds(pad + inner - btn, y, btn, btn);
            y += Math.Max(_orig.Height, _showSpk1 ? btn : 0) + Px(8);

            _tbH = Math.Max(Px(24), Math.Min(_tbContentH, _maxH));
            _tb.SetBounds(pad, y, inner, _tbH);
            y += _tbH;

            y += Px(4);
            _spk2.Visible = !_error && _showSpk2;
            _copy.Visible = !_error;
            _retry.Visible = _error;
            int ax = pad;
            if (_spk2.Visible) { _spk2.SetBounds(ax, y, btn, btn); ax += btn; }
            _copy.SetBounds(ax, y, btn, btn);
            _retry.SetBounds(pad, y, Px(120), btn);
            y += btn;

            y += Px(10);
            _sep.SetBounds(pad, y, inner, 1);
            y += 1 + Px(12);

            int fh = Px(36);
            _block.Width = Math.Min(inner / 2 + Px(30), _block.PreferredWidth);
            _block.Height = Px(24);
            _block.Location = new Point(pad, y + (fh - _block.Height) / 2);
            int aw = Gfx.TextWidth(_add.Caption, Theme.Ui(13, true, S)) + Px(32) + (_noted ? Px(20) : 0);
            _add.SetBounds(W - pad - aw, y, aw, fh);
            y += fh;
        }

        ClientSize = new Size(W, y + Px(14));
        ResumeLayout(false);

        if (!Win32.IsWin11)
        {
            using var path = Gfx.Round(new RectangleF(0, 0, Width, Height), Px(14));
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }
        Reposition();
        Invalidate(true);
    }

    /// <summary>Hiện ngay phía trên con trỏ, căn giữa; thiếu chỗ thì xuống dưới (chọn một lần cho mỗi thẻ).</summary>
    void Reposition()
    {
        if (_pinned || _userMoved) return;
        var g = ScreenGeo();
        int w = Width, h = Height, M = Px(8);
        int above = _anchor.Y - g.Top - Px(14) - M;
        int below = g.Bottom - _anchor.Y - Px(24) - M;
        _side ??= (above >= Px(300) || above >= below) ? "above" : "below";
        int x = Math.Max(g.Left + M, Math.Min(_anchor.X - w / 2, g.Right - w - M));
        int y = _side == "above" ? _anchor.Y - Px(14) - h : _anchor.Y + Px(24);
        y = Math.Max(g.Top + M, Math.Min(y, g.Bottom - h - M));
        Location = new Point(x, y);
    }
}
