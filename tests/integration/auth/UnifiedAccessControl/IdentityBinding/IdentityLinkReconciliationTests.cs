using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// unified-access-control-r2 task 141 — the identity-link reconciliation job, over a row set shaped like
/// spaarkedev1 on 2026-09-30 (re-verified read-only at step 0).
/// </summary>
/// <remarks>
/// <para>The row store applies writes back into itself, so the second run sees what the first run wrote — which
/// is what makes "a second run changes nothing" a real assertion. The decision itself is
/// <see cref="ContactBindingDecision"/>'s (tested row by row elsewhere); these tests pin what the JOB adds: the
/// safety convention (report-only by default, a before-state line per row, a failed scan is a failed run), the
/// never-re-point rule against a live-shaped dataset, flag idempotence, and flag clearing.</para>
/// </remarks>
public class IdentityLinkReconciliationTests
{
    // spaarkedev1, 2026-09-30 (oids abbreviated into valid GUIDs; shapes exact).
    private static readonly Guid RalphOid = Guid.Parse("c74ac1af-ff3b-46fb-83e7-3063616e959c");
    private static readonly Guid EyalOid = Guid.Parse("33b58f6c-befd-4846-8e6d-fa2bf95a26e4");
    private static readonly Guid HotmailGuestOid = Guid.Parse("ad268fcd-ac34-4e40-b63f-dacdc849fcbb");
    private static readonly Guid TestUser1Oid = Guid.Parse("bcde7809-9b31-4744-933c-766a600199f6");

    private static readonly Guid RalphUser = Guid.Parse("1d02f31c-1872-f011-b4cb-7c1e52671ad0");
    private static readonly Guid EyalUser = Guid.Parse("b0cd2d30-eeef-f011-8406-7ced8d1dc988");
    private static readonly Guid HotmailUser = Guid.Parse("e44317a0-bb2e-f111-88b5-7ced8d1dc988");
    private static readonly Guid TestUser1 = Guid.Parse("8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c");

    private static readonly Guid RalphContact = Guid.Parse("8e9918a9-9021-f111-88b5-7c1e520aa4df");
    private static readonly Guid EyalContact = Guid.Parse("8cb95c16-e974-f111-ab0e-7ced8ddc4a05");
    private static readonly Guid HotmailContact = Guid.Parse("2e419a4f-010d-f111-8342-7ced8d1dc988");
    private static readonly Guid TestUser1Contact = Guid.Parse("ac6d7b68-fd21-f111-88b5-7ced8d1dc988");

    private static readonly string[] CreateEmails =
    {
        "final.test@demo.spaarke.com", "demo@demo.spaarke.com", "jake.schroeder@demo.spaarke.com", "e2e.test2@demo.spaarke.com",
    };

    private readonly InMemoryContactIdentityStore _store = new();
    private readonly BindingLogCapture<IdentityLinkReconciliationJob> _log = new();

