using System;
using System.Collections.Generic;
using System.Linq;

namespace Tiisu.App.Table;

/// <summary>
/// 演示用牌桌数据：真实对局状态尚未接入 C#（目前由 Web 侧 JS 驱动），
/// 这里给一份固定的示意模型，供 --render-yoga 预览与 Skia 界面自检使用。
/// </summary>
public static class TableDemoModel
{
    public static TableViewModel Create(int selfMelds = 0)
    {
        TileView T(string id) => new(id);
        MeldView M(string type, params string[] ids) => new() { Type = type, Tiles = ids.Select(T).ToArray() };

        var meldPool = new[]
        {
            M("pon", "2s", "2s", "2s"),
            M("chi", "4m", "5m", "6m"),
            M("pon", "7z", "7z", "7z"),
            M("minkan", "1z", "1z", "1z", "1z"),
        };

        int n = Math.Clamp(selfMelds, 0, 4);
        var ownMelds = meldPool.Take(n).ToArray();
        int handN = Math.Max(2, 13 - 3 * n);   // 七子 13 张；每副露一组少 3 张

        var handPool = new[] { "1m", "2m", "3m", "5m", "5m", "7m", "2p", "3p", "4p", "1s", "3z", "6p", "8s", "9m" };
        var hand0 = handPool.Take(handN).Select(T).ToArray();

        var river0 = new[] { "3p", "8p", "1z", "9s", "2p", "5s", "6z", "7m", "1p", "4s", "2z", "4m" }.Select(T).ToArray();
        var river1 = new[] { "2m", "9m", "6p", "3z", "7s", "5z", "1m", "4z", "6s" }.Select(T).ToArray();
        var river2 = new[] { "1p", "2p", "3p", "4p", "5p", "6p", "7p", "8p", "9p", "1z", "2z", "3z", "4z", "5z", "6z" }.Select(T).ToArray();
        var river3 = new[] { "9m", "8m", "7m", "6m", "5m", "4m", "3m" }.Select(T).ToArray();

        var seats = new List<SeatView>
        {
            new() { Index = 0, Name = "你", Wind = "东", Score = 49000, IsDealer = true, IsHuman = true, IsTurn = true,
                    HandCount = hand0.Length, Hand = hand0, Discards = river0, Melds = ownMelds },
            new() { Index = 1, Name = "南 AI", Wind = "南", Score = 39200, HandCount = 13, Discards = river1 },
            new() { Index = 2, Name = "西 AI", Wind = "西", Score = 58800, HandCount = 13, Discards = river2 },
            new() { Index = 3, Name = "北 AI", Wind = "北", Score = 49000, HandCount = 13, Discards = river3 },
        };

        return new TableViewModel
        {
            RoundLabel = "东 1 局 · 0 本场",
            WallCount = 36,
            RinshanCount = 4,
            Honba = 0,
            Kyotaku = 0,
            TurnIndex = 0,
            Seats = seats,
        };
    }

    /// <summary>演示：人类回合且同时有杠/和/碰/吃/过/听牌，用于动作按钮条出图。</summary>
    public static TableViewModel CreateWithActions()
    {
        var vm = Create(1);
        return new TableViewModel
        {
            RoundLabel = vm.RoundLabel,
            WallCount = vm.WallCount,
            RinshanCount = vm.RinshanCount,
            Honba = vm.Honba,
            Kyotaku = vm.Kyotaku,
            TurnIndex = vm.TurnIndex,
            Seats = vm.Seats,
            ModeTarget = vm.ModeTarget,
            RevealHands = vm.RevealHands,
            Rinshan = vm.Rinshan,
            Phase = "human_meld",
            Actions = HumanAction.Kan | HumanAction.Tsumo | HumanAction.Pon | HumanAction.Chi | HumanAction.Pass,
            ActionLabels = new[] { "吃 4-5-6", "吃 5-6-7" },
            Message = "副露窗口",
            TenpaiTiles = new[] { "2m", "5p", "8s", "3z" },
        };
    }
}
