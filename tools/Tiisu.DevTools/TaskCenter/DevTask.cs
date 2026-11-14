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
    bool Confirm = false);

/// <summary>发布任务的实际动作，由 DevToolsForm 注入，方便测试注册表。</summary>
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
}
