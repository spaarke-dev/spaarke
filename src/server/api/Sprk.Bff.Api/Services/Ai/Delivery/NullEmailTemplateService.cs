using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Delivery;

/// <summary>
/// §F.1 Endpoint↔DI Registration Conditionality Symmetry — the compound-OFF Null peer for
/// <see cref="IEmailTemplateService"/>. The <c>POST /api/communications/template/render</c> endpoint
/// (<see cref="Sprk.Bff.Api.Api.CommunicationTemplateEndpoints"/>) maps UNCONDITIONALLY, but the real
/// <see cref="EmailTemplateService"/> registers only under the AI compound gate (AddDeliveryServices,
/// <c>Analysis:Enabled &amp;&amp; DocumentIntelligence:Enabled</c>). Without this peer, a compound-OFF host
/// crashes at STARTUP: <c>AuthorizationPolicyCache</c> eagerly enumerates every endpoint, and
/// <c>RequestDelegateFactory</c> throws "Failure to infer one or more parameters" ({@code emailTemplateService})
/// because the endpoint's injected parameter has no registration — the same LATENT-BUG shape
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.NullComposeTemplateSource"/> fixed for its sibling.
///
/// Behavior mirrors that sibling: resolve every render to a failed result → the endpoint's clean
/// 404/400 not-found path, with a loud warning. Graceful degradation (ADR-032 P2) is correct here —
/// template rendering is a best-effort composer convenience, not a load-bearing capability.
/// </summary>
public sealed class NullEmailTemplateService : IEmailTemplateService
{
    private const string DisabledError =
        "Email template rendering is unavailable: the AI compound gate (Analysis + DocumentIntelligence) is disabled on this host.";

    private readonly ILogger<NullEmailTemplateService> _logger;

    public NullEmailTemplateService(ILogger<NullEmailTemplateService> logger) => _logger = logger;

    public Task<EmailTemplateResult> FetchAndRenderAsync(
        Guid templateId,
        Dictionary<string, object?> variables,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Email template render invoked while the AI compound gate is OFF — template {TemplateId} resolves to unavailable (NullEmailTemplateService).",
            templateId);
        return Task.FromResult(Fail());
    }

    public Task<EmailTemplateResult> FetchAndRenderByNameAsync(
        string templateName,
        Dictionary<string, object?> variables,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Email template render-by-name invoked while the AI compound gate is OFF — template '{TemplateName}' resolves to unavailable (NullEmailTemplateService).",
            templateName);
        return Task.FromResult(Fail());
    }

    public EmailTemplateResult RenderFromContent(
        string subject,
        string body,
        Dictionary<string, object?> variables,
        bool isHtml = true)
    {
        _logger.LogWarning(
            "Email template render-from-content invoked while the AI compound gate is OFF (NullEmailTemplateService).");
        return Fail();
    }

    private static EmailTemplateResult Fail() => new() { Success = false, Error = DisabledError };
}
