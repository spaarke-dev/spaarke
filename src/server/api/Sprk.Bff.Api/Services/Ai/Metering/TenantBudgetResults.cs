namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// Extension helpers that convert a caught <see cref="TenantBudgetExceededException"/> into the
/// canonical 429 ProblemDetails response defined by spec.md SC #13 + ADR-019 (ProblemDetails).
/// </summary>
/// <remarks>
/// Endpoint <c>try/catch</c> blocks call <see cref="AsTenantBudgetExceeded429"/> to keep the
/// conversion DRY across every AI-consuming endpoint (chat, briefing, analysis, RAG, embeddings,
/// tool-completions — 30+ surfaces). Mirrors <see cref="Configuration.FeatureDisabledResults"/>
/// which converts <see cref="Configuration.FeatureDisabledException"/> to 503.
/// </remarks>
public static class TenantBudgetResults
{
    /// <summary>
    /// Canonical type URI for tenant-budget-exceeded errors. Stable; clients may match on this
    /// URI to render a budget-specific UX without parsing the detail string.
    /// </summary>
    public const string TypeUri = "https://errors.spaarke.com/tenant-budget-exceeded";

    /// <summary>
    /// Converts a <see cref="TenantBudgetExceededException"/> into a 429 ProblemDetails
    /// <see cref="IResult"/> per spec.md SC #13 + ADR-019.
    /// </summary>
    /// <param name="ex">The caught exception. Must not be null.</param>
    /// <returns>A 429 <see cref="IResult"/> with the canonical ProblemDetails shape:
    /// <c>title="Tenant Budget Exceeded"</c>, <c>type=TypeUri</c>, <c>detail=ex.Message</c>,
    /// and extensions <c>errorCode</c> + <c>tenant.id</c> + <c>observed_usd</c> + <c>cap_usd</c>.</returns>
    public static IResult AsTenantBudgetExceeded429(this TenantBudgetExceededException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return Results.Problem(
            title: "Tenant Budget Exceeded",
            detail: ex.Message,
            statusCode: StatusCodes.Status429TooManyRequests,
            type: TypeUri,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = TenantBudgetExceededException.StableErrorCode,
                ["tenant.id"] = ex.TenantId,
                ["observed_usd"] = ex.ObservedSpendUsd,
                ["cap_usd"] = ex.MonthlyBudgetUsd,
            });
    }
}
