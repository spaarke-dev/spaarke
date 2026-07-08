using System.Runtime.CompilerServices;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.EventRules;

/// <summary>
/// Null-Object <see cref="IEventRulesService"/> registered when the compound AI
/// kill switch is OFF (<c>Analysis:Enabled=false</c> or
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// P3 Fail-Fast pattern per ADR-032 / CLAUDE.md §10 F.1 — canonical siblings:
/// <see cref="Chat.NullSessionDispatchOrchestrator"/>.
/// The document_uploaded event endpoint (mapped UNCONDITIONALLY in
/// <c>ChatDocumentEndpoints</c>) injects <see cref="IEventRulesService"/>; without
/// this peer, minimal-API parameter inference fails at host startup when the
/// compound AI gate is OFF. <see cref="FireAsync"/> throws
/// <see cref="FeatureDisabledException"/> on the first <c>MoveNextAsync()</c>;
/// the endpoint's pre-stream probe maps it to the canonical 503 ProblemDetails
/// (ADR-018 + ADR-019).
/// </remarks>
public sealed class NullEventRulesService : IEventRulesService
{
    private const string ErrorCode = "ai.event-rules.disabled";
    private const string DetailMessage =
        "The AI Event path requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullEventRulesService> _logger;

    public NullEventRulesService(ILogger<NullEventRulesService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatSseEvent> FireAsync(
        SurfaceEventRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "NullEventRulesService.FireAsync invoked while the AI Event path is disabled (errorCode={ErrorCode}).",
            ErrorCode);

        // Surfaces synchronously on the first MoveNextAsync() — exactly what the
        // endpoint's pre-stream probe catches BEFORE setting SSE headers.
        throw new FeatureDisabledException(ErrorCode, DetailMessage);

#pragma warning disable CS0162 // unreachable — required to make this a valid iterator method
        yield break;
#pragma warning restore CS0162
    }
}
