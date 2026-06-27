// R3 Part 1 — User-Record Membership Resolution (org-membership implementation)
// Task 032 (2026-06-21): Implements IOrganizationMembershipResolver via
// Option (b) — configurable Lookup field on sprk_organization that points
// to systemuser. See notes/sprk-organization-mapping-decision.md for the
// full decision record and rationale.
//
// Behaviour:
//   - Reads MembershipOptions.OrganizationLookup.UserLookupField (operator-
//     configured logical name, e.g. "sprk_owneruser").
//   - If unset → returns empty list + logs Info ONCE per process (operator
//     setup pending; not an error — users with no org affiliations are the
//     common case).
//   - If set → executes a single FetchXml query filtered by the configured
//     lookup field equalling the systemuser GUID, capped by
//     MaxOrganizationsPerUser. Returns the organization GUIDs.
//   - All failure modes (missing field, query error, permission issue) fail
//     soft to an empty list + Warning log. Throwing here would cascade-fail
//     the entire membership pipeline for unrelated reasons.
//
// Operator alternatives (deferred — not in this implementation):
//   (a) Dataverse N:N between systemuser and sprk_organization — future swap.
//   (c) Team-per-organization derivation — future swap.
// The contract is identity-only (Guid in → IReadOnlyList<Guid> out), so any
// alternative mechanism can replace this implementation transparently.
//
// Reference: projects/spaarke-platform-foundations-r3/design.md Part 1 §
// Identity normalization contract (row 6 `Lookup → sprk_organization`);
// spec.md FR-1A.6 + Q4; ADR-034 (forthcoming, task 037).

