using Tiisu.App.Table;

namespace Tiisu.App.Runtime;

/// <summary>
/// Host 发给客户端前把绝对座位旋转成「自己永远在索引 0」。
/// 这样牌桌渲染、手牌点击和 ActionBar 不需要知道网络座位。
/// </summary>
public static class SnapshotRotation
{
    public static TableViewModel RotateForViewer(TableViewModel snapshot, int viewerSeat)
    {
        if (viewerSeat <= 0 || snapshot.Seats.Count != 4)
        {
            return snapshot.SelfSeat == viewerSeat
                ? snapshot
                : snapshot with { SelfSeat = viewerSeat };
        }

        var seats = new List<SeatView>(4);
        for (int display = 0; display < 4; display++)
        {
            int absolute = (viewerSeat + display) % 4;
            SeatView source = snapshot.Seats[absolute];
            seats.Add(source with { Index = display, IsHuman = display == 0 });
        }

        int turn = snapshot.TurnIndex;
        if (turn >= 0 && turn < 4)
        {
            turn = (turn - viewerSeat + 4) % 4;
        }

        return snapshot with
        {
            Seats = seats,
            TurnIndex = turn,
            SelfSeat = 0,
        };
    }
}
