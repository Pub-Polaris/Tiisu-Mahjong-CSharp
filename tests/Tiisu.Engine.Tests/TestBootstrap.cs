using System.Runtime.CompilerServices;
using Tiisu.Localization;

namespace Tiisu.Engine.Tests;

internal static class TestBootstrap
{
    /// <summary>引擎内部用 L.T 输出文案；测试进程里先显式加载语言目录（等价 App 启动时的 SetLanguage）。</summary>
    [ModuleInitializer]
    internal static void Initialize() => L.SetLanguage(L.Language);
}
