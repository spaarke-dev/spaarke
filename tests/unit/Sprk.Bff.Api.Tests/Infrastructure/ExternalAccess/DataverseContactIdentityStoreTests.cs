using System.Text.Json;
using FluentAssertions;
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
}
