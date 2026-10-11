using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>
/// Derives a thread's authorized participant set (FR-07). Reads the thread's topology + privacy + ADR-024
/// anchor from <c>sprk_communicationthread</c>, then composes:
/// <c>open = reverse-ADR-034(anchor)</c>, <c>direct = explicit participants</c>,
/// <c>private = open ∪ overlay grants</c> (design §5, spike 002 option B).
/// </summary>
/// <remarks>
/// <para><b>ADR-034 reuse (not a second engine).</b> <c>MembershipResolverService</c> answers the
/// user→records direction; open-thread membership needs the record→users inverse. Rather than reimplement
/// association discovery, this service reuses the canonical <see cref="IMembershipFieldDiscoveryService"/>
/// (the same metadata-driven discovery ADR-034 mandates) to find the anchor entity's membership-bearing
/// lookup fields, reads the anchor record's values for exactly those fields, and collects the referenced
/// identities. This is the inverse query over the SAME discovered fields — not the ad-hoc FetchXML pattern
/// ADR-034 exists to prevent.</para>
/// <para><b>Identity-type coverage (R1).</b> Direct <c>systemuser</c> + <c>contact</c> lookups are collected
/// directly; <c>team</c> lookups are expanded to their members via <c>teammembership</c>. Coarse identity
/// types (<c>businessunit</c> / <c>account</c> / <c>sprk_organization</c>) are NOT expanded in R1 — expanding
/// them could balloon ACS membership far beyond the intended thread audience. Skipping them is SAFE for the
/// projection invariant (it can only UNDER-include, never over-include); a user with only coarse-typed access
/// simply isn't added to ACS (functional under-exposure repaired if R2 adds expansion), and the invariant
/// "ACS ⊆ Dataverse-derived" is never violated.</para>
/// </remarks>
public sealed class ThreadMembershipDerivationService : IThreadMembershipDerivationService
{
    private const string ThreadEntity = "sprk_communicationthread";

    private readonly IGenericEntityService _entityService;
    private readonly IMembershipFieldDiscoveryService _discovery;
    private readonly IThreadExplicitParticipantReader _explicitReader;
    private readonly ILogger<ThreadMembershipDerivationService> _logger;

