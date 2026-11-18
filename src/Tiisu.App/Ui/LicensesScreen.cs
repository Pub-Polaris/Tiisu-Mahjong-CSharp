using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.Localization;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>菜单「开源信息」：逐页列出组件许可，支持查看全文与闭源参考鸣谢。</summary>
public sealed class LicensesScreen : Screen
{
    private sealed record Entry(string Name, string Version, string License, string Usage, string Url, bool Reference = false);

    private static readonly Entry[] Entries =
    {
        new("app.ui.licenses.name.tiisuRoundedScWenyuanSubset", "v1.110", "SIL OFL 1.1",
            "app.ui.licenses.usage.tiisuRoundedScWenyuanSubset", "https://github.com/takushun-wu/WenYuanFonts"),
        new("app.ui.licenses.name.lietxiaMahjongGraphicTiles", "3e27580", "app.ui.licenses.license.lietxiaMahjongGraphicTiles",
            "app.ui.licenses.usage.lietxiaMahjongGraphicTiles", "https://github.com/lietxia/mahjong_graphic"),
        new("SkiaSharp", "4.153.1", "MIT",
            "app.ui.licenses.usage.skiasharp", "https://github.com/mono/SkiaSharp"),
        new("SkiaSharp.NativeAssets.Win32", "4.153.1", "MIT",
            "app.ui.licenses.usage.skiasharpNativeassetsWin32", "https://github.com/mono/SkiaSharp"),
        new("SkiaSharp.Views.WindowsForms", "4.153.1", "MIT",
            "app.ui.licenses.usage.skiasharpViewsWindowsforms", "https://github.com/mono/SkiaSharp"),
        new("SkiaSharp.HarfBuzz", "4.153.1", "MIT",
            "app.ui.licenses.usage.skiasharpHarfbuzz", "https://github.com/mono/SkiaSharp"),
        new("HarfBuzz", "app.ui.licenses.version.harfbuzz", "MIT",
            "app.ui.licenses.usage.harfbuzz", "https://github.com/harfbuzz/harfbuzz"),
        new("NAudio", "2.2.1", "MIT",
            "app.ui.licenses.usage.naudio", "https://github.com/naudio/NAudio"),
        new("NAudio.Asio / Core / Midi / Wasapi / WinForms / WinMM", "2.2.1", "MIT",
            "app.ui.licenses.usage.naudioAsioCoreMidiWasapiWinformsWinmm", "https://github.com/naudio/NAudio"),
        new("HarfBuzzSharp / NativeAssets.Win32", "14.2.1.301", "MIT",
            "app.ui.licenses.usage.harfbuzzsharpNativeassetsWin32", "https://github.com/mono/SkiaSharp"),
        new("OpenTK", "4.8.2", "MIT",
            "app.ui.licenses.usage.opentk", "https://github.com/opentk/opentk"),
        new("OpenTK.GLControl", "4.0.1", "MIT",
            "app.ui.licenses.usage.opentkGlcontrol", "https://github.com/opentk/opentk"),
        new("GLFW（OpenTK.redist.glfw）", "3.3.8.39", "Zlib/libpng",
            "app.ui.licenses.usage.glfwOpentkRedistGlfw", "https://www.glfw.org/"),
        new("YogaSharp.Core / SkiaSharp.Views.Desktop.Common", "1.0.6 / 4.153.1", "MIT",
            "app.ui.licenses.usage.yogasharpCoreSkiasharpViewsDesktopCommon", "https://github.com/RaZeR-RaY/YogaSharp"),
        new("YogaSharp.Layout", "1.0.6", "MIT",
            "app.ui.licenses.usage.yogasharpLayout", "https://github.com/RaZeR-RaY/YogaSharp"),
        new("Yoga", "app.ui.licenses.version.yoga", "MIT",
            "app.ui.licenses.usage.yoga", "https://github.com/facebook/yoga"),
        new(".NET / Windows Desktop Runtime", "10", "MIT",
            "app.ui.licenses.usage.netWindowsDesktopRuntime", "https://github.com/dotnet/runtime"),
        new("System.Speech", "9.0.0", "MIT",
            "app.ui.licenses.usage.systemSpeech", "https://github.com/dotnet/runtime"),
        new("xUnit", "2.9.3", "Apache-2.0",
            "app.ui.licenses.usage.xunit", "https://xunit.net/"),
        new("xunit.runner.visualstudio", "3.1.4", "Apache-2.0",
            "app.ui.licenses.usage.xunitRunnerVisualstudio", "https://xunit.net/"),
        new("Microsoft.NET.Test.Sdk", "17.14.1", "MIT",
            "app.ui.licenses.usage.microsoftNetTestSdk", "https://github.com/microsoft/vstest"),
        new("coverlet.collector", "6.0.4", "MIT",
            "app.ui.licenses.usage.coverletCollector", "https://github.com/coverlet-coverage/coverlet"),
        new("Parisian", "Kevin MacLeod", "CC BY 4.0",
            "app.ui.licenses.usage.parisian", "https://incompetech.com/music/royalty-free/"),
        new("app.ui.licenses.name.audioVoiceAssets", "app.ui.licenses.version.audioVoiceAssets", "app.ui.licenses.license.audioVoiceAssets",
            "app.ui.licenses.usage.audioVoiceAssets", "—"),
        new("app.ui.licenses.name.tiisuMahjongSource", "app.ui.licenses.version.tiisuMahjongSource", "MIT",
            "app.ui.licenses.usage.tiisuMahjongSource", "https://github.com/Pub-Polaris/Tiisu-Mahjong-CSharp"),
        new("Alice in Cradle", "app.ui.licenses.version.aliceInCradle", "app.ui.licenses.license.aliceInCradle",
            "app.ui.licenses.usage.aliceInCradle", "—", true),
        new("app.ui.licenses.name.starMahjong2002", "app.ui.licenses.version.starMahjong2002", "app.ui.licenses.license.starMahjong2002",
            "app.ui.licenses.usage.starMahjong2002", "—", true),
        new("app.ui.licenses.name.tenhou", "app.ui.licenses.version.tenhou", "app.ui.licenses.license.tenhou",
            "app.ui.licenses.usage.tenhou", "—", true),
    };

