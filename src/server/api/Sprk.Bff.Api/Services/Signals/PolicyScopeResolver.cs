using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// Scope-matching input for policy resolution (spec FR-09/FR-10): the dimensions a predicate evaluation run is
/// matched against. <see cref="Tenant"/> null/blank matches policies with a blank <c>sprk_tenant</c> ("all
/// tenants"); <see cref="MatterId"/> null matches policies with an empty <c>sprk_matter</c> ("all matters").
/// </summary>
public sealed record PolicyScopeRequest(string? Tenant, Guid? MatterId);

/// <summary>
/// One <c>sprk_policy</c> row whose scope matched a <see cref="PolicyScopeRequest"/> — carries just enough for
/// a caller to load its current <c>sprk_policyversion</c> (the rule body) in a second step. A
/// <see cref="PolicyScopeResolution"/>'s <see cref="PolicyScopeResolution.MatchedPolicies"/> list is already
/// ordered by <see cref="PolicyScopeResolver"/>; callers should not re-sort.
/// </summary>
public sealed record MatchedPolicy(Guid PolicyId, string PolicyCode, int Priority, Guid? CurrentVersionId);

/// <summary>
/// The outcome of a <see cref="PolicyScopeResolver.ResolveAsync"/> call. Produced on BOTH the success and the
/// fail-closed path — <see cref="PolicyScopeResolver"/> never throws for this outcome, mirroring
/// <see cref="Sprk.Bff.Api.Services.Communication.CommunicationRuleGate"/>'s <c>CommunicationRuleDecision</c>
/// shape (always a value, both branches).
/// </summary>
/// <remarks>
/// <see cref="ReadFailed"/> MUST be checked before treating an empty <see cref="MatchedPolicies"/> as "no
/// policy applies" — a store-read failure also yields an empty list, but for a structurally different reason
/// that callers (the nightly evaluator, task 031; the event triggers, task 032 — neither landed yet) need to
/// distinguish, so a transient read failure does not silently evaluate nothing and look like a working quiet
/// system (spec NFR-03).
/// </remarks>
public sealed record PolicyScopeResolution(
    bool ReadFailed,
    string Reason,
    IReadOnlyList<MatchedPolicy> MatchedPolicies)
{
    public static PolicyScopeResolution Failed(string reason) =>
        new(ReadFailed: true, Reason: reason, MatchedPolicies: Array.Empty<MatchedPolicy>());

    public static PolicyScopeResolution Ok(IReadOnlyList<MatchedPolicy> matches, string reason) =>
        new(ReadFailed: false, Reason: reason, MatchedPolicies: matches);
}

/// <summary>
/// Resolves which enabled <c>sprk_policy</c> rows apply to a given scope (spec FR-09, FR-10). Scope-matching,
/// ordering and fail-closed failure semantics are copied VERBATIM from the shipped
/// <see cref="Sprk.Bff.Api.Services.Communication.CommunicationRuleGate"/> (lines 108-188 of that file) rather
/// than re-derived — per root CLAUDE.md §11 and this project's CLAUDE.md §3.3/§3.4, these semantics are already
/// settled and tested; re-deriving them is how they drift.
/// </summary>
/// <remarks>
/// <para><b>Scope semantics — verbatim from CommunicationRuleGate.cs:175-188.</b> A blank <c>sprk_tenant</c>
/// matches every tenant. An empty <c>sprk_matter</c> matches every matter.</para>
/// <para><b>Ordering — verbatim from CommunicationRuleGate.cs:129-133.</b>
/// <c>OrderBy(sprk_priority ?? 500).ThenBy(Id)</c> — lowest priority wins; ties broken by record id.</para>
/// <para><b>Fail-closed — verbatim from CommunicationRuleGate.cs:108-126.</b> A policy-store read failure
/// returns <see cref="PolicyScopeResolution.ReadFailed"/> = <c>true</c> with reason
/// <c>"policy-store-read-failed"</c> — it does NOT proceed with an empty policy set, which would silently
/// evaluate nothing and look like a working quiet system. Deciding whether that failure aborts a whole
/// evaluation run or only the affected policy/matter is the CALLER's decision (task 031 / task 032, neither
/// landed yet) — this resolver's job is only to make the distinction available, never to collapse it into an
/// empty list (see the task 023 escalation note in <c>notes/</c> for why this boundary was drawn here rather
/// than guessed at the run-granularity question).</para>
/// <para><b>Policy authoring defaults — spec FR-10.</b> <c>sprk_enabled</c> defaults No on the real
/// <c>spaarkedev1</c> schema (verified empirically 2026-10-03 — see task notes) — enforced here by filtering
/// to <c>sprk_enabled = true</c> at the query, the same mechanism
/// <c>CommunicationRuleGate.LoadEnabledRulesAsync</c> uses, so a newly authored policy is excluded until
/// explicitly enabled. <c>sprk_priority</c> has NO Dataverse-level default (also verified empirically — a
/// probe row created with no priority value persisted a null, not 500); the 500 fallback is therefore an
/// APPLICATION-level concern, applied here via <c>?? 500</c> exactly as CommunicationRuleGate applies it.</para>
/// <para><b>Placement / justification (root §10/§11).</b> Concrete class, no interface (ADR-010) — this
/// project's design.md §3.2 explicitly defers introducing an <c>IPolicyEvaluator</c> contract until a second
/// consumer exists. Lives in <c>Services/Signals/</c> — a new namespace for the Signal/Policy domain (spec
/// FR-09..FR-16) — beside the predicate compiler (task 021) and the Signal writer (task 030) that will consume
/// this resolver's output once they land. No new endpoint, no new background work, no new package: pure
/// synchronous domain code called by whichever BFF-hosted workload needs it, so no ADR-052 workload-placement
/// question arises (this is not itself a workload with its own host).</para>
/// </remarks>
public sealed class PolicyScopeResolver
{
    private const string PolicyEntity = "sprk_policy";
    private const string PolicyCodeColumn = "sprk_policycode";
    private const string TenantColumn = "sprk_tenant";
    private const string MatterColumn = "sprk_matter";
    private const string EnabledColumn = "sprk_enabled";
    private const string PriorityColumn = "sprk_priority";
    private const string CurrentVersionColumn = "sprk_currentversion";

