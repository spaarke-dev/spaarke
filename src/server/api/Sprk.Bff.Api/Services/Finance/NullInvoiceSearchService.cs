using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Null-Object implementation of <see cref="IInvoiceSearchService"/> registered when
/// <c>DocumentIntelligence:Enabled=false</c>.
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 L2. Returning an empty <c>InvoiceSearchResponse</c>
/// would silently render "no invoices match" in the Finance UI, masking the kill-switch
/// state — fail-fast surfaces the actual config state to operators.
/// </para>
/// <para>Introduced 2026-06-01 by task 011 Phase 1b Tier 2.</para>
/// </remarks>
public sealed class NullInvoiceSearchService : IInvoiceSearchService
{
    private const string ErrorCode = "ai.finance.search.disabled";
    private const string DetailMessage =
        "Invoice semantic search requires DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullInvoiceSearchService> _logger;

    public NullInvoiceSearchService(ILogger<NullInvoiceSearchService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<InvoiceSearchResponse> SearchAsync(
        string query,
        Guid? matterId = null,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "NullInvoiceSearchService.SearchAsync invoked while AI feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
