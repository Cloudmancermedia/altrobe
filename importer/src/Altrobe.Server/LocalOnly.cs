namespace Altrobe.Server;

// DNS-rebinding defense: a hostile page can point its own domain at 127.0.0.1, but its requests
// still carry that domain in Host, so only literal local names are served. Cross-site Origins are
// refused too; with no CORS headers the browser already blocks reading responses, and this also
// stops a foreign page from triggering work.
public sealed class LocalOnlyMiddleware(RequestDelegate next)
{
    public static bool IsLocalHost(string? host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Host.HasValue || !IsLocalHost(ctx.Request.Host.Host))
        {
            await ApiErrors.Write(ctx, StatusCodes.Status403Forbidden, "forbidden_host", "Requests must use localhost or 127.0.0.1 as the host.");
            return;
        }
        if (ctx.Request.Headers.Origin is { Count: > 0 } origin
            && !(Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var o) && o.Scheme == Uri.UriSchemeHttp && IsLocalHost(o.Host)))
        {
            await ApiErrors.Write(ctx, StatusCodes.Status403Forbidden, "forbidden_origin", "Cross-site requests are not allowed.");
            return;
        }
        await next(ctx);
    }
}

public static class ApiErrors
{
    public sealed record ErrorInfo(string Code, string Message);

    public sealed record ErrorBody(ErrorInfo Error);

    public static IResult Result(int status, string code, string message) => Results.Json(new ErrorBody(new ErrorInfo(code, message)), statusCode: status);

    public static Task Write(HttpContext ctx, int status, string code, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new ErrorBody(new ErrorInfo(code, message)));
    }

    public static IResult BadRequest(string message) => Result(StatusCodes.Status400BadRequest, "bad_request", message);

    public static IResult NotFound(string code, string message) => Result(StatusCodes.Status404NotFound, code, message);

    public static IResult NoInstall() => Result(StatusCodes.Status409Conflict, "no_install", "Select a WoW install and product first (POST /api/v1/install).");
}

// Maps exceptions to the error shape: files missing from the install are 404, anything else 500.
public sealed class ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (FileNotFoundException e) when (!ctx.Response.HasStarted)
        {
            await ApiErrors.Write(ctx, StatusCodes.Status404NotFound, "file_not_found", e.Message);
        }
        catch (InvalidDataException e) when (!ctx.Response.HasStarted)
        {
            log.LogWarning(e, "Conversion failed for {Path}", ctx.Request.Path);
            await ApiErrors.Write(ctx, StatusCodes.Status500InternalServerError, "conversion_failed", e.Message);
        }
        catch (Exception e) when (!ctx.Response.HasStarted && !ctx.RequestAborted.IsCancellationRequested)
        {
            log.LogError(e, "Request {Path} failed", ctx.Request.Path);
            await ApiErrors.Write(ctx, StatusCodes.Status500InternalServerError, "internal_error", e.Message);
        }
    }
}
