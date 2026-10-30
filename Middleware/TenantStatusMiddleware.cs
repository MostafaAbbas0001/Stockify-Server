namespace Stockify.Middleware;

/// <summary>
/// Rejects requests from tenants that are not active (e.g. suspended), even while their
/// access tokens are still valid.
/// </summary>
public sealed class TenantStatusMiddleware
{
    private readonly RequestDelegate _next;

    public TenantStatusMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, TenantStatusService tenantStatus)
    {
        var tenantId = context.User.FindTenantId();
        if (tenantId is not null && !await tenantStatus.IsActiveAsync(tenantId.Value))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = TenantSuspendedException.Code,
                message = "This company account is not active. Contact support."
            });
            return;
        }

        await _next(context);
    }
}
