using System;
using System.IO;

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
        string? outDir = null;
        string? espeak = null;
        string engine = "auto";
        bool sfx = false;
        bool voices = false;
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
                case "--espeak":
                    espeak = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--sfx":
                    sfx = true;
                    break;
                case "--voices":
                    voices = true;
                    break;
                case "--list":
                    list = true;
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine("Tiisu.AudioGen [--out <dir>] [--sfx] [--voices] [--engine auto|sapi|espeak] [--espeak <exe>] [--list]");
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
            Console.WriteLine("音效：");
            foreach (var (name, _) in SfxSynth.All)
            {
                Console.WriteLine("  " + name + ".wav");
            }

            Console.WriteLine("语音：");
            foreach (var (name, text) in VoiceSynth.Lines)
            {
                Console.WriteLine($"  {name}.wav  ({text})");
            }

            Console.WriteLine("输出目录：" + outDir);
            return 0;
        }

        Console.WriteLine("输出目录：" + outDir);

        if (sfx)
        {
            string dir = Path.Combine(outDir, "sounds");
            Directory.CreateDirectory(dir);
            Console.WriteLine("音效：");
            foreach (var (name, build) in SfxSynth.All)
            {
                byte[] wav = build();
                string path = Path.Combine(dir, name + ".wav");
                File.WriteAllBytes(path, wav);
                Console.WriteLine($"  {name,-9} {wav.Length,7} B  {path}");
            }
        }

        if (voices)
        {
            Console.WriteLine("语音：");
            string voiceDir = Path.Combine(outDir, "voices");
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
                    Console.WriteLine("（没有中文 SAPI 语音，改用 espeak-ng）");
                }

                int code = VoiceSynth.Generate(voiceDir, espeak ?? VoiceSynth.DefaultEspeak);
                if (code != 0)
                {
                    Console.Error.WriteLine("语音生成失败（可用 --espeak 指定 espeak-ng 路径）");
                    return code;
                }
            }
        }

        Console.WriteLine("完成。");
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
