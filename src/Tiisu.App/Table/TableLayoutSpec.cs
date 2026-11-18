using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Tiisu.App.Rendering;
using Tiisu.Localization;
using Tiisu.App.Runtime;
using Tiisu.App.Ui;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Table;

/// <summary>
/// 牌桌布局参数（唯一真源）+ Skia 绘制。
///
/// 布局由 <see cref="YogaTableLayout"/> 用 Yoga 计算，本类只提供：
///   · 牌尺寸 / 间距 / 画布等全部可调参数
///   · 牌河几何（沿该家左手边对齐中央框，行向远离中央框生长）
///   · 各家的旋转角与右手方向
///
/// 四家手牌框恒为 823×82，手牌在框内居中；副露按手牌框边缘 + 该家右手方向摆，2 行堆叠，
/// 所以副露永远不参与手牌定位，也就不会「推开手牌」。
/// 画布 1400×1400：中央板 400×400，四家牌河 7 列 × 5 行（单格 50×68）。
/// </summary>
public sealed class TableLayoutSpec
{
    // ── 牌尺寸：唯一真源，改这里整桌重排 ──
    public float HandW = 60f;
    public float HandH = 82f;
    public float HandGap = 3.6f;
    public float DrawnGap = 14.4f;

    /// <summary>手牌框按几张牌定长。七子是 13 张（摸牌时的第 14 张会临时外溢）。</summary>
    public int HandTiles = 13;

    public float MeldW = 28.8f;
    public float MeldH = 39.6f;
    public float MeldGap = 1.2f;
    public float MeldGroupGap = 7.2f;
    public float MeldPad = 2.4f;
    public float HandMeldGap = 12f;

    /// <summary>副露按「2 行 × 每行 2 组」堆叠：4 组副露时正好两行各两组，不往画布外拉长。</summary>
    public int MeldRows = 2;
    public int MeldGroupsPerRow = 2;
    public float MeldRowGap = 3f;

    public float RiverW = 50f;
    public float RiverH = 68f;
    public float RiverGap = 3f;
    public int RiverCols = 7;
    public int RiverRows = 5;

    /// <summary>
    /// 手牌矩形是纯函数结果：按输入缓存，避免每帧重新分配（审计 P2-6）。
    /// 返回的数组由缓存持有，调用方只读、不要原地修改。
    /// </summary>
    internal readonly Dictionary<(int Index, int N, int Drawn, float Fx, float Fy, float Fw, float Fh,
        float HandW, float HandH, float HandGap, float DrawnGap), RectF[]> _handRectCache = new();

    public float CenterSize = 400f;
    public float EdgePad = 24f;
    public float PlateGap = 14f;
    public float Margin = 16f;

    public float CanvasW = 1400f;
    public float CanvasH = 1400f;

    public float FrameLen => HandTiles * HandW + (HandTiles - 1) * HandGap;
    public float FrameDepth => HandH;
    public float RiverSpan => RiverCols * (RiverW + RiverGap) - RiverGap;
    public float RiverDepth => RiverRows * (RiverH + RiverGap) - RiverGap;

    // ── 布局结果 ──
    public RectF PlateRect { get; set; }
    public RectF[] HandRects { get; } = new RectF[4];

    /// <summary>四家副露区（参考框；副露几何仍由手牌框 + 右手方向推导，绘制不读这里）。</summary>
    public RectF[] MeldRects { get; } = new RectF[4];

    /// <summary>四家牌河外接矩形；<see cref="River"/> 由它反推锚点，可被自定义布局覆盖。</summary>
    public RectF[] RiverRects { get; } = new RectF[4];

    /// <summary>左上角快捷键提示条。</summary>
    public RectF HudRect { get; set; } = new RectF(24, 20, 300, 34);

    /// <summary>动作按钮条锚框：按钮纵向顶边与水平中心取自这里（见 <see cref="Ui.ActionBar.Build"/>）。</summary>
    public RectF ActionBarRect { get; set; }

    /// <summary>结算面板默认外框（编辑器参考框；绘制未自定义时仍按内容动态居中）。1400 画布下居中：x = (1400−1120)/2。</summary>
    public static readonly RectF DefaultSettlementRect = new(140, 46, 1120, 660);

    /// <summary>结算左侧「和牌者手牌」区默认外框（原牌桌缩略图的位置，改为直接画牌面）。</summary>
    public static readonly RectF DefaultSettlementHandRect = new(164, 190, 636, 900);

    /// <summary>结算面板框；与 <see cref="DefaultSettlementRect"/> 相同表示未自定义（自动居中）。</summary>
    public RectF SettlementRect { get; set; } = DefaultSettlementRect;

    /// <summary>
    /// 结算左侧手牌区框（原 <c>ThumbRect</c>）。区域 id 仍为 <c>thumbnail</c>：
    /// 老的自定义布局 JSON 继续生效，布局编辑器里改的就是这块。
    /// </summary>
    public RectF SettlementHandRect { get; set; } = DefaultSettlementHandRect;

    /// <summary>是否有外部（保存的 JSON / 编辑器）覆盖过布局；false 时绘制走原生的动态计算。</summary>
    public bool OverridesActive { get; set; }

    /// <summary>
    /// 区域清单：名称 + 读写访问器。绘制继续读原有字段，编辑器通过 <see cref="LayoutRegion.Set"/> 写回。
    /// 牌河 / 副露 / HUD / 按钮条 / 结算面板 / 缩略图都在这里出现一次。
    /// </summary>
    public IReadOnlyList<LayoutRegion> Regions => _regions ??= BuildRegions();

    private IReadOnlyList<LayoutRegion>? _regions;

    /// <summary>一个命名区域的可读写视图。</summary>
    public readonly record struct LayoutRegion(string Id, string Note, Func<RectF> Get, Action<RectF> Set)
    {
        public RectF Rect => Get();
    }

