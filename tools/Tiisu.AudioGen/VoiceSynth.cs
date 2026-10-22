using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Tiisu.AudioGen;

/// <summary>
/// 报牌语音：调 espeak-ng 合成，再做后处理（裁静音 / 高通 / 软限幅 / 归一）。
/// 参数针对参考音调过：参考 F0 中位 261 Hz、单字 ~0.35 s → cmn+f4 / rate 180 / pitch 85（实测 256 Hz）。
/// </summary>
internal static class VoiceSynth
{
    public const string DefaultEspeak = @"C:\Program Files\eSpeak NG\espeak-ng.exe";

    private static readonly string[] EspeakArgs = { "-v", "cmn+f4", "-s", "180", "-p", "85" };

    public static readonly (string Name, string Text)[] Lines =
    {
        ("chi", "吃"),
        ("peng", "碰"),
        ("gang", "杠"),
        ("hepai", "和牌"),
        ("zimo", "自摸"),
        ("ronghe", "荣和"),
        ("liuju", "流局"),
        ("tingpai", "听牌"),
    };

    public static int Generate(string outDir, string espeak)
    {
        if (!File.Exists(espeak))
        {
            Console.Error.WriteLine($"找不到 espeak-ng：{espeak}");
            return 2;
        }

        Directory.CreateDirectory(outDir);
        string tmp = Path.Combine(Path.GetTempPath(), "tiisu-voice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);

        try
        {
            foreach (var (name, text) in Lines)
            {
                string raw = Path.Combine(tmp, name + ".wav");
                var psi = new ProcessStartInfo(espeak)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                foreach (var a in EspeakArgs)
                {
                    psi.ArgumentList.Add(a);
                }

                psi.ArgumentList.Add("-w");
                psi.ArgumentList.Add(raw);
                psi.ArgumentList.Add(text);

                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    Console.Error.WriteLine("启动 espeak-ng 失败");
                    return 3;
                }

                proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    Console.Error.WriteLine($"espeak-ng 合成失败：{text}（exit {proc.ExitCode}）");
                    return 4;
                }

                (double[] data, int rate) = ReadWav(raw);
                data = Polish(Trim(data, rate), rate);
                string outPath = Path.Combine(outDir, name + ".wav");
                WriteWav(outPath, data, rate);
                Console.WriteLine($"  {name,-8} {text,-3} {data.Length / (double)rate:5.2f}s  {new FileInfo(outPath).Length / 1024,3} KB");
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
                // 清理失败忽略
            }
        }

        return 0;
    }

    private static (double[] Data, int Rate) ReadWav(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        string riff = Encoding.ASCII.GetString(br.ReadBytes(4));
        _ = br.ReadInt32();
        string wave = Encoding.ASCII.GetString(br.ReadBytes(4));
        if (riff != "RIFF" || wave != "WAVE")
        {
            throw new InvalidDataException("不是 WAV: " + path);
        }

        int channels = 1;
        int bits = 16;
        int rate = 22050;
        byte[]? payload = null;

        while (fs.Position + 8 <= fs.Length)
        {
            string chunk = Encoding.ASCII.GetString(br.ReadBytes(4));
            int size = br.ReadInt32();
            if (chunk == "fmt ")
            {
                _ = br.ReadInt16();          // format
                channels = br.ReadInt16();
                rate = br.ReadInt32();
                _ = br.ReadInt32();          // byte rate
                _ = br.ReadInt16();          // block align
                bits = br.ReadInt16();
                if (size > 16)
                {
                    br.ReadBytes(size - 16);
                }
            }
            else if (chunk == "data")
            {
                payload = br.ReadBytes(size);
            }
            else
            {
                br.ReadBytes(size);
            }

            if (size % 2 == 1)
            {
                br.ReadByte();
            }
        }

        if (payload is null)
        {
            throw new InvalidDataException("WAV 缺少 data: " + path);
        }

        var data = new double[payload.Length / (bits / 8) / channels];
        if (bits == 16)
        {
            int frames = payload.Length / 2 / channels;
            for (int i = 0; i < frames; i++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += BitConverter.ToInt16(payload, (i * channels + c) * 2) / 32768.0;
                }

                data[i] = sum / channels;
            }
        }
        else
        {
            int frames = payload.Length / channels;
            for (int i = 0; i < frames; i++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += (payload[i * channels + c] - 128) / 128.0;
                }

                data[i] = sum / channels;
            }
        }

        return (data, rate);
    }

    private static double[] Trim(double[] data, int rate, double threshold = 0.015)
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

    private static double[] Polish(double[] data, int rate)
    {
        double mean = 0;
        foreach (var v in data)
        {
            mean += v;
        }

        mean /= Math.Max(1, data.Length);

        // 一阶高通 ~140Hz
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

        // 软限幅（压齿音）
        for (int i = 0; i < hp.Length; i++)
        {
            hp[i] = Math.Tanh(hp[i] * 1.4) / 1.4;
        }

        // 淡入 6ms / 淡出 40ms
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

        // 峰值归一 0.72
        double peak = 0;
        foreach (var v in hp)
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

    private static void WriteWav(string path, double[] data, int rate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = data.Length * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        foreach (var v in data)
        {
            w.Write((short)(Math.Clamp(v, -1, 1) * short.MaxValue));
        }

        w.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }
}
