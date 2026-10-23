using System.Security.Claims;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// The invite-stamp rule for email binds (<see cref="ContactBindingDecision.CheckInviteStamp"/>): an email bind onto an
/// existing contact needs a current invite stamp (same 30-day window as the invite marker) whose recorded address is the
/// token's email AND the contact's current <c>emailaddress1</c>; on the Workforce planes the stamp's tenant must also be the
/// token's <c>tid</c>, unless <c>xms_edov</c> is boolean true. Every failing leg answers the one deny code
/// <see cref="ContactBindingDecision.DenyContactNotInvited"/>; the leg is an operator-only log word. A successful bind clears
/// the stamp in its own conditional write; a row that changed since the stamp was read is re-read and re-decided.
/// </summary>
public class InviteStampBindingTests
{
    private static readonly Guid ContactA = Guid.Parse("eeeeeeee-6666-4666-8666-00000000000a");
    private static readonly Guid Caller = Guid.Parse("aaaaaaaa-6666-4666-8666-000000000001");
    private const string Email = "person@customer.example";

    private readonly InMemoryContactIdentityStore _store = new();

    // ── The rule, leg by leg (pure) ─────────────────────────────────────────────────────────────────────────

    /// <summary>A current stamp for <see cref="Email"/> naming <see cref="CustomerTenant"/>, read at <see cref="Now"/>.</summary>
    private static BindIntentLookup Stamp()
        => new(ContactA, LookupStatus.Read, Now)
        {
            AsOf = Now,
            InvitedEmailHash = InviteEmailStamp.Create(Email),
            RawInvitedTenantId = CustomerTenant.ToString("D"),
            CurrentEmail = Email,
            ETag = "W/\"7\"",
        };

    private static readonly string[] Planes = { "workforce", "ciam" };

