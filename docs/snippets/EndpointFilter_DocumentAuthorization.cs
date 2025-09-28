public sealed class DocumentAuthorizationFilter : IEndpointFilter
{
    private readonly IAuthorizationService _authz;
    private readonly Operation _op;

    public DocumentAuthorizationFilter(IAuthorizationService authz, Operation op)
    { _authz = authz; _op = op; }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (!ctx.HttpContext.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();

        if (!Guid.TryParse(ctx.HttpContext.Request.RouteValues["id"]?.ToString(), out var docId))
            return Results.Problem(title: "Invalid route", detail: "Missing or invalid document id", statusCode: 400);

        var res = await _authz.AuthorizeAsync(ctx.HttpContext.User,
                    new ResourceRef(ResourceType.Document, docId), _op, ctx.HttpContext.RequestAborted);

        if (!res.Allowed)
            return Results.Problem(title: "Forbidden", detail: res.Reason, statusCode: 403);

        return await next(ctx);
    }
}