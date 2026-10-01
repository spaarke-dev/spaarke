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

    [Fact]
    public void TheCreate_IsAddressedByTheAlternateKey()
        => DataverseContactIdentityStore.BuildCreateByKeyPath(Oid)
            .Should().Be("contacts(sprk_externalobjectid='aaaaaaaa-0000-4000-8000-000000000001')");

    [Fact]
    public void TheBindPayload_WritesTheOidInDFormat_AndThePlane_Together()
    {
        var payload = DataverseContactIdentityStore.BindPayload(Oid, IdentityPlaneMarker.Workforce);

        payload["sprk_externalobjectid"].Should().Be("aaaaaaaa-0000-4000-8000-000000000001");
        payload["sprk_identityplane"].Should().Be(100000001);
    }

    [Fact]
    public void TheFlagPayloads_SetAndClearAllFourColumns()
    {
        var flag = new CollisionFlag(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), Oid,
            IdentityPlaneMarker.External, IdentityCollisionReason.EmailAmbiguous);

        var set = DataverseContactIdentityStore.FlagPayload(flag);
        set["sprk_identitycollisionon"].Should().Be("2026-10-01T09:00:00Z");
        set["sprk_identitycollisionreason"].Should().Be(100000001);
        DataverseContactIdentityStore.ClearFlagPayload().Values.Should().AllSatisfy(v => v.Should().BeNull());
        DataverseContactIdentityStore.ClearFlagPayload().Keys.Should().BeEquivalentTo(set.Keys);
    }

    [Fact]
    public void ParseContactRow_ReadsTheBindingTheFlagAndTheETag()
    {
        using var doc = JsonDocument.Parse("""
            {"@odata.etag":"W/\"42\"","contactid":"cccccccc-0000-4000-8000-00000000000a","statecode":0,
             "emailaddress1":"a@b.example","sprk_externalobjectid":"AAAAAAAA-0000-4000-8000-000000000001",
             "sprk_identityplane":100000001,"sprk_identitycollisionon":"2026-10-01T09:00:00Z",
             "sprk_identitycollisionoid":"aaaaaaaa-0000-4000-8000-000000000001","sprk_identitycollisionplane":100000000,
             "sprk_identitycollisionreason":100000000}
            """);

        var row = DataverseContactIdentityStore.ParseContactRow(doc.RootElement)!;

        row.ETag.Should().Be("W/\"42\"");
        row.Binding.Kind.Should().Be(BindingKind.Bound);
        row.Binding.Oid.Should().Be(Oid);
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
