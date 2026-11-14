using Tiisu.App.Rendering;

namespace Tiisu.App.Table;

/// <summary>
/// 牌桌渲染组件：只负责「一块方形牌桌画面」，不拥有窗口、HUD、动作栏或输入焦点。
/// 2D（TableSceneSkia）现在实现；未来 3D 场景实现同一接口即可替换，外层 HUD/输入不用改。
/// </summary>
public interface ITableScene
{
    void Draw(ICanvas c, TableViewModel vm, TableLayoutSpec layout, int hoverHand = -1, double time = 0);
}
