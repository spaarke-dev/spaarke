namespace Spaarke.Dataverse;

/// <summary>
/// A multi-request Dataverse transaction (<see cref="IGenericEntityService.ExecuteTransactionAsync"/>) did not complete.
/// All-or-nothing: either no request was applied (<see cref="OutcomeUnknown"/> false), or the outcome could not be learned
/// (true), in which case the transaction was sent as one unit and so was applied entirely or not at all, never partially.
/// </summary>
/// <remarks>
/// A subclass of <see cref="InvalidOperationException"/> so a caller that already handles the failure of
/// <see cref="IGenericEntityService.BulkUpdateAsync"/> handles this one the same way. The message names the failing request's
/// kind, table and id and adds no field value of its own (ADR-015: no extracted values in logs); it appends Dataverse's own fault text, which
/// names a column or a rule but is not built from our values.
/// </remarks>
public sealed class DataverseTransactionException : InvalidOperationException
{
    /// <param name="message">Worded by <c>DataverseServiceClientImpl.MapTransactionFailure</c>.</param>
    /// <param name="failedRequestIndex">Zero-based index, in the submitted list, of the request Dataverse says faulted; null when it did not say.</param>
    /// <param name="outcomeUnknown">True when Dataverse returned no fault at all (a timeout can land after the commit).</param>
    /// <param name="inner">The exception the SDK threw.</param>
    public DataverseTransactionException(string message, int? failedRequestIndex, bool outcomeUnknown, Exception? inner)
        : base(message, inner)
    {
        FailedRequestIndex = failedRequestIndex;
        OutcomeUnknown = outcomeUnknown;
    }

    /// <summary>Zero-based index in the submitted request list of the request that faulted; null when Dataverse did not identify one.</summary>
    public int? FailedRequestIndex { get; }

    /// <summary>
    /// True when no Dataverse fault exists anywhere in the exception chain (for example a timeout): the transaction may have committed.
    /// A caller whose resume path re-reads state (049's linked-invoice marker) handles this case by re-reading, not by assuming nothing landed.
    /// </summary>
    public bool OutcomeUnknown { get; }
}
