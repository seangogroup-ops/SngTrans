using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace SngTrans;

/// <summary>Đọc to bằng giọng Google: tải mp3 về thư mục tạm rồi phát bằng MCI của Windows (không cần thư viện ngoài).</summary>
public sealed class Tts : IDisposable
{
    const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
    const string Alias = "sngtts";

    static readonly HttpClient Http = CreateClient();
    readonly object _play = new();
    int _token;

    /// <summary>Dự phòng khi Google không phản hồi: tạo file wav bằng giọng có sẵn của Windows (lang -> đường dẫn hoặc null).</summary>
    public Func<string, string, Task<string?>>? Local { get; set; }
    /// <summary>Báo lý do không đọc được để hiện cho người dùng.</summary>
    public event Action<string>? Failed;

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
        return c;
    }

    public static List<string> Chunks(string text, int limit = 180)
    {
        var parts = Regex.Matches(text, @"[^.!?。！？\n]+[.!?。！？]?").Select(m => m.Value).ToList();
        if (parts.Count == 0) parts.Add(text);
        var result = new List<string>();
        var cur = "";
        foreach (var p in parts)
        {
            if ((cur + p).Length > limit)
            {
                if (cur.Trim().Length > 0) result.Add(cur.Trim());
                cur = p;
                while (cur.Length > limit)
                {
                    int cut = cur.LastIndexOfAny(new[] { ',', ';', ':', '，', '、', ' ' }, limit - 1, limit);
                    if (cut < limit / 3) cut = limit - 1;           // không có chỗ ngắt hợp lý thì đành cắt cứng
                    result.Add(cur[..(cut + 1)].Trim());
                    cur = cur[(cut + 1)..];
                }
            }
            else cur += p;
        }
        if (cur.Trim().Length > 0) result.Add(cur.Trim());
        return result;
    }

    /// <summary>"auto"/rỗng không phải mã giọng hợp lệ: đoán theo bảng chữ; mã lạ thì lấy phần gốc.</summary>
    public static string NormalizeLang(string? lang, string text)
    {
        var l = (lang ?? "").Trim();
        if (l.Length == 0 || l.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var ch in text)
            {
                if (ch >= 0x3040 && ch <= 0x30FF) return "ja";
                if (ch >= 0xAC00 && ch <= 0xD7AF) return "ko";
                if (ch >= 0x4E00 && ch <= 0x9FFF) return "zh-CN";
                if (ch >= 0x0E00 && ch <= 0x0E7F) return "th";
                if (ch >= 0x0400 && ch <= 0x04FF) return "ru";
                if (ch >= 0x0600 && ch <= 0x06FF) return "ar";
                if (ch >= 0x0900 && ch <= 0x097F) return "hi";
            }
            // chữ Latinh có dấu đặc trưng tiếng Việt
            if (Regex.IsMatch(text, "[ăâđêôơưĂÂĐÊÔƠƯạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỵỷỹ]")) return "vi";
            return "en";
        }
        if (l.Equals("zh-cn", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (l.Equals("zh-tw", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
        if (l.Equals("iw", StringComparison.OrdinalIgnoreCase)) return "he";
        if (l.Equals("jw", StringComparison.OrdinalIgnoreCase)) return "jv";
        return l.Contains('-') && !l.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? l.Split('-')[0].ToLowerInvariant() : l;
    }

    public void Speak(string? text, string lang)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;
        lang = NormalizeLang(lang, text);
        int token = Interlocked.Increment(ref _token);      // đọc câu mới thì câu cũ tự dừng
        _ = Task.Run(() => Work(text, lang, token));
    }

    async Task Work(string text, string lang, int token)
    {
        var files = new List<string>();
        try
        {
            string? why = null;
            int i = 0;
            foreach (var c in Chunks(text).Take(12))
            {
                if (token != _token) return;
                var (data, err) = await Download(c, lang).ConfigureAwait(false);
                why = err;
                if (data == null) break;
                var p = Path.Combine(Path.GetTempPath(), $"sngtrans_tts_{Environment.ProcessId}_{token}_{i++}.mp3");
                await File.WriteAllBytesAsync(p, data).ConfigureAwait(false);
                files.Add(p);
            }
            if (token != _token) return;

            if (files.Count == 0 || why != null)
            {
                // Google lỗi/bị chặn: chuyển sang giọng của Windows
                Log.Info($"giọng Google lỗi ({why ?? "không có dữ liệu"}), thử giọng Windows");
                foreach (var f in files) { try { File.Delete(f); } catch { } }
                files.Clear();
                string? wav = null;
                if (Local != null)
                {
                    try { wav = await Local(text.Length > 800 ? text[..800] : text, lang).ConfigureAwait(false); }
                    catch (Exception e) { Log.Error("giọng Windows lỗi", e); }
                }
                if (wav == null)
                {
                    Failed?.Invoke("Không đọc to được: không kết nối được giọng Google và máy chưa có giọng Windows cho ngôn ngữ này "
                                   + "(Cài đặt Windows > Thời gian & ngôn ngữ > Giọng nói).");
                    return;
                }
                files.Add(wav);
            }
            if (token != _token || files.Count == 0) return;
            bool ok = PlayAll(files, token);
            if (!ok && token == _token && !files[0].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                // Có mp3 nhưng máy không phát được: thử giọng Windows (file wav)
                Log.Info("không phát được mp3 của Google, thử giọng Windows");
                string? wav = null;
                if (Local != null)
                {
                    try { wav = await Local(text.Length > 800 ? text[..800] : text, lang).ConfigureAwait(false); }
                    catch (Exception e) { Log.Error("giọng Windows lỗi", e); }
                }
                if (wav != null)
                {
                    files.Add(wav);
                    ok = PlayAll(new List<string> { wav }, token);
                }
            }
            if (!ok && token == _token)
                Failed?.Invoke("Không phát được âm thanh. Kiểm tra loa/thiết bị phát mặc định của Windows, "
                               + "hoặc gửi file nhật ký %APPDATA%\\SngTrans\\sngtrans.log để xem nguyên nhân.");
        }
        catch (Exception e) { Log.Error("đọc to lỗi", e); }
        finally
        {
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { }
            }
        }
    }

    /// <summary>Tải mp3 một đoạn; thử lần lượt các client của Google. Trả null và ghi lý do vào why nếu hỏng.</summary>
    static async Task<(byte[]? Data, string? Why)> Download(string chunk, string lang)
    {
        string? why = null;
        foreach (var client in new[] { "tw-ob", "gtx" })
        {
            var url = "https://translate.google.com/translate_tts?ie=UTF-8&client=" + client
                      + "&tl=" + Uri.EscapeDataString(lang)
                      + "&total=1&idx=0&textlen=" + chunk.Length
                      + "&q=" + Uri.EscapeDataString(chunk);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Referrer = new Uri("https://translate.google.com/");
                using var resp = await Http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { why = $"HTTP {(int)resp.StatusCode} ({client})"; continue; }
                var data = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (data.Length > 200 && LooksLikeMp3(data)) return (data, null);
                why = $"phản hồi rỗng ({client})";
            }
            catch (Exception e) { why = $"{e.GetType().Name}: {e.Message} ({client})"; }
        }
        return (null, why);
    }

    static bool LooksLikeMp3(byte[] d) =>
        (d[0] == 0x49 && d[1] == 0x44 && d[2] == 0x33)                 // "ID3"
        || (d[0] == 0xFF && (d[1] & 0xE0) == 0xE0);                    // khung MPEG

    bool PlayAll(List<string> files, int token)
    {
        lock (_play)                                         // chờ câu cũ dừng hẳn rồi mới phát
        {
            bool any = false;
            foreach (var f in files)
            {
                if (token != _token) return true;           // bị câu mới thay thế: không phải lỗi
                if (PlayFile(f, token)) any = true;
                else if (!any) return false;
            }
            return any;
        }
    }

    /// <summary>Ưu tiên bộ phát của Windows (MediaPlayer), không được thì thử MCI.</summary>
    bool PlayFile(string path, int token)
    {
        if (PlayMedia(path, token)) return true;
        return PlayMci(path, token);
    }

    bool PlayMedia(string path, int token)
    {
        try
        {
            using var player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Media, Volume = 1.0 };
            using var done = new ManualResetEventSlim();
            string? fail = null;
            player.MediaEnded += (_, _) => done.Set();
            player.MediaFailed += (_, e) => { fail = e.ErrorMessage; done.Set(); };
            player.Source = MediaSource.CreateFromUri(new Uri(path));
            player.Play();
            var sw = Stopwatch.StartNew();
            while (token == _token && sw.Elapsed < TimeSpan.FromSeconds(90) && !done.Wait(60)) { }
            player.Pause();
            if (fail != null) { Log.Info($"MediaPlayer lỗi: {fail}"); return false; }
            return true;
        }
        catch (Exception e)
        {
            Log.Error("MediaPlayer không phát được", e);
            return false;
        }
    }

    bool PlayMci(string path, int token)
    {
        Win32.Mci($"close {Alias}");
        var type = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? "waveaudio" : "mpegvideo";
        int r = Win32.Mci($"open \"{path}\" type {type} alias {Alias}");
        if (r != 0) { Log.Info($"MCI không mở được file âm thanh (mã {r})"); return false; }
        try
        {
            r = Win32.Mci($"play {Alias} from 0");
            if (r != 0) { Log.Info($"MCI không phát được file âm thanh (mã {r})"); return false; }
            var sb = new StringBuilder(64);
            var sw = Stopwatch.StartNew();
            while (token == _token && sw.Elapsed < TimeSpan.FromSeconds(90))
            {
                Thread.Sleep(60);
                sb.Clear();
                if (Win32.Mci($"status {Alias} mode", sb) != 0) break;
                if (sb.ToString().StartsWith("stopped", StringComparison.OrdinalIgnoreCase) && sw.ElapsedMilliseconds > 300) break;
            }
            return true;
        }
        finally
        {
            Win32.Mci($"stop {Alias}");
            Win32.Mci($"close {Alias}");
        }
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _token);
    }
}
