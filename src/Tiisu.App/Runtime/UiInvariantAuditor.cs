using System.Collections.Generic;
using System.Globalization;
using Tiisu.App.Table;
using Tiisu.Core;
using Tiisu.Engine;

namespace Tiisu.App.Runtime;

/// <summary>
/// 虚拟上桌的 UI 自检：拿运行时真快照对照引擎真状态，抓「听牌提示和实际判和不一致」
/// 「牌形成和却不能和且无提示」「能自摸却没有按钮」这类 UI/提示层问题。
/// 只在 DevTools 的 --audit-games --ui 里使用，正常对局不跑。
/// </summary>
public sealed class UiInvariantAuditor
{
    private readonly List<string> _samples = new();
    private int _count;

    public int Count => _count;

    public IReadOnlyList<string> Samples => _samples;

    public void Audit(MahjongEngine engine, TableViewModel vm)
    {
        int human = -1;
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            if (engine.Players[i].IsHuman)
            {
                human = i;
                break;
            }
        }

        if (human < 0)
        {
            return;
        }

        var player = engine.Players[human];
        bool banned = engine.IsWinBanned(player);
        bool hasTsumo = (vm.Actions & HumanAction.Tsumo) != 0;

        if (vm.Phase == "human_discard" && player.TotalTiles() == 14 + player.KongCount())
        {
            var eval = engine.EvaluateWinFor(player, player.Hand, selfDraw: true, player.LastDraw);
            if (eval.Success)
            {
                if (!banned && !hasTsumo)
                {
                    Report("能自摸却没有和按钮", engine, vm, "");
                }
            }
            else if (hasTsumo)
            {
                bool misfire = eval.Reason == "no_yaku" && eval.ThresholdFu > 0 && eval.ThresholdFu < engine.BaseFu;
                if (!misfire)
                {
                    Report("不能和却给了和按钮", engine, vm, eval.Reason ?? "");
                }
                else if (string.IsNullOrEmpty(vm.WinHint))
                {
                    Report("错和候选但无可见提示", engine, vm,
                        "fu=" + eval.ThresholdFu.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                if (banned && !vm.WinHint.Contains("禁和", StringComparison.Ordinal))
                {
                    Report("禁和但无提示", engine, vm, "");
                }
                else if (eval.Reason == "no_yaku" && string.IsNullOrEmpty(vm.WinHint))
                {
                    Report("成和形但不能和且无提示", engine, vm,
                        "fu=" + eval.ThresholdFu.ToString(CultureInfo.InvariantCulture));
                }
                else if (eval.Reason == "no_yaku" && eval.ThresholdFu > 0 && eval.ThresholdFu < engine.BaseFu
                         && !vm.WinHint.Contains("错和", StringComparison.Ordinal))
                {
                    Report("赋不足无错和提示", engine, vm,
                        "fu=" + eval.ThresholdFu.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        if (vm.Phase == "human_ron"
            && ((vm.Actions & HumanAction.Ron) == 0 || (vm.Actions & HumanAction.Pass) == 0))
        {
            Report("荣和窗口缺 Ron/Pass", engine, vm, "");
        }

        // 待牌提示：13 张形逐张对照实际判和。
        var shape = engine.TenpaiHandOf(player);
        if (shape.Count + player.MeldTiles() == 13 + player.KongCount())
        {
            foreach (string wire in vm.TenpaiTiles)
            {
                if (!TryParseAtlas(wire, out var tile))
                {
                    Report("待牌提示无法解析", engine, vm, wire);
                    continue;
                }

                var trial = new List<Tile>(shape) { tile };
                var eval = engine.EvaluateWinFor(player, trial, selfDraw: true, tile);
                if (!eval.Success && eval.Reason != "no_yaku")
                {
                    Report("待牌提示有张实际不能和", engine, vm,
                        wire + " reason=" + (eval.Reason ?? "?"));
                }
            }
        }
    }

    private static bool TryParseAtlas(string wire, out Tile tile)
    {
        tile = default;
        if (string.IsNullOrEmpty(wire) || wire.Length < 2)
        {
            return false;
        }

        char suit = wire[^1];
        if (!int.TryParse(wire[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            return false;
        }

        Suit s = suit switch
        {
            'm' => Suit.Man,
            'p' => Suit.Pin,
            's' => Suit.Sou,
            'z' => Suit.Honor,
            _ => (Suit)(-1),
        };
        if ((int)s < 0)
        {
            return false;
        }

        tile = new Tile(s, number);
        return true;
    }

    private void Report(string kind, MahjongEngine engine, TableViewModel vm, string detail)
    {
        _count++;
        if (_samples.Count >= 20)
        {
            return;
        }

        var seat = engine.Players.FirstOrDefault(p => p.IsHuman);
        string hand = seat is null
            ? string.Empty
            : string.Join("", seat.Hand.OrderBy(t => t.Index).Select(t => t.Num.ToString(CultureInfo.InvariantCulture) + SuitChar(t.Suit)));
        _samples.Add(
            kind + " · round=" + engine.RoundLabel()
            + " phase=" + vm.Phase
            + " hand=" + hand
            + (string.IsNullOrEmpty(detail) ? string.Empty : " " + detail));
    }

    private static string SuitChar(Suit suit) => suit switch
    {
        Suit.Man => "m",
        Suit.Pin => "p",
        Suit.Sou => "s",
        _ => "z",
    };
}
