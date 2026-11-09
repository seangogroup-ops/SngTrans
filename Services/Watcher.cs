using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SngTrans;

/// <summary>Trạng thái thẻ dịch mà Watcher cần biết (đọc từ luồng hook nên chỉ gồm giá trị đơn giản).</summary>
public interface ICardState
{
    bool IsOpen { get; }
    bool PopupOpen { get; }
    bool Pinned { get; }
    IntPtr Hwnd { get; }
}

/// <summary>Theo dõi chuột/bàn phím toàn hệ thống trên một luồng riêng (để không làm giật chuột khi
/// giao diện bận): thấy bôi đen (kéo chuột, nhấp đúp/ba) thì lấy chữ đang chọn và báo ra ngoài.
/// Các sự kiện được gọi trên luồng hook: nơi nhận phải tự chuyển về luồng giao diện.</summary>
public sealed class Watcher : IDisposable
{
    // Cửa sổ dòng lệnh: Ctrl+C ở đây là ngắt chương trình, không được gửi
    static readonly HashSet<string> BlockClasses = new()
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "mintty", "VirtualConsoleClass", "PuTTY",
    };
    const int MaxChars = 1500;

    readonly Settings _settings;
    readonly ICardState _card;
    readonly ClickDetector _det = new();

    public event Action<string, string>? TextSelected;   // (chữ đã chọn, tên file exe của app)
    public event Action? OutsideClick;
    public event Action? Escape;

    /// <summary>Bật khi đang đổi phím tắt trong cài đặt.</summary>
    public volatile bool HotkeyPaused;

    Thread? _thread;
    uint _threadId;
    readonly ManualResetEventSlim _ready = new(false);
    IntPtr _mouseHook, _kbdHook;
    Win32.HookProc? _mouseProc, _kbdProc;       // giữ tham chiếu để GC không thu hồi delegate
    bool _pressInCard;
    int _busy;
    CancellationTokenSource? _pending;

    public Watcher(Settings settings, ICardState card)
    {
        _settings = settings;
        _card = card;
    }

    // ---------------------------------------------------------------- vòng đời
    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(HookMain) { IsBackground = true, Name = "SngTrans.Hooks" };
        _thread.Start();
        _ready.Wait(3000);
        Log.Info($"đã bật theo dõi chuột/phím (phím tắt {_settings.Hotkey})");
    }

    void HookMain()
    {
        _threadId = Win32.GetCurrentThreadId();
        Win32.PeekMessageW(out _, IntPtr.Zero, 0, 0, Win32.PM_NOREMOVE);     // tạo hàng đợi tin nhắn cho luồng
        _mouseProc = MouseProc;
        _kbdProc = KeyProc;
        var mod = Win32.GetModuleHandleW(null);
        _mouseHook = Win32.SetWindowsHookExW(Win32.WH_MOUSE_LL, _mouseProc, mod, 0);
        if (_mouseHook == IntPtr.Zero) Log.Error($"không cài được hook chuột (mã {Marshal.GetLastWin32Error()})");
        _kbdHook = Win32.SetWindowsHookExW(Win32.WH_KEYBOARD_LL, _kbdProc, mod, 0);
        if (_kbdHook == IntPtr.Zero) Log.Error($"không cài được hook bàn phím (mã {Marshal.GetLastWin32Error()})");
        _ready.Set();

        while (Win32.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }
        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        if (_kbdHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_kbdHook);
        _mouseHook = _kbdHook = IntPtr.Zero;
    }

    public void Stop()
    {
        var t = _thread;
        if (t == null) return;
        _thread = null;
        Win32.PostThreadMessageW(_threadId, (uint)Win32.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        t.Join(1000);
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- hook
    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_LBUTTONUP)
            {
                try
                {
                    var s = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                    OnLeft(s.pt.X, s.pt.Y, msg == Win32.WM_LBUTTONDOWN);
                }
                catch (Exception e) { Log.Error("lỗi trong hook chuột", e); }
            }
        }
        return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN)
            {
                try
                {
                    var s = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                    if (s.vkCode == Win32.VK_ESCAPE) Escape?.Invoke();
                }
                catch (Exception e) { Log.Error("lỗi trong hook bàn phím", e); }
            }
        }
        return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // ---------------------------------------------------------------- chuột
    bool InCard(int x, int y)
    {
        if (!_card.IsOpen) return false;
        if (_card.PopupOpen) return true;            // đang mở danh sách ngôn ngữ: coi như bấm trong thẻ
        var r = Win32.WindowRect(_card.Hwnd);
        return r is { } rc && rc.L <= x && x <= rc.R && rc.T <= y && y <= rc.B;
    }

    void OnLeft(int x, int y, bool pressed)
    {
        double t = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (pressed)
        {
            _pressInCard = InCard(x, y);
            if (!_pressInCard && _card.IsOpen && !_card.Pinned) OutsideClick?.Invoke();
            _det.Press(x, y, t);
            return;
        }
        var kind = _det.Release(x, y, t);
        if (kind != null) Log.Info($"phát hiện bôi đen ({kind}) tại {x},{y} tự_dịch={_settings.Auto}");
        if (kind == null || _pressInCard || InCard(x, y) || !_settings.Auto) return;
        Translator.Prewarm();                        // bắt tay TLS song song với lúc lấy chữ
        ScheduleCapture("auto", 60);
    }

    void ScheduleCapture(string mode, int delayMs)
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _pending, cts)?.Cancel();
        _ = Task.Delay(delayMs, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Capture(mode);
        }, TaskScheduler.Default);
    }

    // ---------------------------------------------------------------- lấy chữ
    void Capture(string mode)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        try
        {
            var fg = Win32.Foreground();
            if (fg == null || fg.Pid == (uint)Environment.ProcessId)
            {
                Log.Info($"bỏ qua: không có cửa sổ hoặc là chính app ({fg?.Exe})");
                return;
            }
            if (BlockClasses.Contains(fg.Class) || _settings.BlockedApps.Contains(fg.Exe))
            {
                Log.Info($"bỏ qua: app bị chặn {fg.Exe} / {fg.Class}");
                return;
            }
            if (mode == "auto")
            {
                if (_settings.IbeamOnly && !Win32.CursorIsIBeam())
                {
                    Log.Info($"bỏ qua: con trỏ không phải chữ I ({fg.Exe})");
                    return;
                }
                if (Win32.ModifiersDown())
                {
                    Log.Info("bỏ qua: đang giữ Ctrl/Shift/Alt");
                    return;
                }
            }
            else
                Win32.WaitModifiersReleased(1000);

            var text = (Win32.CopySelection() ?? "").Trim();
            Log.Info($"lấy chữ từ {fg.Exe}: {text.Length} ký tự");
            if (text.Length > 0)
                TextSelected?.Invoke(text.Length > MaxChars ? text[..MaxChars] : text, fg.Exe);
        }
        catch (Exception e) { Log.Error("lỗi khi lấy chữ", e); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// <summary>Gọi khi người dùng bấm phím tắt dịch ngay (từ luồng giao diện).</summary>
    public void TriggerHotkey()
    {
        if (HotkeyPaused) return;
        Log.Info("bấm phím tắt");
        _ = Task.Run(() => Capture("hotkey"));
    }
}
