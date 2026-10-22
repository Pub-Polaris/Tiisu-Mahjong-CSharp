using System;
using System.IO;
using System.Text;

namespace Tiisu.AudioGen;

/// <summary>WAV 读写（16bit / 8bit，单声道或多声道混合到单声道）。</summary>
internal static class WavIo
{
    public static (double[] Data, int Rate) Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF")
        {
            throw new InvalidDataException("不是 RIFF/WAV: " + path);
        }

        int pos = 12;
        int channels = 1, bits = 16, rate = 22050;
        byte[]? payload = null;

        while (pos + 8 <= bytes.Length)
        {
            string chunk = Encoding.ASCII.GetString(bytes, pos, 4);
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
            throw new InvalidDataException("WAV 缺 data 块: " + path);
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
