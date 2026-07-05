using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Null-Object implementation of <see cref="IOutputOrchestratorService"/> registered when the
/// compound AI kill-switch is OFF (<c>Analysis:Enabled=false</c> or
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-032: a P2 quiet no-op returning
/// <c>OutputMappingResult.SuccessResult([])</c> would falsely report a successful playbook
/// output application while writing nothing to Dataverse — forbidden for computation
/// services. Throwing <see cref="FeatureDisabledException"/> fails the consuming job
/// (InvoiceExtractionJobHandler) fast on dequeue; Service Bus retry/DLQ semantics apply per
/// ADR-018.
/// </para>
/// <para>
/// Introduced 2026-07-05 by ai-architecture-redesign-r1 task 006 (FR-P0-05) — the real
/// registration moved from FinanceModule to AnalysisServicesModule.AddPlaybookServices.
/// </para>
/// </remarks>
public sealed class NullOutputOrchestratorService : IOutputOrchestratorService
{
    /// <summary>Stable errorCode carried in the 503 ProblemDetails — clients switch on this string.</summary>
    public const string ErrorCode = "ai.output-orchestrator.disabled";

    private const string DetailMessage =
        "Playbook output orchestration requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullOutputOrchestratorService> _logger;

    public NullOutputOrchestratorService(ILogger<NullOutputOrchestratorService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<OutputMappingResult> ApplyOutputMappingAsync(
        Guid playbookId,
        PlaybookExecutionContext context,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "NullOutputOrchestratorService.ApplyOutputMappingAsync invoked while AI feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
