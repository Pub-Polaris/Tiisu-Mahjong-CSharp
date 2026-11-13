using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Tiisu.App.Ui;
using Xunit;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Tests;

public sealed class SettlementClickTests
{
    [Fact]
    public void NextRoundButton_IsHitTestable()
    {
        var overlay = new SettlementOverlay();
        var result = new RoundResult(
            1, "flow", Array.Empty<int>(), "exhaustive",
            new[] { 50000, 50000, 50000, 50000 }, 0, 0, false);
        var vm = TableDemoModel.Create(1);

        overlay.Draw(new NullCanvas(), result, vm);

        var bounds = overlay.NextRoundBounds;
        Assert.True(bounds.W > 0f);
        Assert.True(overlay.HitNextRound(bounds.CenterX, bounds.CenterY));
        Assert.False(overlay.HitNextRound(0f, 0f));
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
