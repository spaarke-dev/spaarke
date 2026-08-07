using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// 032 Step-9.5 F1 (§F.1 Endpoint↔DI Registration Conditionality Symmetry): the compound-OFF Null peer
/// for <see cref="IComposeTemplateSource"/>. The apply-template endpoint maps UNCONDITIONALLY, but the
/// real <c>ComposeTemplateSource</c> registers only under the AI compound gate (AddDeliveryServices) —
/// without this peer a compound-OFF host 500s at parameter injection (the LATENT BUG #1 shape). Resolves
/// every template to null → the endpoint's clean 404 template-not-found path, with a loud log.
/// </summary>
public sealed class NullComposeTemplateSource : IComposeTemplateSource
{
    private readonly ILogger<NullComposeTemplateSource> _logger;

    public NullComposeTemplateSource(ILogger<NullComposeTemplateSource> logger) => _logger = logger;

    public Task<ComposeResolvedTemplate?> ResolveAsync(
        string templateIdOrName,
        Dictionary<string, object?>? variables,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Compose apply-template invoked while the AI compound gate is OFF — template '{Template}' resolves to not-found (NullComposeTemplateSource).",
            templateIdOrName);
        return Task.FromResult<ComposeResolvedTemplate?>(null);
    }
}