    private IReadOnlyList<LayoutRegion> BuildRegions()
    {
        var list = new List<LayoutRegion>(20);
        string[] keys = { "self", "right", "top", "left" };
        string[] notes = { "自家", "下家", "对家", "上家" };

        for (int i = 0; i < 4; i++)
        {
            int seat = i;
            list.Add(new($"hand-{keys[i]}", notes[i] + "手牌", () => HandRects[seat],
                r => HandRects[seat] = r));
        }

        // 副露区：参考框。副露仍贴着手牌框的右手侧摆（绘制不改），这里给出可枚举的锚位。
        for (int i = 0; i < 4; i++)
        {
            int seat = i;
            list.Add(new($"meld-{keys[i]}", notes[i] + "副露区", () => MeldRects[seat],
                r => MeldRects[seat] = r));
        }

        for (int i = 0; i < 4; i++)
        {
            int seat = i;
            list.Add(new($"river-{keys[i]}", notes[i] + "牌河", () => RiverRects[seat],
                r => RiverRects[seat] = r));
        }

        list.Add(new("plate", "中央板（牌山）", () => PlateRect, r => PlateRect = r));
        list.Add(new("hud", "HUD 提示条", () => HudRect, r => HudRect = r));
        list.Add(new("action-bar", "动作按钮条", () => ActionBarRect, r => ActionBarRect = r));
        list.Add(new("settlement", "结算面板", () => SettlementRect, r => SettlementRect = r));

        // id 保留 "thumbnail"：老的 layout-custom.json 里的同名区域继续作用于结算手牌区。
        list.Add(new("thumbnail", "结算手牌区", () => SettlementHandRect, r => SettlementHandRect = r));

        return list;
    }

    /// <summary>
    /// 牌河几何：沿「该家左手边」对齐中央框边沿，行向远离中央框的方向长。
    /// <para><see cref="RiverGeometry.Origin"/> 是第 0 张牌的锚点（画牌用）；
    /// <see cref="RiverGeometry.Bounds"/> 是整片牌河实际占用的外接矩形（标注 / JSON 用）。
    /// 两者不同：负向步进的座位，锚点在外接矩形的一端。</para>
    /// </summary>
    public RiverGeometry River(int seat)
    {
        var bounds = RiverRects[seat];
        if (bounds.W > 0f && bounds.H > 0f)
        {
            return RiverFromBounds(seat, bounds);
        }

        return RiverFromPlate(seat);
    }

    /// <summary>由可编辑的外接矩形反推牌河几何（锚点随方向落在矩形不同角上）。</summary>
    private RiverGeometry RiverFromBounds(int seat, RectF b)
    {
        float w = RiverW, h = RiverH, gap = RiverGap;
        return seat switch
        {
            0 => new RiverGeometry(b, b, w + gap, 0, 0, h + gap, 0f),
            1 => new RiverGeometry(
                new RectF(b.X, b.Bottom - w, b.W, b.H), b,
                0, -(w + gap), h + gap, 0, -90f),
            2 => new RiverGeometry(
                new RectF(b.Right - w, b.Bottom - h, b.W, b.H), b,
                -(w + gap), 0, 0, -(h + gap), 180f),
            _ => new RiverGeometry(
                new RectF(b.Right - h, b.Y, b.W, b.H), b,
                0, w + gap, -(h + gap), 0, 90f),
        };
    }

    private RiverGeometry RiverFromPlate(int seat)
    {
        float w = RiverW, h = RiverH, gap = RiverGap;
        float span = RiverSpan, depth = RiverDepth;
        float G = PlateGap;

        var p = PlateRect;
        switch (seat)
        {
            case 0: // 自家（下）：列向右，行向下
                return new RiverGeometry(
                    new RectF(p.X, p.Bottom + G, span, depth),
                    new RectF(p.X, p.Bottom + G, span, depth),
                    w + gap, 0, 0, h + gap, 0f);

            case 1: // 下家（右）：列向上，行向右
                return new RiverGeometry(
                    new RectF(p.Right + G, p.Bottom - w, depth, span),
                    new RectF(p.Right + G, p.Bottom - span, depth, span),
                    0, -(w + gap), h + gap, 0, -90f);

            case 2: // 对家（上）：列向左，行向上
                return new RiverGeometry(
                    new RectF(p.Right - w, p.Top - G - h, span, depth),
                    new RectF(p.Right - span, p.Top - G - depth, span, depth),
                    -(w + gap), 0, 0, -(h + gap), 180f);

            default: // 上家（左）：列向下，行向左
                return new RiverGeometry(
                    new RectF(p.X - G - h, p.Top, depth, span),
                    new RectF(p.X - G - depth, p.Top, depth, span),
                    0, w + gap, -(h + gap), 0, 90f);
        }
    }

    public readonly record struct RiverGeometry(
        RectF Origin, RectF Bounds, float dxCol, float dyCol, float dxRow, float dyRow, float angle);

    /// <summary>某家的轴向描述：牌面旋转角跟随该家牌河（副露是平放的，与牌河同向）。</summary>
    public static (bool Horizontal, float Angle, int RightDir) Axis(int seat) => seat switch
    {
        0 => (true, 0f, +1),
        1 => (false, -90f, -1),
        2 => (true, 180f, -1),
        _ => (false, 90f, +1),
    };
}

/// <summary>
/// 静态层指纹：桌面层只依赖 (皮肤, 观感, 画布尺寸, 边距, 中央板, 四家牌河)。
/// 任一变化 → 键变 → 重建；否则命中离屏位图缓存。全部是值类型，每帧比较不分配。
/// </summary>
public readonly record struct TableStaticKey(
    int SkinId,
    long SkinStamp,
    TableRenderStyle Style,
    float CanvasW,
    float CanvasH,
    float Margin,
    RectF Plate,
    RectF River0,
    RectF River1,
    RectF River2,
    RectF River3)
{
    public static TableStaticKey For(TableLayoutSpec l, TableRenderStyle style, TableSkin skin) => new(
        skin.IsActive ? RuntimeHelpers.GetHashCode(skin) : 0,
        skin.Stamp,
        style,
        l.CanvasW,
        l.CanvasH,
        l.Margin,
        l.PlateRect,
        l.RiverRects[0],
        l.RiverRects[1],
        l.RiverRects[2],
        l.RiverRects[3]);
}

/// <summary>牌桌渲染：按 Yoga 算出的矩形绘制。</summary>
public sealed class TableSceneSkia : ITableScene
{
    /// <summary>
    /// 观感。默认 <see cref="TableRenderStyle.Flat"/>（Light 牌身 + 真透视桌面）；
    /// 运行时由设置项 <c>tableRenderStyle</c> 注入，出图由 <c>--render-25d --variant</c> 指定。
    /// 只影响绘制，不改布局与命中测试。
    /// </summary>
    public TableRenderStyle Style { get; set; } = TableRenderStyle.Flat;

    private TableRenderMetrics Metrics => TableRenderMetrics.For(Style);

    public void Draw(ICanvas c, TableViewModel vm, TableLayoutSpec l, int hoverHand = -1, double time = 0)
    {
        var m = Metrics;
        DrawTableSurface(c, vm, l, m);

        DrawRivers(c, vm, l);

        for (int seat = 0; seat < 4; seat++)
        {
            DrawSeat(c, vm, Seat(vm, seat), seat, l, seat == 0 ? hoverHand : -1, time);
        }

        DrawAiLevelBadges(c, vm, l);
        DrawCallBadges(c, vm, l);
        DrawSelectionLinks(c, vm, l, hoverHand, time);
    }

