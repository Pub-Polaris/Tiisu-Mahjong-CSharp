using System;
using System.IO;
using System.Linq;
using Tiisu.Localization;

namespace Tiisu.AudioGen;

/// <summary>
/// 音频合成工具：一次生成全部音效 + 报牌语音到 Assets 目录。
///
/// 用法：
///   Tiisu.AudioGen                         # 生成音效 + 语音（默认输出到 App 的 Assets）
///   Tiisu.AudioGen --out &lt;dir&gt;             # 指定输出目录（会建 sounds/ 与 voices/）
///   Tiisu.AudioGen --sfx                   # 只生成音效
///   Tiisu.AudioGen --voices                # 只生成语音（需要 espeak-ng）
///   Tiisu.AudioGen --espeak &lt;exe&gt;          # 指定 espeak-ng 路径
///   Tiisu.AudioGen --list                  # 只列出会生成哪些文件
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // CLI 文案跟随 TIISU_LANG；缺省 zh-CN。
        L.SetLanguage(Environment.GetEnvironmentVariable("TIISU_LANG"));
        string? outDir = null;
        string? espeak = null;
        string engine = "auto";
        string? bgm = null;
        double gainDb = -5.0;
        bool sfx = false;
        bool voices = false;
        bool force = false;
        bool list = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                    outDir = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--engine":
                    engine = i + 1 < args.Length ? args[++i] : "auto";
                    break;
                case "--bgm":
                    bgm = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--gain":
                    gainDb = i + 1 < args.Length && double.TryParse(args[++i], out var g) ? g : -5.0;
                    break;
                case "--espeak":
                    espeak = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--sfx":
                    sfx = true;
                    break;
                case "--voices":
                    voices = true;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--list":
                    list = true;
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine("Tiisu.AudioGen [--out <dir>] [--sfx] [--voices] [--bgm <wav>] [--gain dB] [--engine auto|sapi|espeak] [--list]");
                    return 0;
            }
        }

        if (!sfx && !voices)
        {
            sfx = true;
            voices = true;
        }

        outDir ??= FindDefaultOutput();

        if (list)
        {
            Console.WriteLine(L.T("tools.cli.audio.sfxHeader"));
            foreach (var (name, _) in SfxSynth.All)
            {
                Console.WriteLine("  " + name + ".wav");
            }

            Console.WriteLine(L.T("tools.cli.audio.voiceHeader"));
            foreach (var (name, text) in VoiceSynth.Lines)
            {
                Console.WriteLine($"  {name}.wav  ({text})");
            }

            Console.WriteLine(L.T("tools.cli.audio.outputDir", ("path", outDir)));
            return 0;
        }

        Console.WriteLine(L.T("tools.cli.audio.outputDir", ("path", outDir)));

        if (sfx)
        {
            string dir = Path.Combine(outDir, "sounds");
            Directory.CreateDirectory(dir);
            Console.WriteLine(L.T("tools.cli.audio.sfxHeader"));
            foreach (var (name, build) in SfxSynth.All)
            {
                string path = Path.Combine(dir, name + ".wav");
                if (!force && File.Exists(path))
                {
                    Console.WriteLine($"  {name,-9} " + L.T("tools.cli.audio.skipExists"));
                    continue;
                }

                byte[] wav = build();
                File.WriteAllBytes(path, wav);
                Console.WriteLine($"  {name,-9} {wav.Length,7} B  {path}");
            }
        }

        if (voices)
        {
            Console.WriteLine(L.T("tools.cli.audio.voiceHeader"));
            string voiceDir = Path.Combine(outDir, "voices");
            if (!force && Directory.Exists(voiceDir)
                && Directory.EnumerateFiles(voiceDir, "*.wav").Any())
            {
                Console.WriteLine("  " + L.T("tools.cli.audio.voiceSkip"));
                goto AfterVoices;
            }

            bool useSapi = engine is "sapi" or "auto" && SapiVoiceSynth.IsAvailable;

            if (useSapi)
            {
                int code = SapiVoiceSynth.Generate(voiceDir);
                if (code != 0 && engine == "sapi")
                {
                    return code;
                }

                useSapi = code == 0;
            }

            if (!useSapi)
            {
                if (engine == "auto")
                {
                    Console.WriteLine(L.T("tools.cli.audio.noChineseSapi"));
                }

                int code = VoiceSynth.Generate(voiceDir, espeak ?? VoiceSynth.DefaultEspeak);
                if (code != 0)
                {
                    Console.Error.WriteLine(L.T("tools.cli.audio.voiceFailed"));
                    return code;
                }
            }
        }

    AfterVoices:
        if (!string.IsNullOrWhiteSpace(bgm))
        {
            Console.WriteLine(L.T("tools.cli.audio.bgmHeader"));
            string bgmPath = Path.Combine(outDir, "bgm", "lobby.wav");
            if (!force && File.Exists(bgmPath))
            {
                Console.WriteLine("  " + L.T("tools.cli.audio.bgmSkip", ("path", bgmPath)));
            }
            else
            {
                int bgmCode = BgmSynth.Convert(bgm!, bgmPath, gainDb);
                if (bgmCode != 0)
                {
                    return bgmCode;
                }
            }
        }

        Console.WriteLine(L.T("tools.cli.audio.done"));
        return 0;
    }

    /// <summary>默认输出到 App 的 Assets（从当前目录向上找 src/Tiisu.App/Assets）。</summary>
    private static string FindDefaultOutput()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir.FullName, "src", "Tiisu.App", "Assets");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        var exeDir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && exeDir is not null; i++)
        {
            string candidate = Path.Combine(exeDir.FullName, "src", "Tiisu.App", "Assets");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            exeDir = exeDir.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "audio-out");
    }
}
