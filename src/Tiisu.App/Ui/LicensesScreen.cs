using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>菜单「开源信息」：逐页列出本项目使用的全部开源组件与素材许可。</summary>
public sealed class LicensesScreen : Screen
{
    private sealed record Entry(string Name, string Version, string License, string Usage, string Url);

    private static readonly Entry[] Entries =
    {
        new("文渊圆体 WenYuan Rounded SC", "v1.110", "SIL OFL 1.1",
            "规则速查页标题/正文；界面圆体（子集化可变字体）",
            "https://github.com/takushun-wu/WenYuanFonts"),
        new("SkiaSharp", "4.153.1", "MIT",
            "2D 绘制、牌面渲染、文字排版",
            "https://github.com/mono/SkiaSharp"),
        new("SkiaSharp.NativeAssets.Win32", "4.153.1", "MIT",
            "SkiaSharp 的 Windows 原生库",
            "https://github.com/mono/SkiaSharp"),
        new("SkiaSharp.Views.WindowsForms", "4.153.1", "MIT",
            "WinForms 上的 Skia 画布控件",
            "https://github.com/mono/SkiaSharp"),
        new("SkiaSharp.HarfBuzz", "4.153.1", "MIT",
            "文字整形（HarfBuzz 绑定）",
            "https://github.com/mono/SkiaSharp"),
        new("HarfBuzz", "随 SkiaSharp.HarfBuzz 分发", "MIT",
            "复杂文字整形引擎（随包原生库）",
            "https://github.com/harfbuzz/harfbuzz"),
        new("NAudio", "2.2.1", "MIT",
            "BGM / 音效混音与播放",
            "https://github.com/naudio/NAudio"),
        new("YogaSharp.Layout", "1.0.6", "MIT",
            "牌桌 Flex 布局绑定",
            "https://github.com/RaZeR-RaY/YogaSharp"),
        new("Yoga", "随 YogaSharp 分发", "MIT",
            "Facebook 的 Flexbox 布局引擎（随包 yoga.dll）",
            "https://github.com/facebook/yoga"),
        new(".NET / Windows Desktop Runtime", "10", "MIT",
            "运行时、WinForms 与基础库",
            "https://github.com/dotnet/runtime"),
        new("System.Speech", "9.0.0", "MIT",
            "Tiisu.AudioGen 的语音合成",
            "https://github.com/dotnet/runtime"),
        new("xUnit", "2.9.3", "Apache-2.0",
            "单元测试（仅开发构建）",
            "https://xunit.net/"),
        new("xunit.runner.visualstudio", "3.1.4", "Apache-2.0",
            "测试运行器（仅开发构建）",
            "https://xunit.net/"),
        new("Microsoft.NET.Test.Sdk", "17.14.1", "MIT",
            "测试宿主（仅开发构建）",
            "https://github.com/microsoft/vstest"),
        new("coverlet.collector", "6.0.4", "MIT",
            "代码覆盖率（仅开发构建）",
            "https://github.com/coverlet-coverage/coverlet"),
        new("Parisian", "Kevin MacLeod", "CC BY 4.0",
            "菜单 BGM（lobby.wav，已做音量调整）",
            "https://incompetech.com/music/royalty-free/"),
        new("音效与语音素材", "本仓合成", "随项目 MIT",
            "Assets/sounds、Assets/voices 由 Tiisu.AudioGen 合成，不含第三方录音",
            "—"),
        new("Tiisu Mahjong 本项目代码", "见版本行", "MIT",
            "仓库内自有代码与文档",
            "https://github.com/Pub-Polaris/Tiisu-Mahjong-CSharp"),
    };

    private readonly Action _back;
    private int _page;
    private RectF _prevButton;
    private RectF _nextButton;
    private RectF _backButton;
    private bool _prevHover;
    private bool _nextHover;
    private bool _backHover;

