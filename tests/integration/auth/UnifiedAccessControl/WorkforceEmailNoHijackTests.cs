using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
/// every row for <c>If-Match</c>, and enforces the alternate key where owner round 4 item 4 (B2) put it: on the
/// UNSECURED uniqueness mirror <c>sprk_externalobjectidkey</c>, while field-level security stays on the binding
/// <c>sprk_externalobjectid</c> — a combination Dataverse allows. Every test reads what the code DID
/// (<see cref="InMemoryContactIdentityStore.Writes"/>), not only what it returned.</para>
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
        created.KeyMirror.Should().Be(Caller.ToString("D"), "the binding and its uniqueness mirror land in the same request");
        created.Email.Should().Be(Email);
        (created.FirstName, created.LastName).Should().Be(("Ada", "Lovelace"));
        _store.Writes.Should().ContainSingle(w => w.Op == "create",
            "creating a contact is the ONLY write — it carries no grant; access still comes from Dataverse or explicit grants");
    }

    [Fact]
    public async Task TwoConcurrentFirstSignIns_StillLeaveExactlyOneContactForTheOid()
    {
        // Both sign-ins pass the "no contact yet" read, then race to create. The alternate key's unique index —
        // on the unsecured uniqueness mirror, owner round 4 item 4 (B2) — admits one; the loser's create-only write
        // gets 412 and it re-reads by the (field-secured) binding, which the winner wrote in the same request.
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
        _store.Contacts.Values.Count(c => c.KeyMirror == Caller.ToString("D")).Should().Be(1);
        _store.Writes.Count(w => w.Op == "create").Should().Be(1, "the loser's create was refused by the index");
        results.Select(r => r.Principal!.ContactId).Distinct().Should().ContainSingle(
            "both sign-ins resolve to the one contact");
    }

    // ── B2 (owner round 4 item 4): a squatted uniqueness mirror DENIES — it never binds or creates ──────
    //
    // The mirror is unsecured, so any user with contact Write can put an oid into it. The index then refuses that
    // oid everywhere else. These pin what that buys the squatter: a visible denial of service, never a binding.

    [Fact]
    public async Task ASquattedMirror_DeniesTheCreate_CreatesNothing_AndFlagsTheHolder()
    {
        // ContactB holds the caller's oid in its mirror only — its binding (the secured column) is empty.
        _store.AddContact(ContactB, email: "squatter@customer.example", keyMirror: Caller.ToString("D"));

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactKeyConflict);
        _store.Contacts.Should().ContainSingle("no contact is created past the index");
        _store.Contacts[ContactB].Oid.Should().BeNull("the holder is never bound by the conflict");
        var flag = _store.Contacts[ContactB].Flag!;
        flag.Reason.Should().Be(IdentityCollisionReason.KeyMirrorConflict);
        (flag.CollidingOid, flag.CollidingPlane).Should().Be((Caller, IdentityPlaneMarker.Workforce));
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Error
            && e.Message.Contains(ContactBindingDecision.DenyContactKeyConflict) && e.Message.Contains(ContactB.ToString()),
            "the operator is told which contact holds the oid");
    }

    [Fact]
    public async Task ASquattedMirror_DeniesTheEmailBind_TheMatchedContactStaysUnbound()
    {
        _store.AddContact(ContactA, email: Email);                                     // the bind target
        _store.AddContact(ContactB, email: "squatter@customer.example", keyMirror: Caller.ToString("D"));

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.IsResolved.Should().BeFalse("a refused bind is never 'resolved anyway'");
        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactKeyConflict);
        (_store.Contacts[ContactA].Oid, _store.Contacts[ContactA].KeyMirror).Should().Be(((string?)null, (string?)null),
            "the index refused the whole write: neither the binding nor the mirror landed");
        _store.Writes.Should().NotContain(w => w.Op == "bind" || w.Op == "create");
        _store.Contacts[ContactB].Flag!.Reason.Should().Be(IdentityCollisionReason.KeyMirrorConflict);
    }

    [Fact]
    public async Task ASquattedMirror_RetriedSignIn_WritesNoSecondFlag()
    {
        _store.AddContact(ContactB, email: "squatter@customer.example", keyMirror: Caller.ToString().ToUpperInvariant());

        await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));
        var again = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        again.DenyCode.Should().Be(ContactBindingDecision.DenyContactKeyConflict,
            "the index compares case-insensitively, as the stored mirror does");
        _store.Writes.Should().ContainSingle(w => w.Op == "flag", "the same identity's conflict is recorded once");
    }

    [Fact]
    public async Task AMirrorThatMatchesItsOwnBinding_IsNotASquat_TheCallerResolvesByOid()
    {
        // Schema step (a) copies every existing binding into the mirror; that contact IS the caller's.
        _store.AddContact(ContactA, email: "x@customer.example", oid: Caller.ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.Principal!.ContactId.Should().Be(ContactA);
        _store.Writes.Should().BeEmpty();
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

    [Fact]
    public async Task ASecondIdentityCollidingWithAFlaggedContact_IsRecorded_WithoutOverwritingAConcurrentParty()
    {
        // Verifier finding 3: the contact already carries another identity's flag. This caller's collision is
        // ADDED (not swallowed), and a party a concurrent writer appended between our read and our write
        // survives: the append is conditional on the row version, and a 412 re-reads and appends again.
        var first = new CollisionParty(Victim, IdentityPlaneMarker.Workforce, IdentityCollisionReason.LinkedToOtherUser, Now.AddDays(-1));
        _store.AddContact(ContactA, email: Email, oid: Victim.ToString("D"), plane: IdentityPlaneMarker.External,
            flag: ContactBindingDecision.FlagWith(null, first));
        var concurrent = new CollisionParty(Guid.NewGuid(), IdentityPlaneMarker.External, IdentityCollisionReason.EmailAmbiguous, Now);
        var fired = false;
        _store.BeforeFlagWrite = contact =>
        {
            if (fired) return;
            fired = true;
            contact.Flag = ContactBindingDecision.FlagWith(contact.Flag, concurrent);
            _store.Touch(contact);
        };

        var result = await Resolve(WorkforceUser(Caller, CustomerTenant, email: Email));

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid);
        var flag = _store.Contacts[ContactA].Flag!;
        flag.Parties.Should().HaveCount(3);
        flag.Records(first).Should().BeTrue();
        flag.Records(concurrent).Should().BeTrue("a party appended meanwhile is never overwritten");
        flag.Parties.Should().Contain(p => p.Oid == Caller && p.Reason == IdentityCollisionReason.BoundToDifferentOid);
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
        // Only possible when a binding has no mirror (one written by hand by a holder of the writer profile, or
        // before the key existed) — the index guards the mirror, so the second binding slipped past it.
        _store.AddContact(ContactA, oid: Caller.ToString("D"), plane: IdentityPlaneMarker.Workforce);
        _store.AddContact(ContactB, oid: Caller.ToString().ToUpperInvariant(), plane: IdentityPlaneMarker.Workforce,
            deriveKeyMirror: false);

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

        // Task 137 (C5) verification of this rule: with an ACTIVE contact (B) sharing the caller's email, the oid match
        // on the inactive contact ends the decision — the email path is never even read, so B can never be bound.
        _store.Reads.Should().NotContain("email", "an oid bound to an inactive contact never falls through to the email path");
        _store.Contacts[ContactB].Oid.Should().BeNull();
        result.Principal.Should().BeNull("a deny carries no partial principal");
    }

    /// <summary>
    /// Task 137 criterion 1 on the WORKFORCE plane, at the HTTP boundary: the real resolver's inactive-contact deny,
    /// carried through the real <see cref="WorkforcePrincipalStrategy"/>, is a 403 ProblemDetails naming
    /// <c>sdap.access.deny.contact_inactive</c> — not the generic <c>principal_not_resolved</c> — and the evaluator
    /// is never asked to compose anything for the caller.
    /// </summary>
    [Fact]
    public async Task AnOidOnlyOnAnInactiveContact_ThroughTheWorkforceStrategy_Is403WithTheContactInactiveCode()
    {
        _store.AddContact(ContactA, oid: Caller.ToString("D"), plane: IdentityPlaneMarker.Workforce, stateCode: 1);

        // Strict + no setups: a denied caller must never reach the evaluator.
        var evaluator = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var strategy = new WorkforcePrincipalStrategy(
            Resolver(Binder(_store, _log)), evaluator.Object, NullLogger<WorkforcePrincipalStrategy>.Instance);
        var http = new DefaultHttpContext
        {
            User = WorkforceUser(Caller, CustomerTenant, email: Email),
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();

        var result = await strategy.ResolveAsync(http, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        await result.Failure!.ExecuteAsync(http);
        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        body.Should().Contain("sdap.access.deny.contact_inactive");
        body.Should().NotContain(WorkforcePrincipalResolver.DenyPrincipalNotResolved);
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
    public async Task BeforeTheOwnerEnablesLinkWrites_ASignInLinksNothing_ExactlyLikeTheReportOnlyJob()
    {
        // Verifier finding 4: the inline link is the same R1-class write the job's report-only switch stages
        // (linking a systemuser to an existing contact hands that user its grants). Ungated, every licensed
        // sign-in after the deploy would write ahead of the report-only review.
        var suid = Guid.NewGuid();
        _store.AddSystemUser(suid, Caller, Email);
        _store.AddContact(ContactA, email: Email); // an unbound contact the link WOULD bind and hand over
        var identity = new Mock<IIdentityNormalizationService>(MockBehavior.Strict);
        identity.Setup(x => x.ResolveAsync(suid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(suid, ContactId: null));

        var result = await Resolver(Binder(_store, _log), identity.Object, systemUserId: suid, linkWrites: false)
            .ResolveAsync(WorkforceUser(Caller, CustomerTenant, email: Email), CancellationToken.None);

        result.Principal!.Kind.Should().Be(WorkforcePrincipalKind.SystemUser);
        result.Principal.ContactId.Should().BeNull("no link exists and none may be written yet");
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().BeEmpty("a gated inline link does not even pay for the decision's reads");
        _store.Contacts[ContactA].Oid.Should().BeNull();
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
        ITenantCache? cache = null, bool linkWrites = true)
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

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ContactIdentityBinder.LinkWritesEnabledConfigKey] = linkWrites ? "true" : null,
            })
            .Build();

        return new WorkforcePrincipalResolver(
            identity ?? new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object,
            dataverse.Object,
            cache ?? new DictionaryTenantCache(),
            binder,
            configuration,
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
