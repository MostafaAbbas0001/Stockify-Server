using System.Diagnostics;

namespace Stockify.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            var request = context.Request;
            var userId = context.User?.Identity?.IsAuthenticated == true
                ? context.User.FindFirst("sub")?.Value
                    ?? context.User.FindFirst("id")?.Value
                    ?? context.User.Identity?.Name
                : "anonymous";

            using (_logger.BeginScope(new Dictionary<string, object>
                   {
                       ["TraceId"] = traceId,
                       ["RequestPath"] = request.Path.Value ?? string.Empty,
                       ["RequestMethod"] = request.Method,
                       ["UserId"] = userId ?? "anonymous",
                       ["RemoteIp"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                   }))
            {
                _logger.LogError(ex, "Unhandled exception caught by global middleware");
            }

            context.Response.StatusCode = ex switch
            {
                TenantSuspendedException => StatusCodes.Status403Forbidden,
                TenantAccessException => StatusCodes.Status403Forbidden,
                ArgumentException => StatusCodes.Status400BadRequest,
                KeyNotFoundException => StatusCodes.Status404NotFound,
                UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
                InvalidOperationException => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            };
            context.Response.ContentType = "application/json";

            var response = new
            {
                code = ex is TenantSuspendedException ? TenantSuspendedException.Code : context.Response.StatusCode switch
                {
                    StatusCodes.Status400BadRequest => "BAD_REQUEST",
                    StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
                    StatusCodes.Status403Forbidden => "FORBIDDEN",
                    StatusCodes.Status404NotFound => "NOT_FOUND",
                    StatusCodes.Status409Conflict => "CONFLICT",
                    _ => "INTERNAL_SERVER_ERROR"
                },
                message = context.Response.StatusCode == StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred."
                    : ex.Message,
                traceId
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