    /// <summary>
    /// MUST FIRE / MUST NOT FIRE for every leg, on the plane(s) it applies to. <c>expected</c> is the operator log word, or
    /// null for a bind. MUTATION PROOFS (see the report's table): removing any one leg from CheckInviteStamp fails its rows.
    /// </summary>
    [Theory]
    // a stamp exists
    [InlineData("valid", "workforce", null)]
    [InlineData("valid", "ciam", null)]
    [InlineData("no-stamp", "workforce", "stamp-none")]
    [InlineData("no-stamp", "ciam", "stamp-none")]
    [InlineData("unproven-empty", "workforce", "stamp-unproven")]
    [InlineData("column-missing", "workforce", null)] // a missing column is no marker (CIAM) / no stamp: denied, below
    // it is current (the invite marker's own window and skew)
    [InlineData("stale", "workforce", "stamp-expired")]
    [InlineData("stale", "ciam", "marker-expired")]   // on CIAM the marker leg answers first: same deny code
    [InlineData("exactly-30d", "workforce", null)]
    [InlineData("future-beyond-skew", "workforce", "stamp-expired")]
    [InlineData("no-decision-time", "workforce", "stamp-expired")]
    // it parses as a version this code writes
    [InlineData("malformed", "workforce", "stamp-malformed")]
    [InlineData("unknown-version", "workforce", "stamp-malformed")]
    [InlineData("unknown-version", "ciam", "stamp-malformed")]
    // it records the token's email
    [InlineData("token-email-mismatch", "workforce", "stamp-token-email-mismatch")]
    [InlineData("token-email-mismatch", "ciam", "stamp-token-email-mismatch")]
    [InlineData("token-email-case-and-space", "workforce", null)]
    // it records the contact's current emailaddress1
    [InlineData("contact-email-changed", "workforce", "stamp-contact-email-mismatch")]
    [InlineData("contact-email-changed", "ciam", "stamp-contact-email-mismatch")]
    [InlineData("contact-email-absent", "workforce", "stamp-contact-email-mismatch")]
    // Workforce: the tenant leg
    [InlineData("tid-mismatch", "workforce", "stamp-tenant-not-verified")]
    [InlineData("tid-mismatch", "ciam", null)]         // the CIAM plane has no tenant leg
    [InlineData("tid-absent", "workforce", "stamp-tenant-not-verified")]
    [InlineData("no-tenant-stamped", "workforce", "stamp-tenant-not-verified")]
    [InlineData("stored-tenant-not-a-guid", "workforce", "stamp-tenant-not-verified")]
    [InlineData("tid-mismatch-edov-false", "workforce", "stamp-tenant-not-verified")]
    [InlineData("tid-mismatch-edov-absent", "workforce", "stamp-tenant-not-verified")]
    [InlineData("tid-mismatch-edov-true", "workforce", null)]
    [InlineData("no-tenant-stamped-edov-true", "workforce", null)]
    [InlineData("stored-tenant-upper-case", "workforce", null)]
    public void EachLeg_DeniesNotInvited_OrBinds(string row, string plane, string? expected)
    {
        var intent = Stamp();
        var tokenEmail = Email;
        Guid? tid = CustomerTenant;
        bool? edov = null;
        switch (row)
        {
            case "no-stamp": intent = intent with { InvitedEmailHash = null }; break;
            case "unproven-empty": intent = intent with { InvitedEmailHash = "", StampColumnsUnproven = true }; break;
            case "column-missing": intent = intent with { InviteStatus = LookupStatus.ColumnMissing }; break;
            case "stale": intent = intent with { InvitedOn = Now - ContactBindingDecision.InviteMarkerWindow - TimeSpan.FromSeconds(1) }; break;
            case "exactly-30d": intent = intent with { InvitedOn = Now - ContactBindingDecision.InviteMarkerWindow }; break;
            case "future-beyond-skew": intent = intent with { InvitedOn = Now + ContactBindingDecision.InviteMarkerClockSkew + TimeSpan.FromSeconds(1) }; break;
            case "no-decision-time": intent = intent with { AsOf = default }; break;
            case "malformed": intent = intent with { InvitedEmailHash = "v1:not base64:also not" }; break;
            case "unknown-version": intent = intent with { InvitedEmailHash = "v2" + intent.InvitedEmailHash![2..] }; break;
            case "token-email-mismatch": tokenEmail = "someone.else@customer.example"; break;
            case "token-email-case-and-space": tokenEmail = "  PERSON@Customer.Example "; break;
            case "contact-email-changed": intent = intent with { CurrentEmail = "renamed@customer.example" }; break;
            case "contact-email-absent": intent = intent with { CurrentEmail = null }; break;
            case "tid-mismatch": tid = Guid.NewGuid(); break;
            case "tid-absent": tid = null; break;
            case "no-tenant-stamped": intent = intent with { RawInvitedTenantId = null }; break;
            case "stored-tenant-not-a-guid": intent = intent with { RawInvitedTenantId = "customer.example" }; break;
            case "tid-mismatch-edov-false": tid = Guid.NewGuid(); edov = false; break;
            case "tid-mismatch-edov-absent": tid = Guid.NewGuid(); edov = null; break;
            case "tid-mismatch-edov-true": tid = Guid.NewGuid(); edov = true; break;
            case "no-tenant-stamped-edov-true": intent = intent with { RawInvitedTenantId = null }; edov = true; break;
            case "stored-tenant-upper-case": intent = intent with { RawInvitedTenantId = CustomerTenant.ToString("D").ToUpperInvariant() }; break;
        }

        var invite = InviteCode.Create(ContactA);
        var ciam = plane == "ciam";
        if (ciam)
        {
            intent = intent with { InviteCodeHash = invite.StoredValue };
        }

        var request = new BindingRequest(
            ciam ? BindingPlane.CiamToken : BindingPlane.WorkforceToken, Caller, tokenEmail,
            ciam ? BindingEligibility.EmailBindOnly : BindingEligibility.EmailBindAndCreate)
        {
            InviteCode = ciam ? invite.Code : null,
            TokenTenantId = tid,
            EmailDomainOwnerVerified = edov,
        };

        var decision = ContactBindingDecision.Decide(request, ContactLookup.Of(),
            ContactLookup.Of(new ContactBindingRow(ContactA, 0, null, null)), ReferenceLookup.Of(), intent);

        if (row == "column-missing")
        {
            // The stamp cannot be read where its columns are not provisioned: no stamp, denied, never a fault.
            decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
            ContactBindingDecision.CheckInviteStamp(intent, tokenEmail, true, tid, edov).Should().Be(InviteStampFailure.NoStamp);
            return;
        }

        if (expected is null)
        {
            decision.Should().Be(new BindingDecision(BindingAction.BindByEmail, ContactA) { ConsumeInviteCode = ciam }, row);
            ContactBindingDecision.NotInvitedReason(request, intent, ContactA).Should().BeNull(row);
            return;
        }

        decision.Should().Be(new BindingDecision(BindingAction.Deny, DenyCode: ContactBindingDecision.DenyContactNotInvited), row);
        decision.FlagContactIds.Should().BeNullOrEmpty("a missing invite involves no other person");
        ContactBindingDecision.NotInvitedReason(request, intent, ContactA).Should().Be(expected, row);
    }

