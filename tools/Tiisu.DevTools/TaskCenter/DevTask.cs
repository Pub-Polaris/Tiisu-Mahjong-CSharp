using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tiisu.Localization;

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
    Func<Task> ExportSteamShots,
    Func<string?> SteamShotsPath,
    Func<Task> ExportSteamCapsules,
    Func<string?> SteamCapsulePath,
    Func<Task> OpenOutput);

internal sealed record AuditTaskActions(
    Func<Task> AuditWithDialog,
    Func<Task> AuditUi,
    Func<Task> AuditCall,
    Func<Task> SmokeRun);

internal sealed record ResourceTaskActions(
    Func<Task> CheckFont,
    Func<Task> AuditUiStrings,
    Func<Task> ExportAtlas,
    Func<string?> AtlasPath,
    Func<Task> OpenAssets,
    Func<Task> OpenDocs);

internal sealed record AiTaskActions(
    Func<Task> ExportSelfplay,
    Func<Task> SmokeNeural,
    Func<Task> Smoke7Neuu,
    Func<Task> BenchSnapshot,
    Func<Task> OpenDataDir);

internal sealed record PublishTaskActions(
    Func<Task> Check,
    Func<Task> CheckSteam,
    Func<Task> BuildInstaller,
    Func<Task> BuildSteamDepot,
    Func<Task> BuildRules,
    Func<Task> CopyHashes,
    Func<Task> OpenDist,
    Func<Task> OpenSetup);

internal static class DevTaskRegistry
{
    /// <summary>分类 id（稳定标识，不随语言变化）；显示名走 tools.devtools.category.*。</summary>
    public static readonly IReadOnlyList<string> Categories = new[]
    {
        PublishCategory,
        "render",
        "audit",
        "ai",
        "resource",
    };

    public const string PublishCategory = "publish";

    public static IReadOnlyList<DevTask> Publish(PublishTaskActions actions) => new[]
    {
        new DevTask(
            "publish.check",
            PublishCategory,
            L.T("tools.devtools.taskc.publish.check.title"),
            L.T("tools.devtools.taskc.publish.check.summary"),
            L.T("tools.devtools.hint.none"),
            actions.Check),
        new DevTask(
            "publish.steam-check",
            PublishCategory,
            L.T("tools.devtools.taskc.publish.steam-check.title"),
            L.T("tools.devtools.taskc.publish.steam-check.summary"),
            L.T("tools.devtools.hint.logReadyBlocked"),
            actions.CheckSteam),
        new DevTask(
            "publish.installer",
            PublishCategory,
            L.T("tools.devtools.taskc.publish.installer.title"),
            L.T("tools.devtools.taskc.publish.installer.summary"),
            "TiisuMahjong-Setup.exe",
            actions.BuildInstaller,
            Confirm: true),
        new DevTask(
            "publish.steam",
            PublishCategory,
            L.T("tools.devtools.taskc.publish.steam.title"),
            L.T("tools.devtools.taskc.publish.steam.summary"),
            "dist/steam/depot + depot-manifest.json",
            actions.BuildSteamDepot),
        new DevTask(
            "publish.rules",
            PublishCategory,
            L.T("tools.devtools.taskc.publish.rules.title"),
            L.T("tools.devtools.taskc.publish.rules.summary"),
            "Tiisu.Rules.*.zip",
            actions.BuildRules),
        new DevTask(
            "publish.hashes",
            PublishCategory,
            L.T("tools.devtools.taskc.publish.hashes.title"),
            L.T("tools.devtools.taskc.publish.hashes.summary"),
            L.T("tools.devtools.hint.sha256Log"),
            actions.CopyHashes),
        new DevTask(
            "publish.open-dist",
            PublishCategory,
            L.T("tools.devtools.taskc.publish.open-dist.title"),
            L.T("tools.devtools.taskc.publish.open-dist.summary"),
            L.T("tools.devtools.hint.explorer"),
            actions.OpenDist),
        new DevTask(
            "publish.open-setup",
            PublishCategory,
            L.T("tools.devtools.taskc.publish.open-setup.title"),
            L.T("tools.devtools.taskc.publish.open-setup.summary"),
            L.T("tools.devtools.hint.explorer"),
            actions.OpenSetup),
    };

