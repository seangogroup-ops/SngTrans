namespace SngTrans.Ui;

/// <summary>Cửa sổ ẩn (message-only) nhận phím tắt toàn cục qua RegisterHotKey.</summary>
public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    public const int IdTranslate = 1, IdForceQuit = 2;
    public event Action<int>? Pressed;
    readonly HashSet<int> _registered = new();

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Parent = (IntPtr)(-3) });      // HWND_MESSAGE
    }

    /// <summary>Đăng ký phím tắt; trả về false nếu chuỗi sai hoặc phím đang bị app khác chiếm.</summary>
    public bool Register(int id, string spec)
    {
        Unregister(id);
        if (!HotkeySpec.TryParse(spec, out var mods, out var vk)) return false;
        if (!Win32.RegisterHotKey(Handle, id, mods | HotkeySpec.MOD_NOREPEAT, vk)) return false;
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) Win32.UnregisterHotKey(Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.ToList()) Unregister(id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY) Pressed?.Invoke((int)m.WParam);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
