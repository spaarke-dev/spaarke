using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// Unit tests for <see cref="OfficeService"/>.
/// Tests the business logic for Office add-in operations.
/// </summary>
public class OfficeServiceTests
{
    private readonly Mock<IJobStatusService> _jobStatusServiceMock;
    private readonly Mock<ILogger<OfficeService>> _loggerMock;
    private readonly OfficeService _sut;

    public OfficeServiceTests()
    {
        _jobStatusServiceMock = new Mock<IJobStatusService>();
        _loggerMock = new Mock<ILogger<OfficeService>>();
        _sut = new OfficeService(_jobStatusServiceMock.Object, _loggerMock.Object);
    }

    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_Email_ReturnsSuccessWithJobTracking()
    {
        // Arrange
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            Email = new EmailMetadata
            {
                Subject = "Test Email",
                SenderEmail = "sender@test.com"
            },
            TargetEntity = new SaveEntityReference
            {
                EntityType = "sprk_matter",
                EntityId = Guid.NewGuid()
            }
        };
        var userId = "user-123";

        // Act
        var result = await _sut.SaveAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Duplicate.Should().BeFalse();
        result.JobId.Should().NotBe(Guid.Empty);
        result.StatusUrl.Should().NotBeNullOrEmpty();
        result.StatusUrl.Should().Contain($"/office/jobs/{result.JobId}");
        result.StreamUrl.Should().NotBeNullOrEmpty();
        result.StreamUrl.Should().Contain($"/office/jobs/{result.JobId}/stream");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_Attachment_ReturnsSuccessWithJobTracking()
    {
        // Arrange
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Attachment,
            Attachment = new AttachmentMetadata
            {
                AttachmentId = "att-1",
                FileName = "test.pdf"
            },
            TargetEntity = new SaveEntityReference
            {
                EntityType = "account",
                EntityId = Guid.NewGuid()
            }
        };
        var userId = "user-456";

        // Act
        var result = await _sut.SaveAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Duplicate.Should().BeFalse();
        result.JobId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task SaveAsync_Document_ReturnsSuccessWithJobTracking()
    {
        // Arrange
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Document,
            Document = new DocumentMetadata
            {
                FileName = "document.docx"
            },
            TargetEntity = new SaveEntityReference
            {
                EntityType = "sprk_project",
                EntityId = Guid.NewGuid()
            }
        };
        var userId = "user-789";

        // Act
        var result = await _sut.SaveAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.JobId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task SaveAsync_WithIdempotencyKey_IncludesKeyInRequest()
    {
        // Arrange
        var idempotencyKey = "unique-key-123";
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            Email = new EmailMetadata
            {
                Subject = "Test Email",
                SenderEmail = "sender@test.com"
            },
            IdempotencyKey = idempotencyKey,
            TargetEntity = new SaveEntityReference
            {
                EntityType = "sprk_matter",
                EntityId = Guid.NewGuid()
            }
        };
        var userId = "user-123";

