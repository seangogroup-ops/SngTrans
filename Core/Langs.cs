using System.Globalization;
using System.Text;

namespace SngTrans;

/// <summary>Danh sách ngôn ngữ và tiện ích so sánh mã ngôn ngữ.</summary>
public static class Langs
{
    public static readonly (string Code, string Name)[] All =
    {
        ("vi", "Tiếng Việt"), ("en", "English"), ("ja", "日本語"), ("ko", "한국어"),
        ("zh-CN", "中文 (简体)"), ("zh-TW", "中文 (繁體)"), ("fr", "Français"),
        ("de", "Deutsch"), ("es", "Español"), ("pt", "Português"), ("it", "Italiano"),
        ("ru", "Русский"), ("th", "ไทย"), ("id", "Bahasa Indonesia"),
        ("ar", "العربية"), ("hi", "हिन्दी"),
        ("tr", "Türkçe"), ("nl", "Nederlands"), ("pl", "Polski"), ("uk", "Українська"),
        ("sv", "Svenska"), ("ms", "Bahasa Melayu"), ("tl", "Filipino"), ("el", "Ελληνικά"),
        ("cs", "Čeština"), ("he", "עברית"),
    };

    public static string Base(string? code)
    {
        var s = code ?? "";
        int i = s.IndexOf('-');
        return (i >= 0 ? s[..i] : s).ToLowerInvariant();
    }

    public static bool Same(string? a, string? b) => Base(a) == Base(b);

    public static string Name(string? code)
    {
        code ??= "";
        foreach (var (c, n) in All)           // khớp đúng mã trước (zh-TW không bị nhận nhầm là zh-CN)
            if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) return n;
        foreach (var (c, n) in All)
            if (Base(c) == Base(code)) return n;
        return code.ToUpperInvariant();
    }

    /// <summary>Từ khoá tìm kiếm thêm vì tên hiển thị là tên bản địa.</summary>
    public static readonly Dictionary<string, string> Aliases = new()
    {
        ["vi"] = "vietnamese viet nam", ["en"] = "tieng anh english anh", ["ja"] = "tieng nhat japanese nhat",
        ["ko"] = "tieng han korean han quoc", ["zh-CN"] = "tieng trung chinese simplified gian the trung quoc",
        ["zh-TW"] = "tieng trung chinese traditional phon the dai loan", ["fr"] = "tieng phap french phap",
        ["de"] = "tieng duc german duc", ["es"] = "tieng tay ban nha spanish", ["pt"] = "tieng bo dao nha portuguese",
        ["it"] = "tieng y italian", ["ru"] = "tieng nga russian nga", ["th"] = "tieng thai thai lan thai",
        ["id"] = "tieng indonesia indonesian", ["ar"] = "tieng a rap arabic a rap", ["hi"] = "tieng hindi an do hindi",
        ["tr"] = "tieng tho nhi ky turkish tho nhi ky", ["nl"] = "tieng ha lan dutch ha lan netherlands",
        ["pl"] = "tieng ba lan polish ba lan", ["uk"] = "tieng ukraina ukrainian ukraina",
        ["sv"] = "tieng thuy dien swedish thuy dien", ["ms"] = "tieng malay malaysia malay",
        ["tl"] = "tieng philippines filipino tagalog phi", ["el"] = "tieng hy lap greek hy lap",
        ["cs"] = "tieng sec czech cong hoa sec", ["he"] = "tieng do thai hebrew israel",
    };

    /// <summary>Bỏ dấu + chữ thường để tìm kiếm không phân biệt dấu.</summary>
    public static string Plain(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }
}