    /// <summary>0→0.5→0 的 500ms 线性呼吸透明度（选中联动高亮用）。</summary>
    private static float PulseAlpha(double time)
    {
        double frac = time % 0.5 / 0.5;
        return (float)(0.5 * (1.0 - Math.Abs(2.0 * frac - 1.0)));
    }

    /// <summary>
    /// 手牌里指着某张时，牌河与别家副露里的同种牌加 50% 蓝色遮罩（0–50–0 呼吸）。
    /// </summary>
    private static void DrawSelectionLinks(ICanvas c, TableViewModel vm, TableLayoutSpec l, int hoverHand, double time)
    {
        if (hoverHand < 0 || vm.Seats.Count == 0 || hoverHand >= vm.Seats[0].Hand.Count)
        {
            return;
        }

        string id = vm.Seats[0].Hand[hoverHand].Id;
        float alpha = PulseAlpha(time);
        if (alpha <= 0f)
        {
            return;
        }

        var mark = Color.Rgb(64, 156, 255).WithAlpha((byte)(alpha * 255));

        for (int seat = 0; seat < 4; seat++)
        {
            var view = Seat(vm, seat);
            var g = l.River(seat);
            int count = Math.Min(view.Discards.Count, l.RiverCols * l.RiverRows);
            for (int i = 0; i < count; i++)
            {
                if (view.Discards[i].Id != id)
                {
                    continue;
                }

                int row = i / l.RiverCols;
                int col = i % l.RiverCols;
                float x = g.Origin.X + col * g.dxCol + row * g.dxRow;
                float y = g.Origin.Y + col * g.dyCol + row * g.dyRow;
                var slot = Math.Abs(g.angle) == 90f
                    ? new RectF(x, y, l.RiverH, l.RiverW)
                    : new RectF(x, y, l.RiverW, l.RiverH);
                c.FillRoundRect(slot.Inflate(1.5f), 4, mark);
            }

            foreach (var placement in MeldTilePlacements(l, view, seat))
            {
                if (placement.Meld.Tiles[placement.TileIndex].Id == id)
                {
                    c.FillRoundRect(placement.Slot.Inflate(1.5f), 4, mark);
                }
            }
        }
    }

    /// <summary>
    /// 桌面背景 + 中央信息板底板。分两条路径：
    /// <list type="bullet">
    /// <item>默认平面桌且没有皮肤：直接程序化绘制（与旧实现逐行一致，不引入任何额外开销）。</item>
    /// <item>有皮肤或 2.5D 桌面：整层预合成到离屏位图（<see cref="StaticLayerCache{TKey}"/>），
    /// 每帧只回放一次 1:1 blit，再逐帧画板上的文字 / 余牌 / 点数 / 风位（<see cref="DrawCenterInfo"/>）。</item>
    /// </list>
    /// 为什么按需启用：默认路径只有 3 个纯色填充，缓存反而多一次全屏位图拷贝（实测 +0.3–0.5ms）；
    /// 皮肤/2.5D 的静态层是几十次带插值的图片绘制，预合成后 17.6ms → 4.4ms（同一布局、同一机器）。
    /// </summary>
    private void DrawTableSurface(ICanvas c, TableViewModel vm, TableLayoutSpec l, TableRenderMetrics m)
    {
        TableSkin skin = SkinStore.Current;
        bool styled = m.UsePerspective || m.HasTileDepth || m.TableTopInset > 0f;
        if (skin.IsActive || styled)
        {
            TableStaticKey key = TableStaticKey.For(l, Style, skin);
            var bounds = new RectF(0, 0, l.CanvasW, l.CanvasH);
            if (!StaticLayerCache<TableStaticKey>.TryDraw(c, key, bounds))
            {
                StaticLayerCache<TableStaticKey>.Draw(c, key, bounds, ctx =>
                {
                    using var perf = PerfProbe.Measure("table.static.rebuild");
                    DrawStaticLayer(ctx, l, m, skin);
                });
            }
        }
        else
        {
            DrawStaticLayer(c, l, m, skin);
        }

        DrawCenterInfo(c, vm, l.PlateRect);
    }

    /// <summary>清空静态层缓存（换皮 / 观感重载时调用，避免回放到已释放的皮肤图）。</summary>
    public static void ClearStaticLayers() => StaticLayerCache<TableStaticKey>.Clear();

    /// <summary>
    /// 静态层内容：外圈底色 → 阴影(皮) → 桌面(皮/程序化圆角) → 牌河底(皮) → 中央板底(皮/程序化) → 外框(皮)。
    /// 皮肤缺哪块就回退哪块；没有皮肤时这里的绘制调用与旧代码一一对应，逐像素一致。
    /// </summary>
    private static void DrawStaticLayer(ICanvas c, TableLayoutSpec l, TableRenderMetrics m, TableSkin skin)
    {
        if (m.UsePerspective || m.HasTileDepth || m.TableTopInset > 0f)
        {
            DrawStyledStaticLayer(c, l, m, skin);
            return;
        }

        c.FillRect(new RectF(0, 0, l.CanvasW, l.CanvasH), skin.Background);

        var table = new RectF(l.Margin, l.Margin, l.CanvasW - l.Margin * 2f, l.CanvasH - l.Margin * 2f);
        skin.TryDraw(c, TableSkinRegion.Shadow, table);

        if (!skin.TryDraw(c, TableSkinRegion.Table, table))
        {
            c.FillRoundRect(table, 20, Color.Rgb(46, 72, 56));
        }

        // 牌河底：默认桌面没有垫布，只有皮肤提供该区域时才画。
        for (int seat = 0; seat < 4; seat++)
        {
            var river = l.RiverRects[seat];
            if (river.W > 0f && river.H > 0f)
            {
                skin.TryDraw(c, TableSkinRegion.RiverBed, river);
            }
        }

        DrawPlateBackground(c, l.PlateRect, skin);

        skin.TryDraw(c, TableSkinRegion.Frame, new RectF(0, 0, l.CanvasW, l.CanvasH));
    }

    /// <summary>中央板底板：有皮肤用九宫格，没有就还是原来的深圆角矩形 + 描边（文字始终实时画）。</summary>
    private static void DrawPlateBackground(ICanvas c, RectF p, TableSkin skin)
    {
        if (skin.TryDraw(c, TableSkinRegion.CenterPlate, p))
        {
            return;
        }

        c.FillRoundRect(p, 16, Color.Rgb(52, 62, 74));
        c.StrokeRoundRect(p, 16, Color.Rgb(22, 28, 34), 2f);
    }

