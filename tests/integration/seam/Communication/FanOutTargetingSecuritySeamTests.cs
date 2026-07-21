using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Identity;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Negative-access vertical-slice seam (ADR-038 / spec FR-08 / NFR-07 / R-5) for
/// <see cref="CommunicationFanOutTargetingService"/> — the Layer-C fan-out targeting composition. Proves the
/// SECURITY property "a private thread's signal reaches only its shared participants, and an internal-only
/// message never reaches an external user" by exercising the REAL access primitives in composition:
/// <list type="bullet">
///   <item>REAL <see cref="CommunicationAccessFilter"/> — the internal-only fail-closed rule;</item>
///   <item>REAL <see cref="DenyAllThreadPrivateGrantProvider"/> — the fail-closed private-thread default;</item>
///   <item>only the Dataverse MODULE BOUNDARY (<see cref="IGenericEntityService"/>, the junction read) is
///     doubled (ADR-038 "mock at module boundaries, not the HTTP-handler level"; NOT a unit-mock of the
///     access primitives).</item>
/// </list>
/// A leak of a private-thread or internal-only signal is a compliance incident, so every case here asserts a
/// NON-inclusion — except the single positive case, which proves the negative rules do not over-exclude.
/// </summary>
public sealed class FanOutTargetingSecuritySeamTests
{
    // sprk_privacystate choice integers (task 004 schema / ThreadPrivacyState).
    private const int PrivacyOpen = 100000000;
    private const int PrivacyPrivate = 100000001;

    // ── seam wiring: REAL access primitives, only the Dataverse junction read doubled ──────────────────

