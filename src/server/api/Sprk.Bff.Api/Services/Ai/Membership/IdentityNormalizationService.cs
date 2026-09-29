// R3 Part 1 — User-Record Membership Resolution (identity normalization implementation)
// Task 031 (2026-06-21): Resolves a systemuserid into the six identity-type
// components defined by design.md Part 1 § Identity normalization contract.
// Each path is independent (failing one does NOT fail others). Results cached
// in Redis (IDistributedCache) with a 10-minute TTL per ADR-009.
//
// Sub-queries executed in parallel via Task.WhenAll:
//   1. systemuser row     → BusinessUnitId, PrimaryEmail, azureactivedirectoryobjectid
//   2. contact cross-ref  → ContactId (via azureactivedirectoryobjectid match, ADR-028)
//   3. teammembership     → TeamIds[]
//
// Sequential after #1+#2 (depend on contact lookup):
//   4. account             → AccountId (from contact.parentcustomerid if account)
//   5. organizations       → OrganizationIds[] (delegated to IIdentityOrganizationResolver)
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.5, FR-1A.6;
//            projects/spaarke-platform-foundations-r3/design.md Part 1 §
//            Identity normalization contract; ADR-009, ADR-010, ADR-028, ADR-024.

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Resolves a Dataverse <c>systemuserid</c> into a normalized
/// <see cref="PersonIdentity"/> by querying the six identity-type paths in
/// parallel and merging the results. Cached in Redis (<see cref="IDistributedCache"/>)
/// with a 10-minute TTL per ADR-009. Failure on a single identity-type path
/// produces a <c>null</c> / empty value for that field without failing the
/// other paths (per FR-1A.5 contract).
/// </summary>
public sealed class IdentityNormalizationService : IIdentityNormalizationService
{
    /// <summary>
    /// Cache resource label (per ITenantCache contract). The on-wire key becomes
    /// <c>tenant:{tenantId}:membership-identity:{systemUserId:D}:v1</c>
    /// (with the configured <c>InstanceName</c> prepended by StackExchangeRedisCache).
    /// </summary>
    /// <remarks>
    /// Phase 2 invalidation channel (FR-2P2.8) — a future per-user invalidation can
    /// target this resource label without affecting other Redis namespaces.
    /// </remarks>
    internal const string CacheResource = "membership-identity";

    /// <summary>Cache schema version per ADR-009.</summary>
    private const int CacheVersion = 1;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly IDataverseService _dataverse;
    private readonly ITenantCache _cache;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IEnumerable<IIdentityOrganizationResolver> _organizationResolvers;
    private readonly ILogger<IdentityNormalizationService> _logger;

