using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SngTrans;

public sealed record ForegroundInfo(IntPtr Hwnd, string Class, string Exe, uint Pid);

/// <summary>Các lời gọi Windows (P/Invoke): hook, clipboard, gửi Ctrl+C, cửa sổ đang dùng, khởi động cùng Windows.</summary>
public static class Win32
{
    // ---------------------------------------------------------------- hằng số
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
    public const int WM_HOTKEY = 0x312, WM_QUIT = 0x12;
    public const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002, PM_NOREMOVE = 0;
    public const int IDC_IBEAM = 32513;
    public const int VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_C = 0x43, VK_ESCAPE = 0x1B;
    const uint KEYEVENTF_KEYUP = 0x0002;
    // Chỉ sao lưu các định dạng clipboard nằm trong bộ nhớ HGLOBAL (an toàn để chép lại)
    static readonly HashSet<uint> SafeStdFormats = new() { 1, 7, 8, 13, 15, 16, 17 };
    const long MaxBackup = 24 * 1024 * 1024;

    // ---------------------------------------------------------------- kiểu dữ liệu
    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; public uint lPrivate; }

    [StructLayout(LayoutKind.Sequential)]
    struct CURSORINFO { public uint cbSize, flags; public IntPtr hCursor; public POINT ptScreenPos; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;     // có mặt để union đủ lớn (INPUT = 40 byte trên x64)
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public INPUTUNION u; }

    // ---------------------------------------------------------------- P/Invoke
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookExW(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PeekMessageW(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostThreadMessageW(uint threadId, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, StringBuilder sb, ref uint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetCursorInfo(ref CURSORINFO ci);
    [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr hInst, IntPtr name);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint fmt, IntPtr h);
    [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint fmt);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string? subApp, string? subIdList);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int mciSendStringW(string cmd, StringBuilder? ret, int retLen, IntPtr hwnd);

    // ---------------------------------------------------------------- cửa sổ / chuột
    public static ForegroundInfo? Foreground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        var cls = new StringBuilder(256);
        GetClassNameW(hwnd, cls, 256);
        GetWindowThreadProcessId(hwnd, out var pid);
        return new ForegroundInfo(hwnd, cls.ToString(), ProcessName(pid), pid);
    }

    /// <summary>Tên file exe (chữ thường) của tiến trình, hoặc "" nếu không đọc được.</summary>
    public static string ProcessName(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid);          // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            uint size = 1024;
            return QueryFullProcessImageNameW(h, 0, sb, ref size)
                ? Path.GetFileName(sb.ToString()).ToLowerInvariant()
                : "";
        }
        finally { CloseHandle(h); }
    }

    public static bool CursorIsIBeam()
    {
        var ci = new CURSORINFO { cbSize = (uint)Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci)) return true;
        var ibeam = LoadCursorW(IntPtr.Zero, (IntPtr)IDC_IBEAM);
        return ci.hCursor != IntPtr.Zero && ci.hCursor == ibeam;
    }

    /// <summary>Có nút chuột nào (trái/phải/giữa) đang được nhấn không.</summary>
    public static bool AnyMouseButtonDown() =>
        (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 || (GetAsyncKeyState(0x04) & 0x8000) != 0;

    public static bool ModifiersDown()
    {
        foreach (var vk in new[] { VK_CONTROL, VK_SHIFT, VK_MENU, VK_LWIN, VK_RWIN })
            if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        return false;
    }

    public static void WaitModifiersReleased(int timeoutMs = 1000)
    {
        var sw = Stopwatch.StartNew();
        while (ModifiersDown() && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(20);
    }

    /// <summary>(left, top, right, bottom) theo pixel vật lý, hoặc null.</summary>
    public static (int L, int T, int R, int B)? WindowRect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        return GetWindowRect(hwnd, out var r) ? (r.Left, r.Top, r.Right, r.Bottom) : null;
    }

    /// <summary>Đưa cửa sổ lên trước kể cả khi app đang chạy nền (tránh bị Windows chặn giành focus).</summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        try
        {
            var fg = GetForegroundWindow();
            uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            SetForegroundWindow(hwnd);
            if (attached) AttachThreadInput(me, fgThread, false);
        }
        catch { }
    }

    public static void TopMostNoActivate(IntPtr hwnd) =>
        SetWindowPos(hwnd, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // NOSIZE | NOMOVE | NOACTIVATE

    /// <summary>DPI của màn hình chứa điểm (x, y).</summary>
    public static int DpiAt(int x, int y)
    {
        try
        {
            var mon = MonitorFromPoint(new POINT { X = x, Y = y }, 2);       // MONITOR_DEFAULTTONEAREST
            if (GetDpiForMonitor(mon, 0, out var dx, out _) == 0 && dx > 0) return (int)dx;
        }
        catch { }
        return 96;
    }

    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>DPI thật của cửa sổ (theo màn hình nó đang nằm), 96 nếu không đọc được.</summary>
    public static int DpiOfWindow(IntPtr hwnd)
    {
        try
        {
            var d = GetDpiForWindow(hwnd);
            if (d > 0) return (int)d;
        }
        catch { }
        return 96;
    }

    public static bool IsWin11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>Thanh tiêu đề tối (Win10 1809+) và đổi màu theo nền (Win11). Lỗi thì bỏ qua.</summary>
    public static void SetDarkTitlebar(IntPtr hwnd, int colorRef = 0x22120B)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int one = 1;
            foreach (var attr in new[] { 20, 19 })          // DWMWA_USE_IMMERSIVE_DARK_MODE
                if (DwmSetWindowAttribute(hwnd, attr, ref one, 4) == 0) break;
            int col = colorRef;                              // DWMWA_CAPTION_COLOR (COLORREF 0x00BBGGRR)
            DwmSetWindowAttribute(hwnd, 35, ref col, 4);
        }
        catch { }
    }

    /// <summary>Thanh tiêu đề sáng, cùng màu nền cửa sổ (Win11). Lỗi thì bỏ qua.</summary>
    public static void SetLightTitlebar(IntPtr hwnd, int captionRef = 0xF3F3F3, int textRef = 0x1B1B1B)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int zero = 0;
            foreach (var attr in new[] { 20, 19 })          // DWMWA_USE_IMMERSIVE_DARK_MODE = tắt
                if (DwmSetWindowAttribute(hwnd, attr, ref zero, 4) == 0) break;
            int col = captionRef;                            // DWMWA_CAPTION_COLOR (COLORREF 0x00BBGGRR)
            DwmSetWindowAttribute(hwnd, 35, ref col, 4);
            int txt = textRef;                               // DWMWA_TEXT_COLOR
            DwmSetWindowAttribute(hwnd, 36, ref txt, 4);
        }
        catch { }
    }

    /// <summary>Thanh cuộn sáng (kiểu Explorer) cho điều khiển.</summary>
    public static void LightScrollbars(IntPtr hwnd)
    {
        try { SetWindowTheme(hwnd, "Explorer", null); } catch { }
    }

    /// <summary>Win11: bo góc hệ thống và viền màu cho cửa sổ không khung.</summary>
    public static void SetRoundedBorder(IntPtr hwnd, int borderColorRef)
    {
        if (hwnd == IntPtr.Zero || !IsWin11) return;
        try
        {
            int round = 2;                                   // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref round, 4);   // DWMWA_WINDOW_CORNER_PREFERENCE
            int col = borderColorRef;
            DwmSetWindowAttribute(hwnd, 34, ref col, 4);     // DWMWA_BORDER_COLOR
        }
        catch { }
    }

    /// <summary>Thanh cuộn tối cho điều khiển (Win10 1809+).</summary>
    public static void DarkScrollbars(IntPtr hwnd)
    {
        try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
    }

    // ---------------------------------------------------------------- clipboard
    static bool OpenClip(int retries = 12)
    {
        for (int i = 0; i < retries; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    public static string? GetClipboardText()
    {
        if (!OpenClip()) return null;
        try
        {
            var h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            var p = GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    /// <summary>Sao lưu clipboard (text, ảnh DIB, danh sách file, HTML, RTF...). null nếu thất bại.</summary>
    public static List<(uint Fmt, byte[] Data)>? ClipboardSnapshot()
    {
        if (!OpenClip()) return null;
        try
        {
            var items = new List<(uint, byte[])>();
            long total = 0;
            uint fmt = 0;
            while ((fmt = EnumClipboardFormats(fmt)) != 0)
            {
                if (!(SafeStdFormats.Contains(fmt) || fmt >= 0xC000)) continue;
                var h = GetClipboardData(fmt);
                if (h == IntPtr.Zero) continue;
                long size = (long)(ulong)GlobalSize(h);
                if (size <= 0 || size > MaxBackup) continue;
                var p = GlobalLock(h);
                if (p == IntPtr.Zero) continue;
                byte[] data;
                try
                {
                    data = new byte[size];
                    Marshal.Copy(p, data, 0, (int)size);
                }
                finally { GlobalUnlock(h); }
                total += size;
                if (total > MaxBackup) return null;
                items.Add((fmt, data));
            }
            return items;
        }
        finally { CloseClipboard(); }
    }

    public static bool ClipboardRestore(List<(uint Fmt, byte[] Data)>? items)
    {
        if (items == null || !OpenClip()) return false;
        try
        {
            EmptyClipboard();
            foreach (var (fmt, data) in items)
            {
                var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
                if (h == IntPtr.Zero) continue;
                var p = GlobalLock(h);
                if (p == IntPtr.Zero) { GlobalFree(h); continue; }
                Marshal.Copy(data, 0, p, data.Length);
                GlobalUnlock(h);
                if (SetClipboardData(fmt, h) == IntPtr.Zero) GlobalFree(h);
            }
            return true;
        }
        finally { CloseClipboard(); }
    }

    static INPUT Key(int vk, bool up) => new()
    {
        type = 1,                                            // INPUT_KEYBOARD
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    public static void SendCtrlC()
    {
        var seq = new List<INPUT>(8);
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0)
        {
            // Alt vẫn đang giữ (hay gặp khi bấm dồn Alt+phím): nếu cứ gửi Ctrl+C thì app nhận Ctrl+Alt+C (không chép được),
            // còn khi Alt nhả ra mà không có phím nào xen giữa, Windows bật thanh menu của app. Chèn một phím trống (0xE8)
            // để Windows coi là "đã dùng Alt cho phím khác", rồi nhả Alt giả trước khi gửi Ctrl+C.
            seq.Add(Key(0xE8, false)); seq.Add(Key(0xE8, true));
            seq.Add(Key(VK_MENU, true));
        }
        seq.Add(Key(VK_CONTROL, false)); seq.Add(Key(VK_C, false));
        seq.Add(Key(VK_C, true)); seq.Add(Key(VK_CONTROL, true));
        var arr = seq.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Gửi Ctrl+C tới app đang dùng, đọc chữ, rồi trả clipboard về như cũ. "" nếu không có gì.</summary>
    public static string CopySelection(int timeoutMs = 500)
    {
        var snap = ClipboardSnapshot();
        var seq0 = GetClipboardSequenceNumber();
        SendCtrlC();
        var text = "";
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(10);
            if (GetClipboardSequenceNumber() != seq0)
            {
                Thread.Sleep(30);                            // chờ app ghi xong
                text = GetClipboardText() ?? "";
                if (snap != null) ClipboardRestore(snap);
                break;
            }
        }
        return text;
    }

    // ---------------------------------------------------------------- bộ nhớ / console / âm thanh
    /// <summary>Trả RAM nhàn rỗi về cho Windows (thu gọn Working Set). Vô hại: trang nào cần sẽ tự nạp lại.</summary>
    public static void TrimMemory()
    {
        try
        {
            var h = GetCurrentProcess();
            if (!EmptyWorkingSet(h)) SetProcessWorkingSetSize(h, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }

    /// <summary>Cho app WinExe in nhật ký ra cửa sổ cmd đã mở nó (dùng với --debug).</summary>
    public static void AttachParentConsole()
    {
        try
        {
            if (AttachConsole(-1))                           // ATTACH_PARENT_PROCESS
            {
                var w = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                Console.SetOut(w);
            }
        }
        catch { }
    }

    public static int Mci(string cmd, StringBuilder? ret = null) => mciSendStringW(cmd, ret, ret?.Capacity ?? 0, IntPtr.Zero);

    // ---------------------------------------------------------------- chạy cùng Windows
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string AppName = "SngTrans";
    const string OldName = "MiniTranslate";

    static string AutostartCommand() => $"\"{Environment.ProcessPath}\"";

    /// <summary>Người dùng có thể tắt mục khởi động trong Task Manager/Cài đặt: Windows ghi byte lẻ (vd 0x03).</summary>
    static bool DisabledInTaskManager()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            if (k?.GetValue(AppName) is byte[] data && data.Length > 0) return (data[0] & 1) == 1;
        }
        catch { }
        return false;
    }

    /// <summary>True khi app thật sự sẽ chạy cùng Windows (có mục khởi động và chưa bị tắt ở Task Manager).</summary>
    public static bool IsAutostart()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            if (k?.GetValue(AppName) == null) return false;
        }
        catch { return false; }
        return !DisabledInTaskManager();
    }

    public static bool SetAutostart(bool enabled)
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                k.DeleteValue(OldName, false);               // bỏ mục khởi động của tên cũ
                if (enabled) k.SetValue(AppName, AutostartCommand(), RegistryValueKind.String);
                else k.DeleteValue(AppName, false);
            }
            try
            {
                using var a = Registry.CurrentUser.OpenSubKey(ApprovedKey, true);
                a?.DeleteValue(AppName, false);              // xoá cờ "đã tắt" cũ để lần bật lại có hiệu lực
            }
            catch { }
            return true;
        }
        catch { return false; }
    }
}
