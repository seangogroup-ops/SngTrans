using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.SpeechSynthesis;

namespace SngTrans;

/// <summary>Giọng đọc có sẵn của Windows (chạy offline). Cần cài giọng của ngôn ngữ đó: Cài đặt Windows > Thời gian &amp; ngôn ngữ > Giọng nói.</summary>
static class LocalVoice
{
    /// <summary>Tạo file .wav cho đoạn chữ; trả về đường dẫn, hoặc null nếu máy chưa có giọng phù hợp.</summary>
    public static async Task<string?> SynthesizeAsync(string text, string lang)
    {
        var want = lang.Trim();
        var baseLang = want.Split('-')[0];
        var voices = SpeechSynthesizer.AllVoices;
        var voice = voices.FirstOrDefault(v => v.Language.Equals(want, StringComparison.OrdinalIgnoreCase))
                    ?? voices.FirstOrDefault(v => v.Language.StartsWith(baseLang + "-", StringComparison.OrdinalIgnoreCase)
                                                  || v.Language.Equals(baseLang, StringComparison.OrdinalIgnoreCase));
        if (voice == null)
        {
            Log.Info($"không có giọng Windows cho \"{lang}\"");
            return null;
        }
        using var synth = new SpeechSynthesizer { Voice = voice };
        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        var path = Path.Combine(Path.GetTempPath(), $"sngtrans_voice_{Environment.ProcessId}_{Environment.TickCount64}.wav");
        await using (var fs = File.Create(path))
        {
            using var input = stream.AsStreamForRead();
            await input.CopyToAsync(fs);
        }
        return path;
    }
}
