using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Model tests for enqueue request/response types.
/// </summary>
public class EnqueueAnalysisModelsTests
{
    [Fact]
    public void EnqueueAnalysisResponse_PropertiesAreSet()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        // Act
        var response = new EnqueueAnalysisResponse(jobId, documentId);

        // Assert
        response.JobId.Should().Be(jobId);
        response.DocumentId.Should().Be(documentId);
    }

    [Fact]
    public void BatchAnalysisRequest_PropertiesAreSet()
    {
        // Arrange
        var documents = new List<DocumentAnalysisRequest>
        {
            new(Guid.NewGuid(), "drive-1", "item-1"),
            new(Guid.NewGuid(), "drive-2", "item-2")
        };

        // Act
        var request = new BatchAnalysisRequest(documents);

        // Assert
        request.Documents.Should().HaveCount(2);
        request.Documents[0].DriveId.Should().Be("drive-1");
        request.Documents[1].DriveId.Should().Be("drive-2");
    }

    [Fact]
    public void BatchAnalysisResponse_PropertiesAreSet()
    {
        // Arrange
        var jobs = new List<EnqueueAnalysisResponse>
        {
            new(Guid.NewGuid(), Guid.NewGuid()),
            new(Guid.NewGuid(), Guid.NewGuid())
        };

        // Act
        var response = new BatchAnalysisResponse(jobs, jobs.Count);

        // Assert
        response.Jobs.Should().HaveCount(2);
        response.TotalEnqueued.Should().Be(2);
    }
}