    /// <summary>
    /// 2.5D / 梯形观感（当前设置一律简化成 Flat，保留原路径以便将来恢复档位）：
    /// 整块桌面照旧绘制，中央板底板同样走皮肤 / 程序化回退。
    /// </summary>
    private static void DrawStyledStaticLayer(ICanvas c, TableLayoutSpec l, TableRenderMetrics m, TableSkin skin)
    {
        c.FillLinearGradient(new RectF(0, 0, l.CanvasW, l.CanvasH), 0, m.SurroundTop, m.SurroundBottom);

        if (m.UsePerspective)
        {
            DrawPerspectiveSurface(c, l, m, skin);
            return;
        }

        float top = l.Margin;
        float bottom = l.CanvasH - l.Margin;
        float left = l.Margin;
        float right = l.CanvasW - l.Margin;

        var quad = new[]
        {
            new CanvasPoint(left + m.TableTopInset, top),
            new CanvasPoint(right - m.TableTopInset, top),
            new CanvasPoint(right, bottom),
            new CanvasPoint(left, bottom),
        };

        c.FillPolygonGradient(quad, m.TableTop, m.TableBottom);

        // 桌面下沿：一条更暗的窄带，读作桌体厚度。
        if (m.TableBottomLip > 0f)
        {
            var lip = new[]
            {
                new CanvasPoint(left, bottom),
                new CanvasPoint(right, bottom),
                new CanvasPoint(right, bottom + m.TableBottomLip),
                new CanvasPoint(left, bottom + m.TableBottomLip),
            };
            c.FillPolygon(lip, m.TableLip);
        }

        DrawVignette(c, l, m);

        // 远边（上沿）受光：外框描边 + 内侧一条窄高光。
        c.StrokePolygon(quad, Color.Rgb(150, 190, 160).WithAlpha(m.EdgeHighlightAlpha), 1.2f);
        var topGlow = new[]
        {
            new CanvasPoint(quad[0].X + 6, top + 1.5f),
            new CanvasPoint(quad[1].X - 6, top + 1.5f),
            new CanvasPoint(quad[1].X - 6, top + 5f),
            new CanvasPoint(quad[0].X + 6, top + 5f),
        };
        c.FillPolygon(topGlow, Color.Rgb(186, 224, 190).WithAlpha(m.EdgeHighlightAlpha));

        DrawPlateBackground(c, l.PlateRect, skin);
    }

    /// <summary>
    /// 真透视桌面层：Save → ConcatPerspective → 画整块桌面（底板/渐变/暗角/桌沿/远边高光）
    /// + 中央信息板底 → Restore。输入仍是 1400×1400 逻辑坐标，透视只发生在这一层。
    /// </summary>
    private static void DrawPerspectiveSurface(ICanvas c, TableLayoutSpec l, TableRenderMetrics m, TableSkin skin)
    {
        c.Save();
        c.ConcatPerspective(new PerspectiveCamera(
            m.PerspectiveFov,
            m.PerspectiveAspect,
            m.PerspectiveNear,
            m.PerspectiveFar,
            m.PerspectiveRotateX,
            m.PerspectiveDistance,
            l.CanvasW,
            l.CanvasH,
            m.PerspectiveZoom,
            m.PerspectiveOffsetY,
            m.PerspectiveZoomY));

        // 桌面整块缩进画布：四边从逻辑矩形推到画布外，后倾投影后正好收在画布内（约 45–55px 边距）。
        var area = m.TableQuad ?? new RectF(
            l.Margin, l.Margin, l.CanvasW - l.Margin * 2f, l.CanvasH - l.Margin * 2f);
        float left = area.X;
        float top = area.Y;
        float right = area.Right;
        float bottom = area.Bottom;

        var quad = new[]
        {
            new CanvasPoint(left, top),
            new CanvasPoint(right, top),
            new CanvasPoint(right, bottom),
            new CanvasPoint(left, bottom),
        };

        c.FillPolygonGradient(quad, m.TableTop, m.TableBottom);

        // 桌沿/桌体厚度：近边下沿一条更暗的窄带。
        if (m.TableBottomLip > 0f)
        {
            var lip = new[]
            {
                new CanvasPoint(left, bottom),
                new CanvasPoint(right, bottom),
                new CanvasPoint(right, bottom + m.TableBottomLip),
                new CanvasPoint(left, bottom + m.TableBottomLip),
            };
            c.FillPolygon(lip, m.TableLip);
        }

        DrawVignette(c, l, m);

        // 远边（上沿）受光：透视后是一条横贯上方的亮边，两侧收向消失点。
        c.StrokePolygon(quad, Color.Rgb(150, 190, 160).WithAlpha(m.EdgeHighlightAlpha), 1.2f);
        var topGlow = new[]
        {
            new CanvasPoint(left + 6, top + 1.5f),
            new CanvasPoint(right - 6, top + 1.5f),
            new CanvasPoint(right - 6, top + 5f),
            new CanvasPoint(left + 6, top + 5f),
        };
        c.FillPolygon(topGlow, Color.Rgb(186, 224, 190).WithAlpha(m.EdgeHighlightAlpha));

        // 中央信息板底：与桌面同一套透视矩阵画，斜度自然一致（牌不套矩阵，仍在外面画）。
        DrawPlateBackground(c, l.PlateRect, skin);

        c.Restore();
    }

    /// <summary>四边暗角：上/下/左/右各压一条渐变，制造「斜放桌面」的纵深。</summary>
    private static void DrawVignette(ICanvas c, TableLayoutSpec l, TableRenderMetrics m)
    {
        if (m.VignetteAlpha == 0)
        {
            return;
        }

        byte a = m.VignetteAlpha;
        byte side = (byte)(a * 3 / 4);
        float w = l.CanvasW;
        float h = l.CanvasH;

        c.FillLinearGradient(new RectF(0, 0, w, 170), 0,
            Color.Black.WithAlpha(a), Color.Transparent);
        c.FillLinearGradient(new RectF(0, h - 140, w, 140), 0,
            Color.Transparent, Color.Black.WithAlpha(side));
        c.FillLinearGradient(new RectF(0, 0, 170, h), 0,
            Color.Black.WithAlpha(side), Color.Transparent, horizontal: true);
        c.FillLinearGradient(new RectF(w - 170, 0, 170, h), 0,
            Color.Transparent, Color.Black.WithAlpha(side), horizontal: true);
    }

