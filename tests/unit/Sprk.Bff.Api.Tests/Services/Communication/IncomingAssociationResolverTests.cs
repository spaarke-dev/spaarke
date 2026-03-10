using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Xrm.Sdk;
using KiotaIParsable = Microsoft.Kiota.Abstractions.Serialization.IParsable;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Unit tests for IncomingAssociationResolver.
/// Tests the priority cascade: thread → sender → subject → mailbox context → pending review.
/// </summary>
public class IncomingAssociationResolverTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly IncomingAssociationResolver _resolver;

    private static readonly Guid TestCommunicationId = Guid.NewGuid();
    private const string TestMailbox = "shared@contoso.com";
    private const string TestGraphMessageId = "AAMkAGQ=";

    public IncomingAssociationResolverTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();

        // Default: Graph ForApp returns a mock client that returns no headers
        // (loose mock — un-setup calls return null, causing thread matching to skip)
        var mockGraphClient = new Mock<GraphServiceClient>(
            MockBehavior.Loose, Mock.Of<IRequestAdapter>(), string.Empty);
        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(mockGraphClient.Object);

        _resolver = new IncomingAssociationResolver(
            _dataverseServiceMock.Object,
            _graphClientFactoryMock.Object,
            Mock.Of<ILogger<IncomingAssociationResolver>>());
    }

    // =========================================================================
    // Priority 1: Thread matching
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ThreadMatch_CopiesParentAssociations()
    {
        // Arrange: Set up a request adapter that returns a message with In-Reply-To header
        var parentMatterId = Guid.NewGuid();
        var parentOrgId = Guid.NewGuid();

        SetupGraphClientWithInReplyToHeader("<parent-msg-id@contoso.com>");

        var parentComm = new DataverseEntity("sprk_communication");
        parentComm["sprk_regardingmatter"] = new EntityReference("sprk_matter", parentMatterId);
        parentComm["sprk_regardingorganization"] = new EntityReference("account", parentOrgId);

        _dataverseServiceMock
            .Setup(d => d.GetCommunicationByGraphMessageIdAsync("<parent-msg-id@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComm);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var message = CreateTestMessage("Re: Test Subject", "sender@external.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, TestMailbox, TestGraphMessageId, message, null, CancellationToken.None);

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
    // Priority 2: Sender matching
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SenderMatch_LinksToContact()
    {
        // Arrange: no thread match, but sender matches a contact
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

        var message = CreateTestMessage("Hello", "jane@external.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, TestMailbox, TestGraphMessageId, message, null, CancellationToken.None);

        // Assert: contact should be set as regarding person
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingperson") &&
                ((EntityReference)fields["sprk_regardingperson"]).Id == contactId &&
                ((OptionSetValue)fields["sprk_associationstatus"]).Value == 100000000), // Resolved
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

        var message = CreateTestMessage("Hello", "user@gmail.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, TestMailbox, TestGraphMessageId, message, null, CancellationToken.None);

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

        var message = CreateTestMessage(subject, "unknown@external.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, TestMailbox, TestGraphMessageId, message, null, CancellationToken.None);

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
    // Priority 4: Mailbox context
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_MailboxContext_UsesDefaultMatter()
    {
        // Arrange: no matches at any level, but account has a default matter
        var defaultMatterId = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(d => d.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        _dataverseServiceMock
            .Setup(d => d.QueryAccountByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);

        _dataverseServiceMock
            .Setup(d => d.UpdateAsync("sprk_communication", TestCommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var account = new CommunicationAccount
        {
            Id = Guid.NewGuid(),
            Name = "Shared Mailbox",
            EmailAddress = TestMailbox,
            DefaultRegardingMatterId = defaultMatterId
        };

        var message = CreateTestMessage("Random subject", "someone@external.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, TestMailbox, TestGraphMessageId, message, account, CancellationToken.None);

        // Assert: default matter should be set
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingmatter") &&
                ((EntityReference)fields["sprk_regardingmatter"]).Id == defaultMatterId &&
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

        var message = CreateTestMessage("Random subject with no patterns", "someone@gmail.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, TestMailbox, TestGraphMessageId, message, null, CancellationToken.None);

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
    public async Task ResolveAsync_PriorityCascade_ThreadWinsOverSender()
    {
        // Arrange: both thread AND sender would match, but thread should win
        var parentMatterId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        // Thread match setup
        SetupGraphClientWithInReplyToHeader("<parent@contoso.com>");

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

        var message = CreateTestMessage("Re: Test", "jane@external.com");

        // Act
        await _resolver.ResolveAsync(TestCommunicationId, TestMailbox, TestGraphMessageId, message, null, CancellationToken.None);

        // Assert: thread match used (matter from parent), not sender match
        _dataverseServiceMock.Verify(d => d.UpdateAsync(
            "sprk_communication",
            TestCommunicationId,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_regardingmatter") &&
                ((EntityReference)fields["sprk_regardingmatter"]).Id == parentMatterId),
            It.IsAny<CancellationToken>()), Times.Once);

        // Sender query should NOT have been called
        _dataverseServiceMock.Verify(
            d => d.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // Test Helpers
    // =========================================================================

    private static Message CreateTestMessage(string subject, string fromEmail)
    {
        return new Message
        {
            Subject = subject,
            From = new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = fromEmail
                }
            }
        };
    }

    /// <summary>
    /// Sets up the Graph mock to return a message with a specific In-Reply-To header value.
    /// Uses Kiota's IRequestAdapter mock to intercept the Graph SDK call.
    /// </summary>
    private void SetupGraphClientWithInReplyToHeader(string inReplyToValue)
    {
        var responseMessage = new Message
        {
            InternetMessageHeaders = new List<InternetMessageHeader>
            {
                new() { Name = "In-Reply-To", Value = inReplyToValue }
            }
        };

        var mockRequestAdapter = new Mock<IRequestAdapter>();
        mockRequestAdapter.Setup(a => a.BaseUrl).Returns("https://graph.microsoft.com/v1.0");

        // Mock SendAsync with the correct Kiota signature
        mockRequestAdapter
            .Setup(a => a.SendAsync(
                It.IsAny<RequestInformation>(),
                It.IsAny<ParsableFactory<Message>>(),
                It.IsAny<Dictionary<string, ParsableFactory<KiotaIParsable>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMessage);

        var graphClient = new GraphServiceClient(mockRequestAdapter.Object);

        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(graphClient);
    }
}