    public static IReadOnlyList<DevTask> Render(RenderTaskActions actions)
    {
        string PathFor(string name) => System.IO.Path.Combine(actions.OutputDirectory(), name);
        Func<string?> Output(string name) => () => PathFor(name);

        return new[]
        {
            new DevTask("render.table", "render", L.T("tools.devtools.taskc.render.table.title"),
                L.T("tools.devtools.taskc.render.table.summary"), "screen-table.png",
                () => actions.RenderScreen("table", PathFor("screen-table.png")), OutputPath: Output("screen-table.png")),
            new DevTask("render.menu", "render", L.T("tools.devtools.taskc.render.menu.title"),
                L.T("tools.devtools.taskc.render.menu.summary"), "screen-menu.png",
                () => actions.RenderScreen("menu", PathFor("screen-menu.png")), OutputPath: Output("screen-menu.png")),
            new DevTask("render.settings", "render", L.T("tools.devtools.taskc.render.settings.title"),
                L.T("tools.devtools.taskc.render.settings.summary"), "screen-settings.png",
                () => actions.RenderScreen("settings", PathFor("screen-settings.png")), OutputPath: Output("screen-settings.png")),
            new DevTask("render.tutorial", "render", L.T("tools.devtools.taskc.render.tutorial.title"),
                L.T("tools.devtools.taskc.render.tutorial.summary"), "screen-tutorial.png",
                () => actions.RenderScreen("tutorial", PathFor("screen-tutorial.png")), OutputPath: Output("screen-tutorial.png")),
            new DevTask("render.markdown", "render", L.T("tools.devtools.taskc.render.markdown.title"),
                L.T("tools.devtools.taskc.render.markdown.summary"), "screen-markdown.png",
                () => actions.RenderScreen("markdown", PathFor("screen-markdown.png")), OutputPath: Output("screen-markdown.png")),
            new DevTask("render.roundend", "render", L.T("tools.devtools.taskc.render.roundend.title"),
                L.T("tools.devtools.taskc.render.roundend.summary"), "roundend.png",
                () => actions.RenderRoundEnd(PathFor("roundend.png")), OutputPath: Output("roundend.png")),
            new DevTask("render.actions", "render", L.T("tools.devtools.taskc.render.actions.title"),
                L.T("tools.devtools.taskc.render.actions.summary"), "btn-colors.png",
                () => actions.RenderActions(PathFor("btn-colors.png")), OutputPath: Output("btn-colors.png")),
            new DevTask("render.ui-svg", "render", L.T("tools.devtools.taskc.render.ui-svg.title"),
                L.T("tools.devtools.taskc.render.ui-svg.summary"), "ui-merged.svg",
                () => actions.ExportUiSvg(PathFor("ui-merged.svg")), OutputPath: Output("ui-merged.svg")),
            new DevTask("render.style", "render", L.T("tools.devtools.taskc.render.style.title"),
                L.T("tools.devtools.taskc.render.style.summary"), "style-flat.png",
                () => actions.RenderTableStyle(PathFor("style-flat.png")), OutputPath: Output("style-flat.png")),
            new DevTask("render.steam", "render", L.T("tools.devtools.taskc.render.steam.title"),
                L.T("tools.devtools.taskc.render.steam.summary"), "artifacts/steam/*.png",
                actions.ExportSteamShots, OutputPath: actions.SteamShotsPath),
            new DevTask("render.steam-capsules", "render", L.T("tools.devtools.taskc.render.steam-capsules.title"),
                L.T("tools.devtools.taskc.render.steam-capsules.summary"), L.T("tools.devtools.hint.capsules"),
                actions.ExportSteamCapsules, OutputPath: actions.SteamCapsulePath),
            new DevTask("render.open-output", "render", L.T("tools.devtools.taskc.render.open-output.title"),
                L.T("tools.devtools.taskc.render.open-output.summary"), L.T("tools.devtools.hint.explorer"),
                actions.OpenOutput),
        };
    }


