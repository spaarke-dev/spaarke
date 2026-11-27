namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 037a (owner decisions D-037-results and D-037-engine). The result types below moved here unchanged from
// Api/Ai/AnalysisCallerResults.cs (task 031), with the analysis start they describe, so the facade names no Api type.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md "MUST use the Services/Ai/PublicContracts/ facade"):
// the analysis create core (task 031's AnalysisEndpoints.CreateAnalysisRecordAsync and StartAnalysisAsCallerAsync) moves out of the
// Api layer into Services/Ai (AnalysisStartService) so that three hosts share ONE create: POST /api/ai/analysis/create,
// spaarke_start_analysis (an MCP tool, which may name only PublicContracts types: ADR-013 and the MCP ledger guard) and the Copilot
// playbook start (IPlaybookRunAi, D-037-engine). This file references no Api type. One stateless singleton DI registration; no package,
// no endpoint, no store, no background work.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: AnalysisEndpoints.StartAnalysisAsCallerAsync (task 031: the create decision, then the route's one RecordCreationService
//     create), which only Api code could reach; IPlaybookRunAi (starts and reads runs, creates nothing).
//   Extension: the method itself moves (not copied) and keeps its behaviour; the route handler, the MCP tool and the playbook start all
//     call it. IPlaybookRunAi cannot carry it: it is the run seam, and the tool spaarke_start_analysis creates without running.
//   Cost of doing nothing: the Copilot start cannot create the caller's analysis, so a run's results have nowhere the caller can read
//     them (037-decisions §1.2, kind A), or the create would be copied into Services/Ai (F2 duplication of an authorization path).
// =====================================================================================================================

/// <summary>What <see cref="IAnalysisStartAi.StartAsCallerAsync"/> did. <see cref="UserContextRequired"/> is the zero value.</summary>
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
/// The answer of <see cref="IAnalysisStartAi.StartAsCallerAsync"/>: an outcome, the route's status and <c>reasonCode</c> for a
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

/// <summary>The stable reason codes of an analysis start that are this host's own (the route's body carries none for them).</summary>
public static class AnalysisStartReasonCodes
{
    /// <summary>The <c>reasonCode</c> of a start the route would answer with its bare 500.</summary>
    public const string CreateFailed = "analysis.create_failed";
}

/// <summary>
/// THE analysis create for a host that is not <c>POST /api/ai/analysis/create</c> (spaarkeai-mcp-bff-api-r2 tasks 031 and 037a): today the
/// MCP tool <c>spaarke_start_analysis</c> and the Copilot playbook start (<see cref="IPlaybookRunAi.StartAsCallerAsync"/>).
/// </summary>
/// <remarks>
/// It is the route's rule and the route's create, in the route's order: the shared create decision (the caller's Create on
/// <c>sprk_analysis</c>, Read and AppendTo on the document, the playbook-use decision), then the route's one
/// <c>RecordCreationService</c> create as the caller, with what the decision allowed. No scope rows are associated and nothing is run.
/// The route handler calls the same create (<c>AnalysisStartService.CreateAnalysisRecordAsync</c>).
/// </remarks>
public interface IAnalysisStartAi
{
    /// <summary>
    /// Creates an analysis named <paramref name="name"/> on <paramref name="documentId"/> for the caller of <paramref name="http"/>, with
    /// <paramref name="playbookId"/> when given. A refusal carries the route's status and <c>reasonCode</c> for the same case; the generic
    /// failure (the route's bare 500) carries <see cref="AnalysisStartReasonCodes.CreateFailed"/>. Never throws except on cancellation; no
    /// exception text reaches the result.
    /// </summary>
    Task<AnalysisStartResult> StartAsCallerAsync(
        HttpContext? http, string? name, Guid documentId, Guid? playbookId, CancellationToken cancellationToken);
}
