using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The record-wide share expiry — spec FR-33, task 098: the Manage Access toolbar's ONE Expiration date,
/// written to every active share of a record in one all-or-nothing transaction.
///
/// <para><b>What these tests protect.</b> Which rows change (every active share of THIS record — contact and
/// organization shares, lapsed ones included per the owner's 2026-09-11 decision — and nothing else), that
/// they change in exactly ONE <see cref="IGenericEntityService.BulkUpdateAsync"/> (the atomic write task 096
/// built; a per-row loop is the fail-open path this endpoint exists to replace), the exact value handed to
/// the SDK, whose participation cache is cleared afterwards, and that every refusal writes nothing.</para>
///
/// <para><b>Why the fake is STRICT about the filter.</b> <see cref="FakeShareTable"/> applies each clause of
/// the production <c>$filter</c> the way Dataverse would, and THROWS on a clause it does not understand. So a
/// filter that lost its root clause hands back every record's shares, one that lost <c>statecode eq 0</c>
/// hands back revoked ones, and one that GAINED a narrowing clause (say an expiry predicate copied from the read
/// path, which would silently stop renewing lapsed shares) fails loudly instead of passing against a fake that
/// ignored it.</para>
///
/// <para>Seams: <see cref="DataverseWebApiClient"/> (virtual) for the reads, a STRICT
/// <see cref="IDataverseService"/> mock for the write — any call other than the one bulk update throws — and
/// <see cref="ITenantCache"/>. No <c>Mock&lt;HttpMessageHandler&gt;</c> (ban B1), no reflection (ban B8).</para>
/// </summary>
public class RecordShareExpiryTests
{
    private const string GrantEntitySet = "sprk_externalrecordaccesses";
    private const string MembershipEntitySet = "sprk_contactorganizations";
    private const string TenantId = "00000000-0000-0000-0000-0000000000cc";

    private static readonly Guid MatterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherMatterId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid UnrelatedContactId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid MemberA = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid MemberB = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid FormerMember = Guid.Parse("88888888-8888-8888-8888-888888888888");

    /// <summary>Task 140: the internal caller's Entra object id, its systemuser, and another systemuser (an earlier issuer).</summary>
    private static readonly Guid CallerOid = Guid.Parse("0e140000-0000-0000-0000-0000000000a1");
    private static readonly Guid CallerSystemUserId = Guid.Parse("5a140000-0000-0000-0000-0000000000a1");
    private static readonly Guid OtherSystemUserId = Guid.Parse("5a140000-0000-0000-0000-0000000000b2");

    /// <summary>A FIXED clock: "today" is the UTC date 2026-09-10 in every test, whatever day they run.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 10);
    private static readonly DateOnly NewExpiry = new(2026, 12, 10);

