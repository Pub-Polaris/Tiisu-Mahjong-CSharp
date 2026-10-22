using System;
using System.IO;
using System.Text;

namespace Tiisu.AudioGen;

/// <summary>
/// BGM 处理：把外部 WAV 按指定增益缩放后拷进 Assets/bgm（默认 -5 dB）。
/// 保留原声道/采样率/位深——只在 data 块上原地缩放，不重编码。
/// </summary>
internal static class BgmSynth
{
    public static int Convert(string src, string dest, double gainDb)
    {
        if (!File.Exists(src))
        {
            Console.Error.WriteLine("BGM 源不存在: " + src);
            return 6;
        }

        byte[] bytes = File.ReadAllBytes(src);
        if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF"
            || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
        {
            Console.Error.WriteLine("不是 WAV: " + src);
            return 6;
        }

        double gain = Math.Pow(10, gainDb / 20.0);

        int pos = 12;
        int bits = 16;
        while (pos + 8 <= bytes.Length)
        {
            string chunk = Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int body = pos + 8;

            if (chunk == "fmt ")
            {
                bits = BitConverter.ToInt16(bytes, body + 14);
            }
            else if (chunk == "data")
            {
                int len = Math.Min(size, bytes.Length - body);
                if (bits == 16)
                {
                    for (int i = 0; i + 1 < len; i += 2)
                    {
                        short v = BitConverter.ToInt16(bytes, body + i);
                        short scaled = (short)Math.Clamp((int)Math.Round(v * gain), short.MinValue, short.MaxValue);
                        bytes[body + i] = (byte)(scaled & 0xFF);
                        bytes[body + i + 1] = (byte)((scaled >> 8) & 0xFF);
                    }
                }
                else if (bits == 8)
                {
                    for (int i = 0; i < len; i++)
                    {
                        int v = bytes[body + i] - 128;
                        int scaled = (int)Math.Clamp(Math.Round(v * gain), -128, 127);
                        bytes[body + i] = (byte)(scaled + 128);
                    }
                }
                else
                {
                    Console.Error.WriteLine($"暂不支持 {bits} bit");
                    return 6;
                }

                break;
            }

            pos = body + size + (size % 2);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dest))!);
        File.WriteAllBytes(dest, bytes);
        Console.WriteLine($"  BGM {Math.Round(gainDb, 1)} dB → {dest}（{bytes.Length / 1024} KB）");
        return 0;
    }
}
