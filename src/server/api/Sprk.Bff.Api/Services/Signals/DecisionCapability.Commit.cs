using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The decision capability's commit member (spaarkeai-mcp-bff-api-r1 task 014, design §8 B-1, spec FR-08, FR-10 as amended 2026-11-07,
/// option C): the REST route (<c>POST /api/v1/signals/{signalId}/decision</c>) and the MCP complete tool (task 024) record a decision
/// through this one entry, so both reach <see cref="DecisionCommitService.CommitAsync"/> and nothing else.
/// </summary>
/// <remarks>
/// <para><b>Direct delegation.</b> <see cref="DecisionCommitService"/> stays the single owner of completion (ADR-002 WP-1) and is not
/// changed: the commit order, the ledger replay, the invoice-review refusal (D-207), the caller-rights check and the locks run there,
/// in place. This member passes the request's <see cref="HttpContext"/> through and adds no rule. Every invariant, with its line, is
/// listed in <c>projects/spaarkeai-mcp-bff-api-r1/notes/b1-commit-invariants.md</c>.</para>
/// <para><b>No caller, no commit.</b> A null context or a principal with no authenticated identity answers
/// <see cref="UserContextRequiredCommitResult"/> before the service is called: nothing is read, locked or written and the review
/// ledger is not touched (FR-10; ADR-002 WP-6, fail closed). A REST request never reaches it: the route's <c>RequireAuthorization</c>
/// answers 401 first.</para>
/// </remarks>
public sealed partial class DecisionCapability
{
    /// <summary>
    /// The commit's <c>UserContextRequired</c> answer. <see cref="DecisionCommitStatus"/> is ontology's type and has no such status, so
    /// it is the <see cref="DecisionCommitStatus.CallerUnresolved"/> result tagged with that code. <see cref="DecisionCommitService"/>
    /// never sets a reason on <see cref="DecisionCommitStatus.CallerUnresolved"/>, and <see cref="DecisionResultMapper"/>
    /// tells this one apart by reference, so a service answer stays the single 403.
    /// </summary>
    public static readonly DecisionCommitResult UserContextRequiredCommitResult = new()
    {
        Status = DecisionCommitStatus.CallerUnresolved,
        ReasonCode = DataverseUserClientErrorCodes.UserContextRequired,
    };

    /// <inheritdoc />
    public async Task<DecisionCommitResult> CommitAsync(HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct)
    {
        if (http is null || !HasSignedInCaller(http.User))
        {
            return UserContextRequiredCommitResult;
        }

        return await _commits.CommitAsync(signalId, review, http, ct);
    }
}