    private const int TextLinesPerPage = 24;
    private const int TextCharsPerLine = 100;

    private readonly Action _back;
    private readonly Action<string>? _openMarkdown;
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
    private RectF _docsButton;
    private bool _prevHover;
    private bool _nextHover;
    private bool _backHover;
    private bool _textHover;
    private bool _docsHover;

    public LicensesScreen(Action back, Action<string>? openMarkdown = null) : base("licenses")
    {
        _back = back;
        _openMarkdown = openMarkdown;
    }

    public override void OnLanguageChanged()
    {
        _textMode = false;
        _textTitle = string.Empty;
        _textAllLines = Array.Empty<string>();
        _textPage = 0;
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

        c.DrawText(L.T("app.ui.licenses.title"), new RectF(0, 54, Width, 56),
            Color.Rgb(242, 230, 190), new TextStyle(42, Bold: true, Center: true));
        c.DrawText(L.T("app.ui.licenses.pageIndicator", ("current", _page + 1), ("total", Entries.Length)),
            new RectF(0, 108, Width, 26), Color.Rgb(159, 180, 170), new TextStyle(16, Center: true));

        var entry = Entries[_page];
        string[] lines =
        {
            L.T("app.ui.licenses.line.version", ("value", Text(entry.Version))),
            L.T("app.ui.licenses.line.license", ("value", Text(entry.License))),
            L.T("app.ui.licenses.line.usage", ("value", Text(entry.Usage))),
            L.T("app.ui.licenses.line.link", ("value", entry.Url)),
        };

        float y = 156;
        const float lineH = 34f;
        int bodyCount = entry.Reference ? lines.Length + 1 : lines.Length;
        float boxH = 14f + 28f + bodyCount * lineH + 10f;
        var box = new RectF(150, y, Width - 300, boxH);
        c.FillRoundRect(box, 14, Color.Rgb(26, 38, 34));
        c.StrokeRoundRect(box, 14, Color.Rgb(96, 122, 108), 1.2f);

        c.DrawText((entry.Reference ? L.T("app.ui.licenses.reference") + " · " : string.Empty) + Text(entry.Name),
            new RectF(box.X + 26, box.Y + 10, box.W - 52, 28),
            entry.Reference ? Color.Rgb(196, 232, 190) : Color.Rgb(242, 201, 76),
            new TextStyle(20, Bold: true, Center: false));

        string[] body = entry.Reference
            ? new[] { L.T("app.ui.licenses.referenceNote") }.Concat(lines).ToArray()
            : lines;
        float ly = box.Y + 42;
        foreach (string line in body)
        {
            c.DrawText("· " + line, new RectF(box.X + 26, ly, box.W - 52, lineH),
                Color.Rgb(220, 228, 220), new TextStyle(16, Center: false));
            ly += lineH;
        }

        c.DrawText(L.T("app.ui.licenses.footer"),
            new RectF(0, 558, Width, 22), Color.Rgb(127, 145, 137), new TextStyle(14, Center: true));

        _textButton = new RectF(Width / 2f - 140, 510, 280, 40);
        DrawButton(c, _textButton, "app.ui.licenses.viewText", _textHover, true);

        _docsButton = new RectF(Width / 2f - 140, 458, 280, 40);
        DrawButton(c, _docsButton, "app.ui.licenses.docs", _docsHover, _openMarkdown is not null);

        _prevButton = new RectF(Width / 2f - 330, 596, 150, 44);
        _backButton = new RectF(Width / 2f - 75, 596, 150, 44);
        _nextButton = new RectF(Width / 2f + 120, 596, 150, 44);
        DrawButton(c, _prevButton, "app.ui.licenses.prev", _prevHover, _page > 0);
        DrawButton(c, _backButton, "app.ui.licenses.back", _backHover, true);
        DrawButton(c, _nextButton, "app.ui.licenses.next", _nextHover, _page < Entries.Length - 1);
    }

