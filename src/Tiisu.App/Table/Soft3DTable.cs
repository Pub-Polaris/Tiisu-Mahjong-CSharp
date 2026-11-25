using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Table;

/// <summary>
/// Soft-3D 桌面：几何体只构建一次，投影/着色只做一次（由 <see cref="StaticLayerCache{TKey}"/> 缓存），
/// 之后每帧只贴缓存图。动态牌仍然是 2D，不进入软 3D 热路径。
///
/// 顶面/侧面都按「整块四边形投影后一次填充」画，不把四边形拆成两个三角形，
/// 避免抗锯齿边界出现三角面之间的亮线/暗线。
/// 参考图是 2002 年的预渲染 2D，FOV 不可知；25° 只是本实验选定的相机，不是参考图测出的角度。
/// </summary>
public static class Soft3DTable
{
    public const float CameraPitchDegrees = 25f;
    public const float CameraFovDegrees = 55f;

    private static readonly Software3D.Camera3D Camera = BuildCamera();

    public static void Draw(ICanvas c, TableLayoutSpec layout)
    {
        c.FillLinearGradient(new RectF(0, 0, layout.CanvasW, layout.CanvasH), 0,
            Color.Rgb(14, 19, 22), Color.Rgb(7, 10, 12));

        // 地面（整块四边形）
        DrawQuad(c, layout,
            new Software3D.Vertex3(-2f, -0.105f, -2f),
            new Software3D.Vertex3(2f, -0.105f, -2f),
            new Software3D.Vertex3(2f, -0.105f, 2f),
            new Software3D.Vertex3(-2f, -0.105f, 2f),
            Color.Rgb(20, 25, 27));

        // 桌侧
        const float half = 0.5f;
        const float bottom = -0.10f;
        DrawSide(c, layout, -half, -half, half, -half, bottom, Color.Rgb(48, 33, 24));
        DrawSide(c, layout, half, -half, half, half, bottom, Color.Rgb(36, 24, 18));
        DrawSide(c, layout, half, half, -half, half, bottom, Color.Rgb(48, 33, 24));
        DrawSide(c, layout, -half, half, -half, -half, bottom, Color.Rgb(36, 24, 18));

        // 木框整块 → 绿呢 → 中央板；每层一个四边形，避免内部三角接缝。
        DrawQuad(c, layout,
            new Software3D.Vertex3(-half, 0.001f, -half),
            new Software3D.Vertex3(half, 0.001f, -half),
            new Software3D.Vertex3(half, 0.001f, half),
            new Software3D.Vertex3(-half, 0.001f, half),
            Color.Rgb(84, 55, 38));

        const float felt = 0.405f;
        DrawQuad(c, layout,
            new Software3D.Vertex3(-felt, 0.002f, -felt),
            new Software3D.Vertex3(felt, 0.002f, -felt),
            new Software3D.Vertex3(felt, 0.002f, felt),
            new Software3D.Vertex3(-felt, 0.002f, felt),
            Color.Rgb(31, 78, 57));

        const float plate = 0.16f;
        DrawQuad(c, layout,
            new Software3D.Vertex3(-plate, 0.003f, -plate),
            new Software3D.Vertex3(plate, 0.003f, -plate),
            new Software3D.Vertex3(plate, 0.003f, plate),
            new Software3D.Vertex3(-plate, 0.003f, plate),
            Color.Rgb(47, 57, 72));
    }

    private static void DrawSide(ICanvas c, TableLayoutSpec layout,
        float x0, float z0, float x1, float z1, float bottom, Color color)
    {
        DrawQuad(c, layout,
            new Software3D.Vertex3(x0, 0f, z0),
            new Software3D.Vertex3(x1, 0f, z1),
            new Software3D.Vertex3(x1, bottom, z1),
            new Software3D.Vertex3(x0, bottom, z0),
            color);
    }

    private static void DrawQuad(ICanvas c, TableLayoutSpec layout,
        Software3D.Vertex3 a, Software3D.Vertex3 b, Software3D.Vertex3 c3, Software3D.Vertex3 d,
        Color color)
    {
        if (!Software3D.TryProject(Camera, a, layout.CanvasW, layout.CanvasH, out CanvasPoint pa, out _)
            || !Software3D.TryProject(Camera, b, layout.CanvasW, layout.CanvasH, out CanvasPoint pb, out _)
            || !Software3D.TryProject(Camera, c3, layout.CanvasW, layout.CanvasH, out CanvasPoint pc, out _)
            || !Software3D.TryProject(Camera, d, layout.CanvasW, layout.CanvasH, out CanvasPoint pd, out _))
        {
            return;
        }

        c.FillPolygon(new[] { pa, pb, pc, pd }, color);
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
}
