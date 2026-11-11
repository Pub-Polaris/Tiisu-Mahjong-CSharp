using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Tiisu.App.Audio;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>
/// 混音器采样率/声道转换（261112 音频回归）：
/// 合成音是 22050/24000 单声道，若被按 44100 直接读会「变快 + 升调」。
/// </summary>
public sealed class AudioResampleTests
{
    private static byte[] MakeWav(int rate, int channels, double seconds)
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            int frames = (int)(rate * seconds);
            int dataBytes = frames * channels * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            for (int i = 0; i < frames * channels; i++)
            {
                writer.Write((short)(Math.Sin(i * 0.05) * 8000));
            }
        }

        return ms.ToArray();
    }

    private static int ReadFrameCount(ISampleProvider provider)
    {
        var buffer = new float[4096];
        int total = 0;
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
        }

        return total / provider.WaveFormat.Channels;
    }

    [Fact]
    public void 单声道22050_转成44100立体声_时长不变()
    {
        var wav = MakeWav(rate: 22050, channels: 1, seconds: 1.0);
        using var reader = new WaveFileReader(new MemoryStream(wav));

        var converted = AudioMixer.ToMixerFormat(reader.ToSampleProvider());

        Assert.Equal(44100, converted.WaveFormat.SampleRate);
        Assert.Equal(2, converted.WaveFormat.Channels);
        Assert.InRange(ReadFrameCount(converted), 43500, 44500);
    }

    [Fact]
    public void 立体声44100_原样通过()
    {
        var wav = MakeWav(rate: 44100, channels: 2, seconds: 0.5);
        using var reader = new WaveFileReader(new MemoryStream(wav));

        var converted = AudioMixer.ToMixerFormat(reader.ToSampleProvider());

        Assert.Equal(44100, converted.WaveFormat.SampleRate);
        Assert.Equal(2, converted.WaveFormat.Channels);
        Assert.InRange(ReadFrameCount(converted), 19000, 24000);
    }

    [Fact]
    public void 单声道24000_转成44100立体声_时长不变()
    {
        var wav = MakeWav(rate: 24000, channels: 1, seconds: 1.0);
        using var reader = new WaveFileReader(new MemoryStream(wav));

        var converted = AudioMixer.ToMixerFormat(reader.ToSampleProvider());

        Assert.Equal(44100, converted.WaveFormat.SampleRate);
        Assert.Equal(2, converted.WaveFormat.Channels);
        Assert.InRange(ReadFrameCount(converted), 43500, 44500);
    }
}
