using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// The REAL <see cref="DataverseContactIdentityStore"/> over its own Web API requests (<see cref="ReadProofHttp"/> answers
/// by route and records every write): the invite stamp is read in the marker's own query together with
/// <c>emailaddress1</c> and the row version, an empty stamp is put to the stamp columns' read proof, the invite mark writes
/// the stamp in its one conditional PATCH, and the consuming bind clears it under the row version it was decided on.
/// </summary>
public class DataverseContactIdentityStoreInviteStampTests
{
    private static readonly Guid Contact = Guid.Parse("dddddddd-0000-4000-8000-0000000005b1");
    private static readonly Guid Tenant = Guid.Parse("11111111-aaaa-4aaa-8aaa-111111111111");
    private static readonly Guid Oid = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000005b1");
    private const string Email = "person@customer.example";

    private static DataverseContactIdentityStore StoreOver(ReadProofHttp http)
        => new(http, _ => Task.FromResult("token"), "https://org.test.invalid", NullLogger.Instance);

    private static string Row(string? emailHash, string? tenant = null, string? email = Email, string etag = "W/\"4711\"")
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@odata.etag"] = etag,
            ["contactid"] = Contact,
            [ContactBindingDecision.ExternalInvitedOnColumn] = "2026-10-20T09:00:00Z",
            [ContactBindingDecision.ExternalInviteCodeHashColumn] = null,
            ["emailaddress1"] = email,
            [ContactBindingDecision.ExternalInvitedEmailHashColumn] = emailHash,
            [ContactBindingDecision.ExternalInvitedTenantIdColumn] = tenant,
        });

    [Fact]
    public async Task TheIntentRead_ReturnsTheStamp_TheCurrentEmailAndTheRowVersion_FromTheMarkersOwnQuery()
    {
        var stored = InviteEmailStamp.Create(Email);
        var http = new ReadProofHttp { ContactBody = Row(stored, Tenant.ToString("D")) };

        var intent = await StoreOver(http).FindBindIntentAsync(Contact, CancellationToken.None);

        intent.InviteStatus.Should().Be(LookupStatus.Read);
        intent.InvitedEmailHash.Should().Be(stored);
        intent.RawInvitedTenantId.Should().Be(Tenant.ToString("D"));
        intent.CurrentEmail.Should().Be(Email);
        intent.ETag.Should().Be("W/\"4711\"");
        intent.StampColumnsUnproven.Should().BeFalse();
        var read = http.Requests.Should().ContainSingle(r => r.Contains("/contacts(")).Subject;
        Uri.UnescapeDataString(read).Should().Contain(
            "$select=contactid,sprk_externalinvitedon,sprk_externalinvitecodehash,emailaddress1,"
            + "sprk_externalinvitedemailhash,sprk_externalinvitedtenantid");
        http.WhoAmICount.Should().Be(0, "a stamp that reads present needs no proof");
        http.Writes.Should().BeEmpty();
    }

    /// <summary>
    /// An empty stamp is no stamp either way; read without proof (the stamp columns not granted to this identity) it is
    /// marked unproven so the operator log says so. MUST FIRE: no grant. MUST NOT FIRE: both stamp columns granted.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AnEmptyStamp_IsPutToTheStampColumnsProof(bool granted, bool unproven)
    {
        var http = new ReadProofHttp { ContactBody = Row(emailHash: null) };
        if (granted)
        {
            http.DirectGrants.UnionWith(DataverseContactIdentityStore.InviteStampColumns);
        }

        var intent = await StoreOver(http).FindBindIntentAsync(Contact, CancellationToken.None);

        intent.InviteStatus.Should().Be(LookupStatus.Read);
        intent.InvitedEmailHash.Should().BeNull();
        intent.StampColumnsUnproven.Should().Be(unproven);
        http.WhoAmICount.Should().BeGreaterThan(0, "the empty read was put to the proof");
        var intentAt = intent with { AsOf = DateTimeOffset.Parse("2026-10-21T09:00:00Z") };
        ContactBindingDecision.CheckInviteStamp(intentAt, Email, false, null, null)
            .Should().Be(unproven ? InviteStampFailure.StampUnproven : InviteStampFailure.NoStamp);
    }

    /// <summary>
    /// The marker, the code hash, the email stamp and the tenant stamp are read in ONE query, so a platform answer that
    /// names ANY of the four as a property the environment lacks is <see cref="LookupStatus.ColumnMissing"/> for the whole
    /// lookup. No stamp can then be met, so NO email bind is made on either token plane: the external (CIAM) plane answers
    /// <c>contact_not_invited</c> (operator reason <c>no-marker</c>), the Workforce plane <c>contact_not_invited</c>
    /// (operator reason <c>stamp-none</c>), even for an otherwise valid invite code and sign-in. MUST FIRE: each of the
    /// four columns missing, on both planes. MUST NOT FIRE: the same sign-in over a provisioned read binds.
    /// </summary>
    [Theory]
    [InlineData("sprk_externalinvitedon", "ciam", "no-marker")]
    [InlineData("sprk_externalinvitecodehash", "ciam", "no-marker")]
    [InlineData("sprk_externalinvitedemailhash", "ciam", "no-marker")]
    [InlineData("sprk_externalinvitedtenantid", "ciam", "no-marker")]
    [InlineData("sprk_externalinvitedemailhash", "workforce", "stamp-none")]
    [InlineData("sprk_externalinvitedtenantid", "workforce", "stamp-none")]
    [InlineData(null, "ciam", null)]
    [InlineData(null, "workforce", null)]
    public async Task AStampColumnNotProvisioned_RefusesEveryEmailBind_OnBothTokenPlanes(string? missingColumn, string plane, string? reason)
    {
        var invite = InviteCode.Create(Contact);
        var ciam = plane == "ciam";
        var asOf = DateTimeOffset.Parse("2026-10-21T09:00:00Z");
        var http = new ReadProofHttp
        {
            ContactBody = missingColumn is null
                ? JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["@odata.etag"] = "W/\"4711\"",
                    ["contactid"] = Contact,
                    [ContactBindingDecision.ExternalInvitedOnColumn] = "2026-10-20T09:00:00Z",
                    [ContactBindingDecision.ExternalInviteCodeHashColumn] = invite.StoredValue,
                    ["emailaddress1"] = Email,
                    [ContactBindingDecision.ExternalInvitedEmailHashColumn] = InviteEmailStamp.Create(Email),
                    // A portal-account invite (CIAM) names no tenant; a work-account invite (Workforce) names one.
                    [ContactBindingDecision.ExternalInvitedTenantIdColumn] = ciam ? null : Tenant.ToString("D"),
                })
                : $"{{\"error\":{{\"code\":\"0x80060888\",\"message\":\"Could not find a property named '{missingColumn}' on type 'Microsoft.Dynamics.CRM.contact'.\"}}}}",
            ContactStatus = missingColumn is null ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
        };
        http.DirectGrants.UnionWith(DataverseContactIdentityStore.InviteStampColumns);

        var intent = (await StoreOver(http).FindBindIntentAsync(Contact, CancellationToken.None)) with { AsOf = asOf };
        var request = new BindingRequest(
            ciam ? BindingPlane.CiamToken : BindingPlane.WorkforceToken, Oid, Email,
            ciam ? BindingEligibility.EmailBindOnly : BindingEligibility.EmailBindAndCreate)
        {
            InviteCode = ciam ? invite.Code : null,
            TokenTenantId = Tenant,
        };
        var decision = ContactBindingDecision.Decide(request, ContactLookup.Of(),
            ContactLookup.Of(new ContactBindingRow(Contact, 0, null, null)), ReferenceLookup.Of(), intent);

        if (missingColumn is null)
        {
            intent.InviteStatus.Should().Be(LookupStatus.Read);
            decision.Action.Should().Be(BindingAction.BindByEmail);
            ContactBindingDecision.NotInvitedReason(request, intent, Contact).Should().BeNull();
            return;
        }

        intent.InviteStatus.Should().Be(LookupStatus.ColumnMissing, "one query reads all four columns");
        decision.Action.Should().Be(BindingAction.Deny);
        decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        ContactBindingDecision.NotInvitedReason(request, intent, Contact).Should().Be(reason);
        http.Writes.Should().BeEmpty();
    }

    /// <summary>
    /// The invite mark is ONE PATCH of the contact, If-Match the row version it was decided on (never <c>*</c>), carrying
    /// the marker, the code hash, the email hash and the tenant, and nothing else.
    /// </summary>
    [Fact]
    public async Task TheInviteMark_IsOnePatch_IfMatchTheRowVersion_CarryingTheStamp()
    {
        var http = new ReadProofHttp();
        var emailHash = InviteEmailStamp.Create(Email);
        var codeHash = InviteCode.Create(Contact).StoredValue;

        var write = await StoreOver(http).MarkInvitedAsync(Contact, "W/\"4711\"", DateTimeOffset.Parse("2026-10-21T09:00:00Z"),
            codeHash, new InviteStamp(emailHash, null), CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Written);
        var patch = http.Writes.Should().ContainSingle().Subject;
        patch.Method.Should().Be(HttpMethod.Patch);
        patch.Uri.Should().EndWith($"/contacts({Contact:D})");
        patch.IfMatch.Should().Be("W/\"4711\"");
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(patch.Body)!;
        body.Keys.Should().BeEquivalentTo(DataverseContactIdentityStore.InviteMarkerColumns);
        body[ContactBindingDecision.ExternalInvitedEmailHashColumn].GetString().Should().Be(emailHash);
        body[ContactBindingDecision.ExternalInvitedTenantIdColumn].ValueKind.Should().Be(JsonValueKind.Null,
            "a portal-account invite names no tenant");
        body[ContactBindingDecision.ExternalInviteCodeHashColumn].GetString().Should().Be(codeHash);
        patch.Body.Should().NotContain(Email, "only the hash of the address is written");
    }

    /// <summary>
    /// Slice B2: a work-account invite mark is the same one PATCH, carrying the resolved tenant and clearing the code hash.
    /// </summary>
    [Fact]
    public async Task AWorkAccountInviteMark_IsOnePatch_CarryingTheTenant_AndClearingTheCode()
    {
        var http = new ReadProofHttp();

        var write = await StoreOver(http).MarkInvitedAsync(Contact, "W/\"4711\"", DateTimeOffset.Parse("2026-10-21T09:00:00Z"),
            null, new InviteStamp(InviteEmailStamp.Create(Email), Tenant), CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Written);
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(http.Writes.Should().ContainSingle().Subject.Body)!;
        body[ContactBindingDecision.ExternalInvitedTenantIdColumn].GetString().Should().Be(Tenant.ToString("D"));
        body[ContactBindingDecision.ExternalInviteCodeHashColumn].ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Slice B2, MUST FIRE: the real store refuses a null code hash with no tenant (and a code beside a tenant) whatever
    /// the row version, before anything is sent, so no caller can write a portal-account invite without its code.
    /// </summary>
    [Theory]
    [InlineData(false, false, "W/\"4711\"")]
    [InlineData(false, false, null)]
    [InlineData(true, true, "W/\"4711\"")]
    public async Task TheStore_RefusesANullCodeWithoutATenant_AndACodeWithATenant(bool code, bool tenant, string? etag)
    {
        var http = new ReadProofHttp();

        var act = () => StoreOver(http).MarkInvitedAsync(Contact, etag, DateTimeOffset.Parse("2026-10-21T09:00:00Z"),
            code ? InviteCode.Create(Contact).StoredValue : null,
            new InviteStamp(InviteEmailStamp.Create(Email), tenant ? Tenant : null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        http.Writes.Should().BeEmpty();
    }

    /// <summary>The consuming bind clears both stamp columns in its one PATCH, If-Match the version it was decided on.</summary>
    [Fact]
    public async Task TheConsumingBind_ClearsTheStamp_IfMatchTheRowVersion()
    {
        var http = new ReadProofHttp();

        var write = await StoreOver(http).BindOidAndConsumeInviteAsync(Contact, "W/\"4711\"", Oid,
            IdentityPlaneMarker.Workforce, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Written);
        var patch = http.Writes.Should().ContainSingle().Subject;
        patch.IfMatch.Should().Be("W/\"4711\"");
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(patch.Body)!;
        body[ContactBindingDecision.ExternalInvitedEmailHashColumn].ValueKind.Should().Be(JsonValueKind.Null);
        body[ContactBindingDecision.ExternalInvitedTenantIdColumn].ValueKind.Should().Be(JsonValueKind.Null);
        body[ContactBindingDecision.ExternalInviteCodeHashColumn].ValueKind.Should().Be(JsonValueKind.Null);
        body[ContactBindingDecision.ExternalObjectIdColumn].GetString().Should().Be(Oid.ToString("D"));
    }

    /// <summary>A row that changed since its version was read answers 412, which the binder re-decides on; never retried here.</summary>
    [Fact]
    public async Task AStaleRowVersion_IsPreconditionFailed_AndNotRetried()
    {
        var http = new ReadProofHttp { WriteStatus = HttpStatusCode.PreconditionFailed };

        var write = await StoreOver(http).BindOidAndConsumeInviteAsync(Contact, "W/\"4711\"", Oid,
            IdentityPlaneMarker.Workforce, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.PreconditionFailed);
        http.Writes.Should().ContainSingle();
    }
}
