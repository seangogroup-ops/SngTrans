namespace SngTrans;

/// <summary>Phím tắt dạng chuỗi "&lt;ctrl&gt;+&lt;alt&gt;+t" (cùng định dạng bản Python cũ) &lt;-&gt; mã Win32.</summary>
public static class HotkeySpec
{
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    public const string ForceQuit = "<ctrl>+<alt>+<shift>+q";

    static readonly Dictionary<string, uint> Named = new()
    {
        ["<space>"] = 0x20, ["<enter>"] = 0x0D, ["<tab>"] = 0x09,
        ["<left>"] = 0x25, ["<up>"] = 0x26, ["<right>"] = 0x27, ["<down>"] = 0x28,
        ["<home>"] = 0x24, ["<end>"] = 0x23, ["<page_up>"] = 0x21, ["<page_down>"] = 0x22,
        ["<insert>"] = 0x2D,
    };

    public static bool TryParse(string? spec, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;
        foreach (var raw in spec.Split('+'))
        {
            var p = raw.Trim().ToLowerInvariant();
            if (p.Length == 0) return false;
            switch (p)
            {
                case "<ctrl>": mods |= MOD_CONTROL; continue;
                case "<alt>": mods |= MOD_ALT; continue;
                case "<shift>": mods |= MOD_SHIFT; continue;
                case "<cmd>": mods |= MOD_WIN; continue;
            }
            if (vk != 0) return false;                    // chỉ cho một phím chính
            if (p.Length == 1 && ((p[0] >= 'a' && p[0] <= 'z') || (p[0] >= '0' && p[0] <= '9')))
                vk = char.ToUpperInvariant(p[0]);
            else if (p.Length >= 4 && p.StartsWith("<f") && p.EndsWith(">") && int.TryParse(p[2..^1], out var n) && n >= 1 && n <= 12)
                vk = (uint)(0x70 + n - 1);
            else if (Named.TryGetValue(p, out var v))
                vk = v;
            else
                return false;
        }
        return vk != 0;
    }

    public static string Pretty(string? spec)
    {
        var parts = new List<string>();
        foreach (var raw in (spec ?? "").Split('+'))
        {
            var p = raw.Trim();
            parts.Add(p.ToLowerInvariant() switch
            {
                "<ctrl>" => "Ctrl",
                "<alt>" => "Alt",
                "<shift>" => "Shift",
                "<cmd>" => "Win",
                _ => p.Trim('<', '>').ToUpperInvariant(),
            });
        }
        return string.Join("  +  ", parts);
    }
}
