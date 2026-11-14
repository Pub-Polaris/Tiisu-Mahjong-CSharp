using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tiisu.DevTools.TaskCenter;

/// <summary>任务中心的一条可执行任务；第一轮只带固定动作，参数表单后续再加。</summary>
internal sealed record DevTask(
    string Id,
    string Category,
    string Title,
    string Summary,
    string OutputHint,
    Func<Task> Run,
    bool Confirm = false,
    Func<string?>? OutputPath = null);

/// <summary>发布任务的实际动作，由 DevToolsForm 注入，方便测试注册表。</summary>
internal sealed record RenderTaskActions(
    Func<string> OutputDirectory,
    Func<string, string, Task> RenderScreen,
    Func<string, Task> RenderRoundEnd,
    Func<string, Task> RenderActions,
    Func<string, Task> ExportUiSvg,
    Func<string, Task> RenderTableStyle,
    Func<Task> OpenOutput);

internal sealed record AiTaskActions(
    Func<Task> ExportSelfplay,
    Func<Task> SmokeNeural,
    Func<Task> Smoke7Neuu,
    Func<Task> BenchSnapshot,
    Func<Task> OpenDataDir);

internal sealed record PublishTaskActions(
    Func<Task> Check,
    Func<Task> BuildInstaller,
    Func<Task> BuildRules,
    Func<Task> CopyHashes,
    Func<Task> OpenDist,
    Func<Task> OpenSetup);

internal static class DevTaskRegistry
{
    public static readonly IReadOnlyList<string> Categories = new[]
    {
        "发布",
        "出图",
        "审局",
        "AI 数据",
        "资源",
    };

    public const string PublishCategory = "发布";

    public static IReadOnlyList<DevTask> Publish(PublishTaskActions actions) => new[]
    {
        new DevTask(
            "publish.check",
            PublishCategory,
            "发布自检",
            "检查 dist/publish 的许可、字体、资源哈希与禁带项。",
            "无文件产物；结果看日志",
            actions.Check),
        new DevTask(
            "publish.installer",
            PublishCategory,
            "生成安装包",
            "重新 publish 并打包 Setup，复制到桌面。",
            "TiisuMahjong-Setup.exe",
            actions.BuildInstaller,
            Confirm: true),
        new DevTask(
            "publish.rules",
            PublishCategory,
            "生成规则库",
            "生成规则库独立 zip，供外部调用方引用。",
            "Tiisu.Rules.*.zip",
            actions.BuildRules),
        new DevTask(
            "publish.hashes",
            PublishCategory,
            "复制产物哈希",
            "把 dist 下文件的 SHA256 写进日志，方便发版说明。",
            "日志中的 SHA256",
            actions.CopyHashes),
        new DevTask(
            "publish.open-dist",
            PublishCategory,
            "打开 dist",
            "定位安装包与规则库的输出目录。",
            "资源管理器",
            actions.OpenDist),
        new DevTask(
            "publish.open-setup",
            PublishCategory,
            "打开桌面安装包",
            "定位桌面上的 TiisuMahjong-Setup.exe。",
            "资源管理器",
            actions.OpenSetup),
    };

    public static IReadOnlyList<DevTask> Render(RenderTaskActions actions)
    {
        string PathFor(string name) => System.IO.Path.Combine(actions.OutputDirectory(), name);
        Func<string?> Output(string name) => () => PathFor(name);

        return new[]
        {
            new DevTask("render.table", "出图", "牌桌截图",
                "当前牌桌布局与手牌，用于检查视觉回归。", "screen-table.png",
                () => actions.RenderScreen("table", PathFor("screen-table.png")), OutputPath: Output("screen-table.png")),
            new DevTask("render.menu", "出图", "主菜单截图",
                "菜单标题、模式按钮与底部署名。", "screen-menu.png",
                () => actions.RenderScreen("menu", PathFor("screen-menu.png")), OutputPath: Output("screen-menu.png")),
            new DevTask("render.settings", "出图", "设置页截图",
                "设置页分组与滑条，检查文字是否溢出。", "screen-settings.png",
                () => actions.RenderScreen("settings", PathFor("screen-settings.png")), OutputPath: Output("screen-settings.png")),
            new DevTask("render.tutorial", "出图", "新手引导截图",
                "新手引导三页的第一页。", "screen-tutorial.png",
                () => actions.RenderScreen("tutorial", PathFor("screen-tutorial.png")), OutputPath: Output("screen-tutorial.png")),
            new DevTask("render.markdown", "出图", "参考文档截图",
                "Markdown 渲染器的参考文献页。", "screen-markdown.png",
                () => actions.RenderScreen("markdown", PathFor("screen-markdown.png")), OutputPath: Output("screen-markdown.png")),
            new DevTask("render.roundend", "出图", "结算页截图",
                "单家和牌结算布局；不跑游戏。", "roundend.png",
                () => actions.RenderRoundEnd(PathFor("roundend.png")), OutputPath: Output("roundend.png")),
            new DevTask("render.actions", "出图", "按钮三态图",
                "动作按钮 normal / hover / pressed 对照。", "btn-colors.png",
                () => actions.RenderActions(PathFor("btn-colors.png")), OutputPath: Output("btn-colors.png")),
            new DevTask("render.ui-svg", "出图", "导出合并 SVG",
                "UI 图形合并为可编辑 SVG。", "ui-merged.svg",
                () => actions.ExportUiSvg(PathFor("ui-merged.svg")), OutputPath: Output("ui-merged.svg")),
            new DevTask("render.style", "出图", "牌桌样式：平面",
                "当前唯一的 Flat 牌桌出图。", "style-flat.png",
                () => actions.RenderTableStyle(PathFor("style-flat.png")), OutputPath: Output("style-flat.png")),
            new DevTask("render.open-output", "出图", "打开出图目录",
                "定位本次出图的输出目录。", "资源管理器",
                actions.OpenOutput),
        };
    }


    public static IReadOnlyList<DevTask> Ai(AiTaskActions actions) => new[]
    {
        new DevTask("ai.export-selfplay", "AI 数据", "导出自博弈数据",
            "跑自博弈并导出弃牌样本 JSONL；参数在弹窗里选。", "selfplay-*.jsonl",
            actions.ExportSelfplay, Confirm: true),
        new DevTask("ai.smoke-neural", "AI 数据", "验证 7Neuu 权重",
            "检查 v4 权重能否加载并给出合法弃牌。", "日志：PASS / FAIL",
            actions.SmokeNeural),
        new DevTask("ai.smoke-7neuu", "AI 数据", "验证 7Neuu 决策",
            "整局跑一遍，确认 7Neuu 座位真的走小策略网。", "日志：PASS / FAIL",
            actions.Smoke7Neuu),
        new DevTask("ai.bench-snapshot", "AI 数据", "UI 快照性能",
            "用真实引擎状态重复构建 UI 快照，输出耗时/分配。", "日志：us/iter",
            actions.BenchSnapshot),
        new DevTask("ai.open-data", "AI 数据", "打开数据目录",
            "定位自博弈 JSONL 的输出目录。", "资源管理器",
            actions.OpenDataDir),
    };

}
