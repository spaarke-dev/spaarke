using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The workforce lookup search (spaarkeai-mcp-bff-api-r1 task 016, design §8 B-3, spec FR-07): the rule the Console's browser picker
/// applies today (<c>Spaarke.AI.Widgets/src/widgets/workspace/worklistLookup.ts</c>), moved to the server so a host that is not the
/// Console (the MCP tool, task 023) cannot widen a search. It follows the external SPA's scoped read (a query built on the server,
/// scoped, failing closed; <c>ExternalModuleDataEndpoints</c>) on the workforce plane, never through that route or its policy.
/// </summary>
/// <remarks>
/// <para><b>Order.</b> (1) The caller guard: no request context or no authenticated identity is <c>UserContextRequired</c> before any
/// Dataverse call. (2) <see cref="SignalCoreRecordAccess.AuthorizeAsync"/>, the decision-plan route's own first act: anything but
/// Allowed returns that outcome, so the route answers exactly as the plan route does. (3) <see cref="DecisionPlanService.GetAsync"/>, the
/// plan route's second act: a refused plan is the plan route's 422; a fault is Unavailable. (4) The parameter's table, from the plan
/// alone. (5) One read AS THE CALLER through <see cref="IDataverseUserClient"/>, capped at <see cref="MaxResults"/>.</para>
/// <para><b>Why not <see cref="IDecisionCapability.GetPlanAsync"/>.</b> It writes the plan-read stamp the commit route reads as the
/// start of the review (<see cref="DecisionPlanReadStamp"/>); a search on every keystroke would keep moving that start. Its response also
/// carries no matter, which the budget scope needs. Steps (2) and (3) are its first two calls, unchanged, so the access answer and the
/// offered parameters are the plan route's. The Create budget and Mark complete adjustments it applies change no Person or Lookup
/// parameter (a matter with no active budget has no budget to find).</para>
/// <para><b>Scope per table (design §8 B-3).</b> Person: active contacts. <c>sprk_budget</c>: the active budgets of the Signal's MATTER
/// (<c>_sprk_matter_value</c> = the core record when its table is <c>sprk_matter</c>), the rule <c>ReviseBudgetExecutor</c> refuses
/// against (D-18); a Signal without a matter has no budget to offer and nothing is read. <c>sprk_organization</c>: active organisations.
/// Any other parameter, or a code two offered parameters disagree on, is <see cref="DecisionLookupOutcome.InvalidParameter"/>.</para>
/// <para><b>The typed text</b> is trimmed, refused above <see cref="MaxQueryLength"/> characters, its <c>LIKE</c> wildcards
/// bracket-escaped, escaped as an OData string literal (quotes doubled) and then URL-encoded, so neither a quote nor <c>&amp;</c>,
/// <c>$</c> or <c>#</c> can end the literal or the query option, and <c>%</c> or <c>_</c> cannot match every row. Contacts and organisations need at least <see cref="MinQueryLength"/> characters (an empty list otherwise, nothing read);
/// a budget search with less lists the matter's budgets.</para>
/// <para><b>Fail closed.</b> A failed or malformed read is Unavailable, never a partial list; rows beyond <see cref="MaxResults"/> are
/// dropped even if Dataverse returns them. Logs carry the Signal id, the table, status and error codes only (ADR-015): never the typed
/// text, a record name or a claim.</para>
/// <para><b>Placement justification (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> The decision slice's data owner is
/// the BFF (FR-24) and the search needs the decision plan and the caller's OBO Dataverse client (spec "Placement Justification": lookup
/// search in the BFF). No package, no AI-internal type, no background work; scoped like the services it calls and registered
/// unconditionally (SignalsModule), like the route that uses it.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: the Console's browser picker (<c>worklistLookup.ts</c>,
/// <c>Xrm.WebApi</c>) and the external plane's <c>POST /api/v1/external/api/dataverse/fetch</c>. Extension: a browser <c>Xrm</c> helper
/// cannot run on the server, and the external route's dual-scheme policy and access guard must not serve workforce callers. Cost of doing
/// nothing: revise-budget, reassign, add-todo, assign-work, assign-task and the inquiry's outside firm cannot be completed outside the
/// Console, and D-18 stays enforced only in the browser.</para>
/// </remarks>
public sealed class DecisionLookupSearch : IDecisionLookupSearch
{
    /// <summary>The most candidates one call returns (FR-07 "capped"; notes/decisions.md).</summary>
    public const int MaxResults = 25;

