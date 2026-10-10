using Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// An <see cref="IRetrievalAccessTrim"/> that lets every row through, for suites that test what a retrieval caller
/// does with rows it is ALLOWED to see (search options, formatting, citations). The trim itself, and every caller's
/// fail-closed behaviour, is tested against the real <see cref="RetrievalAccessTrim"/> in
/// <c>tests/integration/regression/Ai/Issue1511_AiRetrievalAccessTrimTests.cs</c> and
/// <c>tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PublicContracts/RetrievalAccessTrimTests.cs</c>.
/// </summary>
/// <remarks>Global namespace, like <c>TestSessionOwner</c>: referenced from several suites.</remarks>
public sealed class PermitAllRetrievalAccessTrim : IRetrievalAccessTrim
{
    public static readonly PermitAllRetrievalAccessTrim Instance = new();

    public bool CanEvaluate(string? callerObjectId) => true;

    public Task<RetrievalTrimResult<T>> TrimAsync<T>(
        IReadOnlyList<T> rows,
        Func<T, string?> documentIdOf,
        string? callerObjectId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new RetrievalTrimResult<T>(rows, RetrievalTrimOutcome.Evaluated, 0));

    public Task<RetrievalTrimResult<T>> TrimByRecordAsync<T>(
        IReadOnlyList<T> rows,
        Func<T, RetrievalRecordKey?> recordOf,
        string? callerObjectId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new RetrievalTrimResult<T>(rows, RetrievalTrimOutcome.Evaluated, 0));
}
