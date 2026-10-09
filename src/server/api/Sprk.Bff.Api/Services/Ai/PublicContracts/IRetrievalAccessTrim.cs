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

    /// <summary>
    /// The general form of <see cref="TrimAsync{T}"/>: each row names the RECORD that decides whether the caller may see
    /// it (its source document, or, when it has none, the matter/project/invoice/work assignment it is about). One
    /// caller-scoped read per record kind per <see cref="RetrievalAccessTrim.MaxIdsPerRead"/> keys. A row whose key is
    /// null is dropped. Used where a retrieved row is not a document chunk (Insights observations, L3 entity context).
    /// </summary>
    Task<RetrievalTrimResult<T>> TrimByRecordAsync<T>(
        IReadOnlyList<T> rows,
        Func<T, RetrievalRecordKey?> recordOf,
        string? callerObjectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The record a retrieved row is checked against: an entity set, the column to match, and the canonical value. Built only
/// through the factories below, so only a fixed set of (entity set, column) pairs ever reaches a Dataverse filter.
/// </summary>
public sealed record RetrievalRecordKey
{
    private static readonly Dictionary<string, (string Set, string Key)> ParentRecordTables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["matter"] = ("sprk_matters", "sprk_matterid"),
            ["project"] = ("sprk_projects", "sprk_projectid"),
            ["invoice"] = ("sprk_invoices", "sprk_invoiceid"),
            ["workassignment"] = ("sprk_workassignments", "sprk_workassignmentid"),
        };

    private const string DocumentSet = "sprk_documents";
    private const string DocumentIdField = "sprk_documentid";
    private const string DriveItemIdField = "sprk_driveitemid";

    internal RetrievalRecordKey(string entitySetName, string keyField, string value)
    {
        EntitySetName = entitySetName;
        KeyField = keyField;
        Value = value;
    }

    /// <summary>Entity set name, e.g. <c>sprk_documents</c>.</summary>
    public string EntitySetName { get; }

    /// <summary>The column matched, e.g. <c>sprk_documentid</c>.</summary>
    public string KeyField { get; }

    /// <summary>Canonical value: bare lowercase GUID for id columns (ADR-044), trimmed text otherwise.</summary>
    public string Value { get; }

    /// <summary>A <c>sprk_document</c> by id. Null when the value is not a non-empty GUID (any spelling).</summary>
    public static RetrievalRecordKey? Document(string? documentId) =>
        CanonicalGuid(documentId) is { } id ? new RetrievalRecordKey(DocumentSet, DocumentIdField, id) : null;

    /// <summary>
    /// A <c>sprk_document</c> by the SharePoint Embedded item it points at (<c>sprk_driveitemid</c>), for evidence refs
    /// of the form <c>spe://drive/{driveId}/item/{itemId}</c>. The caller passes when they can read ANY document row on
    /// that item, which is the file the quote came from.
    /// </summary>
    public static RetrievalRecordKey? DocumentByDriveItem(string? driveItemId) =>
        string.IsNullOrWhiteSpace(driveItemId) ? null : new RetrievalRecordKey(DocumentSet, DriveItemIdField, driveItemId.Trim());

    /// <summary>
    /// A parent business record (<c>matter</c>, <c>project</c>, <c>invoice</c>, <c>workassignment</c>; the
    /// <c>sprk_</c>-prefixed logical names are accepted too) by id. Null for any other type or a non-GUID id.
    /// </summary>
    public static RetrievalRecordKey? ParentRecord(string? entityType, string? id)
    {
        if (string.IsNullOrWhiteSpace(entityType) || CanonicalGuid(id) is not { } canonical)
        {
            return null;
        }

        var type = entityType.Trim();
        if (type.StartsWith("sprk_", StringComparison.OrdinalIgnoreCase))
        {
            type = type[5..];
        }

        return ParentRecordTables.TryGetValue(type, out var table)
            ? new RetrievalRecordKey(table.Set, table.Key, canonical)
            : null;
    }

    internal static bool IsGuidKey(string entitySet, string keyField) =>
        !(string.Equals(entitySet, DocumentSet, StringComparison.Ordinal)
          && string.Equals(keyField, DriveItemIdField, StringComparison.Ordinal));

    /// <summary>The canonical form of a value Dataverse returned for <paramref name="keyField"/>.</summary>
    internal static string? Canonical(string entitySet, string keyField, string? value) =>
        IsGuidKey(entitySet, keyField)
            ? CanonicalGuid(value)
            : string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CanonicalGuid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value.Trim(), out var id) && id != Guid.Empty
            ? id.ToString("D")
            : null;
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
