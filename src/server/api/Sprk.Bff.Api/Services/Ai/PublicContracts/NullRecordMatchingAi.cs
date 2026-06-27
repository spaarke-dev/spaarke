using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.RecordSearch;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="IRecordMatchingAi"/> registered when the
/// compound AI kill-switch is OFF (<c>Analysis:Enabled=false</c> OR
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-032 + D-09 §2 L1. Throws <see cref="FeatureDisabledException"/>
/// so consumers (none in the CRUD-external set today, but pre-registered ahead of the Phase 4
/// FR-C6 CI guard per <see cref="IRecordMatchingAi"/> remarks) convert to 503 ProblemDetails
/// per ADR-018 + ADR-019. Returning empty match results would silently mislead callers into
/// believing no matching records exist when the substrate is simply offline.
/// </para>
/// <para>
/// Introduced 2026-06-04 by <c>bff-ai-architecture-audit-r1</c> Phase 4 Migration PR #1
/// (LATENT BUG #1 remediation per W4 §4.5 + DR-003) — pre-registered together with the
/// three other PublicContracts facade Null peers (Insights, Invoice, WorkspacePrefill) so
/// the compound-AI-OFF DI graph resolves uniformly.
/// </para>
/// </remarks>
public sealed class NullRecordMatchingAi : IRecordMatchingAi
{
    private const string ErrorCode = "ai.record-matching.disabled";
    private const string DetailMessage =
        "AI-driven record matching requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullRecordMatchingAi> _logger;

    public NullRecordMatchingAi(ILogger<NullRecordMatchingAi> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<RecordSearchResponse> SearchAsync(
        RecordSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "NullRecordMatchingAi.SearchAsync invoked while AI feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
