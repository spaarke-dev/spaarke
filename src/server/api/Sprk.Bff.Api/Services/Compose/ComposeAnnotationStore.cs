using System.Text.RegularExpressions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 6 of the <c>ComposeService</c> decomposition (task 070): the session-annotations
/// contract.
///
/// <para><b>Its reason to change</b> is what an annotation IS and how it is validated — the ADR-040
/// <c>{bindingId}@t{n}</c> ledger-ref shape, and the partial-replace semantics of the two mutable
/// session collections. None of that moves when the save path changes, which is what makes it a
/// seam.</para>
///
/// <para><b>Mutable session collections, NOT ledger writes.</b> A null collection on the request
/// leaves the stored collection unchanged; a non-null (possibly empty) collection replaces it
/// wholesale. That is by design — annotations are accepted, rejected and edited — and is the
/// opposite of the append-only ledger, so the two must not be confused.</para>
///
/// <para>An <c>internal sealed</c> collaborator built from dependencies <c>ComposeService</c>
/// already holds — <b>no new DI registration</b> (ADR-010). The two public <c>IComposeService</c>
/// members stay on <c>ComposeService</c> and delegate here: the interface is the service's contract
/// to keep, only the implementation moves. Behaviour is unchanged; this is a move, not a rewrite.
/// </para>
/// </summary>
internal sealed class ComposeAnnotationStore
{
    /// <summary>
    /// ADR-040 <c>{bindingId}@t{n}</c> ledger-ref shape validator (mirrors
    /// <see cref="Ai.PublicContracts.OutcomeCard"/>'s own ledger-key validation intent).
    /// </summary>
    private static readonly Regex LedgerRefPattern = new(@"^.+@t\d+$", RegexOptions.Compiled);

    private readonly ChatSessionManager _sessions;
    private readonly ILogger _logger;

    internal ComposeAnnotationStore(ChatSessionManager sessions, ILogger logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    internal async Task<ComposeAnnotationsState> GetAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        var session = await _sessions.GetSessionAsync(tenantId, sessionId, cancellationToken).ConfigureAwait(false);
        return new ComposeAnnotationsState
        {
            AnchoredAnnotations = session?.AnchoredAnnotations ?? Array.Empty<AnchoredAnnotation>(),
            DefinedTermsTracking = session?.DefinedTermsTracking ?? Array.Empty<DefinedTerm>(),
        };
    }

    internal async Task<ComposeAnnotationsState> SaveAsync(
        SaveComposeAnnotationsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("SessionId is required.", nameof(request));

        ValidateLedgerRefs(request.AnchoredAnnotations, request.DefinedTermsTracking);

        var session = await _sessions.GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException(
                $"Compose session not found: session={request.SessionId} tenant={request.TenantId}. " +
                "Annotations can only be saved onto an existing session (create one via LoadAsync first).");
        }

        // Partial-replace: a null collection on the request leaves the stored collection
        // unchanged; a non-null (possibly empty) collection replaces it wholesale. Mutable
        // by design (accept/reject/edit) — NOT an append to the append-only ledger.
        var updated = session with
        {
            AnchoredAnnotations = request.AnchoredAnnotations ?? session.AnchoredAnnotations,
            DefinedTermsTracking = request.DefinedTermsTracking ?? session.DefinedTermsTracking,
            LastActivity = DateTimeOffset.UtcNow,
        };

        await _sessions.UpdateSessionCacheAsync(updated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Compose annotations saved: tenant={TenantId} session={SessionId} annotations={AnnotationCount} definedTerms={DefinedTermCount}",
            request.TenantId, request.SessionId,
            updated.AnchoredAnnotations?.Count ?? 0, updated.DefinedTermsTracking?.Count ?? 0);

        return new ComposeAnnotationsState
        {
            AnchoredAnnotations = updated.AnchoredAnnotations ?? Array.Empty<AnchoredAnnotation>(),
            DefinedTermsTracking = updated.DefinedTermsTracking ?? Array.Empty<DefinedTerm>(),
        };
    }

    /// <summary>
    /// Validates that every supplied <see cref="AnchoredAnnotation.Provenance"/> /
    /// <see cref="DefinedTerm.Provenance"/> ledger ref is in ADR-040 <c>{bindingId}@t{n}</c>
    /// form BEFORE anything persists (fail fast — no partial writes).
    /// </summary>
    private static void ValidateLedgerRefs(
        IReadOnlyList<AnchoredAnnotation>? annotations,
        IReadOnlyList<DefinedTerm>? definedTerms)
    {
        if (annotations is not null)
        {
            foreach (var a in annotations)
            {
                if (a.Provenance is not null && !LedgerRefPattern.IsMatch(a.Provenance.LedgerRef))
                {
                    throw new ArgumentException(
                        $"AnchoredAnnotation '{a.Id}' provenance.ledgerRef '{a.Provenance.LedgerRef}' " +
                        "does not match the ADR-040 {bindingId}@t{n} format.",
                        nameof(annotations));
                }
            }
        }

        if (definedTerms is not null)
        {
            foreach (var t in definedTerms)
            {
                if (t.Provenance is not null && !LedgerRefPattern.IsMatch(t.Provenance.LedgerRef))
                {
                    throw new ArgumentException(
                        $"DefinedTerm '{t.Term}' provenance.ledgerRef '{t.Provenance.LedgerRef}' " +
                        "does not match the ADR-040 {bindingId}@t{n} format.",
                        nameof(definedTerms));
                }
            }
        }
    }
}
