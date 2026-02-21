using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Tests the communication status retrieval logic that maps Dataverse
/// sprk_communication entity fields to CommunicationStatusResponse.
/// Since GetCommunicationStatusAsync is private static on CommunicationEndpoints,
/// these tests verify the Dataverse → response mapping logic directly.
/// </summary>
public class CommunicationStatusEndpointTests
{
    #region Test Infrastructure

    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<CommunicationService>> _loggerMock;

    public CommunicationStatusEndpointTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<CommunicationService>>();
    }

    /// <summary>
    /// Replicates the exact logic of CommunicationEndpoints.GetCommunicationStatusAsync
    /// to unit test the Dataverse → CommunicationStatusResponse mapping.
    /// </summary>
    private async Task<CommunicationStatusResponse> GetStatusAsync(Guid id, CancellationToken ct = default)
    {
        DataverseEntity entity;
        try
        {
            entity = await _dataverseServiceMock.Object.RetrieveAsync(
                "sprk_communication",
                id,
                new[] { "statuscode", "sprk_graphmessageid", "sprk_sentat", "sprk_from" },
                ct);
        }
        catch (Exception ex)
        {
            _loggerMock.Object.LogWarning(ex, "Communication {CommunicationId} not found in Dataverse", id);
            throw new SdapProblemException(
                code: "COMMUNICATION_NOT_FOUND",
                title: "Communication not found",
                detail: $"Communication with ID '{id}' does not exist.",
                statusCode: 404);
        }

        var statusCodeValue = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1;
        var status = (CommunicationStatus)statusCodeValue;

        var sentAtDateTime = entity.GetAttributeValue<DateTime?>("sprk_sentat");
        DateTimeOffset? sentAt = sentAtDateTime.HasValue
            ? new DateTimeOffset(sentAtDateTime.Value, TimeSpan.Zero)
            : null;

        return new CommunicationStatusResponse
        {
            CommunicationId = id,
            Status = status,
            GraphMessageId = entity.GetAttributeValue<string>("sprk_graphmessageid"),
            SentAt = sentAt,
            From = entity.GetAttributeValue<string>("sprk_from")
        };
    }

    private DataverseEntity CreateCommunicationEntity(
        CommunicationStatus status,
        string? graphMessageId = "msg-123",
        DateTime? sentAt = null,
        string? from = "noreply@contoso.com")
    {
        var entity = new DataverseEntity("sprk_communication");
        entity["statuscode"] = new OptionSetValue((int)status);

        if (graphMessageId is not null)
            entity["sprk_graphmessageid"] = graphMessageId;

        if (sentAt.HasValue)
            entity["sprk_sentat"] = sentAt.Value;

        if (from is not null)
            entity["sprk_from"] = from;

        return entity;
    }

    #endregion

    #region Successful Retrieval

    [Fact]
    public async Task GetStatus_WithSendStatus_ReturnsSendStatus()
    {
        // Arrange
        var communicationId = Guid.NewGuid();
        var sentTime = new DateTime(2026, 2, 15, 10, 30, 0, DateTimeKind.Utc);

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                "sprk_communication",
                communicationId,
                It.Is<string[]>(cols => cols.Contains("statuscode") && cols.Contains("sprk_graphmessageid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCommunicationEntity(
                CommunicationStatus.Send,
                graphMessageId: "graph-msg-abc",
                sentAt: sentTime,
                from: "noreply@contoso.com"));

        // Act
        var response = await GetStatusAsync(communicationId);

        // Assert
        response.CommunicationId.Should().Be(communicationId);
        response.Status.Should().Be(CommunicationStatus.Send);
        response.GraphMessageId.Should().Be("graph-msg-abc");
        response.SentAt.Should().NotBeNull();
        response.SentAt!.Value.DateTime.Should().Be(sentTime);
        response.From.Should().Be("noreply@contoso.com");
    }

    [Fact]
    public async Task GetStatus_WithDraftStatus_ReturnsDraftStatus()
    {
        // Arrange
        var communicationId = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                "sprk_communication",
                communicationId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCommunicationEntity(
                CommunicationStatus.Draft,
                graphMessageId: null,
                sentAt: null,
                from: "noreply@contoso.com"));

        // Act
        var response = await GetStatusAsync(communicationId);

        // Assert
        response.Status.Should().Be(CommunicationStatus.Draft);
        response.GraphMessageId.Should().BeNull();
        response.SentAt.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_WithFailedStatus_ReturnsFailedStatus()
    {
        // Arrange
        var communicationId = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                "sprk_communication",
                communicationId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCommunicationEntity(CommunicationStatus.Failed));

        // Act
        var response = await GetStatusAsync(communicationId);

        // Assert
        response.Status.Should().Be(CommunicationStatus.Failed);
    }

    #endregion

    #region Field Mapping

    [Fact]
    public async Task GetStatus_QueriesCorrectColumns()
    {
        // Arrange
        var communicationId = Guid.NewGuid();
        string[]? queriedColumns = null;

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string[], CancellationToken>((_, _, cols, _) => queriedColumns = cols)
            .ReturnsAsync(CreateCommunicationEntity(CommunicationStatus.Send));

        // Act
        await GetStatusAsync(communicationId);

        // Assert
        queriedColumns.Should().NotBeNull();
        queriedColumns.Should().Contain("statuscode");
        queriedColumns.Should().Contain("sprk_graphmessageid");
        queriedColumns.Should().Contain("sprk_sentat");
        queriedColumns.Should().Contain("sprk_from");
    }

    [Fact]
    public async Task GetStatus_QueriesCorrectEntityName()
    {
        // Arrange
        var communicationId = Guid.NewGuid();
        string? queriedEntityName = null;

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string[], CancellationToken>((name, _, _, _) => queriedEntityName = name)
            .ReturnsAsync(CreateCommunicationEntity(CommunicationStatus.Send));

        // Act
        await GetStatusAsync(communicationId);

        // Assert
        queriedEntityName.Should().Be("sprk_communication");
    }

    [Fact]
    public async Task GetStatus_MapsSentAtToDateTimeOffset_WithZeroUtcOffset()
    {
        // Arrange
        var communicationId = Guid.NewGuid();
        var sentTime = new DateTime(2026, 1, 20, 14, 0, 0, DateTimeKind.Utc);

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                It.IsAny<string>(),
                communicationId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCommunicationEntity(CommunicationStatus.Send, sentAt: sentTime));

        // Act
        var response = await GetStatusAsync(communicationId);

        // Assert
        response.SentAt.Should().NotBeNull();
        response.SentAt!.Value.Offset.Should().Be(TimeSpan.Zero, "SentAt offset should be UTC (TimeSpan.Zero)");
    }

    [Fact]
    public async Task GetStatus_WhenStatusCodeMissing_DefaultsToDraft()
    {
        // Arrange - entity without statuscode attribute
        var communicationId = Guid.NewGuid();
        var entity = new DataverseEntity("sprk_communication");
        // No statuscode set — GetAttributeValue<OptionSetValue> returns null, so default is 1 (Draft)

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                It.IsAny<string>(),
                communicationId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var response = await GetStatusAsync(communicationId);

        // Assert
        response.Status.Should().Be(CommunicationStatus.Draft,
            "when statuscode OptionSetValue is null, default value 1 maps to Draft");
    }

    #endregion

    #region Not Found

    [Fact]
    public async Task GetStatus_WhenEntityNotFound_ThrowsCommunicationNotFoundProblem()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                "sprk_communication",
                nonExistentId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity not found"));

        // Act
        var act = () => GetStatusAsync(nonExistentId);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("COMMUNICATION_NOT_FOUND");
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Detail.Should().Contain(nonExistentId.ToString());
    }

    [Fact]
    public async Task GetStatus_WhenDataverseThrowsAnyException_Returns404ProblemDetails()
    {
        // Arrange
        var id = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                It.IsAny<string>(),
                id,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse timeout"));

        // Act
        var act = () => GetStatusAsync(id);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("COMMUNICATION_NOT_FOUND");
        ex.Which.Title.Should().Be("Communication not found");
        ex.Which.StatusCode.Should().Be(404);
    }

    #endregion

    #region Response CommunicationId

    [Fact]
    public async Task GetStatus_ReturnsCommunicationIdInResponse()
    {
        // Arrange
        var communicationId = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(ds => ds.RetrieveAsync(
                It.IsAny<string>(),
                communicationId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCommunicationEntity(CommunicationStatus.Send));

        // Act
        var response = await GetStatusAsync(communicationId);

        // Assert
        response.CommunicationId.Should().Be(communicationId);
    }

    #endregion
}
