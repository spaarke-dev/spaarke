namespace Sprk.Bff.Api.Api.Ai;

// COMPONENT JUSTIFICATION (CLAUDE.md section 11), spaarkeai-mcp-bff-api-r2 task 031.
//   Existing: the analysis routes answer with IResult (ProblemDetails / 201 / 200); no type carries the read's OUTCOME to a host that
//     is not HTTP.
//   Extension: these are the result shapes of AnalysisEndpoints.ReadAnalysisAsCallerAsync, the caller-facing read beside the routes
//     (007/008/024 pattern); no DI registration, no package, no state. (Task 037a moved the start's result types, with the start, to
//     Services/Ai/PublicContracts/IAnalysisStartAi.cs.)
//   Cost of doing nothing: the MCP tools would have to parse the routes' IResult, or name Services/Ai types the ADR-013 MCP guard forbids.

/// <summary>What <see cref="AnalysisEndpoints.ReadAnalysisAsCallerAsync"/> found. <see cref="UserContextRequired"/> is the zero value.</summary>
public enum AnalysisReadOutcome
{
    /// <summary>No signed-in caller: nothing was read.</summary>
    UserContextRequired = 0,

    /// <summary>Unknown, unreadable, hidden by the external narrowing, or gone: the route's one uniform 404.</summary>
    NotFound = 1,

    /// <summary>The caller may read it; <see cref="AnalysisReadResult.Analysis"/> is the route's 200 body.</summary>
    Found = 2,
}

/// <summary>The answer of <see cref="AnalysisEndpoints.ReadAnalysisAsCallerAsync"/>.</summary>
public sealed record AnalysisReadResult
{
    private AnalysisReadResult(AnalysisReadOutcome outcome, AnalysisDetailResult? analysis)
    {
        Outcome = outcome;
        Analysis = analysis;
    }

    public static readonly AnalysisReadResult UserContextRequired = new(AnalysisReadOutcome.UserContextRequired, null);

    public static readonly AnalysisReadResult NotFound = new(AnalysisReadOutcome.NotFound, null);

    public AnalysisReadOutcome Outcome { get; }

    /// <summary>The route's 200 body, when found.</summary>
    public AnalysisDetailResult? Analysis { get; }

    internal static AnalysisReadResult Found(AnalysisDetailResult analysis) => new(AnalysisReadOutcome.Found, analysis);
}
