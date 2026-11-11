using Tiisu.App.Audio;

namespace Tiisu.App.Ui;

/// <summary>
/// 极简音效：运行时合成 WAV（不依赖外部素材），用 NAudio 混音异步播放（BGM 与音效可同时响）。
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

    private static readonly Dictionary<Cue, byte[]> Cache = new();

    /// <summary>NAudio 混音后端（替换 winmm.PlaySound，审计 P1-1 / P1-2）。</summary>
    private static readonly AudioMixer Mixer = new();

    /// <summary>音效总开关（由设置页控制）。</summary>
    public static bool Enabled { get; set; } = true;

    private static int _sfxVolume = 80;
    private static int _bgmVolume = 60;

    /// <summary>音效音量 0..100（0 = 静音）。</summary>
    public static int SfxVolume
    {
        get => _sfxVolume;
        set => _sfxVolume = Math.Clamp(value, 0, 100);
    }

    /// <summary>音乐音量 0..100（0 = 静音）；播放中改音量实时生效，不重启 BGM。</summary>
    public static int BgmVolume
    {
        get => _bgmVolume;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (clamped == _bgmVolume)
            {
                return;
            }

            _bgmVolume = clamped;
            Mixer.SetBgmVolume(clamped / 100f);
        }
    }

    public static void Play(Cue cue)
    {
        if (!Enabled || SfxVolume <= 0)
        {
            return;
        }

        try
        {
            byte[] wav = Scaled(TryLoadCueFile(cue) ?? Get(cue), SfxVolume, cue.ToString());
            Mixer.PlayOneShot(wav);
        }
        catch
        {
            // 没有音频设备 / 系统限制：静默降级
        }
    }

    private static readonly Dictionary<Cue, byte[]?> CueFiles = new();

    /// <summary>Assets/sounds/&lt;cue&gt;.wav（由 tools/Tiisu.AudioGen 生成）；缺失返回 null。</summary>
    private static byte[]? TryLoadCueFile(Cue cue)
    {
        if (CueFiles.TryGetValue(cue, out var cached))
        {
            return cached;
        }

        byte[]? wav = null;
        try
        {
            string path = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "sounds", cueName(cue) + ".wav");
            if (File.Exists(path))
            {
                wav = File.ReadAllBytes(path);
            }
        }
        catch
        {
            wav = null;
        }

        CueFiles[cue] = wav;
        return wav;
    }

    private static string cueName(Cue cue) => cue switch
    {
        Cue.Draw => "draw",
        Cue.Discard => "discard",
        Cue.Call => "call",
        Cue.Kan => "kan",
        Cue.Win => "win",
        Cue.Flow => "flow",
        Cue.Deal => "deal",
        Cue.Shuffle => "shuffle",
        _ => "roundend",
    };

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
        int clickSamples = (int)(rate * 0.026);
        var rnd = new Random(20261023);

        // 先排好每一次点击的频率与间隔，再按总长开数组——旧实现缓冲只装得下 13 声（P2-1）。
        var plan = new (double Hz, int Gap)[clicks];
        int total = 0;
        for (int c = 0; c < clicks; c++)
        {
            double hz = 3200 + (rnd.NextDouble() * 1200);
            int gap = c == clicks - 1 ? 0 : (int)(rate * (0.040 + rnd.NextDouble() * 0.050));
            plan[c] = (hz, gap);
            total += clickSamples + gap;
        }

        double[] samples = new double[total];
        int cursor = 0;
        for (int c = 0; c < clicks; c++)
        {
            double amp = 0.55 - (0.02 * c);
            double hz = plan[c].Hz;
            for (int i = 0; i < clickSamples; i++)
            {
                double t = i / (double)rate;
                double env = Math.Exp(-t * 160) * amp;
                double body = Math.Sin(2 * Math.PI * hz * t) * 0.6 + Math.Sin(2 * Math.PI * hz * 1.7 * t) * 0.25;
                samples[cursor + i] += (body + (rnd.NextDouble() * 2 - 1) * 0.25) * env;
            }

            cursor += clickSamples + plan[c].Gap;
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
    private static byte[]? _bgm;
    private static bool _bgmPlaying;

    /// <summary>播放背景音乐（Assets/bgm/lobby.wav，循环）。没有文件就静默。</summary>
    public static void PlayBgm()
    {
        if (!Enabled || _bgmPlaying)
        {
            return;
        }

        try
        {
            _bgm ??= LoadBgm();
            if (_bgm is null)
            {
                return;
            }

            Mixer.PlayBgm(_bgm, _bgmVolume / 100f);
            _bgmPlaying = true;
        }
        catch
        {
            // 静默降级
        }
    }

    /// <summary>按音量缩放 16-bit PCM（缓存按 cue 名 + 音量）。</summary>
    // 键用 cue 名而不是字节哈希：避免等长 WAV 互相命中（审计 P3-1）。
    private static readonly Dictionary<(string Key, int Volume), byte[]> ScaledCache = new();

    private static byte[] Scaled(byte[] wav, int volume, string keyName)
    {
        int clamped = Math.Clamp(volume, 0, 100);
        if (clamped >= 100)
        {
            return wav;
        }

        var key = (keyName, clamped);
        if (ScaledCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        byte[] copy = (byte[])wav.Clone();
        double gain = clamped / 100.0;

        // 找 fmt（确认 16-bit）与 data 块；非 16-bit 原样返回。
        int pos = 12;
        bool is16 = false;
        int dataStart = -1;
        int dataLen = 0;
        while (pos + 8 <= copy.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(copy, pos, 4);
            int size = BitConverter.ToInt32(copy, pos + 4);
            if (id == "fmt " && size >= 16)
            {
                int bits = BitConverter.ToInt16(copy, pos + 8 + 14);
                is16 = bits == 16;
            }
            else if (id == "data")
            {
                dataStart = pos + 8;
                dataLen = Math.Min(size, copy.Length - dataStart);
                break;
            }

            pos += 8 + size + (size % 2);
        }

        if (!is16 || dataStart < 0)
        {
            ScaledCache[key] = wav;
            return wav;
        }

        for (int i = 0; i + 1 < dataLen; i += 2)
        {
            short sample = BitConverter.ToInt16(copy, dataStart + i);
            int scaled = (int)Math.Round(sample * gain);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            copy[dataStart + i] = (byte)(scaled & 0xFF);
            copy[dataStart + i + 1] = (byte)((scaled >> 8) & 0xFF);
        }

        ScaledCache[key] = copy;
        return copy;
    }

    private static int HashCode(byte[] data) =>
        data.Length ^ (data.Length > 0 ? (data[0] << 8) | data[^1] : 0);

    public static void StopBgm()
    {
        if (!_bgmPlaying)
        {
            return;
        }

        Mixer.StopBgm();
        _bgmPlaying = false;
    }

    private static byte[]? LoadBgm()
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "bgm", "lobby.wav");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>音效开关变化时同步 BGM。'</summary>
    public static void ApplyEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
        {
            StopBgm();
        }
    }

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

            Mixer.PlayOneShot(wav);
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
