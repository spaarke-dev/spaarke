// spaarke-SPA-external-access-platform-r4 task 014a — RecordContactAccessService: the contacts with EFFECTIVE external access
// to one root record (uac-r2's review of task 014, Q2 and Q6 pins).
//
// Driven through the REAL service over the REAL evaluator (AccessibleRecordSetService's No Access check, the read path's deny
// veto) and the REAL deny-list matching (only NoAccessListReader's wire seam is substituted — GrantPolicyTestDoubles), the
// REAL effective-flag fold over task 148's in-memory filing world, and the REAL grant-row read and expiry rule. Only the
// readers' existing data seams are substituted: ExternalParticipationService's virtual reads (subclass + override),
// DataverseWebApiClient.QueryAsync (the virtual seam SecureRecordOwnerTeam's tests use), and the identity store's
// removed-marker read (module boundary). No HttpMessageHandler double (ADR-038 B1).
//
// Every rule has a must-fire case beside its must-not-fire control in the SAME arrangement, so a test cannot pass by
// listing nobody or everybody.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class RecordContactAccessServiceTests
{
    private const string Matter = "sprk_matter";
    private const string WorkAssignment = "sprk_workassignment";
    private const int ViewOnly = (int)ExternalAccessLevel.ViewOnly;
    private const int Collaborate = (int)ExternalAccessLevel.Collaborate;

    private static readonly DateOnly Today = new(2026, 11, 15);

    private static readonly Guid Root = Guid.Parse("014a014a-0000-0000-0000-0000000000a1");
    private static readonly Guid ParentMatter = Guid.Parse("014a014a-0000-0000-0000-0000000000b1");
    private static readonly Guid OtherMatter = Guid.Parse("014a014a-0000-0000-0000-0000000000b2");
    private static readonly Guid Firm = Guid.Parse("014a014a-0000-0000-0000-0000000000e1");
    private static readonly Guid OtherFirm = Guid.Parse("014a014a-0000-0000-0000-0000000000e2");
    private static readonly Guid MatterType = Guid.Parse("014a014a-0000-0000-0000-0000000000f2");

    private static readonly Guid Alice = Guid.Parse("014a014a-0000-0000-0000-00000000c001");
    private static readonly Guid Bob = Guid.Parse("014a014a-0000-0000-0000-00000000c002");
    private static readonly Guid Carol = Guid.Parse("014a014a-0000-0000-0000-00000000c003");
    private static readonly Guid Dan = Guid.Parse("014a014a-0000-0000-0000-00000000c004");
    private static readonly Guid Erin = Guid.Parse("014a014a-0000-0000-0000-00000000c005");
    private static readonly Guid Finn = Guid.Parse("014a014a-0000-0000-0000-00000000c006");
    private static readonly Guid Gail = Guid.Parse("014a014a-0000-0000-0000-00000000c007");
    private static readonly Guid Hank = Guid.Parse("014a014a-0000-0000-0000-00000000c008");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("sprk_recordtype_ref", MatterType, ("sprk_recordlogicalname", Matter));

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero));
    private readonly GrantRowsClient _dataverse = new();
    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _denyList = new();
    private readonly Participations _participations;
    private readonly ConcurrentDictionary<Guid, RemovedMarkerRead> _markers = new();
    private readonly ConcurrentDictionary<Guid, bool> _markerThrows = new();

    public RecordContactAccessServiceTests()
    {
        _participations = new Participations(Entities());
    }

    // ── Rule 1: active, unexpired contact grants ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContactsWithAccess_DirectGrantConferringToday_IsListedWithItsGrantRowName()
    {
        _dataverse.Grant(Alice, null, Today.AddDays(30), name: "Alice Direct");

        var result = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        Known(result).Should().Equal(new RecordAccessContact(Alice, "Alice Direct"));
        _dataverse.Reads.Should().NotContain(r => r.EntitySet == "contacts",
            "the grant row's formatted name is enough; no second read for a named direct grantee");
    }

    [Fact]
    public async Task GetContactsWithAccess_ExpiredUndatedInactiveOrLevellessGrants_AreNotListed_ATodayExpiryIs()
    {
        _dataverse.Grant(Alice, null, Today, name: "Alice Today");                  // expires TODAY: still confers (ge)
        _dataverse.Grant(Dan, null, Today.AddDays(-1), name: "Dan Lapsed");         // expired yesterday
        _dataverse.Grant(Erin, null, null, name: "Erin Undated");                   // no expiry confers nothing (D-1)
        _dataverse.Grant(Finn, null, Today.AddDays(30), state: 1, name: "Finn Off"); // inactive row
        _dataverse.Grant(Gail, null, Today.AddDays(30), level: null, name: "Gail NoLevel"); // no level = not granted

        var result = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        Known(result).Select(c => c.ContactId).Should().Equal(Alice);
    }

    // ── Rule 2: organization grants reach conferring members — except on a direct-only record ──────────────────────────

    [Fact]
    public async Task GetContactsWithAccess_OrganizationGrantOnStandardRecord_ListsOnlyMembersWhoseMembershipConfers()
    {
        _dataverse.Grant(null, Firm, Today.AddDays(30));
        _participations.Members[Firm] = new[] { Bob, Carol };
        _participations.Memberships[Bob] = (Conferring: new[] { Firm }, Wall: new[] { Firm });
        _participations.Memberships[Carol] = (Conferring: Array.Empty<Guid>(), Wall: new[] { Firm }); // ended / not started
        _dataverse.FullNames[Bob] = "Bob Member";
        _dataverse.FullNames[Carol] = "Carol Former";

        var result = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        Known(result).Should().Equal(new RecordAccessContact(Bob, "Bob Member"));
    }

    [Theory]
    [InlineData(true, false)]  // Secure
    [InlineData(false, true)]  // Limited — the evaluator's same direct-only predicate
    public async Task GetContactsWithAccess_DirectOnlyRecord_ListsNamedDirectGrantsButNotOrganizationMembers(bool secure, bool limited)
    {
        _dataverse.Grant(Alice, null, Today.AddDays(30), name: "Alice Direct");
        _dataverse.Grant(null, Firm, Today.AddDays(30));
        _participations.Members[Firm] = new[] { Bob };
        _participations.Memberships[Bob] = (Conferring: new[] { Firm }, Wall: new[] { Firm });
        _dataverse.FullNames[Bob] = "Bob Member";

        var standard = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);
        Known(standard).Select(c => c.ContactId).Should().BeEquivalentTo(new[] { Alice, Bob },
            "control: on a Standard record the organization grant reaches its member");

        _participations.Flags[Root] = new RootRecordFlags(IsSecure: secure, IsRestricted: false, IsLimited: limited);
        var directOnly = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        Known(directOnly).Select(c => c.ContactId).Should().Equal(new[] { Alice },
            "on a direct-only record only named, direct contact grants count");
    }

    // ── Rule 3: Restricted → nobody ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContactsWithAccess_RestrictedRecord_IsKnownEmpty_AStandardOneListsTheGrantee()
    {
        _dataverse.Grant(Alice, null, Today.AddDays(30), name: "Alice Direct");

        var standard = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);
        Known(standard).Select(c => c.ContactId).Should().Equal(Alice);

        _participations.Flags[Root] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        var restricted = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        restricted.Should().BeOfType<RecordContactAccess.Known>()
            .Which.Contacts.Should().BeEmpty("Restricted removes every contact-sourced grant, direct ones included");
    }

    // ── Rule 4: the No Access list (the contact, its organization, a filing ancestor) ──────────────────────────────────

    [Fact]
    public async Task GetContactsWithAccess_ContactOnTheRecordsNoAccessList_IsAbsent_AnotherGranteeIsListed()
    {
        _dataverse.Grant(Alice, null, Today.AddDays(30), name: "Alice Direct");
        _dataverse.Grant(Hank, null, Today.AddDays(30), name: "Hank Walled");
        _denyList.DenyContactOnRecord(Hank, Root);

        var result = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        Known(result).Select(c => c.ContactId).Should().Equal(Alice);
    }

    [Fact]
    public async Task GetContactsWithAccess_ContactWhoseOrganizationIsOnTheNoAccessList_IsAbsent_AnOutsiderIsListed()
    {
        _dataverse.Grant(Alice, null, Today.AddDays(30), name: "Alice Direct");
        _dataverse.Grant(Hank, null, Today.AddDays(30), name: "Hank Member");
        // The WALL set binds a member whatever the membership dates (owner D-2 part 2) — so it is not in the conferring set here.
        _participations.Memberships[Hank] = (Conferring: Array.Empty<Guid>(), Wall: new[] { OtherFirm });
        _denyList.DenyOrganizationOnRecord(OtherFirm, Root);

        var result = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        Known(result).Select(c => c.ContactId).Should().Equal(Alice);
    }

    [Fact]
    public async Task GetContactsWithAccess_ContactWalledOnASecureFilingAncestor_IsAbsent_AWallElsewhereDoesNothing()
    {
        MatterRow(ParentMatter, secure: true);
        MatterRow(OtherMatter, secure: true);
        _world.Add(WorkAssignment, Root,
            ("sprk_issecure", false),
            ("sprk_regardingmatter", new EntityReference(Matter, ParentMatter)));
        _participations.Flags[Root] = RootRecordFlags.None; // the work assignment's OWN row is open and Standard
        _dataverse.Grant(Alice, null, Today.AddDays(30), name: "Alice Elsewhere", root: "_sprk_workassignment_value");
        _dataverse.Grant(Hank, null, Today.AddDays(30), name: "Hank Walled", root: "_sprk_workassignment_value");
        _denyList.DenyContactOnRecord(Hank, ParentMatter);
        _denyList.DenyContactOnRecord(Alice, OtherMatter);

        var result = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.WorkAssignment, Root, CancellationToken.None);

        Known(result).Select(c => c.ContactId).Should().Equal(new[] { Alice },
            "the secure parent's list binds what is filed below it; a wall on an unrelated matter does not");
    }

    // ── Rule 5: removed marker, inactive contact ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContactsWithAccess_ContactWithARemovedMarker_IsAbsent_ANotRemovedOneIsListed()
    {
        _dataverse.Grant(Alice, null, Today.AddDays(30), name: "Alice Direct");
        _dataverse.Grant(Hank, null, Today.AddDays(30), name: "Hank Removed");
        _markers[Hank] = RemovedMarkerRead.Removed;

        var result = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        Known(result).Select(c => c.ContactId).Should().Equal(Alice);
    }

    [Fact]
    public async Task GetContactsWithAccess_InactiveContact_IsAbsent_AnActiveOneIsListed()
    {
        _dataverse.Grant(Alice, null, Today.AddDays(30), name: "Alice Direct");
        _dataverse.Grant(Hank, null, Today.AddDays(30), name: "Hank Inactive");
        _participations.States[Hank] = ContactRecordState.Inactive;

        var result = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        Known(result).Select(c => c.ContactId).Should().Equal(Alice);
    }

    // ── Rule 6: any fault → Unknown, never a partial or empty Known ────────────────────────────────────────────────────

    public enum Fault
    {
        FlagsReadThrows,
        FlagsUnreadable,
        GrantsReadThrows,
        GrantsOverBound,
        OrganizationMembersReadThrows,
        OrganizationMembersOverBound,
        MembershipsUnreadable,
        NoAccessReaderFails,
        ContactStateUnreadable,
        RemovedMarkerUnreadable,
        RemovedMarkerReadThrows,
        NamesReadThrows,
    }

    [Theory]
    [InlineData(Fault.FlagsReadThrows, RecordContactAccessUnknownReason.FlagsUnreadable)]
    [InlineData(Fault.FlagsUnreadable, RecordContactAccessUnknownReason.FlagsUnreadable)]
    [InlineData(Fault.GrantsReadThrows, RecordContactAccessUnknownReason.GrantsUnreadable)]
    [InlineData(Fault.GrantsOverBound, RecordContactAccessUnknownReason.GrantsOverBound)]
    [InlineData(Fault.OrganizationMembersReadThrows, RecordContactAccessUnknownReason.OrganizationMembersUnreadable)]
    [InlineData(Fault.OrganizationMembersOverBound, RecordContactAccessUnknownReason.TooManyContacts)]
    [InlineData(Fault.MembershipsUnreadable, RecordContactAccessUnknownReason.MembershipsUnreadable)]
    [InlineData(Fault.NoAccessReaderFails, RecordContactAccessUnknownReason.NoAccessUnverifiable)]
    [InlineData(Fault.ContactStateUnreadable, RecordContactAccessUnknownReason.ContactStateUnreadable)]
    [InlineData(Fault.RemovedMarkerUnreadable, RecordContactAccessUnknownReason.RemovedMarkerUnreadable)]
    [InlineData(Fault.RemovedMarkerReadThrows, RecordContactAccessUnknownReason.RemovedMarkerUnreadable)]
    [InlineData(Fault.NamesReadThrows, RecordContactAccessUnknownReason.NamesUnreadable)]
    public async Task GetContactsWithAccess_AnyReadFault_IsUnknown_NeverAPartialKnown(
        Fault fault, RecordContactAccessUnknownReason expected)
    {
        // A direct grantee (Alice) and an organization member (Bob): every reader is on the path.
        _dataverse.Grant(Alice, null, Today.AddDays(30), name: "Alice Direct");
        _dataverse.Grant(null, Firm, Today.AddDays(30));
        _participations.Members[Firm] = new[] { Bob };
        _participations.Memberships[Bob] = (Conferring: new[] { Firm }, Wall: new[] { Firm });
        _dataverse.FullNames[Bob] = "Bob Member";

        var healthy = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);
        Known(healthy).Select(c => c.ContactId).Should().BeEquivalentTo(new[] { Alice, Bob }, "control: no fault, both listed");

        Inject(fault);
        var faulted = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        faulted.Should().BeOfType<RecordContactAccess.Unknown>()
            .Which.Reason.Should().Be(expected, "a fault is never a partial or empty list");
    }

    // ── Rule 7: names and ids only ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContactsWithAccess_NameRead_SelectsOnlyIdAndFullName_AndTheAnswerCarriesNoEmail()
    {
        _dataverse.Grant(null, Firm, Today.AddDays(30));
        _participations.Members[Firm] = new[] { Bob };
        _participations.Memberships[Bob] = (Conferring: new[] { Firm }, Wall: new[] { Firm });
        _dataverse.FullNames[Bob] = "Bob Member";
        _dataverse.Emails[Bob] = "bob@firm.example";

        var result = await Service().GetContactsWithAccessAsync(ExternalGrantRootType.Matter, Root, CancellationToken.None);

        Known(result).Should().Equal(new RecordAccessContact(Bob, "Bob Member"));
        _dataverse.Reads.Where(r => r.EntitySet == "contacts").Should().ContainSingle()
            .Which.Select.Should().Be("contactid,fullname", "the email column is never even read");
        JsonSerializer.Serialize(result, result.GetType()).Should().Contain("Bob Member")
            .And.NotContain("bob@firm.example");
    }

    // ── Fixtures ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<RecordAccessContact> Known(RecordContactAccess result)
        => result.Should().BeOfType<RecordContactAccess.Known>().Which.Contacts;

    private void Inject(Fault fault)
    {
        switch (fault)
        {
            case Fault.FlagsReadThrows:
                _participations.FlagsThrow = true;
                break;
            case Fault.FlagsUnreadable:
                _participations.Flags[Root] = RootRecordFlags.Unreadable;
                break;
            case Fault.GrantsReadThrows:
                _dataverse.GrantsThrow = true;
                break;
            case Fault.GrantsOverBound:
                for (var i = 0; i < RecordContactAccessService.MaxGrantRows; i++)
                    _dataverse.Grant(Guid.NewGuid(), null, Today.AddDays(-1)); // lapsed: only the COUNT matters
                break;
            case Fault.OrganizationMembersReadThrows:
                _participations.MembersThrow = true;
                break;
            case Fault.OrganizationMembersOverBound:
                _participations.Members[Firm] = Enumerable.Range(0, RecordContactAccessService.MaxContacts + 1)
                    .Select(_ => Guid.NewGuid()).ToArray();
                break;
            case Fault.MembershipsUnreadable:
                _participations.UnreadableMemberships[Bob] = true;
                break;
            case Fault.NoAccessReaderFails:
                _denyList.Faults = true;
                break;
            case Fault.ContactStateUnreadable:
                _participations.States[Alice] = ContactRecordState.Unreadable;
                break;
            case Fault.RemovedMarkerUnreadable:
                _markers[Alice] = RemovedMarkerRead.Unreadable;
                break;
            case Fault.RemovedMarkerReadThrows:
                _markerThrows[Alice] = true;
                break;
            case Fault.NamesReadThrows:
                _dataverse.NamesThrow = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
        }
    }

    private void MatterRow(Guid id, bool secure)
    {
        _world.Add(Matter, id, ("sprk_issecure", secure), ("sprk_mattername", "Matter " + id.ToString("N")[..4]));
        _participations.Flags[id] = new RootRecordFlags(secure, IsRestricted: false);
    }

    private IGenericEntityService Entities()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) => Task.FromResult(_world.Answer(query)));
        return entities.Object;
    }

    private IContactIdentityStore IdentityStore()
    {
        var store = new Mock<IContactIdentityStore>(MockBehavior.Strict);
        store
            .Setup(s => s.ReadRemovedMarkerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) => _markerThrows.ContainsKey(id)
                ? Task.FromException<RemovedMarkerRead>(new HttpRequestException("Test: the marker could not be read."))
                : Task.FromResult(_markers.TryGetValue(id, out var marker) ? marker : RemovedMarkerRead.NotRemoved));
        return store.Object;
    }

    private RecordContactAccessService Service()
    {
        var entities = Entities();
        var evaluator = new AccessibleRecordSetService(
            Mock.Of<IMembershipResolverService>(), _participations, Mock.Of<ISubjectStandingGrantReader>(), _denyList,
            UnlinkedIdentityStore(), InternalSystemUsers(), entities, NullLogger<AccessibleRecordSetService>.Instance);
        return new RecordContactAccessService(
            _dataverse, _participations, evaluator, IdentityStore(), _time, httpContextAccessor: null,
            NullLogger<RecordContactAccessService>.Instance);
    }

    /// <summary>
    /// The production <see cref="ExternalParticipationService"/> with only its data reads overridden (the evaluator tests'
    /// seam). The effective-flag FOLD and the contact-state memo wrapper run unmodified.
    /// </summary>
    private sealed class Participations : ExternalParticipationService
    {
        public Participations(IGenericEntityService filing)
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance, filing: filing)
        {
        }

        public ConcurrentDictionary<Guid, RootRecordFlags> Flags { get; } = new();
        public bool FlagsThrow { get; set; }
        public ConcurrentDictionary<Guid, Guid[]> Members { get; } = new();
        public bool MembersThrow { get; set; }
        public ConcurrentDictionary<Guid, (Guid[] Conferring, Guid[] Wall)> Memberships { get; } = new();
        public ConcurrentDictionary<Guid, bool> UnreadableMemberships { get; } = new();
        public ConcurrentDictionary<Guid, ContactRecordState> States { get; } = new();

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            if (FlagsThrow)
                throw new HttpRequestException("Test: the flags could not be read.");

            IReadOnlyDictionary<Guid, RootRecordFlags> result = recordIds.Distinct()
                .ToDictionary(id => id, id => Flags.TryGetValue(id, out var f) ? f : RootRecordFlags.None);
            return Task.FromResult(result);
        }

        public override Task<(IReadOnlyList<Guid> ContactIds, bool Truncated)> FindWallMemberContactsAsync(
            Guid organizationId, int maxRows, CancellationToken ct = default)
        {
            if (MembersThrow)
                throw new HttpRequestException("Test: the members could not be read.");

            var members = Members.TryGetValue(organizationId, out var m) ? m : Array.Empty<Guid>();
            return Task.FromResult<(IReadOnlyList<Guid>, bool)>(
                members.Length > maxRows ? (members.Take(maxRows).ToList(), true) : (members, false));
        }

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
        {
            if (UnreadableMemberships.ContainsKey(contactId))
                return Task.FromResult(ActiveOrgMemberships.Failed);

            return Task.FromResult(Memberships.TryGetValue(contactId, out var m)
                ? new ActiveOrgMemberships(m.Conferring, m.Wall, Unreadable: false)
                : ActiveOrgMemberships.None);
        }

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
            => Task.FromResult(States.TryGetValue(contactId, out var state) ? state : ContactRecordState.Active);

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, ReferencedOrganizations>>(
                recordIds.Distinct().ToDictionary(id => id, _ => ReferencedOrganizations.None));
    }

    /// <summary>
    /// The production <see cref="DataverseWebApiClient"/> with only its virtual <c>QueryAsync</c> answered: the grant rows (in
    /// the Web API's JSON shape, deserialized by the production row type) and the contacts' names. Records every read.
    /// </summary>
    private sealed class GrantRowsClient : DataverseWebApiClient
    {
        private static readonly Regex ContactIdClause = new(@"contactid eq ([0-9a-fA-F-]{36})");
        private readonly List<Dictionary<string, object?>> _grantRows = new();

        public GrantRowsClient()
            : base(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            }).Build(), NullLogger<DataverseWebApiClient>.Instance, new NoCredential())
        {
        }

        public Dictionary<Guid, string?> FullNames { get; } = new();
        public Dictionary<Guid, string> Emails { get; } = new();
        public bool GrantsThrow { get; set; }
        public bool NamesThrow { get; set; }
        public ConcurrentQueue<(string EntitySet, string? Select)> Reads { get; } = new();

        /// <summary>One active grant row on <see cref="Root"/>: a contact grant, or an organization grant (no contact).</summary>
        public void Grant(Guid? contact, Guid? organization, DateOnly? expires, int? level = Collaborate, int state = 0,
            string? name = null, string root = "_sprk_matter_value")
            => _grantRows.Add(new Dictionary<string, object?>
            {
                ["sprk_externalrecordaccessid"] = Guid.NewGuid(),
                ["sprk_accesslevel"] = level,
                ["statecode"] = state,
                // The TimeZoneIndependent wire shape the live read returns (task 140 §12).
                ["sprk_expiresdate"] = expires is { } date ? $"{date:yyyy-MM-dd}T00:00:00Z" : null,
                ["_sprk_contact_value"] = contact,
                ["_sprk_contact_value@OData.Community.Display.V1.FormattedValue"] = name,
                ["_sprk_organization_value"] = organization,
                [root] = Root,
            });

        public override Task<List<T>> QueryAsync<T>(
            string entitySetName, string? filter = null, string? select = null, int? top = null, int? skip = null,
            CancellationToken cancellationToken = default)
        {
            Reads.Enqueue((entitySetName, select));
            switch (entitySetName)
            {
                case "sprk_externalrecordaccesses":
                    if (GrantsThrow)
                        throw new HttpRequestException("Test: the grant rows could not be read.");
                    return Task.FromResult(Rows<T>(_grantRows.Take(top ?? int.MaxValue)));

                case "contacts":
                    if (NamesThrow)
                        throw new HttpRequestException("Test: the names could not be read.");
                    var wantsEmail = select?.Contains("email", StringComparison.OrdinalIgnoreCase) == true;
                    var rows = ContactIdClause.Matches(filter ?? string.Empty)
                        .Select(m => Guid.Parse(m.Groups[1].Value))
                        .Where(FullNames.ContainsKey)
                        .Select(id =>
                        {
                            var row = new Dictionary<string, object?> { ["contactid"] = id, ["fullname"] = FullNames[id] };
                            if (wantsEmail && Emails.TryGetValue(id, out var email))
                                row["emailaddress1"] = email;
                            return row;
                        });
                    return Task.FromResult(Rows<T>(rows));

                default:
                    throw new InvalidOperationException($"Test: unexpected read of {entitySetName}.");
            }
        }

        private static List<T> Rows<T>(IEnumerable<Dictionary<string, object?>> rows)
            => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows.ToList()))!;

        private sealed class NoCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Test: this double never authenticates.");

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Test: this double never authenticates.");
        }
    }
}
