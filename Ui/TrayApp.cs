using System.Diagnostics;
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
    string _notifiedTag = "";

    SettingsForm? _dialog;
    TrayMenu? _menu;
    string _hotkeyActive;
    int _rid, _current;
    string _text = "", _exe = "";

    public TrayApp(bool openSettings = false)
    {
        _ = _sync.Handle;
        Translator.WarmUp();                         // mở sẵn kết nối để lần dịch đầu không bị chậm

        _card = new Card(_settings);
        _ = _card.Handle;                            // tạo cửa sổ sẵn để lần hiện đầu không bị chậm
        _watcher = new Watcher(_settings, _card);
        _hotkeyActive = _settings.Hotkey;

        _watcher.TextSelected += (t, e) => UI(() => OnSelected(t, e));
        _watcher.OutsideClick += () => UI(() => _card.Hide());
        _watcher.Escape += () => UI(() => { if (_card.Visible && !_card.Pinned) _card.Hide(); });
        _hotkeys.Pressed += id =>
        {
            if (id == HotkeyWindow.IdTranslate) _watcher.TriggerHotkey();
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
            if (!_card.Visible)
            {
                GC.Collect();
                Win32.TrimMemory();
            }
        };

        _idle.Tick += (_, _) =>
        {
            if (_card.Visible || (_dialog != null && !_dialog.IsDisposed)) return;     // đang dùng thì thôi
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            Win32.TrimMemory();
        };
        _idle.Start();

        // ----- khay hệ thống (menu tự vẽ, mở khi bấm chuột phải)
        _tray.Text = "SngTrans";
        _tray.Icon = LoadIcon();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSettings(false); };
        _tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowTrayMenu(); };
        _tray.Visible = true;

        _watcher.Start();
        RegisterHotkeys(announce: true);

        if (_settings.FirstRun)
        {
            _settings.Update(d => d.FirstRun = false);
            EnableBootByDefault();                       // mặc định chạy ngầm cùng Windows; tắt ở Cài đặt
            OpenSettings(true);
        }
        else if (openSettings)
            OpenSettings(false);                         // mở bằng --settings (vd. ngay sau khi build): hiện hộp Cài đặt
        else
            Balloon("SngTrans đang chạy", "Bôi đen chữ ở bất kỳ đâu để dịch. Bấm vào biểu tượng khay để cài đặt.", 4000);
        _trim.Start();

        // ----- mở app lần nữa (bấm đúp exe/shortcut) khi đang chạy ngầm -> hiện hộp Cài đặt
        var showEv = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (showEv.WaitOne()) UI(() => OpenSettings(false));
        }) { IsBackground = true, Name = "SngTrans.ShowSettings" }.Start();

        // ----- tự kiểm tra bản mới (sau 20 giây khi mở máy, rồi 6 giờ một lần)
        _upd.Tick += (_, _) => CheckUpdate(false);
        _upd.Start();
        _ = Task.Delay(20_000).ContinueWith(_ => UI(() => CheckUpdate(false)));
    }

    static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path != null)
            {
                var ico = Icon.ExtractAssociatedIcon(path);
                if (ico != null) return ico;
            }
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
        if (!_hotkeys.Register(HotkeyWindow.IdForceQuit, HotkeySpec.ForceQuit))
            Log.Info("không đăng ký được phím tắt tắt hẳn");
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
                Text = "Tự dịch khi bôi đen", Checked = _settings.Auto,
                Click = () => { _settings.Update(d => d.Auto = !d.Auto); _dialog?.SyncFromSettings(); },
            },
            new()
            {
                Text = "Chạy cùng Windows", Checked = Win32.IsAutostart(),
                Click = () => { SetBoot(!Win32.IsAutostart()); _dialog?.SyncFromSettings(); },
            },
            new() { Text = "Cài đặt & ghi chú…", Click = () => OpenSettings(false) },
            new() { Text = $"Kiểm tra cập nhật (v{Updater.Current.ToString(3)})", Click = () => CheckUpdate(true) },
            new() { Separator = true },
            new() { Text = "Kết thúc tác vụ", Danger = true, Click = ForceQuit },
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

    void SetBoot(bool on)
    {
        bool ok = Win32.SetAutostart(on);
        Log.Info($"chạy cùng Windows: {(on ? "bật" : "tắt")} -> {(ok ? "ok" : "lỗi")}");
        if (ok)
            Balloon("SngTrans", on ? "SngTrans sẽ tự chạy ngầm mỗi khi bật máy." : "Đã tắt chạy cùng Windows.", 2500);
        else
            Balloon("SngTrans", "Không đổi được cài đặt khởi động cùng Windows.", 4000, ToolTipIcon.Warning);
    }

    void OpenSettings(bool welcome)
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
        _dialog.Activate();
        Win32.ForceForeground(_dialog.Handle);
    }

    void OnSettingsChanged()
    {
        if (_settings.Hotkey != _hotkeyActive)        // chỉ nạp lại khi đổi phím tắt
        {
            _hotkeyActive = _settings.Hotkey;
            RegisterHotkeys(announce: true);
        }
    }

    // ---------------------------------------------------------------- dịch
    void OnSelected(string text, string exe)
    {
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
                UI(() => { if (rid == _current && _card.Visible) _card.ShowError($"Không dịch được ({e.Message})"); });
            }
        });
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
            if (manual) Balloon("SngTrans", "Đang kiểm tra cập nhật…", 2000);
            var info = await Task.Run(() => Updater.CheckAsync());
            if (info == null)
            {
                if (manual) Balloon("SngTrans", $"Bạn đang dùng bản mới nhất (v{Updater.Current.ToString(3)}).", 3000);
                return;
            }
            if (!manual && info.Tag == _notifiedTag) return;          // báo tự động chỉ một lần cho mỗi phiên bản
            _notifiedTag = info.Tag;

            var notes = info.Notes.Trim();
            if (notes.Length > 600) notes = notes[..600] + "…";
            var now = new TaskDialogButton(info.AssetUrl != null ? "Cập nhật ngay" : "Mở trang tải");
            var page = new TaskDialogPage
            {
                Caption = "SngTrans",
                Heading = $"Có bản mới: v{info.Version.ToString(3)}",
                Text = $"Bạn đang dùng v{Updater.Current.ToString(3)}." + (notes.Length > 0 ? "\n\n" + notes : ""),
                Icon = TaskDialogIcon.Information,
            };
            page.Buttons.Add(now);
            page.Buttons.Add(new TaskDialogButton("Để sau"));
            if (TaskDialog.ShowDialog(page) != now) return;

            if (info.AssetUrl != null)
            {
                Balloon("SngTrans", "Đang tải bản mới, vui lòng chờ…", 4000);
                if (await Task.Run(() => Updater.ApplyAsync(info)))
                {
                    try { _watcher.Stop(); _tray.Visible = false; } catch { }
                    Environment.Exit(0);                              // bản mới đã được mở
                    return;
                }
                Balloon("SngTrans", "Không tự cập nhật được, đã mở trang tải.", 4000, ToolTipIcon.Warning);
            }
            try { Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true })?.Dispose(); } catch { }
        }
        catch (Exception e)
        {
            Log.Info("kiểm tra cập nhật lỗi: " + e.Message);
            if (manual) Balloon("SngTrans", "Không kiểm tra được cập nhật. Kiểm tra lại kết nối mạng.", 4000, ToolTipIcon.Warning);
        }
        finally { _checking = false; }
    }

    // ---------------------------------------------------------------- thoát
    void Quit()
    {
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
        try
        {
            _watcher.Stop();
            _tray.Visible = false;
        }
        catch { }
        Environment.Exit(0);
    }
}
