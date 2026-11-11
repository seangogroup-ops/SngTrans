using System.Text;
using System.Text.Json;
using SngTrans;

int fails = 0, total = 0;
void Check(bool ok, string name)
{
    total++;
    if (!ok) { fails++; Console.WriteLine("  THẤT BẠI: " + name); }
}
void Eq<T>(T actual, T expected, string name) =>
    Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{name} (nhận {actual}, cần {expected})");

// ---------- Langs
Eq(Langs.Name("zh-TW"), "中文 (繁體)", "zh-TW không bị nhận nhầm là giản thể");
Eq(Langs.Name("zh"), "中文 (简体)", "zh -> giản thể");
Eq(Langs.Name("xx"), "XX", "mã lạ");
Check(Langs.Same("zh-CN", "zh-TW"), "same zh");
Eq(Langs.Plain("Tiếng Việt Đức"), "tieng viet duc", "bỏ dấu");

// ---------- HotkeySpec
Check(HotkeySpec.TryParse("<ctrl>+<alt>+t", out var m, out var vk) && m == (HotkeySpec.MOD_CONTROL | HotkeySpec.MOD_ALT) && vk == 'T', "parse ctrl+alt+t");
Check(HotkeySpec.TryParse("<ctrl>+<alt>+<shift>+q", out m, out vk) && m == 7 && vk == 'Q', "parse force quit");
Check(HotkeySpec.TryParse("<ctrl>+<f5>", out m, out vk) && vk == 0x74, "parse F5");
Check(HotkeySpec.TryParse("<alt>+<page_up>", out m, out vk) && vk == 0x21, "parse page_up");
Check(!HotkeySpec.TryParse("<ctrl>+<alt>", out _, out _), "thiếu phím chính");
Check(!HotkeySpec.TryParse("<ctrl>+ab", out _, out _), "phím lạ");
Check(!HotkeySpec.TryParse("", out _, out _), "rỗng");
Eq(HotkeySpec.Pretty("<ctrl>+<alt>+t"), "Ctrl  +  Alt  +  T", "pretty");

// ---------- ClickDetector
{
    var d = new ClickDetector();
    d.Press(100, 100, 0); Eq(d.Release(160, 100, 0.2), "drag", "kéo");
    d.Press(10, 10, 1.0); Eq(d.Release(10, 10, 1.05), (string?)null, "nhấp đơn");
    d.Press(10, 10, 1.2); Eq(d.Release(11, 10, 1.25), "multi", "nhấp đúp");
    d.Press(10, 10, 1.4); Eq(d.Release(10, 10, 1.45), "multi", "nhấp ba");
    d.Press(10, 10, 3.0); Eq(d.Release(10, 10, 3.05), (string?)null, "quá lâu thì tính lại từ đầu");
    d.Press(10, 10, 3.2); Eq(d.Release(40, 10, 3.25), "drag", "xa nhau thì là kéo");
}

