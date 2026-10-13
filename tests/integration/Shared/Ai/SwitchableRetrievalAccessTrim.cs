using Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// An <see cref="IRetrievalAccessTrim"/> for WebApplicationFactory fixtures: it delegates to <see cref="Inner"/>, which is
/// <see cref="PermitAllRetrievalAccessTrim"/> by default (suites that test parent-record authorization) and is set to a
/// real <see cref="RetrievalAccessTrim"/> by the tests that prove the document trim through the route (task 176).
/// Tests that switch it must reset it (<see cref="Reset"/>) because class fixtures are shared.
/// </summary>
public sealed class SwitchableRetrievalAccessTrim : IRetrievalAccessTrim
{
    public IRetrievalAccessTrim Inner { get; set; } = PermitAllRetrievalAccessTrim.Instance;

    public void Reset() => Inner = PermitAllRetrievalAccessTrim.Instance;

    public bool CanEvaluate(string? callerObjectId) => Inner.CanEvaluate(callerObjectId);

    public Task<RetrievalTrimResult<T>> TrimAsync<T>(
        IReadOnlyList<T> rows, Func<T, string?> documentIdOf, string? callerObjectId, CancellationToken cancellationToken = default)
        => Inner.TrimAsync(rows, documentIdOf, callerObjectId, cancellationToken);

    public Task<RetrievalTrimResult<T>> TrimByRecordAsync<T>(
        IReadOnlyList<T> rows, Func<T, RetrievalRecordKey?> recordOf, string? callerObjectId, CancellationToken cancellationToken = default)
        => Inner.TrimByRecordAsync(rows, recordOf, callerObjectId, cancellationToken);
}