    /// <summary>The fewest characters a contact or organisation search needs, as the Console's picker.</summary>
    public const int MinQueryLength = 2;

    /// <summary>The longest text searched; longer is <see cref="DecisionLookupOutcome.InvalidParameter"/>.</summary>
    public const int MaxQueryLength = 100;

    internal const string ContactEntity = "contact";
    internal const string BudgetEntity = "sprk_budget";
    internal const string OrganizationEntity = "sprk_organization";

    /// <summary>The tables a Person or Lookup parameter can search.</summary>
    internal enum LookupTable
    {
        Contact,
        MatterBudget,
        Organization,
    }

    private readonly SignalCoreRecordAccess _signalAccess;
    private readonly DecisionPlanService _planService;
    private readonly IDataverseUserClient _caller;
    private readonly ILogger<DecisionLookupSearch> _logger;

    public DecisionLookupSearch(
        SignalCoreRecordAccess signalAccess,
        DecisionPlanService planService,
        IDataverseUserClient caller,
        ILogger<DecisionLookupSearch> logger)
    {
        _signalAccess = signalAccess ?? throw new ArgumentNullException(nameof(signalAccess));
        _planService = planService ?? throw new ArgumentNullException(nameof(planService));
        _caller = caller ?? throw new ArgumentNullException(nameof(caller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DecisionLookupResult> SearchAsync(
        HttpContext? http, Guid signalId, string? parameterCode, string? query, CancellationToken ct)
    {
        if (http is null || !DecisionCapability.HasSignedInCaller(http.User))
        {
            return DecisionLookupResult.UserContextRequired;
        }

        var decision = await _signalAccess.AuthorizeAsync(signalId, ct);
        if (decision.Outcome != SignalAccessOutcome.Allowed || decision.Lane is not { } lane || decision.PolicyVersionId is not { } versionId)
        {
            return DecisionLookupResult.NotAllowed(decision.Outcome);
        }

        DecisionPlanResolution resolution;
        try
        {
            resolution = await _planService.GetAsync(versionId, lane, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision lookup: the plan of Signal {SignalId} could not be read ({ExceptionType}); answering unavailable.",
                signalId, ex.GetType().Name);
            return DecisionLookupResult.Unavailable;
        }

        if (resolution.Plan is not { } plan)
        {
            return DecisionLookupResult.Refused(resolution.RefusalReason);
        }

        var text = query?.Trim() ?? string.Empty;
        if (TargetOf(plan, parameterCode) is not { } table || text.Length > MaxQueryLength)
        {
            return DecisionLookupResult.InvalidParameter;
        }

        var matterId = string.Equals(decision.CoreTable, "sprk_matter", StringComparison.OrdinalIgnoreCase) ? decision.CoreId : null;
        var entity = EntityOf(table);
        if (PathFor(table, matterId, text) is not { } path)
        {
            return DecisionLookupResult.Ok(new DecisionLookupResponse(parameterCode!, entity, []));
        }

        DataverseUserResponse response;
        try
        {
            response = await _caller.GetAsync(path, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision lookup: the {Entity} search for Signal {SignalId} faulted ({ExceptionType}); answering unavailable.",
                entity, signalId, ex.GetType().Name);
            return DecisionLookupResult.Unavailable;
        }

        if (!response.IsSuccess)
        {
            if (response.ErrorCode == DataverseUserClientErrorCodes.UserContextRequired)
            {
                return DecisionLookupResult.UserContextRequired;
            }

            _logger.LogWarning("Decision lookup: the {Entity} search for Signal {SignalId} failed (status {Status}, {ErrorCode}); answering unavailable.",
                entity, signalId, response.StatusCode, response.ErrorCode);
            return DecisionLookupResult.Unavailable;
        }

        if (CandidatesOf(table, response.Body) is not { } candidates)
        {
            _logger.LogWarning("Decision lookup: the {Entity} search for Signal {SignalId} returned no row list; answering unavailable.", entity, signalId);
            return DecisionLookupResult.Unavailable;
        }

        return DecisionLookupResult.Ok(new DecisionLookupResponse(parameterCode!, entity, candidates));
    }

    /// <summary>
    /// The table <paramref name="parameterCode"/> searches, from the plan alone: every action and Next step the plan offers that has a
    /// parameter with that code (ordinal) must name the same searchable table. Null when none does, when one is not a Person or a known
    /// Lookup, or when two disagree.
    /// </summary>
    internal static LookupTable? TargetOf(ResolvedDecisionPlan plan, string? parameterCode)
    {
        if (string.IsNullOrWhiteSpace(parameterCode))
        {
            return null;
        }

        var tables = plan.Actions.Concat(plan.NextSteps)
            .SelectMany(a => a.Parameters)
            .Where(p => string.Equals(p.Code, parameterCode, StringComparison.Ordinal))
            .Select(TableOf)
            .Distinct()
            .ToList();
        return tables.Count == 1 ? tables[0] : null;
    }

    private static LookupTable? TableOf(DecisionActionParameter parameter) =>
        parameter.Kind switch
        {
            DecisionParameterKind.Person => LookupTable.Contact,
            DecisionParameterKind.Lookup when parameter.LookupEntity == BudgetEntity => LookupTable.MatterBudget,
            DecisionParameterKind.Lookup when parameter.LookupEntity == OrganizationEntity => LookupTable.Organization,
            _ => null,
        };

    internal static string EntityOf(LookupTable table) =>
        table switch
        {
            LookupTable.Contact => ContactEntity,
            LookupTable.MatterBudget => BudgetEntity,
            _ => OrganizationEntity,
        };

    /// <summary>
    /// The OData text of a <c>contains()</c> literal. Dataverse runs <c>contains()</c> as SQL <c>LIKE</c>, so <c>[</c>, <c>%</c> and
    /// <c>_</c> are bracket-escaped first (<c>[</c> before the others, or it would re-escape their brackets; the same rule as
    /// <c>RecordContainerResolver.EscapeForLike</c>), so typed <c>%%</c> or <c>__</c> matches those characters, never every row. Then
    /// quotes are doubled and the whole literal is URL-encoded.
    /// </summary>
    internal static string Literal(string text) =>
        Uri.EscapeDataString(text
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal));

    /// <summary>
    /// The caller read for <paramref name="table"/>, built here and only here; null when there is nothing to search (a contact or
    /// organisation search shorter than <see cref="MinQueryLength"/>, or a budget search for a Signal without a matter).
    /// </summary>
    internal static string? PathFor(LookupTable table, Guid? matterId, string text)
    {
        var named = text.Length >= MinQueryLength;
        switch (table)
        {
            case LookupTable.Contact:
                return named
                    ? "contacts?$select=contactid,fullname,emailaddress1"
                      + $"&$filter=statecode eq 0 and (contains(fullname,'{Literal(text)}') or contains(emailaddress1,'{Literal(text)}'))"
                      + $"&$orderby=fullname asc&$top={MaxResults}"
                    : null;

            case LookupTable.MatterBudget:
                if (matterId is not { } matter || matter == Guid.Empty)
                {
                    return null;
                }

                return "sprk_budgets?$select=sprk_budgetid,sprk_name"
                    + $"&$filter=statecode eq 0 and _sprk_matter_value eq {matter:D}"
                    + (named ? $" and contains(sprk_name,'{Literal(text)}')" : string.Empty)
                    + $"&$orderby=sprk_name asc&$top={MaxResults}";

            default:
                return named
                    ? "sprk_organizations?$select=sprk_organizationid,sprk_organizationname"
                      + $"&$filter=statecode eq 0 and contains(sprk_organizationname,'{Literal(text)}')"
                      + $"&$orderby=sprk_organizationname asc&$top={MaxResults}"
                    : null;
        }
    }

    /// <summary>The candidates in a read's body, at most <see cref="MaxResults"/>; a row without a usable id is skipped. Null when the
    /// body has no row list.</summary>
    internal static IReadOnlyList<DecisionLookupCandidate>? CandidatesOf(LookupTable table, JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var (idColumn, nameColumn, unnamed) = table switch
        {
            LookupTable.Contact => ("contactid", "fullname", "Contact"),
            LookupTable.MatterBudget => ("sprk_budgetid", "sprk_name", "Budget"),
            _ => ("sprk_organizationid", "sprk_organizationname", "Organization"),
        };

        var candidates = new List<DecisionLookupCandidate>();
        foreach (var row in rows.EnumerateArray())
        {
            if (candidates.Count == MaxResults)
            {
                break;
            }

            if (row.ValueKind != JsonValueKind.Object || !Guid.TryParse(StringOf(row, idColumn), out var id) || id == Guid.Empty)
            {
                continue;
            }

            candidates.Add(new DecisionLookupCandidate(
                id,
                StringOf(row, nameColumn) ?? unnamed,
                table == LookupTable.Contact ? StringOf(row, "emailaddress1") : null));
        }

        return candidates;
    }

    private static string? StringOf(JsonElement row, string column) =>
        row.TryGetProperty(column, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}
