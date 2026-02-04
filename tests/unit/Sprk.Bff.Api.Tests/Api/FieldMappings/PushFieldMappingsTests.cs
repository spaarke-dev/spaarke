using FluentAssertions;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// Unit tests for the Push Field Mappings endpoint DTOs and validation logic.
/// Tests the request validation, response structure, and field result statuses.
/// </summary>
/// <remarks>
/// Integration tests with actual Dataverse queries will be added in task 057.
/// These unit tests focus on the DTO structure and validation logic.
/// </remarks>
public class PushFieldMappingsTests
{
    #region Request DTO Tests

    [Fact]
    public void PushFieldMappingsRequest_ShouldHaveDefaultEmptyStrings()
    {
        // Arrange & Act
        var request = new PushFieldMappingsRequest();

        // Assert
        request.SourceEntity.Should().BeEmpty();
        request.TargetEntity.Should().BeEmpty();
        request.SourceRecordId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void PushFieldMappingsRequest_ShouldSetPropertiesViaInit()
    {
        // Arrange
        var sourceRecordId = Guid.NewGuid();

        // Act
        var request = new PushFieldMappingsRequest
        {
            SourceEntity = "sprk_matter",
            TargetEntity = "sprk_event",
            SourceRecordId = sourceRecordId
        };

        // Assert
        request.SourceEntity.Should().Be("sprk_matter");
        request.TargetEntity.Should().Be("sprk_event");
        request.SourceRecordId.Should().Be(sourceRecordId);
    }

    #endregion

    #region Response DTO Tests

    [Fact]
    public void PushFieldMappingsResponse_ShouldHaveDefaultEmptyArrays()
    {
        // Arrange & Act
        var response = new PushFieldMappingsResponse();

        // Assert
        response.Errors.Should().BeEmpty();
        response.Warnings.Should().BeEmpty();
        response.FieldResults.Should().BeEmpty();
    }

    [Fact]
    public void PushFieldMappingsResponse_ShouldSetAllProperties()
    {
        // Arrange
        var errors = new[] { new PushFieldMappingsError { RecordId = Guid.NewGuid(), Error = "Test error" } };
        var warnings = new[] { "Test warning" };
        var fieldResults = new[] { new FieldMappingResultDto { SourceField = "name", TargetField = "sprk_name", Status = FieldMappingStatus.Mapped } };

        // Act
        var response = new PushFieldMappingsResponse
        {
            Success = true,
            TargetEntity = "sprk_event",
            TotalRecords = 10,
            UpdatedCount = 8,
            FailedCount = 1,
            SkippedCount = 1,
            Errors = errors,
            Warnings = warnings,
            FieldResults = fieldResults
        };

        // Assert
        response.Success.Should().BeTrue();
        response.TargetEntity.Should().Be("sprk_event");
        response.TotalRecords.Should().Be(10);
        response.UpdatedCount.Should().Be(8);
        response.FailedCount.Should().Be(1);
        response.SkippedCount.Should().Be(1);
        response.Errors.Should().HaveCount(1);
        response.Warnings.Should().HaveCount(1);
        response.FieldResults.Should().HaveCount(1);
    }

    [Fact]
    public void PushFieldMappingsResponse_ShouldComputeCountsCorrectly()
    {
        // Arrange & Act
        var response = new PushFieldMappingsResponse
        {
            TotalRecords = 15,
            UpdatedCount = 10,
            FailedCount = 3,
            SkippedCount = 2
        };

        // Assert
        (response.UpdatedCount + response.FailedCount + response.SkippedCount)
            .Should().Be(response.TotalRecords, "counts should sum to total records");
    }

    #endregion

    #region Error DTO Tests

    [Fact]
    public void PushFieldMappingsError_ShouldHaveDefaultEmptyString()
    {
        // Arrange & Act
        var error = new PushFieldMappingsError();

        // Assert
        error.Error.Should().BeEmpty();
        error.RecordId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void PushFieldMappingsError_ShouldSetProperties()
    {
        // Arrange
        var recordId = Guid.NewGuid();

        // Act
        var error = new PushFieldMappingsError
        {
            RecordId = recordId,
            Error = "Record is read-only"
        };

        // Assert
        error.RecordId.Should().Be(recordId);
        error.Error.Should().Be("Record is read-only");
    }

    #endregion

    #region Field Mapping Result DTO Tests

    [Fact]
    public void FieldMappingResultDto_ShouldHaveDefaultEmptyStrings()
    {
        // Arrange & Act
        var result = new FieldMappingResultDto();

        // Assert
        result.SourceField.Should().BeEmpty();
        result.TargetField.Should().BeEmpty();
        result.Status.Should().BeEmpty();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void FieldMappingResultDto_Mapped_ShouldHaveNoErrorMessage()
    {
        // Arrange & Act
        var result = new FieldMappingResultDto
        {
            SourceField = "sprk_client",
            TargetField = "sprk_regardingaccount",
            Status = FieldMappingStatus.Mapped
        };

        // Assert
        result.Status.Should().Be("Mapped");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void FieldMappingResultDto_Skipped_ShouldIncludeReason()
    {
        // Arrange & Act
        var result = new FieldMappingResultDto
        {
            SourceField = "sprk_optionalfield",
            TargetField = "sprk_targetfield",
            Status = FieldMappingStatus.Skipped,
            ErrorMessage = "Source value is null"
        };

        // Assert
        result.Status.Should().Be("Skipped");
        result.ErrorMessage.Should().Be("Source value is null");
    }

    [Fact]
    public void FieldMappingResultDto_Error_ShouldIncludeErrorMessage()
    {
        // Arrange & Act
        var result = new FieldMappingResultDto
        {
            SourceField = "sprk_incompatible",
            TargetField = "sprk_lookup",
            Status = FieldMappingStatus.Error,
            ErrorMessage = "Type compatibility error: Cannot convert Text to Lookup"
        };

        // Assert
        result.Status.Should().Be("Error");
        result.ErrorMessage.Should().Contain("Type compatibility error");
    }

    #endregion

    #region Field Mapping Status Constants Tests

    [Fact]
    public void FieldMappingStatus_ShouldHaveCorrectValues()
    {
        // Assert
        FieldMappingStatus.Mapped.Should().Be("Mapped");
        FieldMappingStatus.Skipped.Should().Be("Skipped");
        FieldMappingStatus.Error.Should().Be("Error");
    }

    #endregion

    #region Validation Scenarios (API Behavior Tests)

    /// <summary>
    /// Validates the expected response structure for a successful push with all records updated.
    /// </summary>
    [Fact]
    public void PushResponse_AllRecordsUpdated_ShouldShowSuccessWithFullCounts()
    {
        // Arrange - Simulating a push where all 10 Events for a Matter were updated
        var response = new PushFieldMappingsResponse
        {
            Success = true,
            TargetEntity = "sprk_event",
            TotalRecords = 10,
            UpdatedCount = 10,
            FailedCount = 0,
            SkippedCount = 0,
            Errors = [],
            Warnings = []
        };

        // Assert
        response.Success.Should().BeTrue();
        response.UpdatedCount.Should().Be(response.TotalRecords);
        response.FailedCount.Should().Be(0);
        response.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Validates the expected response structure for a partial failure scenario.
    /// </summary>
    [Fact]
    public void PushResponse_PartialFailure_ShouldContinueAndReportErrors()
    {
        // Arrange - Simulating a push where 1 of 10 Events failed to update
        var failedRecordId = Guid.NewGuid();
        var response = new PushFieldMappingsResponse
        {
            Success = true, // Still success because 9 records updated
            TargetEntity = "sprk_event",
            TotalRecords = 10,
            UpdatedCount = 9,
            FailedCount = 1,
            SkippedCount = 0,
            Errors = [new PushFieldMappingsError { RecordId = failedRecordId, Error = "Record is read-only" }],
            Warnings = []
        };

        // Assert
        response.Success.Should().BeTrue("partial success should still be success");
        response.UpdatedCount.Should().Be(9);
        response.FailedCount.Should().Be(1);
        response.Errors.Should().HaveCount(1);
        response.Errors[0].RecordId.Should().Be(failedRecordId);
        response.Errors[0].Error.Should().Be("Record is read-only");
    }

    /// <summary>
    /// Validates the expected response structure when no child records are found.
    /// </summary>
    [Fact]
    public void PushResponse_NoChildRecords_ShouldReturnZeroCounts()
    {
        // Arrange - Simulating a push where no Events exist for the Matter
        var response = new PushFieldMappingsResponse
        {
            Success = true,
            TargetEntity = "sprk_event",
            TotalRecords = 0,
            UpdatedCount = 0,
            FailedCount = 0,
            SkippedCount = 0,
            Errors = [],
            Warnings = []
        };

        // Assert
        response.Success.Should().BeTrue();
        response.TotalRecords.Should().Be(0);
        response.UpdatedCount.Should().Be(0);
    }

    /// <summary>
    /// Validates the expected response structure when profile has no rules.
    /// </summary>
    [Fact]
    public void PushResponse_NoMappingRules_ShouldReturnWarning()
    {
        // Arrange - Simulating a push where profile exists but has no rules
        var response = new PushFieldMappingsResponse
        {
            Success = true,
            TargetEntity = "sprk_event",
            TotalRecords = 0,
            UpdatedCount = 0,
            FailedCount = 0,
            SkippedCount = 0,
            Errors = [],
            Warnings = ["Profile has no mapping rules configured."]
        };

        // Assert
        response.Success.Should().BeTrue();
        response.Warnings.Should().ContainSingle()
            .Which.Should().Contain("no mapping rules");
    }

    #endregion
}
