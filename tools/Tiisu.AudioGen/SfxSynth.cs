using System;
using System.Collections.Generic;
using System.IO;

namespace Tiisu.AudioGen;

/// <summary>
/// 音效合成：程序生成 WAV，不依赖外部素材。
/// 参数对齐《明星三缺一 2002》（IGS）参考音的分析结果：
///   发牌 1.175 s / 20 次离散碰撞 / 间隔 40–90 ms；洗牌 1.567 s 连续摩擦。
/// </summary>
internal static class SfxSynth
{
    public static readonly (string Name, Func<byte[]> Build)[] All =
    {
        ("draw", () => Tone(new[] { (1500f, 0.018f, 0.25f) }, 0.03f)),
        ("discard", () => Noise(0.035f, 0.5f)),
        ("call", () => Tone(new[] { (740f, 0.05f, 0.5f), (988f, 0.06f, 0.5f) }, 0.12f)),
        ("kan", () => Tone(new[] { (196f, 0.10f, 0.55f), (147f, 0.10f, 0.45f) }, 0.22f)),
        ("win", () => Tone(new[] { (523f, 0.10f, 0.5f), (659f, 0.10f, 0.5f), (784f, 0.16f, 0.6f), (1046f, 0.22f, 0.45f) }, 0.62f)),
        ("flow", () => Tone(new[] { (440f, 0.12f, 0.45f), (330f, 0.18f, 0.4f) }, 0.34f)),
        ("roundend", () => Tone(new[] { (880f, 0.06f, 0.4f) }, 0.08f)),
        ("deal", Deal),
        ("shuffle", Shuffle),
    };

    public static byte[] Tone((float Hz, float Seconds, float Amp)[] notes, float totalSeconds)
    {
        const int rate = 22050;
        int total = (int)(rate * totalSeconds);
        double[] samples = new double[total];
        int cursor = 0;

        foreach (var (hz, seconds, amp) in notes)
        {
            int len = (int)(rate * seconds);
            for (int i = 0; i < len && cursor + i < total; i++)
            {
                double t = i / (double)rate;
                double env = Math.Sin(Math.PI * Math.Min(1.0, i / (len * 0.2)))
                             * Math.Max(0, 1 - (i / (double)len))
                             * amp;
                samples[cursor + i] += Math.Sin(2 * Math.PI * hz * t) * env;
            }

            cursor += len;
        }

        return ToWav(samples, rate);
    }

    public static byte[] Noise(float seconds, float amp)
    {
        const int rate = 22050;
        int total = (int)(rate * seconds);
        var samples = new double[total];
        var rnd = new Random(12345);
        for (int i = 0; i < total; i++)
        {
            double env = Math.Max(0, 1 - (i / (double)total));
            samples[i] = ((rnd.NextDouble() * 2) - 1) * amp * env * 0.6;
        }

        return ToWav(samples, rate);
    }

    /// <summary>发牌：20 次牌-牌碰撞（间隔 40–90ms），总长 1.2s。</summary>
    public static byte[] Deal()
    {
        const int rate = 24000;
        const int clicks = 20;
        double[] samples = new double[rate * 12 / 10];
        var rnd = new Random(20261023);
        int cursor = 0;

        for (int c = 0; c < clicks && cursor < samples.Length; c++)
        {
            int len = (int)(rate * 0.026);
            double amp = 0.55 - (0.02 * c);
            double hz = 3200 + (rnd.NextDouble() * 1200);
            for (int i = 0; i < len && cursor + i < samples.Length; i++)
            {
                double t = i / (double)rate;
                double env = Math.Exp(-t * 160) * amp;
                double body = Math.Sin(2 * Math.PI * hz * t) * 0.6 + Math.Sin(2 * Math.PI * hz * 1.7 * t) * 0.25;
                samples[cursor + i] += (body + (rnd.NextDouble() * 2 - 1) * 0.25) * env;
            }

            cursor += len + (int)(rate * (0.040 + rnd.NextDouble() * 0.050));
        }

        return ToWav(samples, rate);
    }

    /// <summary>洗牌：1.35s 连续摩擦噪声（8Hz 起伏）。</summary>
    public static byte[] Shuffle()
    {
        const int rate = 24000;
        int total = (int)(rate * 1.35);
        var samples = new double[total];
        var rnd = new Random(777);
        double prev = 0;
        for (int i = 0; i < total; i++)
        {
            double t = i / (double)rate;
            double white = rnd.NextDouble() * 2 - 1;
            prev = prev * 0.55 + white * 0.45;
            double am = 0.55 + 0.45 * Math.Sin(2 * Math.PI * 8 * t);
            double env = Math.Min(1, t / 0.06) * Math.Min(1, (1.35 - t) / 0.2);
            samples[i] = prev * am * env * 0.5;
        }

        return ToWav(samples, rate);
    }

    public static byte[] ToWav(double[] samples, int rate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
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

        foreach (var s in samples)
        {
            w.Write((short)(Math.Clamp(s, -1, 1) * short.MaxValue));
        }

        w.Flush();
        return ms.ToArray();
    }
}
