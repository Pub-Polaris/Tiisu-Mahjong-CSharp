using System.Linq;

namespace Tiisu.App.Table;

/// <summary>
/// 绝对座位（引擎 / <c>RoundResult</c> / <c>SettlementDetail</c> 口径）与快照显示顺序之间的映射。
/// 联机快照会按观察者旋转 <see cref="SeatView.Index"/>，但保留 <see cref="SeatView.AbsoluteIndex"/>；
/// 结算页等使用绝对座位的地方必须走这里，不能直接拿 <c>Index</c> 去比对。
/// </summary>
public static class SeatIndexMap
{
    /// <summary>按绝对座位找快照里的座位；找不到返回 null。</summary>
    public static SeatView? ByAbsolute(TableViewModel vm, int absoluteIndex)
    {
        return vm.Seats.FirstOrDefault(s => s.AbsoluteIndex == absoluteIndex)
            ?? vm.Seats.FirstOrDefault(s => s.AbsoluteIndex < 0 && s.Index == absoluteIndex);
    }

    /// <summary>绝对座位在当前快照里的显示序号；找不到返回 -1。</summary>
    public static int DisplayIndex(TableViewModel vm, int absoluteIndex) =>
        ByAbsolute(vm, absoluteIndex)?.Index ?? -1;
}
