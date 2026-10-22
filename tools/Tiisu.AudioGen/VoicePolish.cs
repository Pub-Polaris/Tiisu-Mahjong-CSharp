using System;

namespace Tiisu.AudioGen;

/// <summary>语音后处理：裁静音 → 140Hz 高通 → 软限幅 → 淡入淡出 → 峰值归一。</summary>
internal static class VoicePolish
{
    public static double[] Process(double[] data, int rate)
    {
        data = Trim(data, rate);

        double mean = 0;
        foreach (var v in data)
        {
            mean += v;
        }

        mean /= Math.Max(1, data.Length);

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

        for (int i = 0; i < hp.Length; i++)
        {
            hp[i] = Math.Tanh(hp[i] * 1.4) / 1.4;
        }

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

    private static double[] Trim(double[] data, int rate, double threshold = 0.01)
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
}
