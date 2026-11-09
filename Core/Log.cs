using System.Text;

namespace SngTrans;

/// <summary>Nhật ký: ghi vào %APPDATA%\SngTrans\sngtrans.log; chạy với --debug thì in cả ra cửa sổ lệnh.</summary>
public static class Log
{
    static readonly object Gate = new();
    static string? _path;
    public static bool Debug;

    public static string LogPath => _path ??= Path.Combine(Settings.DataDir(), "sngtrans.log");

    public static void Info(string msg) => Write(msg);

    public static void Error(string msg, Exception? ex = null) =>
        Write(ex == null ? $"LỖI: {msg}" : $"LỖI: {msg}\n{ex}");

    static void Write(string line)
    {
        var s = $"{DateTime.Now:HH:mm:ss} {line}";
        lock (Gate)
        {
            try
            {
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 200_000)
                    File.Move(LogPath, LogPath + ".1", true);
                File.AppendAllText(LogPath, s + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { /* không để lỗi ghi nhật ký làm sập app */ }
            if (Debug)
            {
                try { Console.WriteLine(s); } catch { }
            }
        }
    }
}