    /// <summary>The window is the invite marker's: one shared constant, so a stamp and its marker expire together.</summary>
    [Fact]
    public void TheStampWindow_IsTheInviteMarkersWindow()
    {
        var atTheEdge = Stamp() with { InvitedOn = Now - ContactBindingDecision.InviteMarkerWindow };
        var pastIt = Stamp() with { InvitedOn = Now - ContactBindingDecision.InviteMarkerWindow - TimeSpan.FromTicks(1) };

        ContactBindingDecision.CheckInviteStamp(atTheEdge, Email, false, null, null).Should().BeNull();
        ContactBindingDecision.CheckInviteStamp(pastIt, Email, false, null, null).Should().Be(InviteStampFailure.StampExpired);
        ContactBindingDecision.IsInviteMarkerCurrent(atTheEdge.InvitedOn, Now).Should().BeTrue();
        ContactBindingDecision.IsInviteMarkerCurrent(pastIt.InvitedOn, Now).Should().BeFalse();
    }

    /// <summary>Every leg has its own operator log word, and none carries an address or a hash.</summary>
    [Fact]
    public void EveryLeg_HasADistinctFixedLogWord()
    {
        var words = Enum.GetValues<InviteStampFailure>().Select(ContactBindingDecision.StampReason).ToList();

        words.Should().OnlyHaveUniqueItems().And.OnlyContain(w => w.StartsWith("stamp-") && !w.Contains('@'));
        words.Should().NotContain("stamp-unknown");
    }

    // ── E8, E10 and the oid path are unchanged: a stamp neither enables nor is needed there ──────────────────

    [Fact]
    public void E8_AGuestsEmailMatch_StaysACollision_EvenWithAValidStamp()
        => ContactBindingDecision.Decide(
                new BindingRequest(BindingPlane.SystemUser, Caller, Email, BindingEligibility.CreateUnlessEmailMatches),
                ContactLookup.Of(), ContactLookup.Of(new ContactBindingRow(ContactA, 0, null, null)), ReferenceLookup.Of(), Stamp())
            .Reason.Should().Be(IdentityCollisionReason.GuestEmailMatch);

    [Theory]
    [InlineData(BindingPlane.WorkforceToken)]
    [InlineData(BindingPlane.CiamToken)]
    public void E10_AContactAnotherUserLinksTo_StaysACollision_EvenWithAValidStamp(BindingPlane plane)
    {
        var refs = ReferenceLookup.Of(new SystemUserReference(Guid.NewGuid(), Guid.NewGuid(), ContactA));
        var request = new BindingRequest(plane, Caller, Email,
            plane == BindingPlane.CiamToken ? BindingEligibility.EmailBindOnly : BindingEligibility.EmailBindAndCreate)
        {
            TokenTenantId = CustomerTenant,
        };

        ContactBindingDecision.Decide(request, ContactLookup.Of(), ContactLookup.Of(new ContactBindingRow(ContactA, 0, null, null)),
                refs, Stamp())
            .Reason.Should().Be(IdentityCollisionReason.LinkedToOtherUser);
    }