    private static CommunicationFanOutTargetingService CreateService(
        IReadOnlyList<DataverseEntity> junctionRows,
        IReadOnlyCollection<Guid>? externalSystemUsers = null)
    {
        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationparticipant"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(junctionRows.ToList()));

        // Authoritative externality flag doubled at the module boundary: a systemuserid in externalSystemUsers
        // is EXTERNAL (systemuser.sprk_isexternal=true), everyone else INTERNAL. This is what the real
        // SystemUserIdentityResolver reads from systemuser.sprk_isexternal — proving the fan-out consults the
        // flag, NOT the "is a systemuser" proxy.
        var external = externalSystemUsers ?? Array.Empty<Guid>();
        var resolver = new Mock<ISystemUserIdentityResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) => Task.FromResult(external.Contains(id)));

        return new CommunicationFanOutTargetingService(
            entity.Object,
            new CommunicationAccessFilter(NullLogger<CommunicationAccessFilter>.Instance), // REAL filter
            new DenyAllThreadPrivateGrantProvider(),                                        // REAL fail-closed default
            resolver.Object,
            NullLogger<CommunicationFanOutTargetingService>.Instance);
    }

    // ── entity builders ────────────────────────────────────────────────────────────────────────────────

    private static DataverseEntity Message(Guid id, bool? isInternalOnly)
    {
        var e = new DataverseEntity("sprk_communication", id) { ["createdon"] = DateTime.UtcNow };
        if (isInternalOnly.HasValue)
            e["sprk_isinternalonly"] = isInternalOnly.Value; // omit entirely to model an UNREADABLE flag
        return e;
    }

    private static DataverseEntity OpenThread(Guid id) =>
        new("sprk_communicationthread", id) { ["sprk_privacystate"] = new OptionSetValue(PrivacyOpen) };

    private static DataverseEntity PrivateThread(Guid id) =>
        new("sprk_communicationthread", id) { ["sprk_privacystate"] = new OptionSetValue(PrivacyPrivate) };

    private static DataverseEntity SystemUserParticipant(Guid systemUserId) =>
        new("sprk_communicationparticipant")
        {
            ["sprk_isresolved"] = true,
            ["sprk_systemuser"] = new EntityReference("systemuser", systemUserId),
        };

    private static DataverseEntity ContactParticipant(Guid contactId) =>
        new("sprk_communicationparticipant")
        {
            ["sprk_isresolved"] = true,
            ["sprk_contact"] = new EntityReference("contact", contactId),
        };

    private static DataverseEntity UnresolvedExternalParticipant() =>
        new("sprk_communicationparticipant")
        {
            ["sprk_isresolved"] = false, // unresolvable external address — both person lookups absent
        };

    // ── (a) private thread, external non-participant → NOT targeted ──────────────────────────────────────

    [Fact]
    public async Task GetEligibleRecipients_PrivateThreadExternalNonParticipant_IsNotTargeted()
    {
        var messageId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var internalParticipant = Guid.NewGuid();      // an actual message participant
        var externalNonParticipant = Guid.NewGuid();   // NOT on the message at all

        // Only the actual participant is in the junction; the external non-participant never appears.
        var sut = CreateService(new[] { SystemUserParticipant(internalParticipant) });

        var recipients = await sut.GetEligibleRecipientsAsync(
            Message(messageId, isInternalOnly: false), PrivateThread(threadId));

        // A private thread under the deny-all default fans out to NOBODY (no active grants) — so the external
        // non-participant is certainly absent, and even the real participant is denied.
        recipients.Should().NotContain(externalNonParticipant);
        recipients.Should().BeEmpty("a private thread has no active grants (DenyAll default) → empty fan-out");
    }

    // ── (b) internal-only message, external participant → NOT targeted (internal IS) ─────────────────────

    [Fact]
    public async Task GetEligibleRecipients_InternalOnlyMessageWithExternalParticipant_ExcludesExternalKeepsInternal()
    {
        var messageId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var internalUser = Guid.NewGuid();
        var externalContact = Guid.NewGuid();

        var sut = CreateService(new[]
        {
            SystemUserParticipant(internalUser),   // internal (systemuser)
            ContactParticipant(externalContact),   // external (contact)
        });

        var recipients = await sut.GetEligibleRecipientsAsync(
            Message(messageId, isInternalOnly: true), OpenThread(threadId));

        recipients.Should().NotContain(externalContact, "an internal-only message must never reach an external user");
        recipients.Should().Contain(internalUser, "an internal participant may receive an internal-only message");
        recipients.Should().HaveCount(1);
    }

    // ── (c) private thread, no grant, non-participant record-visible elsewhere → NOT targeted ───────────

    [Fact]
    public async Task GetEligibleRecipients_PrivateThreadNonParticipantVisibleElsewhere_IsNotTargeted()
    {
        var messageId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var messageParticipant = Guid.NewGuid();
        var recordVisibleOutsider = Guid.NewGuid(); // can see the record elsewhere, but is NOT on THIS message

        // Candidates come ONLY from the message's junction; the outsider is never a candidate regardless of any
        // other record access. On a private thread the deny-all default then denies even the real participant.
        var sut = CreateService(new[] { SystemUserParticipant(messageParticipant) });

        var recipients = await sut.GetEligibleRecipientsAsync(
            Message(messageId, isInternalOnly: false), PrivateThread(threadId));

        recipients.Should().NotContain(recordVisibleOutsider,
            "record visibility elsewhere never widens the message-grain candidate set");
        recipients.Should().BeEmpty("private-thread fan-out is empty until a real grant provider ships");
    }

    // ── (d) unresolved external (sprk_isresolved=false) → contributes no recipient ───────────────────────

    [Fact]
    public async Task GetEligibleRecipients_UnresolvedExternalParticipant_ContributesNoRecipient()
    {
        var messageId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var internalUser = Guid.NewGuid();

        var sut = CreateService(new[]
        {
            UnresolvedExternalParticipant(),      // no systemuserid to target
            SystemUserParticipant(internalUser),  // the one deliverable recipient
        });

        var recipients = await sut.GetEligibleRecipientsAsync(
            Message(messageId, isInternalOnly: false), OpenThread(threadId));

        recipients.Should().BeEquivalentTo(new[] { internalUser },
            "an unresolved external address resolves to no systemuserid → no recipient");
    }

    // ── (e) POSITIVE: internal participant, non-internal-only, non-private → targeted ───────────────────

    [Fact]
    public async Task GetEligibleRecipients_InternalParticipantOnOpenNonInternalOnlyMessage_IsTargeted()
    {
        var messageId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var internalUser = Guid.NewGuid();

        var sut = CreateService(new[] { SystemUserParticipant(internalUser) });

        var recipients = await sut.GetEligibleRecipientsAsync(
            Message(messageId, isInternalOnly: false), OpenThread(threadId));

        recipients.Should().ContainSingle().Which.Should().Be(internalUser,
            "the negative-access rules must not over-exclude a legitimate internal recipient");
    }

    // ── (f) fail-closed: UNREADABLE sprk_isinternalonly → non-internal candidate EXCLUDED ────────────────

    [Fact]
    public async Task GetEligibleRecipients_UnreadableInternalOnlyFlag_ExcludesExternalKeepsInternal()
    {
        var messageId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var internalUser = Guid.NewGuid();
        var externalContact = Guid.NewGuid();

        var sut = CreateService(new[]
        {
            SystemUserParticipant(internalUser),
            ContactParticipant(externalContact),
        });

        // Message with NO sprk_isinternalonly attribute → the filter's IsInternalOnlyOrUnknown treats it as
        // internal-only for the non-internal candidate (fail closed, mirroring CommunicationAccessFilter).
        var recipients = await sut.GetEligibleRecipientsAsync(
            Message(messageId, isInternalOnly: null), OpenThread(threadId));

        recipients.Should().NotContain(externalContact,
            "an unreadable internal-only flag fails closed — the external candidate is excluded");
        recipients.Should().Contain(internalUser, "the fail-closed rule must not exclude an internal candidate");
        recipients.Should().HaveCount(1);
    }

    // ── (g) THE LEAK THIS FIX CLOSES: internal-only message + EXTERNAL-LICENSED SYSTEMUSER → EXCLUDED ─────
    // An external party can be a licensed systemuser (owner confirmation 2026-07-21). The old
    // "systemuser ⇒ internal" proxy would have INCLUDED this user on an internal-only message. The
    // authoritative systemuser.sprk_isexternal flag excludes them.

    [Fact]
    public async Task GetEligibleRecipients_InternalOnlyMessageWithExternalLicensedSystemUser_ExcludesExternalKeepsInternal()
    {
        var messageId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var internalUser = Guid.NewGuid();
        var externalLicensedUser = Guid.NewGuid(); // a systemuser, but sprk_isexternal = true

        var sut = CreateService(
            new[]
            {
                SystemUserParticipant(internalUser),           // sprk_isexternal = false → internal
                SystemUserParticipant(externalLicensedUser),   // sprk_isexternal = true  → external
            },
            externalSystemUsers: new[] { externalLicensedUser });

        var recipients = await sut.GetEligibleRecipientsAsync(
            Message(messageId, isInternalOnly: true), OpenThread(threadId));

        recipients.Should().NotContain(externalLicensedUser,
            "an external-licensed systemuser must NOT receive an internal-only message — the fix consults sprk_isexternal, not record type");
        recipients.Should().Contain(internalUser, "the genuinely internal systemuser participant is still targeted");
        recipients.Should().HaveCount(1);
    }
}
