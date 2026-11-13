using System;
using System.Collections.Generic;
using System.IO;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;

namespace Tiisu.App.Runtime;

/// <summary>
/// 报牌语音不随安装包分发：首次需要时用本机 Windows SAPI 中文语音生成到
/// <c>%LOCALAPPDATA%\TiisuMahjong\voices</c>。没有中文语音时静默降级。
/// </summary>
internal sealed class LocalVoiceCache
{
    private static readonly (string Name, string Spoken)[] Lines =
    {
        ("chi", "吃"),
        ("peng", "碰"),
        ("gang", "杠"),
        ("hepai", "胡牌"),
        ("zimo", "自摸"),
        ("ronghe", "荣胡"),
        ("liuju", "流局"),
        ("tingpai", "听牌"),
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private bool _generationStarted;

    public static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TiisuMahjong", "voices");

    public byte[]? TryGet(string name)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(name, out var cached))
            {
                return cached;
            }
        }

        try
        {
            string path = Path.Combine(CacheDir, name + ".wav");
            if (!File.Exists(path))
            {
                return null;
            }

            byte[] wav = File.ReadAllBytes(path);
            lock (_gate)
            {
                _cache[name] = wav;
            }

            return wav;
        }
        catch
        {
            return null;
        }
    }

    public void EnsureGenerated()
    {
        lock (_gate)
        {
            if (_generationStarted)
            {
                return;
            }

            _generationStarted = true;
        }

        Task.Run(GenerateMissing);
    }

    private void GenerateMissing()
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            string? voice = FindChineseVoice();
            if (voice is null)
            {
                return;
            }

            foreach (var (name, spoken) in Lines)
            {
                string outPath = Path.Combine(CacheDir, name + ".wav");
                if (File.Exists(outPath))
                {
                    continue;
                }

                string tmp = Path.Combine(Path.GetTempPath(), "tiisu-voice-" + Guid.NewGuid().ToString("N") + ".wav");
                try
                {
                    using (var synth = new SpeechSynthesizer())
                    {
                        synth.SelectVoice(voice);
                        synth.Rate = 0;
                        synth.Volume = 100;
                        synth.SetOutputToWaveFile(tmp);
                        synth.Speak(spoken);
                        synth.SetOutputToNull();
                    }

                    var (data, rate) = WavIo.Read(tmp);
                    WavIo.Write(outPath, VoicePolish.Process(data, rate), rate);
                }
                finally
                {
                    try
                    {
                        File.Delete(tmp);
                    }
                    catch
                    {
                        // 忽略
                    }
                }
            }
        }
        catch
        {
            // 没有 SAPI / 没有音频设备：语音静默
        }
    }

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
            // 忽略
        }

        return null;
    }

    private static class VoicePolish
    {
        public static double[] Process(double[] data, int rate)
        {
            data = Trim(data, rate);

            double mean = 0;
            foreach (double v in data)
            {
                mean += v;
            }

            mean /= Math.Max(1, data.Length);

            double a = Math.Exp(-2 * Math.PI * 140 / rate);
            double px = 0, py = 0;
            var hp = new double[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                double x = data[i] - mean;
                double y = a * (py + x - px);
                px = x;
                py = y;
                hp[i] = y;
            }

            for (int i = 0; i < hp.Length; i++)
            {
                hp[i] = Math.Tanh(hp[i] * 1.4) / 1.4;
            }

            int fi = (int)(rate * 0.006), fo = (int)(rate * 0.04);
            if (hp.Length > fi + fo)
            {
                for (int i = 0; i < fi; i++)
                {
                    hp[i] *= i / (double)fi;
                }

                for (int i = 0; i < fo; i++)
                {
                    hp[hp.Length - 1 - i] *= i / (double)fo;
                }
            }

            double peak = 0;
            foreach (double v in hp)
            {
                peak = Math.Max(peak, Math.Abs(v));
            }

            if (peak > 0)
            {
                double gain = 0.72 / peak;
                for (int i = 0; i < hp.Length; i++)
                {
                    hp[i] *= gain;
                }
            }

            return hp;
        }

        private static double[] Trim(double[] data, int rate, double threshold = 0.01)
        {
            int start = -1, end = -1;
            for (int i = 0; i < data.Length; i++)
            {
                if (Math.Abs(data[i]) > threshold)
                {
                    if (start < 0)
                    {
                        start = i;
                    }

                    end = i;
                }
            }

            if (start < 0 || end <= start)
            {
                return data;
            }

            int a = Math.Max(0, start - (int)(rate * 0.01));
            int b = Math.Min(data.Length, end + (int)(rate * 0.05));
            var trimmed = new double[b - a];
            Array.Copy(data, a, trimmed, 0, trimmed.Length);
            return trimmed;
        }
    }

    private static class WavIo
    {
        public static (double[] Data, int Rate) Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 12 || BitConverter.ToInt32(bytes, 0) != 0x46464952)
            {
                throw new InvalidDataException("不是 RIFF/WAV");
            }

            int pos = 12;
            int channels = 1, bits = 16, rate = 22050;
            byte[]? payload = null;
            while (pos + 8 <= bytes.Length)
            {
                string chunk = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                int body = pos + 8;
                if (chunk == "fmt ")
                {
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    rate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                }
                else if (chunk == "data")
                {
                    int len = Math.Min(size, bytes.Length - body);
                    payload = new byte[len];
                    Array.Copy(bytes, body, payload, 0, len);
                }

                pos = body + size + (size % 2);
            }

            if (payload is null)
            {
                throw new InvalidDataException("WAV 缺 data");
            }

            int bytesPerSample = Math.Max(1, bits / 8);
            int frames = payload.Length / bytesPerSample / Math.Max(1, channels);
            var data = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int offset = (i * channels + c) * bytesPerSample;
                    sum += bits == 16
                        ? BitConverter.ToInt16(payload, offset) / 32768.0
                        : (payload[offset] - 128) / 128.0;
                }

                data[i] = sum / channels;
            }

            return (data, rate);
        }

        public static void Write(string path, double[] data, int rate)
        {
            using var stream = File.Create(path);
            using var w = new BinaryWriter(stream);
            int dataBytes = data.Length * 2;
            w.Write("RIFF"u8);
            w.Write(36 + dataBytes);
            w.Write("WAVE"u8);
            w.Write("fmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(dataBytes);
            foreach (double v in data)
            {
                w.Write((short)Math.Clamp((int)(v * 32767), short.MinValue, short.MaxValue));
            }
        }
    }
}