// ---------- Translator.ParseResponse / ParseLite
{
    const string json = """
    [[["Xin chào","Hello",null,null,10],["!", "!",null,null,10]],
     [["interjection",["xin chào","chào","này","ê","a","b"],[["xin chào",["hello"],null,0.5]],"hello",1]],
     "en",null,null,null,null,[],null,null,null,
     [["interjection",[[["hi","greeting","hallo"],""]],"hello"]],
     null,
     [[["<b>Hello</b> world","x"],["Say <b>hello</b>","y"],["third","z"]]]]
    """;
    using var doc = JsonDocument.Parse(json);
    var r = Translator.ParseResponse(doc.RootElement);
    Eq(r.Translation, "Xin chào!", "ghép bản dịch");
    Eq(r.Detected, "en", "ngôn ngữ nhận diện");
    Eq(r.Meanings.Count, 1, "số nhóm nghĩa");
    Eq(r.Meanings[0].Pos, "interjection", "từ loại");
    Eq(r.Meanings[0].Words.Count, 5, "tối đa 5 nghĩa");
    Eq(string.Join("|", r.Synonyms), "hi|greeting|hallo", "đồng nghĩa");
    Eq(r.Examples.Count, 2, "tối đa 2 ví dụ");
    Eq(r.Examples[0], "Hello world", "bỏ thẻ HTML ở ví dụ");

    using var d2 = JsonDocument.Parse("""[[["Xin chào","Hello"]],null,"en"]""");
    var r2 = Translator.ParseResponse(d2.RootElement);
    Eq(r2.Translation, "Xin chào", "dạng ngắn");
    Check(r2.Meanings.Count == 0 && r2.Synonyms.Count == 0 && r2.Examples.Count == 0, "dạng ngắn không có thêm");

    using var l1 = JsonDocument.Parse("""{"sentences":[{"trans":"Xin "},{"trans":"chào"}],"src":"en"}""");
    var rl1 = Translator.ParseLite(l1.RootElement);
    Eq(rl1.Translation, "Xin chào", "lite dạng object"); Eq(rl1.Detected, "en", "lite src");
    using var l2 = JsonDocument.Parse("""[["Bonjour","en"]]""");
    Eq(Translator.ParseLite(l2.RootElement).Translation, "Bonjour", "lite dạng mảng lồng");
    using var l3 = JsonDocument.Parse("""["Hola"]""");
    Eq(Translator.ParseLite(l3.RootElement).Translation, "Hola", "lite dạng mảng phẳng");
    using var l4 = JsonDocument.Parse("{}");
    bool threw = false; try { Translator.ParseLite(l4.RootElement); } catch { threw = true; }
    Check(threw, "lite rỗng phải báo lỗi");
}

// ---------- GuessNative
Eq(Translator.GuessNative("Xin chào các bạn", "vi"), (bool?)true, "có dấu Việt");
Eq(Translator.GuessNative("This is a rather long english sentence indeed", "vi"), (bool?)false, "tiếng Anh dài");
Eq(Translator.GuessNative("hello", "vi"), (bool?)null, "ngắn không chắc");
Eq(Translator.GuessNative("Xin chào", "en"), (bool?)null, "native không phải vi");

// ---------- Translator.Split
{
    var longText = string.Concat(Enumerable.Repeat("Đây là một câu khá dài để thử chia nhỏ. ", 40));
    var parts = Translator.Split(longText);
    Check(parts.Count > 1, "chia thành nhiều khúc");
    Check(parts.All(p => p.Length <= 450), "mỗi khúc <= 450");
    Eq(string.Concat(parts), longText, "ghép lại đúng nguyên văn");
    var noDot = new string('a', 1000);
    Eq(string.Concat(Translator.Split(noDot)), noDot, "câu không dấu chấm vẫn đủ chữ");
}

// ---------- Translator.Split theo hệ chữ (văn bản lẫn nhiều ngôn ngữ)
{
    static bool Han(char c) => c >= 0x4E00 && c <= 0x9FFF;
    static bool Hangul(char c) => c >= 0xAC00 && c <= 0xD7AF;
    static bool Latin(char c) => char.IsLetter(c) && c < 0x250;
    static int Kinds(string s) => (s.Any(Han) ? 1 : 0) + (s.Any(Hangul) ? 1 : 0) + (s.Any(Latin) ? 1 : 0);

    var mixed = "1. When the sun goes down, the city changes.\n2. 当太阳落山时，城市变了。灯光照亮了街道。\n3. 해가 지면 도시가 변합니다.\n4. Khi mặt trời lặn, thành phố khác hẳn.";
    var parts = Translator.Split(mixed);
    Eq(string.Concat(parts), mixed, "lẫn nhiều chữ: ghép lại đúng nguyên văn");
    Check(parts.All(p => Kinds(p) <= 1), "mỗi khúc chỉ một hệ chữ: " + string.Join(" | ", parts));
    Check(parts.Count >= 4, "Anh, Trung, Hàn, Việt tách riêng");
    Check(parts.Any(p => p.StartsWith("\n2.") || p.StartsWith("2.")), "số thứ tự đi theo câu của nó");

    var oneLine = "Hello world. 你好，世界。 안녕하세요.";
    Check(Translator.Split(oneLine).All(p => Kinds(p) <= 1), "một dòng lẫn chữ vẫn tách");

    var cjkLong = string.Concat(Enumerable.Repeat("这是很长的一句话没有句号", 80));
    var cp = Translator.Split(cjkLong);
    Check(cp.All(p => p.Length <= 450), "Trung dài không dấu chấm: khúc <= 450");
    Eq(string.Concat(cp), cjkLong, "Trung dài: đủ chữ");
}

