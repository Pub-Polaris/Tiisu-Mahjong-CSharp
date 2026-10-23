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
            AiLevels = new[] { "Simple", "Normal", "Simple", "Hard" },
        };
    }

    /// <summary>
    /// 合并 UI SVG 演示：恰好五个动作按钮（杠 / 和 / 碰 / 吃 / 报听），
    /// 用来导出带语义色的 <c>btn-*</c> 图层；牌桌、四家手牌/副露/牌河沿用演示快照。
    /// </summary>
    public static TableViewModel CreateForUiExport()
    {
        var vm = CreateWithActions();

        // 四家都至少有一组副露，保证导出里 meld-self / meld-right / meld-top / meld-left 四个图层齐全。
        MeldView M(string type, params string[] ids) => new()
        {
            Type = type,
            Tiles = ids.Select(id => new TileView(id)).ToArray(),
        };

        var extras = new[]
        {
            M("chi", "1m", "2m", "3m"),
            M("pon", "5z", "5z", "5z"),
            M("minkan", "3s", "3s", "3s", "3s"),
        };

        var seats = new List<SeatView>(vm.Seats.Count);
        for (int i = 0; i < vm.Seats.Count; i++)
        {
            var s = vm.Seats[i];
            seats.Add(i == 0
                ? s
                : new SeatView
                {
                    Index = s.Index,
                    Name = s.Name,
                    Wind = s.Wind,
                    Score = s.Score,
                    IsDealer = s.IsDealer,
                    IsHuman = s.IsHuman,
                    IsTurn = s.IsTurn,
                    IsCalled = s.IsCalled,
                    Noten = s.Noten,
                    HandCount = s.HandCount,
                    Hand = s.Hand,
                    Melds = new[] { extras[i - 1] },
                    Discards = s.Discards,
                    LastDiscard = s.LastDiscard,
                });
        }

        return new TableViewModel
        {
            RoundLabel = vm.RoundLabel,
            WallCount = vm.WallCount,
            RinshanCount = vm.RinshanCount,
            Honba = vm.Honba,
            Kyotaku = vm.Kyotaku,
            TurnIndex = vm.TurnIndex,
            Seats = seats,
            ModeTarget = vm.ModeTarget,
            RevealHands = vm.RevealHands,
            Rinshan = vm.Rinshan,
            Phase = "human_meld",
            Actions = HumanAction.Kan | HumanAction.Tsumo | HumanAction.Pon | HumanAction.Chi | HumanAction.DeclareCall,
            ActionLabels = new[] { "吃" },
            TenpaiTiles = vm.TenpaiTiles,
            Message = "合并 UI 导出：五个语义动作按钮",
            AiLevels = vm.AiLevels,
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
            AiLevels = vm.AiLevels,
        };
    }

    /// <summary>
    /// 结算屏出图演示：四家手牌全部翻开，和牌者的手牌恰好 14 张（西家带一组碰，验证副露排版）。
    /// 只用于 <c>--render-roundend --double / --flow</c> 的固定截图，不参与真实对局。
    /// </summary>
    public static TableViewModel CreateForSettlement()
    {
        TileView T(string id) => new(id);
        MeldView M(string type, params string[] ids) => new() { Type = type, Tiles = ids.Select(T).ToArray() };

        var seats = new List<SeatView>
        {
            new()
            {
                Index = 0, Name = "你", Wind = "东", Score = 48300, IsDealer = true, IsHuman = true,
                HandCount = 14,
                Hand = new[] { "1m", "2m", "3m", "4m", "5m", "6m", "7m", "8m", "9m", "2p", "3p", "4p", "5p", "5p" }
                    .Select(T).ToArray(),
                Discards = new[] { "3p", "8p", "1z", "9s", "2p", "5s" }.Select(T).ToArray(),
            },
            new()
            {
                Index = 1, Name = "南 AI", Wind = "南", Score = 31900,
                HandCount = 13,
                Hand = new[] { "1p", "1p", "1p", "2s", "3s", "4s", "6s", "7s", "8s", "1z", "2z", "3z", "9m" }
                    .Select(T).ToArray(),
                Discards = new[] { "2m", "9m", "6p", "3z", "7s" }.Select(T).ToArray(),
            },
            new()
            {
                Index = 2, Name = "西 AI", Wind = "西", Score = 42100,
                HandCount = 11,
                Hand = new[] { "2m", "3m", "4m", "6p", "7p", "8p", "1s", "2s", "3s", "9s", "9s" }
                    .Select(T).ToArray(),
                Melds = new[] { M("pon", "7z", "7z", "7z") },
                Discards = new[] { "1p", "2p", "3p", "4p", "5p" }.Select(T).ToArray(),
            },
            new()
            {
                Index = 3, Name = "北 AI", Wind = "北", Score = 27700,
                HandCount = 13,
                Hand = new[] { "1m", "4m", "7m", "2p", "5p", "8p", "3s", "6s", "9s", "4z", "5z", "6z", "7z" }
                    .Select(T).ToArray(),
                Discards = new[] { "9m", "8m", "7m", "6m" }.Select(T).ToArray(),
            },
        };

        return new TableViewModel
        {
            RoundLabel = "东 2 局 · 1 本场",
            WallCount = 0,
            RinshanCount = 4,
            Honba = 1,
            Kyotaku = 700,
            TurnIndex = 0,
            Seats = seats,
            ModeTarget = 4,
            RevealHands = true,
            Phase = "round_end",
            Message = string.Empty,
            AiLevels = new[] { "Simple", "Normal", "Simple", "Hard" },
        };
    }

    /// <summary>
    /// 报听按钮演示：<paramref name="callable"/> = 真听牌（按钮可点 + 待牌提示）/
    /// false = 不听牌（按钮灰显、无提示）。
    /// </summary>
    public static TableViewModel CreateWithCall(bool callable)
    {
        var vm = Create(0);
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
            Phase = "human_discard",
            Actions = HumanAction.Discard | (callable ? HumanAction.DeclareCall : HumanAction.None),
            TenpaiTiles = callable ? new[] { "2m", "5p", "8s", "3z" } : Array.Empty<string>(),
            Message = callable ? "真听牌：报听按钮可点" : "不听牌：报听按钮灰显",
            AiLevels = vm.AiLevels,
        };
    }

    /// <summary>
    /// 流局翻牌演示：四家手牌全翻；未听牌家灰显；已报听家标「报听」。
    /// 座位 1、3 已报听（算听牌），座位 0、2 未报听且未听牌（灰显）。
    /// </summary>
    public static TableViewModel CreateWithFlow()
    {
        TileView T(string id) => new(id);
        var vm = Create(0);

        var hands = new[]
        {
            new[] { "1m", "2m", "3m", "5m", "5m", "7m", "2p", "3p", "4p", "1s", "3z", "6p", "8s" },
            new[] { "1p", "2p", "3p", "5p", "5p", "7p", "2s", "3s", "4s", "1z", "1z", "6z", "7z" },
            new[] { "4m", "5m", "6m", "7m", "8m", "9m", "9p", "9p", "1s", "1s", "2z", "2z", "3z" },
            new[] { "1m", "4m", "7m", "2p", "5p", "8p", "3s", "6s", "9s", "4z", "5z", "6z", "7z" },
        };

        var seats = new List<SeatView>(4);
        for (int i = 0; i < 4; i++)
        {
            var seat = vm.Seats[i];
            bool called = i is 1 or 3;
            seats.Add(new SeatView
            {
                Index = seat.Index,
                Name = seat.Name,
                Wind = seat.Wind,
                Score = seat.Score,
                IsDealer = seat.IsDealer,
                IsHuman = seat.IsHuman,
                IsTurn = false,
                IsCalled = called,
                Noten = !called,
                HandCount = hands[i].Length,
                Hand = hands[i].Select(T).ToArray(),
                Melds = seat.Melds,
                Discards = seat.Discards,
                LastDiscard = seat.LastDiscard,
            });
        }

        return new TableViewModel
        {
            RoundLabel = vm.RoundLabel,
            WallCount = 0,
            RinshanCount = vm.RinshanCount,
            Honba = 1,
            Kyotaku = 700,
            TurnIndex = 0,
            Seats = seats,
            ModeTarget = vm.ModeTarget,
            RevealHands = true,
            Phase = "round_end",
            Message = "荒牌流局：四家翻牌 · 未听牌灰显 · 已报听算听牌",
            AiLevels = vm.AiLevels,
        };
    }

    /// <summary>
    /// 暗杠展示演示：自家与对家各一组暗杠。
    /// <paramref name="settled"/> = false 为对局中（只翻第 2、4 张）；true 为和牌后（全翻 + 30% 黑遮罩）。
    /// </summary>
    public static TableViewModel CreateWithAnkan(bool settled)
    {
        TileView T(string id) => new(id);
        var vm = Create(0);

        MeldView Ankan(string id) => new()
        {
            Type = "ankan",
            Tiles = new[] { T(id), T(id), T(id), T(id) },
        };

        SeatView Copy(SeatView s, IReadOnlyList<MeldView> melds) => new()
        {
            Index = s.Index,
            Name = s.Name,
            Wind = s.Wind,
            Score = s.Score,
            IsDealer = s.IsDealer,
            IsHuman = s.IsHuman,
            IsTurn = s.IsTurn,
            HandCount = 10,
            Hand = s.Index == 0 ? s.Hand.Take(10).ToArray() : s.Hand,
            Melds = melds,
            Discards = s.Discards,
            LastDiscard = s.LastDiscard,
        };

        var seats = new List<SeatView>(4);
        foreach (var seat in vm.Seats)
        {
            seats.Add(seat.Index switch
            {
                0 => Copy(seat, new[] { Ankan("7z") }),
                2 => Copy(seat, new[] { Ankan("5s") }),
                _ => seat,
            });
        }

        // 3 本场 → 场赏 = 700 × 3 = 2100（同时验证 HUD 文案）。
        return new TableViewModel
        {
            RoundLabel = "东 1 局 · 3 本场",
            WallCount = 36,
            RinshanCount = 4,
            Honba = 3,
            Kyotaku = 700 * 3,
            TurnIndex = 0,
            Seats = seats,
            ModeTarget = 1,
            RevealHands = settled,
            Phase = settled ? "round_end" : "human_discard",
            Message = settled ? "和牌后：暗杠整组翻开" : "对局中：暗杠只翻第 2、4 张",
            AiLevels = vm.AiLevels,
        };
    }
}