    public LicensesScreen(Action back) : base("licenses")
    {
        _back = back;
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText("开源许可声明", new RectF(0, 54, Width, 56),
            Color.Rgb(242, 230, 190), new TextStyle(42, Bold: true, Center: true));
        c.DrawText("本项目使用的全部开源组件与素材 · 第 " + (_page + 1) + " / " + Entries.Length + " 项",
            new RectF(0, 108, Width, 26), Color.Rgb(159, 180, 170), new TextStyle(16, Center: true));

        var entry = Entries[_page];
        string[] lines =
        {
            "版本：" + entry.Version,
            "许可：" + entry.License,
            "用途：" + entry.Usage,
            "链接：" + entry.Url,
        };

        float y = 156;
        const float lineH = 34f;
        float boxH = 14f + 28f + lines.Length * lineH + 10f;
        var box = new RectF(150, y, Width - 300, boxH);
        c.FillRoundRect(box, 14, Color.Rgb(26, 38, 34));
        c.StrokeRoundRect(box, 14, Color.Rgb(96, 122, 108), 1.2f);

        c.DrawText(entry.Name, new RectF(box.X + 26, box.Y + 10, box.W - 52, 28),
            Color.Rgb(242, 201, 76), new TextStyle(20, Bold: true, Center: false));

        float ly = box.Y + 42;
        foreach (string line in lines)
        {
            c.DrawText("· " + line, new RectF(box.X + 26, ly, box.W - 52, lineH),
                Color.Rgb(220, 228, 220), new TextStyle(16, Center: false));
            ly += lineH;
        }

        c.DrawText("按 ← / → 翻页 · Esc 返回菜单",
            new RectF(0, 558, Width, 22), Color.Rgb(127, 145, 137), new TextStyle(14, Center: true));

        _prevButton = new RectF(Width / 2f - 270, 596, 150, 44);
        _backButton = new RectF(Width / 2f - 75, 596, 150, 44);
        _nextButton = new RectF(Width / 2f + 120, 596, 150, 44);

        DrawButton(c, _prevButton, "上一页", _prevHover, _page > 0);
        DrawButton(c, _backButton, "返回菜单", _backHover, true);
        DrawButton(c, _nextButton, "下一页", _nextHover, _page < Entries.Length - 1);
    }

    /// <summary>与规则速查页同一个按钮规格。</summary>
    private static void DrawButton(ICanvas c, RectF rect, string label, bool hover, bool enabled)
    {
        var border = !enabled ? Color.Rgb(70, 84, 78) : hover ? Color.Rgb(242, 201, 76) : Color.Rgb(196, 168, 96);
        var fill = enabled
            ? hover ? Color.Rgb(52, 86, 78) : Color.Rgb(40, 58, 48)
            : Color.Rgb(28, 36, 32);
        c.FillRoundRect(rect, 10, fill);
        c.StrokeRoundRect(rect, 10, border, 1.3f);
        c.DrawText(label, rect, enabled ? Color.Rgb(242, 230, 190) : Color.Rgb(110, 124, 116),
            new TextStyle(17, Bold: true, Center: true));
    }

    public override bool OnClick(float x, float y)
    {
        if (_backButton.Contains(x, y))
        {
            _back();
            return true;
        }

        if (_prevButton.Contains(x, y) && _page > 0)
        {
            _page--;
            return true;
        }

        if (_nextButton.Contains(x, y) && _page < Entries.Length - 1)
        {
            _page++;
            return true;
        }

        return false;
    }

    public override bool OnPointerMove(float x, float y)
    {
        _prevHover = _page > 0 && _prevButton.Contains(x, y);
        _nextHover = _page < Entries.Length - 1 && _nextButton.Contains(x, y);
        _backHover = _backButton.Contains(x, y);
        return _prevHover || _nextHover || _backHover;
    }

    public override bool OnKey(Keys key)
    {
        switch (key)
        {
            case Keys.Escape:
                _back();
                return true;
            case Keys.Left:
            case Keys.PageUp:
                if (_page > 0)
                {
                    _page--;
                }

                return true;
            case Keys.Right:
            case Keys.PageDown:
                if (_page < Entries.Length - 1)
                {
                    _page++;
                }

                return true;
            default:
                return false;
        }
    }
}
