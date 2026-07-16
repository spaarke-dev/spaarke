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
            Mock.Of<ILogger<IncomingAssociationResolver>>());
    }

    // =========================================================================
    // Priority 1: Thread matching
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ThreadMatch_CopiesParentAssociations()
    {
        // Arrange: envelope carries an In-Reply-To parent id; parent has matter + organization.
        var parentMatterId = Guid.NewGuid();
        var parentOrgId = Guid.NewGuid();

        var parentComm = new DataverseEntity("sprk_communication");
        parentComm["sprk_regardingmatter"] = new EntityReference("sprk_matter", parentMatterId);
        parentComm["sprk_regardingorganization"] = new EntityReference("account", parentOrgId);

        _dataverseServiceMock
            .Setup(d => d.GetCommunicationByGraphMessageIdAsync("<parent-msg-id@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComm);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var envelope = CreateEnvelope("Re: Test Subject", "sender@external.com", inReplyTo: "<parent-msg-id@contoso.com>");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, envelope, new AssociationContext(), CancellationToken.None);

        // Assert: verify update was called with parent's associations and Resolved status
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingmatter") &&
                fields.ContainsKey("sprk_regardingorganization") &&
                fields.ContainsKey("sprk_associationstatus") &&
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000000), // Resolved
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Priority 2: Sender (participant) matching
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SenderMatch_LinksToContact_AsSuggested()
    {
        // R-7 evolution (task 015 / FR-11): the sender→contact regarding WRITE is preserved (the person
        // lookup is still set), but the STATUS is now Suggested (100000003), not Resolved. A lone
        // participant-correlation contact match carries confidence 0.70 (< the 0.85 auto-file threshold),
        // so the confidence→status ladder correctly surfaces it for confirmation rather than auto-filing.
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

        // Assert: contact should be set as regarding person
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingperson") &&
                ((EntityReference)fields["sprk_regardingperson"]).Id == contactId &&
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
    public async Task ResolveAsync_SenderDomainMatch_WritesOrganizationAndAccountToSeparateLookups()
    {
        // Regression (task 004 / DEC-3): a sender-domain match MUST write
        // sprk_regardingorganization -> sprk_organization AND sprk_regardingaccount -> account,
        // each to its OWN lookup. The prior bug wrote an account reference into the
        // sprk_regardingorganization lookup (which targets sprk_organization). sprk_organization
        // is the legal entity; account is a vendor/payment account — distinct, never mixed.
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

        // Assert: org -> sprk_organization lookup, account -> account lookup, correct types, no cross-stuffing
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingorganization") &&
                ((EntityReference)fields["sprk_regardingorganization"]).LogicalName == "sprk_organization" &&
                ((EntityReference)fields["sprk_regardingorganization"]).Id == orgId &&
                fields.ContainsKey("sprk_regardingaccount") &&
                ((EntityReference)fields["sprk_regardingaccount"]).LogicalName == "account" &&
                ((EntityReference)fields["sprk_regardingaccount"]).Id == accountId),
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
    // Test Helpers
    // =========================================================================

    private static NormalizedMessage CreateEnvelope(string subject, string fromEmail, string? inReplyTo = null)
        => new()
        {
            Direction = CommunicationDirection.Incoming,
            Subject = subject,
            From = fromEmail,
            InReplyTo = inReplyTo,
        };
}
