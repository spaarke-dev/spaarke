using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Identity;
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
/// <para><b>Note on the internal-only axis + the #675 fix (NOW APPLIED, GitHub #675 / ISS-006).</b>
/// <see cref="CommunicationThreadReadService"/> previously hardcoded <c>IsInternalUser: true</c> at its read call
/// sites, so <see cref="CommunicationAccessFilter"/> treated EVERY caller as internal and an external-licensed
/// systemuser could read internal-only (D-05) messages (over-disclosure). The service now resolves the AUTHORITATIVE
/// per-caller bit via <see cref="ISystemUserIdentityResolver.IsExternalAsync"/> and passes
/// <c>IsInternalUser: !isExternal</c>. Section D asserts the drop END-TO-END through the REAL service for an external
/// caller (the exact over-disclosure the fix closes); section C keeps the filter-boundary assertion as the unit-level
/// proof of the same axis.</para>
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
    // #675 / ISS-006: the read service now consults the shared identity resolver for the per-caller internal/external
    // bit. Default = INTERNAL (IsExternalAsync ⇒ false) so the parity/impersonation cases below are unchanged; the
    // external-caller drop is asserted end-to-end through the REAL service in section D.
    private readonly Mock<ISystemUserIdentityResolver> _identity = new();

    // The loose _identity mock returns false (INTERNAL) by default for any caller; MarkExternal overrides a specific
    // caller to external. (No blanket It.IsAny setup here — it would be configured AFTER MarkExternal in a test body
    // and, being the last matching setup, would clobber the per-caller external override.)
    private CommunicationThreadReadService Sut() => new(
        _query.Object,
        new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
        _resolver.Object,
        _identity.Object,
        Mock.Of<ILogger<CommunicationThreadReadService>>());

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    private void ResolveCallerAs(Guid systemUserId) =>
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CallerSystemUserResolution.Resolved(systemUserId.ToString("D")));

    /// <summary>Marks <paramref name="systemUserId"/> as an EXTERNAL-licensed systemuser (#675 / ISS-006) — a
    /// caller Dataverse impersonation may still return internal-only rows to (a licensed systemuser can hold record
    /// access), but whom the shared filter MUST treat as non-internal so those rows are dropped.</summary>
    private void MarkExternal(Guid systemUserId) =>
        _identity.Setup(i => i.IsExternalAsync(systemUserId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

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
    // D. NEGATIVE (#675 / ISS-006 — external caller, END-TO-END through the REAL service). An external-licensed
    //    systemuser CAN hold Dataverse record access, so impersonation may RETURN an internal-only row to them.
    //    The read service must still DROP it because the caller is external (IsExternalAsync ⇒ true → IsInternalUser
    //    false). This is the exact over-disclosure the previously-hardcoded IsInternalUser:true allowed; it must be
    //    provably closed. A non-internal (regular) row in the same result MUST still surface (no under-disclosure).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadThreadAsync_ExternalCaller_InternalOnlyRowIsFilteredOut_ButRegularRowSurfaces()
    {
        var internalOnlyId = Guid.NewGuid();
        var regularId = Guid.NewGuid();

        ResolveCallerAs(UnauthorizedCallerId);
        MarkExternal(UnauthorizedCallerId); // #675: authoritative per-caller bit = EXTERNAL

        // Impersonation RETURNS BOTH rows to this external-but-record-authorized caller — Dataverse row-level access
        // does not encode the internal-only business rule; that is the filter's job.
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), UnauthorizedCallerId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { RestrictedRow(internalOnlyId), RegularRow(regularId) });
        _query.Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string>(), UnauthorizedCallerId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), UnauthorizedCallerId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { new Dictionary<string, JsonElement> { ["sprk_name"] = El("Acme v. Roe") } });

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        // The internal-only communication is DROPPED — none of its identity/recipients/markers surface (no over-disclosure).
        result.Messages.Select(m => m.MessageId).Should().NotContain(internalOnlyId);
        result.Messages.SelectMany(m => m.To).Should().NotContain(RestrictedRecipients);
        result.Messages.Where(m => m.IsInternalOnly).Should().BeEmpty(
            "an external-licensed caller must NEVER see an internal-only (D-05) message — the #675 over-disclosure");

        // The regular (non-internal-only) communication still surfaces — the filter is scoped, not a blanket deny.
        result.Messages.Should().ContainSingle().Which.MessageId.Should().Be(regularId);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ExternalCaller_DoesNotCountInternalOnlyMessages()
    {
        // The unread scan shares the SAME per-caller filter — an external caller's unread count must exclude
        // internal-only messages (parity with the thread-read drop above; the #675 fix is applied at all read sites).
        ResolveCallerAs(UnauthorizedCallerId);
        MarkExternal(UnauthorizedCallerId);

        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), UnauthorizedCallerId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { RestrictedRow(Guid.NewGuid()), RegularRow(Guid.NewGuid()) });

        var result = await Sut().GetUnreadCountAsync(ThreadId, Caller(), since: null, CancellationToken.None);

        result.UnreadCount.Should().Be(1, "only the non-internal-only message is countable for an external caller");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // Row builder — the restricted communication (mirrors the unit-test row shape; no new shape invented).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    // A plain, non-internal-only communication — must remain visible to any caller the impersonated query returns it to.
    private static Dictionary<string, JsonElement> RegularRow(Guid id) => new()
    {
        ["sprk_communicationid"] = El(id.ToString()),
        ["createdon"] = El("2026-07-19T12:05:00Z"),
        ["sprk_communicationtype"] = El(TypeMessage),
        ["sprk_body"] = El("routine, non-internal message"),
        ["sprk_to"] = El("team@firm.com"),
        ["sprk_isinternalonly"] = El(false),
        ["sprk_isprivate"] = El(false),
        ["sprk_privilegeclassification"] = El(PrivilegeNone),
    };

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