    public static IReadOnlyList<DevTask> Ai(AiTaskActions actions) => new[]
    {
        new DevTask("ai.export-selfplay", "ai", L.T("tools.devtools.taskc.ai.export-selfplay.title"),
            L.T("tools.devtools.taskc.ai.export-selfplay.summary"), "selfplay-*.jsonl",
            actions.ExportSelfplay, Confirm: true),
        new DevTask("ai.smoke-neural", "ai", L.T("tools.devtools.taskc.ai.smoke-neural.title"),
            L.T("tools.devtools.taskc.ai.smoke-neural.summary"), L.T("tools.devtools.hint.passFail"),
            actions.SmokeNeural),
        new DevTask("ai.smoke-7neuu", "ai", L.T("tools.devtools.taskc.ai.smoke-7neuu.title"),
            L.T("tools.devtools.taskc.ai.smoke-7neuu.summary"), L.T("tools.devtools.hint.passFail"),
            actions.Smoke7Neuu),
        new DevTask("ai.bench-snapshot", "ai", L.T("tools.devtools.taskc.ai.bench-snapshot.title"),
            L.T("tools.devtools.taskc.ai.bench-snapshot.summary"), L.T("tools.devtools.hint.bench"),
            actions.BenchSnapshot),
        new DevTask("ai.open-data", "ai", L.T("tools.devtools.taskc.ai.open-data.title"),
            L.T("tools.devtools.taskc.ai.open-data.summary"), L.T("tools.devtools.hint.explorer"),
            actions.OpenDataDir),
    };


    public static IReadOnlyList<DevTask> Audit(AuditTaskActions actions) => new[]
    {
        new DevTask("audit.games", "audit", L.T("tools.devtools.taskc.audit.games.title"),
            L.T("tools.devtools.taskc.audit.games.summary"), L.T("tools.devtools.hint.auditCount"),
            actions.AuditWithDialog),
        new DevTask("audit.ui", "audit", L.T("tools.devtools.taskc.audit.ui.title"),
            L.T("tools.devtools.taskc.audit.ui.summary"), L.T("tools.devtools.hint.auditUi"),
            actions.AuditUi),
        new DevTask("audit.call", "audit", L.T("tools.devtools.taskc.audit.call.title"),
            L.T("tools.devtools.taskc.audit.call.summary"), L.T("tools.devtools.hint.auditCall"),
            actions.AuditCall),
        new DevTask("audit.smoke", "audit", L.T("tools.devtools.taskc.audit.smoke.title"),
            L.T("tools.devtools.taskc.audit.smoke.summary"), L.T("tools.devtools.hint.smoke"),
            actions.SmokeRun),
    };

    public static IReadOnlyList<DevTask> Resource(ResourceTaskActions actions) => new[]
    {
        new DevTask("resource.font", "resource", L.T("tools.devtools.taskc.resource.font.title"),
            L.T("tools.devtools.taskc.resource.font.summary"), L.T("tools.devtools.hint.font"),
            actions.CheckFont),
        new DevTask("resource.i18n", "resource", L.T("tools.devtools.taskc.resource.i18n.title"),
            L.T("tools.devtools.taskc.resource.i18n.summary"), "artifacts/localization/ui-strings.csv",
            actions.AuditUiStrings),
        new DevTask("resource.atlas", "resource", L.T("tools.devtools.taskc.resource.atlas.title"),
            L.T("tools.devtools.taskc.resource.atlas.summary"), "ui-atlas.png",
            actions.ExportAtlas, OutputPath: () => actions.AtlasPath()),
        new DevTask("resource.open-assets", "resource", L.T("tools.devtools.taskc.resource.open-assets.title"),
            L.T("tools.devtools.taskc.resource.open-assets.summary"), L.T("tools.devtools.hint.explorer"),
            actions.OpenAssets),
        new DevTask("resource.open-docs", "resource", L.T("tools.devtools.taskc.resource.open-docs.title"),
            L.T("tools.devtools.taskc.resource.open-docs.summary"), L.T("tools.devtools.hint.explorer"),
            actions.OpenDocs),
    };

}