    /// <summary>
    /// Every column Dataverse exposes on <c>sprk_externalrecordaccess</c> (live metadata, task 016). A
    /// <c>$select</c> naming anything else is a 400 — the fake reproduces that rather than tolerating it.
    /// </summary>
    /// <remarks>
    /// Task 140 added <c>_sprk_grantedbycontact_value</c> (the contact-typed grant issuer, created by
    /// <c>scripts/Deploy-ExternalRecordAccessContactGrantor.ps1</c>). This list going red when it was first selected is
    /// exactly the deploy-order hazard that script documents: the column must exist before a BFF that reads it. Session 27
    /// round 50 item 2 added <c>sprk_grantedbycontactid</c> (the issuer's id as text, created by the same script) under the
    /// same gate.
    /// </remarks>
    private static readonly HashSet<string> LiveColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_externalrecordaccessid", "sprk_name", "sprk_accesslevel", "sprk_expiresdate",
        "sprk_granteddate", "statecode", "statuscode",
        "_sprk_contact_value", "_sprk_organization_value", "_sprk_project_value",
        "_sprk_matter_value", "_sprk_workassignment_value", "_sprk_invoice_value",
        "_sprk_grantedby_value", "_sprk_grantedbycontact_value", "sprk_grantedbycontactid", "_sprk_recordtype_value",
        "createdon", "modifiedon", "ownerid"
    };

    private readonly FakeShareTable _table = new();
    private readonly Mock<DataverseWebApiClient> _client;
    private readonly Mock<IDataverseService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<ITenantCache> _cache = new();

    private readonly List<(string Entity, List<(Guid id, Dictionary<string, object> fields)> Updates)> _bulkUpdates = new();
    // Concurrent: since task 137 the invalidation routine removes keys with bounded parallelism.
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Tenant, string Resource, string Id, int Version)> _invalidated = new();

    public RecordShareExpiryTests()
    {
        _client = _table.BuildMock();

        _dataverse
            .Setup(d => d.BulkUpdateAsync(
                It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<(Guid id, Dictionary<string, object> fields)>, CancellationToken>(
                (entity, updates, _) => _bulkUpdates.Add((entity, updates)))
            .Returns(Task.CompletedTask);

        _cache
            .Setup(c => c.RemoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, string, CancellationToken>(
                (tenant, resource, id, version, _, _) => _invalidated.Enqueue((tenant, resource, id, version)))
            .Returns(Task.CompletedTask);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Which rows change — and that they change in ONE transaction
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The core contract: a person share, a person share that also records the person's firm, and the firm's
    /// own organization share all take the new date — in exactly one bulk update, writing the expiry and
    /// nothing else. The three start at a nearer date, a LATER date (so this is a shortening for one of them)
    /// and no date at all (the unbounded shape #974 still allows outside the BFF).
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_OnARecordWithContactAndOrganizationShares_WritesEveryActiveShareInOneBulkUpdate()
    {
        var person = _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId, expires: Today.AddDays(30));
        var personWithFirm = _table.SeedContactShare(
            OtherContactId, ExternalGrantRootType.Matter, MatterId, expires: Today.AddDays(200), organizationId: OrganizationId);
        var firm = _table.SeedOrganizationShare(OrganizationId, ExternalGrantRootType.Matter, MatterId, expires: null);

        var result = await Send(NewExpiry);

        OkBody(result).Should().Be(new SetRecordShareExpiryResponse(UpdatedCount: 3, ExpiresDate: NewExpiry));

        var call = _bulkUpdates.Should().ContainSingle(
            "every share must move in ONE all-or-nothing transaction — N writes is the partial-failure path " +
            "that leaves a shortened share at its later date").Subject;
        call.Entity.Should().Be("sprk_externalrecordaccess");
        call.Updates.Select(u => u.id).Should().BeEquivalentTo(new[] { person.Id, personWithFirm.Id, firm.Id });
        call.Updates.Should().OnlyContain(u => u.fields.Count == 1 && u.fields.ContainsKey("sprk_expiresdate"),
            "the change writes the expiry and nothing else — level, grantee and state stay as they were");
    }

    /// <summary>
    /// Owner decision 2026-09-11 ("Renew them too"): a share whose date has already passed is still an ACTIVE
    /// row, and the record's Expiration applies to it like any other — so it starts working again.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_OnAShareThatHasAlreadyLapsed_RenewsItToTheNewDate()
    {
        var lapsed = _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId, expires: Today.AddDays(-20));

        var result = await Send(NewExpiry);

        OkBody(result).UpdatedCount.Should().Be(1);
        SingleBulkUpdate().Updates.Should().ContainSingle(u => u.id == lapsed.Id,
            "the Expiration applies to all sharing on the record, lapsed shares included (owner, 2026-09-11)");
    }

    /// <summary>Negative: a REVOKED share on the same record is not an active share and is not touched.</summary>
    [Fact]
    public async Task SetShareExpiry_LeavesRevokedSharesOnTheRecordUntouched()
    {
        var active = _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);
        var revoked = _table.SeedContactShare(OtherContactId, ExternalGrantRootType.Matter, MatterId, stateCode: 1);

        await Send(NewExpiry);

        SingleBulkUpdate().Updates.Select(u => u.id).Should().Equal(new[] { active.Id },
            "writing a date onto revoked share {0} would be meaningless at best — and the revoke path must stay the only thing that decides a revoked row",
            revoked.Id);
    }

    /// <summary>
    /// Negative: shares on ANY other record are untouched — another matter, and a project that happens to carry
    /// the SAME id as the matter. The second pins that the row selection keys on the right root COLUMN, not
    /// merely on the id.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_LeavesSharesOnOtherRecordsUntouched()
    {
        var mine = _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);
        _table.SeedContactShare(OtherContactId, ExternalGrantRootType.Matter, OtherMatterId);
        _table.SeedContactShare(UnrelatedContactId, ExternalGrantRootType.Project, MatterId);

        await Send(NewExpiry);

        SingleBulkUpdate().Updates.Select(u => u.id).Should().Equal(new[] { mine.Id },
            "Write on one record must never change the lifetime of another record's shares");
    }

    /// <summary>
    /// Negative, and the sharper form of the one above: a share row that is ALSO linked to another record (two
    /// root lookups — never written by the BFF, possible through an import or a form edit) confers access on
    /// that other record too. Changing it here would change access to a record the caller's Write was never
    /// checked on, so the whole request is refused and nothing is written.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_WhenAShareIsAlsoLinkedToAnotherRecord_Refuses409AndWritesNothing()
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);
        _table.SeedContactShare(OtherContactId, ExternalGrantRootType.Matter, MatterId).ProjectId = ProjectId;

        var result = await Send(NewExpiry);

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(SetRecordShareExpiryEndpoint.ShareSpansRecordsReasonCode);
        _bulkUpdates.Should().BeEmpty();
    }

    /// <summary>
    /// Negative: standing-grant access is unaffected. Standing access is a flag on the contact / organization
    /// and has no share row (task 042), so the distinguishing observable is that NOTHING is written except this
    /// record's share rows: one bulk update on the share table, no other SDK call (the mock is strict), and no
    /// Web API write of any kind.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_WritesOnlyShareRows_SoStandingGrantAccessIsUnaffected()
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);
        _table.SeedOrganizationShare(OrganizationId, ExternalGrantRootType.Matter, MatterId);

        await Send(NewExpiry);

        _dataverse.Verify(d => d.BulkUpdateAsync(
            "sprk_externalrecordaccess", It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(),
            It.IsAny<CancellationToken>()), Times.Once());
        _dataverse.VerifyNoOtherCalls();
        _client.Verify(c => c.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never(), "no contact or organization row — where the standing-grant flag lives — is written");
        _client.Verify(c => c.CreateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Validation — the same date rule as /grant (task 097)
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, SetRecordShareExpiryEndpoint.ExpiryRequiredReasonCode)]
    [InlineData(-1, GrantExternalAccessEndpoint.ExpiryInPastReasonCode)]
    public async Task SetShareExpiry_WithAMissingOrPastExpiry_Returns400AndWritesNothing(int? offsetDays, string reasonCode)
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);

        var result = await Send(offsetDays is { } days ? Today.AddDays(days) : null);

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(reasonCode);
        _bulkUpdates.Should().BeEmpty("a refused request writes nothing");
        _invalidated.Should().BeEmpty();
    }

    /// <summary>The twin of the past-date case: TODAY is a valid expiry — "access until 30 June" means 30 June works.</summary>
    [Fact]
    public async Task SetShareExpiry_WithTodaysDate_IsAccepted()
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);

        var result = await Send(Today);

        OkBody(result).Should().Be(new SetRecordShareExpiryResponse(UpdatedCount: 1, ExpiresDate: Today));
    }

    /// <summary>
    /// Defence in depth: the delegation filter denies an unresolvable record before the handler runs, but the
    /// handler checks again and answers with a reason code and a trace id rather than a bare validation error.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_ForAnUnresolvableRecordType_Returns400WithReasonCodeAndWritesNothing()
    {
        var result = await Send(NewExpiry, recordType: "invoice");

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(SetRecordShareExpiryEndpoint.RecordUnresolvedReasonCode);
        problem.ProblemDetails.Extensions.Should().ContainKey("traceId");
        _bulkUpdates.Should().BeEmpty();
    }

    /// <summary>
    /// A record with no active shares is not an error. Under the reuse design the date has nowhere to live until
    /// a share exists; the client holds it as the default for the next one (task 099). Nothing is written —
    /// which also matters mechanically, since <c>BulkUpdateAsync</c> rejects an empty list.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_OnARecordWithNoActiveShares_Returns200WithZeroAndWritesNothing()
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId, stateCode: 1);

        var result = await Send(NewExpiry);

        OkBody(result).Should().Be(new SetRecordShareExpiryResponse(UpdatedCount: 0, ExpiresDate: NewExpiry));
        _bulkUpdates.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Freshness — whose cache is cleared
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After the write, every contact whose access came through an updated share has its participation cache
    /// cleared: the contact on a contact share, and each ACTIVE member of an organization share's organization.
    /// A former member and a contact on another record are left alone — the set is exactly the affected one.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_InvalidatesTheCacheOfEveryAffectedContact_IncludingOrganizationMembers()
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);
        _table.SeedOrganizationShare(OrganizationId, ExternalGrantRootType.Matter, MatterId);
        _table.SeedMembership(OrganizationId, MemberA);
        _table.SeedMembership(OrganizationId, MemberB);
        _table.SeedMembership(OrganizationId, FormerMember, stateCode: 1);
        _table.SeedContactShare(UnrelatedContactId, ExternalGrantRootType.Matter, OtherMatterId);

        await Send(NewExpiry);

        _invalidated.Should().OnlyContain(i =>
            i.Tenant == TenantId &&
            i.Resource == ExternalParticipationService.ExternalAccessResource &&
            i.Version == ExternalParticipationService.CacheVersion,
            "the key must be the one ExternalParticipationService stores under, or the removal clears nothing");
        _invalidated.Select(i => i.Id).Should().BeEquivalentTo(
            new[] { ContactId, MemberA, MemberB }.Select(id => id.ToString()));
    }

    /// <summary>
    /// Task 137 (verifier r1 finding 8): an organization share whose organization is LARGER than the revoke path's
    /// 200-member SPE bound has every ACTIVE member invalidated — the member walk is paged to completion through the
    /// real routine, not capped (the old share-expiry path skipped an organization over 200 members silently). A
    /// former member is still left alone.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_OnAnOrganizationOverTheTwoHundredMemberBound_InvalidatesEveryActiveMember()
    {
        _table.SeedOrganizationShare(OrganizationId, ExternalGrantRootType.Matter, MatterId);
        var members = Enumerable.Range(1, ExternalOrganizationMembership.MaxMembersPerSweep + 51)
            .Select(i => Guid.Parse($"dddddddd-0000-0000-0000-{i:D12}"))
            .ToArray();
        foreach (var member in members)
        {
            _table.SeedMembership(OrganizationId, member);
        }

        _table.SeedMembership(OrganizationId, FormerMember, stateCode: 1);
        var participations = Participations();
        participations.PageSize = 100;

        var result = await Send(NewExpiry, participations: participations);

        OkBody(result).UpdatedCount.Should().Be(1);
        _invalidated.Select(i => i.Id).Should().BeEquivalentTo(members.Select(id => id.ToString()),
            "every active member — all 251 — is invalidated, and the former member is not");
        participations.PageReads.Select(p => p.Page).Should().Equal(new[] { 0, 1, 2 },
            "251 members at 100 per page are read in three pages, to the end");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The stored date
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The value handed to the SDK is the requested calendar date at midnight with NO time-zone kind.
    /// <c>sprk_expiresdate</c> is Format = DateOnly, Behavior = TimeZoneIndependent (live metadata, 2026-09-11),
    /// so Dataverse stores what it is given unconverted; an Unspecified kind leaves nothing on the way to convert.
    /// A Local value would move midnight onto the neighbouring date on any machine not at UTC. The second half
    /// runs the captured updates through the real transaction builder, so the assertion covers what the SDK
    /// receives, not only what this endpoint built.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_StoresTheRequestedCalendarDate_WithNoTimeZoneForTheWriteToShift()
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);

        await Send(NewExpiry);

        var updates = SingleBulkUpdate().Updates;
        var written = updates.Single().fields["sprk_expiresdate"].Should().BeOfType<DateTime>().Subject;
        written.Should().Be(new DateTime(2026, 12, 10, 0, 0, 0));
        written.Kind.Should().Be(DateTimeKind.Unspecified);

        var transaction = DataverseServiceClientImpl.BuildBulkUpdateTransaction("sprk_externalrecordaccess", updates);
        var sent = (DateTime)((UpdateRequest)transaction.Requests.Single()).Target["sprk_expiresdate"];
        sent.Should().Be(new DateTime(2026, 12, 10, 0, 0, 0));
        sent.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Contact-issued shares are taken over (task 140; session 27 round 34 item 3)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Round 34 item 3: an internal Write holder who changes a CONTACT-issued share outside the grant core takes it over
    /// exactly as <c>/grant</c> does — <c>sprk_grantedbycontact</c> cleared AND <c>sprk_grantedby</c> stamped with the
    /// changing systemuser, in the SAME write as the expiry (the ONE transaction). Left stamped, the issuing contact could
    /// revoke the operator's date, or lengthen it again with its own re-grant. The twins on the same record differ in one
    /// input — the issuer: a systemuser-issued share and an organization share take the expiry and nothing else.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_TakesOverEveryContactIssuedShare_InTheSameTransaction_AndOnlyThose()
    {
        _table.SystemUsersByOid[CallerOid] = CallerSystemUserId;
        var contactIssued = _table.SeedContactShare(
            ContactId, ExternalGrantRootType.Matter, MatterId, expires: Today.AddDays(60), grantedByContact: OtherContactId);
        var internallyIssued = _table.SeedContactShare(
            UnrelatedContactId, ExternalGrantRootType.Matter, MatterId, expires: Today.AddDays(60), grantedBySystemUser: OtherSystemUserId);
        var firm = _table.SeedOrganizationShare(OrganizationId, ExternalGrantRootType.Matter, MatterId, expires: Today.AddDays(60));

        var result = await Send(NewExpiry, callerOid: CallerOid);

        OkBody(result).UpdatedCount.Should().Be(3);
        var updates = SingleBulkUpdate().Updates.ToDictionary(u => u.id, u => u.fields);

        var takenOver = updates[contactIssued.Id];
        takenOver.Keys.Should().BeEquivalentTo(new[] { "sprk_expiresdate", "sprk_grantedbycontact", "sprk_grantedbycontactid", "sprk_grantedby" },
            "the take-over travels in the SAME write as the date — never a second, separately failing one");
        takenOver["sprk_grantedbycontact"].Should().BeSameAs(DBNull.Value,
            "DBNull.Value is the IGenericEntityService convention that CLEARS a column (a C# null would be skipped)");
        takenOver["sprk_grantedbycontactid"].Should().BeSameAs(DBNull.Value,
            "the issuer's recorded provenance is cleared in the same write as its lookup (round 50 item 2)");
        var stamp = takenOver["sprk_grantedby"].Should().BeOfType<Microsoft.Xrm.Sdk.EntityReference>().Subject;
        stamp.LogicalName.Should().Be("systemuser");
        stamp.Id.Should().Be(CallerSystemUserId, "the changing internal user now owns the decision");

        updates[internallyIssued.Id].Keys.Should().Equal(new[] { "sprk_expiresdate" },
            "a share an internal user issued is not re-stamped — only a contact-issued one is taken over");
        updates[firm.Id].Keys.Should().Equal(new[] { "sprk_expiresdate" });

        // What the SDK receives — through the REAL transaction builder: the issuer attribute present with a null value
        // (the SDK's "clear"), the systemuser reference set, in the ONE ExecuteTransactionRequest.
        var transaction = DataverseServiceClientImpl.BuildBulkUpdateTransaction(
            "sprk_externalrecordaccess", SingleBulkUpdate().Updates);
        var target = transaction.Requests.Cast<UpdateRequest>().Select(r => r.Target).Single(t => t.Id == contactIssued.Id);
        target.Attributes.Should().ContainKey("sprk_grantedbycontact");
        target["sprk_grantedbycontact"].Should().BeNull();
        target.Attributes.Should().ContainKey("sprk_grantedbycontactid");
        target["sprk_grantedbycontactid"].Should().BeNull();
        target.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("sprk_grantedby").Id.Should().Be(CallerSystemUserId);
    }

    /// <summary>
    /// Session 27 round 50 item 2: a share whose issuing CONTACT WAS DELETED — the lookup emptied by its RemoveLink cascade,
    /// the issuer's id still recorded in <c>sprk_grantedbycontactid</c> — is contact-issued all the same, so the internal
    /// change takes it over too: the provenance cleared and the caller stamped, in the same write. Left recorded, the
    /// reconciliation job would later read the operator's share as a deleted contact's. The twin is the test above (the
    /// lookup still set): the one input is whether the contact still exists.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_TakesOverAShareWhoseIssuingContactWasDeleted()
    {
        _table.SystemUsersByOid[CallerOid] = CallerSystemUserId;
        var orphaned = _table.SeedContactShare(
            ContactId, ExternalGrantRootType.Matter, MatterId, expires: Today.AddDays(60), grantedByContact: OtherContactId);
        orphaned.GrantedByContactId = null; // the contact was deleted: RemoveLink empties the lookup, the provenance stays

        var result = await Send(NewExpiry, callerOid: CallerOid);

        OkBody(result).UpdatedCount.Should().Be(1);
        var fields = SingleBulkUpdate().Updates.Single(u => u.id == orphaned.Id).fields;
        fields.Keys.Should().BeEquivalentTo(new[] { "sprk_expiresdate", "sprk_grantedbycontact", "sprk_grantedbycontactid", "sprk_grantedby" });
        fields["sprk_grantedbycontactid"].Should().BeSameAs(DBNull.Value);
        fields["sprk_grantedby"].Should().BeOfType<Microsoft.Xrm.Sdk.EntityReference>().Which.Id.Should().Be(CallerSystemUserId);
    }

    /// <summary>
    /// The systemuser stamp is audit; an unresolvable caller never blocks the write (the grant core's rule) — but the
    /// contact issuer is STILL cleared, because that is what takes the decision out of the contact's hands. The twin of
    /// the test above differing in one input: whether the caller's systemuser resolves.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_WhenTheCallersSystemUserDoesNotResolve_StillClearsTheContactIssuer()
    {
        var contactIssued = _table.SeedContactShare(
            ContactId, ExternalGrantRootType.Matter, MatterId, expires: Today.AddDays(60), grantedByContact: OtherContactId);

        var result = await Send(NewExpiry, callerOid: CallerOid);

        OkBody(result).UpdatedCount.Should().Be(1);
        var fields = SingleBulkUpdate().Updates.Single(u => u.id == contactIssued.Id).fields;
        fields.Keys.Should().BeEquivalentTo(new[] { "sprk_expiresdate", "sprk_grantedbycontact", "sprk_grantedbycontactid" });
        fields["sprk_grantedbycontact"].Should().BeSameAs(DBNull.Value);
        fields["sprk_grantedbycontactid"].Should().BeSameAs(DBNull.Value);
        _table.SystemUserReads.Should().Equal(new[] { $"azureactivedirectoryobjectid eq {CallerOid}" },
            "the caller's systemuser was looked up — by its Entra object id — and found nothing");
    }

    /// <summary>
    /// With no contact-issued share on the record nothing is taken over, so the caller's systemuser is not even read —
    /// the expiry write stays exactly what it was before task 140 (positive twin: the issuer is the one input).
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_WithNoContactIssuedShare_ReadsNoSystemUserAndWritesTheExpiryOnly()
    {
        _table.SystemUsersByOid[CallerOid] = CallerSystemUserId;
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId, grantedBySystemUser: OtherSystemUserId);

        await Send(NewExpiry, callerOid: CallerOid);

        SingleBulkUpdate().Updates.Should().OnlyContain(u => u.fields.Count == 1 && u.fields.ContainsKey("sprk_expiresdate"));
        _table.SystemUserReads.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Failure shapes — a message, never a bare 500, and never a subset
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A failed transaction returns a reason code and a message that says only what is known: the change is
    /// all-or-nothing, so the record is either fully at the new date or untouched — never half. The TITLE must
    /// not claim "not applied" either: a transaction that timed out after committing DID apply.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_WhenTheTransactionFails_Returns500WithAnAllOrNothingMessage()
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);
        _dataverse
            .Setup(d => d.BulkUpdateAsync(
                It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Bulk update of 1 sprk_externalrecordaccess records failed"));

        var result = await Send(NewExpiry);

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(SetRecordShareExpiryEndpoint.WriteFailedReasonCode);
        problem.ProblemDetails.Title.Should().Be("Expiry change not confirmed");
        problem.ProblemDetails.Detail.Should().Contain("all-or-nothing").And.Contain("2026-12-10");
    }

    /// <summary>When the record's shares cannot be read, nothing is written and the caller is told so.</summary>
    [Fact]
    public async Task SetShareExpiry_WhenTheSharesCannotBeRead_Returns500AndWritesNothing()
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);
        _table.QueryFailure = new HttpRequestException("Dataverse unavailable");

        var result = await Send(NewExpiry);

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(SetRecordShareExpiryEndpoint.EnumerationFailedReasonCode);
        _bulkUpdates.Should().BeEmpty();
    }

    /// <summary>
    /// An active share that came back without an id cannot be addressed by an update. Skipping it — as the
    /// upsert's own reader does, correctly for an upsert — would leave that share at its old, possibly later,
    /// date while reporting success. The whole request is refused instead.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_WhenAShareHasNoId_Refuses500AndWritesNothing()
    {
        _table.SeedContactShare(ContactId, ExternalGrantRootType.Matter, MatterId);
        _table.SeedContactShare(OtherContactId, ExternalGrantRootType.Matter, MatterId).Id = Guid.Empty;

        var result = await Send(NewExpiry);

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(SetRecordShareExpiryEndpoint.ShareUnidentifiableReasonCode);
        _bulkUpdates.Should().BeEmpty();
    }

    /// <summary>
    /// More shares than one transaction may carry is refused outright. Updating the first page and reporting
    /// success would leave the rest at their OLD date — for a shortening, access that outlives the new one.
    /// </summary>
    [Fact]
    public async Task SetShareExpiry_OnARecordWithMoreSharesThanOneTransactionHolds_Refuses422AndWritesNothing()
    {
        for (var i = 0; i <= SetRecordShareExpiryEndpoint.MaxSharesPerRecord; i++)
            _table.SeedContactShare(Guid.NewGuid(), ExternalGrantRootType.Matter, MatterId);

        var result = await Send(NewExpiry);

        var problem = Problem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(SetRecordShareExpiryEndpoint.TooManySharesReasonCode);
        _bulkUpdates.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Driving the real handler
    // ─────────────────────────────────────────────────────────────────────────────

    private Task<IResult> Send(
        DateOnly? expiry, string recordType = "matter",
        GrantPolicyTestDoubles.MemberPagingParticipationService? participations = null,
        Guid? callerOid = null) =>
        SetRecordShareExpiryEndpoint.Handle(
            new SetRecordShareExpiryRequest(recordType, MatterId, expiry),
            _client.Object,
            _dataverse.Object,
            // Task 137: the ONE invalidation routine, run for real over this test's cache and the request's tid; only
            // its organization-member page read is answered from the table (by the production filter).
            participations ?? Participations(),
            new FakeTimeProvider(Now),
            AuthenticatedContext(callerOid),
            NullLogger<Program>.Instance,
            CancellationToken.None);

    private GrantPolicyTestDoubles.MemberPagingParticipationService Participations() =>
        new(_cache.Object, AuthenticatedContext(), null) { MemberSource = _table.ActiveMembersOf };

    private (string Entity, List<(Guid id, Dictionary<string, object> fields)> Updates) SingleBulkUpdate() =>
        _bulkUpdates.Should().ContainSingle().Subject;

    private static HttpContext AuthenticatedContext(Guid? callerOid = null)
    {
        var claims = new List<Claim> { new("tid", TenantId) };
        if (callerOid is { } oid)
            claims.Add(new Claim("oid", oid.ToString()));

        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
        return context;
    }

    private static SetRecordShareExpiryResponse OkBody(IResult result) =>
        result.Should().BeOfType<Ok<SetRecordShareExpiryResponse>>().Subject.Value!;

    private static ProblemHttpResult Problem(IResult result) =>
        result.Should().BeOfType<ProblemHttpResult>().Subject;

    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            // Takes the managed-identity branch, whose credential is constructed lazily and never used —
            // every method the handler calls on this client is overridden.
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["TENANT_ID"] = TenantId
        }).Build();

    // ─────────────────────────────────────────────────────────────────────────────
    // An in-memory share table + membership junction that behave like Dataverse
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class FakeShareTable
    {
        private readonly List<ExternalGrantRow> _rows = new();
        private readonly List<(Guid OrganizationId, Guid ContactId, int StateCode)> _memberships = new();
        private int _seq;

        /// <summary>Set to make the share query fail outright.</summary>
        public Exception? QueryFailure { get; set; }

        /// <summary>Task 140: Entra object id → systemuser id, for the take-over stamp's caller lookup.</summary>
        public Dictionary<Guid, Guid> SystemUsersByOid { get; } = new();

        /// <summary>Task 140: every <c>systemusers</c> <c>$filter</c> the handler sent.</summary>
        public List<string> SystemUserReads { get; } = new();

        public ExternalGrantRow SeedContactShare(
            Guid contactId, ExternalGrantRootType rootType, Guid rootId,
            DateOnly? expires = null, int stateCode = 0, Guid? organizationId = null,
            Guid? grantedByContact = null, Guid? grantedBySystemUser = null)
        {
            var row = Seed(contactId, organizationId, rootType, rootId, expires, stateCode);
            row.GrantedByContactId = grantedByContact;
            // As the BFF writes it (round 50 item 2): the issuer's id as text beside the lookup.
            row.GrantedByContactProvenance = grantedByContact is { } issuer ? ExternalGrantLifecycle.ContactIssuerProvenance(issuer) : null;
            row.GrantedBySystemUserId = grantedBySystemUser;
            return row;
        }

        public ExternalGrantRow SeedOrganizationShare(
            Guid organizationId, ExternalGrantRootType rootType, Guid rootId, DateOnly? expires = null)
            => Seed(null, organizationId, rootType, rootId, expires, stateCode: 0);

        public void SeedMembership(Guid organizationId, Guid contactId, int stateCode = 0)
            => _memberships.Add((organizationId, contactId, stateCode));

        /// <summary>
        /// Task 137: the members the invalidation routine's page read returns — by INTERPRETING the production
        /// <see cref="ExternalOrganizationMembership.ActiveMembersFilter"/>, as this table's own junction query does.
        /// </summary>
        public Guid[] ActiveMembersOf(Guid organizationId)
            => MatchMembers(ExternalOrganizationMembership.ActiveMembersFilter(organizationId))
                .Select(m => m.ContactId)
                .ToArray();

        private ExternalGrantRow Seed(
            Guid? contactId, Guid? organizationId, ExternalGrantRootType rootType, Guid rootId,
            DateOnly? expires, int stateCode)
        {
            var row = new ExternalGrantRow
            {
                Id = Guid.Parse($"aaaaaaaa-0000-0000-0000-{++_seq:D12}"),
                ContactId = contactId,
                OrganizationId = organizationId,
                AccessLevel = 1,
                ExpiresDate = expires,
                StateCode = stateCode
            };

            switch (rootType)
            {
                case ExternalGrantRootType.Project: row.ProjectId = rootId; break;
                case ExternalGrantRootType.Matter: row.MatterId = rootId; break;
                case ExternalGrantRootType.WorkAssignment: row.WorkAssignmentId = rootId; break;
            }

            _rows.Add(row);
            return row;
        }

        public Mock<DataverseWebApiClient> BuildMock()
        {
            var mock = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance,
                // Moq matches a class-proxy constructor exactly, so the two optional credential slots are
                // passed positionally; this double never authenticates.
                null!, null!);

            mock.Setup(c => c.QueryAsync<ExternalGrantRow>(
                    GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? select, int? top, int? _, CancellationToken _) =>
                {
                    if (QueryFailure is not null)
                        throw QueryFailure;

                    RejectUnknownColumns(select);
                    return MatchShares(filter).Take(top ?? int.MaxValue).ToList();
                });

            mock.Setup(c => c.QueryAsync<ExternalOrganizationMembership.ContactOrganizationRow>(
                    MembershipEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? _, int? _, int? _, CancellationToken _) =>
                    MatchMembers(filter)
                        .Select(m => new ExternalOrganizationMembership.ContactOrganizationRow { ContactId = m.ContactId })
                        .ToList());

            // Task 140: the take-over stamp's caller lookup (GrantExternalAccessEndpoint.ResolveGrantedBySystemUserIdAsync),
            // answered by INTERPRETING its one clause — strict, like the share filter.
            mock.Setup(c => c.QueryAsync<GrantExternalAccessEndpoint.SystemUserRow>(
                    "systemusers", It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? _, int? _, int? _, CancellationToken _) =>
                {
                    SystemUserReads.Add(filter ?? string.Empty);
                    var oid = Regex.Match(filter ?? string.Empty, @"^azureactivedirectoryobjectid eq ([0-9a-fA-F-]{36})$");
                    if (!oid.Success)
                        throw UnknownClause(filter ?? string.Empty);

                    return SystemUsersByOid.TryGetValue(Guid.Parse(oid.Groups[1].Value), out var systemUserId)
                        ? new List<GrantExternalAccessEndpoint.SystemUserRow> { new() { systemuserid = systemUserId } }
                        : new List<GrantExternalAccessEndpoint.SystemUserRow>();
                });

            return mock;
        }

        private static void RejectUnknownColumns(string? select)
        {
            foreach (var column in (select ?? string.Empty)
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!LiveColumns.Contains(column))
                    throw new InvalidOperationException(
                        $"Dataverse 400: Could not find a property named '{column}' on type " +
                        "'Microsoft.Dynamics.CRM.sprk_externalrecordaccess'.");
            }
        }

        private static IEnumerable<string> Clauses(string? filter)
            => (filter ?? string.Empty).Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static InvalidOperationException UnknownClause(string clause) => new(
            $"The fake does not understand the filter clause '{clause}'. Dataverse WOULD narrow by it, so ignoring " +
            "it would let a narrowing regression pass — teach the fake the clause deliberately.");

        /// <summary>Applies every clause as Dataverse would; throws on a clause it does not understand.</summary>
        private IEnumerable<ExternalGrantRow> MatchShares(string? filter)
        {
            IEnumerable<ExternalGrantRow> rows = _rows;

            foreach (var clause in Clauses(filter))
            {
                var root = Regex.Match(clause,
                    @"^(_sprk_project_value|_sprk_matter_value|_sprk_workassignment_value) eq ([0-9a-fA-F-]{36})$");
                if (root.Success)
                {
                    var id = Guid.Parse(root.Groups[2].Value);
                    rows = root.Groups[1].Value switch
                    {
                        "_sprk_project_value" => rows.Where(r => r.ProjectId == id),
                        "_sprk_matter_value" => rows.Where(r => r.MatterId == id),
                        _ => rows.Where(r => r.WorkAssignmentId == id),
                    };
                    continue;
                }

                if (clause == "statecode eq 0")
                {
                    rows = rows.Where(r => r.StateCode == 0);
                    continue;
                }

                throw UnknownClause(clause);
            }

            return rows;
        }

        private IEnumerable<(Guid OrganizationId, Guid ContactId, int StateCode)> MatchMembers(string? filter)
        {
            IEnumerable<(Guid OrganizationId, Guid ContactId, int StateCode)> members = _memberships;

            foreach (var clause in Clauses(filter))
            {
                var organization = Regex.Match(clause, @"^_sprk_organization_value eq ([0-9a-fA-F-]{36})$");
                if (organization.Success)
                {
                    var id = Guid.Parse(organization.Groups[1].Value);
                    members = members.Where(m => m.OrganizationId == id);
                    continue;
                }

                if (clause == "statecode eq 0")
                {
                    members = members.Where(m => m.StateCode == 0);
                    continue;
                }

                throw UnknownClause(clause);
            }

            return members;
        }
    }
}