    [Fact]
    public async Task AContactBoundToTheOid_ResolvesWithoutAnyStamp_OnBothTokenPlanes()
    {
        var ciamOid = Guid.NewGuid();
        _store.AddContact(ContactA, email: Email, oid: Caller.ToString("D"), plane: IdentityPlaneMarker.Workforce);
        _store.AddContact(Guid.NewGuid(), email: "ciam@firm.example", oid: ciamOid.ToString("D"), plane: IdentityPlaneMarker.External);

        (await Binder(_store).ResolveWorkforceCallerAsync(WorkforceUser(Caller, tid: CustomerTenant, email: Email), Caller,
                stampTenantId: null, CancellationToken.None))
            .Should().Be(ContactBindingResult.Resolved(ContactA, BindingAction.ResolveByOid, wrote: false));
        (await Binder(_store).ResolveCiamCallerAsync(ciamOid.ToString(), "ciam@firm.example", null, CancellationToken.None))
            .Action.Should().Be(BindingAction.ResolveByOid);
        _store.Reads.Should().NotContain("intent", "an oid binding never needs a stamp");
        _store.Writes.Should().BeEmpty();
    }

    // ── Through the binder: deny, log, clear on bind, and never bind on stale state ─────────────────────────

    private ClaimsPrincipal Member(Guid? tid = null, string email = Email, Claim? extra = null)
    {
        var user = WorkforceUser(Caller, tid: tid ?? CustomerTenant, email: email);
        if (extra is not null)
        {
            ((ClaimsIdentity)user.Identity!).AddClaim(extra);
        }

        return user;
    }

    private Task<ContactBindingResult> SignIn(ClaimsPrincipal user, BindingLogCapture<ContactIdentityBinder>? log = null)
        => Binder(_store, log).ResolveWorkforceCallerAsync(user, Caller, stampTenantId: null, CancellationToken.None);

    [Fact]
    public async Task Workforce_NoStamp_IsDeniedNotInvited_NothingWritten_TheLogNamesTheLegOnly()
    {
        _store.AddContact(ContactA, email: Email);
        var log = new BindingLogCapture<ContactIdentityBinder>();

        var result = await SignIn(Member(), log);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Should().ContainSingle("no fallback to a create");
        log.Messages.Should().Contain(m => m.Contains("stamp-none") && m.Contains(ContactA.ToString()));
        log.Messages.Should().NotContain(m => m.Contains(Email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Workforce_AStampForAnotherAddress_IsDenied_AndNoLogLineCarriesAnAddressOrTheHash()
    {
        _store.AddContact(ContactA, email: Email);
        var row = _store.StampInvite(ContactA, "invited@customer.example", CustomerTenant);
        var log = new BindingLogCapture<ContactIdentityBinder>();

        var result = await SignIn(Member(), log);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        row.Oid.Should().BeNull();
        log.Messages.Should().Contain(m => m.Contains("stamp-token-email-mismatch"));
        var hashParts = row.InvitedEmailHash!.Split(':').Where(p => p.Length >= 8);
        log.Messages.Should().NotContain(m => m.Contains('@') || hashParts.Any(m.Contains));
    }

    [Fact]
    public async Task Workforce_AnEmptyStampReadWithoutProof_IsDenied_AsUnproven()
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, CustomerTenant);
        _store.MaskInviteStampColumns = true;
        _store.InviteStampColumnsProof = ColumnReadProofOutcome.Unproven;
        var log = new BindingLogCapture<ContactIdentityBinder>();

        var result = await SignIn(Member(), log);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited, "an unproven empty stamp is no stamp");
        _store.Contacts[ContactA].Oid.Should().BeNull();
        log.Messages.Should().Contain(m => m.Contains("stamp-unproven"));
    }

