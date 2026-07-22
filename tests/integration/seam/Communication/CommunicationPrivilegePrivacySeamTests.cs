using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Task-043 (FR-21 / NFR-01) vertical-slice seam tests (ADR-038 "DoD for dispatch-spine changes") for the
/// privilege/privacy accuracy surface: the three markers (<c>sprk_privilegeclassification</c>,
/// <c>sprk_isinternalonly</c>, <c>sprk_isprivate</c>) AND the recipient display equal the ACTUAL permitted set —
/// never over-disclosing. Composes the REAL <see cref="CommunicationThreadReadService"/> + REAL
/// <see cref="CommunicationAccessFilter"/> over the two genuine module boundaries
/// (<see cref="IImpersonatedCommunicationQuery"/> = the Dataverse/impersonation read boundary,
/// <see cref="ICallerSystemUserResolver"/> = the caller-resolution boundary). No <c>Mock&lt;HttpMessageHandler&gt;</c>,
/// no DI-registration test, no ctor null-check test.
///
/// <para><b>The crown-jewel property (NFR-01):</b> a caller can never see a recipient/marker/privilege they must not.
/// This is guaranteed by TWO composed gates, both proven here end-to-end through production code:</para>
/// <list type="number">
/// <item><b>Impersonation (record-level — the R1 primary gate).</b> A restricted communication is simply ABSENT from
/// the impersonated result of a caller who lacks access (Dataverse row-level security answers the
/// <c>MSCRMCallerID</c> query). Because recipients + markers ride the SAME projected row, an absent row contributes
/// NONE of them — no recipient, no privilege label, no privacy marker. Proven by the parity vs. negative pair on the
/// real service.</item>
/// <item><b>Internal-only business rule (the D-05 axis).</b> Even a row Dataverse returned is DROPPED by the shared
/// <see cref="CommunicationAccessFilter"/> for a non-internal caller — so its recipients/markers never reach a DTO.
/// Proven directly on the REAL filter (see the note below on why this axis is asserted at the filter boundary).</item>
/// </list>
///
/// <para><b>Note on the internal-only axis + the blocked #675 fix.</b> <see cref="CommunicationThreadReadService"/>
/// currently composes <c>IsInternalUser: true</c> at its call sites (a SEPARATE, blocked change — awaiting the
/// notification-spine <c>ISystemUserIdentityResolver</c> — will swap those to the real per-caller value; NOT in
/// scope for task 043). So the non-internal-caller drop is asserted on the REAL
/// <see cref="CommunicationAccessFilter"/> — the exact production component that will enforce it once #675 wires the
/// real flag — rather than by forcing an external caller through the service. The service-level negative below uses
/// the impersonation gate (which is caller-flag-independent and IS the R1 no-leak mechanism per
/// <c>../messaging-communication-app-r1/notes/access-model-decision.md</c>).</para>
/// </summary>
public class CommunicationPrivilegePrivacySeamTests
{
    private static readonly Guid ThreadId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AuthorizedCallerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UnauthorizedCallerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private const string CommunicationSet = "sprk_communications";
    private const string AttachmentSet = "sprk_communicationattachments";
    private const string ThreadSet = "sprk_communicationthreads";
    private const int PrivilegeNone = 100000000;
    private const int PrivilegePrivileged = 100000002;
    private const int TypeMessage = 100000004;

    // The restricted communication's recipient set — the exact strings that MUST NOT surface to an unauthorized viewer.
    private const string RestrictedTo = "lead.counsel@firm.com; client@firm.com";
    private static readonly string[] RestrictedRecipients = { "lead.counsel@firm.com", "client@firm.com" };

    private readonly Mock<IImpersonatedCommunicationQuery> _query = new();
    private readonly Mock<ICallerSystemUserResolver> _resolver = new();

