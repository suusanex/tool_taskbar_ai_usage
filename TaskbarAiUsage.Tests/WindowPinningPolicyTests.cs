using TaskbarAiUsage;

public sealed class WindowPinningPolicyTests
{
    [Fact]
    public void DoesNotCallPropertyStoreBeforeAnHwndExists()
    {
        var called = false;

        var applied = WindowPinningPolicy.TryPreventPinning(IntPtr.Zero, _ => called = true);

        Assert.False(applied);
        Assert.False(called);
    }

    [Fact]
    public void AppliesPolicyToAnInitializedWindowHandle()
    {
        var expectedHandle = new IntPtr(42);
        var receivedHandle = IntPtr.Zero;

        var applied = WindowPinningPolicy.TryPreventPinning(expectedHandle, hwnd => receivedHandle = hwnd);

        Assert.True(applied);
        Assert.Equal(expectedHandle, receivedHandle);
    }

    [Fact]
    public void PropertyStoreFailureDoesNotEscapeToTheApplication()
    {
        var applied = WindowPinningPolicy.TryPreventPinning(new IntPtr(42), _ => throw new InvalidOperationException("Shell unavailable"));

        Assert.False(applied);
    }
}