using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Default <see cref="IOrganizationMembershipResolver"/> backed by a
/// configurable Lookup field on <c>sprk_organization</c> pointing at
/// <c>systemuser</c> (Option b — see notes/sprk-organization-mapping-decision.md).
/// </summary>
/// <remarks>
/// Also implements <see cref="IIdentityOrganizationResolver"/> (task 031's
/// coordination seam) so a single registration in <c>MembershipModule</c> can
/// satisfy both consumers: <c>IdentityNormalizationService</c> consumes the
/// <see cref="IIdentityOrganizationResolver"/> shape via
/// <c>IEnumerable&lt;IIdentityOrganizationResolver&gt;</c>; direct callers
/// (future playbook nodes) consume the <see cref="IOrganizationMembershipResolver"/>
/// shape. The seam's <c>contactId</c> parameter is intentionally ignored by
/// Option (b) — the configured Lookup field targets systemuser, not contact.
/// </remarks>
public sealed class OrganizationMembershipResolver
    : IOrganizationMembershipResolver, IIdentityOrganizationResolver
{
    /// <summary>Dataverse logical name of the organization entity.</summary>
    internal const string OrganizationEntityLogicalName = "sprk_organization";

    /// <summary>Dataverse logical name of the organization primary key attribute.</summary>
    internal const string OrganizationIdAttribute = "sprk_organizationid";

    private readonly IGenericEntityService _entityService;
    private readonly IOptionsMonitor<MembershipOptions> _options;
    private readonly ILogger<OrganizationMembershipResolver> _logger;

    /// <summary>
    /// Flag (volatile bool) to ensure the "no mapping configured" Info log
    /// fires at most once per process — avoids log spam on every call.
    /// </summary>
    private int _noMappingLogged;

    public OrganizationMembershipResolver(
        IGenericEntityService entityService,
        IOptionsMonitor<MembershipOptions> options,
        ILogger<OrganizationMembershipResolver> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetOrganizationIdsAsync(
        Guid systemUserId,
        PersonIdentity? identityContext,
        CancellationToken ct)
    {
        // identityContext is currently unused (Option b only needs the
        // systemuser GUID). Future mechanisms (a) and (c) may read teamIds or
        // contactId from it — keeping the parameter on the contract.
        _ = identityContext;

        if (systemUserId == Guid.Empty)
        {
            _logger.LogDebug(
                "OrganizationMembershipResolver: empty systemUserId — returning empty list");
            return Array.Empty<Guid>();
        }

        var lookup = _options.CurrentValue.OrganizationLookup;
        var lookupField = lookup.UserLookupField?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(lookupField))
        {
            // Log once per process; subsequent calls stay silent. Operators
            // see one Info entry when they wire the feature without setting
            // the lookup field; users without org affiliations remain the
            // common, correct case.
            if (Interlocked.Exchange(ref _noMappingLogged, 1) == 0)
            {
                _logger.LogInformation(
                    "OrganizationMembershipResolver: MembershipOptions.OrganizationLookup.UserLookupField is empty — " +
                    "returning empty organization list for all users. To enable, set the configuration to the logical " +
                    "name of a Lookup field on sprk_organization that points to systemuser " +
                    "(see projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md).");
            }

            return Array.Empty<Guid>();
        }

        var cap = Math.Max(1, lookup.MaxOrganizationsPerUser);

        try
        {
            // FetchXml: select sprk_organizationid where the configured lookup
            // field equals the systemuser GUID. Lookup fields are stored as
            // EntityReference; FetchXml `eq` against the logical name matches
            // the underlying GUID.
            var fetchXml = $@"<fetch top='{cap}'>
  <entity name='{OrganizationEntityLogicalName}'>
    <attribute name='{OrganizationIdAttribute}' />
    <filter>
      <condition attribute='{lookupField}' operator='eq' value='{systemUserId:D}' />
    </filter>
  </entity>
</fetch>";

            var fetch = new FetchExpression(fetchXml);
            var results = await _entityService.RetrieveMultipleAsync(fetch, ct).ConfigureAwait(false);

            if (results.Entities.Count == 0)
            {
                _logger.LogDebug(
                    "OrganizationMembershipResolver: user {SystemUserId} mapped to 0 organizations via field {Field}",
                    systemUserId, lookupField);
                return Array.Empty<Guid>();
            }

            var ids = new List<Guid>(results.Entities.Count);
            foreach (var row in results.Entities)
            {
                if (row.Id != Guid.Empty)
                {
                    ids.Add(row.Id);
                }
            }

            if (ids.Count == cap)
            {
                _logger.LogWarning(
                    "OrganizationMembershipResolver: user {SystemUserId} hit MaxOrganizationsPerUser cap {Cap} via field {Field}. " +
                    "Consider raising MembershipOptions.OrganizationLookup.MaxOrganizationsPerUser or investigating data quality.",
                    systemUserId, cap, lookupField);
            }

            _logger.LogDebug(
                "OrganizationMembershipResolver: user {SystemUserId} mapped to {Count} organizations via field {Field}",
                systemUserId, ids.Count, lookupField);

            return ids;
        }
        catch (OperationCanceledException)
        {
            // Surface cancellation to the caller — not a soft failure.
            throw;
        }
        catch (Exception ex)
        {
            // Fail-soft: a query failure (e.g., the configured lookup field
            // does not exist, or the caller lacks read permission on
            // sprk_organization) should NOT cascade-fail the membership
            // pipeline. Return empty + Warning so operators can diagnose.
            _logger.LogWarning(
                ex,
                "OrganizationMembershipResolver: failed to resolve organizations for user {SystemUserId} via field {Field} — returning empty list",
                systemUserId, lookupField);
            return Array.Empty<Guid>();
        }
    }

    /// <summary>
    /// <see cref="IIdentityOrganizationResolver"/> adapter — bridges task 031's
    /// resolver-seam contract to the canonical task-032 contract. The
    /// <paramref name="contactId"/> parameter is intentionally ignored by
    /// Option (b) (configurable Lookup field targets <c>systemuser</c>, not
    /// <c>contact</c>); future Option (a)/(c) implementations may consume it.
    /// </summary>
    Task<IReadOnlyList<Guid>> IIdentityOrganizationResolver.ResolveOrganizationsAsync(
        Guid systemUserId,
        Guid? contactId,
        CancellationToken ct)
    {
        _ = contactId; // see remarks above
        return GetOrganizationIdsAsync(systemUserId, identityContext: null, ct);
    }
}
