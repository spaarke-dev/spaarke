using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 142 r4 (owner round 13 item 4, BINDING): the WRITE-time No Access check
/// (<see cref="IAccessibleRecordSetService.CheckGranteeNoAccessAsync"/>) answers a TRI-STATE — Allowed, Denied (an entry,
/// the record's policy) or Unverifiable (a read fault) — instead of absorbing every fault into "denied".
/// </summary>
/// <remarks>
/// <para><b>What runs.</b> The production <see cref="AccessibleRecordSetService"/> and its shared deny-veto code, over the
/// production <see cref="NoAccessListReader"/> behind its wire seam
/// (<see cref="GrantPolicyTestDoubles.SeamNoAccessListReader"/>) and the participation reads behind theirs
/// (<see cref="GrantPolicyTestDoubles.FlagStubParticipationService"/>) — the doubles the grant core's own tests use, so the
/// answer is the veto code's, never the double's. No <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038).</para>
/// <para><b>Every fault has its twins</b>: the same request with the reads answering is Allowed, and an entry is Denied —
/// so a check that answered Unverifiable (or Denied) for everything would fail here.</para>
/// <para>The consumers' handling of each answer is pinned where they live: the grant routes in
/// <c>GrantorCeilingTests</c>, the Assigned-To materializer in <c>AssignedAccessMaterializerTests</c>, its job in
/// <c>AssignedAccessReconciliationJobTests</c>. KEEP path: <c>tests/integration/auth/**</c> (ADR-038 §2).</para>
/// </remarks>
public class GranteeNoAccessCheckTests
{
    private const string Project = "sprk_project";
    private static readonly Guid RecordId = Guid.Parse("14214214-2142-1421-4214-214214214214");
    private static readonly Guid ContactId = Guid.Parse("c0c0c0c0-0000-0000-0000-000000000142");
    private static readonly Guid OtherContactId = Guid.Parse("c0c0c0c0-0000-0000-0000-000000000143");
    private static readonly Guid FirmId = Guid.Parse("f1f1f1f1-0000-0000-0000-000000000142");
    private static readonly Guid WalledOrganizationId = Guid.Parse("0a0a0a0a-0000-0000-0000-000000000142");

    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _participations = new(RootRecordFlags.None);
    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _reader = new();

    private Task<NoAccessCheckAnswer> CheckAsync(
        Guid? contactId = null, Guid[]? organizationIds = null, INoAccessListReader? reader = null,
        Guid? recordId = null, CancellationToken ct = default)
        => Service(reader).CheckGranteeNoAccessAsync(
            Project, recordId ?? RecordId, contactId ?? ContactId, organizationIds ?? Array.Empty<Guid>(), ct);

    private AccessibleRecordSetService Service(INoAccessListReader? reader) => reader is null
        ? GrantPolicyTestDoubles.RealDenyList(_participations, _reader)
        : new AccessibleRecordSetService(
            Mock.Of<IMembershipResolverService>(),
            _participations,
            Mock.Of<ISubjectStandingGrantReader>(),
            reader,
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.UnlinkedIdentityStore(),
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.InternalSystemUsers(),
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities(),
            NullLogger<AccessibleRecordSetService>.Instance);

    // ─────────────────────────────────────────────────────────────────────────────
    // Allowed and Denied — the twins every fault below is measured against
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AGranteeNoEntryCovers_IsAllowed()
    {
        _reader.DenyContactOnRecord(OtherContactId, RecordId); // someone else

        (await CheckAsync()).Should().Be(NoAccessCheckAnswer.Allowed);
        _reader.Queries.Should().BePositive("the answer came from the shared deny-list reader");
    }

    [Theory]
    [InlineData("contact-on-record")]
    [InlineData("contacts-organization-on-record")]
    [InlineData("named-firm-on-record")]
    [InlineData("contact-walled-off-a-referenced-organization")]
    public async Task AnEntryCoveringTheGrantee_IsDenied_TheRecordsPolicy(string entry)
    {
        Guid[]? firms = null;
        switch (entry)
        {
            case "contact-on-record":
                _reader.DenyContactOnRecord(ContactId, RecordId);
                break;
            case "contacts-organization-on-record":
                _participations.ContactOrganizations[ContactId] = new[] { FirmId };
                _reader.DenyOrganizationOnRecord(FirmId, RecordId);
                break;
            case "named-firm-on-record":
                firms = new[] { FirmId };
                _reader.DenyOrganizationOnRecord(FirmId, RecordId);
                break;
            default:
                _participations.RecordOrganizations[RecordId] = new[] { WalledOrganizationId };
                _reader.DenyContactOnOrganization(ContactId, WalledOrganizationId);
                break;
        }

        (await CheckAsync(organizationIds: firms)).Should().Be(NoAccessCheckAnswer.Denied);
    }

