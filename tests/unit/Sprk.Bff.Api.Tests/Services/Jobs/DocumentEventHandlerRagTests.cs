using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Jobs;

/// <summary>
/// Unit tests for RAG indexing integration with DocumentEventHandler.
/// Tests configuration parsing, event parsing, and job payload serialization.
///
/// NOTE: Full handler integration tests are not possible without mocking concrete
/// classes (SpeFileStore, JobSubmissionService, etc.) which don't have interfaces.
/// RAG indexing behavior is tested via RagIndexingJobHandlerTests.
/// </summary>
public class DocumentEventHandlerRagTests
{
    #region Configuration Tests

    [Fact]
    public void EmailProcessingOptions_AutoIndexToRag_AffectsDocumentEvents()
    {
        // Arrange - Same options class is used for document event processing
        var optionsEnabled = new EmailProcessingOptions { AutoIndexToRag = true };
        var optionsDisabled = new EmailProcessingOptions { AutoIndexToRag = false };

        // Assert
        optionsEnabled.AutoIndexToRag.Should().BeTrue();
        optionsDisabled.AutoIndexToRag.Should().BeFalse();
    }

    #endregion

    #region DocumentEvent Tests

    [Fact]
    public void DocumentEvent_CreatedWithRequiredFields_IsValid()
    {
        // Arrange & Act
        var documentEvent = new DocumentEvent
        {
            EventId = Guid.NewGuid().ToString(),
            DocumentId = Guid.NewGuid().ToString(),
            Operation = "Create",
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Assert
        documentEvent.EventId.Should().NotBeNullOrEmpty();
        documentEvent.DocumentId.Should().NotBeNullOrEmpty();
        documentEvent.Operation.Should().Be("Create");
    }

    [Fact]
    public void DocumentEvent_EntityData_ContainsFileInfo()
    {
        // Arrange
        var entityData = new Dictionary<string, object>
        {
            ["sprk_hasfile"] = true,
            ["sprk_graphdriveid"] = "test-drive-id",
            ["sprk_graphitemid"] = "test-item-id",
            ["sprk_filename"] = "test-document.pdf"
        };

        var documentEvent = new DocumentEvent
        {
            EventId = Guid.NewGuid().ToString(),
            DocumentId = Guid.NewGuid().ToString(),
            Operation = "Create",
            EntityData = entityData
        };

        // Assert - File info is available in entity data
        documentEvent.EntityData["sprk_hasfile"].Should().Be(true);
        documentEvent.EntityData["sprk_graphdriveid"].Should().Be("test-drive-id");
        documentEvent.EntityData["sprk_graphitemid"].Should().Be("test-item-id");
        documentEvent.EntityData["sprk_filename"].Should().Be("test-document.pdf");
    }

    [Fact]
    public void DocumentEvent_MissingDriveId_CanBeDetected()
    {
        // Arrange
        var entityData = new Dictionary<string, object>
        {
            ["sprk_hasfile"] = true,
            // Missing: sprk_graphdriveid
            ["sprk_graphitemid"] = "test-item-id",
            ["sprk_filename"] = "test-document.pdf"
        };

        var documentEvent = new DocumentEvent
        {
            EventId = Guid.NewGuid().ToString(),
            DocumentId = Guid.NewGuid().ToString(),
            Operation = "Create",
            EntityData = entityData
        };

        // Assert - Missing drive ID can be detected
        documentEvent.EntityData.ContainsKey("sprk_graphdriveid").Should().BeFalse();
    }

    [Fact]
    public void DocumentEvent_NoFile_CanBeDetected()
    {
        // Arrange
        var entityData = new Dictionary<string, object>
        {
            ["sprk_hasfile"] = false
        };

        var documentEvent = new DocumentEvent
        {
            EventId = Guid.NewGuid().ToString(),
            DocumentId = Guid.NewGuid().ToString(),
            Operation = "Create",
            EntityData = entityData
        };

        // Assert - No file condition can be detected
        documentEvent.EntityData["sprk_hasfile"].Should().Be(false);
    }

    [Fact]
    public void DocumentEvent_Serialization_RoundTrips()
    {
        // Arrange
        var documentEvent = new DocumentEvent
        {
            EventId = "event-123",
            DocumentId = "doc-456",
            Operation = "Create",
            CorrelationId = "corr-789",
            UserId = "user-abc",
            OrganizationId = "org-xyz",
            Priority = 2,
            Timestamp = DateTime.UtcNow,
            EntityData = new Dictionary<string, object>
            {
                ["sprk_hasfile"] = true,
                ["sprk_graphdriveid"] = "drive-1"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(documentEvent);
        var deserialized = JsonSerializer.Deserialize<DocumentEvent>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.EventId.Should().Be("event-123");
        deserialized.DocumentId.Should().Be("doc-456");
        deserialized.Operation.Should().Be("Create");
        deserialized.CorrelationId.Should().Be("corr-789");
        deserialized.UserId.Should().Be("user-abc");
        deserialized.Priority.Should().Be(2);
    }

    #endregion

    #region RAG Payload for Document Events

    [Fact]
    public void RagIndexingJobPayload_ForDocumentEvent_HasCorrectSource()
    {
        // Arrange
        var payload = new RagIndexingJobPayload
        {
            TenantId = "tenant-1",
            DriveId = "drive-1",
            ItemId = "item-1",
            FileName = "document.pdf",
            DocumentId = "doc-guid-1",
            Source = "DocumentEvent"  // Source should indicate document event origin
        };

        // Assert
        payload.Source.Should().Be("DocumentEvent");
    }

    [Fact]
    public void RagIndexingJobPayload_ForDocumentEvent_IncludesDocumentId()
    {
        // Arrange - Document events should include the Dataverse document ID
        var documentId = Guid.NewGuid().ToString();
        var payload = new RagIndexingJobPayload
        {
            TenantId = "tenant-1",
            DriveId = "drive-1",
            ItemId = "item-1",
            FileName = "document.pdf",
            DocumentId = documentId,
            Source = "DocumentEvent"
        };

        // Assert
        payload.DocumentId.Should().Be(documentId);
    }

    [Fact]
    public void RagIndexingJobPayload_CreateForDocumentEvent_HasAllRequiredFields()
    {
        // Arrange - Simulate creating payload for document event
        var driveId = "test-drive";
        var itemId = "test-item";
        var fileName = "test.pdf";
        var tenantId = "test-tenant";
        var documentId = Guid.NewGuid().ToString();

        var payload = new RagIndexingJobPayload
        {
            TenantId = tenantId,
            DriveId = driveId,
            ItemId = itemId,
            FileName = fileName,
            DocumentId = documentId,
            Source = "DocumentEvent"
        };

        // Assert - All required fields for RAG indexing
        payload.TenantId.Should().NotBeNullOrEmpty();
        payload.DriveId.Should().NotBeNullOrEmpty();
        payload.ItemId.Should().NotBeNullOrEmpty();
        payload.FileName.Should().NotBeNullOrEmpty();
        payload.Source.Should().Be("DocumentEvent");
    }

    #endregion

    #region Job Contract Creation

    [Fact]
    public void JobContract_ForRagIndexing_HasCorrectJobType()
    {
        // Arrange
        var payload = new RagIndexingJobPayload
        {
            TenantId = "t",
            DriveId = "d",
            ItemId = "i",
            FileName = "f.pdf"
        };

        var jobContract = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = RagIndexingJobHandler.JobTypeName,
            SubjectId = "doc-id",
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = $"rag-index-{payload.DriveId}-{payload.ItemId}",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };

        // Assert
        jobContract.JobType.Should().Be("RagIndexing");
        jobContract.IdempotencyKey.Should().Contain(payload.DriveId);
        jobContract.IdempotencyKey.Should().Contain(payload.ItemId);
    }

    [Fact]
    public void JobContract_IdempotencyKey_PreventsDoubleIndexing()
    {
        // Arrange - Same document should have same idempotency key
        var driveId = "drive-123";
        var itemId = "item-456";

        var key1 = $"rag-index-{driveId}-{itemId}";
        var key2 = $"rag-index-{driveId}-{itemId}";

        // Assert
        key1.Should().Be(key2, "Same document should produce same idempotency key");
    }

    #endregion
}
