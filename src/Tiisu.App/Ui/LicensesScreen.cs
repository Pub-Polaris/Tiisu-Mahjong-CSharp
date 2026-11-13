using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>菜单「开源信息」：逐页列出组件许可，支持查看全文与闭源参考鸣谢。</summary>
public sealed class LicensesScreen : Screen
{
    private sealed record Entry(string Name, string Version, string License, string Usage, string Url, bool Reference = false);

    private static readonly Entry[] Entries =
    {
        new("Tiisu Rounded SC（文渊圆体子集）", "v1.110", "SIL OFL 1.1",
            "界面圆体；子集化后重命名，避免 OFL 保留字体名",
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
            "本机 Windows SAPI 报牌语音生成",
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
        new("音效与语音素材", "本仓合成/本机生成", "随项目 MIT",
            "音效由 AudioGen 合成；语音本机 SAPI 生成，不随包分发",
            "—"),
        new("Tiisu Mahjong 本项目代码", "见版本行", "MIT",
            "仓库内自有代码与文档",
            "https://github.com/Pub-Polaris/Tiisu-Mahjong-CSharp"),
        new("Alice in Cradle", "闭源商业作品", "EULA（仅参考）",
            "空间音频架构调研；未使用代码、中间件、素材或衍生产物",
            "—", true),
        new("明星三缺一 2002", "IGS 商业作品", "EULA（仅参考）",
            "音效包络/时长/密度特征分析；未使用原音频、代码或美术",
            "—", true),
        new("天凤 Tenhou", "在线服务", "服务条款（仅参考）",
            "人类听牌形参考；未使用代码、数据或素材",
            "—", true),
    };

    private const int TextLinesPerPage = 24;
    private const int TextCharsPerLine = 100;

    private readonly Action _back;
    private int _page;
    private bool _textMode;
    private string _textTitle = string.Empty;
    private string[] _textAllLines = Array.Empty<string>();
    private int _textPage;
    private int _textPages = 1;

    private RectF _prevButton;
    private RectF _nextButton;
    private RectF _backButton;
    private RectF _textButton;
    private bool _prevHover;
    private bool _nextHover;
    private bool _backHover;
    private bool _textHover;

    public LicensesScreen(Action back) : base("licenses")
    {
        _back = back;
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        if (_textMode)
        {
            DrawTextMode(c);
            return;
        }

        c.DrawText("开源许可与参考鸣谢", new RectF(0, 54, Width, 56),
            Color.Rgb(242, 230, 190), new TextStyle(42, Bold: true, Center: true));
        c.DrawText("开源组件 + 闭源参考项目 · 第 " + (_page + 1) + " / " + Entries.Length + " 项",
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
        int bodyCount = entry.Reference ? lines.Length + 1 : lines.Length;
        float boxH = 14f + 28f + bodyCount * lineH + 10f;
        var box = new RectF(150, y, Width - 300, boxH);
        c.FillRoundRect(box, 14, Color.Rgb(26, 38, 34));
        c.StrokeRoundRect(box, 14, Color.Rgb(96, 122, 108), 1.2f);

        c.DrawText((entry.Reference ? "参考 · " : string.Empty) + entry.Name,
            new RectF(box.X + 26, box.Y + 10, box.W - 52, 28),
            entry.Reference ? Color.Rgb(196, 232, 190) : Color.Rgb(242, 201, 76),
            new TextStyle(20, Bold: true, Center: false));

        string[] body = entry.Reference
            ? new[] { "性质：闭源/EULA 参考鸣谢，未使用其代码或素材" }.Concat(lines).ToArray()
            : lines;
        float ly = box.Y + 42;
        foreach (string line in body)
        {
            c.DrawText("· " + line, new RectF(box.X + 26, ly, box.W - 52, lineH),
                Color.Rgb(220, 228, 220), new TextStyle(16, Center: false));
            ly += lineH;
        }

        c.DrawText("按 ← / → 翻页 · Esc 返回菜单",
            new RectF(0, 558, Width, 22), Color.Rgb(127, 145, 137), new TextStyle(14, Center: true));

        _textButton = new RectF(Width / 2f - 140, 510, 280, 40);
        DrawButton(c, _textButton, "查看本页许可全文", _textHover, true);

        _prevButton = new RectF(Width / 2f - 330, 596, 150, 44);
        _backButton = new RectF(Width / 2f - 75, 596, 150, 44);
        _nextButton = new RectF(Width / 2f + 120, 596, 150, 44);
        DrawButton(c, _prevButton, "上一页", _prevHover, _page > 0);
        DrawButton(c, _backButton, "返回菜单", _backHover, true);
        DrawButton(c, _nextButton, "下一页", _nextHover, _page < Entries.Length - 1);
    }

    private void DrawTextMode(ICanvas c)
    {
        c.DrawText(_textTitle, new RectF(0, 40, Width, 48),
            Color.Rgb(242, 230, 190), new TextStyle(34, Bold: true, Center: true));
        c.DrawText("许可全文 · 第 " + (_textPage + 1) + " / " + _textPages + " 页",
            new RectF(0, 88, Width, 24), Color.Rgb(159, 180, 170), new TextStyle(15, Center: true));

        var box = new RectF(90, 122, Width - 180, 480);
        c.FillRoundRect(box, 14, Color.Rgb(20, 30, 26));
        c.StrokeRoundRect(box, 14, Color.Rgb(80, 102, 90), 1.2f);

        int start = _textPage * TextLinesPerPage;
        for (int i = 0; i < TextLinesPerPage && start + i < _textAllLines.Length; i++)
        {
            c.DrawText(_textAllLines[start + i],
                new RectF(box.X + 22, box.Y + 14 + i * 18, box.W - 44, 18),
                Color.Rgb(216, 224, 214), new TextStyle(13, Center: false));
        }

        _prevButton = new RectF(Width / 2f - 270, 620, 150, 44);
        _backButton = new RectF(Width / 2f - 75, 620, 150, 44);
        _nextButton = new RectF(Width / 2f + 120, 620, 150, 44);
        DrawButton(c, _prevButton, "上一页", _prevHover, _textPage > 0);
        DrawButton(c, _backButton, "返回列表", _backHover, true);
        DrawButton(c, _nextButton, "下一页", _nextHover, _textPage < _textPages - 1);
    }

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
        if (_textMode)
        {
            if (_backButton.Contains(x, y))
            {
                _textMode = false;
                return true;
            }

            if (_prevButton.Contains(x, y) && _textPage > 0)
            {
                _textPage--;
                return true;
            }

            if (_nextButton.Contains(x, y) && _textPage < _textPages - 1)
            {
                _textPage++;
                return true;
            }

            return false;
        }

        if (_backButton.Contains(x, y))
        {
            _back();
            return true;
        }

        if (_textButton.Contains(x, y))
        {
            OpenText();
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
        _prevHover = (_textMode ? _textPage > 0 : _page > 0) && _prevButton.Contains(x, y);
        _nextHover = (_textMode ? _textPage < _textPages - 1 : _page < Entries.Length - 1) && _nextButton.Contains(x, y);
        _backHover = _backButton.Contains(x, y);
        _textHover = !_textMode && _textButton.Contains(x, y);
        return _prevHover || _nextHover || _backHover || _textHover;
    }

    public override bool OnKey(Keys key)
    {
        if (key == Keys.Escape)
        {
            if (_textMode)
            {
                _textMode = false;
            }
            else
            {
                _back();
            }

            return true;
        }

        if (key is Keys.Left or Keys.PageUp)
        {
            if (_textMode)
            {
                if (_textPage > 0)
                {
                    _textPage--;
                }
            }
            else if (_page > 0)
            {
                _page--;
            }

            return true;
        }

        if (key is Keys.Right or Keys.PageDown)
        {
            if (_textMode)
            {
                if (_textPage < _textPages - 1)
                {
                    _textPage++;
                }
            }
            else if (_page < Entries.Length - 1)
            {
                _page++;
            }

            return true;
        }

        return false;
    }

    private void OpenText()
    {
        var entry = Entries[_page];
        _textTitle = entry.Name + " · " + entry.License;
        string file = entry.Reference
            ? "REFERENCES.txt"
            : entry.License.Contains("OFL", StringComparison.Ordinal) ? "OFL-1.1.txt"
            : entry.License.Contains("Apache", StringComparison.Ordinal) ? "Apache-2.0.txt"
            : entry.License.Contains("CC BY", StringComparison.Ordinal) ? "CC-BY-4.0.txt"
            : "MIT.txt";

        string text;
        try
        {
            text = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "Assets", "licenses", file));
        }
        catch
        {
            text = "未找到许可全文：" + file;
        }

        var wrapped = new List<string>();
        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
            {
                wrapped.Add(string.Empty);
                continue;
            }

            for (int i = 0; i < line.Length; i += TextCharsPerLine)
            {
                wrapped.Add(line.Substring(i, Math.Min(TextCharsPerLine, line.Length - i)));
            }
        }

        _textAllLines = wrapped.ToArray();
        _textPages = Math.Max(1, (int)Math.Ceiling(_textAllLines.Length / (double)TextLinesPerPage));
        _textPage = 0;
        _textMode = true;
    }
}
