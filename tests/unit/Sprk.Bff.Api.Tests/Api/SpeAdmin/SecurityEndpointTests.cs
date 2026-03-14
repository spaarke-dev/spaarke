using FluentAssertions;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for security endpoints DTO contracts and domain model mapping.
///
/// Strategy: Tests validate DTO structure, mapping logic, and Graph service domain model
/// record properties. Graph SDK classes are sealed and cannot be mocked — integration-level
/// Graph behavior is validated through the endpoint handler structure and the
/// SpeAdminGraphService domain model mapping (no live Graph calls in unit tests).
///
/// SPE-060: Security alerts (GET /api/spe/security/alerts) and
///          secure score (GET /api/spe/security/score) endpoints.
/// </summary>
public class SecurityEndpointTests
{
    // =========================================================================
    // SecurityAlertDto Tests
    // =========================================================================

    [Fact]
    public void SecurityAlertDto_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var dto = new SecurityAlertDto();

        // Assert
        dto.Id.Should().Be(string.Empty);
        dto.Title.Should().BeNull();
        dto.Severity.Should().BeNull();
        dto.Status.Should().BeNull();
        dto.CreatedDateTime.Should().BeNull();
        dto.Description.Should().BeNull();
    }

    [Fact]
    public void SecurityAlertDto_WithAllValues_RoundTrips()
    {
        // Arrange
        var id = "alert-abc-001";
        var title = "Suspicious sign-in activity";
        var severity = "high";
        var status = "newAlert";
        var createdAt = new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);
        var description = "Multiple failed sign-in attempts detected from unusual location.";

        // Act
        var dto = new SecurityAlertDto
        {
            Id = id,
            Title = title,
            Severity = severity,
            Status = status,
            CreatedDateTime = createdAt,
            Description = description
        };

        // Assert
        dto.Id.Should().Be(id);
        dto.Title.Should().Be(title);
        dto.Severity.Should().Be(severity);
        dto.Status.Should().Be(status);
        dto.CreatedDateTime.Should().Be(createdAt);
        dto.Description.Should().Be(description);
    }

    [Theory]
    [InlineData("informational")]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("unknownFutureValue")]
    public void SecurityAlertDto_Severity_AcceptsExpectedValues(string severity)
    {
        // Arrange & Act
        var dto = new SecurityAlertDto { Severity = severity };

        // Assert
        dto.Severity.Should().Be(severity);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("newAlert")]
    [InlineData("inProgress")]
    [InlineData("resolved")]
    [InlineData("dismissed")]
    [InlineData("unknownFutureValue")]
    public void SecurityAlertDto_Status_AcceptsExpectedValues(string status)
    {
        // Arrange & Act
        var dto = new SecurityAlertDto { Status = status };

        // Assert
        dto.Status.Should().Be(status);
    }

    // =========================================================================
    // SecureScoreDto Tests
    // =========================================================================

    [Fact]
    public void SecureScoreDto_DefaultValues_AreNull()
    {
        // Arrange & Act
        var dto = new SecureScoreDto();

        // Assert
        dto.CurrentScore.Should().BeNull();
        dto.MaxScore.Should().BeNull();
        dto.AverageComparativeScores.Should().BeNull();
    }

    [Fact]
    public void SecureScoreDto_WithScores_RoundTrips()
    {
        // Arrange
        var currentScore = 87.5;
        var maxScore = 200.0;

        // Act
        var dto = new SecureScoreDto
        {
            CurrentScore = currentScore,
            MaxScore = maxScore
        };

        // Assert
        dto.CurrentScore.Should().Be(currentScore);
        dto.MaxScore.Should().Be(maxScore);
    }

    [Fact]
    public void SecureScoreDto_WithAverageComparativeScores_RoundTrips()
    {
        // Arrange
        var comparisons = new List<AverageComparativeScoreDto>
        {
            new() { Basis = "AllTenants", AverageScore = 52.3 },
            new() { Basis = "TotalSeats", AverageScore = 64.7 }
        };

        // Act
        var dto = new SecureScoreDto
        {
            CurrentScore = 87.5,
            MaxScore = 200.0,
            AverageComparativeScores = comparisons
        };

        // Assert
        dto.AverageComparativeScores.Should().HaveCount(2);
        dto.AverageComparativeScores![0].Basis.Should().Be("AllTenants");
        dto.AverageComparativeScores![0].AverageScore.Should().Be(52.3);
        dto.AverageComparativeScores![1].Basis.Should().Be("TotalSeats");
        dto.AverageComparativeScores![1].AverageScore.Should().Be(64.7);
    }

    [Fact]
    public void SecureScoreDto_EmptyComparativeScores_IsAllowed()
    {
        // Arrange & Act
        var dto = new SecureScoreDto
        {
            CurrentScore = 50.0,
            MaxScore = 100.0,
            AverageComparativeScores = []
        };

        // Assert
        dto.AverageComparativeScores.Should().NotBeNull();
        dto.AverageComparativeScores.Should().BeEmpty();
    }

    // =========================================================================
    // AverageComparativeScoreDto Tests
    // =========================================================================

    [Fact]
    public void AverageComparativeScoreDto_DefaultValues_AreNull()
    {
        // Arrange & Act
        var dto = new AverageComparativeScoreDto();

        // Assert
        dto.Basis.Should().BeNull();
        dto.AverageScore.Should().BeNull();
    }

    [Fact]
    public void AverageComparativeScoreDto_WithValues_RoundTrips()
    {
        // Arrange
        var basis = "AllTenants";
        var avgScore = 52.3;

        // Act
        var dto = new AverageComparativeScoreDto { Basis = basis, AverageScore = avgScore };

        // Assert
        dto.Basis.Should().Be(basis);
        dto.AverageScore.Should().Be(avgScore);
    }

    // =========================================================================
    // SpeAdminGraphService.SecurityAlertResult Domain Model Tests
    // =========================================================================

    [Fact]
    public void SecurityAlertResult_ConstructedFromRecord_PropertiesAreReadable()
    {
        // Arrange
        var id = "graph-alert-001";
        var title = "Malware detected";
        var severity = "High";
        var status = "NewAlert";
        var createdAt = DateTimeOffset.UtcNow;
        var description = "Malware detected on endpoint.";

        // Act
        var result = new SpeAdminGraphService.SecurityAlertResult(
            Id: id,
            Title: title,
            Severity: severity,
            Status: status,
            CreatedDateTime: createdAt,
            Description: description);

        // Assert
        result.Id.Should().Be(id);
        result.Title.Should().Be(title);
        result.Severity.Should().Be(severity);
        result.Status.Should().Be(status);
        result.CreatedDateTime.Should().Be(createdAt);
        result.Description.Should().Be(description);
    }

    [Fact]
    public void SecurityAlertResult_WithNullOptionalFields_IsValid()
    {
        // Arrange & Act
        var result = new SpeAdminGraphService.SecurityAlertResult(
            Id: "alert-001",
            Title: null,
            Severity: null,
            Status: null,
            CreatedDateTime: null,
            Description: null);

        // Assert — null optional fields are valid (Graph may not return all fields)
        result.Id.Should().Be("alert-001");
        result.Title.Should().BeNull();
        result.Severity.Should().BeNull();
        result.Status.Should().BeNull();
        result.CreatedDateTime.Should().BeNull();
        result.Description.Should().BeNull();
    }

    // =========================================================================
    // SpeAdminGraphService.SecureScoreResult Domain Model Tests
    // =========================================================================

    [Fact]
    public void SecureScoreResult_ConstructedFromRecord_PropertiesAreReadable()
    {
        // Arrange
        var comparisons = new List<SpeAdminGraphService.AverageComparativeScore>
        {
            new(Basis: "AllTenants", AverageScore: 52.3)
        };

        // Act
        var result = new SpeAdminGraphService.SecureScoreResult(
            CurrentScore: 87.5,
            MaxScore: 200.0,
            AverageComparativeScores: comparisons);

        // Assert
        result.CurrentScore.Should().Be(87.5);
        result.MaxScore.Should().Be(200.0);
        result.AverageComparativeScores.Should().HaveCount(1);
        result.AverageComparativeScores![0].Basis.Should().Be("AllTenants");
        result.AverageComparativeScores![0].AverageScore.Should().Be(52.3);
    }

    [Fact]
    public void SecureScoreResult_WithNullComparisons_IsValid()
    {
        // Arrange & Act — Graph may not return comparative scores for all tenants
        var result = new SpeAdminGraphService.SecureScoreResult(
            CurrentScore: 75.0,
            MaxScore: 150.0,
            AverageComparativeScores: null);

        // Assert
        result.CurrentScore.Should().Be(75.0);
        result.MaxScore.Should().Be(150.0);
        result.AverageComparativeScores.Should().BeNull();
    }

    [Fact]
    public void SecureScoreResult_EmptyComparisons_IsDistinctFromNull()
    {
        // Arrange & Act
        var resultWithEmpty = new SpeAdminGraphService.SecureScoreResult(
            CurrentScore: 75.0,
            MaxScore: 150.0,
            AverageComparativeScores: []);

        var resultWithNull = new SpeAdminGraphService.SecureScoreResult(
            CurrentScore: 75.0,
            MaxScore: 150.0,
            AverageComparativeScores: null);

        // Assert
        resultWithEmpty.AverageComparativeScores.Should().NotBeNull();
        resultWithEmpty.AverageComparativeScores.Should().BeEmpty();
        resultWithNull.AverageComparativeScores.Should().BeNull();
    }

    // =========================================================================
    // SpeAdminGraphService.AverageComparativeScore Domain Model Tests
    // =========================================================================

    [Fact]
    public void AverageComparativeScore_ConstructedFromRecord_PropertiesAreReadable()
    {
        // Arrange & Act
        var score = new SpeAdminGraphService.AverageComparativeScore(
            Basis: "TotalSeats",
            AverageScore: 64.7);

        // Assert
        score.Basis.Should().Be("TotalSeats");
        score.AverageScore.Should().Be(64.7);
    }

    [Fact]
    public void AverageComparativeScore_WithNullValues_IsValid()
    {
        // Arrange & Act
        var score = new SpeAdminGraphService.AverageComparativeScore(
            Basis: null,
            AverageScore: null);

        // Assert
        score.Basis.Should().BeNull();
        score.AverageScore.Should().BeNull();
    }

    // =========================================================================
    // SecurityEndpoints Registration Tests
    // =========================================================================

    [Fact]
    public void SecurityEndpoints_IsStaticClass_WithCorrectMapMethod()
    {
        // Arrange
        var endpointsType = typeof(SecurityEndpoints);

        // Assert — validates that the endpoints class follows Minimal API pattern (ADR-001)
        endpointsType.IsAbstract.Should().BeTrue("static classes are abstract in reflection");
        endpointsType.IsSealed.Should().BeTrue("static classes are sealed in reflection");

        var mapMethod = endpointsType.GetMethod("MapSecurityEndpoints");
        mapMethod.Should().NotBeNull("MapSecurityEndpoints extension method must exist");
        mapMethod!.IsPublic.Should().BeTrue();
        mapMethod.IsStatic.Should().BeTrue();
    }

    // =========================================================================
    // Empty alerts scenario
    // =========================================================================

    [Fact]
    public void SecurityAlertsResponse_EmptyAlerts_ReturnsEmptyList()
    {
        // Arrange & Act — simulates the endpoint handler mapping an empty alerts list
        var emptyAlerts = new List<SpeAdminGraphService.SecurityAlertResult>();

        var dtos = emptyAlerts.Select(a => new SecurityAlertDto
        {
            Id = a.Id,
            Title = a.Title,
            Severity = a.Severity,
            Status = a.Status,
            CreatedDateTime = a.CreatedDateTime,
            Description = a.Description
        }).ToList();

        var response = new SecurityEndpoints.SecurityAlertsResponse(dtos, dtos.Count);

        // Assert — criteria: empty alerts list returns 200 with empty array
        response.Items.Should().NotBeNull();
        response.Items.Should().BeEmpty();
        response.Count.Should().Be(0);
    }

    [Fact]
    public void SecurityAlertsResponse_WithAlerts_MapsAllFields()
    {
        // Arrange
        var createdAt = new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);
        var alertResults = new List<SpeAdminGraphService.SecurityAlertResult>
        {
            new(
                Id: "alert-001",
                Title: "Suspicious activity",
                Severity: "High",
                Status: "NewAlert",
                CreatedDateTime: createdAt,
                Description: "Suspicious login from unknown IP"),
            new(
                Id: "alert-002",
                Title: "Data exfiltration attempt",
                Severity: "Medium",
                Status: "InProgress",
                CreatedDateTime: createdAt.AddHours(-1),
                Description: "Large data transfer detected")
        };

        // Act — simulate the mapping performed in GetSecurityAlertsAsync handler
        var dtos = alertResults.Select(a => new SecurityAlertDto
        {
            Id = a.Id,
            Title = a.Title,
            Severity = a.Severity,
            Status = a.Status,
            CreatedDateTime = a.CreatedDateTime,
            Description = a.Description
        }).ToList();

        var response = new SecurityEndpoints.SecurityAlertsResponse(dtos, dtos.Count);

        // Assert
        response.Count.Should().Be(2);
        response.Items.Should().HaveCount(2);
        response.Items[0].Id.Should().Be("alert-001");
        response.Items[0].Title.Should().Be("Suspicious activity");
        response.Items[0].Severity.Should().Be("High");
        response.Items[1].Id.Should().Be("alert-002");
        response.Items[1].Status.Should().Be("InProgress");
    }
}
