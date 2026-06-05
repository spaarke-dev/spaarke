using OpenAI.Chat;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="IInvoiceAi"/> registered when the compound
/// AI kill-switch is OFF (<c>Analysis:Enabled=false</c> OR <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-032 + D-09 §2 L1. Throws <see cref="FeatureDisabledException"/>
/// on every public method so that Finance consumers
/// (<c>Services/Finance/InvoiceAnalysisService.cs</c>,
/// <c>Services/Finance/InvoiceSearchService.cs</c>,
/// <c>Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs</c>) convert to 503 ProblemDetails
/// per ADR-018 + ADR-019. Returning empty playbook responses / zero-length embeddings would
/// silently corrupt the invoice index and mislead operators.
/// </para>
/// <para>
/// Introduced 2026-06-04 by <c>bff-ai-architecture-audit-r1</c> Phase 4 Migration PR #1
/// (LATENT BUG #1 remediation per W4 §4.5 + DR-003). Closes the asymmetric-registration
/// gap where <see cref="IInvoiceAi"/> was registered only in the compound-AI-ON path
/// (<c>AddPublicContractsFacade</c>) with no Null peer for compound-AI-OFF — producing a
/// runtime DI resolution failure (500) when Finance endpoints + the invoice indexing job
/// handler ran against a kill-switched env, instead of the contract-specified 503
/// <see cref="FeatureDisabledException"/>.
/// </para>
/// </remarks>
public sealed class NullInvoiceAi : IInvoiceAi
{
    private const string ErrorCode = "ai.invoice.disabled";
    private const string DetailMessage =
        "Finance Intelligence (invoice classification/extraction/search) requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullInvoiceAi> _logger;

    public NullInvoiceAi(ILogger<NullInvoiceAi> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<PlaybookResponse> GetPlaybookByNameAsync(
        string playbookName,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetPlaybookByNameAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<T> GetStructuredCompletionAsync<T>(
        IEnumerable<ChatMessage> messages,
        BinaryData jsonSchema,
        string schemaName,
        string deploymentName,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetStructuredCompletionAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        string? model = null,
        int? dimensions = null,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GenerateEmbeddingAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullInvoiceAi.{Method} invoked while AI feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
