using System;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>
/// 独立水平滑条：支持点击跳值、按住左右拖动、方向键步进。
/// 只负责几何与绘制；业务值由 <see cref="ValueChanged"/> / <see cref="ValueCommitted"/> 回传。
/// </summary>
public sealed class Slider
{
    private const float TrackHeight = 4f;
    private const float KnobRadius = 8f;
    private const float HitPadding = 8f;

    public Slider(float min, float max, float value, Action<float>? valueChanged = null, Action? valueCommitted = null)
    {
        Min = min;
        Max = max;
        Value = Math.Clamp(value, min, max);
        ValueChanged = valueChanged;
        ValueCommitted = valueCommitted;
    }

    public float Min { get; }

    public float Max { get; }

    public float Value { get; private set; }

    /// <summary>整条滑条的可点击区域（行内右侧）。</summary>
    public RectF Bounds { get; private set; }

    public bool Dragging { get; private set; }

    public Action<float>? ValueChanged { get; set; }

    public Action? ValueCommitted { get; set; }

    public float Ratio => Max <= Min ? 0f : Math.Clamp((Value - Min) / (Max - Min), 0f, 1f);

    public void Layout(RectF bounds) => Bounds = bounds;

    public bool Contains(float x, float y) =>
        x >= Bounds.X - HitPadding && x <= Bounds.Right + HitPadding
        && y >= Bounds.Y - HitPadding && y <= Bounds.Bottom + HitPadding;

    public bool OnPointerDown(float x, float y)
    {
        if (!Contains(x, y))
        {
            return false;
        }

        Dragging = true;
        SetFromX(x);
        return true;
    }

    public bool OnPointerMove(float x, float y)
    {
        if (!Dragging)
        {
            return false;
        }

        SetFromX(x);
        return true;
    }

    public void OnPointerUp()
    {
        if (!Dragging)
        {
            return;
        }

        Dragging = false;
        ValueCommitted?.Invoke();
    }

    /// <summary>键盘/滚轮步进；每步一个单位，调用方决定步长。</summary>
    public void Step(float delta)
    {
        SetValue(Value + delta);
        ValueCommitted?.Invoke();
    }

    public void SetValue(float value, bool notify = true)
    {
        float next = Math.Clamp(value, Min, Max);
        if (Math.Abs(next - Value) < 0.0001f)
        {
            return;
        }

        Value = next;
        if (notify)
        {
            ValueChanged?.Invoke(Value);
        }
    }

    public void Draw(ICanvas c, bool selected, bool hover)
    {
        float knobs = KnobRadius;
        float trackX = Bounds.X + knobs;
        float trackW = Math.Max(0, Bounds.W - knobs * 2);
        float trackY = Bounds.CenterY - TrackHeight / 2f;

        c.FillRoundRect(new RectF(trackX, trackY, trackW, TrackHeight), TrackHeight / 2f,
            Color.Rgb(60, 78, 70));
        c.FillRoundRect(new RectF(trackX, trackY, trackW * Ratio, TrackHeight), TrackHeight / 2f,
            Color.Rgb(196, 168, 96));

        float knobX = trackX + trackW * Ratio;
        var knobColor = selected || hover || Dragging
            ? Color.Rgb(255, 244, 214)
            : Color.Rgb(196, 168, 96);
        c.FillCircle(knobX, Bounds.CenterY, knobs, knobColor);
    }

    private void SetFromX(float x)
    {
        float trackX = Bounds.X + KnobRadius;
        float trackW = Math.Max(1, Bounds.W - KnobRadius * 2);
        float ratio = Math.Clamp((x - trackX) / trackW, 0f, 1f);
        SetValue(Min + (Max - Min) * ratio);
    }
}
