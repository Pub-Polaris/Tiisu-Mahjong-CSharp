using Tiisu.App.Rendering;
using Tiisu.App.Ui;
using Xunit;

namespace Tiisu.App.Tests;

public sealed class SliderTests
{
    [Fact]
    public void Drag_MovesValueAndCommits()
    {
        var slider = new Slider(0, 100, 50);
        slider.Layout(new RectF(100, 100, 200, 30));
        int commits = 0;
        slider.ValueCommitted = () => commits++;

        Assert.True(slider.OnPointerDown(300, 115));
        Assert.True(slider.Dragging);
        Assert.True(slider.Value > 90);

        Assert.True(slider.OnPointerMove(110, 115));
        Assert.True(slider.Value < 10);

        slider.OnPointerUp();
        Assert.False(slider.Dragging);
        Assert.Equal(1, commits);
    }

    [Fact]
    public void Step_ClampsAndCommits()
    {
        var slider = new Slider(0, 100, 95);
        int commits = 0;
        slider.ValueCommitted = () => commits++;

        slider.Step(10);

        Assert.Equal(100, slider.Value);
        Assert.Equal(1, commits);
    }

    [Fact]
    public void ClickOutside_DoesNothing()
    {
        var slider = new Slider(0, 100, 50);
        slider.Layout(new RectF(100, 100, 200, 30));

        Assert.False(slider.OnPointerDown(10, 10));
        Assert.False(slider.Dragging);
        Assert.Equal(50, slider.Value);
    }
}
