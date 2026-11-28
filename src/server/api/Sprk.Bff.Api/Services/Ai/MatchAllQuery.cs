namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// The one definition of a "match everything" search query: a lone <c>*</c> (surrounding whitespace ignored).
/// </summary>
/// <remarks>
/// <para>
/// A match-all is a plain keyword search over the caller's filters. It must not be embedded, must not run a vector
/// leg, and must not ask for highlights or semantic ranking: the index answers 400 to highlights on a query that is
/// <c>*</c> plus vector queries, and a vector or semantic ranker has nothing to rank a wildcard against.
/// </para>
/// <para>
/// Finance's invoice search keeps its own copy of this one-liner because non-AI code reaches the AI services only
/// through <c>PublicContracts</c>.
/// </para>
/// </remarks>
internal static class MatchAllQuery
{
    /// <summary>True when <paramref name="query"/> is a lone <c>*</c> after trimming.</summary>
    public static bool IsMatchAll(string? query) => query is not null && query.Trim() == "*";
}