    public IdentityNormalizationService(
        IDataverseService dataverse,
        ITenantCache cache,
        IEnumerable<IIdentityOrganizationResolver> organizationResolvers,
        IOptions<MembershipOptions> options,
        ILogger<IdentityNormalizationService> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(organizationResolvers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _dataverse = dataverse;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _organizationResolvers = organizationResolvers;
        _logger = logger;
        _ = options.Value; // Currently unused at runtime; reserved for future tuning
                           // (BU-descendant policy, additional identity tables, etc.)
                           // Resolving here surfaces binding errors at construction
                           // rather than first call.
    }

    /// <summary>
    /// Resolves the tenant ID for tenant-scoped cache keys (FR-05).
    /// Reads the AAD <c>tid</c> claim from the current HttpContext per ADR-028;
    /// falls back to <c>"anonymous"</c> when no HttpContext is available.
    /// </summary>
    private string GetTenantId()
        => _httpContextAccessor?.HttpContext?.User?.FindFirst("tid")?.Value
            ?? _httpContextAccessor?.HttpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? "anonymous";

    /// <inheritdoc/>
    public async Task<PersonIdentity> ResolveAsync(Guid systemUserId, CancellationToken ct)
    {
        if (systemUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "systemUserId must not be Guid.Empty",
                nameof(systemUserId));
        }

        ct.ThrowIfCancellationRequested();

        // ── Cache lookup ────────────────────────────────────────────────────
        var tenantId = GetTenantId();
        var cacheId = systemUserId.ToString("D");
        var cached = await TryGetFromCacheAsync(tenantId, cacheId, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogDebug(
                "IdentityNormalizationService cache HIT for systemUserId={SystemUserId}",
                systemUserId);
            return cached;
        }

        var sw = Stopwatch.StartNew();
        _logger.LogDebug(
            "IdentityNormalizationService cache MISS for systemUserId={SystemUserId} — resolving",
            systemUserId);

        // ── Parallel-fetch the three independent root paths ────────────────
        // SystemUser row provides: BusinessUnitId, PrimaryEmail, AADObjectId
        // (the AADObjectId then drives the contact cross-ref below).
        // Teams are independent of the systemuser row content.
        var systemUserTask = TryResolveSystemUserAsync(systemUserId, ct);
        var teamsTask = TryResolveTeamsAsync(systemUserId, ct);

        await Task.WhenAll(systemUserTask, teamsTask).ConfigureAwait(false);

        var systemUserData = await systemUserTask.ConfigureAwait(false);
        var teamIds = await teamsTask.ConfigureAwait(false);

        // ── Contact resolution ──────────────────────────────────────────────
        // PRIMARY (Spaarke model): the user's own sprk_primarycontact lookup → contact.
        // This is authoritative and needs no cross-ref field on contact. Enables the
        // Contact-typed membership descriptors (assignedAttorney / assignedParalegal /
        // assignedToInternal|External) to match the acting user.
        // FALLBACK (ADR-028): contact.azureactivedirectoryobjectid == user AAD oid, for
        // environments that provision that field but not sprk_primarycontact.
        Guid? contactId = systemUserData.PrimaryContactId;
        if (contactId is null && systemUserData.AzureAdObjectId is { } aadOid)
        {
            contactId = await TryResolveContactIdAsync(aadOid, ct).ConfigureAwait(false);
        }

        // ── Account via contact.parentcustomerid ───────────────────────────
        Guid? accountId = null;
        if (contactId is { } cid)
        {
            accountId = await TryResolveAccountIdAsync(cid, ct).ConfigureAwait(false);
        }

        // ── Organizations (delegated to task 032's resolver(s)) ────────────
        var organizationIds = await ResolveOrganizationIdsAsync(
            systemUserId,
            contactId,
            ct).ConfigureAwait(false);

        var identity = new PersonIdentity(
            SystemUserId: systemUserId,
            ContactId: contactId,
            PrimaryEmail: systemUserData.PrimaryEmail,
            TeamIds: teamIds,
            BusinessUnitId: systemUserData.BusinessUnitId,
            AccountId: accountId,
            OrganizationIds: organizationIds);

        await TrySetCacheAsync(tenantId, cacheId, identity, ct).ConfigureAwait(false);

        sw.Stop();
        _logger.LogInformation(
            "IdentityNormalizationService resolved systemUserId={SystemUserId} " +
            "in {ElapsedMs}ms (contactId={ContactId}, teams={TeamCount}, " +
            "bu={BusinessUnitId}, account={AccountId}, orgs={OrgCount})",
            systemUserId,
            sw.ElapsedMilliseconds,
            contactId,
            teamIds.Count,
            systemUserData.BusinessUnitId,
            accountId,
            organizationIds.Count);

        return identity;
    }

    // ── Path 1: systemuser row ─────────────────────────────────────────────
    private async Task<SystemUserData> TryResolveSystemUserAsync(
        Guid systemUserId,
        CancellationToken ct)
    {
        try
        {
            var entity = await _dataverse.RetrieveAsync(
                "systemuser",
                systemUserId,
                new[]
                {
                    "systemuserid",
                    "internalemailaddress",
                    "domainname",
                    "businessunitid",
                    "azureactivedirectoryobjectid",
                    // Spaarke model (2026-07-09): the SystemUser→Contact link lives on the USER
                    // record as sprk_primarycontact (lookup → contact). This is the authoritative
                    // source for ContactId, avoiding the contact.azureactivedirectoryobjectid
                    // cross-ref field that isn't provisioned in every environment.
                    "sprk_primarycontact"
                },
                ct).ConfigureAwait(false);

            var email = GetString(entity, "internalemailaddress")
                ?? GetString(entity, "domainname");

            var businessUnitId = GetEntityReferenceId(entity, "businessunitid");
            var aadOid = GetGuidLike(entity, "azureactivedirectoryobjectid");
            var primaryContactId = GetEntityReferenceId(entity, "sprk_primarycontact");

            return new SystemUserData(email, businessUnitId, aadOid, primaryContactId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to resolve systemuser row for " +
                "systemUserId={SystemUserId}; BU/email/AAD-oid will be null",
                systemUserId);
            return SystemUserData.Empty;
        }
    }

