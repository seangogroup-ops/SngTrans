using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
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
    const double DnsTtl = 300.0;
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
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var h = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(80),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(3),
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            ConnectCallback = ConnectIpv4First,
        };
        var c = new HttpClient(h)
        {
            Timeout = TimeSpan.FromSeconds(10),
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
        foreach (var ip in await ResolveAsync(host, ct).ConfigureAwait(false))
        {
            var s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(3000);
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
        Dns.TryRemove(host, out _);
        throw last ?? new IOException("không kết nối được");
    }

    static async Task<byte[]> FetchNetwork(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if ((int)resp.StatusCode == 429)
        {
            double ra = 0;
            if (resp.Headers.RetryAfter?.Delta is { } d) ra = d.TotalSeconds;
            throw new RateLimitedException(ra);
        }
        if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {(int)resp.StatusCode}");
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
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

    public static List<string> Split(string text)
    {
        var parts = new List<string>();
        var cur = "";
        foreach (var p in Regex.Split(text, @"(?<=[.!?。\n])"))
        {
            var piece = p;
            if (piece.Length == 0) continue;
            if (cur.Length + piece.Length > Chunk && cur.Length > 0)
            {
                parts.Add(cur);
                cur = "";
            }
            while (piece.Length > Chunk)               // câu quá dài không có dấu chấm
            {
                parts.Add(piece[..Chunk]);
                piece = piece[Chunk..];
            }
            cur += piece;
        }
        if (cur.Length > 0) parts.Add(cur);
        return parts;
    }

    static async Task<TranslationResult> GtUncachedAsync(string text, string sl, string tl)
    {
        if (text.Length <= Chunk) return await GtOneAsync(text, sl, tl).ConfigureAwait(false);
        var parts = Split(text);
        var tasks = parts.Select(async p =>
        {
            await ChunkGate.WaitAsync().ConfigureAwait(false);
            try { return await GtOneAsync(p, sl, tl).ConfigureAwait(false); }
            finally { ChunkGate.Release(); }
        }).ToList();
        var res = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new TranslationResult
        {
            Translation = string.Concat(res.Select(r => r.Translation)),
            Detected = res[0].Detected,
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
