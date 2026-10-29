using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance.Coding;

/// <summary>
/// FR-22 step 4: proposes a code set when the firm's own codes, the matter-type mapping and the practice-area mapping did
/// not resolve one. Task 048's <see cref="CodeSetProposalService"/> is the AI implementation (registered in
/// <c>FinanceModule</c>); it never persists the suggestion. A mapping is written only by that service's admin-confirm route.
/// <see cref="NoCodeSetSuggestionProvider"/> remains as the Null-Object peer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an interface.</b> The suggestion is produced by a different Action in a later task. The resolver must not know how
/// it is made, and task 048 must not edit the resolver to add it (it replaces one DI registration). The resolver validates the
/// returned id (an active set with task codes) and flags the resolution <see cref="CodeSetSource.AiSuggestion"/>, so a
/// provider can never make a line pass the code-set guard with a set that does not exist.
/// </para>
/// <para>
/// A provider failure propagates: the resolver does not swallow it, because falling through to the settings default after a
/// provider fault would silently code an invoice with the wrong set.
/// </para>
/// </remarks>
public interface ICodeSetSuggestionProvider
{
    /// <summary>
    /// Proposes a code set, or <c>null</c> when it has no proposal (the resolver then uses the settings default).
    /// </summary>
    Task<Guid?> SuggestCodeSetAsync(CodeSetSuggestionContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a provider may use to propose a code set. Ids and lines only; the provider reads whatever else it needs.
/// </summary>
/// <param name="CoreRecord">The core record the invoice belongs to.</param>
/// <param name="MatterTypeId">The record's matter type, or <c>null</c> when none is set.</param>
/// <param name="PracticeAreaId">The record's practice area, or <c>null</c> when none is set.</param>
/// <param name="Lines">The lines being coded (their narratives show the kind of work billed).</param>
public sealed record CodeSetSuggestionContext(
    CodingCoreRecord CoreRecord,
    Guid? MatterTypeId,
    Guid? PracticeAreaId,
    IReadOnlyList<InvoiceLineToCode> Lines);

/// <summary>
/// The Null-Object <see cref="ICodeSetSuggestionProvider"/>: never proposes a set, so FR-22 step 4 is skipped (ADR-032 peer; not a
/// feature-flag fallback). Since task 048 it is no longer the registered provider; it stays for tests and as the explicit "no step 4" choice.
/// </summary>
public sealed class NoCodeSetSuggestionProvider : ICodeSetSuggestionProvider
{
    /// <inheritdoc />
    public Task<Guid?> SuggestCodeSetAsync(CodeSetSuggestionContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(null);
}
