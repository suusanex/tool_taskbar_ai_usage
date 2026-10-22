using System.Windows.Media.Imaging;
using TaskbarAiUsage;

public sealed class NativeWindowIconTests
{
    [Fact]
    public void CreatesAndReleasesNativeIconFrom256PixelRender()
    {
        RunSta(() =>
        {
            foreach (var value in new int?[] { 0, 7, 73, 100, null })
            {
                var bitmap = TaskbarIconRenderer.Render(value, value, logicalSize: 256);
                Assert.Equal(256, bitmap.PixelWidth);
                var icon = Win32WindowIconPlatform.Instance.CreateIcon(bitmap);
                Assert.NotEqual(IntPtr.Zero, icon);
                Win32WindowIconPlatform.Instance.DestroyIcon(icon);
            }
        });
    }

    [Fact]
    public void ReplacesAndDisposesOnlyOwnedIconHandles()
    {
        RunSta(() =>
        {
            var platform = new FakePlatform();
            var windowIcon = new NativeWindowIcon(new IntPtr(10), platform);
            var bitmap = TaskbarIconRenderer.Render(0, null);

            windowIcon.Apply(bitmap);
            Assert.Equal(new IntPtr(1), windowIcon.CurrentIcon);
            Assert.Equal(2, platform.Assignments.Count);
            Assert.DoesNotContain(IntPtr.Zero, platform.Destroyed);

            windowIcon.Apply(bitmap);
            Assert.Equal(new IntPtr(2), windowIcon.CurrentIcon);
            Assert.Equal(new[] { new IntPtr(1) }, platform.Destroyed);

            windowIcon.Dispose();
            windowIcon.Dispose();
            Assert.Equal(new[] { new IntPtr(1), new IntPtr(2) }, platform.Destroyed);
            Assert.Equal(6, platform.Assignments.Count);
            Assert.Throws<ObjectDisposedException>(() => windowIcon.Apply(bitmap));
        });
    }

    [Fact]
    public void ShellAndDpiMessagesRequestReapplication()
    {
        var reapplies = 0;
        Assert.True(TaskbarIconMessageRouter.Handle(301, 301, 302, () => reapplies++));
        Assert.True(TaskbarIconMessageRouter.Handle(302, 301, 302, () => reapplies++));
        Assert.True(TaskbarIconMessageRouter.Handle(TaskbarIconMessageRouter.WmDpiChanged, 301, 302, () => reapplies++));
        Assert.False(TaskbarIconMessageRouter.Handle(99, 301, 302, () => reapplies++));
        Assert.Equal(3, reapplies);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private sealed class FakePlatform : IWindowIconPlatform
    {
        private int next = 1;
        public List<(IntPtr Hwnd, IntPtr Type, IntPtr Icon)> Assignments { get; } = [];
        public List<IntPtr> Destroyed { get; } = [];
        public IntPtr CreateIcon(BitmapSource bitmap) => new(next++);
        public void SetWindowIcon(IntPtr hwnd, IntPtr iconType, IntPtr icon) => Assignments.Add((hwnd, iconType, icon));
        public void DestroyIcon(IntPtr icon) => Destroyed.Add(icon);
    }
}
