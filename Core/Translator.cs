using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SngTrans;

public sealed class Meaning
{
    public string Pos { get; set; } = "";
    public List<string> Words { get; set; } = new();
}

public sealed class TranslationResult
{
    public string Translation { get; set; } = "";
    public string Detected { get; set; } = "auto";
    public List<Meaning> Meanings { get; set; } = new();
    public List<string> Synonyms { get; set; } = new();
    public List<string> Examples { get; set; } = new();
    /// <summary>Ngôn ngữ đích thực tế đã dịch sang.</summary>
    public string Tl { get; set; } = "";

    public TranslationResult Clone() => (TranslationResult)MemberwiseClone();
}

public sealed class RateLimitedException : Exception
{
    public double RetryAfter { get; }
    public RateLimitedException(double retryAfter = 0) : base("HTTP Error 429: Too Many Requests") => RetryAfter = retryAfter;
}

/// <summary>Gọi Google Translate (endpoint miễn phí, không cần key).</summary>
public static class Translator
{
    static readonly string[] Endpoints =
    {
        "https://translate.googleapis.com/translate_a/single",
        "https://translate.google.com/translate_a/single",
        "https://translate.google.com.vn/translate_a/single",
    };
    // Endpoint dự phòng (quota riêng, ít thông tin hơn: chỉ có bản dịch)
    const string LiteEndpoint = "https://clients5.google.com/translate_a/t";
    const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    const int HedgeMs = 600;        // chờ endpoint đầu; chưa xong thì gửi thêm sang endpoint kế
    const int Retries = 1;          // số lần thử lại khi gặp 429/5xx
    const double Cooldown = 20.0;   // giây tạm bỏ một endpoint sau khi nó trả 429
    const int CacheMax = 200;
    const double DnsTtl = 60.0;     // ngắn để bật/tắt VPN, proxy hay đổi DNS không bị kẹt IP cũ
    const int Long = 60;            // >= ngần này ký tự thì chỉ lấy bản dịch (bỏ nghĩa/ví dụ cho nhẹ)
    const int Chunk = 450;          // cắt đoạn dài thành các khúc và dịch song song

    static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    static readonly ConcurrentDictionary<string, double> CoolUntil = new();   // endpoint -> thời điểm dùng lại
    static readonly ConcurrentDictionary<string, double> Latency = new();     // host -> thời gian phản hồi trung bình
    static readonly ConcurrentDictionary<string, double> LastUse = new();     // host -> lần dùng cuối
    static readonly object CacheGate = new();
    static readonly Dictionary<string, TranslationResult> Cache = new();
    static readonly Queue<string> CacheOrder = new();
    static readonly SemaphoreSlim ChunkGate = new(4);

    /// <summary>Hàm lấy dữ liệu thô từ URL. Có thể thay thế khi kiểm thử.</summary>
    public static Func<string, CancellationToken, Task<byte[]>> Fetcher { get; set; } = FetchNetwork;

    // ---------------------------------------------------------------- kết nối dùng chung
    static volatile HttpClient Http = CreateClient();
    static long _lastReset = -10000;

    static Translator()
    {
        // Bật/tắt VPN, đổi Wi-Fi, đổi proxy...: bỏ DNS đã nhớ và các kết nối cũ (chúng thường đã chết) rồi tạo lại.
        NetworkChange.NetworkAddressChanged += (_, _) => ResetNetwork();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => ResetNetwork();
    }

    /// <summary>Quên DNS, bỏ hồ kết nối cũ và các endpoint đang bị "phạt".</summary>
    public static void ResetNetwork()
    {
        // Windows báo đổi mạng nhiều lần liên tiếp, và nhiều yêu cầu song song cùng lỗi một lúc: chỉ làm mới một lần mỗi giây
        long now = Environment.TickCount64, prev = Interlocked.Read(ref _lastReset);
        if (now - prev < 1000 || Interlocked.CompareExchange(ref _lastReset, now, prev) != prev) return;
        Dns.Clear();
        CoolUntil.Clear();
        Latency.Clear();
        LastUse.Clear();
        var old = Http;
        Http = CreateClient();
        // yêu cầu đang chạy dở trên client cũ vẫn được kết thúc; 30 giây sau mới đóng hẳn
        _ = Task.Delay(30000).ContinueWith(_ => { try { old.Dispose(); } catch { } });
    }

