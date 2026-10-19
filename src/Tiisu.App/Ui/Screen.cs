using Tiisu.App.Rendering;

namespace Tiisu.App.Ui;

/// <summary>一屏界面。所有屏共用同一张画布与同一套输入。</summary>
public abstract class Screen
{
    protected Screen(string id) => Id = id;

    public string Id { get; }

    /// <summary>逻辑坐标尺寸（与 BoardScene 一致：1280×720）。</summary>
    public const float Width = 1280f;
    public const float Height = 720f;

    public virtual void Update(double dt) { }

    public abstract void Draw(ICanvas c, double time);

    /// <summary>返回 true 表示事件被消费。</summary>
    public virtual bool OnPointerMove(float x, float y) => false;

    public virtual bool OnClick(float x, float y) => false;

    public virtual bool OnKey(Keys key) => false;
}
