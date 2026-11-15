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
                BranchAccessException => StatusCodes.Status403Forbidden,
                ArgumentException => StatusCodes.Status400BadRequest,
                KeyNotFoundException => StatusCodes.Status404NotFound,
                UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
                InvalidOperationException => StatusCodes.Status409Conflict,
                _ when SerializableTransactions.IsSerializationFailure(ex) => StatusCodes.Status409Conflict,
                _ when ConstraintViolation(ex) != null => StatusCodes.Status409Conflict,
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
                    : SerializableTransactions.IsSerializationFailure(ex)
                        ? "The system is busy with the same records; please try again."
                        : ConstraintViolation(ex) ?? ex.Message,
                traceId
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }

    /// <summary>A plain message for a unique (23505) or foreign-key (23503, RESTRICT 23001) violation; null otherwise.</summary>
    private static string? ConstraintViolation(Exception ex) =>
        (ex as Npgsql.PostgresException ?? ex.InnerException as Npgsql.PostgresException)?.SqlState switch
        {
            "23505" => "A record with the same value already exists.",
            "23503" or "23001" => "This record is still used by other records and cannot be changed this way.",
            _ => null
        };
}