    /// <summary>报听（已报听）小标：贴在自家手牌框下方的左右两端，不压动作条与牌河。</summary>
    private static void DrawCallBadges(ICanvas c, TableViewModel vm, TableLayoutSpec l)
    {
        const float badgeW = 66f;
        const float badgeH = 24f;
        TableSkin skin = SkinStore.Current;

        for (int seat = 0; seat < 4; seat++)
        {
            var view = Seat(vm, seat);
            if (!view.IsCalled)
            {
                continue;
            }

            var frame = l.HandRects[seat];
            if (frame.W <= 0f)
            {
                continue;
            }

            var box = seat switch
            {
                0 => new RectF(frame.Right - badgeW, frame.Bottom + 6, badgeW, badgeH),
                2 => new RectF(frame.X, frame.Bottom + 8, badgeW, badgeH),
                _ => new RectF(frame.X, frame.Bottom + 8, badgeW, badgeH),
            };

            bool self = seat == 0;
            if (!skin.TryDrawButton(c, box))
            {
                c.FillRoundRect(box, 7, Color.Rgb(96, 62, 12).WithAlpha(220));
                c.StrokeRoundRect(box, 7, Color.Rgb(255, 206, 120).WithAlpha(180), 1.2f);
            }

            c.DrawText(self ? L.T("app.table.hud.calledBadge") : L.T("app.table.hud.callBadge"), box,
                Color.Rgb(255, 236, 190), new TextStyle(13, Bold: true, Center: true));
        }
    }

    /// <summary>
    /// 在每家 AI 手牌框外侧画一枚半透明小标（座位名 · 档位），贴合座位名牌位置，
    /// 不压手牌与牌河。
    /// </summary>
    private static void DrawAiLevelBadges(ICanvas c, TableViewModel vm, TableLayoutSpec l)
    {
        var levels = vm.AiLevels;
        if (levels.Count == 0)
        {
            return;
        }

        float badgeW = UiText.IsEnglish || UiText.IsJapanese ? 82f : 62f;
        const float badgeH = 24f;
        TableSkin skin = SkinStore.Current;

        for (int seat = 1; seat < 4 && seat < levels.Count; seat++)
        {
            var view = Seat(vm, seat);
            if (view.IsHuman)
            {
                continue;
            }

            string level = AiShort(levels[seat]);
            if (level.Length == 0)
            {
                continue;
            }

            var frame = l.HandRects[seat];
            var box = seat switch
            {
                // 对家（上）：手牌框右端下方。
                2 => new RectF(frame.Right - badgeW, frame.Bottom + 8, badgeW, badgeH),

                // 左右两家：手牌框上端外侧。
                _ => new RectF(frame.X, frame.Top - badgeH - 8, badgeW, badgeH),
            };

            if (!skin.TryDrawButton(c, box))
            {
                c.FillRoundRect(box, 7, Color.Rgb(0, 0, 0).WithAlpha(150));
                c.StrokeRoundRect(box, 7, Color.Rgb(150, 170, 160).WithAlpha(90), 1f);
            }

            c.DrawText((UiText.IsEnglish ? WindShort(view.Wind) : UiText.IsJapanese ? UiText.Tr(view.Wind) : view.Wind) + "·" + level, box,
                Color.Rgb(214, 230, 220), new TextStyle(13, Bold: true, Center: true));
        }
    }