    [Fact]
    public async Task NothingToCheck_NoContactAndNoOrganization_IsAllowed_NotAFault()
    {
        _reader.Faults = true; // never consulted: there is no subject to ask about

        (await Service(null).CheckGranteeNoAccessAsync(Project, RecordId, null, Array.Empty<Guid>(), CancellationToken.None))
            .Should().Be(NoAccessCheckAnswer.Allowed);
        _reader.Queries.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Unverifiable — every read fault, reported as one (never absorbed into Denied)
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("memberships-unreadable")]
    [InlineData("referenced-organizations-unreadable")]
    [InlineData("reader-fails-closed")]
    [InlineData("reader-throws-a-5xx")]
    [InlineData("referenced-organizations-throw")]
    [InlineData("referenced-organizations-timeout")]
    [InlineData("no-access-list-timeout")]
    public async Task AReadFault_IsUnverifiable_NeverDenied_AndNeverAllowed(string fault)
    {
        // A timeout is a TaskCanceledException while the CALLER has not cancelled — the veto code rethrows it; the
        // write-time check reports it.
        var timeout = new TaskCanceledException("Simulated HttpClient timeout (the caller did not cancel).");
        switch (fault)
        {
            case "memberships-unreadable": _participations.MembershipsUnreadable = true; break;
            case "referenced-organizations-unreadable": _participations.UnreadableReferencedOrganizations[RecordId] = true; break;
            case "reader-fails-closed": _reader.Faults = true; break;
            case "reader-throws-a-5xx": _reader.Throws = new HttpRequestException("Simulated HTTP 503 (throttled)."); break;
            case "referenced-organizations-throw":
                _participations.ReferencedOrganizationsThrow = new InvalidOperationException("Simulated Dataverse 5xx.");
                break;
            case "referenced-organizations-timeout": _participations.ReferencedOrganizationsThrow = timeout; break;
            default: _reader.Throws = timeout; break;
        }

        (await CheckAsync()).Should().Be(NoAccessCheckAnswer.Unverifiable,
            "a check that could not be completed is a fault (owner round 13 item 4) — fail closed, never an entry");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Round 18 (task 142 R-14): a grantee with more organizations than ONE query holds is CHECKED, not Unverifiable
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Before round 18 a contact whose wall set (every active membership, plus the firm the request names) exceeded 25
    /// organizations was answered Unverifiable on every call — the grant routes' 503 "try again" that never succeeds, the
    /// Assigned-To job red for as long as the contact stayed assigned. Now the reader evaluates the whole set in chunks:
    /// an entry on the LAST organization is Denied, and no entry is Allowed.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AGranteeWithMoreOrganizationsThanOneQueryHolds_IsChecked_NeverUnverifiable(bool lastOrganizationWalled)
    {
        var memberships = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
        _participations.ContactOrganizations[ContactId] = memberships;
        if (lastOrganizationWalled)
            _reader.DenyOrganizationOnRecord(memberships[^1], RecordId);

        (await CheckAsync()).Should().Be(lastOrganizationWalled ? NoAccessCheckAnswer.Denied : NoAccessCheckAnswer.Allowed,
            "a large subject set is evaluated (round 18) — never the deterministic Unverifiable it used to be");
        _reader.Queries.Should().BeGreaterThan(1, "the set was split across subject chunks");
    }

    /// <summary>The fail-closed twin: a genuine read fault in ONE of the chunks is still Unverifiable for the whole check.</summary>
    [Fact]
    public async Task AGranteeWithMoreOrganizationsThanOneQueryHolds_AFaultInOneChunk_IsUnverifiable()
    {
        var memberships = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
        _participations.ContactOrganizations[ContactId] = memberships;
        _reader.FaultsWhenSubjectNames = memberships[^1]; // one subject chunk carries it; the other answers

        (await CheckAsync()).Should().Be(NoAccessCheckAnswer.Unverifiable,
            "the answers of the readable chunks are not all the denials — fail closed, reported as a fault");
    }

    /// <summary>A reader that returns no answer at all — previously a NullReferenceException in the veto's catch-all.</summary>
    [Fact]
    public async Task ADenyListReaderThatReturnsNoAnswer_IsUnverifiable()
    {
        var reader = new Mock<INoAccessListReader>(MockBehavior.Strict);
        reader.Setup(r => r.GetDeniedRecordsAsync(
                It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoAccessListResult)null!);

        (await CheckAsync(reader: reader.Object)).Should().Be(NoAccessCheckAnswer.Unverifiable);
    }

    [Fact]
    public async Task ACallWithoutARecord_IsUnverifiable_ACallerBugIsNeverAnEntry()
    {
        (await CheckAsync(recordId: Guid.Empty)).Should().Be(NoAccessCheckAnswer.Unverifiable);
        _reader.Queries.Should().Be(0);
    }

    /// <summary>
    /// The twin of the timeout rows: the CALLER's own cancellation is not a fault to report — it propagates, as every
    /// cancellation in this code does.
    /// </summary>
    [Fact]
    public async Task TheCallersOwnCancellation_Propagates_NeverAnAnswer()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _participations.ReferencedOrganizationsThrow = new OperationCanceledException(cts.Token);

        var act = () => CheckAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