    private void DrawTextMode(ICanvas c)
    {
        c.DrawText(_textTitle, new RectF(0, 40, Width, 48),
            Color.Rgb(242, 230, 190), new TextStyle(34, Bold: true, Center: true));
        c.DrawText(L.T("app.ui.licenses.textPageIndicator", ("current", _textPage + 1), ("total", _textPages)),
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
        DrawButton(c, _prevButton, "app.ui.licenses.prev", _prevHover, _textPage > 0);
        DrawButton(c, _backButton, "app.ui.licenses.backToList", _backHover, true);
        DrawButton(c, _nextButton, "app.ui.licenses.next", _nextHover, _textPage < _textPages - 1);
    }

    /// <summary>数据表里的显示串：app.* 走 L.T，其余（产品名 / 标准许可名）原样显示。</summary>
    private static string Text(string value) =>
        value.StartsWith("app.", StringComparison.Ordinal) ? L.T(value) : value;

    private static void DrawButton(ICanvas c, RectF rect, string label, bool hover, bool enabled)
    {
        var border = !enabled ? Color.Rgb(70, 84, 78) : hover ? Color.Rgb(242, 201, 76) : Color.Rgb(196, 168, 96);
        var fill = enabled
            ? hover ? Color.Rgb(52, 86, 78) : Color.Rgb(40, 58, 48)
            : Color.Rgb(28, 36, 32);
        c.FillRoundRect(rect, 10, fill);
        c.StrokeRoundRect(rect, 10, border, 1.3f);
        c.DrawText(L.T(label), rect, enabled ? Color.Rgb(242, 230, 190) : Color.Rgb(110, 124, 116),
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

        if (_docsButton.Contains(x, y) && _openMarkdown is not null)
        {
            _openMarkdown("REFERENCES.md");
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
        _docsHover = !_textMode && _docsButton.Contains(x, y);
        return _prevHover || _nextHover || _backHover || _textHover || _docsHover;
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
        _textTitle = Text(entry.Name) + " · " + Text(entry.License);
        string file = entry.Reference
            ? "REFERENCES.txt"
            : entry.Url.Contains("lietxia/mahjong_graphic", StringComparison.Ordinal) ? "TILES-LICENSE.txt"
            : entry.License.Contains("OFL", StringComparison.Ordinal) ? "OFL-1.1.txt"
            : entry.License.Contains("Apache", StringComparison.Ordinal) ? "Apache-2.0.txt"
            : entry.License.Contains("CC BY", StringComparison.Ordinal) ? "CC-BY-4.0.txt"
            : entry.License.Contains("Zlib", StringComparison.OrdinalIgnoreCase) ? "Zlib.txt"
            : "MIT.txt";

        string text;
        try
        {
            text = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "Assets", "licenses", file));
        }
        catch
        {
            text = L.T("app.ui.licenses.textNotFound", ("file", file));
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
