namespace Altrobe.Core.Tests;

public class MemoTests
{
    [Fact]
    public void AFailedLoadIsRetriedOnTheNextCall()
    {
        var calls = 0;
        var memo = new Memo<int>(() => ++calls == 1 ? throw new IOException("offline") : 42);
        Assert.Throws<IOException>(() => { _ = memo.Value; });
        Assert.Equal(42, memo.Value);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ASuccessfulLoadRunsOnce()
    {
        var calls = 0;
        var memo = new Memo<int>(() => ++calls);
        Assert.Equal(1, memo.Value);
        Assert.Equal(1, memo.Value);
        Assert.Equal(1, calls);
    }
}