    static HttpClient CreateClient()
    {
        var h = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(80),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),     // qua VPN/proxy bắt tay chậm hơn bình thường
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            ConnectCallback = ConnectIpv4First,
        };
        var c = new HttpClient(h)
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
        return c;
    }

    static readonly ConcurrentDictionary<string, (double Expire, IPAddress[] Addrs)> Dns = new();

    static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (Dns.TryGetValue(host, out var hit) && hit.Expire > Now) return hit.Addrs;
        var addrs = await System.Net.Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        // IPv4 trước: IPv6 hỏng có thể treo vài giây
        addrs = addrs.OrderBy(a => a.AddressFamily != AddressFamily.InterNetwork).ToArray();
        Dns[host] = (Now + DnsTtl, addrs);
        return addrs;
    }

    static async ValueTask<Stream> ConnectIpv4First(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        var port = ctx.DnsEndPoint.Port;
        Exception? last = null;
        try
        {
            foreach (var ip in await ResolveAsync(host, ct).ConfigureAwait(false))
            {
                var s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(6000);
                    await s.ConnectAsync(new IPEndPoint(ip, port), cts.Token).ConfigureAwait(false);
                    return new NetworkStream(s, ownsSocket: true);
                }
                catch (Exception e)
                {
                    s.Dispose();
                    last = e;
                    if (ct.IsCancellationRequested) throw;
                }
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested) { last = e; }
        Dns.TryRemove(host, out _);

        // Cách riêng không được (VPN/proxy kiểu "fake IP", DNS lạ...): để hệ điều hành tự phân giải và kết nối.
        var fallback = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await fallback.ConnectAsync(ctx.DnsEndPoint, ct).ConfigureAwait(false);
            return new NetworkStream(fallback, ownsSocket: true);
        }
        catch
        {
            fallback.Dispose();
            if (ct.IsCancellationRequested) throw;
            throw last ?? new IOException("không kết nối được");
        }
    }

    static async Task<byte[]> FetchNetwork(string url, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await SendGet(url, ct).ConfigureAwait(false); }
        catch (Exception e) when ((e is HttpRequestException or IOException or SocketException) && !ct.IsCancellationRequested)
        {
            ResetNetwork();                         // kết nối cũ chết (đổi VPN giữa chừng...): thử lại một lần với kết nối mới
            resp = await SendGet(url, ct).ConfigureAwait(false);
        }
        using var respScope = resp;
        if ((int)resp.StatusCode == 429)
        {
            double ra = 0;
            if (resp.Headers.RetryAfter?.Delta is { } d) ra = d.TotalSeconds;
            throw new RateLimitedException(ra);
        }
        if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {(int)resp.StatusCode}");
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    static async Task<HttpResponseMessage> SendGet(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        return await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
    }

    static string Host(string url) => new Uri(url).Host;

    /// <summary>Gọi lúc vừa thấy bôi đen: nếu kết nối nhàn rỗi lâu thì mở sẵn để lần dịch tới nhanh.</summary>
    public static void Prewarm()
    {
        var host = Host(Order(Endpoints)[0]);
        if (LastUse.TryGetValue(host, out var t) && Now - t < 25.0) return;
        LastUse[host] = Now;
        _ = Task.Run(() => HeadAsync(host));
    }

    /// <summary>Gọi 1 lần lúc khởi động: phân giải DNS, bắt tay TLS sẵn.</summary>
    public static void WarmUp()
    {
        _ = Task.Run(async () =>
        {
            foreach (var ep in Endpoints.Take(2))
            {
                var host = Host(ep);
                await HeadAsync(host).ConfigureAwait(false);
                LastUse[host] = Now;
            }
        });
    }

    static async Task HeadAsync(string host)
    {
        try
        {
            using var cts = new CancellationTokenSource(5000);
            using var req = new HttpRequestMessage(HttpMethod.Head, $"https://{host}/");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        }
        catch { /* chỉ là làm nóng, lỗi bỏ qua */ }
    }

    static List<string> Order(IEnumerable<string> eps) =>
        eps.OrderBy(e => Latency.TryGetValue(Host(e), out var v) ? v : 0.0).ToList();

    // ---------------------------------------------------------------- phân tích phản hồi
    static JsonElement At(JsonElement e, int i) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > i ? e[i] : default;

    static bool IsArr(JsonElement e) => e.ValueKind == JsonValueKind.Array;

    static string Str(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";

    static readonly Regex Tags = new("<[^>]+>");

    public static TranslationResult ParseResponse(JsonElement data, string sl = "auto")
    {
        var r = new TranslationResult();
        var seg = At(data, 0);
        if (IsArr(seg))
            r.Translation = string.Concat(seg.EnumerateArray().Select(p => Str(At(p, 0))));

        var bd = At(data, 1);
        if (IsArr(bd))
        {
            foreach (var m in bd.EnumerateArray())
            {
                var words = At(m, 1);
                r.Meanings.Add(new Meaning
                {
                    Pos = Str(At(m, 0)),
                    Words = IsArr(words) ? words.EnumerateArray().Select(Str).Where(s => s.Length > 0).Take(5).ToList() : new(),
                });
            }
        }

        var ss = At(data, 11);
        if (IsArr(ss))
        {
            foreach (var g in ss.EnumerateArray())
            {
                var groups = At(g, 1);
                if (!IsArr(groups)) continue;
                foreach (var s in groups.EnumerateArray())
                {
                    var list = At(s, 0);
                    if (IsArr(list)) r.Synonyms.AddRange(list.EnumerateArray().Select(Str).Where(x => x.Length > 0));
                }
            }
            r.Synonyms = r.Synonyms.Take(6).ToList();
        }

        var ex = At(At(data, 13), 0);
        if (IsArr(ex))
        {
            foreach (var x in ex.EnumerateArray())
            {
                var t = Tags.Replace(Str(At(x, 0)), "");
                if (t.Length > 0) r.Examples.Add(t);
            }
            r.Examples = r.Examples.Take(2).ToList();
        }

        var det = Str(At(data, 2));
        r.Detected = det.Length > 0 ? det : sl;
        return r;
    }

    /// <summary>Định dạng của endpoint dự phòng (client=dict-chrome-ex).</summary>
    public static TranslationResult ParseLite(JsonElement data, string sl = "auto")
    {
        string tr, detected = sl;
        if (data.ValueKind == JsonValueKind.Object)
        {
            tr = "";
            if (data.TryGetProperty("sentences", out var sents) && IsArr(sents))
                tr = string.Concat(sents.EnumerateArray().Select(x =>
                    x.ValueKind == JsonValueKind.Object && x.TryGetProperty("trans", out var t) ? Str(t) : ""));
            if (data.TryGetProperty("src", out var src) && Str(src).Length > 0) detected = Str(src);
        }
        else if (IsArr(data) && data.GetArrayLength() > 0)           // dạng ["bản dịch", "ngôn ngữ"]
        {
            var first = data[0];
            if (IsArr(first))
            {
                tr = Str(At(first, 0));
                var d = Str(At(first, 1));
                if (d.Length > 0) detected = d;
            }
            else
                tr = Str(first);
        }
        else
            throw new Exception("phản hồi không hợp lệ");
        if (tr.Length == 0) throw new Exception("phản hồi rỗng");
        return new TranslationResult { Translation = tr, Detected = detected };
    }

    // ---------------------------------------------------------------- gọi mạng
    static async Task<TranslationResult> RunFull(string url, string sl, CancellationToken ct)
    {
        var t0 = Now;
        var bytes = await Fetcher(url, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(bytes);
        var res = ParseResponse(doc.RootElement, sl);
        var host = Host(url);
        var dt = Now - t0;
        Latency.AddOrUpdate(host, dt, (_, old) => 0.7 * old + 0.3 * dt);
        LastUse[host] = Now;
        return res;
    }

    static async Task<TranslationResult> RunLite(string url, string sl, CancellationToken ct)
    {
        var bytes = await Fetcher(url, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(bytes);
        return ParseLite(doc.RootElement, sl);
    }

    /// <summary>Gửi tới endpoint đầu; nếu chậm hơn HedgeMs (hoặc lỗi) mới gửi thêm sang endpoint kế.
    /// Lấy kết quả nào về trước. Ít request hơn kiểu gửi đồng loạt nên cũng ít bị 429 hơn.</summary>
    static async Task<TranslationResult> RaceAsync(IReadOnlyList<string> urls,
        Func<string, CancellationToken, Task<TranslationResult>> run)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var pending = new List<Task<TranslationResult>>();
        int next = 0;
        Exception? last = null;

        bool Launch()
        {
            if (next >= urls.Count) return false;
            var u = urls[next++];
            pending.Add(Task.Run(() => run(u, cts.Token)));
            return true;
        }

        Launch();
        while (pending.Count > 0)
        {
            var anyDone = Task.WhenAny(pending);
            if (next < urls.Count)
            {
                var first = await Task.WhenAny(anyDone, Task.Delay(HedgeMs)).ConfigureAwait(false);
                if (!ReferenceEquals(first, anyDone))
                {
                    Launch();                      // endpoint đầu chậm: gửi thêm, không bỏ cái cũ
                    continue;
                }
            }
            var done = await anyDone.ConfigureAwait(false);
            pending.Remove(done);
            try
            {
                var res = await done.ConfigureAwait(false);
                cts.Cancel();
                return res;
            }
            catch (Exception e)
            {
                if (last is not RateLimitedException) last = e;
                if (pending.Count == 0 && !Launch()) break;
            }
        }
        cts.Cancel();
        if (last is OperationCanceledException or null)
            throw new Exception("quá thời gian chờ, kiểm tra mạng");
        throw last;
    }

    static string Esc(string s) => Uri.EscapeDataString(s);

    static async Task<TranslationResult> GtOneAsync(string text, string sl, string tl)
    {
        var dts = text.Length >= Long ? new[] { "t" } : new[] { "t", "bd", "ss", "ex" };
        var qs = $"client=gtx&sl={Esc(sl)}&tl={Esc(tl)}&{string.Join("&", dts.Select(d => "dt=" + d))}&q={Esc(text)}";
        Exception? last = null;
        for (int attempt = 0; attempt <= Retries; attempt++)
        {
            var avail = Endpoints.Where(e => !CoolUntil.TryGetValue(e, out var t) || t <= Now).ToList();
            var eps = Order(avail.Count > 0 ? avail : Endpoints);
            try
            {
                return await RaceAsync(eps.Select(e => $"{e}?{qs}").ToList(), (u, ct) => RunFull(u, sl, ct)).ConfigureAwait(false);
            }
            catch (RateLimitedException e)
            {
                last = e;
                foreach (var ep in eps) CoolUntil[ep] = Now + Math.Max(Cooldown, e.RetryAfter);
            }
            catch (Exception e)
            {
                last = e;
            }
            if (attempt < Retries) await Task.Delay(600).ConfigureAwait(false);
        }
        try                                           // dự phòng
        {
            var lqs = $"client=dict-chrome-ex&sl={Esc(sl)}&tl={Esc(tl)}&q={Esc(text)}";
            using var cts = new CancellationTokenSource(6000);
            return await RunLite($"{LiteEndpoint}?{lqs}", sl, cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) { last ??= e; }
        if (last is RateLimitedException)
            throw new Exception("Google đang giới hạn tần suất dịch. Đợi vài giây rồi bấm Thử lại.");
        var msg = last?.Message;
        throw new Exception(string.IsNullOrEmpty(msg) ? "không kết nối được" : msg);
    }

    // ---------------------------------------------------------------- chuẩn hóa văn bản trước khi dịch
    static readonly Regex Invisible = new("[­​‌‍⁠﻿]");
    static readonly Regex Spaces = new("[ \t 　]+");
    static readonly Regex Paragraphs = new(@"\n[ \t]*\n\s*");
    // dòng mở đầu bằng gạch đầu dòng hoặc số thứ tự: giữ nguyên chỗ xuống dòng
    static readonly Regex ListStart = new(@"^(?:[-*•·▪●○◦‣–—]\s|\(?\d{1,3}[.)]\s|\(?[a-zA-Z][.)]\s)");
    // thiếu dấu cách sau dấu câu giữa hai chữ Hàn: "밝히고,자동차" -> "밝히고, 자동차"
    static readonly Regex HangulPunct = new(@"(?<=[가-힣])([,.;!?])(?=[가-힣])");

    static bool IsCjk(char c) =>
        (c >= 0x2E80 && c <= 0x9FFF && !(c >= 0x3130 && c <= 0x318F))   // bộ thủ, CJK, kana, ký hiệu CJK (trừ jamo Hàn)
        || (c >= 0xF900 && c <= 0xFAFF)
        || (c >= 0xFF00 && c <= 0xFFEF)    // toàn góc
        || c == '，' || c == '。';

    /// <summary>Văn bản bôi đen từ PDF/web thường bị xuống dòng cứng giữa câu. Google dịch từng dòng riêng nên
    /// câu bị cắt đôi, mất ý. Nối các dòng bị ngắt lại, giữ nguyên đoạn văn và danh sách.</summary>
    public static string Prepare(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var t = Invisible.Replace(text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\v', '\n').Replace('\f', '\n'), "");
        var paras = Paragraphs.Split(t).Select(JoinLines).Where(p => p.Length > 0);
        return string.Join("\n\n", paras);
    }

    static string JoinLines(string para)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var raw in para.Split('\n'))
        {
            var line = Spaces.Replace(raw, " ").Trim();
            if (line.Length == 0) continue;
            if (sb.Length == 0) { sb.Append(line); continue; }

            char prev = sb[^1], next = line[0];
            if (ListStart.IsMatch(line)) sb.Append('\n');                       // mục danh sách mới
            else if (prev == '-' && sb.Length > 1 && char.IsLetter(sb[^2]) && char.IsLower(next))
                sb.Length--;                                                    // "exam-\nple" -> "example"
            else if (IsCjk(prev) || IsCjk(next)) { }                            // Trung/Nhật: không cần dấu cách
            else sb.Append(' ');                                                // Hàn, Latin, Nga...: dòng ngắt = dấu cách
            sb.Append(line);
        }
        return HangulPunct.Replace(sb.ToString(), "$1 ");
    }

    // ---------------------------------------------------------------- chia khúc theo hệ chữ
    static readonly Regex SentenceEnd = new(@"(?<=[.!?。！？\n])");

    enum Script { None, Latin, Han, Japanese, Hangul, Cyrillic, Other }

    /// <summary>Hệ chữ chiếm nhiều nhất trong đoạn. None nếu không có chữ cái nào (số, dấu câu, xuống dòng...).</summary>
    static Script DominantScript(string s)
    {
        int lat = 0, han = 0, kana = 0, hang = 0, cyr = 0, oth = 0;
        foreach (var c in s)
        {
            if (!char.IsLetter(c)) continue;
            if (c < 0x250 || (c >= 0x1E00 && c <= 0x1EFF)) lat++;                         // Latin (kể cả chữ Việt có dấu)
            else if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0x31F0 && c <= 0x31FF)) kana++;
            else if ((c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F)) hang++;
            else if (c >= 0x400 && c <= 0x52F) cyr++;
            else if ((c >= 0x3400 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)) han++;
            else oth++;
        }
        // có kana thì chữ Hán trong đoạn đó là của tiếng Nhật
        int ja = kana > 0 ? han + kana : 0, zh = kana > 0 ? 0 : han;
        var best = Script.None;
        int max = 0;
        void Pick(Script sc, int n) { if (n > max) { max = n; best = sc; } }
        Pick(Script.Latin, lat); Pick(Script.Han, zh); Pick(Script.Japanese, ja);
        Pick(Script.Hangul, hang); Pick(Script.Cyrillic, cyr); Pick(Script.Other, oth);
        return best;
    }

    /// <summary>Chỗ cắt một câu quá dài: ưu tiên sau dấu cách/dấu phẩy, không cắt giữa cặp thay thế (surrogate).</summary>
    static int CutPoint(string piece)
    {
        for (int i = Chunk; i > Chunk / 2; i--)
        {
            char c = piece[i - 1];
            if (char.IsWhiteSpace(c) || c is ',' or ';' or '，' or '、' or '；') return i;
        }
        int cut = Chunk;
        if (char.IsHighSurrogate(piece[cut - 1])) cut--;
        return cut;
    }

    /// <summary>Cắt thành các khúc &lt;= Chunk ký tự. Ghép lại các khúc sẽ ra đúng nguyên văn.
    /// Không bao giờ gộp hai câu khác hệ chữ (Trung, Hàn, Nhật, Nga, Latin...) vào cùng một khúc, vì Google chỉ đoán
    /// được MỘT ngôn ngữ cho mỗi lần gọi: gộp lại thì câu ngôn ngữ kia bị bỏ nguyên hoặc bị dịch bậy.</summary>
    public static List<string> Split(string text)
    {
        var parts = new List<string>();
        var cur = new System.Text.StringBuilder();
        var neutral = new System.Text.StringBuilder();       // số thứ tự, xuống dòng...: đi theo câu kế tiếp
        var curScript = Script.None;

        void Flush()
        {
            if (cur.Length > 0) parts.Add(cur.ToString());
            cur.Clear();
            curScript = Script.None;
        }

        foreach (var unit in SentenceEnd.Split(text))
        {
            if (unit.Length == 0) continue;
            var sc = DominantScript(unit);
            if (sc == Script.None) { neutral.Append(unit); continue; }

            var piece = neutral.ToString() + unit;
            neutral.Clear();
            if (cur.Length > 0 && (sc != curScript || cur.Length + piece.Length > Chunk)) Flush();
            while (piece.Length > Chunk)                // câu quá dài không có dấu chấm
            {
                int cut = CutPoint(piece);
                parts.Add(piece[..cut]);
                piece = piece[cut..];
            }
            cur.Append(piece);
            curScript = sc;
        }
        if (neutral.Length > 0)
        {
            if (cur.Length + neutral.Length > Chunk) Flush();
            cur.Append(neutral);
        }
        Flush();
        return parts;
    }

    /// <summary>Ghép bản dịch các khúc lại. Google hay cắt mất khoảng trắng/xuống dòng ở hai đầu: lấy lại từ khúc gốc.</summary>
    static string JoinParts(IReadOnlyList<string> sources, IReadOnlyList<TranslationResult> res)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < res.Count; i++)
        {
            var src = sources[i];
            var core = res[i].Translation.Trim();
            var lead = src[..(src.Length - src.TrimStart().Length)];
            var trail = src[src.TrimEnd().Length..];
            if (sb.Length > 0 && core.Length > 0 && lead.Length == 0
                && !char.IsWhiteSpace(sb[^1]) && !IsCjk(sb[^1]) && !IsCjk(core[0]))
                sb.Append(' ');                         // câu Trung liền câu Việt: cần dấu cách
            sb.Append(lead).Append(core).Append(trail);
        }
        return sb.ToString();
    }

    static async Task<TranslationResult> GtUncachedAsync(string text, string sl, string tl)
    {
        var parts = Split(text);                         // cả đoạn ngắn cũng phải tách nếu lẫn nhiều hệ chữ
        if (parts.Count <= 1) return await GtOneAsync(text, sl, tl).ConfigureAwait(false);
        async Task<TranslationResult> Run(string p)
        {
            await ChunkGate.WaitAsync().ConfigureAwait(false);
            try { return await GtOneAsync(p, sl, tl).ConfigureAwait(false); }
            finally { ChunkGate.Release(); }
        }
        var once = new Dictionary<string, Task<TranslationResult>>();   // khúc giống hệt nhau chỉ gọi mạng một lần
        foreach (var p in parts)
            if (!once.ContainsKey(p)) once[p] = Run(p);
        await Task.WhenAll(once.Values).ConfigureAwait(false);
        var res = parts.Select(p => once[p].Result).ToList();
        int big = 0;                                    // ngôn ngữ nhận diện = của khúc dài nhất, không phải khúc đầu
        for (int i = 1; i < parts.Count; i++) if (parts[i].Length > parts[big].Length) big = i;
        return new TranslationResult
        {
            Translation = JoinParts(parts, res),
            Detected = res[big].Detected,
        };
    }

    public static void ClearCache()
    {
        lock (CacheGate) { Cache.Clear(); CacheOrder.Clear(); }
    }

    public static async Task<TranslationResult> GtAsync(string text, string sl, string tl)
    {
        var key = $"{sl}\u0001{tl}\u0001{text}";
        lock (CacheGate)
            if (Cache.TryGetValue(key, out var hit)) return hit.Clone();
        var r = await GtUncachedAsync(text, sl, tl).ConfigureAwait(false);
        lock (CacheGate)
        {
            if (!Cache.ContainsKey(key))
            {
                if (Cache.Count >= CacheMax) Cache.Remove(CacheOrder.Dequeue());
                CacheOrder.Enqueue(key);
            }
            Cache[key] = r.Clone();
        }
        return r;
    }

    // ---------------------------------------------------------------- đoán ngôn ngữ nhanh
    const string ViChars =
        "ăâđêôơưĂÂĐÊÔƠƯàáãéèíìóòõúùýÀÁÃÉÈÍÌÓÒÕÚÙÝ" +
        "ạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỷỹỵ" +
        "ẠẢẤẦẨẪẬẮẰẲẴẶẸẺẼẾỀỂỄỆỈỊỌỎỐỒỔỖỘỚỜỞỠỢỤỦỨỪỬỮỰỲỶỸỴ";

    /// <summary>Đoán nhanh bằng chữ cái. true/false, hoặc null nếu không chắc.</summary>
    public static bool? GuessNative(string text, string native)
    {
        if (Langs.Base(native) != "vi") return null;
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return null;
        int n = letters.Count(c => ViChars.IndexOf(c) >= 0);
        if (n >= 3 || (n >= 1 && (double)n / letters.Count > 0.04))
            return true;                              // có dấu riêng của tiếng Việt: khỏi gọi lần dò
        if (n == 0 && letters.Count > 25 && letters.All(c => c < 128))
            return false;
        return null;
    }

    /// <summary>Trả về kết quả, kèm Tl là ngôn ngữ đích thực tế đã dịch sang.</summary>
    public static async Task<TranslationResult> TranslateAsync(string text, string native = "vi", string foreign = "en",
        bool smart = true, (string Src, string Tgt)? manual = null)
    {
        var prepared = Prepare(text);
        if (prepared.Length > 0) text = prepared;
        if (manual is { } m)
        {
            var rm = await GtAsync(text, m.Src, m.Tgt).ConfigureAwait(false);
            rm.Tl = m.Tgt;
            return rm;
        }
        bool canFlip = smart && !Langs.Same(foreign, native);
        bool? guess = canFlip ? GuessNative(text, native) : null;
        var first = guess == true ? foreign : native;      // đoán đúng thì chỉ cần 1 lần gọi
        var r = await GtAsync(text, "auto", first).ConfigureAwait(false);
        r.Tl = first;
        if (canFlip)
        {
            bool isNative = Langs.Same(r.Detected, native);
            if (isNative && first != foreign)
            {
                r = await GtAsync(text, "auto", foreign).ConfigureAwait(false);
                r.Tl = foreign;
            }
            else if (!isNative && first == foreign && guess == true)
            {
                r = await GtAsync(text, "auto", native).ConfigureAwait(false);
                r.Tl = native;
            }
        }
        return r;
    }
}
