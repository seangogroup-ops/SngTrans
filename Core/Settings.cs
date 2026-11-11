using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SngTrans;

/// <summary>Dữ liệu cài đặt (cùng định dạng settings.json với bản Python cũ nên giữ được cài đặt).</summary>
public sealed class SettingsData
{
    [JsonPropertyName("native")] public string Native { get; set; } = "vi";
    [JsonPropertyName("foreign")] public string Foreign { get; set; } = "en";
    [JsonPropertyName("smart")] public bool Smart { get; set; } = true;
    [JsonPropertyName("auto")] public bool Auto { get; set; } = true;
    [JsonPropertyName("ibeam_only")] public bool IbeamOnly { get; set; } = false;
    [JsonPropertyName("hotkey")] public string Hotkey { get; set; } = DefaultHotkey;
    [JsonPropertyName("toggle_hotkey")] public string ToggleHotkey { get; set; } = DefaultToggleHotkey;
    [JsonPropertyName("ver")] public int Ver { get; set; } = 3;
    [JsonPropertyName("first_run")] public bool FirstRun { get; set; } = true;
    [JsonPropertyName("blocked_apps")] public List<string> BlockedApps { get; set; } = new();
    /// <summary>Phiên bản đã hiện màn hướng dẫn. Khác phiên bản hiện tại thì hiện lại đúng một lần.</summary>
    [JsonPropertyName("seen_guide")] public string SeenGuide { get; set; } = "";

    public const string DefaultHotkey = "<alt>+1";
    public const string DefaultToggleHotkey = "<alt>+2";
}

public sealed class VocabItem
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("translation")] public string Translation { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("ts")] public long Ts { get; set; }
}