    /// <inheritdoc/>
    public async Task<Guid?> TryResolveContactByWorkforceIdentityAsync(
        Guid aadObjectId,
        string? verifiedEmail,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Primary: reuse the existing AAD-oid→contact cross-reference
        //    (contact.azureactivedirectoryobjectid == oid). Same path the systemuser
        //    branch uses; failure-isolated (returns null, never throws).
        if (aadObjectId != Guid.Empty)
        {
            var byOid = await TryResolveContactIdAsync(aadObjectId, ct).ConfigureAwait(false);
            if (byOid is { } cidByOid)
            {
                _logger.LogDebug(
                    "Workforce contact resolution: matched contact {ContactId} by AAD oid",
                    cidByOid);
                return cidByOid;
            }
        }

        // 2. Fallback: verified-email match on contact.emailaddress1, consulted only when the oid
        //    cross-reference found nothing. The decision itself lives in the pure
        //    DecideWorkforceEmailMatch below — see the block comment there for why.
        if (string.IsNullOrWhiteSpace(verifiedEmail))
        {
            return null;
        }

        var matches = await TryResolveContactMatchesByEmailAsync(verifiedEmail, ct).ConfigureAwait(false);
        if (matches is null)
        {
            // The binding state could not be READ. We cannot show the match is not someone else's,
            // so we do not resolve to it (ADR-003 fail-closed).
            _logger.LogWarning(
                "Workforce contact resolution DENIED ({DenyCode}): the contact-by-email query failed, " +
                "so the oid-binding state of any match is unknown",
                DenyContactBindingUnreadable);
            return null;
        }

        var decision = DecideWorkforceEmailMatch(matches, aadObjectId);
        switch (decision)
        {
            case WorkforceEmailMatchDecision.Resolve:
                _logger.LogDebug(
                    "Workforce contact resolution: matched contact {ContactId} by verified email",
                    matches[0].ContactId);
                return matches[0].ContactId;

            case WorkforceEmailMatchDecision.NoMatch:
                return null;

            default:
                _logger.LogWarning(
                    "Workforce contact resolution DENIED ({DenyCode}) for caller oid {CallerOid}: {Decision} " +
                    "({MatchCount} contact(s) carry this email)",
                    DenyCodeFor(decision), aadObjectId, decision, matches.Count);
                return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Workforce contact-by-email decision — extracted by task 013 (spec FR-12, finding A-18)
    // ─────────────────────────────────────────────────────────────────────────
    //
    // WHY THIS IS A SEPARATE, PURE FUNCTION. The decision "does this email match resolve to a
    // principal?" used to be four lines welded to a Dataverse query, so the only way to observe it
    // was to stand up Dataverse — which is why task 001 could not pin A-18 at all. Extracted here it
    // is assertable directly, with no transport and therefore no Mock<HttpMessageHandler>
    // (ADR-038 §7 ban B1).
    //
    // ⚠️ ADR-038 §7 ban B8 — READ THIS RATHER THAN COPYING IT. B8 bans testing internals via *either*
    // reflection *or* [InternalsVisibleTo]. This member is internal and IS exercised through
    // InternalsVisibleTo("Sprk.Bff.Api.Tests"), so it is a B8 DEVIATION, not B8 compliance. It is
    // pending a CLAUDE.md §6.5 decision (task 013 flagged it; the reviewer picks a project-scoped
    // exception or a B8 amendment). The neighbouring comment on ExternalParticipationService's
    // grant-filter builders claims this construct is B8-compliant because there is "no reflection into
    // privates" — that reading is wrong, and this comment exists so it stops propagating from here.
    //
    // Purity is the point, not a style preference. A test that drives this decision through a
    // Dataverse double proves only what the double was told to say. A test that calls this function
    // proves what the code decides.

    /// <summary>Deny code (auth.md <c>{domain}.{area}.{action}.{reason}</c>) for the A-18 hijack:
    /// the email matched a contact already bound to someone else's oid.</summary>
    internal const string DenyContactBoundToDifferentOid = "sdap.access.deny.contact_bound_to_different_oid";

    /// <summary>Deny code for an email carried by more than one contact — we refuse to pick.</summary>
    internal const string DenyContactEmailAmbiguous = "sdap.access.deny.contact_email_ambiguous";

    /// <summary>Deny code for a caller whose own oid is unusable, so no match can be attributed.</summary>
    internal const string DenyUnidentifiableCaller = "sdap.access.deny.unidentifiable_caller";

    /// <summary>Deny code for a contact-by-email query that could not be read at all.</summary>
    internal const string DenyContactBindingUnreadable = "sdap.access.deny.contact_binding_unreadable";

    /// <summary>
    /// Fallback code for a deny outcome with no code of its own. Exists so that adding a
    /// <see cref="WorkforceEmailMatchDecision"/> value and forgetting its code produces an
    /// obviously-unlabelled deny rather than silently inheriting some other deny's identity —
    /// mislabelling a deny in the audit trail is worse than admitting it is unlabelled.
    /// </summary>
    internal const string DenyContactResolutionUnspecified = "sdap.access.deny.contact_resolution_unspecified";

    /// <summary>
    /// NOT a deny — an operational alarm. Emitted when contacts matched by email but none carried an
    /// oid binding value at all, meaning the no-hijack check had nothing to compare and is inert in
    /// this environment. A control that cannot fire should say so rather than look like it passed.
    /// </summary>
    internal const string GuardInertNoBindingColumn = "sdap.access.warn.oid_binding_column_absent";

    /// <summary>
    /// One <c>contact</c> row matched by <c>emailaddress1</c>, carrying its current workforce oid
    /// binding (<c>azureactivedirectoryobjectid</c>).
    /// </summary>
    /// <param name="ContactId">The matched contact.</param>
    /// <param name="BoundAadObjectId">
    /// The oid this contact belongs to, or <c>null</c> when it is genuinely UNBOUND (the attribute is
    /// absent or null — Dataverse omits null attributes from a returned row).
    /// </param>
    /// <param name="BindingUnreadable">
    /// <c>true</c> when the attribute was PRESENT but did not yield a usable oid. This is a third
    /// state, not a flavour of unbound: "nobody owns this contact" and "somebody owns it but we cannot
    /// tell who" have opposite safe answers, and collapsing them into <c>null</c> is the value-level
    /// version of exactly the fail-open this task closed at the query level.
    /// </param>
    internal readonly record struct WorkforceContactEmailMatch(
        Guid ContactId,
        Guid? BoundAadObjectId,
        bool BindingUnreadable = false);

    /// <summary>The outcome of the workforce contact-by-email fallback.</summary>
    internal enum WorkforceEmailMatchDecision
    {
        /// <summary>No contact carries this email — the caller simply is not a contact here.</summary>
        NoMatch,

        /// <summary>Exactly one unambiguous, non-hijacking match — resolve to it.</summary>
        Resolve,

        /// <summary>More than one contact carries this email; deny rather than pick one.</summary>
        DenyAmbiguousEmail,

        /// <summary>The matched contact is already bound to a DIFFERENT oid — the A-18 hijack.</summary>
        DenyBoundToDifferentOid,

        /// <summary>
        /// The contact carries a binding value we could not read as an oid. We cannot show it is the
        /// caller's, so we do not hand it over.
        /// </summary>
        DenyBindingUnreadable,

        /// <summary>The caller's own oid is unusable, so no match can be attributed to them.</summary>
        DenyUnidentifiableCaller
    }

    /// <summary>
    /// Test seam over <see cref="DenyCodeFor"/>. Exists because the deny→code table is the only thing
    /// separating one deny from another in the audit trail, so it needs asserting as a TABLE — every
    /// outcome, unique codes, nothing falling through — rather than one Dataverse scenario at a time.
    /// </summary>
    internal static string DenyCodeForTesting(WorkforceEmailMatchDecision decision)
        => DenyCodeFor(decision);

    private static string DenyCodeFor(WorkforceEmailMatchDecision decision) => decision switch
    {
        WorkforceEmailMatchDecision.DenyBoundToDifferentOid => DenyContactBoundToDifferentOid,
        WorkforceEmailMatchDecision.DenyAmbiguousEmail => DenyContactEmailAmbiguous,
        WorkforceEmailMatchDecision.DenyUnidentifiableCaller => DenyUnidentifiableCaller,
        WorkforceEmailMatchDecision.DenyBindingUnreadable => DenyContactBindingUnreadable,
        // NoMatch / Resolve have their own arms at the call site and never reach here.
        _ => DenyContactResolutionUnspecified
    };

    /// <summary>
    /// Reads a contact's workforce oid binding as a THREE-state value: bound to an oid, genuinely
    /// unbound, or present-but-unreadable.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <see cref="GetGuidLike"/>. That helper answers "give me a Guid if you can",
    /// which is right for the identity fields it serves (a missing business unit is simply missing) and
    /// wrong here: it maps an attribute that is present but not a usable oid to the same <c>null</c> as
    /// an absent one, so a contact carrying a malformed or zero binding would read as UNBOUND and be
    /// handed to whoever's email matched. That is the same fail-open as A-18, one layer down, and it is
    /// the reason this is a separate reader rather than a reuse.
    /// <para>
    /// <c>Guid.Empty</c> counts as unreadable, not unbound. Dataverse stores an unset uniqueidentifier
    /// as NULL and omits it from the returned row, so an explicit all-zero oid is anomalous data rather
    /// than an ordinary "not yet bound" — and the safe reading of anomalous identity data is to refuse.
    /// </para>
    /// </remarks>
    internal static (Guid? Bound, bool Unreadable) ReadOidBinding(Entity row, string attribute)
    {
        // Absent attribute == unbound. Dataverse omits null attributes from a returned Entity.
        if (!row.Contains(attribute))
        {
            return (null, false);
        }

        return row[attribute] switch
        {
            null => (null, false),
            Guid g when g != Guid.Empty => (g, false),
            string s when Guid.TryParse(s, out var parsed) && parsed != Guid.Empty => (parsed, false),
            // Present, but not something we can call an oid: a malformed string, an all-zero Guid, an
            // unexpected type. Somebody may own this contact; we cannot tell who.
            _ => (null, true)
        };
    }

    /// <summary>
    /// Decides whether a set of contacts matched by verified email may be resolved to, given the
    /// caller's own AAD object id.
    /// </summary>
    /// <remarks>
    /// <para><b>The rule (spec FR-12, closing finding A-18).</b> An email match may be resolved to
    /// only when it is unambiguous and the contact is not already somebody else's. This is the same
    /// rule the CIAM plane has enforced since ADR-028 Amendment A1
    /// (<c>ExternalParticipationService.ResolveExternalContactAsync</c> — "no email hijack of a bound
    /// Contact"); the workforce plane simply never had it. Each plane checks its OWN binding column:
    /// CIAM compares <c>sprk_externalobjectid</c> against the CIAM <c>oid</c>, this compares
    /// <c>azureactivedirectoryobjectid</c> against the workforce <c>oid</c>.</para>
    ///
    /// <para><b>Why an UNBOUND contact still resolves.</b> That is the legitimate Type-2 onboarding
    /// path — a customer employee with no <c>azureactivedirectoryobjectid</c> yet. Denying it would
    /// break the feature this guard exists to protect, and the escalation trigger on this task asked
    /// precisely this question. The distinction that carries the security property is
    /// <i>unbound</i> (nobody's yet) versus <i>bound to a different oid</i> (already someone's) —
    /// not "has a binding at all".</para>
    ///
    /// <para><b>Why comparison is on parsed <see cref="Guid"/>s.</b> An oid compared as a string
    /// carries a case and formatting assumption that no test written against a self-authored double
    /// can falsify. Comparing parsed Guids removes the assumption instead of testing it.</para>
    ///
    /// <para><b>Nothing here writes a binding.</b> A denied match must not confirm or create one, and
    /// neither must a resolved one on this path — only an oid-verified resolution may bind, and this
    /// fallback is by definition not oid-verified.</para>
    /// </remarks>
    internal static WorkforceEmailMatchDecision DecideWorkforceEmailMatch(
        IReadOnlyList<WorkforceContactEmailMatch> matches,
        Guid callerOid)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (matches.Count == 0)
        {
            return WorkforceEmailMatchDecision.NoMatch;
        }

        // Several contacts carry this email. Picking one is a coin-flip over whose grants the caller
        // inherits, so refuse (ADR-003 fail-closed). This branch is reachable only because the query
        // reads two rows; under TopCount = 1 the second contact simply never arrives.
        if (matches.Count > 1)
        {
            return WorkforceEmailMatchDecision.DenyAmbiguousEmail;
        }

        var match = matches[0];
        if (match.ContactId == Guid.Empty)
        {
            return WorkforceEmailMatchDecision.NoMatch;
        }

        // A caller we cannot name cannot be shown to own anything. Today's only caller
        // (WorkforcePrincipalResolver) denies a missing oid before reaching here, so this guards the
        // public interface rather than a live path — which is the point: the next caller gets the
        // rule for free instead of re-deriving it.
        if (callerOid == Guid.Empty)
        {
            return WorkforceEmailMatchDecision.DenyUnidentifiableCaller;
        }

        // The binding attribute was there but unreadable. "Nobody owns this contact" and "somebody owns
        // it but we cannot tell who" have opposite safe answers, so they get separate outcomes; reading
        // the second as the first is how a bound contact gets handed over.
        if (match.BindingUnreadable)
        {
            return WorkforceEmailMatchDecision.DenyBindingUnreadable;
        }

        // The finding itself: the contact already belongs to a different person.
        if (match.BoundAadObjectId is { } bound && bound != callerOid)
        {
            return WorkforceEmailMatchDecision.DenyBoundToDifferentOid;
        }

        return WorkforceEmailMatchDecision.Resolve;
    }

    // ── Path 2: contact cross-ref via azureactivedirectoryobjectid ─────────
    // Per ADR-028 — the single source of truth for SystemUser↔Contact mapping.
    private async Task<Guid?> TryResolveContactIdAsync(
        Guid aadObjectId,
        CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid"),
                TopCount = 1,
                NoLock = true
            };
            query.Criteria.AddCondition(
                "azureactivedirectoryobjectid",
                ConditionOperator.Equal,
                aadObjectId);

            var results = await _dataverse
                .RetrieveMultipleAsync(query, ct)
                .ConfigureAwait(false);

            if (results.Entities.Count == 0)
            {
                return null;
            }

            var contactId = results.Entities[0].Id;
            return contactId == Guid.Empty ? null : contactId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to cross-reference contact for " +
                "azureActiveDirectoryObjectId={AadObjectId}; ContactId will be null",
                aadObjectId);
            return null;
        }
    }

    // ── Contact-only fallback: contact by verified email (workforce plane) ──
    // teams-app-r1 FR-04: only consulted when the AAD-oid cross-ref returns no contact.
    //
    // Reads the BINDING COLUMN and TWO rows, because the decision above cannot be made without
    // either. TopCount = 2 is not a magic number: it is the cheapest query that can tell "exactly
    // one" from "more than one", and TopCount = 1 is what made an ambiguous email indistinguishable
    // from an unambiguous one — the row you get back is simply whichever Dataverse returned first.
    //
    // Returns null when the query could not be READ at all, and an empty list when it read fine and
    // nothing matched. Collapsing those two (as the previous signature did, returning Guid? for
    // both) is exactly how an unreadable binding state comes to look like a clean miss.
    private async Task<IReadOnlyList<WorkforceContactEmailMatch>?> TryResolveContactMatchesByEmailAsync(
        string email,
        CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid", "azureactivedirectoryobjectid"),
                TopCount = 2,
                NoLock = true
            };
            query.Criteria.AddCondition(
                "emailaddress1",
                ConditionOperator.Equal,
                email);

            var results = await _dataverse
                .RetrieveMultipleAsync(query, ct)
                .ConfigureAwait(false);

            var matches = new List<WorkforceContactEmailMatch>(results.Entities.Count);
            var anyRowCarriedTheBindingColumn = false;
            foreach (var row in results.Entities)
            {
                anyRowCarriedTheBindingColumn |= row.Contains("azureactivedirectoryobjectid");
                var (bound, unreadable) = ReadOidBinding(row, "azureactivedirectoryobjectid");
                matches.Add(new WorkforceContactEmailMatch(row.Id, bound, unreadable));
            }

            // ⚠️ The guard's inert-mode alarm. This service states ~390 lines above that
            // contact.azureactivedirectoryobjectid "isn't provisioned in every environment" — and where
            // it is missing or universally empty, EVERY row reads UNBOUND and the no-hijack check
            // passes everything while reading, in code and in tests, exactly like a control that fired.
            // That is pre-fix behaviour restored by CONFIGURATION rather than by a code change, so no
            // test can catch it and only an operator can. Hence a log line: an inert security control
            // must be visible, not assumed.
            if (matches.Count > 0 && !anyRowCarriedTheBindingColumn)
            {
                _logger.LogWarning(
                    "[WF-AUTH] {DenyCode}: matched {MatchCount} contact(s) by verified email but NOT ONE " +
                    "carried an azureactivedirectoryobjectid value, so the no-hijack oid check had " +
                    "nothing to compare and cannot discriminate. If this recurs, verify the column is " +
                    "provisioned and populated in this environment — otherwise FR-12 is inert here.",
                    GuardInertNoBindingColumn, matches.Count);
            }

            return matches;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Guarded: HttpClient reports a TIMEOUT as TaskCanceledException, an
            // OperationCanceledException. An unguarded arm would rethrow a Dataverse timeout instead of
            // failing closed through the arm below. Only real cancellation propagates.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to resolve contact by verified email; " +
                "the caller will NOT be resolved to a contact (fail closed)");
            return null;
        }
    }

    // ── Path 3: teammembership → teamIds[] ─────────────────────────────────
    private async Task<IReadOnlyList<Guid>> TryResolveTeamsAsync(
        Guid systemUserId,
        CancellationToken ct)
    {
        try
        {
            // teammembership is the intersect entity. Filter by systemuserid,
            // project teamid only — no payload bloat.
            var query = new QueryExpression("teammembership")
            {
                ColumnSet = new ColumnSet("teamid"),
                NoLock = true
            };
            query.Criteria.AddCondition(
                "systemuserid",
                ConditionOperator.Equal,
                systemUserId);

            var results = await _dataverse
                .RetrieveMultipleAsync(query, ct)
                .ConfigureAwait(false);

            if (results.Entities.Count == 0)
            {
                return Array.Empty<Guid>();
            }

            var ids = new HashSet<Guid>();
            foreach (var row in results.Entities)
            {
                if (row.Contains("teamid") && row["teamid"] is Guid g && g != Guid.Empty)
                {
                    ids.Add(g);
                }
            }

            return ids.Count == 0 ? Array.Empty<Guid>() : ids.ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to resolve teammembership for " +
                "systemUserId={SystemUserId}; TeamIds will be empty",
                systemUserId);
            return Array.Empty<Guid>();
        }
    }

    // ── Path 4: contact → parentcustomerid → accountid (only if Account) ───
    private async Task<Guid?> TryResolveAccountIdAsync(
        Guid contactId,
        CancellationToken ct)
    {
        try
        {
            var entity = await _dataverse.RetrieveAsync(
                "contact",
                contactId,
                new[] { "contactid", "parentcustomerid" },
                ct).ConfigureAwait(false);

            if (!entity.Contains("parentcustomerid") ||
                entity["parentcustomerid"] is not EntityReference parentRef)
            {
                return null;
            }

            // parentcustomerid is polymorphic (contact OR account). We only
            // care about Account; ignore Contact-typed parents per design.
            return string.Equals(parentRef.LogicalName, "account", StringComparison.OrdinalIgnoreCase)
                ? parentRef.Id == Guid.Empty ? null : parentRef.Id
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to resolve parentcustomerid → account for " +
                "contactId={ContactId}; AccountId will be null",
                contactId);
            return null;
        }
    }

    // ── Path 5: organizations via task 032's resolver(s) ───────────────────
    private async Task<IReadOnlyList<Guid>> ResolveOrganizationIdsAsync(
        Guid systemUserId,
        Guid? contactId,
        CancellationToken ct)
    {
        var resolvers = _organizationResolvers.ToList();
        if (resolvers.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var merged = new HashSet<Guid>();
        foreach (var resolver in resolvers)
        {
            try
            {
                var ids = await resolver
                    .ResolveOrganizationsAsync(systemUserId, contactId, ct)
                    .ConfigureAwait(false);

                if (ids is null)
                {
                    continue;
                }

                foreach (var id in ids)
                {
                    if (id != Guid.Empty)
                    {
                        merged.Add(id);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "IdentityNormalizationService: organization resolver {ResolverType} " +
                    "threw for systemUserId={SystemUserId}; skipping this resolver, " +
                    "other resolvers' results still merged",
                    resolver.GetType().FullName,
                    systemUserId);
            }
        }

        return merged.Count == 0 ? Array.Empty<Guid>() : merged.ToArray();
    }

    // ── Cache helpers ──────────────────────────────────────────────────────
    private async Task<PersonIdentity?> TryGetFromCacheAsync(
        string tenantId,
        string cacheId,
        CancellationToken ct)
    {
        try
        {
            return await _cache.GetAsync<PersonIdentity>(
                tenantId,
                CacheResource,
                cacheId,
                CacheVersion,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cache failure must NOT break resolution — fall through to re-resolve.
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to read cache for tenant={TenantId} id={CacheId}; " +
                "falling through to live resolve",
                tenantId, cacheId);
            return null;
        }
    }

    private async Task TrySetCacheAsync(
        string tenantId,
        string cacheId,
        PersonIdentity identity,
        CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(
                tenantId,
                CacheResource,
                cacheId,
                CacheVersion,
                identity,
                CacheTtl,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to write cache for tenant={TenantId} id={CacheId}; " +
                "next call will re-resolve (no functional impact)",
                tenantId, cacheId);
        }
    }

    // ── Entity helpers ─────────────────────────────────────────────────────
    private static string? GetString(Entity entity, string attribute)
        => entity.Contains(attribute) && entity[attribute] is string s && !string.IsNullOrWhiteSpace(s)
            ? s
            : null;

    private static Guid? GetEntityReferenceId(Entity entity, string attribute)
    {
        if (!entity.Contains(attribute) || entity[attribute] is not EntityReference er)
        {
            return null;
        }
        return er.Id == Guid.Empty ? null : er.Id;
    }

    /// <summary>
    /// Reads a value that may be stored as <see cref="Guid"/> or as a string
    /// containing a Guid representation. Dataverse <c>azureactivedirectoryobjectid</c>
    /// returns as a Guid via the SDK but as a string via the Web API — accept both.
    /// </summary>
    private static Guid? GetGuidLike(Entity entity, string attribute)
    {
        if (!entity.Contains(attribute))
        {
            return null;
        }

        var value = entity[attribute];
        return value switch
        {
            Guid g when g != Guid.Empty => g,
            string s when Guid.TryParse(s, out var parsed) && parsed != Guid.Empty => parsed,
            _ => null
        };
    }

    private readonly record struct SystemUserData(
        string? PrimaryEmail,
        Guid? BusinessUnitId,
        Guid? AzureAdObjectId,
        Guid? PrimaryContactId)
    {
        public static SystemUserData Empty { get; } = new(null, null, null, null);
    }
}