    /// <summary>档位名（Simple / Normal / Hard / Shura）→ 单字缩写；未知返回空。</summary>
    private static string AiShort(string level)
    {
        if (string.IsNullOrEmpty(level))
        {
            return string.Empty;
        }

        return level switch
        {
            "Simple" => L.T("app.table.aiLevel.simple"),
            "Normal" => L.T("app.table.aiLevel.normal"),
            "Hard" => L.T("app.table.aiLevel.hard"),
            "Shura" => L.T("app.table.aiLevel.shura"),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// 某家手牌的逐张轴对齐矩形。绘制、命中测试、SVG 导出共用同一份算式。
    ///
    /// 关键：牌沿主轴占的长度恒为 HandW（50），跨轴深度恒为 HandH（68）——
    /// 侧家的牌旋转 90° 后 footprint 变成 68×50，「短边朝向中心」由旋转实现，
    /// 不改变沿主轴的步进。这样 14 张在四家都恰好是 739 = 手牌框长度。
    /// </summary>
    public static IReadOnlyList<RectF> HandTileRects(TableLayoutSpec l, SeatView seat, int index)
    {
        var frame = l.HandRects[index];
        var (horizontal, _, _) = TableLayoutSpec.Axis(index);
        bool self = index == 0;

        int n = seat.Hand.Count > 0 ? seat.Hand.Count : seat.HandCount;
        if (n <= 0)
        {
            n = 13;
        }

        float along = l.HandW;    // 沿主轴步进
        float depth = l.HandH;    // 跨轴深度

        // 摸到的牌单独排在右侧：其余牌按原下标顺序前移，摸牌格用更大的 DrawnGap 隔开。
        // 返回数组仍按「手牌下标」索引，所以绘制与命中测试不用重映射。
        int drawn = self && seat.DrawnIndex >= 0 && seat.DrawnIndex < n ? seat.DrawnIndex : -1;

        var cacheKey = (index, n, drawn, frame.X, frame.Y, frame.W, frame.H,
            along, depth, l.HandGap, l.DrawnGap);
        if (l._handRectCache.TryGetValue(cacheKey, out RectF[]? cachedRects))
        {
            return cachedRects;
        }

        int packed = drawn >= 0 ? n - 1 : n;
        float drawnGap = drawn >= 0 ? l.DrawnGap : l.HandGap;
        float total = Math.Max(0, packed) * along
                      + Math.Max(0, packed - 1) * l.HandGap
                      + (drawn >= 0 ? drawnGap + along : 0f);

        float frameStart = horizontal ? frame.X : frame.Y;
        float frameLen = horizontal ? frame.W : frame.H;
        float frameCrossStart = horizontal ? frame.Y : frame.X;
        float frameCrossLen = horizontal ? frame.H : frame.W;

        float start = frameStart + (frameLen - total) / 2f;
        float cross = frameCrossStart + (frameCrossLen - depth) / 2f;

        var rects = new RectF[n];
        int slot = 0;
        for (int i = 0; i < n; i++)
        {
            if (i == drawn)
            {
                continue;
            }

            float a = start + slot * (along + l.HandGap);
            rects[i] = horizontal
                ? new RectF(a, cross, along, depth)
                : new RectF(cross, a, depth, along);
            slot++;
        }

        if (drawn >= 0)
        {
            float a = start + packed * (along + l.HandGap) + (drawnGap - l.HandGap);
            rects[drawn] = horizontal
                ? new RectF(a, cross, along, depth)
                : new RectF(cross, a, depth, along);
        }

        // 窗口尺寸变化会带来新键；封顶避免缓存无限增长（复核 P2-6）。
        if (l._handRectCache.Count >= 128)
        {
            l._handRectCache.Clear();
        }

        l._handRectCache[cacheKey] = rects;
        return rects;
    }

    /// <summary>
    /// 某家副露的逐张槽位（轴对齐 footprint）。与绘制共用，供 SVG 导出与命中测试使用。
    /// 返回的 RectF 是槽位（已含旋转后的宽高），Id 是该张牌的牌面 id。
    /// </summary>
    public static (RectF Slot, string Id)[] MeldTileSlots(TableLayoutSpec l, SeatView seat, int index)
    {
        var placements = MeldTilePlacements(l, seat, index);
        var slots = new (RectF, string)[placements.Length];
        for (int i = 0; i < placements.Length; i++)
        {
            slots[i] = (placements[i].Slot, placements[i].Meld.Tiles[placements[i].TileIndex].Id);
        }

        return slots;
    }

    /// <summary>副露的一张牌：槽位 + 所属组 + 组内牌位（绘制时要按暗杠规则决定牌面 / 牌背）。</summary>
    public readonly record struct MeldTilePlacement(RectF Slot, MeldView Meld, int TileIndex);

    /// <summary>
    /// 某家副露的逐张槽位与所属副露组（与 <see cref="MeldTileSlots"/> 同一套几何）。
    /// 副露按 2 行堆叠：每行最多 <see cref="MeldGroupsPerRow"/> 组，行沿跨轴方向排在牌面上，
    /// 4 组副露也只占两行、不往画布外拉长。
    /// </summary>
    public static MeldTilePlacement[] MeldTilePlacements(TableLayoutSpec l, SeatView seat, int index)
    {
        var melds = (seat.Melds ?? Array.Empty<MeldView>()).Where(m => m.Tiles is { Count: > 0 }).ToArray();
        if (melds.Length == 0)
        {
            return Array.Empty<MeldTilePlacement>();
        }

        var frame = l.HandRects[index];
        var (horizontal, _, rightDir) = TableLayoutSpec.Axis(index);
        bool self = index == 0;

        int n = seat.Hand.Count > 0 ? seat.Hand.Count : seat.HandCount;
        if (n <= 0)
        {
            n = 13;
        }

        var tiles = HandTileRects(l, seat, index);
        float total = n * l.HandW + (n - 1) * l.HandGap
                      + (self && n > l.HandTiles ? l.DrawnGap - l.HandGap : 0f);
        float start = horizontal ? tiles[0].X : tiles[0].Y;

        float slotAlong = l.MeldW;
        float slotCross = l.MeldH;
        int rows = Math.Max(1, l.MeldRows);
        int perRow = Math.Max(1, l.MeldGroupsPerRow);
        float blockDepth = rows * slotCross + (rows - 1) * l.MeldRowGap;
        float crossBase = (horizontal ? frame.CenterY : frame.CenterX) - blockDepth / 2f + slotCross / 2f;

        var byRow = new List<MeldView>[rows];
        for (int r = 0; r < rows; r++)
        {
            byRow[r] = new List<MeldView>();
        }

        for (int g = 0; g < melds.Length; g++)
        {
            byRow[Math.Min(g / perRow, rows - 1)].Add(melds[g]);
        }

        var list = new List<MeldTilePlacement>();
        for (int r = 0; r < rows; r++)
        {
            var rowMelds = byRow[r];
            if (rowMelds.Count == 0)
            {
                continue;
            }

            float rowLen = RowLength(rowMelds, l);
            float cross = crossBase + r * (slotCross + l.MeldRowGap);
            float cursor = rightDir > 0
                ? start + total + l.HandMeldGap
                : start - l.HandMeldGap - rowLen;

            foreach (var m in rowMelds)
            {
                cursor += l.MeldPad;
                for (int t = 0; t < m.Tiles.Count; t++)
                {
                    var slot = horizontal
                        ? new RectF(cursor, cross - slotCross / 2f, slotAlong, slotCross)
                        : new RectF(cross - slotCross / 2f, cursor, slotCross, slotAlong);

                    list.Add(new MeldTilePlacement(slot, m, t));
                    cursor += l.MeldW + l.MeldGap;
                }

                cursor -= l.MeldGap;
                cursor += l.MeldPad + l.MeldGroupGap;
            }
        }

        return list.ToArray();
    }

    /// <summary>一行副露组沿主轴的占长（与绘制同一套数）。</summary>
    private static float RowLength(IReadOnlyList<MeldView> row, TableLayoutSpec l)
    {
        float sum = 0f;
        foreach (var m in row)
        {
            sum += l.MeldPad * 2f + m.Tiles.Count * l.MeldW
                   + (m.Tiles.Count - 1) * l.MeldGap + l.MeldGroupGap;
        }

        return sum - l.MeldGroupGap;
    }

    /// <summary>
    /// 中央板上的动态信息：局数 / 余牌 / 本场·场赏 / 实时点数顺位 / 风位小方块。
    /// 底板已在静态层里画好，这里只叠文字与小方框（皮肤提供按钮区域时，风位小方块也换成皮肤九宫格）。
    /// </summary>
    private static void DrawCenterInfo(ICanvas c, TableViewModel vm, RectF p)
    {
        TableSkin skin = SkinStore.Current;
        c.DrawText(UiText.Tr(vm.RoundLabel), new RectF(p.X, p.CenterY - 62, p.W, 34),
            Color.Rgb(140, 246, 236), new TextStyle(26, Bold: true, Center: true));
        c.DrawText(L.T("app.table.center.wall", ("count", vm.WallCount)), new RectF(p.X, p.CenterY - 18, p.W, 30),
            Color.Rgb(150, 236, 226), new TextStyle(21, Center: true));
        c.DrawText(L.T("app.table.center.honbaKyotaku", ("honba", vm.Honba), ("kyotaku", vm.Kyotaku)), new RectF(p.X, p.CenterY + 20, p.W, 24),
            Color.Rgb(180, 214, 208), new TextStyle(15, Center: true));
        // 实时点数与顺位：2×2 排在中央面板下半部。
        var ordered = Enumerable.Range(0, 4)
            .OrderByDescending(i => Seat(vm, i).Score)
            .ThenBy(i => i)
            .ToArray();
        var rank = new int[4];
        for (int r = 0; r < ordered.Length; r++)
        {
            rank[ordered[r]] = r + 1;
        }

        for (int i = 0; i < 4; i++)
        {
            var seat = Seat(vm, i);
            float x = p.CenterX - 188f + (i % 2) * 198f;
            float y = p.CenterY + 50f + (i / 2) * 28f;
            bool first = rank[i] == 1;
            bool human = seat.IsHuman;
            string text = rank[i].ToString(System.Globalization.CultureInfo.InvariantCulture)
                          + "  " + UiText.Tr(seat.Wind) + (human ? L.T("app.table.hud.you") : string.Empty) + "  " + seat.Score;
            c.DrawText(text, new RectF(x, y, 188, 24),
                first ? Color.Rgb(255, 226, 140) : human ? Color.Rgb(210, 236, 220) : Color.Rgb(176, 200, 188),
                new TextStyle(14, Bold: first || human, Center: false));
        }


        const float s = 38f, m = 14f;
        var winds = new[] { Seat(vm, 0).Wind, Seat(vm, 1).Wind, Seat(vm, 2).Wind, Seat(vm, 3).Wind };
        Wind(c, skin, WindShort(winds[0]), new RectF(p.X + m, p.Bottom - m - s, s, s));
        Wind(c, skin, WindShort(winds[1]), new RectF(p.Right - m - s, p.Bottom - m - s, s, s));
        Wind(c, skin, WindShort(winds[2]), new RectF(p.Right - m - s, p.Y + m, s, s));
        Wind(c, skin, WindShort(winds[3]), new RectF(p.X + m, p.Y + m, s, s));
    }

    private static string WindShort(string wind) => UiText.IsEnglish
        ? wind switch
        {
            "东" => "E",
            "南" => "S",
            "西" => "W",
            "北" => "N",
            _ => wind,
        }
        : wind;

    private static void Wind(ICanvas c, TableSkin skin, string w, RectF r)
    {
        if (!skin.TryDrawButton(c, r))
        {
            c.FillRoundRect(r, 8, Color.Rgb(30, 36, 42));
            c.StrokeRoundRect(r, 8, Color.Rgb(120, 132, 140), 1.5f);
        }

        c.DrawText(w, r, Color.Rgb(240, 244, 240), new TextStyle(21, Bold: true, Center: true));
    }

    private void DrawRivers(ICanvas c, TableViewModel vm, TableLayoutSpec l)
    {
        for (int seat = 0; seat < 4; seat++)
        {
            var view = Seat(vm, seat);
            if (view.Discards.Count == 0)
            {
                continue;
            }

            var g = l.River(seat);
            int count = Math.Min(view.Discards.Count, l.RiverCols * l.RiverRows);

            for (int i = 0; i < count; i++)
            {
                int row = i / l.RiverCols;
                int col = i % l.RiverCols;
                float x = g.Origin.X + col * g.dxCol + row * g.dxRow;
                float y = g.Origin.Y + col * g.dyCol + row * g.dyRow;

                var slot = Math.Abs(g.angle) == 90f
                    ? new RectF(x, y, l.RiverH, l.RiverW)
                    : new RectF(x, y, l.RiverW, l.RiverH);

                if (i == count - 1)
                {
                    c.FillRoundRect(slot.Inflate(2), 4, Color.Rgb(255, 210, 96).WithAlpha(80));
                }

                Rot(c, view.Discards[i].Id, slot.CenterX, slot.CenterY, g.angle, l.RiverW, l.RiverH);
            }
        }
    }

    /// <summary>和牌后暗杠整组的 30% 黑遮罩（0.3 × 255 ≈ 77）。</summary>
    private const byte AnkanMaskAlpha = 77;

    private void DrawSeat(ICanvas c, TableViewModel vm, SeatView seat, int index, TableLayoutSpec l, int hover = -1,
        double time = 0)
    {
        var (horizontal, angle, _) = TableLayoutSpec.Axis(index);
        bool self = index == 0;

        // 手牌翻开条件：自己恒可见；流局 / 和牌后四家全翻（流局必须翻牌）。
        bool reveal = self || vm.RevealHands;

        int n = seat.Hand.Count > 0 ? seat.Hand.Count : seat.HandCount;
        if (n <= 0)
        {
            n = 13;
        }

        var tiles = HandTileRects(l, seat, index);

        for (int i = 0; i < tiles.Count; i++)
        {
            var r = tiles[i];
            float lift = i == hover ? 10f : 0f;
            float cx = r.CenterX;
            float cy = r.CenterY - lift;

            if (reveal && i < seat.Hand.Count)
            {
                // 牌面始终按未旋转的 50×68 画，旋转交给 Transform
                Rot(c, seat.Hand[i].Id, cx, cy, angle, l.HandW, l.HandH);
            }
            else
            {
                Back(c, cx, cy, r.W, r.H);
            }

            if (i == hover)
            {
                float depth = Metrics.HasTileDepth ? Metrics.TileThickness * TileDepthScale(r.W, r.H) : 0f;
                c.StrokeRoundRect(new RectF(cx - r.W / 2f, cy - r.H / 2f, r.W + depth, r.H + depth),
                    4, Color.Rgb(255, 228, 120), 2f);
            }
        }

        // 流局灰显：未听牌家手牌盖 30% 黑遮罩。
        if (seat.Noten)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                c.FillRoundRect(tiles[i].Inflate(1f), 4, Color.Black.WithAlpha(AnkanMaskAlpha));
            }
        }

        // 报听锁手灰显：锁手不允许打的牌盖 30% 黑。
        if (seat.IsCalled && seat.LegalHand.Count == tiles.Count)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                if (!seat.LegalHand[i])
                {
                    c.FillRoundRect(tiles[i].Inflate(1f), 4, Color.Black.WithAlpha(AnkanMaskAlpha));
                }
            }
        }

        // 副露：暗杠对局中只翻第 2、4 张；结算态整组翻开并盖 30% 黑遮罩。
        bool afterWin = vm.InSettlement;
        Dictionary<MeldView, RectF>? masked = null;
        foreach (var placement in MeldTilePlacements(l, seat, index))
        {
            var slot = placement.Slot;
            var meld = placement.Meld;
            if (meld.IsFaceUp(placement.TileIndex, afterWin))
            {
                Rot(c, meld.Tiles[placement.TileIndex].Id, slot.CenterX, slot.CenterY, angle, l.MeldW, l.MeldH);
            }
            else
            {
                Back(c, slot.CenterX, slot.CenterY, slot.W, slot.H);
            }

            if (meld.IsAnkan && (afterWin || meld.RevealedAfterWin))
            {
                masked ??= new Dictionary<MeldView, RectF>();
                masked[meld] = masked.TryGetValue(meld, out var acc)
                    ? RectF.FromLTRB(
                        Math.Min(acc.Left, slot.Left),
                        Math.Min(acc.Top, slot.Top),
                        Math.Max(acc.Right, slot.Right),
                        Math.Max(acc.Bottom, slot.Bottom))
                    : slot;
            }

            // 取牌脉光：被吃/碰/明杠的那张牌盖 50% 黄色遮罩，0–50–0 呼吸（500ms）。
            if (placement.TileIndex == meld.ClaimedIndex)
            {
                float glow = PulseAlpha(time);
                if (glow > 0f)
                {
                    c.FillRoundRect(slot.Inflate(1.5f), 4, Color.Rgb(255, 210, 96).WithAlpha((byte)(glow * 255)));
                }
            }
        }

        if (masked is not null)
        {
            foreach (var rect in masked.Values)
            {
                c.FillRoundRect(rect.Inflate(1f), 4, Color.Black.WithAlpha(AnkanMaskAlpha));
            }
        }
    }

    /// <summary>
    /// 在任意矩形里画一张正面牌（不旋转）：复用牌桌的牌身厚度 / 左上高光 / 投影，
    /// 供结算屏的手牌区直接画牌面。牌面始终按 <paramref name="rect"/> 缩放。
    /// </summary>
    /// <param name="depth">false = 只画平面牌面（密集小牌用，免得厚度边糊成一片）。</param>
    public static void DrawTileFace(ICanvas c, string tileId, RectF rect,
        TableRenderStyle style = TableRenderStyle.Flat, bool depth = true)
    {
        var m = TableRenderMetrics.For(style);
        float cx = rect.CenterX;
        float cy = rect.CenterY;

        if (depth)
        {
            DrawTileShadow(c, cx, cy, 0f, rect.W, rect.H, m);
        }

        c.Save();
        c.Translate(cx, cy);
        if (depth)
        {
            DrawTileSide(c, rect.W, rect.H, m, back: false);
        }

        if (depth)
        {
            DrawTileHighlight(c, rect.W, rect.H, m);
        }

        var face = new RectF(-rect.W / 2f, -rect.H / 2f, rect.W, rect.H);
        c.DrawTile(tileId, face);
        if (m.FaceBevelAlpha > 0)
        {
            c.StrokeRoundRect(face, 4f, Color.Rgb(255, 252, 235).WithAlpha(m.FaceBevelAlpha), 0.8f);
        }

        c.Restore();
    }

    private void Rot(ICanvas c, string id, float cx, float cy, float angle, float w, float h)    {
        var m = Metrics;
        DrawTileShadow(c, cx, cy, angle, w, h, m);

        c.Save();
        c.Translate(cx, cy);
        if (Math.Abs(angle) > 0.01f)
        {
            c.RotateDegrees(angle);
        }

        DrawTileSide(c, w, h, m, back: false);
        DrawTileHighlight(c, w, h, m);
        var face = new RectF(-w / 2f, -h / 2f, w, h);
        c.DrawTile(id, face);
        if (m.FaceBevelAlpha > 0)
        {
            c.StrokeRoundRect(face, 4f, Color.Rgb(255, 252, 235).WithAlpha(m.FaceBevelAlpha), 0.8f);
        }

        c.Restore();
    }

    private void Back(ICanvas c, float cx, float cy, float w, float h)
    {
        var m = Metrics;
        DrawTileShadow(c, cx, cy, 0f, w, h, m);

        c.Save();
        c.Translate(cx, cy);
        DrawTileSide(c, w, h, m, back: true);
        DrawTileHighlight(c, w, h, m);

        var r = new RectF(-w / 2f, -h / 2f, w, h);
        c.FillRoundRect(r, 4, Color.Rgb(46, 138, 198));

        c.Restore();
    }

    /// <summary>
    /// 牌身左上受光边。贴在正面轮廓外侧（右下是侧面与投影，正好反衬），
    /// 读作实体牌被左上光打亮的一条亮边；在正面之前画，正面圆角处还能透出一点。
    /// </summary>
    private static void DrawTileHighlight(ICanvas c, float w, float h, TableRenderMetrics m)
    {
        if (m.TileHighlightAlpha == 0)
        {
            return;
        }

        float edge = Math.Max(1.2f, 2.2f * TileDepthScale(w, h));
        var lit = Color.Rgb(255, 252, 236).WithAlpha(m.TileHighlightAlpha);
        c.FillRect(new RectF(-w / 2f - edge, -h / 2f, edge, h), lit);
        c.FillRect(new RectF(-w / 2f, -h / 2f - edge, w + edge, edge), lit);
    }

    /// <summary>
    /// 牌身右下侧的厚度。轴向与牌面一致（在旋转后的局部坐标里画），
    /// 副露小牌按尺寸缩小厚度，免得 24×33 的牌顶着一圈 8px 边。
    /// </summary>
    private static void DrawTileSide(ICanvas c, float w, float h, TableRenderMetrics m, bool back)
    {
        if (!m.HasTileDepth)
        {
            return;
        }

        float scale = TileDepthScale(w, h);
        float thickness = m.TileThickness * scale;
        var side = new RectF(-w / 2f, -h / 2f, w + thickness, h + thickness);
        c.FillLinearGradient(side, 4f + thickness,
            back ? m.TileBackSideTop : m.TileSideTop,
            back ? m.TileBackSideBottom : m.TileSideBottom);
        c.StrokeRoundRect(side, 4f + thickness, Color.Rgb(42, 34, 22).WithAlpha(110), 0.8f);
    }

    /// <summary>投影按牌面在屏幕上的外接矩形画（全局光从左上打），不随牌旋转反向。</summary>
    private static void DrawTileShadow(ICanvas c, float cx, float cy, float angle, float w, float h,
        TableRenderMetrics m)
    {
        if (!m.HasTileDepth)
        {
            return;
        }

        float scale = TileDepthScale(w, h);
        bool sideways = Math.Abs(Math.Abs(angle) % 180f - 90f) < 1f;
        float fw = sideways ? h : w;
        float fh = sideways ? w : h;
        var r = new RectF(
            cx - fw / 2f + (m.ShadowOffsetX * scale),
            cy - fh / 2f + (m.ShadowOffsetY * scale),
            fw,
            fh);
        c.FillShadow(r, 4f, m.ShadowBlur * scale, Color.Black.WithAlpha(m.ShadowAlpha));
    }

    /// <summary>牌面 50×68 为 1.0；小牌（副露 24×33）最薄也保留 0.5 的比例，避免完全看不出厚度。</summary>
    private static float TileDepthScale(float w, float h) =>
        Math.Clamp(Math.Min(w / 50f, h / 68f), 0.5f, 1f);

    private static SeatView Seat(TableViewModel vm, int i) =>
        i < vm.Seats.Count ? vm.Seats[i] : new SeatView { Index = i };
}