// ---------- Translator.Prepare (nối dòng bị ngắt khi copy từ PDF)
{
    Eq(Translator.Prepare("작은 식당들은 맛있는 음식을 즐기며\n즐겁게 대화하는 사람들로 가득합니다."),
        "작은 식당들은 맛있는 음식을 즐기며 즐겁게 대화하는 사람들로 가득합니다.", "Hàn: nối dòng bằng dấu cách");
    Eq(Translator.Prepare("수많은 불빛이 거리를 밝히고,자동차와 오토바이가"),
        "수많은 불빛이 거리를 밝히고, 자동차와 오토바이가", "Hàn: thiếu dấu cách sau dấu phẩy");
    Eq(Translator.Prepare("今天天气\n很好。"), "今天天气很好。", "Trung: nối dòng không dấu cách");
    Eq(Translator.Prepare("This is a long sen-\ntence that wraps\r\nacross lines."),
        "This is a long sentence that wraps across lines.", "Anh: nối gạch cuối dòng và dòng ngắt");
    Eq(Translator.Prepare("Đoạn một dòng một\ndòng hai\n\nĐoạn hai"), "Đoạn một dòng một dòng hai\n\nĐoạn hai", "giữ ranh giới đoạn");
    Eq(Translator.Prepare("Mua:\n- táo\n- cam"), "Mua:\n- táo\n- cam", "giữ danh sách");
    Eq(Translator.Prepare("pi = 3.14,abc"), "pi = 3.14,abc", "không sửa dấu câu ngoài chữ Hàn");
    Eq(Translator.Prepare("  \n "), "", "toàn khoảng trắng");
}

