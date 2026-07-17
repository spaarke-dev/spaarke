using Azure;
using Azure.Communication;
using Azure.Communication.Identity;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="AcsIdentityService"/> — the ACS identity plane (FR-03). These protect branched
/// business logic that would regress silently: (1) idempotent create-vs-reuse of the
/// <c>communicationUserId ↔ Dataverse</c> mapping, (2) <c>chat</c>-scope-only token minting (a
/// security-relevant contract — a widened scope would over-privilege a token), and (3) the 1–24h validity
/// clamp. The ACS SDK + Dataverse client are mocked because no live ACS resource is provisioned (the task's
/// explicit constraint) — the branches under test have no other observable surface.
/// </summary>
public class AcsIdentityServiceTests
{
    private const string Mri = "8:acs:res_00000000-0000-0000-0000-000000000001";

    private static AcsIdentityService BuildSut(
        Mock<CommunicationIdentityClient> identityClient,
        Mock<IGenericEntityService> dataverse,
        int defaultValidityHours = 24)
        => new(
            identityClient.Object,
            dataverse.Object,
            Options.Create(new AcsOptions { DefaultTokenValidityHours = defaultValidityHours }),
            Mock.Of<ILogger<AcsIdentityService>>());

    private static Response<T> Resp<T>(T value) => Response.FromValue(value, Mock.Of<Response>());

    [Fact]
    public async Task EnsureIdentityAsync_WhenNoMappingExists_CreatesIdentityAndPersistsMapping()
    {
        // Arrange — Dataverse record has no sprk_communicationuserid yet.
        var participant = ParticipantReference.SystemUser(Guid.NewGuid());
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveAsync("systemuser", participant.RecordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", participant.RecordId)); // no attribute set

        Dictionary<string, object>? persistedFields = null;
        dataverse
            .Setup(d => d.UpdateAsync("systemuser", participant.RecordId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) => persistedFields = fields)
            .Returns(Task.CompletedTask);

        var identityClient = new Mock<CommunicationIdentityClient>();
        identityClient
            .Setup(c => c.CreateUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resp(new CommunicationUserIdentifier(Mri)));

        var sut = BuildSut(identityClient, dataverse);

        // Act
        var mapping = await sut.EnsureIdentityAsync(participant);

        // Assert — created, persisted on the as-built column, and flagged new.
        mapping.CommunicationUserId.Should().Be(Mri);
        mapping.WasCreated.Should().BeTrue();
        mapping.Participant.Should().Be(participant);
        persistedFields.Should().ContainKey(AcsIdentityService.CommunicationUserIdColumn)
            .WhoseValue.Should().Be(Mri);
        identityClient.Verify(c => c.CreateUserAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureIdentityAsync_WhenMappingExists_ReturnsExistingAndDoesNotCreateOrWrite()
    {
        // Arrange — Dataverse already carries a communicationUserId (idempotency path).
        var participant = ParticipantReference.Contact(Guid.NewGuid());
        var existingRecord = new Entity("contact", participant.RecordId)
        {
            [AcsIdentityService.CommunicationUserIdColumn] = Mri
        };

        var dataverse = new Mock<IGenericEntityService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync("contact", participant.RecordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRecord);

        var identityClient = new Mock<CommunicationIdentityClient>(MockBehavior.Strict);

        var sut = BuildSut(identityClient, dataverse);

        // Act
        var mapping = await sut.EnsureIdentityAsync(participant);

        // Assert — reused; no ACS create, no Dataverse write (Strict mocks would throw on either).
        mapping.CommunicationUserId.Should().Be(Mri);
        mapping.WasCreated.Should().BeFalse();
        dataverse.Verify(d => d.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        identityClient.Verify(c => c.CreateUserAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MintChatTokenAsync_RequestsChatScopeOnly()
    {
        // Arrange
        IEnumerable<CommunicationTokenScope>? requestedScopes = null;
        var identityClient = new Mock<CommunicationIdentityClient>();
        identityClient
            .Setup(c => c.GetTokenAsync(It.IsAny<CommunicationUserIdentifier>(), It.IsAny<IEnumerable<CommunicationTokenScope>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<CommunicationUserIdentifier, IEnumerable<CommunicationTokenScope>, TimeSpan, CancellationToken>((_, scopes, _, _) => requestedScopes = scopes)
            .ReturnsAsync(Resp(new AccessToken("tok", DateTimeOffset.UtcNow.AddHours(24))));

        var sut = BuildSut(identityClient, new Mock<IGenericEntityService>());

        // Act
        var token = await sut.MintChatTokenAsync(Mri);

        // Assert — exactly the chat scope, nothing else (no VoIP / admin scope).
        token.Token.Should().Be("tok");
        requestedScopes.Should().ContainSingle().Which.Should().Be(CommunicationTokenScope.Chat);
    }

    [Theory]
    [InlineData(48, 24)]   // over the ceiling → clamp to 24h
    [InlineData(0.25, 1)]  // under the floor → clamp to 1h
    [InlineData(6, 6)]     // in range → unchanged
    public async Task MintChatTokenAsync_ClampsValidityToAcsRange(double requestedHours, int expectedHours)
    {
        // Arrange
        TimeSpan? passedExpiry = null;
        var identityClient = new Mock<CommunicationIdentityClient>();
        identityClient
            .Setup(c => c.GetTokenAsync(It.IsAny<CommunicationUserIdentifier>(), It.IsAny<IEnumerable<CommunicationTokenScope>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<CommunicationUserIdentifier, IEnumerable<CommunicationTokenScope>, TimeSpan, CancellationToken>((_, _, expiresIn, _) => passedExpiry = expiresIn)
            .ReturnsAsync(Resp(new AccessToken("tok", DateTimeOffset.UtcNow.AddHours(expectedHours))));

        var sut = BuildSut(identityClient, new Mock<IGenericEntityService>());

        // Act
        await sut.MintChatTokenAsync(Mri, TimeSpan.FromHours(requestedHours));

        // Assert
        passedExpiry.Should().Be(TimeSpan.FromHours(expectedHours));
    }

    [Fact]
    public async Task MintChatTokenAsync_WhenNoValidityGiven_UsesConfiguredDefault()
    {
        // Arrange — configured default of 12h should flow to ACS.
        TimeSpan? passedExpiry = null;
        var identityClient = new Mock<CommunicationIdentityClient>();
        identityClient
            .Setup(c => c.GetTokenAsync(It.IsAny<CommunicationUserIdentifier>(), It.IsAny<IEnumerable<CommunicationTokenScope>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<CommunicationUserIdentifier, IEnumerable<CommunicationTokenScope>, TimeSpan, CancellationToken>((_, _, expiresIn, _) => passedExpiry = expiresIn)
            .ReturnsAsync(Resp(new AccessToken("tok", DateTimeOffset.UtcNow.AddHours(12))));

        var sut = BuildSut(identityClient, new Mock<IGenericEntityService>(), defaultValidityHours: 12);

        // Act
        await sut.MintChatTokenAsync(Mri);

        // Assert
        passedExpiry.Should().Be(TimeSpan.FromHours(12));
    }
}