/// <summary>Cài đặt và ghi chú, lưu trong %APPDATA%\SngTrans.</summary>
public sealed class Settings
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // giữ nguyên chữ có dấu, không đổi thành \uXXXX
        PropertyNameCaseInsensitive = true,
    };

    readonly object _lock = new();
    readonly string _path, _vocabPath;

    public string Folder { get; }
    public SettingsData Data { get; private set; } = new();

    public Settings(string? folder = null)
    {
        Folder = folder ?? DataDir();
        Directory.CreateDirectory(Folder);
        _path = Path.Combine(Folder, "settings.json");
        _vocabPath = Path.Combine(Folder, "vocab.json");
        Load();
    }

    public static string DataDir()
    {
        var baseDir = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var d = Path.Combine(baseDir, "SngTrans");
        var old = Path.Combine(baseDir, "MiniTranslate");        // bản rất cũ: chuyển dữ liệu sang thư mục mới
        if (!Directory.Exists(d) && Directory.Exists(old))
        {
            try { Directory.Move(old, d); } catch { }
        }
        Directory.CreateDirectory(d);
        return d;
    }

    public string Native => Data.Native;
    public string Foreign => Data.Foreign;
    public bool Smart => Data.Smart;
    public bool Auto => Data.Auto;
    public bool IbeamOnly => Data.IbeamOnly;
    public string Hotkey => Data.Hotkey;
    public string ToggleHotkey => Data.ToggleHotkey;
    public bool FirstRun => Data.FirstRun;
    public IReadOnlyList<string> BlockedApps { get { lock (_lock) return Data.BlockedApps.ToList(); } }

    void Load()
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_path, Encoding.UTF8), Json);
            if (loaded != null)
            {
                if (loaded.Ver < 2) loaded.IbeamOnly = false;     // bản cũ bật ibeam_only làm nhiều app không dịch được
                bool migrate = loaded.Ver < 3;
                if (migrate)                                      // bản cũ: bỏ mọi phím tắt khách đã lưu (Alt+F1/F2, Alt+1/2 cũ hay phím tự đổi), về mặc định
                {
                    loaded.Hotkey = SettingsData.DefaultHotkey;
                    loaded.ToggleHotkey = SettingsData.DefaultToggleHotkey;
                }
                loaded.Ver = 3;
                if (string.IsNullOrWhiteSpace(loaded.Native)) loaded.Native = "vi";
                if (string.IsNullOrWhiteSpace(loaded.Foreign)) loaded.Foreign = "en";
                loaded.SeenGuide ??= "";
                if (string.IsNullOrWhiteSpace(loaded.Hotkey)) loaded.Hotkey = SettingsData.DefaultHotkey;
                if (string.IsNullOrWhiteSpace(loaded.ToggleHotkey)) loaded.ToggleHotkey = SettingsData.DefaultToggleHotkey;
                loaded.BlockedApps ??= new List<string>();
                Data = loaded;
                if (migrate) Save();                              // ghi ngay để lần sau không phải đổi lại
            }
        }
        catch { /* chưa có file hoặc file hỏng: dùng mặc định */ }
    }

    void Save()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json), new UTF8Encoding(false));
            File.Move(tmp, _path, true);
        }
        catch { }
    }

    public void Update(Action<SettingsData> mutate)
    {
        lock (_lock)
        {
            mutate(Data);
            _dirty = false;                  // Save() ghi toàn bộ nên mọi thay đổi chờ ghi trễ cũng đã được ghi
            Save();
        }
    }

    bool _dirty;
    System.Threading.Timer? _saveTimer;

    /// <summary>Đổi giá trị ngay trong bộ nhớ (có hiệu lực tức thì), còn ghi file thì dồn lại sau ~0,4 giây.
    /// Dùng cho thao tác bấm dồn như bật/tắt tự dịch: bấm 10 lần chỉ ghi đĩa 1 lần.</summary>
    public void UpdateDeferred(Action<SettingsData> mutate, int delayMs = 400)
    {
        lock (_lock)
        {
            mutate(Data);
            _dirty = true;
            if (_saveTimer == null) _saveTimer = new System.Threading.Timer(_ => Flush());
            _saveTimer.Change(delayMs, Timeout.Infinite);
        }
    }

    /// <summary>Ghi ngay các thay đổi còn chờ (gọi trước khi thoát app).</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (!_dirty) return;
            _dirty = false;
            Save();
        }
    }

    // ----- ghi chú (vocab.json, tương thích bản cũ) -----
    /// <summary>Dấu hiệu file ghi chú đã đổi (giờ ghi + dung lượng): so sánh rẻ, khỏi đọc và phân tích JSON mỗi lần.</summary>
    public (long Ticks, long Length) VocabStamp()
    {
        try
        {
            var fi = new FileInfo(_vocabPath);
            return fi.Exists ? (fi.LastWriteTimeUtc.Ticks, fi.Length) : (0, 0);
        }
        catch { return (0, 0); }
    }

    public List<VocabItem> Vocab()
    {
        try
        {
            return JsonSerializer.Deserialize<List<VocabItem>>(File.ReadAllText(_vocabPath, Encoding.UTF8), Json)
                   ?? new List<VocabItem>();
        }
        catch { return new List<VocabItem>(); }
    }

    void WriteVocab(List<VocabItem> items)
    {
        try
        {
            var tmp = _vocabPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(items, Json), new UTF8Encoding(false));
            File.Move(tmp, _vocabPath, true);
        }
        catch { }
    }

    public void AddVocab(VocabItem item)
    {
        lock (_lock)
        {
            var items = Vocab();
            items.Insert(0, item);
            WriteVocab(items);
        }
    }

    public void RemoveVocab(long ts)
    {
        lock (_lock) WriteVocab(Vocab().Where(v => v.Ts != ts).ToList());
    }

    public void ClearVocab()
    {
        lock (_lock) WriteVocab(new List<VocabItem>());
    }

    static string Csv(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public string ExportCsv(string? path = null)
    {
        path ??= Path.Combine(Folder, "ghi_chu.csv");
        var sb = new StringBuilder();
        foreach (var v in Vocab())
            sb.Append(Csv(v.Text)).Append(',').Append(Csv(v.Translation)).Append("\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // BOM để Excel đọc đúng tiếng Việt
        return path;
    }
}