// ---------- Translator.TranslateAsync với mạng giả
{
    var calls = new List<string>();
    Translator.Fetcher = (url, ct) =>
    {
        calls.Add(url);
        var q = Uri.UnescapeDataString(url[(url.IndexOf("&q=") + 3)..]);
        var tl = url[(url.IndexOf("&tl=") + 4)..].Split('&')[0];
        string src = q.Contains("chào") ? "vi" : "en";
        var json = $"[[[\"[{tl}] {q}\",\"{q}\"]],null,\"{src}\"]";
        return Task.FromResult(Encoding.UTF8.GetBytes(json));
    };
    Translator.ClearCache();

    var r1 = await Translator.TranslateAsync("Xin chào các bạn", "vi", "en", true);
    Eq(r1.Tl, "en", "tiếng Việt -> dịch sang en"); Eq(calls.Count, 1, "đoán đúng native: chỉ 1 lần gọi");

    calls.Clear();
    var r2 = await Translator.TranslateAsync("hello", "vi", "en", true);
    Eq(r2.Tl, "vi", "tiếng Anh -> dịch sang vi"); Eq(calls.Count, 1, "một lần gọi");

    calls.Clear();
    var r3 = await Translator.TranslateAsync("hello", "vi", "en", true);   // lần 2 lấy từ bộ nhớ đệm
    Eq(calls.Count, 0, "bộ nhớ đệm");

    calls.Clear();
    var r4 = await Translator.TranslateAsync("chào", "vi", "en", true);    // không dấu riêng, 'chào' có à → đoán native
    Eq(r4.Tl, "en", "chào -> en");

    calls.Clear();
    var r5 = await Translator.TranslateAsync("xin", "en", "fr", false, ("auto", "ja"));
    Eq(r5.Tl, "ja", "dịch tay"); Check(calls[0].Contains("tl=ja"), "dịch tay gọi đúng đích");

    // văn bản lẫn Anh/Trung/Hàn: mỗi ngôn ngữ một lần gọi riêng, không khúc nào lẫn hai hệ chữ
    Translator.ClearCache();
    calls.Clear();
    var multi = "1. When the sun goes down, the city changes.\n2. 当太阳落山时，城市变了。\n3. 해가 지면 도시가 변합니다.";
    var rm = await Translator.TranslateAsync(multi, "vi", "en", true);
    Eq(calls.Count, 3, "Anh/Trung/Hàn: 3 lần gọi");
    Check(calls.All(u =>
    {
        var q = Uri.UnescapeDataString(u[(u.IndexOf("&q=") + 3)..]);
        int k = (q.Any(ch => ch >= 0x4E00 && ch <= 0x9FFF) ? 1 : 0) + (q.Any(ch => ch >= 0xAC00 && ch <= 0xD7AF) ? 1 : 0)
              + (q.Any(ch => char.IsLetter(ch) && ch < 0x250) ? 1 : 0);
        return k <= 1;
    }), "không lần gọi nào lẫn hai hệ chữ");
    Check(rm.Translation.Contains('\n'), "giữ xuống dòng giữa các mục");

    // bị giới hạn tần suất ở mọi endpoint rồi tới dự phòng
    Translator.ClearCache();
    Translator.Fetcher = (url, ct) => throw new RateLimitedException(0);
    string msg = "";
    try { await Translator.TranslateAsync("zzz limited", "vi", "en", true); } catch (Exception e) { msg = e.Message; }
    Check(msg.Contains("giới hạn"), "báo giới hạn tần suất: " + msg);

    // endpoint chính hỏng, dự phòng sống
    Translator.ClearCache();
    Translator.Fetcher = (url, ct) => url.Contains("clients5")
        ? Task.FromResult(Encoding.UTF8.GetBytes("""{"sentences":[{"trans":"dự phòng"}],"src":"en"}"""))
        : throw new Exception("HTTP 500");
    var rb = await Translator.TranslateAsync("backup test", "vi", "en", true);
    Eq(rb.Translation, "dự phòng", "dùng endpoint dự phòng");
}

// ---------- Settings (đọc file kiểu Python cũ)
{
    var dir = Path.Combine(Path.GetTempPath(), "sngtrans_test_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "settings.json"),
        """{"native":"ja","foreign":"vi","smart":false,"auto":true,"ibeam_only":true,"hotkey":"<ctrl>+<alt>+y","ver":1,"first_run":false,"blocked_apps":["code.exe"]}""");
    File.WriteAllText(Path.Combine(dir, "vocab.json"),
        """[{"text":"hello","translation":"xin chào","source":"en","ts":1700000000000}]""");
    var s = new Settings(dir);
    Eq(s.Native, "ja", "native"); Eq(s.Smart, false, "smart"); Eq(s.IbeamOnly, false, "ver<2 tắt ibeam_only");
    Eq(s.Hotkey, SettingsData.DefaultHotkey, "ver<3: bỏ phím tắt khách đã lưu, về mặc định");
    Eq(s.Data.ToggleHotkey, SettingsData.DefaultToggleHotkey, "ver<3: phím bật/tắt về mặc định");
    Eq(s.FirstRun, false, "first_run");
    Eq(s.BlockedApps.Count, 1, "blocked_apps");
    s.Update(d => { d.Auto = false; d.BlockedApps.Add("winword.exe"); d.Hotkey = "<ctrl>+<alt>+y"; });
    var s2 = new Settings(dir);
    Eq(s2.Hotkey, "<ctrl>+<alt>+y", "ver 3: phím khách đổi sau đó được giữ nguyên");
    Eq(s2.Auto, false, "lưu và đọc lại"); Eq(s2.BlockedApps.Count, 2, "blocked_apps lưu lại");
    var json = File.ReadAllText(Path.Combine(dir, "settings.json"));
    Check(json.Contains("\"ibeam_only\"") && json.Contains("\"first_run\"") && json.Contains("\"blocked_apps\""), "khoá JSON giữ tên cũ");

    Eq(s.Vocab().Count, 1, "đọc ghi chú cũ"); Eq(s.Vocab()[0].Translation, "xin chào", "bản dịch ghi chú");
    s.AddVocab(new VocabItem { Text = "a,b", Translation = "x\"y", Source = "en", Ts = 5 });
    Eq(s.Vocab()[0].Ts, 5L, "ghi chú mới lên đầu");
    var csv = File.ReadAllText(s.ExportCsv(Path.Combine(dir, "out.csv")), Encoding.UTF8);
    Check(csv.Contains("\"a,b\",\"x\"\"y\""), "CSV thoát dấu phẩy và nháy kép");
    s.RemoveVocab(5); Eq(s.Vocab().Count, 1, "xoá ghi chú");
    s.ClearVocab(); Eq(s.Vocab().Count, 0, "xoá hết");
    Directory.Delete(dir, true);
}

