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

    static StreamWriter? _w;               // giữ file mở sẵn: khỏi mở/đóng file mỗi dòng (log được gọi nhiều ở luồng hook chuột)
    static int _n;

    static void Write(string line)
    {
        var s = $"{DateTime.Now:HH:mm:ss} {line}";
        lock (Gate)
        {
            try
            {
                // Mỗi 100 dòng mới kiểm tra dung lượng; quá 200 KB thì xoay sang file .1
                if (_w != null && ++_n % 100 == 0 && new FileInfo(LogPath).Length > 200_000)
                {
                    _w.Dispose();
                    _w = null;
                    File.Move(LogPath, LogPath + ".1", true);
                }
                if (_w == null)
                {
                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 200_000) File.Move(LogPath, LogPath + ".1", true);
                    _w = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
                        new UTF8Encoding(false)) { AutoFlush = true };       // tự đẩy xuống đĩa từng dòng: thoát đột ngột vẫn không mất log
                }
                _w.WriteLine(s);
            }
            catch
            {
                /* không để lỗi ghi nhật ký làm sập app */
                try { _w?.Dispose(); } catch { }
                _w = null;                                                    // lần sau mở lại
            }
            if (Debug)
            {
                try { Console.WriteLine(s); } catch { }
            }
        }
    }
}
