using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Tiisu.App.Audio;

/// <summary>
/// NAudio 混音播放器（审计 P1-1 / P1-2 的后端替换）：
/// 一条 <see cref="WaveOutEvent"/> 输出 + <see cref="MixingSampleProvider"/> 混音，
/// BGM 与音效各自独立 voice，不再出现「放音效把 BGM 掐掉」。
/// <para>
/// 所有 WAV 都在内存里，缓冲由 NAudio 持有（不再用 winmm 的裸指针 SND_MEMORY）。
/// 播放失败（无音频设备 / 系统限制）时静默降级。
/// </para>
/// </summary>
internal sealed class AudioMixer : IDisposable
{
    private readonly object _gate = new();
    private IWavePlayer? _output;
    private MixingSampleProvider? _mixer;
    private LoopSampleProvider? _bgm;
    private bool _broken;
    private bool _disposed;

    private void EnsureStartedLocked()
    {
        if (_broken || _disposed || _output is not null)
        {
            return;
        }

        try
        {
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(MixerSampleRate, 2))
            {
                ReadFully = true,
            };

            // 优先 WASAPI 共享模式（Win10/11 的现代栈）；设备不支持时回退 WaveOut。
            // 两条路径都由 NAudio 自己持缓冲，不再有 winmm SND_MEMORY 裸指针问题。
            try
            {
                var wasapi = new WasapiOut(AudioClientShareMode.Shared, 100);
                wasapi.Init(_mixer);
                wasapi.Play();
                _output = wasapi;
            }
            catch
            {
                var waveOut = new WaveOutEvent
                {
                    DesiredLatency = 120,
                    NumberOfBuffers = 3,
                };
                waveOut.Init(_mixer);
                waveOut.Play();
                _output = waveOut;
            }
        }
        catch
        {
            // 没有音频设备：后续调用全部静默
            _broken = true;
            try
            {
                _output?.Dispose();
            }
            catch
            {
                // 忽略
            }

            _output = null;
            _mixer = null;
        }
    }


    /// <summary>
    /// 统一到混音器格式（44100 Hz 立体声）：合成音是 22050/24000 单声道，
    /// 不转换会被按输出采样率直接读——表现为「变快 + 升调」。
    /// </summary>
    internal static ISampleProvider ToMixerFormat(ISampleProvider input)
    {
        ISampleProvider provider = input;
        if (provider.WaveFormat.SampleRate != MixerSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, MixerSampleRate);
        }

        if (provider.WaveFormat.Channels == 1)
        {
            provider = new MonoToStereoSampleProvider(provider);
        }

        return provider;
    }

    private const int MixerSampleRate = 44100;

    /// <summary>播一个短音效（WAV 字节，已是目标音量）。</summary>
    public void PlayOneShot(byte[] wav)
    {
        lock (_gate)
        {
            try
            {
                EnsureStartedLocked();
                if (_mixer is null)
                {
                    return;
                }

                var reader = new WaveFileReader(new MemoryStream(wav, writable: false));
                var provider = new DisposingSampleProvider(reader, ToMixerFormat(reader.ToSampleProvider()));
                _mixer.AddMixerInput(provider);
            }
            catch
            {
                // 静默降级
            }
        }
    }

    /// <summary>循环播 BGM；已有一首在播时先停旧的。</summary>
    public void PlayBgm(byte[] wav, float volume)
    {
        lock (_gate)
        {
            try
            {
                EnsureStartedLocked();
                if (_mixer is null)
                {
                    return;
                }

                StopBgmLocked();

                var reader = new WaveFileReader(new MemoryStream(wav, writable: false));
                var loop = new LoopSampleProvider(ToMixerFormat(reader.ToSampleProvider()), reader);
                var volumeProvider = new VolumeSampleProvider(loop) { Volume = volume };
                _mixer.AddMixerInput(volumeProvider);
                _bgm = loop;
                _bgmVolume = volumeProvider;
            }
            catch
            {
                // 静默降级
            }
        }
    }

    private VolumeSampleProvider? _bgmVolume;

    /// <summary>改 BGM 音量（不重启、不掐断）。</summary>
    public void SetBgmVolume(float volume)
    {
        lock (_gate)
        {
            if (_bgmVolume is not null)
            {
                _bgmVolume.Volume = volume;
            }
        }
    }

    /// <summary>停 BGM。</summary>
    public void StopBgm()
    {
        lock (_gate)
        {
            StopBgmLocked();
        }
    }

    private void StopBgmLocked()
    {
        if (_bgm is null)
        {
            return;
        }

        try
        {
            _mixer?.RemoveMixerInput(_bgmVolume!);
            _bgm.Dispose();
        }
        catch
        {
            // 忽略
        }

        _bgm = null;
        _bgmVolume = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopBgmLocked();
            try
            {
                _output?.Stop();
                _output?.Dispose();
            }
            catch
            {
                // 忽略
            }

            _output = null;
            _mixer = null;
        }
    }

    /// <summary>播完自动释放底层 reader 的一次性 sample provider。</summary>
    private sealed class DisposingSampleProvider : ISampleProvider
    {
        private readonly WaveFileReader _reader;
        private readonly ISampleProvider _inner;
        private bool _done;

        public DisposingSampleProvider(WaveFileReader reader, ISampleProvider inner)
        {
            _reader = reader;
            _inner = inner;
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            if (read == 0 && !_done)
            {
                _done = true;
                try
                {
                    _reader.Dispose();
                }
                catch
                {
                    // 忽略
                }
            }

            return read;
        }
    }

    /// <summary>把短 WAV 循环起来（读到头就回到开头）。</summary>
    private sealed class LoopSampleProvider : ISampleProvider, IDisposable
    {
        private readonly WaveFileReader _reader;
        private readonly ISampleProvider _inner;

        public LoopSampleProvider(ISampleProvider inner, WaveFileReader reader)
        {
            _inner = inner;
            _reader = reader;
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _inner.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    try
                    {
                        _reader.Position = 0;
                    }
                    catch
                    {
                        return total;
                    }

                    read = _inner.Read(buffer, offset + total, count - total);
                    if (read == 0)
                    {
                        return total;
                    }
                }

                total += read;
            }

            return total;
        }

        public void Dispose()
        {
            try
            {
                _reader.Dispose();
            }
            catch
            {
                // 忽略
            }
        }
    }
}
