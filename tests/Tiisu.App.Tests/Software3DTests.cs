using Tiisu.App.Rendering;
using Xunit;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Tests;

public sealed class Software3DTests
{
    private static readonly Software3D.Camera3D Camera = new(
        new Software3D.Vertex3(0f, 0f, -10f),
        new Software3D.Vertex3(0f, 0f, 0f),
        new Software3D.Vertex3(0f, 1f, 0f),
        60f,
        Near: 0.1f,
        Far: 1000f);

    [Fact]
    public void Origin_ProjectsToCanvasCenter()
    {
        bool ok = Software3D.TryProject(Camera, new Software3D.Vertex3(0f, 0f, 0f),
            800f, 600f, out var point, out float depth);

        Assert.True(ok);
        Assert.Equal(400f, point.X, 1);
        Assert.Equal(300f, point.Y, 1);
        Assert.Equal(10f, depth, 1);
    }

    [Fact]
    public void PointBehindCamera_IsRejected()
    {
        bool ok = Software3D.TryProject(Camera, new Software3D.Vertex3(0f, 0f, -20f),
            800f, 600f, out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void EmptyTriangleList_RenderDoesNotThrow()
    {
        Software3D.Render(new NullCanvas(), Array.Empty<Software3D.Triangle3D>(), Camera, 800f, 600f);
    }

    private sealed class NullCanvas : ICanvas
    {
        public void Save() { }
        public void Restore() { }
        public void Translate(float dx, float dy) { }
        public void Scale(float factor) { }
        public void RotateDegrees(float degrees) { }
        public void ClipRect(RectF r) { }
        public void ConcatPerspective(in PerspectiveCamera camera) { }
        public void Clear(Color c) { }
        public void FillRect(RectF r, Color c) { }
        public void FillRoundRect(RectF r, float radius, Color c) { }
        public void StrokeRoundRect(RectF r, float radius, Color c, float width) { }
        public void FillLinearGradient(RectF r, float radius, Color top, Color bottom, bool horizontal = false) { }
        public void FillPolygon(IReadOnlyList<CanvasPoint> points, Color c) { }
        public void FillPolygonGradient(IReadOnlyList<CanvasPoint> points, Color top, Color bottom) { }
        public void StrokePolygon(IReadOnlyList<CanvasPoint> points, Color c, float width) { }
        public void FillShadow(RectF r, float radius, float blur, Color c) { }
        public void FillCircle(float cx, float cy, float radius, Color c) { }
        public void DrawText(string text, RectF box, Color c, TextStyle style) { }
        public float MeasureText(string text, TextStyle style) => 0f;
        public void DrawTile(string tileId, RectF dst, float alpha = 1f) { }
    }
}