    public ThreadMembershipDerivationService(
        IGenericEntityService entityService,
        IMembershipFieldDiscoveryService discovery,
        IThreadExplicitParticipantReader explicitReader,
        ILogger<ThreadMembershipDerivationService> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _explicitReader = explicitReader ?? throw new ArgumentNullException(nameof(explicitReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ThreadAuthorizedSet> DeriveAuthorizedSetAsync(Guid threadId, CancellationToken ct = default)
    {
        var context = await ReadThreadContextAsync(threadId, ct);
        if (context is null)
        {
            _logger.LogWarning("Thread {ThreadId} not found; authorized set is empty.", threadId);
            return ThreadAuthorizedSet.Empty(threadId);
        }

        // First-reason-wins de-dup: a participant authorized by BOTH record membership and an overlay grant
        // is recorded once (record membership takes provenance precedence since it is evaluated first).
        var authorized = new Dictionary<(string, Guid), AuthorizedParticipant>();

        // ── Open (record-anchored): reverse ADR-034 over the anchor record ──
        if (context.Topology == ThreadTopology.RecordAnchored && context.HasAnchor)
        {
            foreach (var participant in await ReverseResolveAnchorMembersAsync(context.AnchorRecordType!, context.AnchorRecordId!, ct))
            {
                Add(authorized, participant, AuthorizationReason.RecordMembership);
            }
        }

        // ── Explicit participants: Direct two-party list + Private overlay grants (spike 002 option B) ──
        // The reader returns the Direct list for a Direct thread and the overlay grants for a Private thread.
        // For an Open record-anchored thread the reader returns nothing (Null-Object default until 042/043).
        var explicitParticipants = await _explicitReader.GetExplicitParticipantsAsync(threadId, ct);
        foreach (var ep in explicitParticipants)
        {
            var reason = ep.Kind == ThreadExplicitParticipantKind.PrivateOverlayGrant
                ? AuthorizationReason.PrivateOverlayGrant
                : AuthorizationReason.DirectParticipant;
            Add(authorized, ep.Participant, reason);
        }

        _logger.LogInformation(
            "Derived authorized set for thread {ThreadId}: {Count} participant(s) (topology={Topology}, privacy={Privacy}).",
            threadId, authorized.Count, context.Topology, context.Privacy);

        return new ThreadAuthorizedSet
        {
            ThreadId = threadId,
            Participants = authorized.Values.ToList(),
        };
    }

    private static void Add(
        IDictionary<(string, Guid), AuthorizedParticipant> set,
        ParticipantReference participant,
        AuthorizationReason reason)
    {
        var key = (participant.EntityLogicalName, participant.RecordId);
        if (!set.ContainsKey(key))
        {
            set[key] = new AuthorizedParticipant { Participant = participant, Reason = reason };
        }
    }

    private async Task<ThreadMembershipContext?> ReadThreadContextAsync(Guid threadId, CancellationToken ct)
    {
        // NOTE: a transient read failure PROPAGATES (not swallowed) so the reconcile job retries rather than
        // computing an empty desired set — an empty set would make the reconcile mass-remove every ACS
        // participant (membership thrashing). Only a genuinely not-found thread returns null (→ empty set).
        // NOTE: sprk_communicationthread has NO 'sprk_regardingrecordtype' text attribute — requesting it in a
        // ColumnSet raised the create-time FaultException that surfaced as a 500 on POST /api/communications/threads
        // (RB R3 UAT 2026-07-27, membership-derivation path). Nor does it have 'sprk_regardingreportcard', so the
        // typed lookups read are the thread's own (ThreadResolver.ThreadRegardingColumns): selecting a column the table
        // lacks faults the whole read (#1629). sprk_regardingrecordid is read only to log a pair no lookup repeats.
        var columns = new List<string> { "sprk_threadtype", "sprk_privacystate", "sprk_regardingrecordid" };
        columns.AddRange(ThreadResolver.ThreadRegardingColumns);

        var thread = await _entityService.RetrieveAsync(ThreadEntity, threadId, columns.ToArray(), ct);

        if (thread is null || thread.Id == Guid.Empty)
            return null;

        var topology = thread.GetAttributeValue<OptionSetValue>("sprk_threadtype")?.Value == (int)ThreadTopology.Direct
            ? ThreadTopology.Direct
            : ThreadTopology.RecordAnchored;

        var privacy = thread.GetAttributeValue<OptionSetValue>("sprk_privacystate")?.Value == (int)ThreadPrivacyState.Private
            ? ThreadPrivacyState.Private
            : ThreadPrivacyState.Open;

        // #1629: the anchor is the record the thread's TYPED regarding lookup names (the one thread-anchor rule,
        // ThreadResolver.ThreadAnchorOf), never the plain-text sprk_regardingrecordid. Spaarke Basic/Core User hold Write on
        // their own threads, so the pair is whatever the owner typed through the Web API: it was preferred here, and a
        // user who pointed it at a secure matter while the lookup named their own matter had the secure matter's members
        // derived into their thread (ACS participants, message read grants, notifications). Dataverse checks AppendTo on
        // the lookup, so it is the anchor; the pair counts only when a typed lookup repeats it (#1628's rule), and a pair
        // with no typed lookup anchors nothing: no member is derived from it (fail closed).
        var anchor = ThreadResolver.ThreadAnchorOf(thread);
        if (anchor.Pair == ThreadResolver.ThreadPairAgreement.NotRepeated)
        {
            if (anchor.HasAnchor)
            {
                _logger.LogWarning(
                    "Thread {ThreadId}: its regarding pair '{PairId}' is not repeated by a typed regarding lookup; membership is " +
                    "derived from the typed lookup's record {AnchorType}:{AnchorId}, never from the pair (#1629).",
                    threadId, anchor.PairText, anchor.RecordType, anchor.RecordId);
            }
            else
            {
                _logger.LogWarning(
                    "Thread {ThreadId}: its regarding pair '{PairId}' is not repeated by a typed regarding lookup and nothing " +
                    "else anchors it; no record membership is derived (fail closed, #1629).",
                    threadId, anchor.PairText);
            }
        }

        return new ThreadMembershipContext
        {
            ThreadId = threadId,
            Topology = topology,
            Privacy = privacy,
            AnchorRecordId = anchor.RecordId?.ToString(),
            AnchorRecordType = anchor.RecordType,
        };
    }

    /// <summary>
    /// Reverse ADR-034: the authorized users of the anchor RECORD. Reuses <see cref="IMembershipFieldDiscoveryService"/>
    /// to discover the membership-bearing lookup fields for the anchor entity, reads the anchor record's values
    /// for those fields, and collects systemuser/contact refs (teams expanded to members).
    /// </summary>
    private async Task<IReadOnlyList<ParticipantReference>> ReverseResolveAnchorMembersAsync(
        string anchorEntityType,
        string anchorRecordId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(anchorRecordId, out var anchorId) || anchorId == Guid.Empty)
        {
            _logger.LogWarning("Anchor record id '{AnchorId}' on entity '{Entity}' is not a valid GUID; no membership derived.",
                anchorRecordId, anchorEntityType);
            return Array.Empty<ParticipantReference>();
        }

        var discovery = await _discovery.DiscoverAsync(anchorEntityType, ct);
        if (discovery.DiscoveredFields.Count == 0)
        {
            _logger.LogInformation("No membership-bearing lookup fields discovered on '{Entity}'; no open membership.", anchorEntityType);
            return Array.Empty<ParticipantReference>();
        }

        var fields = discovery.DiscoveredFields.Select(d => d.Field).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // A transient anchor-read failure PROPAGATES (not swallowed) so the job retries instead of computing
        // an under-inclusive set (which would spuriously remove authorized ACS participants).
        var anchor = await _entityService.RetrieveAsync(anchorEntityType, anchorId, fields, ct);

        if (anchor is null || anchor.Id == Guid.Empty)
            return Array.Empty<ParticipantReference>();

        var members = new Dictionary<(string, Guid), ParticipantReference>();

        foreach (var field in fields)
        {
            var reference = anchor.GetAttributeValue<EntityReference>(field);
            if (reference is null || reference.Id == Guid.Empty)
                continue;

            switch (reference.LogicalName)
            {
                case "systemuser":
                    AddMember(members, ParticipantReference.SystemUser(reference.Id));
                    break;

                case "contact":
                    AddMember(members, ParticipantReference.Contact(reference.Id));
                    break;

                case "team":
                    foreach (var userId in await ExpandTeamMembersAsync(reference.Id, ct))
                        AddMember(members, ParticipantReference.SystemUser(userId));
                    break;

                default:
                    // businessunit / account / sprk_organization — coarse identity types NOT expanded in R1
                    // (see class remarks: safe under-inclusion; never over-includes). Deferred to R2.
                    _logger.LogDebug(
                        "Skipping coarse identity target '{Target}' on field '{Field}' of '{Entity}' (R1 does not expand it).",
                        reference.LogicalName, field, anchorEntityType);
                    break;
            }
        }

        return members.Values.ToList();
    }

    private static void AddMember(IDictionary<(string, Guid), ParticipantReference> set, ParticipantReference p)
    {
        var key = (p.EntityLogicalName, p.RecordId);
        if (!set.ContainsKey(key))
            set[key] = p;
    }

    /// <summary>
    /// Expands a team lookup to its member systemusers via the <c>teammembership</c> intersect. A transient
    /// query failure PROPAGATES so the job retries — omitting team members would spuriously remove them from
    /// the ACS thread on this pass and re-add them on the next (membership thrashing).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ExpandTeamMembersAsync(Guid teamId, CancellationToken ct)
    {
        var query = new QueryExpression("teammembership")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("teamid", ConditionOperator.Equal, teamId) },
            },
        };

        var result = await _entityService.RetrieveMultipleAsync(query, ct);
        return result.Entities
            .Select(e => e.GetAttributeValue<Guid>("systemuserid"))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
    }
}
