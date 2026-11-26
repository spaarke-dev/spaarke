namespace Sprk.Bff.Api.Api.Ai;

// COMPONENT JUSTIFICATION (CLAUDE.md section 11), spaarkeai-mcp-bff-api-r2 task 031.
//   Existing: the analysis routes answer with IResult (ProblemDetails / 201 / 200); AnalysisCreateAuthorization (Api/Filters) is the
//     create decision's typed answer; no type carries the create's or the read's OUTCOME to a host that is not HTTP.
//   Extension: these are the result shapes of AnalysisEndpoints.StartAnalysisAsCallerAsync / ReadAnalysisAsCallerAsync, the two
//     caller-facing methods beside the routes (007/008/024 pattern); no DI registration, no package, no state.
//   Cost of doing nothing: the MCP tools would have to parse the routes' IResult, or name Services/Ai types the ADR-013 MCP guard forbids.

/// <summary>What <see cref="AnalysisEndpoints.StartAnalysisAsCallerAsync"/> did. <see cref="UserContextRequired"/> is the zero value.</summary>
public enum AnalysisStartOutcome
{
    /// <summary>No signed-in caller: nothing was read or written.</summary>
    UserContextRequired = 0,

    /// <summary>The input is malformed (the route's 400).</summary>
    Invalid = 1,

    /// <summary>Refused with the route's status and <c>reasonCode</c> (403 from the checks; 409 / 503 / 403 from the owner rule).</summary>
    Refused = 2,

    /// <summary>The create did not complete (the route's bare 500).</summary>
    Failed = 3,

    /// <summary>The analysis row was created (not run).</summary>
    Created = 4,
}

/// <summary>
/// The answer of <see cref="AnalysisEndpoints.StartAnalysisAsCallerAsync"/>: an outcome, the route's status and <c>reasonCode</c> for a
/// failure, the route's 400 text for malformed input, and the new analysis id. Never exception text or record text (ADR-015).
/// </summary>
public sealed record AnalysisStartResult
{
    private AnalysisStartResult(AnalysisStartOutcome outcome, string? reasonCode, int status, string? message, Guid? analysisId, int warningCount)
    {
        Outcome = outcome;
        ReasonCode = reasonCode;
        Status = status;
        Message = message;
        AnalysisId = analysisId;
        WarningCount = warningCount;
    }

    public AnalysisStartOutcome Outcome { get; }

    /// <summary>The route's <c>reasonCode</c> for the same failure; <c>null</c> when created or invalid.</summary>
    public string? ReasonCode { get; }

    /// <summary>The HTTP status the route answers for the same case.</summary>
    public int Status { get; }

    /// <summary>For <see cref="AnalysisStartOutcome.Invalid"/>: the route's 400 detail (fixed text).</summary>
    public string? Message { get; }

    /// <summary>The created analysis's id.</summary>
    public Guid? AnalysisId { get; }

    /// <summary>How many warnings the create returned (the route returns their text; a model-visible host reports only that there were some).</summary>
    public int WarningCount { get; }

    internal static AnalysisStartResult Failure(AnalysisStartOutcome outcome, string? reasonCode, int status, string? message) =>
        new(outcome, reasonCode, status, message, null, 0);

    internal static AnalysisStartResult Created(Guid analysisId, int warningCount) =>
        new(AnalysisStartOutcome.Created, null, StatusCodes.Status201Created, null, analysisId, warningCount);
}

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
