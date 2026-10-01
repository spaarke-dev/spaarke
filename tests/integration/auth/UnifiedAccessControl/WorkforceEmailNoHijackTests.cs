using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The workforce plane end to end — <see cref="WorkforcePrincipalResolver"/> over the real
/// <see cref="ContactIdentityBinder"/> and an in-memory Dataverse row store — after task 141 (defect C7).
/// </summary>
/// <remarks>
/// <para><b>What changed, and why this file was rewritten.</b> Task 013 (A-18) added a no-hijack guard to the
/// workforce contact-by-email fallback, but compared the email match against
/// <c>contact.azureactivedirectoryobjectid</c> — a column that does not exist in dev, so every query threw,
/// was swallowed as "no contact", and every Type-2 employee got 403 — and the plane never WROTE a binding, so
/// an unbound contact stayed hijackable forever. Its "inert guard" warning could only fire when the query
/// SUCCEEDED, i.e. never in the environment its comment described. Task 141 replaces all of it: the binding
/// key is the Entra oid in <c>contact.sprk_externalobjectid</c>; the first sign-in of a MEMBER binds or creates;
/// after that, resolution is by oid only; a collision is refused AND flagged.</para>
///
/// <para><b>The inert-guard alarm is gone, by replacement.</b> Its two jobs are now real denies with their own
/// codes: a binding column the environment lacks denies <c>binding_column_missing</c> with an ERROR log
/// (<see cref="AMissingBindingColumn_DeniesWithItsOwnCode_AndAnErrorLog"/>); a column field-level security
/// masks — the one condition that genuinely disables the check — denies <c>binding_column_masked</c> and writes
/// nothing (<see cref="AMaskedBindingColumn_WritesNothing"/>).</para>
///
/// <para><b>The double.</b> <see cref="InMemoryContactIdentityStore"/> is the module boundary (ADR-038: no HTTP
/// doubles). It matches oid and email case-insensitively as Dataverse does, honours <c>$top=2</c>, versions
/// every row for <c>If-Match</c>, and enforces the <c>sprk_externalobjectid</c> alternate key. Every test reads
/// what the code DID (<see cref="InMemoryContactIdentityStore.Writes"/>), not only what it returned.</para>
/// </remarks>
public class WorkforceEmailNoHijackTests
{
    private static readonly Guid Caller = Guid.Parse("aaaaaaaa-1111-4111-8111-000000000001");
    private static readonly Guid Victim = Guid.Parse("bbbbbbbb-2222-4222-8222-000000000002");
    private static readonly Guid ContactA = Guid.Parse("cccccccc-3333-4333-8333-00000000000a");
    private static readonly Guid ContactB = Guid.Parse("cccccccc-3333-4333-8333-00000000000b");
    private const string Email = "employee@customer.example";

    private readonly InMemoryContactIdentityStore _store = new();
    private readonly BindingLogCapture<ContactIdentityBinder> _log = new();

    // ── Acceptance 1: a bound member resolves by oid; email is not consulted ─────────────────────────

    [Fact]
    public async Task ABoundMember_ResolvesByOid_AndEmailIsNeverConsulted()
    {
        _store.AddContact(ContactA, email: "someone-else@customer.example", oid: Caller.ToString("D"),
            plane: IdentityPlaneMarker.Workforce);

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.Principal!.ContactId.Should().Be(ContactA);
        _store.Reads.Should().NotContain("email", "after a contact is bound, resolution is by oid only");
        _store.Writes.Should().BeEmpty();
    }

    // ── Acceptance 2: first sign-in binds; the next sign-in resolves by oid even with another email ──

