public sealed class SpaarkeContextMiddleware : IMiddleware
{
    private readonly IPrincipalResolver _resolver;
    public SpaarkeContextMiddleware(IPrincipalResolver resolver) => _resolver = resolver;

    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        var (userId, dvPrincipalId, tenantId) = await _resolver.ResolveAsync(ctx.User, ctx.RequestAborted);
        ctx.Items[nameof(SpaarkeContext)] = new SpaarkeContext { UserId = userId, DataversePrincipalId = dvPrincipalId, TenantId = tenantId, CorrelationId = ctx.TraceIdentifier };
        await next(ctx);
    }
}