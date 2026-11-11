namespace SngTrans.Ui;

/// <summary>Chương trình chạy ngầm ở khay hệ thống: nối hook, thẻ dịch, cài đặt, đọc to.</summary>
public sealed class TrayApp : ApplicationContext
{
    /// <summary>Tên sự kiện để lần mở app thứ hai gọi bản đang chạy ngầm hiện hộp Cài đặt.</summary>
    public const string ShowEventName = @"Local\SngTrans.ShowSettings";

    readonly Settings _settings = new();
    readonly Control _sync = new();                 // để đưa việc từ luồng nền về luồng giao diện
    readonly Card _card;
    readonly Watcher _watcher;
    readonly HotkeyWindow _hotkeys = new();
    readonly Tts _tts = new();
    readonly NotifyIcon _tray = new();
    readonly System.Windows.Forms.Timer _trim = new() { Interval = 4000 };
    readonly System.Windows.Forms.Timer _idle = new() { Interval = 90_000 };    // định kỳ trả RAM nhàn rỗi về cho Windows

    readonly System.Windows.Forms.Timer _upd = new() { Interval = 6 * 3600 * 1000 };   // 6 giờ kiểm tra cập nhật một lần
    bool _checking;
    readonly System.Windows.Forms.Timer _toggleNote = new() { Interval = 350 };   // báo bật/tắt tự dịch sau khi ngừng bấm
    long _shownAt;                                        // lúc hiện thẻ gần nhất (để bỏ qua bấm lặp cùng một đoạn chữ)
    string _failedTag = "";                               // bản đã tự cài lỗi: không thử lại cho tới khi có bản khác hoặc mở lại app

    SettingsForm? _dialog;
    TrayMenu? _menu;
    string _hotkeyActive, _toggleActive;
    int _rid, _current;
    string _text = "", _exe = "";

