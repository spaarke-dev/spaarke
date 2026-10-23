using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 141 — the Web API shapes of <see cref="DataverseContactIdentityStore"/>, asserted
/// through its PURE builders and parsers (ADR-038 bans HTTP doubles, so the emitted query is pinned directly).
/// Each assertion is a property the binding decision relies on: a query that stopped asking for it would make
/// the decision's guard vacuous while every behavioural test stayed green.
/// </summary>
public class DataverseContactIdentityStoreTests
{
    private static readonly Guid Oid = Guid.Parse("AAAAAAAA-0000-4000-8000-000000000001");

    [Fact]
    public void TheOidLookup_ReadsTwoRowsOfAnyState_ByTheBindingColumn_InDFormat()
    {
        var path = DataverseContactIdentityStore.BuildOidLookupPath(Oid);

        path.Should().Contain("$filter=sprk_externalobjectid eq 'aaaaaaaa-0000-4000-8000-000000000001'");
        path.Should().Contain("$top=2", "two contacts carrying one oid must be VISIBLE, not a first-row pick");
        path.Should().NotContain("statecode eq", "an inactive oid contact must be seen so it can deny");
        path.Should().Contain("sprk_identityplane").And.Contain("sprk_identitycollisionon");
    }

    [Fact]
    public void TheEmailLookup_ReadsTwoActiveRows_AndEscapesTheEmail()
    {
        var path = DataverseContactIdentityStore.BuildEmailLookupPath("o'brien@firm.example");

        path.Should().Contain("statecode eq 0").And.Contain("$top=2");
        path.Should().Contain(Uri.EscapeDataString("o''brien@firm.example"), "an OData literal doubles its quotes");
        path.Should().Contain("sprk_externalobjectid").And.Contain("sprk_identityplane",
            "without the binding and its marker every match would look unbound");
    }

    [Fact]
    public void TheInactiveEmailLookup_ReadsTwoINACTIVERows_WithTheSameColumnsAndEscaping_AndNeverTheActiveOnes()
    {
        // r3 task 047 fix round 2: the registration route's deactivated-contact read.
        var path = DataverseContactIdentityStore.BuildInactiveEmailLookupPath("o'brien@firm.example");

        path.Should().Contain("statecode eq 1").And.NotContain("statecode eq 0").And.Contain("$top=2");
        path.Should().Contain(Uri.EscapeDataString("o''brien@firm.example"), "an OData literal doubles its quotes");
        path.Should().Contain("sprk_externalobjectid").And.Contain("sprk_identityplane").And.Contain("statecode",
            "the row must carry its state and its binding");
        DataverseContactIdentityStore.BuildEmailLookupPath("o'brien@firm.example")
            .Should().Contain("statecode eq 0", "the active read is unchanged");
    }

    [Theory]
    [InlineData("400: {\"error\":{\"code\":\"0x80048d19\",\"message\":\"The value 100000012 of 'sprk_identitycollisionreason' on record of type 'contact' is outside the valid range.\"}}", true)]
    [InlineData("400: ... IS OUTSIDE THE VALID RANGE ...", true)]
    [InlineData("503: {\"error\":{\"message\":\"Service unavailable\"}}", false)]
    [InlineData("412: precondition failed", false)]
    [InlineData("injected", false)]
    [InlineData(null, false)]
    public void IsOptionValueOutOfRange_RecognisesAnUnprovisionedChoiceOption_AndNothingElse(string? error, bool expected)
        => DataverseContactIdentityStore.IsOptionValueOutOfRange(error).Should().Be(expected);

    // UAC 047 re-check: Dataverse echoes the whole $filter (the caller's email) in a syntax error's message, so a labelled
    // read logs only the error code. The first case is the shape probed on dev.
    [Theory]
    [InlineData("{\"error\":{\"code\":\"0x80060888\",\"message\":\"Expression expected at position 62 in 'emailaddress1 eq 'probe.uac@example.invalid' and (statecode eq'.\"}}", "0x80060888")]
    [InlineData("{\"error\":{\"message\":\"no code\"}}", null)]
    [InlineData("{\"error\":\"flat\"}", null)]
    [InlineData("<html>Bad Request</html>", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ErrorCodeOf_ReturnsOnlyTheCode_NeverTheMessage(string? body, string? expected)
        => DataverseContactIdentityStore.ErrorCodeOf(body).Should().Be(expected);

    [Fact]
    public void TheReferenceLookup_CountsEverySystemUser_EnabledOrNot()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var path = DataverseContactIdentityStore.BuildReferenceLookupPath(new[] { a, b });

        path.Should().Contain($"_sprk_primarycontact_value eq {a:D}").And.Contain($"_sprk_primarycontact_value eq {b:D}");
        path.Should().NotContain("isdisabled", "a disabled user's link still marks the contact as theirs");
    }

    [Fact]
    public void TheSystemUserScan_IsEnabledHumanUsersOnly_AndExpandsTheLinkedContact()
    {
        var path = DataverseContactIdentityStore.BuildSystemUserScanPath();

        path.Should().Contain("isdisabled eq false").And.Contain("applicationid eq null");
        path.Should().Contain("accessmode eq 0").And.Contain("accessmode eq 1").And.Contain("accessmode eq 2");
        path.Should().NotContain("accessmode eq 4", "non-interactive users are excluded");
        path.Should().Contain("$expand=sprk_PrimaryContact(");
    }

    // ── Owner round 4 item 4 (B2): field-level security stays on the binding; the alternate key moves to an
    //    unsecured mirror written with the same oid in the same request as every bind and create ─────────────

    /// <summary>
    /// The create is a plain POST to the collection. Dataverse answers a create-only upsert on an alternate key
    /// (<c>PATCH contacts(sprk_externalobjectidkey='…')</c> + <c>If-None-Match: *</c>) with 404 0x80060891 and creates
    /// nothing — every create of 141 gate G-6's first write run failed that way (2026-10-02). The uniqueness comes from
    /// the mirror key's unique index refusing the POST (412 0x80060892).
    /// </summary>
    [Fact]
    public void TheCreate_IsAPlainPostToTheCollection_NeverAKeyAddressedUpsert()
    {
        var (method, path, payload) = DataverseContactIdentityStore.BuildCreateRequest(
            Oid, IdentityPlaneMarker.Workforce, new NewContactDetails(null, "Example", null));

        method.Should().Be(HttpMethod.Post);
        path.Should().Be("contacts", "a key-addressed PATCH with If-None-Match: * is answered 404 and creates nothing");
        payload["sprk_externalobjectidkey"].Should().Be("aaaaaaaa-0000-4000-8000-000000000001",
            "without the mirror in the body the unique index has nothing to refuse, and two racing creates both land");
    }

    [Fact]
    public void TheMirrorKeysDuplicateFault_IsRecognised_SoACreateThatLostTheRaceReReads()
    {
        // Verbatim body of the second POST with the same mirror value, captured live on spaarkedev1 2026-10-02.
        const string body = "{\"error\":{\"code\":\"0x80060892\",\"message\":\"Entity Key External Object ID (unique) " +
            "violated. A record with the same value for External Object ID (uniqueness key) already exists.\"}}";

        DataverseContactIdentityStore.IsDuplicateKey(body).Should().BeTrue();
        DataverseContactIdentityStore.IsDuplicateKey(
            "{\"error\":{\"code\":\"0x80060891\",\"message\":\"A record with the specified key values does not exist\"}}")
            .Should().BeFalse("the keyed-PATCH 404 is not a duplicate; reading it as one would hide a create that never happened");
    }

    [Fact]
    public void TheBindPayload_WritesTheOidInDFormat_IntoTheBindingAndTheMirror_WithThePlane_InOneRequest()
    {
        var payload = DataverseContactIdentityStore.BindPayload(Oid, IdentityPlaneMarker.Workforce);

        payload["sprk_externalobjectid"].Should().Be("aaaaaaaa-0000-4000-8000-000000000001");
        payload["sprk_externalobjectidkey"].Should().Be("aaaaaaaa-0000-4000-8000-000000000001",
            "a bind that skipped the mirror would let a second contact take the same oid past the unique index");
        payload["sprk_identityplane"].Should().Be(100000001);
    }

    [Fact]
    public void TheCreatePayload_CarriesTheBindingTheMirrorAndThePlane_InOneBody()
    {
        var payload = DataverseContactIdentityStore.CreatePayload(
            Oid, IdentityPlaneMarker.Workforce, new NewContactDetails("Pat", "Example", "pat@customer.example"));

        payload["sprk_externalobjectid"].Should().Be("aaaaaaaa-0000-4000-8000-000000000001",
            "the binding is what every reader resolves by; a created contact without it would be unbound");
        payload["sprk_identityplane"].Should().Be(100000001);
        payload["sprk_externalobjectidkey"].Should().Be("aaaaaaaa-0000-4000-8000-000000000001",
            "the binding and the mirror carry the same oid, written together in the create");
        (payload["lastname"], payload["firstname"], payload["emailaddress1"])
            .Should().Be(("Example", "Pat", "pat@customer.example"));
    }

    [Fact]
    public void TheKeyMirrorLookup_IsDiagnostic_ReadingTwoRowsOfAnyState_ByTheMirror()
    {
        var path = DataverseContactIdentityStore.BuildKeyMirrorLookupPath(Oid);

        path.Should().Contain("$filter=sprk_externalobjectidkey eq 'aaaaaaaa-0000-4000-8000-000000000001'");
        path.Should().Contain("$top=2").And.NotContain("statecode eq",
            "the unique index counts inactive rows too, so an inactive holder must be found");
        path.Should().Contain("sprk_externalobjectid,", "the holder's binding decides whether it is a squat");
    }

    /// <summary>
    /// Owner round 4 item 4: "every read keeps using the secured column". The only query that filters on the mirror
    /// is the diagnostic one above, which names a key conflict's holder; every read that decides who a contact IS
    /// filters on the field-secured binding.
    /// </summary>
    [Fact]
    public void EveryIdentityRead_FiltersOnTheSecuredBinding_NeverOnTheMirror()
    {
        DataverseContactIdentityStore.BuildOidLookupPath(Oid)
            .Should().Contain("$filter=sprk_externalobjectid eq").And.NotContain("sprk_externalobjectidkey eq");
        DataverseContactIdentityStore.BuildEmailLookupPath("a@b.example").Should().NotContain("sprk_externalobjectidkey eq");
        DataverseContactIdentityStore.BuildProbePath().Should().NotContain("sprk_externalobjectidkey");

        var sdk = ContactBindingDecision.ContactsBoundToQuery(Oid);
        sdk.Criteria.Conditions.Should().ContainSingle().Which.AttributeName.Should().Be("sprk_externalobjectid");
    }

    // ── ADR-028 A9 (r3 task 046): the two home reads. The in-memory double returns a superset, so these pin the filter ──

    private static readonly Guid HomeTenant = Guid.Parse("BBBBBBBB-0000-4000-8000-000000000002");

    [Fact]
    public void TheHomeLinkLookup_FiltersOnTheSecuredPairOnly_NeverOnTheMirrorsOrTheAadObjectId()
    {
        var path = DataverseContactIdentityStore.BuildHomeLinkLookupPath(HomeTenant, Oid);

        path.Should().StartWith("systemusers?");
        path.Should().Contain("$filter=sprk_hometenantid eq 'bbbbbbbb-0000-4000-8000-000000000002'"
                              + " and sprk_homeobjectid eq 'aaaaaaaa-0000-4000-8000-000000000001'");
        path.Should().NotContain("sprk_hometenantidkey").And.NotContain("sprk_homeobjectidkey",
            "a mirror edit can block a link but must never redirect a sign-in (design §5)");
        path.Should().NotContain("azureactivedirectoryobjectid", "a home oid is never compared with the Spaarke-tenant oid (finding (d))");
        path.Should().NotContain("isdisabled eq", "a disabled link must be seen so it can deny");
        path.Should().Contain("$top=2").And.Contain("isdisabled");
        path.Should().EndWith("&$orderby=isdisabled asc",
            "the two-row sample must be enabled-first, or [disabled, enabled, enabled] could return one of each and resolve");
    }

    [Fact]
    public void TheHomeContactLookup_FiltersOnPlaneTenantAndOid_AnyState()
    {
        var path = DataverseContactIdentityStore.BuildHomeContactLookupPath(HomeTenant, Oid);

        path.Should().Contain("$filter=sprk_identityplane eq 100000002"
                              + " and sprk_externaltenantid eq 'bbbbbbbb-0000-4000-8000-000000000002'"
                              + " and sprk_externalobjectid eq 'aaaaaaaa-0000-4000-8000-000000000001'");
        path.Should().NotContain("sprk_externalobjectidkey eq");
        path.Should().NotContain("statecode eq", "an inactive match must be seen so it can deny");
        path.Should().Contain("$top=2").And.Contain("sprk_externaltenantid").And.Contain("sprk_identityplane");
        DataverseContactIdentityStore.ContactSelect.Should().NotContain("sprk_externaltenantid",
            "no other contact read may depend on the A9 column");
    }

    [Fact]
    public void ParseHomeLinkRow_ReadsTheSecuredPairAndIsDisabled()
    {
        using var doc = JsonDocument.Parse(
            "{\"systemuserid\":\"cccccccc-0000-4000-8000-000000000003\",\"isdisabled\":false,"
            + "\"sprk_hometenantid\":\"bbbbbbbb-0000-4000-8000-000000000002\","
            + "\"sprk_homeobjectid\":\"aaaaaaaa-0000-4000-8000-000000000001\"}");

        DataverseContactIdentityStore.ParseHomeLinkRow(doc.RootElement).Should().Be(new HomeLinkRow(
            Guid.Parse("cccccccc-0000-4000-8000-000000000003"), false,
            "bbbbbbbb-0000-4000-8000-000000000002", "aaaaaaaa-0000-4000-8000-000000000001"));
    }

    [Fact]
    public void EveryContactRead_SelectsTheBindingAndTheMirror_SoASquatCanBeToldApart()
        => DataverseContactIdentityStore.ContactSelect.Split(',')
            .Should().Contain(new[] { "sprk_externalobjectid", "sprk_externalobjectidkey", "sprk_identityplane" },
                "without the mirror a key conflict's holder cannot be told apart from the contact that owns the oid");

    [Theory]
    [InlineData("{\"error\":{\"code\":\"0x80060892\",\"message\":\"Entity Key External Object ID Unique violated. A record with the same value for External Object ID Key already exists.\"}}", true)]
    [InlineData("{\"error\":{\"code\":\"0x80060882\",\"message\":\"The version of the existing record doesn't match the RowVersion property provided.\"}}", false)]
    [InlineData("{\"error\":{\"code\":\"0x80060888\",\"message\":\"The key in the request URI is not valid for resource 'Microsoft.Dynamics.CRM.contact'.\"}}", false)]
    [InlineData("{\"error\":{\"code\":\"0x80060891\",\"message\":\"adjacent code\"}}", false)]
    [InlineData(null, false)]
    public void IsDuplicateKey_RecognisesTheUniqueIndexFault_AndNothingElse(string? body, bool expected)
        => DataverseContactIdentityStore.IsDuplicateKey(body).Should().Be(expected,
            "a stale-version 412 is retried after a re-read; a duplicate-key 412 never succeeds on retry");

    [Fact]
    public void TheFlagPayloads_SetAndClearAllFiveColumns_IncludingEveryParty()
    {
        var flag = new CollisionFlag(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), Oid,
            IdentityPlaneMarker.External, IdentityCollisionReason.EmailAmbiguous);
        flag = ContactBindingDecision.FlagWith(flag, new CollisionParty(null, IdentityPlaneMarker.External,
            IdentityCollisionReason.InviteMatchesWorkforceContact, new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero)));