    public IdentityLinkReconciliationTests()
    {
        // The three collisions — CIAM identities were invited onto internal addresses (test artefacts).
        _store.AddContact(RalphContact, "ralph.schroeder@spaarke.com", "6a9fa229-7033-4e7c-abb5-0a8439cdd068", IdentityPlaneMarker.External);
        _store.AddContact(EyalContact, "eyal.iffergan@spaarke.com", "6b917a49-feeb-48d6-ad9c-29bad951ad17", IdentityPlaneMarker.External);
        _store.AddContact(HotmailContact, "ralph.schroeder@hotmail.com", "06646385-cbbc-4321-a458-f631e0096328", IdentityPlaneMarker.External);
        // The bind candidate.
        _store.AddContact(TestUser1Contact, "testuser1@spaarke.com");

        _store.AddSystemUser(RalphUser, RalphOid, "ralph.schroeder@spaarke.com", primaryContactId: RalphContact);
        _store.AddSystemUser(EyalUser, EyalOid, "eyal.iffergan@spaarke.com");
        _store.AddSystemUser(HotmailUser, HotmailGuestOid, "ralph.schroeder@hotmail.com",
            domainName: "ralph.schroeder_hotmail.com#EXT#@spaarke.onmicrosoft.com");
        _store.AddSystemUser(TestUser1, TestUser1Oid, "testuser1@spaarke.com");
        foreach (var email in CreateEmails)
        {
            _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), email);
        }

        // Never processed: disabled, an application user, a non-interactive user.
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "gone@spaarke.com", disabled: true);
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "# bff app", applicationId: Guid.NewGuid());
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "integration@spaarke.com", accessMode: 4);
    }

    // ── The dev-shaped run ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteMode_LinksTheCandidate_CreatesTheMissing_FlagsTheThreeCollisions_AndKeepsRalphsLink()
    {
        var result = await RunAsync(writes: true);

        // The bind candidate: bound by email, then linked.
        _store.Contacts[TestUser1Contact].Oid.Should().Be(TestUser1Oid.ToString("D"));
        _store.SystemUsers[TestUser1].PrimaryContactId.Should().Be(TestUser1Contact);

        // The four with no contact: exactly one contact each, keyed by their oid, and linked.
        foreach (var user in _store.SystemUsers.Values.Where(u => CreateEmails.Contains(u.Email)))
        {
            var created = _store.ContactsBoundTo(user.Oid!.Value).Should().ContainSingle().Subject;
            user.PrimaryContactId.Should().Be(created.Id);
            created.Plane.Should().Be((int)IdentityPlaneMarker.Workforce);
        }

        // The three collisions: flagged, nothing bound or created for them.
        _store.Contacts[RalphContact].Flag!.Reason.Should().Be(IdentityCollisionReason.LinkedContactBoundToDifferentOid);
        _store.Contacts[EyalContact].Flag!.Reason.Should().Be(IdentityCollisionReason.BoundToDifferentOid);
        _store.Contacts[HotmailContact].Flag!.Reason.Should().Be(IdentityCollisionReason.BoundToDifferentOid);
        _store.Contacts[HotmailContact].Flag!.CollidingOid.Should().Be(HotmailGuestOid);

        // Owner decision (a): Ralph's existing link is untouched, and so are the CIAM bindings.
        _store.SystemUsers[RalphUser].PrimaryContactId.Should().Be(RalphContact);
        _store.Contacts[RalphContact].Oid.Should().Be("6a9fa229-7033-4e7c-abb5-0a8439cdd068");
        _store.SystemUsers[EyalUser].PrimaryContactId.Should().BeNull();
        _store.SystemUsers[HotmailUser].PrimaryContactId.Should().BeNull("a guest is never email-bound");

        result.Success.Should().BeTrue(result.ErrorMessage);
        Outcome(result, "BoundAndLinked").Should().Be(1);
        Outcome(result, "CreatedAndLinked").Should().Be(4);
        Outcome(result, "Flagged").Should().Be(3);
        UsersScanned(result).Should().Be(8, "disabled, application and non-interactive users are not processed");
    }

    [Fact]
    public async Task ASecondRun_ChangesNothing()
    {
        await RunAsync(writes: true);
        var writesAfterFirst = _store.Writes.Count;

        var second = await RunAsync(writes: true);

        _store.Writes.Count.Should().Be(writesAfterFirst, "every row is now verified or already flagged");
        Outcome(second, "Verified").Should().Be(5);
        Outcome(second, "FlagAlreadyPresent").Should().Be(3);
        second.ProcessedItems.Should().Be(0);
    }

    [Fact]
    public async Task ReportOnly_IsTheDefault_WritesNothing_AndLogsABeforeStateForEveryRowItWouldChange()
    {
        var result = await RunAsync(writes: null);

        _store.Writes.Should().BeEmpty();
        Mode(result).Should().Be("report-only");
        var beforeStates = _log.Messages.Where(m => m.Contains("before-state")).ToList();
        beforeStates.Should().Contain(m => m.Contains(TestUser1Contact.ToString()) && m.Contains("sprk_externalobjectid=(null)"));
        beforeStates.Count(m => m.Contains("create contact")).Should().Be(4);
        beforeStates.Count(m => m.Contains("flag=(none)")).Should().Be(3);
        beforeStates.Should().AllSatisfy(m => m.Should().Contain("mode=report-only"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    public async Task AnUnparseableSwitch_IsReportOnly(string value)
    {
        await RunAsync(writes: null, rawSwitch: value);
        _store.Writes.Should().BeEmpty();
    }

    // ── Flags ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFlagWhoseCollisionAnOperatorResolved_IsClearedByTheNextRun_AndTheUserIsLinked()
    {
        await RunAsync(writes: true);

        // The documented operator resolution for Eyal: the CIAM identity loses this contact (binding, mirror and
        // plane cleared — guide §6.5.3).
        _store.Contacts[EyalContact].Oid = null;
        _store.Contacts[EyalContact].KeyMirror = null;
        _store.Contacts[EyalContact].Plane = null;

        var next = await RunAsync(writes: true);

        _store.Contacts[EyalContact].Flag.Should().BeNull("the collision no longer holds");
        _store.Contacts[EyalContact].Oid.Should().Be(EyalOid.ToString("D"));
        _store.SystemUsers[EyalUser].PrimaryContactId.Should().Be(EyalContact);
        _store.Contacts[RalphContact].Flag.Should().NotBeNull("Ralph's collision still holds");
        next.Success.Should().BeTrue(next.ErrorMessage);
    }

    /// <summary>
    /// B2 (owner round 4 item 4): a licensed user whose oid another contact holds in the unsecured uniqueness mirror.
    /// The index refuses the create; the job never creates around it — it flags the holder, keeps that flag while
    /// the slot is held (even if the holder is deactivated, which does not free the index), and links the user on
    /// the run after an operator clears the mirror.
    /// </summary>
    [Fact]
    public async Task ASquattedMirror_IsFlaggedNotCreatedAround_AndTheUserIsLinkedOnceTheOperatorClearsIt()
    {
        var victimOid = Guid.Parse("5a5a5a5a-0000-4000-8000-0000000000a1");
        var victim = Guid.NewGuid();
        var holder = Guid.NewGuid();
        _store.AddSystemUser(victim, victimOid, "squat.victim@demo.spaarke.com");
        _store.AddContact(holder, "holder@elsewhere.example", keyMirror: victimOid.ToString("D"));

        var first = await RunAsync(writes: true);

        _store.ContactsBoundTo(victimOid).Should().BeEmpty("nothing is created past the unique index");
        _store.SystemUsers[victim].PrimaryContactId.Should().BeNull();
        _store.Contacts[holder].Flag!.Reason.Should().Be(IdentityCollisionReason.KeyMirrorConflict);
        _store.Contacts[holder].Flag!.CollidingOid.Should().Be(victimOid);
        Outcome(first, "Flagged").Should().Be(4, "the three dev collisions plus the squat");

        // Deactivating the holder does not free the index slot: the flag stays and nothing is created.
        _store.Contacts[holder].StateCode = 1;
        var second = await RunAsync(writes: true);
        _store.Contacts[holder].Flag.Should().NotBeNull("an inactive holder still holds the slot");
        Outcome(second, "FlagAlreadyPresent").Should().Be(4);

        // The documented resolution: clear the holder's mirror.
        _store.Contacts[holder].KeyMirror = null;
        var third = await RunAsync(writes: true);

        var created = _store.ContactsBoundTo(victimOid).Should().ContainSingle().Subject;
        _store.SystemUsers[victim].PrimaryContactId.Should().Be(created.Id);
        _store.Contacts[holder].Flag.Should().BeNull("the conflict no longer holds, so the job clears the flag");
        third.Success.Should().BeTrue(third.ErrorMessage);
    }

    [Fact]
    public async Task AReportOnlyRun_NeverReportsAHeldKeyMirrorFlagAsClearable()
    {
        // A report-only pass 1 attempts no write, so it cannot SEE the key conflict: it "would create" the user.
        // The holder's flag must still be judged from the holder row — or the review run would promise to clear it.
        var victimOid = Guid.Parse("5a5a5a5a-0000-4000-8000-0000000000b2");
        var holder = Guid.NewGuid();
        _store.AddSystemUser(Guid.NewGuid(), victimOid, "squat.victim2@demo.spaarke.com");
        _store.AddContact(holder, "holder2@elsewhere.example", keyMirror: victimOid.ToString("D"),
            flag: ContactBindingDecision.FlagWith(null, new CollisionParty(victimOid, IdentityPlaneMarker.Workforce,
                IdentityCollisionReason.KeyMirrorConflict, Now.AddDays(-1))));

        var result = await RunAsync(writes: null);

        FlagCount(result, "cleared").Should().Be(0, "the slot is still held");
        _log.Messages.Should().NotContain(m => m.Contains("before-state") && m.Contains(holder.ToString()) && m.Contains("flag=(none)"));
    }

    [Fact]
    public async Task AFlagRaisedByAType2SignIn_IsReEvaluatedFromData_AndClearedOnlyWhenResolved()
    {
        var type2Oid = Guid.NewGuid();
        var contact = Guid.NewGuid();
        _store.AddContact(contact, "type2@customer.example", Guid.NewGuid().ToString("D"), IdentityPlaneMarker.External,
            flag: Flag(IdentityCollisionReason.BoundToDifferentOid, type2Oid));

        await RunAsync(writes: true);
        _store.Contacts[contact].Flag.Should().NotBeNull("still bound to the other identity");

        _store.Contacts[contact].Oid = null;
        _store.Contacts[contact].Plane = null;
        await RunAsync(writes: true);
        _store.Contacts[contact].Flag.Should().BeNull();
    }

    [Fact]
    public async Task AFlagWhoseUserCouldNotBeDecidedThisRun_IsKept_NeverClearedOnAMissingDecision()
    {
        // A guest linked to an unbound contact: GuestLinkUnverified, re-decided per systemuser in pass 1.
        var guestOid = Guid.NewGuid();
        var guestContact = Guid.NewGuid();
        _store.AddContact(guestContact, "guest.partner@other.example");
        _store.AddSystemUser(Guid.NewGuid(), guestOid, "guest.partner@other.example", primaryContactId: guestContact,
            domainName: "guest.partner_other.example#EXT#@spaarke.onmicrosoft.com");
        await RunAsync(writes: true);
        _store.Contacts[guestContact].Flag!.Reason.Should().Be(IdentityCollisionReason.GuestLinkUnverified);

        // The next run cannot finish THAT user's decision (a transient read failure). Pass 2 must not read the
        // missing decision as "the collision's party is gone".
        _store.FailOidLookupOnceFor.Add(guestOid);
        await RunAsync(writes: true);

        _store.Contacts[guestContact].Flag.Should().NotBeNull("no decision is not evidence that the collision was resolved");
        _store.Writes.Should().NotContain(w => w.Op == "clear" && w.RowId == guestContact);
    }

    // ── Flags with more than one party (verifier finding 3) ──────────────────────────────────────────

    private const string SharedEmail = "shared.inbox@customer.example";

    [Fact]
    public async Task ASecondSystemUsersLiveCollision_SurvivesTheFirstOnesResolution()
    {
        // Contact C belongs to a CIAM identity; two licensed users carry its email, so BOTH collide with it.
        var c = Guid.NewGuid();
        _store.AddContact(c, SharedEmail, Guid.NewGuid().ToString("D"), IdentityPlaneMarker.External);
        var userA = Guid.NewGuid();
        var oidB = Guid.NewGuid();
        _store.AddSystemUser(userA, Guid.NewGuid(), SharedEmail);
        _store.AddSystemUser(Guid.NewGuid(), oidB, SharedEmail);

        await RunAsync(writes: true);
        _store.Contacts[c].Flag!.Parties.Should().HaveCount(2, "each identity's collision is recorded");

        // The operator resolves A's collision only (A's directory email is corrected). B still collides.
        _store.SystemUsers[userA].Email = "a.person@customer.example";
        var next = await RunAsync(writes: true);

        var flag = _store.Contacts[c].Flag;
        flag.Should().NotBeNull("B's collision with the same contact still holds");
        flag!.Parties.Should().ContainSingle().Which.Oid.Should().Be(oidB);
        flag.CollidingOid.Should().Be(oidB, "the operator's view now names the party that still collides");
        next.Success.Should().BeTrue(next.ErrorMessage);
    }

    [Fact]
    public async Task ATokenPlaneCallersLiveCollision_SurvivesASystemUsersResolution()
    {
        // The case the job can never re-decide: B is a Type-2 employee (token plane, no systemuser). Its collision
        // is known ONLY through the recorded party, so it must not vanish when A's is resolved.
        var c = Guid.NewGuid();
        _store.AddContact(c, SharedEmail, Guid.NewGuid().ToString("D"), IdentityPlaneMarker.External);
        var userA = Guid.NewGuid();
        _store.AddSystemUser(userA, Guid.NewGuid(), SharedEmail);
        await RunAsync(writes: true);

        var oidB = Guid.NewGuid();
        var denied = await Binder(_store).ResolveWorkforceCallerAsync(
            WorkforceUser(oidB, CustomerTenant, email: SharedEmail), oidB, CancellationToken.None);
        denied.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid);
        _store.Contacts[c].Flag!.Parties.Should().HaveCount(2, "a second identity on a flagged contact is recorded, not swallowed");

        _store.SystemUsers[userA].Email = "a.person@customer.example";
        await RunAsync(writes: true);

        var flag = _store.Contacts[c].Flag;
        flag.Should().NotBeNull("the Type-2 caller's collision still holds and nothing else would ever re-raise it");
        flag!.Parties.Should().ContainSingle().Which.Oid.Should().Be(oidB);
    }

    [Fact]
    public async Task AFlagIsKept_WhileAnyDecidedSystemUserCollidesWithTheContact_EvenIfThatPartyIsNotRecorded()
    {
        // Report-only: pass 1 sees B collide with C but records nothing. C's only recorded party (A) is resolved.
        // The live collision the run just saw keeps the flag — "clear only when no (C, *) collided this run".
        var c = Guid.NewGuid();
        var oidA = Guid.NewGuid();
        _store.AddContact(c, SharedEmail, Guid.NewGuid().ToString("D"), IdentityPlaneMarker.External,
            flag: Flag(IdentityCollisionReason.BoundToDifferentOid, oidA));
        _store.AddSystemUser(Guid.NewGuid(), oidA, "a.person@customer.example"); // A no longer matches C
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), SharedEmail);     // B collides with C

        var result = await RunAsync(writes: null);

        FlagCount(result, "cleared").Should().Be(0, "B's collision with C is live");
        FlagCount(result, "kept").Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AnInviteRefusalFlag_IsKept_WhileTheContactStillBelongsToAnEmployee()
    {
        // Verifier finding 2(4): an invite refusal carries no oid, so pass 2 re-evaluates it from data. It must
        // survive the job's 5-minute cycle for as long as the contact is an employee's.
        var employeeOid = Guid.NewGuid();
        var c = Guid.NewGuid();
        _store.AddContact(c, "employee@customer.example", employeeOid.ToString("D"), IdentityPlaneMarker.Workforce,
            flag: new CollisionFlag(Now, null, IdentityPlaneMarker.External, IdentityCollisionReason.InviteMatchesWorkforceContact));
        _store.AddSystemUser(Guid.NewGuid(), employeeOid, "employee@customer.example", primaryContactId: c);

        await RunAsync(writes: true);

        _store.Contacts[c].Flag.Should().NotBeNull("the invite collision is durable until an operator resolves it");
        _store.Writes.Should().NotContain(w => w.Op == "clear" && w.RowId == c);
    }

    [Fact]
    public async Task AFlagClear_IsConditionalOnTheVersionItWasDecidedOn_SoAPartyAppendedMeanwhileSurvives()
    {
        // Pass 2 decides to clear (the recorded party's collision was resolved), but another identity's collision
        // is appended AFTER the flag scan read the row and BEFORE the clear (here: while pass 2 reads the evidence
        // for that row). The clear must fail rather than wipe the new party — so it must be conditional on the
        // version the verdict was made on, not on a fresher read.
        var c = Guid.NewGuid();
        _store.AddContact(c, "resolved@customer.example", oid: null, plane: null,
            flag: Flag(IdentityCollisionReason.BoundToDifferentOid, Guid.NewGuid(), IdentityPlaneMarker.Workforce));
        var late = new CollisionParty(Guid.NewGuid(), IdentityPlaneMarker.External, IdentityCollisionReason.EmailAmbiguous, Now);
        var fired = false;
        _store.AfterRead = op =>
        {
            if (fired || op != "references" || !_store.Reads.Contains("scan-flags")) return;
            fired = true;
            var contact = _store.Contacts[c];
            contact.Flag = ContactBindingDecision.FlagWith(contact.Flag, late);
            _store.Touch(contact);
        };

        var result = await RunAsync(writes: true);

        _store.Contacts[c].Flag.Should().NotBeNull("the party appended after the scan read the row survives");
        _store.Contacts[c].Flag!.Records(late).Should().BeTrue();
        result.Success.Should().BeFalse("a write that did not complete is reported; the next run re-evaluates");
    }

    [Fact]
    public async Task AFlagReadWithoutARowVersion_IsNeverPruned()
    {
        // One party resolved (the contact is unbound), one still holds (two active contacts carry the email), so
        // pass 2 would PRUNE. Without the version the verdict was made on, an unconditional prune could drop a
        // party appended meanwhile — so it is not written at all.
        var c = Guid.NewGuid();
        var resolved = new CollisionParty(Guid.NewGuid(), IdentityPlaneMarker.Workforce, IdentityCollisionReason.BoundToDifferentOid, Now);
        var holding = new CollisionParty(Guid.NewGuid(), IdentityPlaneMarker.Workforce, IdentityCollisionReason.EmailAmbiguous, Now);
        _store.AddContact(c, "dup@customer.example", flag: CollisionFlag.FromParties(new[] { resolved, holding }));
        _store.AddContact(Guid.NewGuid(), "dup@customer.example");
        _store.ScanFlaggedWithoutETags = true;

        var result = await RunAsync(writes: true);

        _store.Contacts[c].Flag!.Parties.Should().HaveCount(2, "nothing is rewritten without a row version");
        _store.Writes.Should().NotContain(w => w.Op == "flag" && w.RowId == c);
        result.Success.Should().BeFalse("the write that could not be made is reported; the next run re-evaluates");
    }

    // ── Pass 2 never runs on a partial view (verifier finding 2(1), 2(2)) ────────────────────────────

    [Fact]
    public async Task ATruncatedScan_SkipsFlagClearing_SoAFlagOfAnUnscannedUserSurvives()
    {
        // A guest whose systemuser sorts LAST, so a truncated scan never reaches it. Its flag reason is one pass 2
        // can only evaluate from that user's own decision; evaluated without it, the flag would be cleared.
        var guestOid = Guid.NewGuid();
        var guestContact = Guid.NewGuid();
        _store.AddContact(guestContact, "guest.partner@other.example",
            flag: Flag(IdentityCollisionReason.GuestLinkUnverified, guestOid));
        _store.AddSystemUser(Guid.Parse("ffffffff-ffff-4fff-bfff-ffffffffffff"), guestOid, "guest.partner@other.example",
            primaryContactId: guestContact, domainName: "guest.partner_other.example#EXT#@spaarke.onmicrosoft.com");
        const int PageCeiling = 40; // IdentityLinkReconciliationJob.MaxPages
        for (var i = 0; i < PageCeiling + 5; i++)
        {
            _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), email: null);
        }

        _store.ScanPageSize = 1;

        var result = await RunAsync(writes: true);

        UsersScanned(result).Should().Be(PageCeiling, "the scan stopped at the page ceiling");
        _store.Contacts[guestContact].Flag.Should().NotBeNull("no flag is cleared on a partial view");
        _store.Reads.Should().NotContain("scan-flags");
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Pass 2");
    }

    [Fact]
    public async Task AFailedMaskingProbe_AbortsTheRun_BeforeAnyDecision()
    {
        _store.FailProbe = true;

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse("a probe that could not be read is not evidence the column is readable");
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().NotContain("scan-users");
        _store.Reads.Should().NotContain("scan-flags");
    }

    // ── The cheap Verified path (verifier finding 2(3)) ─────────────────────────────────────────────

    [Fact]
    public async Task ALinkedUserWhoseContactIsInactive_IsFlagged_NeverReportedVerified()
    {
        var oid = Guid.NewGuid();
        var linked = Guid.NewGuid();
        var user = Guid.NewGuid();
        _store.AddContact(linked, "inactive.link@spaarke.com", oid.ToString("D"), IdentityPlaneMarker.Workforce, stateCode: 1);
        _store.AddSystemUser(user, oid, "inactive.link@spaarke.com", primaryContactId: linked);

        var outcome = await Binder(_store).EnsureSystemUserLinkAsync(_store.SystemUsers[user].Id, applyWrites: true,
            CancellationToken.None);

        outcome.Outcome.Should().Be(SystemUserLinkOutcome.Flagged, "an inactive contact never verifies a link");
        outcome.Reason.Should().Be(IdentityCollisionReason.LinkedContactInactive);
        _store.Contacts[linked].Flag!.Reason.Should().Be(IdentityCollisionReason.LinkedContactInactive);
        _store.SystemUsers[user].PrimaryContactId.Should().Be(linked, "the existing link is never cleared or re-pointed");
    }

    // ── Failure shapes ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFailedScan_IsAFailedRun_NeverNothingToReconcile()
    {
        _store.FailSystemUserScan = true;

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("scan failed");
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().NotContain("scan-flags", "no flag is cleared on a partial view");
    }

    [Fact]
    public async Task AMaskedBindingColumn_AbortsTheRun_BeforeAnyDecision()
    {
        _store.MaskBindingColumn = true;

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().NotContain("scan-users");
    }

    [Fact]
    public async Task Paging_ReachesEveryUser()
    {
        _store.ScanPageSize = 3;

        var result = await RunAsync(writes: true);

        UsersScanned(result).Should().Be(8);
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    // ── Provisioning targets (third fix round): every environment this BFF provisions users into is
    //    reconciled like its own — the registration-link gap of notes §12.1 is closed, not recorded ─────────

    private const string OwnEnvironment = "https://spaarkedev1.crm.dynamics.com";
    private const string DemoEnvironment = "https://spaarke-demo.crm.dynamics.com";
    private const string OtherDemoEnvironment = "https://spaarke-demo2.crm.dynamics.com";

    [Fact]
    public async Task ARegistrationLinkThatDidNotLand_InAProvisioningTarget_IsMadeByTheNextRun()
    {
        // The registration created this systemuser in the demo environment and its inline link did not land.
        var demo = new InMemoryContactIdentityStore();
        var oid = Guid.NewGuid();
        var user = Guid.NewGuid();
        demo.AddSystemUser(user, oid, "new.user@demo.spaarke.com");

        var result = await RunAsync(writes: true, targets: new() { [DemoEnvironment] = demo },
            registry: new[] { DemoEnvironment });

        var created = demo.ContactsBoundTo(oid).Should().ContainSingle().Subject;
        demo.SystemUsers[user].PrimaryContactId.Should().Be(created.Id);
        TargetOutcome(result, DemoEnvironment, "CreatedAndLinked").Should().Be(1);
        _store.SystemUsers[TestUser1].PrimaryContactId.Should().Be(TestUser1Contact, "the own environment is reconciled too");
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task ReportOnly_WritesNothingInAProvisioningTarget_AndLogsItsBeforeState()
    {
        var demo = new InMemoryContactIdentityStore();
        demo.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "new.user@demo.spaarke.com");

        var result = await RunAsync(writes: null, targets: new() { [DemoEnvironment] = demo }, registry: new[] { DemoEnvironment });

        demo.Writes.Should().BeEmpty("the owner's switch gates every environment");
        _log.Messages.Should().Contain(m => m.Contains("before-state") && m.Contains($"environment={DemoEnvironment}")
            && m.Contains("create contact"));
        TargetOutcome(result, DemoEnvironment, "CreatedAndLinked").Should().Be(1, "reported as what it WOULD do");
    }

    [Fact]
    public async Task AFlagWrittenInAProvisioningTarget_IsReEvaluated_AndClearedOnceResolved()
    {
        // A registration link raised a collision in the demo environment (an internal address once invited as an
        // external). This BFF must re-evaluate that flag too, or it outlives its collision forever.
        var demo = new InMemoryContactIdentityStore();
        var oid = Guid.NewGuid();
        var user = Guid.NewGuid();
        var contact = Guid.NewGuid();
        demo.AddContact(contact, "invited.employee@demo.spaarke.com", Guid.NewGuid().ToString("D"), IdentityPlaneMarker.External);
        demo.AddSystemUser(user, oid, "invited.employee@demo.spaarke.com");
        var targets = new Dictionary<string, InMemoryContactIdentityStore> { [DemoEnvironment] = demo };

        await RunAsync(writes: true, targets: targets, registry: new[] { DemoEnvironment });
        demo.Contacts[contact].Flag!.Reason.Should().Be(IdentityCollisionReason.BoundToDifferentOid);

        // The documented resolution: the external identity loses the contact (binding, mirror and plane cleared).
        demo.Contacts[contact].Oid = null;
        demo.Contacts[contact].KeyMirror = null;
        demo.Contacts[contact].Plane = null;
        var next = await RunAsync(writes: true, targets: targets, registry: new[] { DemoEnvironment });

        demo.Contacts[contact].Flag.Should().BeNull("pass 2 runs in the provisioning target as well");
        demo.SystemUsers[user].PrimaryContactId.Should().Be(contact);
        next.Success.Should().BeTrue(next.ErrorMessage);
    }

    [Fact]
    public async Task TheRegistrationDefaultEnvironment_IsATargetEvenWhenTheRegistryCannotBeRead_AndTheRunFails()
    {
        var registrationDefault = new InMemoryContactIdentityStore();
        var oid = Guid.NewGuid();
        var user = Guid.NewGuid();
        registrationDefault.AddSystemUser(user, oid, "a@demo.spaarke.com");

        var result = await RunAsync(writes: true,
            targets: new() { [OtherDemoEnvironment] = registrationDefault },
            registrationEnvironment: OtherDemoEnvironment, registryFails: true);

        registrationDefault.SystemUsers[user].PrimaryContactId.Should().NotBeNull("DATAVERSE_URL needs no registry read");
        result.Success.Should().BeFalse("an unreadable registry means some targets were not reconciled");
        result.ErrorMessage.Should().Contain("environment registry");
        _store.SystemUsers[TestUser1].PrimaryContactId.Should().Be(TestUser1Contact);
    }

    [Fact]
    public async Task OneUnreachableTarget_FailsTheRun_ButNeverStopsTheOthers()
    {
        var demo2 = new InMemoryContactIdentityStore();
        var user = Guid.NewGuid();
        demo2.AddSystemUser(user, Guid.NewGuid(), "b@demo.spaarke.com");

        // DemoEnvironment has no store: building its binder throws, as an unreachable environment would.
        var result = await RunAsync(writes: true, targets: new() { [OtherDemoEnvironment] = demo2 },
            registry: new[] { DemoEnvironment, OtherDemoEnvironment });

        demo2.SystemUsers[user].PrimaryContactId.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain($"[{DemoEnvironment}]");
        TargetStatus(result, DemoEnvironment).Should().Be("error");
        TargetStatus(result, OtherDemoEnvironment).Should().Be("ok", "the other target is reported on its own merits");
    }

    [Fact]
    public async Task AFailedScanInATarget_IsAFailedRun_NotNothingToReconcile()
    {
        var demo = new InMemoryContactIdentityStore { FailSystemUserScan = true };

        var result = await RunAsync(writes: true, targets: new() { [DemoEnvironment] = demo }, registry: new[] { DemoEnvironment });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain($"[{DemoEnvironment}]").And.Contain("scan failed");
    }

    [Fact]
    public async Task TheOwnEnvironmentInTheRegistry_IsReconciledOnce()
    {
        var result = await RunAsync(writes: true, targets: new(), registry: new[] { OwnEnvironment + "/", OwnEnvironment.ToUpperInvariant() });

        TargetEnvironments(result).Should().BeEmpty("the own environment is reconciled through the DI binder only");
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task WithoutDATAVERSE_URL_ThereAreNoProvisioningTargets_AndTheRunIsClean()
    {
        var result = await RunAsync(writes: true);

        using var doc = JsonDocument.Parse(result.ResultJson!);
        doc.RootElement.GetProperty("provisioningTargets").GetProperty("configured").GetBoolean().Should().BeFalse();
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public void ProvisioningTargetUrls_AreTheRegistrationDefaultAndTheRegistry_NormalisedDeduplicated_NeverTheOwn()
        => IdentityLinkReconciliationJob.ProvisioningTargetUrls(
                OwnEnvironment + "/",
                DemoEnvironment + "/",
                new[] { DemoEnvironment.ToUpperInvariant(), null, "  ", OwnEnvironment, OtherDemoEnvironment })
            .Should().Equal(DemoEnvironment, OtherDemoEnvironment);

    // ── Shipping state ───────────────────────────────────────────────────────────────────────────────

    /// <remarks>
    /// Not an ADR-038 B3 wiring test (the same reasoning as <c>ExternalAccessReconciliationTests.S2</c>): it
    /// asserts the SHIPPING STATE — registered through <c>AddScheduledJob</c> on a 5-minute cron and enabled,
    /// with writes still gated by the report-only switch. Perturb the cron or the enabled flag and it reddens.
    /// </remarks>
    [Fact]
    public void TheJobShipsEnabled_EveryFiveMinutes_WithWritesGatedByTheSwitch()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetServices<ScheduledJobRegistration>()
            .Single(r => r.Job.JobId == IdentityLinkReconciliationJob.JobIdConstant);

        registration.CronSchedule.Should().Be("*/5 * * * *");
        registration.Enabled.Should().BeTrue("report-only is the job's own default; the switch is the rollout guard");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    /// <param name="targets">
    /// When set, registration is configured (DATAVERSE_URL = <paramref name="registrationEnvironment"/>, default the
    /// own environment) and each provisioning-target URL is served by its store; a URL with no store throws when its
    /// binder is built, as an unreachable environment would.
    /// </param>
    private async Task<JobRunResult> RunAsync(
        bool? writes,
        string? rawSwitch = null,
        Dictionary<string, InMemoryContactIdentityStore>? targets = null,
        string[]? registry = null,
        string? registrationEnvironment = null,
        bool registryFails = false)
    {
        var settings = new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = OwnEnvironment };
        if (writes is { } w) settings["IdentityLink:Reconciliation:WritesEnabled"] = w ? "true" : "false";
        if (rawSwitch is not null) settings["IdentityLink:Reconciliation:WritesEnabled"] = rawSwitch;
        if (targets is not null) settings["DATAVERSE_URL"] = registrationEnvironment ?? OwnEnvironment;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton(Binder(_store));
        if (targets is not null)
        {
            services.AddSingleton(new RegistrationDataverseService(
                configuration,
                new TrackingIdGenerator(),
                Mock.Of<TokenCredential>(),
                Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient()),
                NullLogger<RegistrationDataverseService>.Instance,
                new PerEnvironmentBinderFactory(targets),
                // Task 132: the team/BU write-path eviction hook; this suite writes no team or BU.
                new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
                    NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance)));
            services.AddSingleton<DataverseEnvironmentService>(
                new RegistryDouble(configuration, registry ?? Array.Empty<string>(), registryFails));
        }

        using var provider = services.BuildServiceProvider();

        var job = new IdentityLinkReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(Now),
            configuration,
            _log);

        return await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-141", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
            CancellationToken.None);
    }

    private static int Outcome(JobRunResult result, string outcome)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.GetProperty("systemUsers").GetProperty("outcomes").TryGetProperty(outcome, out var o)
            ? o.GetProperty("count").GetInt32()
            : 0;
    }

    private static int UsersScanned(JobRunResult result)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.GetProperty("systemUsers").GetProperty("scanned").GetInt32();
    }

    private static int FlagCount(JobRunResult result, string property)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.GetProperty("flags").GetProperty(property).GetInt32();
    }

    private static string Mode(JobRunResult result)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.GetProperty("mode").GetString()!;
    }

    private static int TargetOutcome(JobRunResult result, string environment, string outcome)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        var env = doc.RootElement.GetProperty("provisioningTargets").GetProperty("environments").EnumerateArray()
            .Single(e => e.GetProperty("environment").GetString() == environment);
        return env.GetProperty("systemUsers").GetProperty("outcomes").TryGetProperty(outcome, out var o)
            ? o.GetProperty("count").GetInt32()
            : 0;
    }

    private static string TargetStatus(JobRunResult result, string environment)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.GetProperty("provisioningTargets").GetProperty("environments").EnumerateArray()
            .Single(e => e.GetProperty("environment").GetString() == environment)
            .GetProperty("status").GetString()!;
    }

    private static List<string> TargetEnvironments(JobRunResult result)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.GetProperty("provisioningTargets").GetProperty("environments").EnumerateArray()
            .Select(e => e.GetProperty("environment").GetString()!).ToList();
    }

    /// <summary>
    /// The binder factory's virtual store seam (the registration path's own test convention): each environment URL
    /// gets its in-memory store, so "this environment was reconciled" is asserted on its own rows — no HTTP double.
    /// </summary>
    private sealed class PerEnvironmentBinderFactory(Dictionary<string, InMemoryContactIdentityStore> stores)
        : ContactIdentityBinderFactory(Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant),
            new FakeTimeProvider(Now))
    {
        public override IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
            => stores.TryGetValue(dataverseBaseUrl, out var store)
                ? store
                : throw new InvalidOperationException($"{dataverseBaseUrl} could not be reached");
    }

    /// <summary>The environment registry at its virtual read (no HTTP double).</summary>
    private sealed class RegistryDouble(IConfiguration configuration, string[] activeUrls, bool fails)
        : DataverseEnvironmentService(configuration, Mock.Of<TokenCredential>(), NullLogger<DataverseEnvironmentService>.Instance)
    {
        public override Task<List<DataverseEnvironmentRecord>> GetActiveEnvironmentsAsync(CancellationToken ct = default)
            => fails
                ? Task.FromException<List<DataverseEnvironmentRecord>>(new HttpRequestException("registry unavailable"))
                : Task.FromResult(activeUrls.Select(u => new DataverseEnvironmentRecord { DataverseUrl = u, IsActive = true }).ToList());
    }
}
