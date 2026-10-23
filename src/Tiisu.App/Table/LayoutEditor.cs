using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Table;

/// <summary>
/// 牌桌布局编辑器：F2 进入/退出，拖拽 / 滚轮缩放 / 方向键微调 / Tab 切换，
/// S 保存到 <c>layout-custom.json</c>，R 两次确认恢复默认。
/// 绘制在牌桌之后叠加，只画半透明框 + 区域名 + 坐标，不参与游戏逻辑。
/// </summary>
public sealed class LayoutEditor
{
    private static readonly Color Accent = Color.Rgb(255, 206, 96);
    private static readonly Color Idle = Color.Rgb(120, 214, 255);
    private static readonly Color LabelText = Color.Rgb(226, 240, 248);
    private static readonly Color BannerFill = Color.Rgb(8, 14, 12).WithAlpha(214);

    private readonly TableLayoutSpec _layout;
    private readonly string? _directory;

    private bool _dragging;
    private float _dragX;
    private float _dragY;
    private RectF _dragStart;
    private int _restoreArmed;

    public LayoutEditor(TableLayoutSpec layout, string? directory = null)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _directory = directory;
    }

    public bool Active { get; private set; }

    public int Selected { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public IReadOnlyList<TableLayoutSpec.LayoutRegion> Regions => _layout.Regions;

    public TableLayoutSpec.LayoutRegion? Current =>
        Selected >= 0 && Selected < Regions.Count ? Regions[Selected] : null;

    public void Enter()
    {
        Active = true;
        _restoreArmed = 0;
        if (Selected >= Regions.Count)
        {
            Selected = 0;
        }

        Status = "编辑中";
    }

    public void Exit()
    {
        Active = false;
        _dragging = false;
        _restoreArmed = 0;
    }

    public void Toggle()
    {
        if (Active)
        {
            Exit();
        }
        else
        {
            Enter();
        }
    }

    /// <summary>键盘：返回 true 表示已消费。F2 由调用方先处理，这里也兼容。</summary>
    public bool OnKey(Keys key, bool shift = false)
    {
        if (!Active)
        {
            return false;
        }

        switch (key)
        {
            case Keys.F2:
            case Keys.Escape:
                Exit();
                return true;

            case Keys.Tab:
                Select((Selected + (shift ? -1 : 1) + Regions.Count) % Math.Max(1, Regions.Count));
                return true;

            case Keys.Left:
                MoveBy(shift ? -10f : -1f, 0f);
                return true;

            case Keys.Right:
                MoveBy(shift ? 10f : 1f, 0f);
                return true;

            case Keys.Up:
                MoveBy(0f, shift ? -10f : -1f);
                return true;

            case Keys.Down:
                MoveBy(0f, shift ? 10f : 1f);
                return true;

            case Keys.S:
                Save();
                return true;

            case Keys.R:
                RestoreDefault();
                return true;

            default:
                return false;
        }
    }

    public void Select(int index)
    {
        _dragging = false;
        _restoreArmed = 0;
        if (Regions.Count > 0)
        {
            Selected = (index % Regions.Count + Regions.Count) % Regions.Count;
        }
    }

    public void SelectNext() => Select(Selected + 1);

    /// <summary>选中项平移（方向键 / 拖拽共用）。</summary>
    public void MoveBy(float dx, float dy)
    {
        if (Current is not { } region || (dx == 0f && dy == 0f))
        {
            return;
        }

        var r = region.Rect;
        SetRect(region, new RectF(r.X + dx, r.Y + dy, r.W, r.H));
    }

    /// <summary>左上角锚点缩放；<paramref name="heightOnly"/> 时只改高度（Shift + 滚轮）。</summary>
    public void ScaleBy(float factor, bool heightOnly = false)
    {
        if (Current is not { } region)
        {
            return;
        }

        var r = region.Rect;
        float w = Math.Max(8f, heightOnly ? r.W : r.W * factor);
        float h = Math.Max(8f, r.H * factor);
        SetRect(region, new RectF(r.X, r.Y, w, h));
    }

    /// <summary>鼠标按下：命中哪个区域就选中它并开始拖拽（多个重叠时取面积最小的）。</summary>
    public bool PointerDown(float x, float y)
    {
        if (!Active)
        {
            return false;
        }

        int hit = HitTest(x, y);
        if (hit < 0)
        {
            return false;
        }

        Select(hit);
        _dragging = true;
        _dragX = x;
        _dragY = y;
        _dragStart = Regions[Selected].Rect;
        return true;
    }

    public void PointerMove(float x, float y)
    {
        if (!Active || !_dragging || Current is not { } region)
        {
            return;
        }

        SetRect(region, new RectF(
            _dragStart.X + (x - _dragX),
            _dragStart.Y + (y - _dragY),
            _dragStart.W,
            _dragStart.H));
    }

    public void PointerUp() => _dragging = false;

    public void Save()
    {
        try
        {
            CustomLayoutStore.Save(_layout, _directory);
            Status = "已保存 → " + CustomLayoutStore.PathFor(_directory);
        }
        catch (Exception ex)
        {
            Status = "保存失败：" + ex.Message;
        }
    }

    /// <summary>第一次按 R 只提醒，再按一次才删文件 + 回默认。</summary>
    public void RestoreDefault()
    {
        if (_restoreArmed == 0)
        {
            _restoreArmed = 1;
            Status = "再按一次 R 确认恢复默认";
            return;
        }

        _restoreArmed = 0;
        try
        {
            CustomLayoutStore.Delete(_directory);
            YogaTableLayout.Apply(_layout);
            _layout.OverridesActive = false;
            Selected = Math.Min(Selected, Math.Max(0, Regions.Count - 1));
            Status = "已恢复默认";
        }
        catch (Exception ex)
        {
            Status = "恢复失败：" + ex.Message;
        }
    }

    /// <summary>叠加绘制（半透明填充 + 名称 + 坐标；选中项金色高亮）。</summary>
    public void Draw(ICanvas c)
    {
        if (!Active)
        {
            return;
        }

        int count = Regions.Count;
        var labels = new List<RectF>(count);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                // 选中项最后画，保证高亮框压在别的区域上。
                bool selected = i == Selected;
                if (selected != (pass == 1))
                {
                    continue;
                }

                DrawRegion(c, Regions[i], selected, labels);
            }
        }

        DrawBanner(c);
    }

    private void DrawRegion(ICanvas c, TableLayoutSpec.LayoutRegion region, bool selected, List<RectF> labels)
    {
        var r = region.Rect;
        if (r.W <= 0f || r.H <= 0f)
        {
            return;
        }

        c.FillRect(r, (selected ? Accent : Idle).WithAlpha(selected ? (byte)44 : (byte)20));
        c.StrokeRoundRect(r, 4, selected ? Accent : Idle.WithAlpha(150), selected ? 2.5f : 1f);

        if (selected)
        {
            // 选中项加四角标记，拖拽时看得见边界。
            float t = 10f;
            c.FillRect(new RectF(r.X, r.Y, t, 2f), Accent);
            c.FillRect(new RectF(r.X, r.Y, 2f, t), Accent);
            c.FillRect(new RectF(r.Right - t, r.Y, t, 2f), Accent);
            c.FillRect(new RectF(r.Right - 2f, r.Y, 2f, t), Accent);
            c.FillRect(new RectF(r.X, r.Bottom - 2f, t, 2f), Accent);
            c.FillRect(new RectF(r.X, r.Bottom - t, 2f, t), Accent);
            c.FillRect(new RectF(r.Right - t, r.Bottom - 2f, t, 2f), Accent);
            c.FillRect(new RectF(r.Right - 2f, r.Bottom - t, 2f, t), Accent);
        }

        string text = string.Create(CultureInfo.InvariantCulture,
            $"{region.Id}  {r.X:0},{r.Y:0}  {r.W:0}×{r.H:0}");
        var style = new TextStyle(selected ? 14 : 12, Bold: selected, Center: false);
        float textW = Math.Min(360f, Math.Max(150f, c.MeasureText(text, style) + 14f));
        float textH = selected ? 22f : 18f;

        // 标签优先放区域上沿外侧，贴边时退到内侧；和已有标签相撞就往下让，尽量不压牌。
        float lx = Math.Max(2f, Math.Min(r.X, _layout.CanvasW - textW - 2f));
        float ly = r.Y >= textH + 2f ? r.Y - textH - 1f : r.Y + 1f;
        var box = new RectF(lx, ly, textW, textH);
        for (int attempt = 0; attempt < 16 && OverlapsAny(box, labels); attempt++)
        {
            ly += textH + 2f;
            if (ly + textH > _layout.CanvasH - 2f)
            {
                ly = r.Y + 1f;
            }

            box = new RectF(lx, ly, textW, textH);
        }

        labels.Add(box);
        c.FillRoundRect(box, 3, Color.Rgb(0, 0, 0).WithAlpha(selected ? (byte)210 : (byte)160));
        c.DrawText(text, new RectF(box.X + 6f, box.Y + (selected ? 2f : 1f), box.W - 8f, textH - 2f),
            selected ? Accent : LabelText, style);
    }

    private static bool OverlapsAny(RectF box, List<RectF> placed)
    {
        foreach (var other in placed)
        {
            if (box.X < other.Right && box.Right > other.X
                && box.Y < other.Bottom && box.Bottom > other.Y)
            {
                return true;
            }
        }

        return false;
    }

    private void DrawBanner(ICanvas c)
    {
        const string hint = "布局编辑 F2 退出 · Tab 切换 · 拖拽移动 · 滚轮缩放（Shift 只改高） · 方向键 1px（Shift 10px） · S 保存 · R 恢复默认";
        var hintStyle = new TextStyle(14, Center: false);
        float hintW = Math.Min(_layout.CanvasW - 32f, c.MeasureText(hint, hintStyle) + 24f);
        var hintBox = new RectF(16f, 60f, hintW, 26f);
        c.FillRoundRect(hintBox, 6, BannerFill);
        c.DrawText(hint, new RectF(hintBox.X + 10f, hintBox.Y + 4f, hintBox.W - 12f, 20f),
            Color.Rgb(210, 232, 224), hintStyle);

        string selection;
        if (Current is { } region)
        {
            var r = region.Rect;
            selection = string.Create(CultureInfo.InvariantCulture,
                $"选中 {Selected + 1}/{Regions.Count}  {region.Id} · {region.Note}  x={r.X:0} y={r.Y:0} w={r.W:0} h={r.H:0}");
        }
        else
        {
            selection = "选中 0/0";
        }

        if (Status.Length > 0)
        {
            selection += "    " + Status;
        }

        var statusStyle = new TextStyle(14, Bold: true, Center: false);
        float statusW = Math.Min(_layout.CanvasW - 32f, c.MeasureText(selection, statusStyle) + 24f);
        var statusBox = new RectF(16f, 90f, statusW, 26f);
        c.FillRoundRect(statusBox, 6, BannerFill);
        c.DrawText(selection, new RectF(statusBox.X + 10f, statusBox.Y + 4f, statusBox.W - 12f, 20f),
            Accent, statusStyle);
    }

    private void SetRect(TableLayoutSpec.LayoutRegion region, RectF rect)
    {
        if (rect.W < 8f)
        {
            rect = rect with { W = 8f };
        }

        if (rect.H < 8f)
        {
            rect = rect with { H = 8f };
        }

        region.Set(rect);
        _layout.OverridesActive = true;
        _restoreArmed = 0;
    }

    private int HitTest(float x, float y)
    {
        int best = -1;
        float bestArea = float.MaxValue;
        for (int i = 0; i < Regions.Count; i++)
        {
            var r = Regions[i].Rect;
            if (r.W <= 0f || r.H <= 0f || !r.Contains(x, y))
            {
                continue;
            }

            float area = r.W * r.H;
            if (area < bestArea)
            {
                bestArea = area;
                best = i;
            }
        }

        return best;
    }
}