    private static readonly string[] PolicyColumns =
    {
        PolicyCodeColumn, TenantColumn, MatterColumn, PriorityColumn, CurrentVersionColumn,
    };

    private readonly IGenericEntityService _entityService;
    private readonly ILogger<PolicyScopeResolver> _logger;

    public PolicyScopeResolver(IGenericEntityService entityService, ILogger<PolicyScopeResolver> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves <paramref name="request"/> against the enabled <c>sprk_policy</c> rows. Never throws for a
    /// store-read failure (that is the fail-closed VALUE, not an exception) — only guards null input.
    /// </summary>
    public async Task<PolicyScopeResolution> ResolveAsync(
        PolicyScopeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<Entity> policies;
        try
        {
            policies = await LoadEnabledPoliciesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fail-closed: a policy-store read failure denies (no silent "no policies" on unreadable state).
            _logger.LogWarning(ex,
                "[policy-scope] policy store read failed — fail-closed, no policies evaluated | Tenant: {Tenant}, MatterId: {MatterId}",
                request.Tenant, request.MatterId);
            return PolicyScopeResolution.Failed("policy-store-read-failed");
        }

        // Match: tenant blank-or-equal AND matter empty-or-equal; lowest sprk_priority wins (then id for determinism).
        var matched = policies
            .Where(p => TenantMatches(p, request.Tenant) && MatterMatches(p, request.MatterId))
            .OrderBy(p => p.GetAttributeValue<int?>(PriorityColumn) ?? 500)
            .ThenBy(p => p.Id)
            .Select(ToMatchedPolicy)
            .ToList();

        var result = PolicyScopeResolution.Ok(matched, matched.Count > 0 ? "matched" : "no-matching-policy");

        _logger.LogInformation(
            "[policy-scope] resolved {Count} matching policies | Tenant: {Tenant}, MatterId: {MatterId}, Reason: {Reason}",
            matched.Count, request.Tenant, request.MatterId, result.Reason);

        return result;
    }

    private async Task<IReadOnlyList<Entity>> LoadEnabledPoliciesAsync(CancellationToken ct)
    {
        var query = new QueryExpression(PolicyEntity)
        {
            ColumnSet = new ColumnSet(PolicyColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(EnabledColumn, ConditionOperator.Equal, true);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active

        var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result?.Entities?.ToList() ?? (IReadOnlyList<Entity>)Array.Empty<Entity>();
    }

    private static bool TenantMatches(Entity policy, string? requestTenant)
    {
        var policyTenant = policy.GetAttributeValue<string>(TenantColumn);
        // Blank policy tenant = "all tenants" (verbatim CommunicationRuleGate.cs:175-181).
        return string.IsNullOrWhiteSpace(policyTenant)
               || string.Equals(policyTenant, requestTenant, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatterMatches(Entity policy, Guid? requestMatterId)
    {
        var policyMatter = policy.GetAttributeValue<EntityReference>(MatterColumn);
        // Empty policy matter = "all matters" (verbatim CommunicationRuleGate.cs:183-188).
        return policyMatter is null || policyMatter.Id == Guid.Empty || policyMatter.Id == requestMatterId;
    }

    private static MatchedPolicy ToMatchedPolicy(Entity policy)
    {
        var currentVersion = policy.GetAttributeValue<EntityReference>(CurrentVersionColumn);
        return new MatchedPolicy(
            PolicyId: policy.Id,
            PolicyCode: policy.GetAttributeValue<string>(PolicyCodeColumn) ?? string.Empty,
            Priority: policy.GetAttributeValue<int?>(PriorityColumn) ?? 500,
            CurrentVersionId: currentVersion is { Id: var id } && id != Guid.Empty ? id : null);
    }
}
