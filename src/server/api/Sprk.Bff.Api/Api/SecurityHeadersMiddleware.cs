namespace Sprk.Bff.Api.Api;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        var path = ctx.Request.Path.Value ?? "";

        // Check if this is the playbook-builder app (designed to be embedded in Dataverse iframe)
        var isPlaybookBuilder = path.StartsWith("/playbook-builder", StringComparison.OrdinalIgnoreCase);

        if (!h.ContainsKey("X-Content-Type-Options")) h.Append("X-Content-Type-Options", "nosniff");
        if (!h.ContainsKey("Referrer-Policy")) h.Append("Referrer-Policy", "no-referrer");
        if (!h.ContainsKey("X-XSS-Protection")) h.Append("X-XSS-Protection", "0");
        if (!h.ContainsKey("Strict-Transport-Security"))
            h.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");

        if (isPlaybookBuilder)
        {
            // Playbook builder is embedded in Dataverse PCF control - allow framing from CRM domains
            if (!h.ContainsKey("X-Frame-Options")) h.Append("X-Frame-Options", "ALLOWALL");
            if (!h.ContainsKey("Content-Security-Policy"))
                h.Append("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; frame-ancestors https://*.crm.dynamics.com https://*.dynamics.com");
        }
        else
        {
            // Locked-down CSP for API responses (safe for JSON/file responses)
            if (!h.ContainsKey("X-Frame-Options")) h.Append("X-Frame-Options", "DENY");
            if (!h.ContainsKey("Content-Security-Policy"))
                h.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
        }

        await _next(ctx);
    }
}
