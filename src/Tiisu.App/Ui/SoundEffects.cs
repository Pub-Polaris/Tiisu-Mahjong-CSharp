using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Tiisu.App.Ui;

/// <summary>
/// 极简音效：运行时合成 WAV（不依赖外部素材），用 winmm 的 PlaySound 异步播放。
/// 素材来源是程序生成的正弦/噪声，体积为 0，风格保持"低调的点击感"。
/// </summary>
public static class SoundEffects
{
    public enum Cue
    {
        Draw,       // 摸牌（轻）
        Discard,    // 出牌
        Call,       // 吃 / 碰
        Kan,        // 杠（低沉）
        Win,        // 和牌
        Flow,       // 流局
        RoundEnd,   // 局末通用
        Deal,       // 发牌（照 IGS 参考：20 次离散碰撞）
        Shuffle,    // 洗牌（连续摩擦）
    }

    private const uint SndAsync = 0x0001;
    private const uint SndMemory = 0x0004;
    private const uint SndNoDefault = 0x0002;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);

    private static readonly Dictionary<Cue, byte[]> Cache = new();

    /// <summary>音效总开关（由设置页控制）。</summary>
    public static bool Enabled { get; set; } = true;

    public static void Play(Cue cue)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            byte[] wav = Get(cue);
            PlaySound(wav, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
        }
        catch
        {
            // 没有音频设备 / 系统限制：静默降级
        }
    }

    private static byte[] Get(Cue cue)
    {
        if (Cache.TryGetValue(cue, out var cached))
        {
            return cached;
        }

        byte[] wav = cue switch
        {
            Cue.Draw => Tone(new[] { (1500f, 0.018f, 0.25f) }, 0.03f),
            Cue.Discard => Noise(0.035f, 0.5f),
            Cue.Call => Tone(new[] { (740f, 0.05f, 0.5f), (988f, 0.06f, 0.5f) }, 0.12f),
            Cue.Kan => Tone(new[] { (196f, 0.10f, 0.55f), (147f, 0.10f, 0.45f) }, 0.22f),
            Cue.Win => Tone(new[] { (523f, 0.10f, 0.5f), (659f, 0.10f, 0.5f), (784f, 0.16f, 0.6f), (1046f, 0.22f, 0.45f) }, 0.62f),
            Cue.Flow => Tone(new[] { (440f, 0.12f, 0.45f), (330f, 0.18f, 0.4f) }, 0.34f),
            Cue.Deal => Deal(),
            Cue.Shuffle => Shuffle(),
            _ => Tone(new[] { (880f, 0.06f, 0.4f) }, 0.08f),
        };

        Cache[cue] = wav;
        return wav;
    }

    /// <summary>发牌：20 次牌-牌碰撞（间隔 40–90ms），照 IGS 参考音的总长与密度。</summary>
    private static byte[] Deal()
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

    /// <summary>洗牌：连续摩擦噪声，带 8Hz 起伏（照 IGS 参考：1.5s 摩擦声）。</summary>
    private static byte[] Shuffle()
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
            // 一阶低通 → 摩擦质感
            prev = prev * 0.55 + white * 0.45;
            double am = 0.55 + 0.45 * Math.Sin(2 * Math.PI * 8 * t);
            double env = Math.Min(1, t / 0.06) * Math.Min(1, (1.35 - t) / 0.2);
            samples[i] = prev * am * env * 0.5;
        }

        return ToWav(samples, rate);
    }

    private static byte[] Tone((float Hz, float Seconds, float Amp)[] notes, float totalSeconds)
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
                double env = Math.Sin(Math.PI * Math.Min(1.0, i / (len * 0.2))) // 起音
                             * Math.Max(0, 1 - (i / (double)len))               // 衰减
                             * amp;
                samples[cursor + i] += Math.Sin(2 * Math.PI * hz * t) * env;
            }

            cursor += len;
        }

        return ToWav(samples, rate);
    }

    private static byte[] Noise(float seconds, float amp)
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

    private static byte[] ToWav(double[] samples, int rate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataBytes);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);       // PCM
        w.Write((short)1);       // mono
        w.Write(rate);
        w.Write(rate * 2);       // byte rate
        w.Write((short)2);       // block align
        w.Write((short)16);      // bits
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataBytes);

        foreach (var s in samples)
        {
            short v = (short)(Math.Clamp(s, -1, 1) * short.MaxValue);
            w.Write(v);
        }

        w.Flush();
        return ms.ToArray();
    }

    private static readonly Dictionary<string, byte[]> Voices = new();

    /// <summary>
    /// 播放报牌语音（Assets/voices/*.wav，由 espeak-ng 生成）。
    /// 文件不存在时静默跳过——不阻断游戏。
    /// </summary>
    public static void PlayVoice(string name)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            if (!Voices.TryGetValue(name, out var wav))
            {
                string path = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "Assets", "voices", name + ".wav");
                if (!File.Exists(path))
                {
                    return;
                }

                wav = File.ReadAllBytes(path);
                Voices[name] = wav;
            }

            PlaySound(wav, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
        }
        catch
        {
            // 静默降级
        }
    }

    /// <summary>自检：把每个音效写到目录里（听不听都行，程序能验收）。</summary>
    public static IReadOnlyList<(Cue Cue, string Path, int Bytes)> ExportWavs(string dir)
    {
        Directory.CreateDirectory(dir);
        var list = new List<(Cue, string, int)>();
        foreach (Cue cue in Enum.GetValues<Cue>())
        {
            string path = System.IO.Path.Combine(dir, cue.ToString().ToLowerInvariant() + ".wav");
            byte[] wav = Get(cue);
            File.WriteAllBytes(path, wav);
            list.Add((cue, path, wav.Length));
        }

        return list;
    }
}
