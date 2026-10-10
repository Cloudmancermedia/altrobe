namespace Altrobe.Server.Tests;

public class ErrorMiddlewareTests
{
    [Fact]
    public void LogsARequestPathOnOneLine()
    {
        // A decoded %0A in a path would otherwise start a fake line in the log.
        Assert.Equal("/api/v1/x fake entry", ErrorMiddleware.OneLine("/api/v1/x\r\nfake entry"));
        Assert.Equal("/api/v1/models/12", ErrorMiddleware.OneLine("/api/v1/models/12"));
    }
}
