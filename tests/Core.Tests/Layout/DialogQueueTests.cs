using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

// Stage J8: the app shows one dialog at a time.
public sealed class DialogQueueTests
{
    private sealed class Dialog(string name)
    {
        public string Name { get; } = name;
        public TaskCompletionSource<string> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [Fact]
    public async Task OnlyOneDialogIsShownAtATime()
    {
        var queue = new DialogQueue<Dialog>();
        var shown = new List<string>();
        int open = 0, mostOpen = 0;
        Task<string> Show(Dialog dialog) => queue.ShowAsync(dialog, async d =>
        {
            shown.Add(d.Name);
            mostOpen = Math.Max(mostOpen, ++open);
            string result = await d.Closed.Task;
            open--;
            return result;
        }, "skipped");

        Dialog first = new("first"), second = new("second");
        Task<string> firstResult = Show(first);
        Task<string> secondResult = Show(second);

        Assert.True(queue.IsBusy);
        Assert.Same(first, queue.Current);
        Assert.Equal(["first"], shown);
        first.Closed.SetResult("cancel");
        Assert.Equal("cancel", await firstResult);
        await WaitUntil(() => ReferenceEquals(queue.Current, second));
        Assert.Equal(["first", "second"], shown);
        second.Closed.SetResult("ok");
        Assert.Equal("ok", await secondResult);
        Assert.Equal(1, mostOpen);
        Assert.False(queue.IsBusy);
        Assert.Null(queue.Current);
    }

    [Fact]
    public async Task ADialogClosedBeforeItsTurnIsNotShown()
    {
        var queue = new DialogQueue<Dialog>();
        var shown = new List<string>();
        Dialog open = new("open"), progress = new("progress");
        Task<string> Show(Dialog dialog) => queue.ShowAsync(dialog, async d => { shown.Add(d.Name); return await d.Closed.Task; }, "skipped");

        Task<string> openResult = Show(open);
        Task<string> progressResult = Show(progress);
        Assert.False(queue.Close(progress)); // still queued: the caller has nothing to hide
        Assert.True(queue.Close(open));      // on screen: the caller hides it
        open.Closed.SetResult("closed");

        Assert.Equal("closed", await openResult);
        Assert.Equal("skipped", await progressResult);
        Assert.Equal(["open"], shown);
    }

    [Fact]
    public async Task AFailedDialogDoesNotBlockTheNextOne()
    {
        var queue = new DialogQueue<Dialog>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queue.ShowAsync<string>(new Dialog("broken"), _ => throw new InvalidOperationException("two dialogs"), "skipped"));

        Assert.False(queue.IsBusy);
        Assert.Equal("ok", await queue.ShowAsync(new Dialog("next"), _ => Task.FromResult("ok"), "skipped"));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}
