using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Jobs;

/// <summary>
/// Unit tests for RAG indexing integration with EmailToDocumentJobHandler.
/// Tests configuration parsing and job payload serialization.
///
/// NOTE: Full handler integration tests are not possible without mocking concrete
/// classes (SpeFileStore, AttachmentFilterService, etc.) which don't have interfaces.
/// RAG indexing behavior is tested via RagIndexingJobHandlerTests.
/// </summary>
public class EmailToDocumentJobHandlerRagTests
{
    #region Configuration Tests

    [Fact]
    public void EmailProcessingOptions_AutoIndexToRag_DefaultsFalse()
    {
        // Arrange & Act
        var options = new EmailProcessingOptions();

        // Assert - AutoIndexToRag should default to false for safety
        options.AutoIndexToRag.Should().BeFalse("RAG indexing should be opt-in");
    }

    [Fact]
    public void EmailProcessingOptions_AutoIndexToRag_CanBeEnabled()
    {
        // Arrange & Act
        var options = new EmailProcessingOptions { AutoIndexToRag = true };

        // Assert
        options.AutoIndexToRag.Should().BeTrue();
    }

    [Fact]
    public void EmailProcessingOptions_WrappedInIOptions_WorksCorrectly()
    {
        // Arrange
        var options = Options.Create(new EmailProcessingOptions
        {
            AutoEnqueueAi = true,
            AutoIndexToRag = true,
            DefaultContainerId = "test-container"
        });

        // Act
        var value = options.Value;

        // Assert
        value.AutoEnqueueAi.Should().BeTrue();
        value.AutoIndexToRag.Should().BeTrue();
        value.DefaultContainerId.Should().Be("test-container");
    }

    #endregion

    #region Payload Serialization Tests

    [Fact]
    public void RagIndexingJobPayload_Serialization_RoundTrips()
    {
        // Arrange
        var payload = new RagIndexingJobPayload
        {
            TenantId = "test-tenant",
            DriveId = "test-drive-id",
            ItemId = "test-item-id",
            FileName = "test-email.eml",
            DocumentId = "test-doc-guid",
            Source = "EmailProcessing",
            KnowledgeSourceId = "source-123"
        };

        // Act
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<RagIndexingJobPayload>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.TenantId.Should().Be("test-tenant");
        deserialized.DriveId.Should().Be("test-drive-id");
        deserialized.ItemId.Should().Be("test-item-id");
        deserialized.FileName.Should().Be("test-email.eml");
        deserialized.DocumentId.Should().Be("test-doc-guid");
        deserialized.Source.Should().Be("EmailProcessing");
        deserialized.KnowledgeSourceId.Should().Be("source-123");
    }

    [Fact]
    public void RagIndexingJobPayload_InJobContract_SerializesCorrectly()
    {
        // Arrange
        var payload = new RagIndexingJobPayload
        {
            TenantId = "tenant-1",
            DriveId = "drive-1",
            ItemId = "item-1",
            FileName = "email.eml",
            Source = "Email"
        };

        var jobContract = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = RagIndexingJobHandler.JobTypeName,
            SubjectId = "subject-1",
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = "test-key",
            Attempt = 1,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };

        // Act
        var extractedPayload = jobContract.Payload!.Deserialize<RagIndexingJobPayload>();

        // Assert
        extractedPayload.Should().NotBeNull();
        extractedPayload!.TenantId.Should().Be("tenant-1");
        extractedPayload.DriveId.Should().Be("drive-1");
        extractedPayload.FileName.Should().Be("email.eml");
    }

    [Fact]
    public void RagIndexingJobPayload_RequiredFields_AreSet()
    {
        // Arrange
        var payload = new RagIndexingJobPayload
        {
            TenantId = "t",
            DriveId = "d",
            ItemId = "i",
            FileName = "f.txt"
        };

        // Assert - Required fields should be populated
        payload.TenantId.Should().NotBeNullOrEmpty();
        payload.DriveId.Should().NotBeNullOrEmpty();
        payload.ItemId.Should().NotBeNullOrEmpty();
        payload.FileName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RagIndexingJobPayload_OptionalFields_CanBeNull()
    {
        // Arrange
        var payload = new RagIndexingJobPayload
        {
            TenantId = "t",
            DriveId = "d",
            ItemId = "i",
            FileName = "f.txt"
            // DocumentId and KnowledgeSourceId are optional
        };

        // Assert - Optional fields can be null/empty
        payload.DocumentId.Should().BeNullOrEmpty();
        payload.KnowledgeSourceId.Should().BeNullOrEmpty();
    }

    #endregion

    #region Job Type Tests

    [Fact]
    public void EmailToDocumentJobHandler_JobTypeName_IsCorrect()
    {
        // Assert
        EmailToDocumentJobHandler.JobTypeName.Should().Be("ProcessEmailToDocument");
    }

    [Fact]
    public void RagIndexingJobHandler_JobTypeName_IsCorrect()
    {
        // Assert - Verify the job type that email handler would submit for RAG
        RagIndexingJobHandler.JobTypeName.Should().Be("RagIndexing");
    }

    #endregion

    #region EmailToDocumentPayload Tests

    [Fact]
    public void EmailToDocumentPayload_Serialization_RoundTrips()
    {
        // Arrange
        var payload = new EmailToDocumentPayload
        {
            EmailId = Guid.NewGuid(),
            TriggerSource = "Webhook"
        };

        // Act
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<EmailToDocumentPayload>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.EmailId.Should().Be(payload.EmailId);
        deserialized.TriggerSource.Should().Be("Webhook");
    }

    #endregion
}