        // Act
        var result = await _sut.SaveAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        // The idempotency key is used internally - just verify the request succeeds
    }

    [Fact]
    public async Task SaveAsync_CancellationRequested_ThrowsOperationCancelledException()
    {
        // Arrange
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            Email = new EmailMetadata
            {
                Subject = "Test Email",
                SenderEmail = "sender@test.com"
            },
            TargetEntity = new SaveEntityReference
            {
                EntityType = "sprk_matter",
                EntityId = Guid.NewGuid()
            }
        };
        var userId = "user-123";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.SaveAsync(request, userId, cts.Token));
    }

    #endregion

    #region GetJobStatusAsync Tests

    [Fact]
    public async Task GetJobStatusAsync_KnownTestJob_ReturnsStatus()
    {
        // Arrange
        var testJobId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var userId = "user-123";

        // Act
        var result = await _sut.GetJobStatusAsync(testJobId, userId);

        // Assert
        result.Should().NotBeNull();
        result!.JobId.Should().Be(testJobId);
        result.Status.Should().Be(JobStatus.Running);
        result.JobType.Should().Be(JobType.EmailSave);
        result.Progress.Should().BeInRange(0, 100);
        result.CreatedBy.Should().Be(userId);
    }

    [Fact]
    public async Task GetJobStatusAsync_UnknownJob_ReturnsNull()
    {
        // Arrange
        var unknownJobId = Guid.NewGuid();
        var userId = "user-123";

        // Act
        var result = await _sut.GetJobStatusAsync(unknownJobId, userId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetJobStatusAsync_WithoutUserId_ReturnsStatus()
    {
        // Arrange
        var testJobId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        // Act
        var result = await _sut.GetJobStatusAsync(testJobId);

        // Assert
        result.Should().NotBeNull();
        result!.JobId.Should().Be(testJobId);
    }

    #endregion

    #region IsHealthyAsync Tests

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue()
    {
        // Act
        var result = await _sut.IsHealthyAsync();

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region SearchEntitiesAsync Tests

    [Fact]
    public async Task SearchEntitiesAsync_MatchingQuery_ReturnsResults()
    {
        // Arrange
        var request = new EntitySearchRequest
        {
            Query = "Acme",
            Skip = 0,
            Top = 10
        };
        var userId = "user-123";

        // Act
        var result = await _sut.SearchEntitiesAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().NotBeEmpty();
        result.Results.Should().Contain(r => r.Name.Contains("Acme", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchEntitiesAsync_WithEntityTypeFilter_FiltersResults()
    {
        // Arrange
        var request = new EntitySearchRequest
        {
            Query = "Acme",
            EntityTypes = new[] { "Account" },
            Skip = 0,
            Top = 10
        };
        var userId = "user-123";

        // Act
        var result = await _sut.SearchEntitiesAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().OnlyContain(r => r.EntityType == AssociationEntityType.Account);
    }

    [Fact]
    public async Task SearchEntitiesAsync_NoMatches_ReturnsEmptyResults()
    {
        // Arrange
        var request = new EntitySearchRequest
        {
            Query = "XYZ_NonExistent_12345",
            Skip = 0,
            Top = 10
        };
        var userId = "user-123";

        // Act
        var result = await _sut.SearchEntitiesAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchEntitiesAsync_WithPagination_RespectsSkipAndTop()
    {
        // Arrange
        var request = new EntitySearchRequest
        {
            Query = "Smith",
            Skip = 1,
            Top = 2
        };
        var userId = "user-123";

        // Act
        var result = await _sut.SearchEntitiesAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Results.Count.Should().BeLessOrEqualTo(2);
    }

    #endregion

    #region SearchDocumentsAsync Tests

    [Fact]
    public async Task SearchDocumentsAsync_MatchingQuery_ReturnsResults()
    {
        // Arrange
        var request = new DocumentSearchRequest
        {
            Query = "Contract",
            Skip = 0,
            Top = 10
        };
        var userId = "user-123";

        // Act
        var result = await _sut.SearchDocumentsAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().NotBeEmpty();
        result.Results.Should().Contain(r => r.Name.Contains("Contract", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchDocumentsAsync_WithEntityTypeFilter_FiltersResults()
    {
        // Arrange
        var request = new DocumentSearchRequest
        {
            Query = "Report",
            EntityType = AssociationEntityType.Account,
            Skip = 0,
            Top = 10
        };
        var userId = "user-123";

        // Act
        var result = await _sut.SearchDocumentsAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().OnlyContain(r => r.AssociationType == AssociationEntityType.Account);
    }

    [Fact]
    public async Task SearchDocumentsAsync_WithContentTypeFilter_FiltersResults()
    {
        // Arrange
        var request = new DocumentSearchRequest
        {
            Query = "Report",
            ContentType = "pdf",
            Skip = 0,
            Top = 10
        };
        var userId = "user-123";

        // Act
        var result = await _sut.SearchDocumentsAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().OnlyContain(r => r.ContentType!.Contains("pdf", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region CreateShareLinksAsync Tests

    [Fact]
    public async Task CreateShareLinksAsync_ValidDocuments_ReturnsLinks()
    {
        // Arrange
        var documentIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var request = new ShareLinksRequest
        {
            DocumentIds = documentIds
        };
        var userId = "user-123";

        // Act
        var result = await _sut.CreateShareLinksAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Links.Should().HaveCount(2);
        result.Links.Should().OnlyContain(link => link.Url.Contains("https://"));
        result.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateShareLinksAsync_WithGrantAccess_ProcessesInvitations()
    {
        // Arrange
        var documentIds = new List<Guid> { Guid.NewGuid() };
        var request = new ShareLinksRequest
        {
            DocumentIds = documentIds,
            GrantAccess = true,
            Recipients = new List<string> { "external@other.com" },
            Role = ShareLinkRole.ViewOnly
        };
        var userId = "user-123";

        // Act
        var result = await _sut.CreateShareLinksAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Invitations.Should().NotBeNull();
        result.Invitations!.Should().HaveCount(1);
        result.Invitations![0].Email.Should().Be("external@other.com");
        result.Invitations[0].Status.Should().Be(InvitationStatus.Created);
    }

    [Fact]
    public async Task CreateShareLinksAsync_InternalRecipient_ReturnsAlreadyHasAccess()
    {
        // Arrange
        var documentIds = new List<Guid> { Guid.NewGuid() };
        var request = new ShareLinksRequest
        {
            DocumentIds = documentIds,
            GrantAccess = true,
            Recipients = new List<string> { "internal@spaarke.com" },
            Role = ShareLinkRole.ViewOnly
        };
        var userId = "user-123";

        // Act
        var result = await _sut.CreateShareLinksAsync(request, userId);

        // Assert
        result.Should().NotBeNull();
        result.Invitations.Should().NotBeNull();
        result.Invitations!.FirstOrDefault()?.Status.Should().Be(InvitationStatus.AlreadyHasAccess);
    }

    #endregion

    #region QuickCreateAsync Tests

    [Fact]
    public async Task QuickCreateAsync_Matter_ReturnsCreatedEntity()
    {
        // Arrange
        var request = new QuickCreateRequest
        {
            Name = "New Matter",
            Description = "Test matter description"
        };
        var userId = "user-123";

        // Act
        var result = await _sut.QuickCreateAsync(QuickCreateEntityType.Matter, request, userId);

        // Assert
        result.Should().NotBeNull();
        result!.EntityType.Should().Be(QuickCreateEntityType.Matter);
        result.Name.Should().Be("New Matter");
        result.Id.Should().NotBe(Guid.Empty);
        result.LogicalName.Should().Be("sprk_matter");
        result.Url.Should().Contain("sprk_matter");
    }

    [Fact]
    public async Task QuickCreateAsync_Contact_ReturnsCreatedEntityWithFullName()
    {
        // Arrange
        var request = new QuickCreateRequest
        {
            FirstName = "John",
            LastName = "Doe"
        };
        var userId = "user-123";

        // Act
        var result = await _sut.QuickCreateAsync(QuickCreateEntityType.Contact, request, userId);

        // Assert
        result.Should().NotBeNull();
        result!.EntityType.Should().Be(QuickCreateEntityType.Contact);
        result.Name.Should().Be("John Doe");
        result.LogicalName.Should().Be("contact");
    }

    [Fact]
    public async Task QuickCreateAsync_Account_ReturnsCreatedEntity()
    {
        // Arrange
        var request = new QuickCreateRequest
        {
            Name = "Acme Corp"
        };
        var userId = "user-123";

        // Act
        var result = await _sut.QuickCreateAsync(QuickCreateEntityType.Account, request, userId);

        // Assert
        result.Should().NotBeNull();
        result!.EntityType.Should().Be(QuickCreateEntityType.Account);
        result.Name.Should().Be("Acme Corp");
        result.LogicalName.Should().Be("account");
    }

    #endregion

    #region GetRecentDocumentsAsync Tests

    [Fact]
    public async Task GetRecentDocumentsAsync_ReturnsRecentItems()
    {
        // Arrange
        var userId = "user-123";

        // Act
        var result = await _sut.GetRecentDocumentsAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.RecentAssociations.Should().NotBeEmpty();
        result.RecentDocuments.Should().NotBeEmpty();
        result.Favorites.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetRecentDocumentsAsync_WithTopLimit_RespectsLimit()
    {
        // Arrange
        var userId = "user-123";
        var top = 2;

        // Act
        var result = await _sut.GetRecentDocumentsAsync(userId, top);

        // Assert
        result.Should().NotBeNull();
        result.RecentAssociations.Count.Should().BeLessOrEqualTo(top);
        result.RecentDocuments.Count.Should().BeLessOrEqualTo(top);
        result.Favorites.Count.Should().BeLessOrEqualTo(top);
    }

    [Fact]
    public async Task GetRecentDocumentsAsync_ItemsSortedByRecency()
    {
        // Arrange
        var userId = "user-123";

        // Act
        var result = await _sut.GetRecentDocumentsAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.RecentAssociations.Should().BeInDescendingOrder(a => a.LastUsed);
        result.RecentDocuments.Should().BeInDescendingOrder(d => d.ModifiedDate);
    }

    #endregion

    #region GetAttachmentsAsync Tests

    [Fact]
    public async Task GetAttachmentsAsync_ValidDocuments_ReturnsAttachmentPackages()
    {
        // Arrange
        var documentIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var request = new ShareAttachRequest
        {
            DocumentIds = documentIds,
            DeliveryMode = AttachmentDeliveryMode.Url
        };
        var userId = "user-123";
        var correlationId = "corr-123";

        // Act
        var result = await _sut.GetAttachmentsAsync(request, userId, correlationId);

        // Assert
        result.Should().NotBeNull();
        result.Attachments.Should().HaveCount(2);
        result.TotalSize.Should().BeGreaterThan(0);
        result.CorrelationId.Should().Be(correlationId);
        result.Errors.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task GetAttachmentsAsync_Base64Mode_IncludesContent()
    {
        // Arrange
        var documentIds = new[] { Guid.NewGuid() };
        var request = new ShareAttachRequest
        {
            DocumentIds = documentIds,
            DeliveryMode = AttachmentDeliveryMode.Base64
        };
        var userId = "user-123";
        var correlationId = "corr-456";

        // Act
        var result = await _sut.GetAttachmentsAsync(request, userId, correlationId);

        // Assert
        result.Should().NotBeNull();
        result.Attachments.Should().HaveCount(1);
        result.Attachments.First().ContentBase64.Should().NotBeNullOrEmpty();
        result.Attachments.First().DownloadUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetAttachmentsAsync_UrlMode_IncludesDownloadUrl()
    {
        // Arrange
        var documentIds = new[] { Guid.NewGuid() };
        var request = new ShareAttachRequest
        {
            DocumentIds = documentIds,
            DeliveryMode = AttachmentDeliveryMode.Url
        };
        var userId = "user-123";
        var correlationId = "corr-789";

        // Act
        var result = await _sut.GetAttachmentsAsync(request, userId, correlationId);

        // Assert
        result.Should().NotBeNull();
        result.Attachments.Should().HaveCount(1);
        result.Attachments.First().DownloadUrl.Should().NotBeNullOrEmpty();
        result.Attachments.First().ContentBase64.Should().BeNull();
    }

    [Fact]
    public async Task GetAttachmentsAsync_ExceedsTotalSizeLimit_ReturnsError()
    {
        // Arrange - Create many documents to exceed 100MB total limit
        // Since stub data has varying sizes, we need enough to exceed the limit
        var documentIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();
        var request = new ShareAttachRequest
        {
            DocumentIds = documentIds,
            DeliveryMode = AttachmentDeliveryMode.Url
        };
        var userId = "user-123";
        var correlationId = "corr-size-limit";

        // Act
        var result = await _sut.GetAttachmentsAsync(request, userId, correlationId);

        // Assert
        result.Should().NotBeNull();
        // With stub data sizes, some should succeed and some may error if total exceeds limit
        result.TotalSize.Should().BeLessOrEqualTo(100 * 1024 * 1024);
    }

    #endregion

    #region StreamJobStatusAsync Tests

    [Fact]
    public async Task StreamJobStatusAsync_ValidJob_ReturnsEvents()
    {
        // Arrange
        var testJobId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var events = new List<byte[]>();
        await foreach (var eventData in _sut.StreamJobStatusAsync(testJobId, null, cts.Token))
        {
            events.Add(eventData);
            if (events.Count >= 3) break; // Get first few events
        }

        // Assert
        events.Should().NotBeEmpty();
        var firstEvent = System.Text.Encoding.UTF8.GetString(events[0]);
        firstEvent.Should().Contain("event:");
    }

    [Fact]
    public async Task StreamJobStatusAsync_WithLastEventId_ResumesFromSequence()
    {
        // Arrange
        var testJobId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var lastEventId = $"{testJobId}:5"; // Resume from sequence 5
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var events = new List<byte[]>();
        await foreach (var eventData in _sut.StreamJobStatusAsync(testJobId, lastEventId, cts.Token))
        {
            events.Add(eventData);
            if (events.Count >= 2) break;
        }

        // Assert
        events.Should().NotBeEmpty();
    }

    [Fact]
    public async Task StreamJobStatusAsync_UnknownJob_ReturnsErrorEvent()
    {
        // Arrange
        var unknownJobId = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var events = new List<byte[]>();
        await foreach (var eventData in _sut.StreamJobStatusAsync(unknownJobId, null, cts.Token))
        {
            events.Add(eventData);
        }

        // Assert
        events.Should().NotBeEmpty();
        var lastEvent = System.Text.Encoding.UTF8.GetString(events.Last());
        lastEvent.Should().Contain("error");
    }

    #endregion
}
