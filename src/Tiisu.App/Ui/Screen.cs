using Tiisu.App.Rendering;

namespace Tiisu.App.Ui;

/// <summary>一屏界面。所有屏共用同一张画布与同一套输入。</summary>
public abstract class Screen
{
    protected Screen(string id) => Id = id;

    public string Id { get; }

    /// <summary>默认逻辑坐标尺寸（菜单屏使用）。牌桌屏会覆盖成 1200×1200。</summary>
    public const float Width = 1280f;
    public const float Height = 720f;

    /// <summary>本屏的逻辑画布尺寸。宿主按它做 letterbox 缩放与输入换算。</summary>
    public virtual float CanvasWidth => Width;

    public virtual float CanvasHeight => Height;

    /// <summary>letterbox 留边用这个颜色填充，与屏自身背景一致才不显黑边。</summary>
    public virtual Rendering.Color Background => Rendering.Color.Black;

    public virtual void Update(double dt) { }

    public abstract void Draw(ICanvas c, double time);

    /// <summary>返回 true 表示事件被消费。</summary>
    public virtual bool OnPointerMove(float x, float y) => false;

    public virtual bool OnClick(float x, float y) => false;

    public virtual bool OnKey(Keys key) => false;
}