    private CommunicationThreadReadService Sut() => new(
        _query.Object,
        new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
        _resolver.Object,
        Mock.Of<ILogger<CommunicationThreadReadService>>());

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    private void ResolveCallerAs(Guid systemUserId) =>
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CallerSystemUserResolution.Resolved(systemUserId.ToString("D")));

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // A. PARITY — the authorized caller sees the recipients + all three markers, exactly (no under-disclosure).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadThreadAsync_AuthorizedCaller_SeesPermittedRecipientsAndAllThreeMarkers_AndNeverSelectsBcc()
    {
        var messageId = Guid.NewGuid();
        string? select = null;
        ResolveCallerAs(AuthorizedCallerId);

        // Dataverse impersonation RETURNS the restricted row to the authorized caller.
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), AuthorizedCallerId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => select = odata)
              .ReturnsAsync(new[] { RestrictedRow(messageId) });
        _query.Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string>(), AuthorizedCallerId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), AuthorizedCallerId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { new Dictionary<string, JsonElement> { ["sprk_name"] = El("Acme v. Roe") } });

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        var msg = result.Messages.Single();
        msg.To.Should().BeEquivalentTo(RestrictedRecipients, "the permitted caller sees the ACTUAL recipient set");
        msg.Privilege.Should().Be(PrivilegePrivileged);
        msg.IsInternalOnly.Should().BeTrue();
        msg.IsPrivate.Should().BeTrue();

        // BCC can never leak by construction — the read must never even SELECT sprk_bcc.
        select.Should().NotBeNull();
        select!.Should().Contain("sprk_to").And.NotContain("sprk_bcc");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // B. NEGATIVE (over-disclosure) — an UNAUTHORIZED / less-privileged caller sees NOTHING of the restricted
    //    communication: no recipients, no privilege, no privacy marker. The impersonated query returns it to the
    //    authorized caller but NOT to the unauthorized one (Dataverse row-level security), so the row — and every
    //    recipient/marker riding it — is absent from the unauthorized caller's read.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadThreadAsync_UnauthorizedCaller_SeesNoRecipientsMarkersOrPrivilege_NoOverDisclosure()
    {
        var messageId = Guid.NewGuid();

        // The SAME thread + the SAME restricted row: impersonation returns it ONLY to the authorized caller.
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), AuthorizedCallerId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { RestrictedRow(messageId) });
        // For the UNAUTHORIZED caller, Dataverse returns nothing (no grant) — for EVERY entity set.
        _query.Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), UnauthorizedCallerId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        ResolveCallerAs(UnauthorizedCallerId);
        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        // Total exclusion — the restricted communication and everything riding it are absent.
        result.Messages.Should().BeEmpty();
        result.Count.Should().Be(0);
        result.Name.Should().BeNull("the thread label is impersonated too — no existence leak (NFR-06)");

        // Belt-and-braces: none of the restricted recipients appear anywhere in the payload.
        result.Messages.SelectMany(m => m.To).Should().NotContain(RestrictedRecipients);
        result.Messages.Select(m => m.Privilege).Should().NotContain(PrivilegePrivileged);
        result.Messages.Where(m => m.IsInternalOnly || m.IsPrivate).Should().BeEmpty();

        // No visible messages ⇒ no attachment fan-out for the unauthorized caller (NFR-07 + no-leak).
        _query.Verify(q => q.QueryAsync(AttachmentSet, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // C. NEGATIVE (internal-only axis) — the REAL access filter DROPS an internal-only communication for a
    //    non-internal caller, so its recipients/markers never reach a DTO. Asserted on the production filter
    //    directly (see the class-level note on the blocked #675 IsInternalUser fix).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CommunicationAccessFilter_NonInternalCaller_DropsInternalOnlyMessage_SoRecipientsAndMarkersNeverSurface()
    {
        var filter = new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>());

        var internalOnly = new Entity("sprk_communication", Guid.NewGuid());
        internalOnly["sprk_isinternalonly"] = true;
        internalOnly["sprk_privilegeclassification"] = new OptionSetValue(PrivilegePrivileged);
        // (sprk_to / sprk_isprivate live on the row too, but the point is the row is DROPPED before any projection.)

        var externalCaller = new CommunicationAccessContext(
            CallerSystemUserId: UnauthorizedCallerId, IsInternalUser: false);

        var result = filter.FilterMessages(externalCaller, new[] { internalOnly });

        result.VisibleMessages.Should().BeEmpty(
            "an internal-only message is invisible to a non-internal caller — it never reaches BuildDto, so no " +
            "recipient/marker can be projected for it (no over-disclosure)");
        result.Decisions.Single().Decision.IsVisible.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // Row builder — the restricted communication (mirrors the unit-test row shape; no new shape invented).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    private static Dictionary<string, JsonElement> RestrictedRow(Guid id) => new()
    {
        ["sprk_communicationid"] = El(id.ToString()),
        ["createdon"] = El("2026-07-19T12:00:00Z"),
        ["sprk_communicationtype"] = El(TypeMessage),
        ["sprk_body"] = El("privileged and confidential"),
        ["sprk_to"] = El(RestrictedTo),
        ["sprk_isinternalonly"] = El(true),
        ["sprk_isprivate"] = El(true),
        ["sprk_privilegeclassification"] = El(PrivilegePrivileged),
    };

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
}
