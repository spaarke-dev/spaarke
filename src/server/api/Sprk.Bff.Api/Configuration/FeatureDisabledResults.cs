namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Extension helpers that convert a caught <see cref="FeatureDisabledException"/> into the
/// canonical 503 ProblemDetails response defined by ADR-018 (kill switches) + ADR-019
/// (ProblemDetails).
/// </summary>
/// <remarks>
/// Endpoint <c>try/catch</c> blocks should call <see cref="AsFeatureDisabled503"/> to keep
/// the conversion DRY across the ~7 Null-Object consumer surfaces introduced by task 011
/// Phase 1b Tier 2 (per D-09 §3 + §8 Risks).
/// </remarks>
public static class FeatureDisabledResults
{
    /// <summary>
    /// Canonical type URI for feature-disabled errors. Stable; clients may match on this URI
    /// to render a kill-switch-specific UX without parsing the detail string.
    /// </summary>
    public const string TypeUri = "https://errors.spaarke.com/feature-disabled";

    /// <summary>
    /// Converts a <see cref="FeatureDisabledException"/> into a 503 ProblemDetails
    /// <see cref="IResult"/> per ADR-018 / ADR-019.
    /// </summary>
    /// <param name="ex">The caught exception. Must not be null.</param>
    /// <returns>A 503 <see cref="IResult"/> with the canonical ProblemDetails shape:
    /// <c>title="Feature Disabled"</c>, <c>type=TypeUri</c>, <c>detail=ex.Message</c>,
    /// and <c>extensions.errorCode=ex.ErrorCode</c>.</returns>
    public static IResult AsFeatureDisabled503(this FeatureDisabledException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return Results.Problem(
            title: "Feature Disabled",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            type: TypeUri,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = ex.ErrorCode
            });
    }
}