// ---------- Tts.Chunks
{
    var c = Tts.Chunks("Câu một. Câu hai! Câu ba?");
    Eq(c.Count, 1, "gộp câu ngắn"); 
    var big = string.Concat(Enumerable.Repeat("Một câu khá dài để thử. ", 30));
    var cs = Tts.Chunks(big);
    Check(cs.Count > 1 && cs.All(x => x.Length <= 180), "chia khúc <= 180");
    Eq(Tts.NormalizeLang("auto", "Xin chào các bạn"), "vi", "auto -> vi theo dấu");
    Eq(Tts.NormalizeLang("auto", "こんにちは"), "ja", "auto -> ja");
    Eq(Tts.NormalizeLang("", "hello"), "en", "rỗng -> en");
    Eq(Tts.NormalizeLang("zh-cn", "你好"), "zh-CN", "zh-cn -> zh-CN");
    Eq(Tts.NormalizeLang("en-US", "hi"), "en", "en-US -> en");
}

// ---------- Updater: kiểm tra SHA256
{
    const string abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";   // SHA256("abc")
    Eq(Updater.ParseSha256(abc + "  SngTrans.exe\n"), abc, "sha256: mã + tên file");
    Eq(Updater.ParseSha256(abc + " *SngTrans.exe"), abc, "sha256: dạng nhị phân (*)");
    Eq(Updater.ParseSha256("\uFEFF" + abc.ToUpperInvariant() + "\r\n"), abc, "sha256: BOM, chữ hoa, CRLF -> chữ thường");
    Eq(Updater.ParseSha256(abc), abc, "sha256: chỉ có mã");
    Check(Updater.ParseSha256("") == null && Updater.ParseSha256(null) == null, "sha256: rỗng -> null");
    Check(Updater.ParseSha256("abc123") == null, "sha256: quá ngắn -> null");
    Check(Updater.ParseSha256(new string('z', 64)) == null, "sha256: không phải hex -> null");
    var f = Path.Combine(Path.GetTempPath(), "sngtrans-sha-" + Guid.NewGuid().ToString("N") + ".bin");
    File.WriteAllBytes(f, new byte[] { 0x61, 0x62, 0x63 });
    Eq(Updater.Sha256Of(f), abc, "Sha256Of(\"abc\")");
    File.WriteAllBytes(f, new byte[] { 0x61, 0x62, 0x64 });
    Check(Updater.Sha256Of(f) != abc, "đổi 1 byte thì mã khác");
    File.Delete(f);
}

Console.WriteLine(fails == 0 ? $"TẤT CẢ ĐẠT ({total} kiểm tra)" : $"CÓ {fails}/{total} KIỂM TRA THẤT BẠI");
return fails == 0 ? 0 : 1;
