namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// The ONE trim that every AI retrieval path applies after it searches the document index and before any row reaches
/// the model or the user (unified-access-control-r2 task 176, GitHub #1511). A row survives only when its
/// <c>sprk_document</c> is readable by the CALLER, as Dataverse itself decides it.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> The document index carries no usable per-user ACL: <c>privilege_group_ids</c> is never
/// stamped by any indexer (finding A-21), so the RAG privilege filter treats every chunk as public. Before this seam, chat
/// tools (Document Search, Document Discovery, Knowledge Retrieval), playbook retrieval and Insights search handed the
/// model and the citations the text of documents on secure, Restricted and No Access records.</para>
/// <para><b>How it decides.</b> One read of the page's distinct document ids against <c>sprk_documents</c> through
/// <see cref="Infrastructure.Dataverse.IDataverseUserClient"/> (user OBO, no app-only path), at most
/// <see cref="RetrievalAccessTrim.MaxIdsPerRead"/> ids per read. Dataverse returns exactly the rows the caller can Read;
/// No Access walls are enforced by revoking shares, so the native read reflects them. One round trip per page replaced
/// the per-document <c>IAiAuthorizationService</c> loop (about 360 ms per cold document, measured on dev; owner decision
/// 2026-10-09, option B).</para>
/// <para><b>Fail closed.</b> No declared caller, no request principal, or a request principal that is not the declared
/// caller: no rows (<see cref="RetrievalTrimOutcome.NoVerifiedCaller"/>). A row without a parseable document id is
/// dropped. A read that fails for any reason (no token, OBO failure, 4xx/5xx, 429, malformed body): no rows
/// (<see cref="RetrievalTrimOutcome.CheckFailed"/>). Nothing is cached; each call asks Dataverse.</para>
/// </remarks>
public interface IRetrievalAccessTrim
{
    /// <summary>
    /// True when <paramref name="callerObjectId"/> is present and is the Entra object id of the principal on the current
    /// request, i.e. when <see cref="TrimAsync{T}"/> could evaluate rows at all. Paths that run without a signed-in user
    /// use it to skip a search whose rows would all be withheld.
    /// </summary>
    bool CanEvaluate(string? callerObjectId);

    /// <summary>
    /// Returns the rows of <paramref name="rows"/> whose document the caller can Read, in their original order.
    /// </summary>
    /// <param name="rows">Retrieved rows (chunks), ranked.</param>
    /// <param name="documentIdOf">The row's <c>sprk_document</c> id (any GUID spelling; null or non-GUID drops the row).</param>
    /// <param name="callerObjectId">The Entra <c>oid</c> of the user the retrieval is for. Must equal the oid of the
    /// current request's principal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RetrievalTrimResult<T>> TrimAsync<T>(
        IReadOnlyList<T> rows,
        Func<T, string?> documentIdOf,
        string? callerObjectId,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a <see cref="IRetrievalAccessTrim.TrimAsync{T}"/> call.</summary>
public enum RetrievalTrimOutcome
{
    /// <summary>The caller was verified and Dataverse answered; <see cref="RetrievalTrimResult{T}.Rows"/> holds the readable rows.</summary>
    Evaluated,

    /// <summary>No declared caller, no request principal, or a mismatch between them. No rows.</summary>
    NoVerifiedCaller,

    /// <summary>The access read could not be completed. No rows.</summary>
    CheckFailed,
}

/// <summary>Result of a trim.</summary>
/// <param name="Rows">The rows the caller may see (empty unless <paramref name="Outcome"/> is Evaluated).</param>
/// <param name="Outcome">How the trim ended.</param>
/// <param name="DroppedCount">How many input rows were withheld.</param>
public sealed record RetrievalTrimResult<T>(IReadOnlyList<T> Rows, RetrievalTrimOutcome Outcome, int DroppedCount)
{
    /// <summary>True when results were withheld because the check could not run, not because access was denied.</summary>
    public bool Withheld => Outcome != RetrievalTrimOutcome.Evaluated;

    /// <summary>
    /// The message a tool result carries when <see cref="Withheld"/> is true (goal 3: "say so in the result"). It names
    /// no document.
    /// </summary>
    public string? WithheldMessage => Outcome switch
    {
        RetrievalTrimOutcome.NoVerifiedCaller =>
            "Document results were withheld: no signed-in user could be verified for this request, so document access could not be checked.",
        RetrievalTrimOutcome.CheckFailed =>
            "Document results were withheld: document access could not be checked right now. Try again.",
        _ => null,
    };
}
