using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Tiisu.App.Rendering;

/// <summary>
/// 极简软件 3D 光栅化：把三角形投影到 <see cref="ICanvas"/> 的 2D 逻辑坐标并填充。
/// <para>
/// <b>未启用</b>：当前游戏全部是 2D（SkiaSharp / Skia 只提供 2D 绘制，没有 3D 管线、
/// 深度缓冲或透视纹理映射）。这里只作为「以后需要一点 3D 效果」的备用方案：
/// 纯 CPU、画家算法、平面着色；三角形多了会明显吃 CPU，不要放进每帧主循环。
/// </para>
/// </summary>
public static class Software3D
{
    /// <summary>三维点（世界坐标）。</summary>
    public readonly record struct Vertex3(float X, float Y, float Z)
    {
        public static Vertex3 operator +(Vertex3 a, Vertex3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vertex3 operator -(Vertex3 a, Vertex3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vertex3 operator *(Vertex3 a, float k) => new(a.X * k, a.Y * k, a.Z * k);

        public static float Dot(Vertex3 a, Vertex3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vertex3 Cross(Vertex3 a, Vertex3 b) =>
            new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);

        public Vertex3 Normalized()
        {
            float len = Length;
            return len <= 1e-6f ? default : new(X / len, Y / len, Z / len);
        }
    }

    /// <summary>一个带颜色的三角形（顶点顺序不影响结果，内部做背面剔除）。</summary>
    public readonly record struct Triangle3D(Vertex3 A, Vertex3 B, Vertex3 C, Color Color);

    /// <summary>透视相机：位置、看向的目标、上方向、垂直视场角。</summary>
    public readonly record struct Camera3D(
        Vertex3 Eye,
        Vertex3 Target,
        Vertex3 Up,
        float FovDegrees,
        float Near = 0.1f,
        float Far = 1000f);

    /// <summary>
    /// 画一批三角形：视空间变换 → 近平面剔除 → 透视投影 → 背面剔除 →
    /// 按平均深度从远到近排序（画家算法）→ 平面着色填充。
    /// </summary>
    public static void Render(
        ICanvas canvas,
        IReadOnlyList<Triangle3D> triangles,
        Camera3D camera,
        float canvasWidth,
        float canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(triangles);
        if (triangles.Count == 0 || canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        var view = BuildView(camera);
        float aspect = canvasWidth / canvasHeight;
        float tanHalfFov = MathF.Tan(camera.FovDegrees * MathF.PI / 360f);

        var projected = new List<(CanvasPoint A, CanvasPoint B, CanvasPoint C, float Depth, Color Color)>(triangles.Count);
        foreach (var tri in triangles)
        {
            var a = Transform(view, tri.A);
            var b = Transform(view, tri.B);
            var c = Transform(view, tri.C);

            // 任一点在近平面后：整片丢弃。真正的多边形裁剪超出入门版范围。
            if (a.Z <= camera.Near || b.Z <= camera.Near || c.Z <= camera.Near
                || a.Z >= camera.Far || b.Z >= camera.Far || c.Z >= camera.Far)
            {
                continue;
            }

            var pa = Project(a, tanHalfFov, aspect, canvasWidth, canvasHeight);
            var pb = Project(b, tanHalfFov, aspect, canvasWidth, canvasHeight);
            var pc = Project(c, tanHalfFov, aspect, canvasWidth, canvasHeight);

            // 屏幕空间绕序：统一按一个方向剔除，避免依赖模型顶点顺序。
            float area = (pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X);
            if (area <= 0f)
            {
                continue;
            }

            float depth = (a.Z + b.Z + c.Z) / 3f;
            projected.Add((pa, pb, pc, depth, Shade(tri)));
        }

        // 画家算法：远 → 近。没有深度缓冲，互相穿插的三角形会出错，这是入门实现的已知限制。
        projected.Sort((l, r) => r.Depth.CompareTo(l.Depth));
        foreach (var tri in projected)
        {
            canvas.FillPolygon(new[] { tri.A, tri.B, tri.C }, tri.Color);
        }
    }

    /// <summary>单点投影（供测试 / 命中测试）；点在近平面后返回 false。</summary>
    public static bool TryProject(
        Camera3D camera,
        Vertex3 world,
        float canvasWidth,
        float canvasHeight,
        out CanvasPoint point,
        out float depth)
    {
        point = default;
        depth = 0f;
        var v = Transform(BuildView(camera), world);
        if (v.Z <= camera.Near || v.Z >= camera.Far)
        {
            return false;
        }

        float aspect = canvasWidth / canvasHeight;
        float tanHalfFov = MathF.Tan(camera.FovDegrees * MathF.PI / 360f);
        point = Project(v, tanHalfFov, aspect, canvasWidth, canvasHeight);
        depth = v.Z;
        return true;
    }

    private static Matrix4x4 BuildView(Camera3D camera)
    {
        var eye = new Vector3(camera.Eye.X, camera.Eye.Y, camera.Eye.Z);
        var target = new Vector3(camera.Target.X, camera.Target.Y, camera.Target.Z);
        var up = new Vector3(camera.Up.X, camera.Up.Y, camera.Up.Z);
        return Matrix4x4.CreateLookAt(eye, target, up);
    }

    private static Vertex3 Transform(Matrix4x4 m, Vertex3 v)
    {
        var p = Vector3.Transform(new Vector3(v.X, v.Y, v.Z), m);
        // CreateLookAt 是右手系：相机前方为 -Z；翻成「正深度」方便近平面判断与投影。
        return new Vertex3(p.X, p.Y, -p.Z);
    }

    private static CanvasPoint Project(Vertex3 v, float tanHalfFov, float aspect, float width, float height)
    {
        float ndcX = v.X / (v.Z * tanHalfFov * aspect);
        float ndcY = v.Y / (v.Z * tanHalfFov);
        return new CanvasPoint(
            (ndcX * 0.5f + 0.5f) * width,
            (0.5f - ndcY * 0.5f) * height);
    }

    private static Color Shade(Triangle3D tri)
    {
        var normal = Vertex3.Cross(tri.B - tri.A, tri.C - tri.A).Normalized();
        var light = new Vertex3(0.35f, 0.8f, 0.45f).Normalized();
        float lit = 0.45f + 0.55f * MathF.Abs(Vertex3.Dot(normal, light));
        return new Color(
            (byte)Math.Clamp(tri.Color.R * lit, 0, 255),
            (byte)Math.Clamp(tri.Color.G * lit, 0, 255),
            (byte)Math.Clamp(tri.Color.B * lit, 0, 255),
            tri.Color.A);
    }
}
