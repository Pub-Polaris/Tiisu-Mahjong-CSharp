using Tiisu.App.Table;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>布局纯函数缓存（审计 P2-6）。</summary>
public sealed class TableLayoutCacheTests
{
    private static SeatView Seat(int count, int drawnIndex) => new()
    {
        Index = 0,
        Hand = Enumerable.Range(0, count).Select(_ => new TileView("m1")).ToArray(),
        HandCount = count,
        DrawnIndex = drawnIndex,
    };

    [Fact]
    public void 相同输入返回同一份数组()
    {
        var layout = new TableLayoutSpec();
        var seat = Seat(13, 13);

        var first = TableSceneSkia.HandTileRects(layout, seat, 0);
        var second = TableSceneSkia.HandTileRects(layout, seat, 0);

        Assert.Same(first, second);
    }

    [Fact]
    public void 摸牌位变化会重新计算()
    {
        var layout = new TableLayoutSpec();

        var noDraw = TableSceneSkia.HandTileRects(layout, Seat(13, -1), 0);
        var drawn = TableSceneSkia.HandTileRects(layout, Seat(14, 13), 0);

        Assert.NotSame(noDraw, drawn);
        Assert.Equal(13, noDraw.Count);
        Assert.Equal(14, drawn.Count);
    }

    [Fact]
    public void 几何参数变化会失效()
    {
        var layout = new TableLayoutSpec();
        var seat = Seat(13, -1);

        var a = TableSceneSkia.HandTileRects(layout, seat, 0);
        layout.HandGap += 4f;
        var b = TableSceneSkia.HandTileRects(layout, seat, 0);

        Assert.NotSame(a, b);
        Assert.NotEqual(a[1].X, b[1].X);
    }
}
