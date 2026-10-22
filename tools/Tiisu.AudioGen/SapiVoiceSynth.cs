using System;
using System.Collections.Generic;
using System.IO;
using System.Speech.Synthesis;

namespace Tiisu.AudioGen;

/// <summary>
/// 用 Windows 自带中文语音（SAPI，如 Microsoft Huihui）合成报牌语音。
/// 比 espeak-ng 自然得多；参数按参考音（F0≈261Hz）调。
/// </summary>
internal static class SapiVoiceSynth
{
    /// <summary>TTS 用的文本：麻将里「和」读 hú，写「胡」让 TTS 读对。</summary>
    private static readonly (string Name, string Text, string Spoken)[] Lines =
    {
        ("chi", "吃", "吃"),
        ("peng", "碰", "碰"),
        ("gang", "杠", "杠"),
        ("hepai", "和牌", "胡牌"),
        ("zimo", "自摸", "自摸"),
        ("ronghe", "荣和", "荣胡"),
        ("liuju", "流局", "流局"),
        ("tingpai", "听牌", "听牌"),
    };

    public static bool IsAvailable => FindChineseVoice() is not null;

    private static string? FindChineseVoice()
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            foreach (var voice in synth.GetInstalledVoices())
            {
                var info = voice.VoiceInfo;
                if (info.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && voice.Enabled)
                {
                    return info.Name;
                }
            }
        }
        catch
        {
            // 无 SAPI 环境
        }

        return null;
    }

    public static int Generate(string outDir)
    {
        string? voiceName = FindChineseVoice();
        if (voiceName is null)
        {
            Console.Error.WriteLine("没有可用的中文 SAPI 语音");
            return 5;
        }

        Directory.CreateDirectory(outDir);
        string tmp = Path.Combine(Path.GetTempPath(), "tiisu-sapi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        Console.WriteLine($"语音引擎：SAPI · {voiceName}");

        try
        {
            foreach (var (name, text, spoken) in Lines)
            {
                string raw = Path.Combine(tmp, name + ".wav");
                using (var synth = new SpeechSynthesizer())
                {
                    synth.SelectVoice(voiceName);
                    synth.Rate = 0;      // -10..10
                    synth.Volume = 100;
                    synth.SetOutputToWaveFile(raw);
                    synth.Speak(spoken);
                    synth.SetOutputToNull();
                }

                (double[] data, int rate) = WavIo.Read(raw);
                data = VoicePolish.Process(data, rate);
                string outPath = Path.Combine(outDir, name + ".wav");
                WavIo.Write(outPath, data, rate);
                Console.WriteLine($"  {name,-8} {text,-3} -> {spoken,-3} {data.Length / (double)rate:5.2f}s  {new FileInfo(outPath).Length / 1024,3} KB");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tmp, true);
            }
            catch
            {
                // 忽略
            }
        }

        return 0;
    }
}