    /// <summary>
    /// xms_edov counts only as a boolean true: the string "true", false or no claim leaves the tenant leg to the token's tid,
    /// which here is not the invited tenant (denied); a boolean true binds a stamp naming no tenant.
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("string:true", false)]
    [InlineData("bool:false", false)]
    [InlineData("bool:true", true)]
    public async Task Workforce_TheTenantLeg_NeedsTheInvitedTenant_OrABooleanTrueXmsEdov(string? edov, bool binds)
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, invitedTenantId: null);
        var claim = edov is null
            ? null
            : new Claim(WorkforceCallerClaims.EmailDomainOwnerVerifiedClaim, edov.Split(':')[1],
                edov.StartsWith("bool", StringComparison.Ordinal) ? ClaimValueTypes.Boolean : ClaimValueTypes.String);

        var result = await SignIn(Member(extra: claim));

        if (binds)
        {
            result.Should().Be(new ContactBindingResult(ContactA, null, BindingAction.BindByEmail, Wrote: true));
        }
        else
        {
            result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
            _store.Writes.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Workforce_ATokenFromAnotherTenant_IsDenied_TheInvitedTenantBinds()
    {
        var otherCustomerTenant = Guid.Parse("33333333-cccc-4ccc-8ccc-333333333333");
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, CustomerTenant);
        var binderForBoth = new ContactIdentityBinder(_store, Tenants(CustomerTenant, otherCustomerTenant),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now), new BindingLogCapture<ContactIdentityBinder>(),
            NoOwnerUnderTest);

        (await binderForBoth.ResolveWorkforceCallerAsync(Member(otherCustomerTenant), Caller, null, CancellationToken.None))
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        (await binderForBoth.ResolveWorkforceCallerAsync(Member(CustomerTenant), Caller, null, CancellationToken.None))
            .Action.Should().Be(BindingAction.BindByEmail);
    }

    /// <summary>The successful bind clears the stamp (email hash and tenant) in the same conditional write as the oid.</summary>
    [Fact]
    public async Task ASuccessfulBind_ClearsTheStampInTheSameWrite_SoItBindsOnlyOnce()
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, CustomerTenant);

        (await SignIn(Member())).Action.Should().Be(BindingAction.BindByEmail);

        var row = _store.Contacts[ContactA];
        _store.Writes.Should().ContainSingle().Which.Op.Should().Be("bind");
        (row.InvitedEmailHash, row.InvitedTenantId).Should().Be(((string?)null, (string?)null));
        row.InvitedOn.Should().NotBeNull("the marker is not touched");

        // An operator unbind afterwards leaves no stamp a second identity could use.
        row.Oid = null;
        row.KeyMirror = null;
        row.Plane = null;
        _store.Touch(row);
        var second = Guid.NewGuid();
        (await Binder(_store).ResolveWorkforceCallerAsync(WorkforceUser(second, tid: CustomerTenant, email: Email), second,
                null, CancellationToken.None))
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
    }

    /// <summary>
    /// 412: a re-invite to another address lands between the decision and the write. The write's If-Match (the version the
    /// stamp was read at) fails, the binder re-reads and re-decides against the new stamp, and nothing is bound.
    /// </summary>
    [Fact]
    public async Task A412_IsReDecided_AgainstTheNewStamp_NeverBoundOnStaleState()
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, CustomerTenant);
        _store.BeforeBind = c =>
        {
            c.InvitedEmailHash = InviteEmailStamp.Create("someone.else@customer.example");
            _store.Touch(c);
        };

        var result = await SignIn(Member());

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        _store.BindPreconditionFailures.Should().Be(1);
        _store.Contacts[ContactA].Oid.Should().BeNull();
        _store.Reads.Count(r => r == "intent").Should().Be(2, "the stamp is read again for the re-decision");
    }

    /// <summary>412 where the re-read still holds a valid stamp: the second attempt binds, under the new version.</summary>
    [Fact]
    public async Task A412_WhereTheStampStillHolds_BindsOnTheReDecision()
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, CustomerTenant);
        _store.BeforeBind = c => _store.Touch(c);

        var result = await SignIn(Member());

        result.Action.Should().Be(BindingAction.BindByEmail);
        _store.BindPreconditionFailures.Should().Be(1);
        _store.Contacts[ContactA].Oid.Should().Be(Caller.ToString("D"));
    }

    /// <summary>
    /// The row changes between the binding read and the stamp read: the two versions differ, so nothing is written on that
    /// pass; the binder re-reads both and binds when they agree. If they never agree it denies bind-failed, never binding
    /// on a version it did not decide on.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, false)]
    public async Task ARowThatChangesBetweenTheBindingReadAndTheStampRead_IsReDecided(int changes, bool binds)
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, CustomerTenant);
        var left = changes;
        _store.AfterRead = op =>
        {
            if (op == "intent" && left-- > 0)
            {
                _store.Touch(_store.Contacts[ContactA]);
            }
        };

        var result = await SignIn(Member());

        _store.BindPreconditionFailures.Should().Be(0, "no write is sent on a pass whose two reads disagree");
        if (binds)
        {
            result.Action.Should().Be(BindingAction.BindByEmail);
            _store.Writes.Should().ContainSingle(w => w.Op == "bind");
        }
        else
        {
            result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBindFailed);
            _store.Writes.Should().BeEmpty();
            _store.Contacts[ContactA].Oid.Should().BeNull();
        }
    }

    // ── WorkforceHome registration (confirmExistingRecord): the same rule, and the same re-read on a changed row ──────

    private Task<HomeRegistrationResult> Register(bool confirm = true, bool? edov = null)
        => Binder(_store).RegisterHomeContactAsync(
            new HomeRegistrationRequest(CustomerTenant, Caller, Email, "Pat", "Example", null, confirm, Guid.NewGuid(),
                "Secure Record", edov),
            CancellationToken.None);

    [Fact]
    public async Task Registration_BindsAnExistingContact_OnlyOnTheStamp_AndClearsIt()
    {
        _store.AddContact(ContactA, email: Email);

        (await Register()).Should().Be(HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactNotInvited));
        (await Register(confirm: false)).DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited,
            "the stamp is checked before the confirmation prompt");
        _store.Writes.Should().BeEmpty();

        _store.StampInvite(ContactA, Email, CustomerTenant);
        var bound = await Register();

        bound.Outcome.Should().Be(HomeRegistrationOutcome.BoundExisting);
        var row = _store.Contacts[ContactA];
        row.Oid.Should().Be(Caller.ToString("D"));
        (row.InvitedEmailHash, row.InvitedTenantId).Should().Be(((string?)null, (string?)null));
    }

    [Fact]
    public async Task Registration_WithABooleanTrueXmsEdov_BindsAStampNamingNoTenant()
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, invitedTenantId: null);

        (await Register(edov: null)).DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        (await Register(edov: true)).Outcome.Should().Be(HomeRegistrationOutcome.BoundExisting);
    }

    /// <summary>
    /// The row changes between the email read and the stamp read: no bind on that pass; the registration re-reads and binds
    /// when both reads agree, and denies bind-failed (nothing written) when they never do.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, false)]
    public async Task Registration_ARowThatChangesBetweenItsReads_IsReDecided(int changes, bool binds)
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, CustomerTenant);
        var left = changes;
        _store.AfterRead = op =>
        {
            if (op == "intent" && left-- > 0)
            {
                _store.Touch(_store.Contacts[ContactA]);
            }
        };

        var result = await Register();

        _store.BindPreconditionFailures.Should().Be(0);
        if (binds)
        {
            result.Outcome.Should().Be(HomeRegistrationOutcome.BoundExisting);
        }
        else
        {
            result.Should().Be(HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactBindFailed));
            _store.Writes.Should().BeEmpty();
        }
    }

    // ── The invite side: MarkInviteSentAsync writes the stamp ─────────────────────────────────────────────

    [Fact]
    public async Task MarkInviteSent_WritesTheStamp_WithTheMarker_InOneWrite()
    {
        _store.AddContact(ContactA, email: Email);

        var mark = await Binder(_store).MarkInviteSentAsync(ContactA, $"W/\"{_store.Contacts[ContactA].Version}\"", Email,
            CustomerTenant, CancellationToken.None);

        mark.Status.Should().Be(StoreWriteStatus.Written);
        _store.Writes.Should().ContainSingle().Which.Op.Should().Be("invite-marker");
        var row = _store.Contacts[ContactA];
        InviteEmailStamp.Matches(row.InvitedEmailHash, Email).Should().BeTrue();
        row.InvitedTenantId.Should().Be(CustomerTenant.ToString("D"));
        row.InvitedOn.Should().Be(Now);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(Email, true)]
    public async Task MarkInviteSent_RefusesABlankEmail_AndTheEmptyTenant(string email, bool emptyTenant)
    {
        _store.AddContact(ContactA, email: Email);

        var act = () => Binder(_store).MarkInviteSentAsync(ContactA, $"W/\"{_store.Contacts[ContactA].Version}\"", email,
            emptyTenant ? Guid.Empty : null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.Writes.Should().BeEmpty();
    }
}
