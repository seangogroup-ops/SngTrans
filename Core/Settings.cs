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
    [JsonPropertyName("ver")] public int Ver { get; set; } = 2;
    [JsonPropertyName("first_run")] public bool FirstRun { get; set; } = true;
    [JsonPropertyName("blocked_apps")] public List<string> BlockedApps { get; set; } = new();

    public const string DefaultHotkey = "<ctrl>+<alt>+t";
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
                loaded.Ver = 2;
                if (string.IsNullOrWhiteSpace(loaded.Native)) loaded.Native = "vi";
                if (string.IsNullOrWhiteSpace(loaded.Foreign)) loaded.Foreign = "en";
                if (string.IsNullOrWhiteSpace(loaded.Hotkey)) loaded.Hotkey = SettingsData.DefaultHotkey;
                loaded.BlockedApps ??= new List<string>();
                Data = loaded;
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
            Save();
        }
    }

    // ----- ghi chú (vocab.json, tương thích bản cũ) -----
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
