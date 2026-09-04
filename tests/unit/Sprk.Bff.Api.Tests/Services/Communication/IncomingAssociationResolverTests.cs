using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Characterization tests for the Association Engine (IncomingAssociationResolver refactored in task
/// 011 to operate over the NormalizedMessage envelope). These assert the SAME Dataverse write contract
/// as the pre-011 resolver — the cascade thread → participant(sender) → subject → pending review, the
/// ADR-024 resolver-field writes, and the task-004 org/account separation — proving R-7 preservation.
/// Only the ARRANGE changed (envelope input instead of Microsoft.Graph.Message + a Graph header call);
/// every Verify assertion is carried over verbatim from the baseline.
/// </summary>
public class IncomingAssociationResolverTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly IncomingAssociationResolver _resolver;

    private static readonly Guid TestCommunicationId = Guid.NewGuid();

    public IncomingAssociationResolverTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();

        // IDataverseService implements both ICommunicationDataverseService and IGenericEntityService.
        // The engine no longer depends on IGraphClientFactory (In-Reply-To comes from the envelope).
        // Rungs are injected (DI in production); here we compose the real deterministic rungs (0/1/2)
        // over the same Dataverse mock so the cascade is exercised end-to-end.
        var rungs = new IAssociationRung[]
        {
            new ExplicitReferenceRung(_dataverseServiceMock.Object),
            new ThreadContinuityRung(_dataverseServiceMock.Object),
            new ParticipantCorrelationRung(_dataverseServiceMock.Object),
        };
        _resolver = new IncomingAssociationResolver(
            rungs,
            _dataverseServiceMock.Object,
            _dataverseServiceMock.Object,
            AssociationTestSupport.Mapper(),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            Mock.Of<ILogger<IncomingAssociationResolver>>());
    }

    // =========================================================================
    // Priority 1: Thread matching
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ThreadMatch_CopiesParentCoreAssociation_OrgIsCandidateOnly()
    {
        // Arrange: envelope carries an In-Reply-To parent id; parent has matter + organization.
        var parentMatterId = Guid.NewGuid();
        var parentOrgId = Guid.NewGuid();

        var parentComm = new DataverseEntity("sprk_communication");
        parentComm["sprk_regardingmatter"] = new EntityReference("sprk_matter", parentMatterId);
        parentComm["sprk_regardingorganization"] = new EntityReference("account", parentOrgId);
        parentComm["sprk_associationstatus"] = new OptionSetValue(100000000); // P3: Resolved parent ⇒ reply inherits at 1.0 (auto-file)

        _dataverseServiceMock
            .Setup(d => d.GetCommunicationByGraphMessageIdAsync("<parent-msg-id@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComm);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Re: Test Subject", "sender@external.com", inReplyTo: "<parent-msg-id@contoso.com>");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        // Assert: the CORE matter is inherited + auto-filed (Resolved); the inherited organization is NON-CORE
        // (061 UAT round-3), so it is NOT auto-written — it is surfaced as a review candidate in provenance.
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingmatter") &&
                !fields.ContainsKey("sprk_regardingorganization") &&      // non-core: candidate only, not written
                fields.ContainsKey("sprk_associationstatus") &&
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000000 && // Resolved (on the core matter)
                ((string)fields["sprk_associationprovenance"]).Contains("sprk_regardingorganization")), // still surfaced
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Priority 2: Sender (participant) matching
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SenderMatch_SurfacesContactAsSuggested_NeverAutoWritten()
    {
        // 061 UAT round-3 (owner, 2026-07-31): a contact is NON-CORE — the sender→contact match is surfaced
        // as a Suggested candidate (in provenance) but the sprk_regardingperson lookup is NO LONGER
        // auto-written. This tightens the earlier "write the contact, status Suggested" behavior to
        // "don't auto-associate the contact at all — the user confirms it via r5."
        var contactId = Guid.NewGuid();
        var contactEntity = new DataverseEntity("contact") { Id = contactId };
        contactEntity["fullname"] = "Jane Doe";

        _dataverseServiceMock
            .Setup(d => d.QueryContactByEmailAsync("jane@external.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactEntity);

        _dataverseServiceMock
            .Setup(d => d.QueryAccountByDomainAsync("external.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Hello", "jane@external.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        // Assert: the contact is NOT auto-written; it is surfaced in provenance as a suggestion; status Suggested.
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                !fields.ContainsKey("sprk_regardingperson") &&           // non-core: never auto-written
                ((string)fields["sprk_associationprovenance"]).Contains("sprk_regardingperson") && // surfaced for review
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000003), // Suggested (FR-11 ladder)
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_SenderMatch_SkipsCommonProviders()
    {
        // Arrange: sender is from gmail.com - should NOT match an account
        _dataverseServiceMock
            .Setup(d => d.QueryContactByEmailAsync("user@gmail.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Hello", "user@gmail.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        // Assert: account query should never be called for gmail.com
        _dataverseServiceMock.Verify(
            d => d.QueryAccountByDomainAsync("gmail.com", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // Priority 3: Subject pattern matching
    // =========================================================================

    [Theory]
    [InlineData("Re: Update on MAT-12345 - contract review")]
    [InlineData("FW: Matter #12345 - urgent")]
    [InlineData("SPRK-12345 document attached")]
    [InlineData("Please review [MATTER:12345]")]
    public async Task ResolveAsync_SubjectPattern_ExtractsMatterReference(string subject)
    {
        // Arrange: no thread or sender match, but subject contains matter reference
        var matterId = Guid.NewGuid();
        var matterEntity = new DataverseEntity("sprk_matter") { Id = matterId };
        matterEntity["sprk_name"] = "Test Matter";

        // Return null for contact/account queries (no sender match)
        _dataverseServiceMock
            .Setup(d => d.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        _dataverseServiceMock
            .Setup(d => d.QueryAccountByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);

        _dataverseServiceMock
            .Setup(d => d.QueryMatterByReferenceNumberAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterEntity);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope(subject, "unknown@external.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        // Assert: matter should be set as regarding matter
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingmatter") &&
                ((EntityReference)fields["sprk_regardingmatter"]).Id == matterId &&
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000000), // Resolved
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // No match: Pending Review
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_NoMatch_SetsPendingReview()
    {
        // Arrange: nothing matches
        _dataverseServiceMock
            .Setup(d => d.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        _dataverseServiceMock
            .Setup(d => d.QueryAccountByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Random subject with no patterns", "someone@gmail.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        // Assert: status should be Pending Review (100000001)
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000001 && // Pending Review
                !fields.ContainsKey("sprk_regardingmatter") &&
                !fields.ContainsKey("sprk_regardingperson") &&
                !fields.ContainsKey("sprk_regardingorganization")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Priority cascade: thread wins over sender
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ThreadAndSenderBothMatch_ThreadMatterWinsTheMatterField()
    {
        // R-7 evolution (task 015 / FR-11 signal reinforcement): the engine now evaluates ALL
        // deterministic rungs (no first-match short-circuit) so independent signals can reinforce and the
        // always-run detector pass records category/obligations regardless of the association outcome.
        // The WRITE CONTRACT is preserved — the thread's matter (confidence 1.0) is what fills the
        // sprk_regardingmatter field; the sender-contact match now contributes the complementary
        // sprk_regardingperson field rather than being suppressed. The old "sender query never called"
        // assertion tested the removed short-circuit (an interaction-shape assertion) and no longer holds.
        var parentMatterId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var parentComm = new DataverseEntity("sprk_communication");
        parentComm["sprk_regardingmatter"] = new EntityReference("sprk_matter", parentMatterId);
        parentComm["sprk_associationstatus"] = new OptionSetValue(100000000); // P3: Resolved parent ⇒ reply inherits at 1.0

        _dataverseServiceMock
            .Setup(d => d.GetCommunicationByGraphMessageIdAsync("<parent@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComm);

        // Sender match setup (should NOT be called if thread succeeds)
        var contactEntity = new DataverseEntity("contact") { Id = contactId };
        _dataverseServiceMock
            .Setup(d => d.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactEntity);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Re: Test", "jane@external.com", inReplyTo: "<parent@contoso.com>");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        // Assert: thread match used (matter from parent), not sender match
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingmatter") &&
                ((EntityReference)fields["sprk_regardingmatter"]).Id == parentMatterId &&
                // thread confidence 1.0 ≥ threshold ⇒ auto-file Resolved
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000000),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Sender domain match: organization + account to SEPARATE lookups (task 004)
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SenderDomainMatch_OrgAndAccountAreCandidates_CorrectlyTyped_NeverAutoWritten()
    {
        // Regression (task 004 / DEC-3) re-cast for 061 UAT round-3: organization + account are NON-CORE, so a
        // sender-domain match NO LONGER auto-writes either lookup — both are surfaced as review candidates the
        // user confirms. The task-004 "no cross-stuffing" guarantee is preserved at the CANDIDATE level: the
        // provenance carries sprk_regardingorganization → sprk_organization AND sprk_regardingaccount → account,
        // each correctly typed (an account ref is never stuffed into the org lookup).
        var orgId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(d => d.QueryContactByEmailAsync("ap@acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        _dataverseServiceMock
            .Setup(d => d.QueryOrganizationByDomainAsync("acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_organization") { Id = orgId });
        _dataverseServiceMock
            .Setup(d => d.QueryAccountByDomainAsync("acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("account") { Id = accountId });
        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Invoice question", "ap@acme.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        // Assert: neither non-core lookup is auto-written; both are surfaced in provenance, each correctly
        // typed (org→sprk_organization, account→account) — the no-cross-stuffing guarantee at candidate level.
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                !fields.ContainsKey("sprk_regardingorganization") &&   // non-core: candidate only
                !fields.ContainsKey("sprk_regardingaccount") &&        // non-core: candidate only
                ((string)fields["sprk_associationprovenance"]).Contains("\"sprk_regardingorganization\"") &&
                ((string)fields["sprk_associationprovenance"]).Contains("\"sprk_organization\"") &&
                ((string)fields["sprk_associationprovenance"]).Contains("\"sprk_regardingaccount\"") &&
                ((string)fields["sprk_associationprovenance"]).Contains("\"account\"")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Task 015: provenance persistence + engine-level kill-switch
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_WritesProvenanceJson_ToAssociationProvenanceColumn()
    {
        // A thread match resolves the association; the engine must persist the decision trail as JSON.
        var parentMatterId = Guid.NewGuid();
        var parentComm = new DataverseEntity("sprk_communication");
        parentComm["sprk_regardingmatter"] = new EntityReference("sprk_matter", parentMatterId);
        parentComm["sprk_associationstatus"] = new OptionSetValue(100000000); // P3: Resolved parent ⇒ reply inherits at 1.0

        _dataverseServiceMock
            .Setup(d => d.GetCommunicationByGraphMessageIdAsync("<p@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComm);
        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Re: Test", "jane@external.com", inReplyTo: "<p@contoso.com>");

        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_associationprovenance") &&
                ((string)fields["sprk_associationprovenance"]).Contains("rungsFired") &&
                ((string)fields["sprk_associationprovenance"]).Contains("autoFiled")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_KillSwitchOff_DowngradesResolvedToSuggested_NoRedeploy()
    {
        // Same strong (thread, 1.0) match, but with the ADR-018 kill-switch OFF: a config flip only —
        // the same code path yields Suggested instead of Resolved (auto-file), and the matter is still
        // surfaced as a suggestion.
        var parentMatterId = Guid.NewGuid();
        var parentComm = new DataverseEntity("sprk_communication");
        parentComm["sprk_regardingmatter"] = new EntityReference("sprk_matter", parentMatterId);
        parentComm["sprk_associationstatus"] = new OptionSetValue(100000000); // P3: Resolved parent (pre-kill-switch state)

        _dataverseServiceMock
            .Setup(d => d.GetCommunicationByGraphMessageIdAsync("<p2@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComm);
        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var suggestOnlyResolver = new IncomingAssociationResolver(
            new IAssociationRung[]
            {
                new ExplicitReferenceRung(_dataverseServiceMock.Object),
                new ThreadContinuityRung(_dataverseServiceMock.Object),
                new ParticipantCorrelationRung(_dataverseServiceMock.Object),
            },
            _dataverseServiceMock.Object,
            _dataverseServiceMock.Object,
            AssociationTestSupport.Mapper(enabled: false),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            Mock.Of<ILogger<IncomingAssociationResolver>>());

        var envelope = CreateEnvelope("Re: Test", "jane@external.com", inReplyTo: "<p2@contoso.com>");

        await suggestOnlyResolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingmatter") &&
                ((EntityReference)fields["sprk_regardingmatter"]).Id == parentMatterId &&
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000003), // Suggested
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Denormalized regarding fields (task 132 / UAT R5): name = name, number = number
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_MatterMatch_WritesDenormalizedNameAndNumber_FromTheMatterRecord()
    {
        // Regression (task 132 / UAT R5): the inbound path previously copied EntityReference.Name into
        // sprk_regardingrecordname and never set sprk_regardingrecordnumber. When a rung matched by NUMBER
        // it attached the record NUMBER as that Name, so inbound emails showed "Regarding Name: LITG-119896"
        // with the number field null. The resolver must now retrieve the matter's ACTUAL name + number and
        // denormalize them separately (mirroring the outbound MapAssociationFields contract).
        var matterId = Guid.NewGuid();

        // Reproduce the bug's input exactly: a matter regarding write whose EntityReference.Name carries the
        // record NUMBER (as a number-match rung would set it).
        var bugRung = new StubMatterRung(matterId, entityReferenceName: "LITG-119896");

        // The matter record itself: real name in sprk_mattername, real number in sprk_matternumber.
        var matterRecord = new DataverseEntity("sprk_matter", matterId);
        matterRecord["sprk_mattername"] = "Monte Rosa Biotechnology v Spaarke Inc";
        matterRecord["sprk_matternumber"] = "LITG-119896";
        _dataverseServiceMock
            .Setup(d => d.RetrieveAsync("sprk_matter", matterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterRecord);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var resolver = new IncomingAssociationResolver(
            new IAssociationRung[] { bugRung },
            _dataverseServiceMock.Object,
            _dataverseServiceMock.Object,
            AssociationTestSupport.Mapper(),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            Mock.Of<ILogger<IncomingAssociationResolver>>());

        var envelope = CreateEnvelope("LITG-119896 filing", "clerk@court.gov");

        // Act
        await resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        // Assert: NAME field carries the matter NAME; NUMBER field carries the matter NUMBER.
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                (string)fields["sprk_regardingrecordname"] == "Monte Rosa Biotechnology v Spaarke Inc" &&
                fields.ContainsKey("sprk_regardingrecordnumber") &&
                (string)fields["sprk_regardingrecordnumber"] == "LITG-119896"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // P2 (FR-12 UAT): the denormalized PRIMARY Regarding is substantive-only (never a fallback)
    // =========================================================================

    [Fact]
    public async Task P2_FallbackContactOnly_DoesNotPopulateDenormalizedPrimaryRegarding()
    {
        // A non-core identity match (contact/org/account) must NOT become the denormalized headline
        // "Regarding record". Before P2, a contact-only match populated sprk_regardingrecordtype/id/name with
        // the contact — and, when the substantive matters went Ambiguous, a spurious sub-threshold invoice
        // (the UAT misfile). Under 061 UAT round-3 the contact is NON-CORE, so the typed sprk_regardingperson
        // lookup is ALSO no longer auto-written (it is a candidate); the denormalized PRIMARY stays withheld.
        var contactId = Guid.NewGuid();
        var contactEntity = new DataverseEntity("contact") { Id = contactId };
        _dataverseServiceMock
            .Setup(d => d.QueryContactByEmailAsync("jane@external.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactEntity);
        _dataverseServiceMock
            .Setup(d => d.QueryAccountByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Hello", "jane@external.com");

        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                !fields.ContainsKey("sprk_regardingperson") &&       // non-core: candidate only, not auto-written
                ((string)fields["sprk_associationprovenance"]).Contains("sprk_regardingperson") && // surfaced for review
                !fields.ContainsKey("sprk_regardingrecordtype") &&   // and NO denormalized headline
                !fields.ContainsKey("sprk_regardingrecordid") &&
                !fields.ContainsKey("sprk_regardingrecordname")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // P3 (FR-12 UAT): a thread reply does not auto-file off an UNCONFIRMED parent
    // =========================================================================

    [Fact]
    public async Task P3_ThreadParentNotResolved_ReplyDoesNotAutoFile_SurfacesSuggested()
    {
        // The parent carries a matter but its own association is only Suggested (unconfirmed). Inheriting it at
        // 1.0 would amplify an unconfirmed association into an auto-file across the thread — the misfile
        // propagation the UAT flagged as "as bad as mis-associating the primary record". The reply must inherit
        // the matter as a SURFACED candidate (Suggested), never Resolved.
        var parentMatterId = Guid.NewGuid();
        var parentComm = new DataverseEntity("sprk_communication");
        parentComm["sprk_regardingmatter"] = new EntityReference("sprk_matter", parentMatterId);
        parentComm["sprk_associationstatus"] = new OptionSetValue(100000003); // Suggested — UNCONFIRMED parent

        _dataverseServiceMock
            .Setup(d => d.GetCommunicationByGraphMessageIdAsync("<unconfirmed-parent@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComm);
        _dataverseServiceMock
            .Setup(d => d.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        _dataverseServiceMock
            .Setup(d => d.QueryAccountByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Re: ongoing", "someone@external.com", inReplyTo: "<unconfirmed-parent@contoso.com>");

        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingmatter") &&                          // inherited matter still surfaced
                ((EntityReference)fields["sprk_regardingmatter"]).Id == parentMatterId &&
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000003), // Suggested, NOT Resolved
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // P2b (061 UAT round-2): Ambiguous decision does NOT crown a denormalized headline
    // =========================================================================

    [Fact]
    public async Task P2b_AmbiguousMattersWithLeftoverInvoice_DoesNotPopulateDenormalizedHeadline()
    {
        // The UAT misfile: two matters CONFLICT (Ambiguous → not written) while an incidental invoice matched
        // non-conflicting. The denormalized "primary Regarding" headline (record type/id/name) must NOT be
        // crowned with that leftover invoice — Ambiguous means "the reviewer decides", so no headline. Under
        // 061 UAT round-3 the invoice is ALSO non-core, so the typed sprk_regardinginvoice lookup is not even
        // auto-written — it is surfaced in provenance as a candidate. Both guards now hold: no write, no headline.
        var matterA = Guid.NewGuid();
        var matterB = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var conflictRung = new StubConflictingMattersPlusInvoiceRung(matterA, matterB, invoiceId);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var resolver = new IncomingAssociationResolver(
            new IAssociationRung[] { conflictRung },
            _dataverseServiceMock.Object,
            _dataverseServiceMock.Object,
            AssociationTestSupport.Mapper(),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            Mock.Of<ILogger<IncomingAssociationResolver>>());

        var envelope = CreateEnvelope("Two matters and an invoice", "clerk@court.gov");

        await resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000004 && // Ambiguous
                !fields.ContainsKey("sprk_regardinginvoice") &&       // non-core invoice: candidate only, not written
                ((string)fields["sprk_associationprovenance"]).Contains("sprk_regardinginvoice") && // surfaced for review
                !fields.ContainsKey("sprk_regardingrecordtype") &&    // and NO denormalized headline
                !fields.ContainsKey("sprk_regardingrecordid") &&
                !fields.ContainsKey("sprk_regardingrecordname")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Emits two conflicting matters (same field, each ≥ threshold ⇒ Ambiguous) plus one
    /// non-conflicting invoice — reproducing the UAT shape where a leftover record could be crowned.</summary>
    private sealed class StubConflictingMattersPlusInvoiceRung : IAssociationRung
    {
        private readonly Guid _matterA, _matterB, _invoiceId;
        public StubConflictingMattersPlusInvoiceRung(Guid matterA, Guid matterB, Guid invoiceId)
        {
            _matterA = matterA; _matterB = matterB; _invoiceId = invoiceId;
        }
        public RungKind Kind => RungKind.ExplicitReference;
        public int Order => 0;
        public Task<IReadOnlyList<RungMatch>> EvaluateAsync(
            NormalizedMessage message, AssociationContext context, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<RungMatch>>(new[]
            {
                new RungMatch { RegardingFieldName = "sprk_regardingmatter", Target = new EntityReference("sprk_matter", _matterA), Confidence = 0.9, Provenance = "explicit:test:A", Rung = RungKind.ExplicitReference },
                new RungMatch { RegardingFieldName = "sprk_regardingmatter", Target = new EntityReference("sprk_matter", _matterB), Confidence = 0.9, Provenance = "explicit:test:B", Rung = RungKind.ExplicitReference },
                new RungMatch { RegardingFieldName = "sprk_regardinginvoice", Target = new EntityReference("sprk_invoice", _invoiceId), Confidence = 0.65, Provenance = "explicit:test:inv", Rung = RungKind.ExplicitReference },
            });
    }

    // =========================================================================
    // Test Helpers
    // =========================================================================

    /// <summary>
    /// Minimal deterministic rung that emits a single high-confidence matter regarding write. Exists to
    /// drive the resolver's denormalization write path with a controlled EntityReference.Name (reproducing
    /// the number-in-Name shape the bug depended on). Not a mock of the class-under-test — a rung is the
    /// engine's first-class, DI-injected extension point.
    /// </summary>
    private sealed class StubMatterRung : IAssociationRung
    {
        private readonly Guid _matterId;
        private readonly string _entityReferenceName;
        public StubMatterRung(Guid matterId, string entityReferenceName)
        {
            _matterId = matterId;
            _entityReferenceName = entityReferenceName;
        }
        public RungKind Kind => RungKind.ExplicitReference;
        public int Order => 0;
        public Task<IReadOnlyList<RungMatch>> EvaluateAsync(
            NormalizedMessage message, AssociationContext context, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<RungMatch>>(new[]
            {
                new RungMatch
                {
                    RegardingFieldName = "sprk_regardingmatter",
                    Target = new EntityReference("sprk_matter", _matterId) { Name = _entityReferenceName },
                    Confidence = 1.0,
                    Provenance = "explicit:caller-supplied:sprk_matter",
                    Rung = RungKind.ExplicitReference,
                },
            });
    }

    private static NormalizedMessage CreateEnvelope(string subject, string fromEmail, string? inReplyTo = null)
        => new()
        {
            Direction = CommunicationDirection.Incoming,
            Subject = subject,
            From = fromEmail,
            InReplyTo = inReplyTo,
        };
}