    public TrayApp(bool openSettings = false)
    {
        _ = _sync.Handle;

        // Hiện icon khay NGAY (rẻ) rồi mới dựng phần nặng, để người dùng thấy app đã lên.
        _tray.Text = "SngTrans";
        _tray.Icon = LoadIcon();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSettings(false); };
        _tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowTrayMenu(); };
        _tray.Visible = true;

        // Mở sẵn kết nối để lần dịch đầu không chậm. Làm hoàn toàn ở luồng nền và chờ có mạng:
        // lúc vừa đăng nhập mạng thường chưa sẵn sàng, gọi sớm chỉ tốn công (và khởi tạo HttpClient trên luồng giao diện).
        _ = Task.Run(async () =>
        {
            // Có mạng rồi thì làm nóng ngay; chưa có thì dò mỗi 0,5 giây (tối đa ~15 giây) rồi làm nóng.
            for (int i = 0; i < 30 && !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); i++)
                await Task.Delay(500).ConfigureAwait(false);
            Translator.WarmUp();
        });

        _card = new Card(_settings);
        _ = _card.Handle;                            // tạo cửa sổ sẵn để lần hiện đầu không bị chậm
        _watcher = new Watcher(_settings, _card);
        _hotkeyActive = _settings.Hotkey;
        _toggleActive = _settings.ToggleHotkey;

        _watcher.TextSelected += (t, e) => UI(() => OnSelected(t, e));
        _watcher.OutsideClick += () => UI(() => _card.Hide());
        _watcher.Escape += () => UI(() => { if (_card.Visible && !_card.Pinned) _card.Hide(); });
        _toggleNote.Tick += (_, _) =>
        {
            _toggleNote.Stop();
            bool on = _settings.Auto;
            Log.Info($"tự dịch khi bôi đen: {(on ? "bật" : "tắt")} (phím tắt)");
            Balloon("SngTrans", on ? "Đã BẬT tự dịch khi bôi đen." : "Đã TẮT tự dịch khi bôi đen.", 1500);
        };
        _hotkeys.Pressed += id =>
        {
            if (id == HotkeyWindow.IdTranslate) _watcher.TriggerHotkey();
            else if (id == HotkeyWindow.IdToggle) ToggleAuto();            // đã ở luồng giao diện: chạy ngay, không qua hàng đợi
            else if (id == HotkeyWindow.IdForceQuit) ForceQuit();
        };

        _tts.Local = LocalVoice.SynthesizeAsync;
        _tts.Failed += msg => UI(() => Balloon("SngTrans", msg, 5000, ToolTipIcon.Warning));
        _card.TranslateRequested += RunTranslate;
        _card.SpeakRequested += _tts.Speak;
        _card.CopyRequested += CopyText;
        _card.AddRequested += AddVocab;
        _card.BlockToggled += ToggleBlock;
        _card.Hidden += () => { _trim.Stop(); _trim.Start(); };

        _trim.Tick += (_, _) =>
        {
            _trim.Stop();
            _trim.Interval = 4000;
            if (!_card.Visible)
            {
                GC.Collect();
                Win32.TrimMemory();
            }
        };

        _idle.Tick += (_, _) =>
        {
            if (InUse) return;                                  // đang dùng thì thôi
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            Win32.TrimMemory();
        };
        _idle.Start();

        _watcher.Start();
        RegisterHotkeys(announce: true);

        if (_settings.FirstRun)
        {
            _settings.Update(d => d.FirstRun = false);
            EnableBootByDefault();                       // mặc định chạy ngầm cùng Windows; tắt ở Cài đặt
            ShowGuideIfNew(() => OpenSettings(true));    // hướng dẫn trước, đóng xong mới mở hộp Cài đặt
        }
        else if (openSettings)
            OpenSettings(false);                         // mở bằng --settings (vd. ngay sau khi build): hiện hộp Cài đặt
        else if (!ShowGuideIfNew())                      // mới tải về / vừa cập nhật phiên bản: hiện hướng dẫn một lần
            Balloon("SngTrans đang chạy", "Bôi đen chữ ở bất kỳ đâu để dịch. Bấm vào biểu tượng khay để cài đặt.", 4000);
        _trim.Interval = 30_000;                         // lần dọn RAM đầu: chờ 30 giây cho máy hết bận lúc khởi động
        _trim.Start();

        // ----- mở app lần nữa (bấm đúp exe/shortcut) khi đang chạy ngầm -> hiện hộp Cài đặt
        var showEv = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (showEv.WaitOne()) UI(() => OpenSettings(false));
        }) { IsBackground = true, Name = "SngTrans.ShowSettings" }.Start();

        // ----- tự kiểm tra và tự cài bản mới (sau 20 giây khi mở máy, rồi 6 giờ một lần)
        _upd.Tick += (_, _) => CheckUpdate(false);
        _upd.Start();
        _ = Task.Delay(20_000).ContinueWith(_ => UI(() => CheckUpdate(false)));
    }

    static Icon LoadIcon()
    {
        try
        {
            using var s = typeof(TrayApp).Assembly.GetManifestResourceStream("icon.ico");
            if (s != null) return new Icon(s, SystemInformation.SmallIconSize);   // icon nhúng sẵn: không phải mở exe lớn để trích icon
        }
        catch { }
        return Theme.LogoIcon(32);
    }

    void UI(Action a)
    {
        try
        {
            if (!_sync.IsDisposed) _sync.BeginInvoke(a);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    void Balloon(string title, string text, int ms, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try { _tray.ShowBalloonTip(ms, title, text, icon); } catch { }
    }

    // ---------------------------------------------------------------- phím tắt
    void RegisterHotkeys(bool announce = false)
    {
        bool ok = _hotkeys.Register(HotkeyWindow.IdTranslate, _settings.Hotkey);
        if (!ok)
        {
            Log.Info($"không đăng ký được phím tắt {_settings.Hotkey} (có thể đang bị app khác dùng)");
            if (announce)
                Balloon("SngTrans", $"Phím tắt {HotkeySpec.Pretty(_settings.Hotkey).Replace("  ", "")} đang bị app khác dùng. Đổi phím khác trong Cài đặt.", 5000, ToolTipIcon.Warning);
        }
        if (!_hotkeys.Register(HotkeyWindow.IdToggle, _settings.ToggleHotkey))
        {
            Log.Info($"không đăng ký được phím tắt bật/tắt tự dịch {_settings.ToggleHotkey} (có thể đang bị app khác dùng)");
            if (announce)
                Balloon("SngTrans", $"Phím bật/tắt tự dịch {HotkeySpec.Pretty(_settings.ToggleHotkey).Replace("  ", "")} đang bị app khác dùng. Đổi phím khác trong Cài đặt.", 5000, ToolTipIcon.Warning);
        }
        if (!_hotkeys.Register(HotkeyWindow.IdForceQuit, HotkeySpec.ForceQuit))
            Log.Info("không đăng ký được phím tắt tắt hẳn");
    }

    /// <summary>Bật/tắt "Tự dịch khi bôi đen" (phím tắt, mặc định Alt+2).</summary>
    void ToggleAuto()
    {
        bool on = !_settings.Auto;
        _settings.UpdateDeferred(d => d.Auto = on);          // đổi tức thì trong bộ nhớ; ghi file dồn lại sau
        _dialog?.SyncFromSettings();
        if (!on && !_card.Pinned) _card.Hide();
        _toggleNote.Stop();                                  // bấm dồn: chỉ báo trạng thái cuối cùng, sau khi người dùng ngừng bấm
        _toggleNote.Start();
    }

    void SetCapturing(bool capturing)
    {
        _watcher.HotkeyPaused = capturing;
        if (capturing) _hotkeys.UnregisterAll();      // để bấm phím mới không kích hoạt phím cũ
        else RegisterHotkeys();
    }

    // ---------------------------------------------------------------- khay
    void ShowTrayMenu()
    {
        try { if (_menu != null && !_menu.IsDisposed) _menu.Close(); } catch { }
        var items = new List<TrayMenuItem>
        {
            new()
            {
                Text = "Tự dịch", Checked = _settings.Auto,
                Click = () => { _settings.Update(d => d.Auto = !d.Auto); _dialog?.SyncFromSettings(); },
            },
            new() { Text = "Ghi chú", Click = () => OpenSettings(false, VocabPage) },
            new() { Text = "Cài đặt", Click = () => OpenSettings(false) },
            new() { Text = $"Cập nhật (v{Updater.Current.ToString(3)})", Click = () => CheckUpdate(true) },
            new() { Text = "Thoát", Danger = true, Click = ForceQuit },
        };
        _menu = TrayMenu.Open(items, Cursor.Position);
    }

    /// <summary>Lần chạy đầu: bật "Chạy cùng Windows". Bỏ qua khi chạy bằng "dotnet run" (không có exe riêng).</summary>
    static void EnableBootByDefault()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path) ||
            string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase)) return;
        bool ok = Win32.SetAutostart(true);
        Log.Info($"bật chạy cùng Windows mặc định -> {(ok ? "ok" : "lỗi")}");
    }

    /// <summary>Chỉ số trang Ghi chú trong hộp Cài đặt (0 = Cài đặt, 1 = Chặn app, 2 = Ghi chú).</summary>
    const int VocabPage = 2;

    GuideForm? _guide;

    /// <summary>Hiện màn hướng dẫn đúng một lần cho mỗi phiên bản: lần đầu tải về hoặc sau khi cập nhật.
    /// Đánh dấu "đã xem" ngay lúc hiện nên có tắt đột ngột cũng không hiện lại. Trả về true nếu đã hiện.</summary>
    bool ShowGuideIfNew(Action? after = null)
    {
        string cur = Updater.Current.ToString(3);
        if (_settings.Data.SeenGuide == cur) { after?.Invoke(); return false; }
        _settings.Update(d => d.SeenGuide = cur);
        _guide = new GuideForm(_settings);
        _guide.FormClosed += (_, _) =>
        {
            var g = _guide;
            _guide = null;
            g?.Dispose();
            _trim.Stop(); _trim.Start();
            after?.Invoke();
        };
        _guide.Show();
        Win32.ForceForeground(_guide.Handle);
        return true;
    }

    void OpenSettings(bool welcome, int? page = null)
    {
        if (_dialog == null || _dialog.IsDisposed)
        {
            _dialog = new SettingsForm(_settings, _tts.Speak, welcome);
            _dialog.Changed += OnSettingsChanged;
            _dialog.ForceQuit += ForceQuit;
            _dialog.CapturingHotkey += SetCapturing;
            _dialog.FormClosed += (_, _) =>
            {
                SetCapturing(false);
                var d = _dialog;
                _dialog = null;
                d?.Dispose();
                _trim.Stop(); _trim.Start();
            };
            _dialog.Show();
        }
        else
        {
            if (_dialog.WindowState == FormWindowState.Minimized) _dialog.WindowState = FormWindowState.Normal;
            _dialog.Show();
        }
        if (page is int p) _dialog.ShowPage(p);
        _dialog.Activate();
        Win32.ForceForeground(_dialog.Handle);
    }

    void OnSettingsChanged()
    {
        if (_settings.Hotkey != _hotkeyActive || _settings.ToggleHotkey != _toggleActive)   // chỉ nạp lại khi đổi phím tắt
        {
            _hotkeyActive = _settings.Hotkey;
            _toggleActive = _settings.ToggleHotkey;
            RegisterHotkeys(announce: true);
        }
    }

    // ---------------------------------------------------------------- dịch
    void OnSelected(string text, string exe)
    {
        long now = Environment.TickCount64;
        if (_card.Visible && text == _text && exe == _exe && now - _shownAt < 1500)
        {
            Win32.TopMostNoActivate(_card.Handle);        // bấm lặp cùng đoạn chữ: chỉ đưa thẻ lên trên, khỏi dịch lại
            return;
        }
        _shownAt = now;
        Log.Info($"hiện thẻ cho {text.Length} ký tự từ {exe}");
        var anchor = Cursor.Position;
        _exe = exe;
        _text = text;
        RunTranslate(null);                           // gửi yêu cầu mạng trước, dựng thẻ trong lúc chờ
        _card.Present(text, anchor, exe);
    }

    void RunTranslate((string Src, string Tgt)? manual)
    {
        int rid = ++_rid;
        _current = rid;
        var text = _text;
        var s = _settings;
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await Translator.TranslateAsync(text, s.Native, s.Foreign, s.Smart, manual).ConfigureAwait(false);
                Log.Info($"dịch xong {text.Length} ký tự -> {r.Tl}");
                UI(() => { if (rid == _current && _card.Visible) _card.ShowResult(r); });
            }
            catch (Exception e)
            {
                Log.Error("dịch lỗi", e);
                UI(() => { if (rid == _current && _card.Visible) _card.ShowError(FriendlyError(e)); });
            }
        });
    }

    /// <summary>Đổi lỗi kỹ thuật (tiếng Anh, tên máy chủ...) thành câu dễ hiểu cho người dùng.</summary>
    static string FriendlyError(Exception e)
    {
        const string fail = "Không dịch được";
        const string offline = "Vui lòng kết nối mạng rồi thử lại";
        for (var x = e; x != null; x = x.InnerException)
        {
            if (x is System.Net.Sockets.SocketException or System.Net.Http.HttpRequestException
                or System.IO.IOException)
                return $"{fail} ({offline})";
        }
        var m = e.Message ?? "";
        if (m.Contains("No such host", StringComparison.OrdinalIgnoreCase)
            || m.Contains("không kết nối được", StringComparison.OrdinalIgnoreCase)
            || m.Contains("quá thời gian chờ", StringComparison.OrdinalIgnoreCase)
            || m.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || m.Contains("network", StringComparison.OrdinalIgnoreCase))
            return $"{fail} ({offline})";
        return $"{fail} ({m})";
    }

    void CopyText(string t)
    {
        try { Clipboard.SetDataObject(t, true, 5, 50); }
        catch (Exception e) { Log.Error("không chép được vào clipboard", e); }
    }

    void AddVocab()
    {
        var r = _card.Res;
        if (r == null) return;
        _settings.AddVocab(new VocabItem
        {
            Text = _text,
            Translation = r.Translation,
            Source = r.Detected,
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
        _card.MarkAdded();
    }

    void ToggleBlock(bool on)
    {
        var exe = _exe;
        if (string.IsNullOrEmpty(exe)) return;
        _settings.Update(d =>
        {
            if (on && !d.BlockedApps.Contains(exe)) d.BlockedApps.Add(exe);
            else if (!on) d.BlockedApps.Remove(exe);
        });
        if (on)
        {
            _card.Hide();
            Balloon("SngTrans", $"Đã tắt dịch trong {exe}. Bật lại ở Cài đặt.", 3000);
        }
    }

    // ---------------------------------------------------------------- cập nhật
    /// <summary>true khi đang dùng app (thẻ dịch hoặc hộp Cài đặt đang mở): không nên khởi động lại lúc này.</summary>
    bool InUse => _card.Visible || (_dialog != null && !_dialog.IsDisposed) || (_guide != null && !_guide.IsDisposed);

    /// <summary>Tự cập nhật, không hỏi và không hiện thông báo. manual = true khi bấm "Cập nhật" ở menu khay.</summary>
    async void CheckUpdate(bool manual)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            if (Updater.Repo() == null)
            {
                if (manual)
                    MessageBox.Show("Chưa cấu hình nơi lấy bản cập nhật.\n\nTạo file:\n" + Path.Combine(Settings.DataDir(), "update-repo.txt")
                                    + "\nvà ghi vào đó  chủ/tên-kho  (ví dụ: abc/SngTrans) hoặc dán link GitHub của dự án.",
                        "SngTrans", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var info = await Task.Run(() => Updater.CheckAsync());
            if (info == null) return;                                // đang dùng bản mới nhất
            if (!manual && info.Tag == _failedTag) return;           // tự cài bản này đã lỗi một lần: đừng thử lại liên tục

            if (info.AssetUrl != null)
            {
                // Chờ lúc không dùng app rồi mới cài, tránh làm mất thẻ dịch hoặc cài đặt đang mở.
                while (InUse) await Task.Delay(15_000);

                Log.Info($"tự cập nhật {Updater.Current.ToString(3)} -> {info.Version.ToString(3)}");
                if (await Task.Run(() => Updater.ApplyAsync(info)))
                {
                    try { _watcher.Stop(); _tray.Visible = false; } catch { }
                    _settings.Flush();
                    Environment.Exit(0);                              // bản mới đã được mở
                    return;
                }
                _failedTag = info.Tag;
                Log.Info("tự cập nhật không thành công (tải lỗi, thiếu hoặc sai SHA256)");
                if (!manual) return;                                  // chạy ngầm: im lặng, lần sau thử bản khác
            }
            if (manual) Updater.OpenUrl(info.PageUrl);                // chỉ mở trình duyệt khi người dùng tự bấm
        }
        catch (Exception e)
        {
            Log.Info("kiểm tra cập nhật lỗi: " + e.Message);
        }
        finally { _checking = false; }
    }

    // ---------------------------------------------------------------- thoát
    void Quit()
    {
        _settings.Flush();
        _watcher.Stop();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        var t = new System.Threading.Timer(_ => Environment.Exit(0), null, 3000, Timeout.Infinite);   // treo thì vẫn tắt hẳn sau 3 giây
        GC.KeepAlive(t);
        ExitThread();
    }

    /// <summary>Kết thúc tác vụ: dừng ngay, không chờ đóng từng cửa sổ.</summary>
    void ForceQuit()
    {
        Log.Info("tắt hẳn (kết thúc tác vụ)");
        _settings.Flush();
        try
        {
            _watcher.Stop();
            _tray.Visible = false;
        }
        catch { }
        Environment.Exit(0);
    }
}
