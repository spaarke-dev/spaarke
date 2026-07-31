namespace Sprk.Bff.Api.Services.Ai.Classification;

/// <summary>
/// Reads the live <c>sprk_agreementtype</c> registry rows that drive the <c>agreement-classify</c>
/// classifier's candidate key set (design Lens 3d, FR-07). Kept as a narrow, single-purpose read
/// abstraction so the classifier dispatch (tasks 021/023) can source the registry-driven type list and
/// the confirmation gate can source per-type thresholds from the SAME rows.
/// </summary>
/// <remarks>
/// <para>
/// ai-advanced-capabilities-agreements-r1 task 020. §11 Component Justification: the existing
/// <c>IScopeResolverService.QueryLookupValuesAsync</c> returns a single field per call and cannot
/// correlate <c>sprk_key</c> with <c>sprk_classificationcue</c> / <c>sprk_isfallback</c> across rows,
/// so a purpose-built multi-field read is required. Cost-of-doing-nothing: without it the classifier
/// prompt cannot be assembled from the registry at runtime and the type-agnostic promise fails.
/// </para>
/// </remarks>
public interface IAgreementTypeRegistryReader
{
    /// <summary>
    /// Returns the active <c>sprk_agreementtype</c> rows (the classifiable sub-domain set + the
    /// fallback row). Ordering is not guaranteed; <see cref="AgreementTypeRegistryPromptAssembler"/>
    /// imposes a deterministic order when it formats the prompt block.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registry rows; an empty list if none are registered.</returns>
    Task<IReadOnlyList<AgreementTypeRow>> GetRowsAsync(CancellationToken cancellationToken = default);
}
