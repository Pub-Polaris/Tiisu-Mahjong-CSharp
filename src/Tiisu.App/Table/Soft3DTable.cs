using System.Collections.Generic;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Table;

/// <summary>
/// Soft-3D 桌面：几何体只构建一次，投影/着色只做一次（由 <see cref="StaticLayerCache{TKey}"/> 缓存），
/// 之后每帧只贴缓存图。动态牌仍然是 2D，不进入软 3D 热路径。
///
/// 参考图的俯视角约 25°（4:3 画面、垂直 FOV 按 55° 估算），相机固定，不跟窗口/快照变化。
/// </summary>
public static class Soft3DTable
{
    public const float CameraPitchDegrees = 25f;
    public const float CameraFovDegrees = 55f;

    private static readonly IReadOnlyList<Software3D.Triangle3D> Sides = BuildSides();
    private static readonly IReadOnlyList<Software3D.Triangle3D> Floor = BuildFloor();
    private static readonly IReadOnlyList<Software3D.Triangle3D> WoodTop = BuildWoodTop();
    private static readonly IReadOnlyList<Software3D.Triangle3D> Felt = BuildFelt();
    private static readonly IReadOnlyList<Software3D.Triangle3D> Plate = BuildPlate();
    private static readonly Software3D.Camera3D Camera = BuildCamera();

    public static void Draw(ICanvas c, TableLayoutSpec layout)
    {
        // 背景与桌体分层画。每层单独投影/排序，避免画家算法把中央板压到绿呢下面。
        c.FillLinearGradient(new RectF(0, 0, layout.CanvasW, layout.CanvasH), 0,
            Color.Rgb(14, 19, 22), Color.Rgb(7, 10, 12));

        Software3D.Render(c, Floor, Camera, layout.CanvasW, layout.CanvasH);
        Software3D.Render(c, Sides, Camera, layout.CanvasW, layout.CanvasH);
        Software3D.Render(c, WoodTop, Camera, layout.CanvasW, layout.CanvasH);
        Software3D.Render(c, Felt, Camera, layout.CanvasW, layout.CanvasH);
        Software3D.Render(c, Plate, Camera, layout.CanvasW, layout.CanvasH);
    }

    private static Software3D.Camera3D BuildCamera()
    {
        // 牌桌在 XZ 平面，Y 向上；相机从 +Z 方向俯视 25°。
        // 距离按 1400 画布反推，使桌面外框接近 TableLayoutSpec.Margin 内缩的 1368×1368。
        const float distance = 1.32f;
        float pitch = CameraPitchDegrees * System.MathF.PI / 180f;
        var eye = new Software3D.Vertex3(
            0f,
            distance * System.MathF.Sin(pitch),
            distance * System.MathF.Cos(pitch));
        return new Software3D.Camera3D(
            eye,
            new Software3D.Vertex3(0f, 0f, 0f),
            new Software3D.Vertex3(0f, 1f, 0f),
            CameraFovDegrees,
            Near: 0.05f,
            Far: 50f);
    }

    private static IReadOnlyList<Software3D.Triangle3D> BuildFloor()
    {
        const float half = 2f;
        const float y = -0.105f;
        var color = Color.Rgb(20, 25, 27);
        var list = new List<Software3D.Triangle3D>(2);
        Quad(list, -half, -half, half, half, y, color);
        return list;
    }

    private static IReadOnlyList<Software3D.Triangle3D> BuildSides()
    {
        const float half = 0.5f;
        const float bottom = -0.10f;
        var wood = Color.Rgb(48, 33, 24);
        var woodDark = Color.Rgb(36, 24, 18);
        var list = new List<Software3D.Triangle3D>(16);

        void Side(Software3D.Vertex3 a, Software3D.Vertex3 b, Color color)
        {
            var c = new Software3D.Vertex3(a.X, bottom, a.Z);
            var d = new Software3D.Vertex3(b.X, bottom, b.Z);
            list.Add(new Software3D.Triangle3D(a, b, c, color));
            list.Add(new Software3D.Triangle3D(b, d, c, color));
        }

        Side(new Software3D.Vertex3(-half, 0f, -half), new Software3D.Vertex3(half, 0f, -half), wood);
        Side(new Software3D.Vertex3(half, 0f, -half), new Software3D.Vertex3(half, 0f, half), woodDark);
        Side(new Software3D.Vertex3(half, 0f, half), new Software3D.Vertex3(-half, 0f, half), wood);
        Side(new Software3D.Vertex3(-half, 0f, half), new Software3D.Vertex3(-half, 0f, -half), woodDark);
        return list;
    }

    private static IReadOnlyList<Software3D.Triangle3D> BuildWoodTop()
    {
        const float half = 0.5f;
        const float felt = 0.405f;
        var woodTop = Color.Rgb(84, 55, 38);
        var list = new List<Software3D.Triangle3D>(16);

        void Quad(float x0, float z0, float x1, float z1, Color color)
        {
            var a = new Software3D.Vertex3(x0, 0.001f, z0);
            var b = new Software3D.Vertex3(x1, 0.001f, z0);
            var c = new Software3D.Vertex3(x1, 0.001f, z1);
            var d = new Software3D.Vertex3(x0, 0.001f, z1);
            list.Add(new Software3D.Triangle3D(a, b, c, color));
            list.Add(new Software3D.Triangle3D(a, c, d, color));
        }

        Quad(-half, -half, half, -felt, woodTop);
        Quad(-half, felt, half, half, woodTop);
        Quad(-half, -felt, -felt, felt, woodTop);
        Quad(felt, -felt, half, felt, woodTop);
        return list;
    }

    private static IReadOnlyList<Software3D.Triangle3D> BuildFelt()
    {
        const float felt = 0.405f;
        var color = Color.Rgb(31, 78, 57);
        var list = new List<Software3D.Triangle3D>(2);
        Quad(list, -felt, -felt, felt, felt, 0.002f, color);
        return list;
    }

    private static IReadOnlyList<Software3D.Triangle3D> BuildPlate()
    {
        const float plate = 0.16f;
        var color = Color.Rgb(47, 57, 72);
        var list = new List<Software3D.Triangle3D>(2);
        Quad(list, -plate, -plate, plate, plate, 0.003f, color);
        return list;
    }

    private static void Quad(
        List<Software3D.Triangle3D> list, float x0, float z0, float x1, float z1, float y, Color color)
    {
        var a = new Software3D.Vertex3(x0, y, z0);
        var b = new Software3D.Vertex3(x1, y, z0);
        var c = new Software3D.Vertex3(x1, y, z1);
        var d = new Software3D.Vertex3(x0, y, z1);
        list.Add(new Software3D.Triangle3D(a, b, c, color));
        list.Add(new Software3D.Triangle3D(a, c, d, color));
    }
}