        var set = DataverseContactIdentityStore.FlagPayload(flag);
        set["sprk_identitycollisionon"].Should().Be("2026-10-01T09:00:00Z");
        set["sprk_identitycollisionreason"].Should().Be(100000001);
        DataverseContactIdentityStore.ParseParties((string?)set["sprk_identitycollisionparties"])!
            .Should().HaveCount(2, "the parties column carries every party, the first included");
        DataverseContactIdentityStore.ClearFlagPayload().Values.Should().AllSatisfy(v => v.Should().BeNull());
        DataverseContactIdentityStore.ClearFlagPayload().Keys.Should().BeEquivalentTo(set.Keys);
    }

    [Fact]
    public void TheParties_RoundTrip_ThroughTheColumnTheBffWrites()
    {
        var on = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var parties = new[]
        {
            new CollisionParty(Oid, IdentityPlaneMarker.Workforce, IdentityCollisionReason.BoundToDifferentOid, on),
            new CollisionParty(null, IdentityPlaneMarker.External, IdentityCollisionReason.InviteMatchesWorkforceContact, on.AddHours(1)),
        };

        var back = DataverseContactIdentityStore.ParseParties(DataverseContactIdentityStore.SerializeParties(parties))!;

        back.Should().HaveCount(2);
        back[0].IsSameCollision(parties[0]).Should().BeTrue();
        back[1].IsSameCollision(parties[1]).Should().BeTrue();
        back[1].FlaggedOn.Should().Be(on.AddHours(1));
    }

    [Fact]
    public void ParseContactRow_ReadsEveryRecordedParty()
    {
        // Verifier finding 3: a second identity's collision lives in the parties column. A reader that ignored
        // it would see one party, and the job would clear the flag when that one was resolved.
        using var doc = JsonDocument.Parse("""
            {"@odata.etag":"W/\"7\"","contactid":"cccccccc-0000-4000-8000-00000000000a","statecode":0,
             "sprk_externalobjectid":"dddddddd-0000-4000-8000-00000000000d","sprk_identityplane":100000000,
             "sprk_identitycollisionon":"2026-10-01T09:00:00Z","sprk_identitycollisionoid":"aaaaaaaa-0000-4000-8000-000000000001",
             "sprk_identitycollisionplane":100000001,"sprk_identitycollisionreason":100000000,
             "sprk_identitycollisionparties":"[{\"oid\":\"aaaaaaaa-0000-4000-8000-000000000001\",\"plane\":100000001,\"reason\":100000000,\"on\":\"2026-10-01T09:00:00Z\"},{\"oid\":\"bbbbbbbb-0000-4000-8000-000000000002\",\"plane\":100000001,\"reason\":100000000,\"on\":\"2026-10-01T09:05:00Z\"}]"}
            """);

        var flag = DataverseContactIdentityStore.ParseContactRow(doc.RootElement)!.Flag!;

        flag.Parties.Should().HaveCount(2);
        flag.Parties[1].Oid.Should().Be(Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002"));
        flag.HasUnreadableParties.Should().BeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[{\"oid\":\"not-a-guid\",\"on\":\"2026-10-01T09:00:00Z\"}]")]
    [InlineData("[{\"oid\":\"bbbbbbbb-0000-4000-8000-000000000002\",\"plane\":100000001,\"reason\":100000000,\"on\":\"2026-10-01T09:00:00Z\"}]")]
    public void ParseContactRow_APartiesColumnItCannotTrust_MarksTheFlagUnreadable_NeverDropsIt(string parties)
    {
        // The third row is well-formed but does not start with the summary's party — someone edited one and not
        // the other. Either way the flag may name parties we cannot see: kept, never overwritten or cleared.
        var row = $$"""
            {"@odata.etag":"W/\"7\"","contactid":"cccccccc-0000-4000-8000-00000000000a","statecode":0,
             "sprk_identitycollisionon":"2026-10-01T09:00:00Z","sprk_identitycollisionoid":"aaaaaaaa-0000-4000-8000-000000000001",
             "sprk_identitycollisionplane":100000001,"sprk_identitycollisionreason":100000000,
             "sprk_identitycollisionparties":{{JsonSerializer.Serialize(parties)}}}
            """;
        using var doc = JsonDocument.Parse(row);

        var flag = DataverseContactIdentityStore.ParseContactRow(doc.RootElement)!.Flag!;

        flag.HasUnreadableParties.Should().BeTrue();
        ContactBindingDecision.ReconcileFlag(flag, Array.Empty<CollisionParty>(), false).Action
            .Should().Be(FlagReconciliationAction.Keep);
    }

    [Fact]
    public void ParseContactRow_ReadsTheBindingTheFlagAndTheETag()
    {
        using var doc = JsonDocument.Parse("""
            {"@odata.etag":"W/\"42\"","contactid":"cccccccc-0000-4000-8000-00000000000a","statecode":0,
             "emailaddress1":"a@b.example","sprk_externalobjectid":"AAAAAAAA-0000-4000-8000-000000000001",
             "sprk_externalobjectidkey":"aaaaaaaa-0000-4000-8000-000000000001",
             "sprk_identityplane":100000001,"sprk_identitycollisionon":"2026-10-01T09:00:00Z",
             "sprk_identitycollisionoid":"aaaaaaaa-0000-4000-8000-000000000001","sprk_identitycollisionplane":100000000,
             "sprk_identitycollisionreason":100000000}
            """);

        var row = DataverseContactIdentityStore.ParseContactRow(doc.RootElement)!;

        row.ETag.Should().Be("W/\"42\"");
        row.Binding.Kind.Should().Be(BindingKind.Bound);
        row.Binding.Oid.Should().Be(Oid);
        row.RawKeyMirror.Should().Be("aaaaaaaa-0000-4000-8000-000000000001");
        ContactBindingDecision.MirrorHeldWithoutBinding(row, Oid).Should().BeFalse("the mirror matches its own binding");
        row.Flag!.Reason.Should().Be(IdentityCollisionReason.BoundToDifferentOid);
        row.Flag.CollidingPlane.Should().Be(IdentityPlaneMarker.External);
    }

    [Fact]
    public void ParseSystemUserRow_CarriesTheExpandedLinkedContact()
    {
        using var doc = JsonDocument.Parse("""
            {"@odata.etag":"W/\"7\"","systemuserid":"11111111-0000-4000-8000-000000000001",
             "azureactivedirectoryobjectid":"aaaaaaaa-0000-4000-8000-000000000001","internalemailaddress":"u@x.example",
             "domainname":"u_x.example#EXT#@t.onmicrosoft.com","_sprk_primarycontact_value":"cccccccc-0000-4000-8000-00000000000a",
             "sprk_PrimaryContact":{"contactid":"cccccccc-0000-4000-8000-00000000000a","statecode":0}}
            """);

        var row = DataverseContactIdentityStore.ParseSystemUserRow(doc.RootElement)!;

        row.Oid.Should().Be(Oid);
        row.LinkedContact!.ContactId.Should().Be(row.PrimaryContactId!.Value);
        ContactBindingDecision.IsGuestDomainName(row.DomainName).Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"0x80060888\",\"message\":\"Could not find a property named 'sprk_identityplane' on type 'Microsoft.Dynamics.CRM.contact'.\"}}", LookupStatus.ColumnMissing)]
    [InlineData("{\"error\":{\"code\":\"0x80040220\",\"message\":\"Principal user is missing prvReadContact\"}}", LookupStatus.Failed)]
    [InlineData(null, LookupStatus.Failed)]
    public void ClassifyError_TellsAMissingColumnFromAnyOtherFailure(string? body, LookupStatus expected)
        => DataverseContactIdentityStore.ClassifyError(body).Should().Be(expected);

    [Fact]
    public void IsKeyMissing_RecognisesTheUndefinedAlternateKeyError()
        => DataverseContactIdentityStore.IsKeyMissing(
                "{\"error\":{\"code\":\"0x80060888\",\"message\":\"The key in the request URI is not valid for resource 'Microsoft.Dynamics.CRM.contact'.\"}}")
            .Should().BeTrue();

    [Fact]
    public void ClassifyProbe_EveryMarkedRowWithoutAnOid_IsMasking()
    {
        var masked = ContactLookup.Of(
            new ContactBindingRow(Guid.NewGuid(), 0, null, 100000000),
            new ContactBindingRow(Guid.NewGuid(), 0, null, 100000001));
        var readable = ContactLookup.Of(
            new ContactBindingRow(Guid.NewGuid(), 0, null, 100000000),           // one orphaned marker
            new ContactBindingRow(Guid.NewGuid(), 0, Oid.ToString(), 100000001));

        DataverseContactIdentityStore.ClassifyProbe(masked).Should().Be(BindingReadability.Masked);
        DataverseContactIdentityStore.ClassifyProbe(readable).Should().Be(BindingReadability.Readable);
        DataverseContactIdentityStore.ClassifyProbe(ContactLookup.Of()).Should().Be(BindingReadability.NoMarkedRows);
        DataverseContactIdentityStore.ClassifyProbe(ContactLookup.Failed).Should().Be(BindingReadability.Failed);
    }

    [Fact]
    public void NoContactQuery_NamesTheRetiredColumn()
    {
        new[]
        {
            DataverseContactIdentityStore.BuildOidLookupPath(Oid),
            DataverseContactIdentityStore.BuildEmailLookupPath("a@b.example"),
            DataverseContactIdentityStore.BuildProbePath(),
            DataverseContactIdentityStore.BuildFlaggedContactScanPath(),
            DataverseContactIdentityStore.BuildKeyMirrorLookupPath(Oid),
        }.Should().AllSatisfy(p => p.Should().NotContain("azureactivedirectoryobjectid"));
    }

    /// <summary>
    /// A bind or a link is ALWAYS conditional on the row version read with the row. <c>*</c> only says "the row
    /// exists": sent for a bind it would overwrite a binding a concurrent sign-in just made, and sent for a link it
    /// would re-point a link that must never be re-pointed. No version means no write.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*")]
    [InlineData(" * ")]
    public void ABindOrLink_WithoutARowVersion_IsNeverSentUnconditionally(string? etag)
        => DataverseContactIdentityStore.RowVersionPrecondition(etag).Should().BeNull();

    [Fact]
    public void ABindOrLink_CarriesTheRowVersionItWasReadWith()
        => DataverseContactIdentityStore.RowVersionPrecondition("W/\"4711\"").Should().Be("W/\"4711\"");

    /// <summary>
    /// A response body that is not JSON is a could-not-read (it denies through the decision), never an exception
    /// escaping the store's three-state contract.
    /// </summary>
    [Fact]
    public void AMalformedResponseBody_IsACouldNotRead_NotAnException()
    {
        DataverseContactIdentityStore.TryParseJson("<html>gateway timeout</html>").Should().BeNull();
        using var doc = DataverseContactIdentityStore.TryParseJson("{\"value\":[]}");
        doc.Should().NotBeNull();
    }

    // ── Task 184: the bind-intent reads and the pending-invite marker ─────────────────────────────────────────

    /// <summary>
    /// Task 185 slice B2 (option (c), design §6): the bind-intent read has no grant leg. The store has no grant path
    /// builder or grant-answer interpreter left, and its one intent query (the marker's) reads no grant entity set, so no
    /// grant can bind and no grant fault can deny. Before B2 this was
    /// <c>TheIntentGrantLeg_IsTheReadPathsOwnActiveUnexpiredContactGrantFilter</c>.
    /// </summary>
    [Fact]
    public void TheBindIntentRead_HasNoGrantLeg()
    {
        typeof(DataverseContactIdentityStore)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.Name)
            .Should().NotContain(new[] { "BuildActiveContactGrantPath", "InterpretGrantLeg" });
        DataverseContactIdentityStore.BuildInviteMarkerPath(Guid.NewGuid())
            .Should().StartWith("contacts(").And.NotContain("sprk_externalrecordaccess");
    }

    [Fact]
    public void TheMarkerAndItsCodeHash_AreReadTogetherInTheirOwnQuery_NeverInTheBindingSelect()
    {
        var contact = Guid.NewGuid();

        DataverseContactIdentityStore.BuildInviteMarkerPath(contact)
            .Should().Be($"contacts({contact:D})?$select=contactid,sprk_externalinvitedon,sprk_externalinvitecodehash");
        DataverseContactIdentityStore.ContactSelect.Should().NotContain(ContactBindingDecision.ExternalInvitedOnColumn,
            "a BFF deployed before the column would otherwise fail every binding read, on every plane");
        DataverseContactIdentityStore.ContactSelect.Should().NotContain(ContactBindingDecision.ExternalInviteCodeHashColumn,
            "the hash is read only where E11 needs it (and never by the Readers profile)");
    }

    // ── Task 184 (fix round 1, F4-1): how each intent leg's answer is read ─────────────────────────────────

    private static readonly Guid IntentContact = Guid.Parse("CCCCCCCC-0000-4000-8000-000000000184");
    private static readonly Guid OtherContact = Guid.Parse("DDDDDDDD-0000-4000-8000-000000000184");

    /// <summary>
    /// The marker leg: a 404 is "this contact was never invited" (it does not exist); a column the environment lacks is
    /// ColumnMissing (no evidence — denied not invited); a body that is absent, not JSON, or about another contact is
    /// a fault — never "not invited" and never "invited". MUTATION PROOF (M22): reporting an unreadable body as Read fails
    /// the not-json, no-body, not-an-object and another-contact rows. Task 185: the stored hash comes back with the marker
    /// (only from this contact's readable row).
    /// </summary>
    [Theory]
    [InlineData("marked", LookupStatus.Read, true)]
    [InlineData("marked-with-hash", LookupStatus.Read, true)]
    [InlineData("unmarked", LookupStatus.Read, false)]
    [InlineData("not-a-date", LookupStatus.Read, false)]
    [InlineData("not-found", LookupStatus.Read, false)]
    [InlineData("not-json", LookupStatus.Failed, false)]
    [InlineData("no-body", LookupStatus.Failed, false)]
    [InlineData("json-not-an-object", LookupStatus.Failed, false)]
    [InlineData("another-contact", LookupStatus.Failed, false)]
    [InlineData("no-contact-id", LookupStatus.Failed, false)]
    public void TheMarkerLeg_IsEvidence_OnlyForThisContactsReadableRow(
        string scenario, LookupStatus expectedStatus, bool expectedInvited)
    {
        var (code, body) = scenario switch
        {
            "marked" => (HttpStatusCode.OK, $"{{\"contactid\":\"{IntentContact:D}\",\"sprk_externalinvitedon\":\"2026-10-13T09:30:05Z\"}}"),
            "marked-with-hash" => (HttpStatusCode.OK, $"{{\"contactid\":\"{IntentContact:D}\",\"sprk_externalinvitedon\":\"2026-10-13T09:30:05Z\",\"sprk_externalinvitecodehash\":\"{StoredHash}\"}}"),
            "unmarked" => (HttpStatusCode.OK, $"{{\"contactid\":\"{IntentContact:D}\",\"sprk_externalinvitedon\":null}}"),
            "not-a-date" => (HttpStatusCode.OK, $"{{\"contactid\":\"{IntentContact:D}\",\"sprk_externalinvitedon\":\"soon\"}}"),
            "not-found" => (HttpStatusCode.NotFound, (string?)null),
            "not-json" => (HttpStatusCode.OK, "<html>gateway timeout</html>"),
            "no-body" => (HttpStatusCode.OK, null),
            "json-not-an-object" => (HttpStatusCode.OK, "[]"),
            "another-contact" => (HttpStatusCode.OK, $"{{\"contactid\":\"{OtherContact:D}\",\"sprk_externalinvitedon\":\"2026-10-13T09:30:05Z\",\"sprk_externalinvitecodehash\":\"{StoredHash}\"}}"),
            "no-contact-id" => (HttpStatusCode.OK, "{\"sprk_externalinvitedon\":\"2026-10-13T09:30:05Z\"}"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var (status, invitedOn, hash) = DataverseContactIdentityStore.InterpretMarkerLeg(LookupStatus.Read, code, body, IntentContact);

        status.Should().Be(expectedStatus);
        (invitedOn is not null).Should().Be(expectedInvited);
        if (expectedInvited)
        {
            invitedOn.Should().Be(new DateTimeOffset(2026, 10, 13, 9, 30, 5, TimeSpan.Zero));
        }

        hash.Should().Be(scenario == "marked-with-hash" ? StoredHash : null);
    }

    /// <summary>A well-formed stored value (task 185's <c>v1.</c> format), as the marker read returns it.</summary>
    private static readonly string StoredHash = InviteCode.Create(IntentContact).StoredValue;

    [Theory]
    [InlineData(LookupStatus.ColumnMissing)]
    [InlineData(LookupStatus.Failed)]
    public void TheMarkerLeg_KeepsAFaultedRead_AndIgnoresItsBody(LookupStatus fault)
        => DataverseContactIdentityStore.InterpretMarkerLeg(fault, HttpStatusCode.BadRequest,
                $"{{\"contactid\":\"{IntentContact:D}\",\"sprk_externalinvitedon\":\"2026-10-13T09:30:05Z\",\"sprk_externalinvitecodehash\":\"{StoredHash}\"}}", IntentContact)
            .Should().Be((fault, (DateTimeOffset?)null, (string?)null));

    /// <summary>
    /// Task 185: the marker write carries the marker (UTC) AND the new code's stored hash — exactly those two columns, in
    /// the one PATCH, so a re-invite replaces both together. Before 185 this was <c>TheMarkerWrite_CarriesOnlyTheMarker_InUtc</c>.
    /// </summary>
    [Fact]
    public void TheMarkerWrite_CarriesTheMarkerInUtc_AndTheCodeHash_AndNothingElse()
    {
        var payload = DataverseContactIdentityStore.InviteMarkerPayload(
            new DateTimeOffset(2026, 10, 13, 11, 30, 5, TimeSpan.FromHours(2)), StoredHash);

        payload.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_externalinvitedon"] = "2026-10-13T09:30:05Z",
            ["sprk_externalinvitecodehash"] = StoredHash,
        });
    }

    /// <summary>
    /// Task 185 (AC2): the consuming bind's PATCH body is the plain bind's three columns PLUS the hash set to null — one
    /// request, so the code is consumed by the write that binds. The marker is not in it. The plain bind (the workforce and
    /// systemuser planes) never names the hash column, so those binds stay byte-identical.
    /// </summary>
    [Fact]
    public void TheConsumingBind_IsThePlainBindPlusTheHashCleared_InOneBody()
    {
        var plain = DataverseContactIdentityStore.BindPayload(Oid, IdentityPlaneMarker.External);
        var consuming = DataverseContactIdentityStore.BindAndConsumeInvitePayload(Oid, IdentityPlaneMarker.External);

        consuming.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_externalobjectid"] = "aaaaaaaa-0000-4000-8000-000000000001",
            ["sprk_externalobjectidkey"] = "aaaaaaaa-0000-4000-8000-000000000001",
            ["sprk_identityplane"] = (int)IdentityPlaneMarker.External,
            ["sprk_externalinvitecodehash"] = null,
        });
        JsonSerializer.Serialize(consuming).Should().Contain("\"sprk_externalinvitecodehash\":null",
            "the request body clears the hash; a missing key would leave it in place");
        plain.Keys.Should().BeEquivalentTo("sprk_externalobjectid", "sprk_externalobjectidkey", "sprk_identityplane");
    }

    /// <summary>
    /// Task 185: the consuming bind is never unconditional. Without a row version (null, empty, or <c>*</c>) it is refused
    /// before any request: no token is acquired and no HTTP client is created.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    public async Task TheConsumingBind_WithoutARowVersion_SendsNothing(string? etag)
    {
        var requests = 0;
        var store = new DataverseContactIdentityStore(
            new CountingHttpClientFactory(() => requests++),
            _ => { requests++; throw new InvalidOperationException("no request may be made"); },
            "https://org.test.invalid",
            NullLogger.Instance);

        var write = await store.BindOidAndConsumeInviteAsync(Guid.NewGuid(), etag, Oid, IdentityPlaneMarker.External,
            CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Failed);
        write.Error.Should().Be("no row version for the bind");
        requests.Should().Be(0);
    }

    // ── #2005: the consuming bind on an environment without the hash column ──────────────────────────────
    // The fallback is the store's own orchestration of two PATCHes, with no pure seam, so these use a hand-written fake
    // HTTP handler that records each request and answers from a script (testing.md B1 bans Mock<HttpMessageHandler>, not a
    // fake; the same shape as ExternalDataServiceServiceRequestCreateTests).

    private const string HashColumnMissingBody =
        "{\"error\":{\"code\":\"0x80048d19\",\"message\":\"Error identified in Payload provided by the user for Entity :'contacts'. "
        + "The property 'sprk_externalinvitecodehash' does not exist on type 'Microsoft.Dynamics.CRM.contact'.\"}}";

    /// <summary>
    /// #2005: every bind now names the hash column. Where it is not provisioned, the platform refuses the whole PATCH as a
    /// missing property (nothing written); the store re-sends the PLAIN bind under the SAME row version, so the bind
    /// succeeds exactly as it did before the schema. MUTATION PROOF: without the fallback the bind answers Failed after
    /// one request.
    /// </summary>
    [Fact]
    public async Task TheConsumingBind_WhereTheHashColumnIsMissing_SendsThePlainBind_UnderTheSameRowVersion()
    {
        var http = new ScriptedHttp(
            (HttpStatusCode.BadRequest, HashColumnMissingBody),
            (HttpStatusCode.NoContent, null));

        var write = await http.Store().BindOidAndConsumeInviteAsync(IntentContact, "W/\"42\"", Oid,
            IdentityPlaneMarker.Workforce, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Written);
        http.Requests.Should().HaveCount(2);
        http.Requests.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Patch);
            r.Uri.Should().EndWith($"/contacts({IntentContact:D})");
            r.IfMatch.Should().Be("W/\"42\"", "the retry is conditional on the version the decision was made on");
        });
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(http.Requests[0].Body)!.Keys
            .Should().Contain("sprk_externalinvitecodehash");
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(http.Requests[1].Body)!.Keys
            .Should().BeEquivalentTo("sprk_externalobjectid", "sprk_externalobjectidkey", "sprk_identityplane");
    }

    /// <summary>
    /// #2005: the fallback is ONLY for the hash column being missing. A provisioned column binds and consumes in one
    /// request; a 412, a duplicate key or any other fault is returned as it is, with no second request.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NoContent, null, StoreWriteStatus.Written)]
    [InlineData(HttpStatusCode.PreconditionFailed, "{\"error\":{\"code\":\"0x80060882\",\"message\":\"The version of the existing record doesn't match\"}}", StoreWriteStatus.PreconditionFailed)]
    [InlineData(HttpStatusCode.PreconditionFailed, "{\"error\":{\"code\":\"0x80060892\",\"message\":\"A record that has the attribute values External Object ID already exists.\"}}", StoreWriteStatus.KeyConflict)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"The property 'sprk_somethingelse' does not exist on type 'Microsoft.Dynamics.CRM.contact'.\"}}", StoreWriteStatus.Failed)]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"Principal user is missing prvWriteContact privilege (sprk_externalinvitecodehash)\"}}", StoreWriteStatus.Forbidden)] // r3 047: a permission fault is Forbidden (callers deny it like Failed)
    public async Task TheConsumingBind_FallsBackOnlyForAMissingHashColumn(HttpStatusCode code, string? body, StoreWriteStatus expected)
    {
        var http = new ScriptedHttp((code, body));

        var write = await http.Store().BindOidAndConsumeInviteAsync(IntentContact, "W/\"42\"", Oid,
            IdentityPlaneMarker.External, CancellationToken.None);

        write.Status.Should().Be(expected);
        http.Requests.Should().ContainSingle().Which.Body.Should().Contain("\"sprk_externalinvitecodehash\":null");
    }

    // ── #2005 K1: the invite mark is never unconditional ──────────────────────────────────────────────────

    /// <summary>
    /// #2005 K1: without a row version (null, empty or <c>*</c>) the marker-and-hash write is refused before any request:
    /// <c>If-Match: *</c> would land on a contact a workforce sign-in bound since it was read. MUTATION PROOF: the former
    /// <c>?? "*"</c> fallback sends a request and fails this test.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    public async Task TheInviteMark_WithoutARowVersion_SendsNothing(string? etag)
    {
        var requests = 0;
        var store = new DataverseContactIdentityStore(
            new CountingHttpClientFactory(() => requests++),
            _ => { requests++; throw new InvalidOperationException("no request may be made"); },
            "https://org.test.invalid",
            NullLogger.Instance);

        var write = await store.MarkInvitedAsync(IntentContact, etag, DateTimeOffset.UtcNow, StoredHash, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Failed);
        write.Error.Should().Be("no row version for the invite mark");
        requests.Should().Be(0);
    }

    /// <summary>#2005 K1: with a row version the mark is ONE PATCH conditional on exactly that version, never <c>*</c>.</summary>
    [Fact]
    public async Task TheInviteMark_IsConditionalOnTheRowVersion_NeverStar()
    {
        var http = new ScriptedHttp((HttpStatusCode.NoContent, null));

        var write = await http.Store().MarkInvitedAsync(IntentContact, "W/\"7\"", DateTimeOffset.UtcNow, StoredHash,
            CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Written);
        var request = http.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Patch);
        request.Uri.Should().EndWith($"/contacts({IntentContact:D})");
        request.IfMatch.Should().Be("W/\"7\"").And.NotBe("*");
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body)!.Keys
            .Should().BeEquivalentTo("sprk_externalinvitedon", "sprk_externalinvitecodehash");
    }

    /// <summary>Answers each request with the next scripted response and records what was sent.</summary>
    private sealed class ScriptedHttp(params (HttpStatusCode Code, string? Body)[] responses) : IHttpClientFactory
    {
        private readonly Queue<(HttpStatusCode Code, string? Body)> _responses = new(responses);

        public List<(HttpMethod Method, string Uri, string? IfMatch, string Body)> Requests { get; } = new();

        public DataverseContactIdentityStore Store()
            => new(this, _ => Task.FromResult("token"), "https://org.test.invalid", NullLogger.Instance);

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(ScriptedHttp owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
                var ifMatch = request.Headers.TryGetValues("If-Match", out var values) ? values.Single() : null;
                owner.Requests.Add((request.Method, request.RequestUri!.ToString(), ifMatch, body));
                var (code, text) = owner._responses.Dequeue();
                return new HttpResponseMessage(code)
                {
                    Content = new StringContent(text ?? string.Empty),
                };
            }
        }
    }

    /// <summary>Fails the test's count if the store ever asks for an HTTP client (no HTTP double: nothing is answered).</summary>
    private sealed class CountingHttpClientFactory(Action onCreate) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            onCreate();
            throw new InvalidOperationException("no request may be made");
        }
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"0x80048d19\",\"message\":\"Error identified in Payload ... The property 'sprk_externalinvitedon' does not exist on type 'Microsoft.Dynamics.CRM.contact'.\"}}", true)]
    [InlineData("Invalid property 'sprk_externalinvitedon' was found in entity 'Microsoft.Dynamics.CRM.contact'.", true)]
    [InlineData("The property 'sprk_somethingelse' does not exist on type 'Microsoft.Dynamics.CRM.contact'.", false)]
    [InlineData("Principal user is missing prvWriteContact privilege (sprk_externalinvitedon)", false)]
    [InlineData(null, false)]
    public void AMissingMarkerColumn_IsToldApartFromEveryOtherWriteFault(string? body, bool missing)
        => DataverseContactIdentityStore.IsWriteColumnMissing(body, "sprk_externalinvitedon").Should().Be(missing);

    /// <summary>
    /// Task 185: the marker write carries two columns, so a fault naming EITHER is ColumnMissing (a stamp with the marker
    /// column but not yet the hash column), and a fault naming neither is not.
    /// </summary>
    [Theory]
    [InlineData("The property 'sprk_externalinvitedon' does not exist on type 'Microsoft.Dynamics.CRM.contact'.", true)]
    [InlineData("The property 'sprk_externalinvitecodehash' does not exist on type 'Microsoft.Dynamics.CRM.contact'.", true)]
    [InlineData("Invalid property 'sprk_externalinvitecodehash' was found in entity 'Microsoft.Dynamics.CRM.contact'.", true)]
    [InlineData("The property 'sprk_somethingelse' does not exist on type 'Microsoft.Dynamics.CRM.contact'.", false)]
    [InlineData("Principal user is missing prvWriteContact privilege (sprk_externalinvitecodehash)", false)]
    public void AMissingMarkerOrHashColumn_IsColumnMissing_ForTheMarkerWrite(string body, bool missing)
        => DataverseContactIdentityStore.IsWriteColumnMissing(body, DataverseContactIdentityStore.InviteMarkerColumns.ToArray())
            .Should().Be(missing);

    [Theory]
    [InlineData("{\"sprk_externalinvitedon\":\"2026-10-13T09:30:05Z\"}", true)]
    [InlineData("{\"sprk_externalinvitedon\":null}", false)]
    [InlineData("{\"sprk_externalinvitedon\":\"not a date\"}", false)]
    [InlineData("{}", false)]
    public void TheMarker_ReadsAsEvidence_OnlyWhenItIsADateTime(string json, bool evidence)
    {
        using var doc = JsonDocument.Parse(json);
        (DataverseContactIdentityStore.ParseInvitedOn(doc.RootElement) is not null).Should().Be(evidence);
    }

    // ── r3 task 047: the A9 registration's reads and writes ──────────────────────────────────────────

    private static readonly Guid Team = Guid.Parse("7EA70000-0000-4000-8000-7EA700007EA7");

    [Fact]
    public void TheWorkforceHomeCreate_CarriesTenantOwnerAndDepartment_InLowercaseD_AndNoParentCustomer()
    {
        var payload = DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.WorkforceHome,
            new NewContactDetails("Mike", "Okafor", "mike@customer.example")
            {
                Department = "Legal", ExternalTenantId = HomeTenant, OwningTeamId = Team,
            });

        payload.Keys.Should().BeEquivalentTo(
            "lastname", "firstname", "emailaddress1", "department", "sprk_externalobjectid", "sprk_externalobjectidkey",
            "sprk_identityplane", "sprk_externaltenantid", "ownerid@odata.bind");
        payload["sprk_identityplane"].Should().Be(100000002);
        payload["sprk_externalobjectid"].Should().Be("aaaaaaaa-0000-4000-8000-000000000001");
        payload["sprk_externaltenantid"].Should().Be("bbbbbbbb-0000-4000-8000-000000000002",
            "the 046 home reads match only lowercase D text");
        payload["ownerid@odata.bind"].Should().Be("/teams(7ea70000-0000-4000-8000-7ea700007ea7)");
        payload.Keys.Should().NotContain(k => k.StartsWith("parentcustomerid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheWorkforceHomeCreate_RefusesAMissingTenantOrOwner_AndOtherPlanesRefuseThem()
    {
        var noTenant = new NewContactDetails("M", "O", "m@x.example") { OwningTeamId = Team };
        var noOwner = new NewContactDetails("M", "O", "m@x.example") { ExternalTenantId = HomeTenant };
        var workforceWithTenant = new NewContactDetails("M", "O", "m@x.example") { ExternalTenantId = HomeTenant };

        FluentActions.Invoking(() => DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.WorkforceHome, noTenant))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.WorkforceHome, noOwner))
            .Should().Throw<InvalidOperationException>().WithMessage("*refusing to create*");
        FluentActions.Invoking(() => DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.Workforce, workforceWithTenant))
            .Should().Throw<ArgumentException>();
    }

    // ── ISS-005 (#1673): the Workforce plane's creates carry the owner; no other non-home plane does ──────────────

    [Fact]
    public void TheWorkforceCreate_CarriesTheOwnerBind_AndNoHomeTenant()
    {
        var payload = DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.Workforce,
            new NewContactDetails("Pat", "Example", "pat@customer.example") { OwningTeamId = Team });

        payload["ownerid@odata.bind"].Should().Be("/teams(7ea70000-0000-4000-8000-7ea700007ea7)");
        payload["sprk_identityplane"].Should().Be((int)IdentityPlaneMarker.Workforce);
        payload.Keys.Should().NotContain("sprk_externaltenantid").And.NotContain("department");
    }

    [Fact]
    public void TheWorkforceCreate_WithNoOwner_IsStillBuilt_ForABinderOverAnotherEnvironment()
    {
        // The payload builder serves BOTH kinds of binder. This is the one built for a binder over ANOTHER environment
        // (ContactCreateOwner.NoneForForeignEnvironment, ContactIdentityBinderFactory only; ownerless by design, tracked in #2270).
        // A binder over this BFF's own environment always carries an owner: TheWorkforceCreate_CarriesTheOwnerBind_AndNoHomeTenant
        // above, and ContactIdentityBinderOwnerTeamTests / RegistrationContactLinkTests through the binder.
        var payload = DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.Workforce,
            new NewContactDetails("Pat", "Example", "pat@customer.example"));

        payload.Keys.Should().NotContain("ownerid@odata.bind");
    }

    [Fact]
    public void TheWorkforceCreate_RefusesAnEmptyOwnerTeam()
        => FluentActions.Invoking(() => DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.Workforce,
                new NewContactDetails("Pat", "Example", "pat@customer.example") { OwningTeamId = Guid.Empty }))
            .Should().Throw<InvalidOperationException>().WithMessage("*refusing to create*");

    [Fact]
    public void TheOwnerRule_AppliesToEveryPlane_AndTheHomeTenantToWorkforceHomeOnly()
    {
        // The owner rule is plane-independent (I-6): an External-plane create carries the owner too (the CIAM plane creates
        // nothing today; a future create there is owned the same way). Only the home directory is WorkforceHome's alone.
        var owned = DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.External,
            new NewContactDetails("P", "E", "p@x.example") { OwningTeamId = Team });
        owned["ownerid@odata.bind"].Should().Be("/teams(7ea70000-0000-4000-8000-7ea700007ea7)");

        FluentActions.Invoking(() => DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.External,
                new NewContactDetails("P", "E", "p@x.example") { OwningTeamId = Guid.Empty }))
            .Should().Throw<InvalidOperationException>().WithMessage("*refusing to create*");
        FluentActions.Invoking(() => DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.External,
                new NewContactDetails("P", "E", "p@x.example") { ExternalTenantId = HomeTenant }))
            .Should().Throw<ArgumentException>();
        DataverseContactIdentityStore.CreatePayload(Oid, IdentityPlaneMarker.External, new NewContactDetails("P", "E", "p@x.example"))
            .Keys.Should().NotContain("ownerid@odata.bind");
    }

    [Fact]
    public void TheHomeBind_WritesBindingMirrorPlaneAndTenant_InOneRequest()
        => DataverseContactIdentityStore.HomeBindPayload(HomeTenant, Oid).Should().BeEquivalentTo(
            new Dictionary<string, object?>
            {
                ["sprk_externalobjectid"] = "aaaaaaaa-0000-4000-8000-000000000001",
                ["sprk_externalobjectidkey"] = "aaaaaaaa-0000-4000-8000-000000000001",
                ["sprk_identityplane"] = 100000002,
                ["sprk_externaltenantid"] = "bbbbbbbb-0000-4000-8000-000000000002",
            });

    /// <summary>
    /// UAC review F1: the registration's home bind consumes the invite-code hash in the same request (task 185's invariant:
    /// a bound contact never holds an unused hash). The marker column is never touched.
    /// </summary>
    [Fact]
    public void TheHomeBindThatConsumesTheInvite_CarriesTheHashAsNull_TheTenantTheOidTheMirrorAndThePlane_AndNoMarker()
    {
        var payload = DataverseContactIdentityStore.HomeBindAndConsumeInvitePayload(HomeTenant, Oid);

        payload.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_externalobjectid"] = "aaaaaaaa-0000-4000-8000-000000000001",
            ["sprk_externalobjectidkey"] = "aaaaaaaa-0000-4000-8000-000000000001",
            ["sprk_identityplane"] = 100000002,
            ["sprk_externaltenantid"] = "bbbbbbbb-0000-4000-8000-000000000002",
            ["sprk_externalinvitecodehash"] = null,
        });
        JsonSerializer.Serialize(payload).Should().Contain("\"sprk_externalinvitecodehash\":null",
            "the request body clears the hash; a missing key would leave it in place");
        payload.Keys.Should().NotContain("sprk_externalinvitedon");
        DataverseContactIdentityStore.HomeBindPayload(HomeTenant, Oid).Keys.Should().NotContain("sprk_externalinvitecodehash",
            "the plain home payload is the fallback for an environment without the column");
    }

    private DataverseContactIdentityStore NewStoreWithNoTransport(ILogger? logger = null)
        => new(new CountingHttpClientFactory(() => { }), _ => Task.FromResult("token"), "https://org.test.invalid",
            logger ?? NullLogger.Instance);

    /// <summary>
    /// UAC review F1, column-missing case, through the real wiring (#2005 K3: the delegate seam let a dropped
    /// missingColumns argument pass). The home bind names the hash column; where it is not provisioned the platform refuses
    /// the PATCH, and the PLAIN home bind goes out once under the SAME row version.
    /// </summary>
    [Fact]
    public async Task TheHomeBind_WhereTheHashColumnIsMissing_SendsThePlainHomeBind_UnderTheSameRowVersion()
    {
        var http = new ScriptedHttp(
            (HttpStatusCode.BadRequest, HashColumnMissingBody),
            (HttpStatusCode.NoContent, null));

        var write = await http.Store().BindHomeOidAsync(IntentContact, "W/\"42\"", HomeTenant, Oid, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Written);
        http.Requests.Should().HaveCount(2);
        http.Requests.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Patch);
            r.Uri.Should().EndWith($"/contacts({IntentContact:D})");
            r.IfMatch.Should().Be("W/\"42\"", "the retry is conditional on the version the decision was made on");
        });
        http.Requests[0].Body.Should().Contain("\"sprk_externalinvitecodehash\":null");
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(http.Requests[1].Body)!.Keys
            .Should().BeEquivalentTo(DataverseContactIdentityStore.HomeBindPayload(HomeTenant, Oid).Keys);
    }

    /// <summary>The home bind falls back ONLY for the missing hash column; every other answer is one request, as is.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NoContent, null, StoreWriteStatus.Written)]
    [InlineData(HttpStatusCode.PreconditionFailed, "{\"error\":{\"code\":\"0x80060882\",\"message\":\"The version of the existing record doesn't match\"}}", StoreWriteStatus.PreconditionFailed)]
    [InlineData(HttpStatusCode.PreconditionFailed, "{\"error\":{\"code\":\"0x80060892\",\"message\":\"A record that has the attribute values External Object ID already exists.\"}}", StoreWriteStatus.KeyConflict)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"The property 'sprk_somethingelse' does not exist on type 'Microsoft.Dynamics.CRM.contact'.\"}}", StoreWriteStatus.Failed)]
    public async Task TheHomeBind_FallsBackOnlyForAMissingHashColumn(HttpStatusCode code, string? body, StoreWriteStatus expected)
    {
        var http = new ScriptedHttp((code, body));

        var write = await http.Store().BindHomeOidAsync(IntentContact, "W/\"42\"", HomeTenant, Oid, CancellationToken.None);

        write.Status.Should().Be(expected);
        http.Requests.Should().ContainSingle().Which.Body.Should().Contain("\"sprk_externalinvitecodehash\":null");
    }

    /// <summary>
    /// UAC review K2: a read whose OData filter carries the caller's email logs a fixed label, never the path (which holds
    /// the URL-escaped email, PII). The fault here is the token source throwing, which reaches the same GetAsync warning as
    /// a refused response without an HTTP double.
    /// </summary>
    [Fact]
    public async Task AReadThatCarriesTheEmail_LogsAFixedLabel_NeverTheEmail()
    {
        const string email = "o'brien@firm.example";
        var logger = new CapturingLogger();
        var store = new DataverseContactIdentityStore(new CountingHttpClientFactory(() => { }),
            _ => throw new InvalidOperationException("token source down"), "https://org.test.invalid", logger);

        (await store.FindActiveContactsByEmailAsync(email, CancellationToken.None)).Status.Should().Be(LookupStatus.Failed);
        (await store.FindInactiveContactsByEmailAsync(email, CancellationToken.None)).Status.Should().Be(LookupStatus.Failed);
        (await store.FindEnabledSystemUsersByEmailAsync(email, CancellationToken.None)).Status.Should().Be(LookupStatus.Failed);

        logger.Messages.Should().HaveCount(3);
        logger.Messages.Should().OnlyContain(m =>
            !m.Contains("brien", StringComparison.OrdinalIgnoreCase) && !m.Contains("firm.example", StringComparison.OrdinalIgnoreCase)
            && !m.Contains(Uri.EscapeDataString(email), StringComparison.OrdinalIgnoreCase));
        logger.Messages.Should().Contain(m => m.Contains("contacts by email"))
            .And.Contain(m => m.Contains("inactive contacts by email"))
            .And.Contain(m => m.Contains("systemusers by email"));

        // Control: a read with no email keeps logging its path (the warning is not silenced wholesale).
        logger.Messages.Clear();
        await store.FindContactsByOidAsync(Oid, CancellationToken.None);
        logger.Messages.Should().ContainSingle().Which.Should().Contain("aaaaaaaa-0000-4000-8000-000000000001");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public void TheWriteRequest_NeverOptsInToDuplicateDetection()
    {
        // Owner 2026-10-15: duplicate-detection rules run on API writes only when the caller sends
        // MSCRM.SuppressDuplicateDetection: false. The registration's create and bind must never send it.
        var (method, path, payload) = DataverseContactIdentityStore.BuildCreateRequest(Oid, IdentityPlaneMarker.WorkforceHome,
            new NewContactDetails("M", "O", "m@x.example") { ExternalTenantId = HomeTenant, OwningTeamId = Team });
        using var create = DataverseContactIdentityStore.BuildWriteRequest(
            method, new Uri("https://org.example/api/data/v9.2/" + path), payload, "token", null);
        using var bind = DataverseContactIdentityStore.BuildWriteRequest(HttpMethod.Patch,
            new Uri("https://org.example/api/data/v9.2/contacts(" + Oid + ")"),
            DataverseContactIdentityStore.HomeBindPayload(HomeTenant, Oid), "token", ("If-Match", "W/\"1\""));

        foreach (var request in new[] { create, bind })
        {
            request.Headers.Select(h => h.Key).Concat(request.Content!.Headers.Select(h => h.Key))
                .Should().NotContain(h => h.StartsWith("MSCRM.", StringComparison.OrdinalIgnoreCase));
        }

        create.Method.Should().Be(HttpMethod.Post);
        bind.Headers.GetValues("If-Match").Should().Equal("W/\"1\"");
    }

    [Fact]
    public void TheLicensedUserRead_IsEnabledUsers_ByEitherEmailColumn_Escaped()
    {
        var path = DataverseContactIdentityStore.BuildLicensedUserEmailLookupPath("o'neil@customer.example");

        path.Should().Contain("isdisabled eq false");
        path.Should().Contain("internalemailaddress eq 'o%27%27neil%40customer.example'")
            .And.Contain("domainname eq 'o%27%27neil%40customer.example'");
        path.Should().Contain(" or ").And.Contain("$top=2");
    }

    [Fact]
    public void TheOwnerTeamRead_IsTheBusinessUnitsDefaultOwnerTeam()
        => DataverseContactIdentityStore.BuildDefaultOwnerTeamPath(Team).Should().Be(
            "teams?$select=teamid&$filter=_businessunitid_value eq 7ea70000-0000-4000-8000-7ea700007ea7"
            + " and isdefault eq true and teamtype eq 0&$top=2");

    [Fact]
    public void ClassifyHomeProbe_AMaskedSecuredColumn_OrAFailedRead_IsNeverReadable()
    {
        var homeRow = new ContactBindingRow(Guid.NewGuid(), 0, Oid.ToString(), 100000002, RawExternalTenantId: HomeTenant.ToString());
        var maskedHomeRow = homeRow with { RawExternalTenantId = null };
        var link = new HomeLinkRow(Guid.NewGuid(), false, HomeTenant.ToString(), Oid.ToString());
        var maskedLink = new HomeLinkRow(Guid.NewGuid(), false, null, null);

        DataverseContactIdentityStore.ClassifyHomeProbe(ContactLookup.Of(homeRow), HomeLinkLookup.Of(link))
            .Should().Be(BindingReadability.Readable);
        DataverseContactIdentityStore.ClassifyHomeProbe(ContactLookup.Of(), HomeLinkLookup.Of())
            .Should().Be(BindingReadability.NoMarkedRows);
        DataverseContactIdentityStore.ClassifyHomeProbe(ContactLookup.Of(maskedHomeRow), HomeLinkLookup.Of(link))
            .Should().Be(BindingReadability.Masked);
        DataverseContactIdentityStore.ClassifyHomeProbe(ContactLookup.Of(homeRow), HomeLinkLookup.Of(maskedLink))
            .Should().Be(BindingReadability.Masked);
        DataverseContactIdentityStore.ClassifyHomeProbe(ContactLookup.Failed, HomeLinkLookup.Of())
            .Should().Be(BindingReadability.Failed);
        // r3 047 fix round 1: a missing column is a permanent fault of its own, NOT a transient failure (500 vs 503 upstream).
        DataverseContactIdentityStore.ClassifyHomeProbe(ContactLookup.Of(), HomeLinkLookup.ColumnMissing)
            .Should().Be(BindingReadability.ColumnMissing);
        DataverseContactIdentityStore.ClassifyHomeProbe(ContactLookup.ColumnMissing, HomeLinkLookup.Of())
            .Should().Be(BindingReadability.ColumnMissing);
        DataverseContactIdentityStore.ClassifyHomeProbe(ContactLookup.Failed, HomeLinkLookup.ColumnMissing)
            .Should().Be(BindingReadability.ColumnMissing, "a permanent fault is certain whatever the other leg did");
        DataverseContactIdentityStore.ClassifyHomeProbe(ContactLookup.Of(), HomeLinkLookup.Failed)
            .Should().Be(BindingReadability.Failed);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "anything", true)]
    [InlineData(HttpStatusCode.BadRequest, "Principal user is missing prvCreateContact privilege.", true)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"0x80040220\"}}", true)]
    [InlineData(HttpStatusCode.BadRequest, "A required property is missing.", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "privilege", false)]
    [InlineData(HttpStatusCode.InternalServerError, "privilege", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, false)]
    public void IsPermissionFault_IsA403OrAPrivilege400_NeverAThrottleOr5xx(HttpStatusCode status, string? body, bool expected)
        => DataverseContactIdentityStore.IsPermissionFault(status, body).Should().Be(expected);

    // ── ISS-005 UAC re-check K1: the owner read-check fault (0x80040299) is its own status on a create ──────────

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"0x80040299\",\"message\":\"Read Privilege Check For Owner failed\"}}", true)]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"read privilege check for owner failed.\"}}", true)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"0x80040299\"}}", true)]
    // Must not fire: the BFF identity's own missing privilege, other faults, throttles and 5xx stay as they were.
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"0x80040220\",\"message\":\"Principal user is missing prvCreateContact privilege\"}}", false)]
    [InlineData(HttpStatusCode.Forbidden, "anything", false)]
    [InlineData(HttpStatusCode.BadRequest, "A required property is missing.", false)]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\":{\"code\":\"0x80040299\"}}", false)]
    [InlineData(HttpStatusCode.Forbidden, null, false)]
    public void IsOwnerPrivilegeFault_IsTheOwnerReadCheck_NotTheBffIdentitysOwnPrivilege(HttpStatusCode status, string? body, bool expected)
        => DataverseContactIdentityStore.IsOwnerPrivilegeFault(status, body).Should().Be(expected);

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"0x80040299\",\"message\":\"Read Privilege Check For Owner failed\"}}", StoreWriteStatus.OwnerPrivilegeMissing)]
    // Every other fault keeps its existing mapping.
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"0x80040220\",\"message\":\"Principal user is missing prvCreateContact privilege\"}}", StoreWriteStatus.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"boom\"}}", StoreWriteStatus.Failed)]
    [InlineData(HttpStatusCode.PreconditionFailed, "{\"error\":{\"code\":\"0x80060892\",\"message\":\"A record that has the attribute values External Object ID already exists.\"}}", StoreWriteStatus.KeyConflict)]
    public async Task AnOwnedContactCreate_MapsOnlyTheOwnerReadCheckFault_ToOwnerPrivilegeMissing(
        HttpStatusCode code, string body, StoreWriteStatus expected)
    {
        var http = new ScriptedHttp((code, body));

        var write = await http.Store().CreateContactForOidAsync(
            Oid, IdentityPlaneMarker.Workforce,
            new NewContactDetails("Pat", "Example", "pat@customer.example") { OwningTeamId = Team }, CancellationToken.None);

        write.Status.Should().Be(expected);
        http.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task ANonCreateWrite_IsNeverClassifiedAsAnOwnerFault()
    {
        // Only a POST sets an owner; a PATCH that meets the same body keeps the generic permission mapping.
        var http = new ScriptedHttp((HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"0x80040299\",\"message\":\"Read Privilege Check For Owner failed\"}}"));

        var write = await http.Store().BindOidAndConsumeInviteAsync(IntentContact, "W/\"42\"", Oid,
            IdentityPlaneMarker.External, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Forbidden);
    }

    [Fact]
    public void TheSecureBusinessUnitRead_IsTheBusinessUnitByName_WithTheSharedSecureRecordFilter()
        => DataverseContactIdentityStore.BuildBusinessUnitByNamePath("Secure Record").Should().Be(
            "businessunits?$select=businessunitid&$filter=name eq 'Secure Record'&$top=2");

    // ── The identity columns' readability proof ─────────────────────────────────────────────────────────────
    // The plane and the collision columns are field-secured: an EMPTY read is trusted only while the BFF's read of every
    // identity column is proven (SecuredColumnReadProof). Unproven: the probe is Masked and an empty-plane row Unreadable.

    [Fact]
    public void TheIdentityColumns_AreTheBindingThePlaneAndTheFiveCollisionColumns()
        => DataverseContactIdentityStore.IdentityColumns.Should().BeEquivalentTo(
            ContactBindingDecision.ExternalObjectIdColumn, ContactBindingDecision.IdentityPlaneColumn,
            DataverseContactIdentityStore.FlagOnColumn, DataverseContactIdentityStore.FlagOidColumn,
            DataverseContactIdentityStore.FlagPlaneColumn, DataverseContactIdentityStore.FlagReasonColumn,
            DataverseContactIdentityStore.FlagPartiesColumn);

    [Fact]
    public void TheIdentityColumns_AreProvenAsTwoColumnSets_TheBindingAndTheCollisionFlag()
    {
        DataverseContactIdentityStore.BindingColumns.Should().Equal(
            ContactBindingDecision.ExternalObjectIdColumn, ContactBindingDecision.IdentityPlaneColumn);
        DataverseContactIdentityStore.CollisionColumns.Should().BeEquivalentTo(
            DataverseContactIdentityStore.FlagOnColumn, DataverseContactIdentityStore.FlagOidColumn,
            DataverseContactIdentityStore.FlagPlaneColumn, DataverseContactIdentityStore.FlagReasonColumn,
            DataverseContactIdentityStore.FlagPartiesColumn);
        DataverseContactIdentityStore.IdentityColumns.Should().BeEquivalentTo(
            DataverseContactIdentityStore.BindingColumns.Concat(DataverseContactIdentityStore.CollisionColumns));
    }

    [Fact]
    public void ARowReadWithoutTheCollisionProof_HasAFlagThatIsNeverWrittenOver_WhateverItsPlane()
    {
        // MUST FIRE: the plane is set and read, the flag read empty, and the collision columns' read is not proven.
        var bound = new ContactBindingRow(Guid.NewGuid(), 0, Oid.ToString(), (int)IdentityPlaneMarker.Workforce);
        var party = new CollisionParty(Guid.NewGuid(), IdentityPlaneMarker.External, IdentityCollisionReason.BoundToDifferentOid,
            DateTimeOffset.UnixEpoch);

        var marked = DataverseContactIdentityStore.WithIdentityColumnsProof(
            ContactLookup.Of(bound), ColumnReadProofOutcome.Proven, ColumnReadProofOutcome.Unproven).Rows[0];
        var proven = DataverseContactIdentityStore.WithIdentityColumnsProof(
            ContactLookup.Of(bound), ColumnReadProofOutcome.Proven, ColumnReadProofOutcome.Proven).Rows[0];

        marked.CollisionColumnsUnproven.Should().BeTrue();
        marked.IdentityColumnsUnproven.Should().BeFalse();
        marked.Binding.Kind.Should().Be(BindingKind.Bound);
        marked.Flag!.HasUnreadableParties.Should().BeTrue();
        ContactBindingDecision.ShouldWriteFlag(marked.Flag, party).Should().BeFalse();
        ContactBindingDecision.ReconcileFlag(marked.Flag, Array.Empty<CollisionParty>(), collidesThisRun: false).Action
            .Should().Be(FlagReconciliationAction.Keep, "a flag read without proof is never cleared");

        // MUST NOT FIRE: with both proofs the row is unchanged and an empty flag is written as today.
        proven.Should().Be(bound);
        ContactBindingDecision.ShouldWriteFlag(proven.Flag, party).Should().BeTrue();
    }

    [Fact]
    public void ClassifyProbe_WithoutProof_IsMasked_EvenWhenNoRowIsMarked()
    {
        // MUST FIRE: a hidden plane reads as no plane, so "no marked rows" without proof is not "nothing to protect".
        DataverseContactIdentityStore.ClassifyProbe(ContactLookup.Of(), ColumnReadProofOutcome.Unproven)
            .Should().Be(BindingReadability.Masked);
        DataverseContactIdentityStore.ClassifyProbe(ContactLookup.Failed, ColumnReadProofOutcome.Unproven)
            .Should().Be(BindingReadability.Masked);
        DataverseContactIdentityStore.ClassifyProbe(ContactLookup.Of(), ColumnReadProofOutcome.Faulted)
            .Should().Be(BindingReadability.Failed, "a proof that faulted is retryable, and still denies");
    }

    [Fact]
    public void ClassifyProbe_WithProof_IsDecidedByTheRows_AsToday()
    {
        // MUST NOT FIRE.
        DataverseContactIdentityStore.ClassifyProbe(ContactLookup.Of(), ColumnReadProofOutcome.Proven)
            .Should().Be(BindingReadability.NoMarkedRows);
        DataverseContactIdentityStore.ClassifyProbe(
                ContactLookup.Of(new ContactBindingRow(Guid.NewGuid(), 0, Oid.ToString(), 100000001)),
                ColumnReadProofOutcome.Proven)
            .Should().Be(BindingReadability.Readable);
    }

    [Theory]
    [InlineData(ColumnReadProofOutcome.Unproven)]
    [InlineData(ColumnReadProofOutcome.Faulted)]
    public void AnEmptyPlaneRow_ReadWithoutProof_IsUnreadable_NeverUnboundNorALegacyBinding(ColumnReadProofOutcome proof)
    {
        // MUST FIRE: no oid and no plane (would be Unbound), and an oid with no plane (would be a plane-less legacy binding).
        var unbound = new ContactBindingRow(Guid.NewGuid(), 0, null, null);
        var legacy = new ContactBindingRow(Guid.NewGuid(), 0, Oid.ToString(), null);

        var marked = DataverseContactIdentityStore.WithIdentityColumnsProof(ContactLookup.Of(unbound, legacy), proof);

        marked.Rows.Should().OnlyContain(r => r.IdentityColumnsUnproven);
        marked.Rows.Select(r => r.Binding.Kind).Should().Equal(BindingKind.Unreadable, BindingKind.Unreadable);
    }

    [Fact]
    public void ARowWithAPlane_IsDecidedOnWithoutProof_AndAProvenReadIsUnchanged()
    {
        // MUST NOT FIRE: a plane that was read is a value, not an empty read; a proven read is decided as today.
        var bound = new ContactBindingRow(Guid.NewGuid(), 0, Oid.ToString(), (int)IdentityPlaneMarker.Workforce);
        var unbound = new ContactBindingRow(Guid.NewGuid(), 0, null, null);

        var unproven = DataverseContactIdentityStore.WithIdentityColumnsProof(ContactLookup.Of(bound), ColumnReadProofOutcome.Unproven);
        var proven = DataverseContactIdentityStore.WithIdentityColumnsProof(ContactLookup.Of(bound, unbound), ColumnReadProofOutcome.Proven);

        unproven.Rows[0].Binding.Should().Be(new BindingState(BindingKind.Bound, Oid, IdentityPlaneMarker.Workforce));
        proven.Rows.Should().Equal(bound, unbound);
        proven.Rows[1].Binding.Kind.Should().Be(BindingKind.Unbound);
    }

    [Fact]
    public void ASystemUsersLinkedContact_ReadWithoutProof_IsMarkedToo()
    {
        var row = new SystemUserIdentityRow(Guid.NewGuid(), Oid, null, null, null, null,
            new ContactBindingRow(Guid.NewGuid(), 0, null, null));

        DataverseContactIdentityStore.WithIdentityColumnsProof(row, ColumnReadProofOutcome.Unproven)
            .LinkedContact!.Binding.Kind.Should().Be(BindingKind.Unreadable);
        DataverseContactIdentityStore.WithIdentityColumnsProof(row, ColumnReadProofOutcome.Proven)
            .LinkedContact!.Binding.Kind.Should().Be(BindingKind.Unbound);
    }

    [Fact]
    public void TheProofsRoleRead_IsSystemAdministrator_JoinedToTheUserThroughSystemUserRoles()
    {
        var user = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

        var path = Uri.UnescapeDataString(DataverseContactIdentityStore.BuildSystemAdministratorRolePath(user));

        path.Should().StartWith("roles?fetchXml=<fetch top=\"1\"><entity name=\"role\">");
        path.Should().Contain("<condition attribute=\"roletemplateid\" operator=\"eq\" value=\"627090ff-40a3-4053-8790-584edc5be201\" />");
        path.Should().NotContain("attribute=\"name\"", "a role is matched by its template, never by its display name");
        path.Should().Contain("<link-entity name=\"systemuserroles\" from=\"roleid\" to=\"roleid\" link-type=\"inner\">"
            + $"<filter><condition attribute=\"systemuserid\" operator=\"eq\" value=\"{user:D}\" /></filter>");
    }

    [Theory]
    [InlineData(false, "<link-entity name=\"systemuserprofiles\" from=\"fieldsecurityprofileid\" to=\"fieldsecurityprofileid\" link-type=\"inner\"><filter>")]
    [InlineData(true, "<link-entity name=\"teamprofiles\" from=\"fieldsecurityprofileid\" to=\"fieldsecurityprofileid\" link-type=\"inner\"><link-entity name=\"teammembership\" from=\"teamid\" to=\"teamid\" link-type=\"inner\"><filter>")]
    public void TheProofsProfileRead_AsksForReadOnEveryIdentityColumn_ThroughTheUsersOwnOrTeamProfiles(bool throughTeam, string link)
    {
        var user = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

        var path = Uri.UnescapeDataString(DataverseContactIdentityStore.BuildFieldPermissionReadPath(
            user, DataverseContactIdentityStore.ContactTable, DataverseContactIdentityStore.IdentityColumns, throughTeam));

        path.Should().StartWith("fieldpermissions?fetchXml=<fetch><entity name=\"fieldpermission\"><attribute name=\"attributelogicalname\" />");
        path.Should().Contain("<condition attribute=\"entityname\" operator=\"eq\" value=\"contact\" />");
        path.Should().Contain("<condition attribute=\"canread\" operator=\"eq\" value=\"4\" />");
        foreach (var column in DataverseContactIdentityStore.IdentityColumns)
        {
            path.Should().Contain($"<value>{column}</value>");
        }

        path.Should().Contain(link + $"<condition attribute=\"systemuserid\" operator=\"eq\" value=\"{user:D}\" /></filter>");
    }

    [Fact]
    public void TheProof_ReadsOnlyThePlatformTablesTheSchemaScriptVerifies()
    {
        var user = Guid.NewGuid();
        var reads = Uri.UnescapeDataString(DataverseContactIdentityStore.BuildSystemAdministratorRolePath(user)
            + DataverseContactIdentityStore.BuildFieldPermissionReadPath(user, "contact", new[] { "x" }, throughTeam: false)
            + DataverseContactIdentityStore.BuildFieldPermissionReadPath(user, "contact", new[] { "x" }, throughTeam: true));
        var tables = System.Text.RegularExpressions.Regex.Matches(reads, "<(?:entity|link-entity) name=\"([a-z]+)\"")
            .Select(m => m.Groups[1].Value).Append("systemuser").Distinct();

        tables.Should().BeEquivalentTo(SecuredColumnReadProof.PlatformTablesRead,
            "the script's -Verify asserts the BFF application user reads exactly these (WhoAmI reads its own systemuser)");
    }

    // ── The real store's proof, end to end over its own Web API reads (ReadProofHttp answers by route) ────────────────
    // These run DataverseContactIdentityStore itself: its WhoAmI and FetchXML reads, their parsing, and the guards in
    // ProbeBindingReadabilityAsync and GetContactAsync. The binder-level tests run the in-memory store, which shares only
    // the pure marking and verdict.

    private static readonly Guid ProofContact = Guid.Parse("dddddddd-0000-4000-8000-0000000000f1");

    private static DataverseContactIdentityStore StoreOver(ReadProofHttp http)
        => new(http, _ => Task.FromResult("token"), "https://org.test.invalid", NullLogger.Instance);

    private static string ContactJson(int? plane = null, string? oid = null)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["contactid"] = ProofContact,
            ["statecode"] = 0,
            [ContactBindingDecision.ExternalObjectIdColumn] = oid,
            [ContactBindingDecision.IdentityPlaneColumn] = plane,
        });

    [Fact]
    public async Task TheRealStore_WithNoMarkedRowsAndNoGrant_ProbesMasked_AndNeverSendsTheProbe()
    {
        // MUST FIRE: no role and no profile grants Read, and the probe would find no marked rows ("nothing to protect").
        var http = new ReadProofHttp();

        var readability = await StoreOver(http).ProbeBindingReadabilityAsync(CancellationToken.None);

        readability.Should().Be(BindingReadability.Masked);
        http.Requests.Should().NotContain(r => r.Contains("/contacts?"), "an unproven read is never classified by its rows");
    }

    [Fact]
    public async Task TheRealStore_GetContact_AnEmptyPlaneWithoutProof_IsUnreadable()
    {
        // MUST FIRE: no oid, no plane, and no proof: never Unbound.
        var http = new ReadProofHttp { ContactBody = ContactJson() };

        var lookup = await StoreOver(http).GetContactAsync(ProofContact, CancellationToken.None);

        lookup.Status.Should().Be(LookupStatus.Read);
        lookup.Rows.Should().ContainSingle().Which.Binding.Kind.Should().Be(BindingKind.Unreadable);
        http.WhoAmICount.Should().BeGreaterThan(0, "the empty read was put to the proof");
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("team")]
    [InlineData("role")]
    public async Task TheRealStore_AProvenRead_TrustsTheEmptyPlane_AndProbesByItsRows(string leg)
    {
        // MUST NOT FIRE: WhoAmI is parsed, and each leg's FetchXML answer proves every identity column.
        var http = new ReadProofHttp { ContactBody = ContactJson() };
        switch (leg)
        {
            case "direct": http.DirectGrants.UnionWith(DataverseContactIdentityStore.IdentityColumns); break;
            case "team": http.TeamGrants.UnionWith(DataverseContactIdentityStore.IdentityColumns); break;
            default: http.Roles.Add(("System Administrator", SecuredColumnReadProof.SystemAdministratorRoleTemplateId)); break;
        }

        var store = StoreOver(http);
        var readability = await store.ProbeBindingReadabilityAsync(CancellationToken.None);
        var lookup = await store.GetContactAsync(ProofContact, CancellationToken.None);

        readability.Should().Be(BindingReadability.NoMarkedRows);
        http.Requests.Should().Contain(r => r.Contains("/contacts?"), "a proven read is classified by its rows");
        var row = lookup.Rows.Should().ContainSingle().Subject;
        row.Binding.Kind.Should().Be(BindingKind.Unbound);
        row.CollisionColumnsUnproven.Should().BeFalse();
        row.Flag.Should().BeNull();
    }

    [Fact]
    public async Task TheRealStore_AProofReadThatFaults_ProbesFailed_AndTheEmptyPlaneIsUnreadable()
    {
        // MUST FIRE: a throttled proof read is not a proof; the probe denies (retryable) and the empty plane is Unreadable.
        var http = new ReadProofHttp { ContactBody = ContactJson(), ProofFaults = true };
        http.DirectGrants.UnionWith(DataverseContactIdentityStore.IdentityColumns);
        var store = StoreOver(http);

        (await store.ProbeBindingReadabilityAsync(CancellationToken.None)).Should().Be(BindingReadability.Failed);
        (await store.GetContactAsync(ProofContact, CancellationToken.None)).Rows.Should().ContainSingle()
            .Which.Binding.Kind.Should().Be(BindingKind.Unreadable);
    }

    [Theory]
    [InlineData("Platform Administrators", true, BindingReadability.NoMarkedRows)] // the template, renamed: proves
    [InlineData("System Administrator", false, BindingReadability.Masked)]         // the name on another template: does not
    public async Task TheRealStore_TheRoleLeg_MatchesTheSystemAdministratorTemplate_NeverTheRoleName(
        string roleName, bool systemAdministratorTemplate, BindingReadability expected)
    {
        var http = new ReadProofHttp();
        http.Roles.Add((roleName, systemAdministratorTemplate ? SecuredColumnReadProof.SystemAdministratorRoleTemplateId : Guid.NewGuid()));

        (await StoreOver(http).ProbeBindingReadabilityAsync(CancellationToken.None)).Should().Be(expected);
    }

    [Fact]
    public async Task TheRealStore_ARowWithItsPlaneSet_IsStillPutToTheCollisionProof()
    {
        // MUST FIRE: the plane is read (a value), but the collision columns are not granted: the flag that read empty may be
        // hidden, so it reads as one that is never written over.
        var http = new ReadProofHttp { ContactBody = ContactJson((int)IdentityPlaneMarker.Workforce, Oid.ToString("D")) };
        http.DirectGrants.UnionWith(DataverseContactIdentityStore.BindingColumns);

        var row = (await StoreOver(http).GetContactAsync(ProofContact, CancellationToken.None)).Rows.Should().ContainSingle().Subject;

        row.Binding.Should().Be(new BindingState(BindingKind.Bound, Oid, IdentityPlaneMarker.Workforce));
        row.CollisionColumnsUnproven.Should().BeTrue();
        row.Flag!.HasUnreadableParties.Should().BeTrue();
        ContactBindingDecision.ShouldWriteFlag(row.Flag, new CollisionParty(Guid.NewGuid(), IdentityPlaneMarker.External,
            IdentityCollisionReason.BoundToDifferentOid, DateTimeOffset.UnixEpoch)).Should().BeFalse();
    }

    [Fact]
    public async Task TheRealStore_ARowWithItsPlaneSet_AndEveryColumnGranted_ReadsAsToday()
    {
        // MUST NOT FIRE.
        var http = new ReadProofHttp { ContactBody = ContactJson((int)IdentityPlaneMarker.Workforce, Oid.ToString("D")) };
        http.DirectGrants.UnionWith(DataverseContactIdentityStore.IdentityColumns);

        var row = (await StoreOver(http).GetContactAsync(ProofContact, CancellationToken.None)).Rows.Should().ContainSingle().Subject;

        row.CollisionColumnsUnproven.Should().BeFalse();
        row.IdentityColumnsUnproven.Should().BeFalse();
        row.Flag.Should().BeNull();
    }
}
