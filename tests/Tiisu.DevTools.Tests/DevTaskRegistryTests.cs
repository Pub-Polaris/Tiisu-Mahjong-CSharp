using System.Linq;
using System.Threading.Tasks;
using Tiisu.DevTools.TaskCenter;
using Xunit;

namespace Tiisu.DevTools.Tests;

public sealed class DevTaskRegistryTests
{
    private static PublishTaskActions DummyActions() => new(
        () => Task.CompletedTask,
        () => Task.CompletedTask,
        () => Task.CompletedTask,
        () => Task.CompletedTask,
        () => Task.CompletedTask,
        () => Task.CompletedTask);

    [Fact]
    public void Publish_RegistersSixUniqueTasks()
    {
        var tasks = DevTaskRegistry.Publish(DummyActions());

        Assert.Equal(6, tasks.Count);
        Assert.Equal(6, tasks.Select(t => t.Id).Distinct().Count());
        Assert.All(tasks, task =>
        {
            Assert.Equal(DevTaskRegistry.PublishCategory, task.Category);
            Assert.False(string.IsNullOrWhiteSpace(task.Title));
            Assert.False(string.IsNullOrWhiteSpace(task.Summary));
            Assert.NotNull(task.Run);
        });
    }

    [Fact]
    public void Render_RegistersOutputsAndOpenTask()
    {
        var actions = new RenderTaskActions(
            () => "artifacts/uitools",
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            () => Task.CompletedTask);

        var tasks = DevTaskRegistry.Render(actions);

        Assert.Contains(tasks, t => t.Id == "render.table" && t.OutputPath is not null);
        Assert.Contains(tasks, t => t.Id == "render.actions");
        Assert.Contains(tasks, t => t.Id == "render.open-output");
        Assert.Equal(tasks.Count, tasks.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void Categories_IncludePublishAndFuture()
    {
        Assert.Contains(DevTaskRegistry.PublishCategory, DevTaskRegistry.Categories);
        Assert.Contains("出图", DevTaskRegistry.Categories);
        Assert.Contains("AI 数据", DevTaskRegistry.Categories);
    }
}
