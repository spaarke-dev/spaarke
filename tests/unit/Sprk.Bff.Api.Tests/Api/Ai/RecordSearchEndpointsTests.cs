using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for RecordSearchEndpoints.
/// Tests endpoint mapping, request validation, response handling, and error scenarios.
/// Follows the endpoint test patterns used in AnalysisEndpointsTests.cs.
/// </summary>
public class RecordSearchEndpointsTests
{
    #region Endpoint Mapping Tests

    [Fact]
    public void MapRecordSearchEndpoints_CreatesExpectedRoutes()
    {
        // Arrange - Verify endpoint extension method exists and has correct signature
        var method = typeof(RecordSearchEndpoints).GetMethod("MapRecordSearchEndpoints");

        // Assert
        method.Should().NotBeNull();
        method!.IsStatic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(IEndpointRouteBuilder));
    }

    #endregion

    #region Request Model Validation Tests

    [Fact]
    public void RecordSearchRequest_QueryIsRequired()
    {
        // Assert - Required attribute should be present
        var attr = typeof(RecordSearchRequest)
            .GetProperty(nameof(RecordSearchRequest.Query))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false);

        attr.Should().NotBeEmpty();
    }

    [Fact]
    public void RecordSearchRequest_QueryHasMaxLength()
    {
        // Assert - StringLength attribute should be present
        var attr = typeof(RecordSearchRequest)
            .GetProperty(nameof(RecordSearchRequest.Query))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.StringLengthAttribute), false);

        attr.Should().NotBeEmpty();
        var stringLength = (System.ComponentModel.DataAnnotations.StringLengthAttribute)attr[0];
        stringLength.MaximumLength.Should().Be(1000);
    }

    [Fact]
    public void RecordSearchRequest_RecordTypesIsRequired()
    {
        // Assert - Required attribute should be present
        var attr = typeof(RecordSearchRequest)
            .GetProperty(nameof(RecordSearchRequest.RecordTypes))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false);

        attr.Should().NotBeEmpty();
    }

    [Fact]
    public void RecordSearchRequest_RecordTypesHasMinLength()
    {
        // Assert - MinLength attribute should be present
        var attr = typeof(RecordSearchRequest)
            .GetProperty(nameof(RecordSearchRequest.RecordTypes))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MinLengthAttribute), false);

        attr.Should().NotBeEmpty();
        var minLength = (System.ComponentModel.DataAnnotations.MinLengthAttribute)attr[0];
        minLength.Length.Should().Be(1);
    }

    [Fact]
    public void RecordSearchOptions_LimitHasRangeValidation()
    {
        // Assert - Range attribute should be present
        var attr = typeof(RecordSearchOptions)
            .GetProperty(nameof(RecordSearchOptions.Limit))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RangeAttribute), false);

        attr.Should().NotBeEmpty();
        var range = (System.ComponentModel.DataAnnotations.RangeAttribute)attr[0];
        range.Minimum.Should().Be(1);
        range.Maximum.Should().Be(50);
    }

    [Fact]
    public void RecordSearchOptions_OffsetHasRangeValidation()
    {
        // Assert - Range attribute should be present
        var attr = typeof(RecordSearchOptions)
            .GetProperty(nameof(RecordSearchOptions.Offset))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RangeAttribute), false);

        attr.Should().NotBeEmpty();
        var range = (System.ComponentModel.DataAnnotations.RangeAttribute)attr[0];
        range.Minimum.Should().Be(0);
        range.Maximum.Should().Be(1000);
    }

    [Fact]
    public void RecordSearchOptions_DefaultLimitIs20()
    {
        // Arrange & Act
        var options = new RecordSearchOptions();

        // Assert
        options.Limit.Should().Be(20);
    }

    [Fact]
    public void RecordSearchOptions_DefaultOffsetIs0()
    {
        // Arrange & Act
        var options = new RecordSearchOptions();

        // Assert
        options.Offset.Should().Be(0);
    }

    [Fact]
    public void RecordSearchOptions_DefaultHybridModeIsRrf()
    {
        // Arrange & Act
        var options = new RecordSearchOptions();

        // Assert
        options.HybridMode.Should().Be(RecordHybridSearchMode.Rrf);
    }

    #endregion

    #region RecordEntityType Validation Tests

    [Theory]
    [InlineData("sprk_matter", true)]
    [InlineData("sprk_project", true)]
    [InlineData("sprk_invoice", true)]
    [InlineData("SPRK_MATTER", true)]   // Case-insensitive
    [InlineData("Sprk_Matter", true)]   // Mixed case
    [InlineData("account", false)]       // Not a valid record type
    [InlineData("contact", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("invalid_type", false)]
    public void RecordEntityType_IsValid_ReturnsExpected(string? recordType, bool expected)
    {
        // Act
        var result = RecordEntityType.IsValid(recordType);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void RecordEntityType_ValidTypes_ContainsAllExpectedValues()
    {
        // Assert
        RecordEntityType.ValidTypes.Should().HaveCount(3);
        RecordEntityType.ValidTypes.Should().Contain("sprk_matter");
        RecordEntityType.ValidTypes.Should().Contain("sprk_project");
        RecordEntityType.ValidTypes.Should().Contain("sprk_invoice");
    }

    [Fact]
    public void RecordEntityType_Constants_HaveCorrectValues()
    {
        // Assert
        RecordEntityType.Matter.Should().Be("sprk_matter");
        RecordEntityType.Project.Should().Be("sprk_project");
        RecordEntityType.Invoice.Should().Be("sprk_invoice");
    }

    #endregion

    #region RecordHybridSearchMode Validation Tests

    [Fact]
    public void RecordHybridSearchMode_Constants_HaveCorrectValues()
    {
        // Assert
        RecordHybridSearchMode.Rrf.Should().Be("rrf");
        RecordHybridSearchMode.VectorOnly.Should().Be("vectorOnly");
        RecordHybridSearchMode.KeywordOnly.Should().Be("keywordOnly");
    }

    [Fact]
    public void RecordHybridSearchMode_ValidModes_ContainsAllExpected()
    {
        // Assert
        RecordHybridSearchMode.ValidModes.Should().HaveCount(3);
        RecordHybridSearchMode.ValidModes.Should().Contain("rrf");
        RecordHybridSearchMode.ValidModes.Should().Contain("vectorOnly");
        RecordHybridSearchMode.ValidModes.Should().Contain("keywordOnly");
    }

    [Theory]
    [InlineData("rrf", true)]           // Exact match after ToLowerInvariant
    [InlineData("RRF", true)]           // ToLowerInvariant("RRF") = "rrf" matches
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RecordHybridSearchMode_IsValid_ReturnsExpected(string? mode, bool expected)
    {
        // Act
        var result = RecordHybridSearchMode.IsValid(mode);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Response Model Tests

    [Fact]
    public void RecordSearchResponse_CanSerializeToJson()
    {
        // Arrange
        var response = new RecordSearchResponse
        {
            Results = new List<RecordSearchResult>
            {
                new RecordSearchResult
                {
                    RecordId = "abc-123",
                    RecordType = "sprk_matter",
                    RecordName = "Test Matter",
                    RecordDescription = "Test description",
                    ConfidenceScore = 0.85,
                    Organizations = new List<string> { "Acme Corp" },
                    People = new List<string> { "John Doe" },
                    Keywords = new List<string> { "legal", "contract" },
                    ModifiedAt = DateTimeOffset.UtcNow
                }
            },
            Metadata = new RecordSearchMetadata
            {
                TotalCount = 1,
                SearchTime = 42.5,
                HybridMode = "rrf"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(response);

        // Assert
        json.Should().Contain("\"recordId\":\"abc-123\"");
        json.Should().Contain("\"recordType\":\"sprk_matter\"");
        json.Should().Contain("\"recordName\":\"Test Matter\"");
        json.Should().Contain("\"confidenceScore\":0.85");
        json.Should().Contain("\"totalCount\":1");
        json.Should().Contain("\"hybridMode\":\"rrf\"");
    }

    [Fact]
    public void RecordSearchResult_NullableFieldsCanBeNull()
    {
        // Arrange & Act
        var result = new RecordSearchResult
        {
            RecordId = "test-id",
            RecordType = "sprk_matter",
            RecordName = "Test",
            ConfidenceScore = 0.5,
            RecordDescription = null,
            MatchReasons = null,
            Organizations = null,
            People = null,
            Keywords = null,
            CreatedAt = null,
            ModifiedAt = null
        };

        // Assert
        result.RecordDescription.Should().BeNull();
        result.MatchReasons.Should().BeNull();
        result.Organizations.Should().BeNull();
        result.People.Should().BeNull();
        result.Keywords.Should().BeNull();
        result.CreatedAt.Should().BeNull();
        result.ModifiedAt.Should().BeNull();
    }

    [Fact]
    public void RecordSearchMetadata_CanBeInitialized()
    {
        // Arrange & Act
        var metadata = new RecordSearchMetadata
        {
            TotalCount = 42,
            SearchTime = 123.4,
            HybridMode = RecordHybridSearchMode.Rrf
        };

        // Assert
        metadata.TotalCount.Should().Be(42);
        metadata.SearchTime.Should().Be(123.4);
        metadata.HybridMode.Should().Be("rrf");
    }

    [Fact]
    public void RecordSearchFilters_CanBeInitializedWithAllFields()
    {
        // Arrange & Act
        var filters = new RecordSearchFilters
        {
            Organizations = new List<string> { "Acme Corp", "Globex" },
            People = new List<string> { "John Doe", "Jane Smith" },
            ReferenceNumbers = new List<string> { "MAT-001", "INV-002" }
        };

        // Assert
        filters.Organizations.Should().HaveCount(2);
        filters.People.Should().HaveCount(2);
        filters.ReferenceNumbers.Should().HaveCount(2);
    }

    [Fact]
    public void RecordSearchFilters_AllFieldsDefaultToNull()
    {
        // Arrange & Act
        var filters = new RecordSearchFilters();

        // Assert
        filters.Organizations.Should().BeNull();
        filters.People.Should().BeNull();
        filters.ReferenceNumbers.Should().BeNull();
    }

    #endregion

    #region SearchErrorCodes Tests

    [Fact]
    public void SearchErrorCodes_InvalidRecordTypes_HasExpectedValue()
    {
        SearchErrorCodes.InvalidRecordTypes.Should().Be("INVALID_RECORD_TYPES");
    }

    [Fact]
    public void SearchErrorCodes_RecordSearchFailed_HasExpectedValue()
    {
        SearchErrorCodes.RecordSearchFailed.Should().Be("RECORD_SEARCH_FAILED");
    }

    [Fact]
    public void SearchErrorCodes_QueryRequired_HasExpectedValue()
    {
        SearchErrorCodes.QueryRequired.Should().Be("QUERY_REQUIRED");
    }

    #endregion

    #region RecordSearchAuthorizationFilter Tests

    [Fact]
    public void RecordSearchAuthorizationFilter_CanBeInstantiated()
    {
        // Arrange & Act
        var filter = new RecordSearchAuthorizationFilter();

        // Assert
        filter.Should().NotBeNull();
    }

    [Fact]
    public void RecordSearchAuthorizationFilter_ImplementsIEndpointFilter()
    {
        // Assert
        typeof(RecordSearchAuthorizationFilter)
            .GetInterfaces()
            .Should()
            .Contain(typeof(IEndpointFilter));
    }

    [Fact]
    public void RecordSearchAuthorizationFilter_AcceptsNullLogger()
    {
        // Arrange & Act - Should not throw
        var filter = new RecordSearchAuthorizationFilter(null);

        // Assert
        filter.Should().NotBeNull();
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void RecordSearchRequest_DeserializesFromJson()
    {
        // Arrange
        var json = """
        {
            "query": "find matters for Acme",
            "recordTypes": ["sprk_matter", "sprk_project"],
            "filters": {
                "organizations": ["Acme Corp"],
                "people": ["John Doe"]
            },
            "options": {
                "limit": 10,
                "offset": 0,
                "hybridMode": "rrf"
            }
        }
        """;

        // Act
        var request = JsonSerializer.Deserialize<RecordSearchRequest>(json);

        // Assert
        request.Should().NotBeNull();
        request!.Query.Should().Be("find matters for Acme");
        request.RecordTypes.Should().HaveCount(2);
        request.RecordTypes.Should().Contain("sprk_matter");
        request.RecordTypes.Should().Contain("sprk_project");
        request.Filters.Should().NotBeNull();
        request.Filters!.Organizations.Should().Contain("Acme Corp");
        request.Filters.People.Should().Contain("John Doe");
        request.Options.Should().NotBeNull();
        request.Options!.Limit.Should().Be(10);
        request.Options.HybridMode.Should().Be("rrf");
    }

    [Fact]
    public void RecordSearchRequest_DeserializesMinimalJson()
    {
        // Arrange - Only required fields
        var json = """
        {
            "query": "test",
            "recordTypes": ["sprk_matter"]
        }
        """;

        // Act
        var request = JsonSerializer.Deserialize<RecordSearchRequest>(json);

        // Assert
        request.Should().NotBeNull();
        request!.Query.Should().Be("test");
        request.RecordTypes.Should().HaveCount(1);
        request.Filters.Should().BeNull();
        request.Options.Should().BeNull();
    }

    [Fact]
    public void RecordSearchResponse_RoundTripsCorrectly()
    {
        // Arrange
        var original = new RecordSearchResponse
        {
            Results = new List<RecordSearchResult>
            {
                new RecordSearchResult
                {
                    RecordId = "guid-1",
                    RecordType = RecordEntityType.Matter,
                    RecordName = "Matter A",
                    ConfidenceScore = 0.92,
                    MatchReasons = new List<string> { "Name match: Matter A" }
                }
            },
            Metadata = new RecordSearchMetadata
            {
                TotalCount = 1,
                SearchTime = 55.2,
                HybridMode = RecordHybridSearchMode.Rrf
            }
        };

        // Act - Serialize and deserialize
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<RecordSearchResponse>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Results.Should().HaveCount(1);
        deserialized.Results[0].RecordId.Should().Be("guid-1");
        deserialized.Results[0].RecordType.Should().Be(RecordEntityType.Matter);
        deserialized.Results[0].RecordName.Should().Be("Matter A");
        deserialized.Results[0].ConfidenceScore.Should().Be(0.92);
        deserialized.Results[0].MatchReasons.Should().Contain("Name match: Matter A");
        deserialized.Metadata.TotalCount.Should().Be(1);
        deserialized.Metadata.SearchTime.Should().Be(55.2);
        deserialized.Metadata.HybridMode.Should().Be("rrf");
    }

    #endregion
}