    [Fact]
    public async Task FirstSignIn_OfAMember_BindsTheOneUnboundMatch_InDFormat_PlaneWorkforce()
    {
        _store.AddContact(ContactA, email: Email);

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email.ToUpperInvariant()));

        result.Principal!.ContactId.Should().Be(ContactA);
        _store.Contacts[ContactA].Oid.Should().Be(Caller.ToString("D"), "the oid is written in 'D' format");
        _store.Contacts[ContactA].Plane.Should().Be((int)IdentityPlaneMarker.Workforce);
        _store.Writes.Should().ContainSingle(w => w.Op == "bind");
    }

    [Fact]
    public async Task TheNextSignIn_ResolvesByOid_EvenWhenTheEmailClaimDiffers()
    {
        _store.AddContact(ContactA, email: Email);
        await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));
        _store.Reads.Clear();

        var second = await Resolve(WorkforceUser(Caller, CustomerTenant, email: "renamed@customer.example"));

        second.Principal!.ContactId.Should().Be(ContactA);
        _store.Reads.Should().NotContain("email");
    }

    // ── Acceptance 3: no match → exactly one contact, even under a race; no grants ───────────────────

    [Fact]
    public async Task AMemberWithNoMatch_GetsExactlyOneContactKeyedByTheOid_CarryingTokenDetails()
    {
        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email, givenName: "Ada", familyName: "Lovelace"));

        var created = _store.ContactsBoundTo(Caller).Should().ContainSingle().Subject;
        result.Principal!.ContactId.Should().Be(created.Id);
        created.Plane.Should().Be((int)IdentityPlaneMarker.Workforce);
        created.Email.Should().Be(Email);
        (created.FirstName, created.LastName).Should().Be(("Ada", "Lovelace"));
        _store.Writes.Should().ContainSingle(w => w.Op == "create",
            "creating a contact is the ONLY write — it carries no grant; access still comes from Dataverse or explicit grants");
    }

    [Fact]
    public async Task TwoConcurrentFirstSignIns_StillLeaveExactlyOneContactForTheOid()
    {
        // Both sign-ins pass the "no contact yet" read, then race to create. The alternate key's unique index
        // admits one; the loser's create-only write gets 412 and it re-reads by oid.
        var gate = new TaskCompletionSource();
        var arrived = 0;
        _store.BeforeCreate = async _ =>
        {
            if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };

        var results = await Task.WhenAll(
            Resolve(WorkforceUser(Caller, CustomerTenant, email: Email)),
            Resolve(WorkforceUser(Caller, CustomerTenant, email: Email)));

        _store.ContactsBoundTo(Caller).Should().ContainSingle();
        results.Select(r => r.Principal!.ContactId).Distinct().Should().ContainSingle(
            "both sign-ins resolve to the one contact");
    }

    [Fact]
    public async Task WithoutTheAlternateKey_CreationDenies_RatherThanRiskTwoContacts()
    {
        _store.KeyDefined = false;

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactCreateUnavailable);
        _store.Contacts.Should().BeEmpty();
    }

    // ── Acceptance 4: collision → deny, no binding, no create, a durable flag, idempotent ───────────

    [Fact]
    public async Task AnEmailMatchBoundToAnotherOid_Denies_FlagsOnce_AndWritesNothingElse()
    {
        _store.AddContact(ContactA, email: Email, oid: Victim.ToString("D"), plane: IdentityPlaneMarker.External);

        var first = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        first.IsResolved.Should().BeFalse();
        first.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid);
        _store.Contacts[ContactA].Oid.Should().Be(Victim.ToString("D"), "no binding is written");
        _store.Contacts.Should().ContainSingle("no contact is created");
        var flag = _store.Contacts[ContactA].Flag!;
        flag.CollidingOid.Should().Be(Caller);
        flag.CollidingPlane.Should().Be(IdentityPlaneMarker.Workforce);
        flag.Reason.Should().Be(IdentityCollisionReason.BoundToDifferentOid);
        flag.FlaggedOn.Should().Be(Now);

        // The same identity retries: still denied, and NO second flag write.
        await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));
        _store.Writes.Should().ContainSingle(w => w.Op == "flag",
            "a caller retrying a denied sign-in must not turn the deny path into a stream of writes");
    }

    // ── Acceptance 5: ambiguity — email or oid — denies and flags; no first-row pick, no create ──────

    [Fact]
    public async Task TwoActiveContactsCarryingTheEmail_Deny_AndBothAreFlagged()
    {
        _store.AddContact(ContactA, email: Email);
        _store.AddContact(ContactB, email: Email);

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactEmailAmbiguous);
        _store.Writes.Where(w => w.Op == "flag").Select(w => w.RowId).Should().BeEquivalentTo(new Guid?[] { ContactA, ContactB });
        _store.Writes.Should().NotContain(w => w.Op == "bind" || w.Op == "create");
    }

    [Fact]
    public async Task TwoContactsCarryingTheOid_Deny_AndBothAreFlagged()
    {
        _store.KeyDefined = false; // only possible without the key — the case the key exists to prevent
        _store.AddContact(ContactA, oid: Caller.ToString("D"), plane: IdentityPlaneMarker.Workforce);
        _store.AddContact(ContactB, oid: Caller.ToString().ToUpperInvariant(), plane: IdentityPlaneMarker.Workforce);

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactOidAmbiguous);
        _store.Writes.Count(w => w.Op == "flag").Should().Be(2);
    }

    // ── Acceptance 6 & 7: who may bind or create ─────────────────────────────────────────────────────

    public static IEnumerable<object[]> NonMembers()
    {
        yield return new object[] { "guest (acct=1)", WorkforceUser(Caller, CustomerTenant, acct: "1", email: Email), WorkforceMembershipTest.DenyGuest };
        yield return new object[] { "foreign tenant", WorkforceUser(Caller, SpaarkeTenant, email: Email), WorkforceMembershipTest.DenyForeignTenant };
        yield return new object[] { "no acct claim", WorkforceUser(Caller, CustomerTenant, acct: null, email: Email), WorkforceMembershipTest.DenyAcctMissing };
        yield return new object[] { "app-only, no idtyp", AppOnlyToken(Caller, CustomerTenant), WorkforceMembershipTest.DenyNotUserToken };
    }

    [Theory]
    [MemberData(nameof(NonMembers))]
    public async Task ANonMember_GetsNoEmailBind_AndNoCreation_AndAnOwnDenyCode(string who, ClaimsPrincipal token, string code)
    {
        _store.AddContact(ContactA, email: Email); // an unbound contact a member WOULD bind

        var result = await Resolve(token);

        result.IsResolved.Should().BeFalse(who);
        result.DenyCode.Should().Be(code);
        _store.Writes.Should().BeEmpty($"a {who} caller never binds or creates");
    }

    [Theory]
    [MemberData(nameof(NonMembers))]
    public async Task ANonMember_StillResolvesThroughAnExistingOidBinding(string who, ClaimsPrincipal token, string _)
    {
        _store.AddContact(ContactA, oid: Caller.ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var result = await Resolve(token);

        result.Principal!.ContactId.Should().Be(ContactA, $"a {who} caller resolves only through an existing oid binding");
    }

    [Fact]
    public async Task Model1_CustomerTenantT_IsServed_AndTheRegistrationsOwnTenantS_IsNot()
    {
        // AzureAd:TenantId = S is irrelevant: the binder reads only WorkforceIdentity:CustomerTenantIds = [T].
        var fromS = await Resolve(WorkforceUser(Caller, SpaarkeTenant, email: Email));
        fromS.DenyCode.Should().Be(WorkforceMembershipTest.DenyForeignTenant);
        _store.Contacts.Should().BeEmpty();

        var fromT = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));
        fromT.IsResolved.Should().BeTrue();
    }

    [Fact]
    public async Task AnEmptyCustomerTenantSetting_BindsAndCreatesForNobody()
    {
        _store.AddContact(ContactA, email: Email);
        var binder = new ContactIdentityBinder(_store, Tenants(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now), _log);

        var result = await Resolver(binder).ResolveAsync(WorkforceUser(Caller, CustomerTenant, email: Email), CancellationToken.None);

        result.DenyCode.Should().Be(WorkforceMembershipTest.DenyTenantListEmpty);
        _store.Writes.Should().BeEmpty();
    }

    // ── Acceptance 8: unreadable and could-not-read deny, and never fall through ─────────────────────

    [Fact]
    public async Task AFailedOidRead_Denies_AndNeverFallsThroughToTheEmailPathOrCreation()
    {
        _store.OidLookupStatus = LookupStatus.Failed;
        _store.AddContact(ContactA, email: Email);

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        _store.Reads.Should().NotContain("email");
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedEmailRead_Denies_AndNeverCreates()
    {
        _store.EmailLookupStatus = LookupStatus.Failed;

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        _store.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task AMalformedOrAllZeroBinding_Denies_AndIsNeverHandedOver(string raw)
    {
        _store.AddContact(ContactA, email: Email, oid: raw, plane: IdentityPlaneMarker.External);

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBindingUnreadable);
        _store.Writes.Should().NotContain(w => w.Op == "bind" || w.Op == "create");
    }

    [Fact]
    public async Task AMissingBindingColumn_DeniesWithItsOwnCode_AndAnErrorLog()
    {
        _store.OidLookupStatus = LookupStatus.ColumnMissing;

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.DenyCode.Should().Be(ContactBindingDecision.DenyBindingColumnMissing);
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Error
            && e.Message.Contains(ContactBindingDecision.DenyBindingColumnMissing),
            "a missing binding column is an environment fault an operator must see — not a silent null");
    }

    [Fact]
    public async Task AMaskedBindingColumn_WritesNothing()
    {
        // FLS without Read: every bound contact reads as unbound — the guard's one fail-OPEN premise. The
        // marker survives the mask, so the probe sees it and the binder refuses to write blind.
        _store.AddContact(ContactB, email: "someone@customer.example", oid: Victim.ToString("D"), plane: IdentityPlaneMarker.External);
        _store.MaskBindingColumn = true;

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(ContactBindingDecision.DenyBindingColumnMasked);
        _store.Writes.Should().BeEmpty();
    }

    // ── Acceptance 9: an inactive oid contact denies; nothing is re-created ──────────────────────────

    [Fact]
    public async Task AnOidOnlyOnAnInactiveContact_DeniesWithItsOwnCode_AndNothingIsReCreated()
    {
        _store.AddContact(ContactA, oid: Caller.ToString("D"), plane: IdentityPlaneMarker.Workforce, stateCode: 1);
        _store.AddContact(ContactB, email: Email);

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactInactive);
        _store.Writes.Should().BeEmpty("deactivating a contact is how an operator removes a person");
    }

    // ── The resolver carries the decision's own code ─────────────────────────────────────────────────

    [Fact]
    public async Task ADeniedBinding_IsAnExplicitDeny_WithNoPartialPrincipal()
    {
        _store.AddContact(ContactA, email: Email, oid: Victim.ToString("D"));

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.Principal.Should().BeNull("no partial principal — no grants inherited");
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
    }

    [Fact]
    public async Task WhenTheCallerActuallyCancelled_ItPropagatesRatherThanDenying()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => Resolver(Binder(_store, _log)).ResolveAsync(WorkforceUser(Caller, CustomerTenant), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Acceptance 10: a licensed user's inline link takes effect on the SAME request ─────────────────

    [Fact]
    public async Task ASystemUserWithNoLink_IsLinkedInline_AndTheContactIsOnThisRequestsPrincipal()
    {
        var suid = Guid.NewGuid();
        _store.AddSystemUser(suid, Caller, Email);
        var identity = new Mock<IIdentityNormalizationService>(MockBehavior.Strict);
        identity.Setup(x => x.ResolveAsync(suid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(suid, ContactId: null));
        identity.Setup(x => x.InvalidateAsync(suid, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await Resolver(Binder(_store, _log), identity.Object, systemUserId: suid)
            .ResolveAsync(WorkforceUser(Caller, CustomerTenant, email: Email), CancellationToken.None);

        var created = _store.ContactsBoundTo(Caller).Should().ContainSingle().Subject;
        result.Principal!.ContactId.Should().Be(created.Id, "no 10-minute wait for the identity cache");
        _store.SystemUsers[suid].PrimaryContactId.Should().Be(created.Id);
        identity.Verify(x => x.InvalidateAsync(suid, It.IsAny<CancellationToken>()), Times.Once,
            "the cached identity is replaced so every later read on this request sees the link");
    }

    [Fact]
    public async Task ASystemUserWhoseLinkCollides_StaysASystemUserPrincipal_AndIsNotReAttemptedEveryRequest()
    {
        var suid = Guid.NewGuid();
        _store.AddSystemUser(suid, Caller, Email);
        _store.AddContact(ContactA, email: Email, oid: Victim.ToString("D"));
        var identity = new Mock<IIdentityNormalizationService>();
        identity.Setup(x => x.ResolveAsync(suid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(suid, ContactId: null));
        var cache = new DictionaryTenantCache();
        var resolver = Resolver(Binder(_store, _log), identity.Object, systemUserId: suid, cache: cache);

        var first = await resolver.ResolveAsync(WorkforceUser(Caller, CustomerTenant, email: Email), CancellationToken.None);
        _store.Reads.Clear();
        var second = await resolver.ResolveAsync(WorkforceUser(Caller, CustomerTenant, email: Email), CancellationToken.None);

        first.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
        first.Principal.ContactId.Should().BeNull();
        second.Principal!.ContactId.Should().BeNull();
        _store.Reads.Should().BeEmpty("a recent failed link attempt is remembered for the identity cache's lifetime");
        _store.Writes.Should().ContainSingle(w => w.Op == "flag");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private Task<WorkforcePrincipalResolution> Resolve(ClaimsPrincipal user)
        => Resolver(Binder(_store, _log)).ResolveAsync(user, CancellationToken.None);

    private static WorkforcePrincipalResolver Resolver(
        ContactIdentityBinder binder, IIdentityNormalizationService? identity = null, Guid? systemUserId = null,
        ITenantCache? cache = null)
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var c = new EntityCollection();
                if (systemUserId is { } id) c.Entities.Add(new Entity("systemuser") { Id = id });
                return c;
            });

        return new WorkforcePrincipalResolver(
            identity ?? new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object,
            dataverse.Object,
            cache ?? new DictionaryTenantCache(),
            binder,
            NullLogger<WorkforcePrincipalResolver>.Instance);
    }

    /// <summary>A real (in-process) cache, so a cached value genuinely short-circuits a second request.</summary>
    private sealed class DictionaryTenantCache : ITenantCache
    {
        private readonly Dictionary<string, object?> _store = new();

        private static string Key(string t, string r, string id, int v) => $"{t}:{r}:{id}:{v}";

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(Key(tenantId, resource, id, version), out var v) ? (T?)v : default);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            _store[Key(tenantId, resource, id, version)] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
        {
            _store.Remove(Key(tenantId, resource, id, version));
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
            => await factory(ct);
    }
}
