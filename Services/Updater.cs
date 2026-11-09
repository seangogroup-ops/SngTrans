using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace SngTrans;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, string? AssetUrl);

/// <summary>Kiểm tra và cài bản mới từ GitHub Releases (không cần sửa code mỗi lần cập nhật).</summary>
public static class Updater
{
    /// <summary>Kho GitHub dạng "chủ/tên-kho". Để trống nếu muốn đặt trong file %APPDATA%\SngTrans\update-repo.txt.</summary>
    public const string DefaultRepo = "seangogroup-ops/SngTrans";

    static readonly HttpClient Http = Create(TimeSpan.FromSeconds(10));
    static readonly HttpClient Big = Create(TimeSpan.FromMinutes(5));

    static HttpClient Create(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SngTrans-updater");
        return c;
    }

    /// <summary>Đọc kho cập nhật: file update-repo.txt (ưu tiên) hoặc DefaultRepo. Chấp nhận cả link GitHub đầy đủ.</summary>
    public static string? Repo()
    {
        string raw = DefaultRepo;
        try
        {
            var f = Path.Combine(Settings.DataDir(), "update-repo.txt");
            if (File.Exists(f))
            {
                var t = File.ReadAllText(f).Trim();
                if (t.Length > 0) raw = t;
            }
        }
        catch { }
        raw = raw.Trim();
        foreach (var pre in new[] { "https://github.com/", "http://github.com/", "github.com/" })
            if (raw.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) { raw = raw[pre.Length..]; break; }
        raw = raw.TrimEnd('/');
        if (raw.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) raw = raw[..^4];
        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[0] + "/" + parts[1] : null;
    }

    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public static Version Current =>
        Norm(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0));

    static bool IsDotnetHost(string? exe) =>
        string.IsNullOrEmpty(exe) ||
        string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Trả về thông tin bản mới nếu có, null nếu đang dùng bản mới nhất. Ném lỗi khi không có mạng.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        var repo = Repo() ?? throw new InvalidOperationException("chưa cấu hình kho cập nhật");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Http.SendAsync(req).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tg) ? tg.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
        ver = Norm(ver);
        if (ver <= Current) return null;

        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : $"https://github.com/{repo}/releases";

        // bản nhẹ hay bản đầy đủ: đoán theo dung lượng exe đang chạy
        string want = "nhe";
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && new FileInfo(exe).Length > 30_000_000) want = "day-du";
        }
        catch { }

        string? asset = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                bool isLight = name.Contains("nhe", StringComparison.OrdinalIgnoreCase);
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    (want == "nhe" ? isLight : !isLight) &&
                    a.TryGetProperty("browser_download_url", out var u))
                {
                    asset = u.GetString();
                    break;
                }
            }
        }
        return new UpdateInfo(ver, tag, notes, page, asset);
    }

    /// <summary>Tải bản mới, thay exe đang chạy (Windows cho đổi tên exe đang chạy) rồi mở bản mới. true = đã mở bản mới, cần thoát bản này.</summary>
    public static async Task<bool> ApplyAsync(UpdateInfo info)
    {
        var exe = Environment.ProcessPath;
        if (info.AssetUrl == null || IsDotnetHost(exe)) return false;
        var tmp = exe + ".new";
        var old = exe + ".old";
        try
        {
            using (var resp = await Big.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmp);
                await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
            }
            if (new FileInfo(tmp).Length < 1_000_000) throw new IOException("file tải về quá nhỏ, có thể bị lỗi");

            try { File.Delete(old); } catch { }
            File.Move(exe!, old);
            try { File.Move(tmp, exe!); }
            catch { File.Move(old, exe!); throw; }          // lỗi thì trả lại bản cũ

            Process.Start(new ProcessStartInfo(exe!, "--after-update")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe!) ?? "",
            })?.Dispose();
            Log.Info($"đã cập nhật lên {info.Tag}, mở bản mới");
            return true;
        }
        catch (Exception e)
        {
            Log.Error("cập nhật lỗi", e);
            try { File.Delete(tmp); } catch { }
            return false;
        }
    }

    /// <summary>Xoá file .old còn sót sau lần cập nhật trước.</summary>
    public static void CleanupOld()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (IsDotnetHost(exe)) return;
            File.Delete(exe + ".old");
            File.Delete(exe + ".new");
        }
        catch { }
    }
}
