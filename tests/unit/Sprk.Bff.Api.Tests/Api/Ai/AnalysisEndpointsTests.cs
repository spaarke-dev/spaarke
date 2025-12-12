using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for Analysis endpoints - contract and behavior validation.
/// These tests validate endpoint behavior without full app startup.
/// Full integration tests will be implemented in the integration test project
/// when test infrastructure (Service Bus, etc.) is available.
/// </summary>
public class AnalysisEndpointsTests
{
    #region Endpoint Mapping Tests

    [Fact]
    public void MapAnalysisEndpoints_CreatesExpectedRoutes()
    {
        // Arrange - Verify endpoint extension method exists and has correct signature
        var method = typeof(AnalysisEndpoints).GetMethod("MapAnalysisEndpoints");

        // Assert
        method.Should().NotBeNull();
        method!.IsStatic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(IEndpointRouteBuilder));
    }

    #endregion

    #region Request Model Validation Tests

    [Fact]
    public void AnalysisExecuteRequest_RequiresAtLeastOneDocumentId()
    {
        // Arrange
        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [],
            ActionId = Guid.NewGuid()
        };

        // Assert - MinLength attribute should be present
        var attr = typeof(AnalysisExecuteRequest)
            .GetProperty(nameof(AnalysisExecuteRequest.DocumentIds))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MinLengthAttribute), false);

        attr.Should().NotBeEmpty();
    }

    [Fact]
    public void AnalysisExecuteRequest_ActionIdIsRequired()
    {
        // Assert - Required attribute should be present
        var attr = typeof(AnalysisExecuteRequest)
            .GetProperty(nameof(AnalysisExecuteRequest.ActionId))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false);

        attr.Should().NotBeEmpty();
    }

    [Fact]
    public void AnalysisSaveRequest_FileNameIsRequired()
    {
        // Assert
        var attr = typeof(AnalysisSaveRequest)
            .GetProperty(nameof(AnalysisSaveRequest.FileName))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false);

        attr.Should().NotBeEmpty();
    }

    [Fact]
    public void AnalysisSaveRequest_FileNameHasMaxLength()
    {
        // Assert
        var attr = typeof(AnalysisSaveRequest)
            .GetProperty(nameof(AnalysisSaveRequest.FileName))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MaxLengthAttribute), false);

        attr.Should().NotBeEmpty();
    }

    [Fact]
    public void AnalysisContinueRequest_MessageIsRequired()
    {
        // Assert
        var attr = typeof(AnalysisContinueRequest)
            .GetProperty(nameof(AnalysisContinueRequest.Message))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false);

        attr.Should().NotBeEmpty();
    }

    #endregion

    #region Response Model Tests

    [Fact]
    public void AnalysisStreamChunk_CanSerializeToJson()
    {
        // Arrange
        var chunk = AnalysisStreamChunk.TextChunk("Hello world");

        // Act
        var json = JsonSerializer.Serialize(chunk, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Assert
        json.Should().Contain("\"type\":\"chunk\"");
        json.Should().Contain("\"content\":\"Hello world\"");
        json.Should().Contain("\"done\":false");
    }

    [Fact]
    public void AnalysisStreamChunk_MetadataIncludesAnalysisId()
    {
        // Arrange
        var analysisId = Guid.NewGuid();

        // Act
        var chunk = AnalysisStreamChunk.Metadata(analysisId, "test.pdf");
        var json = JsonSerializer.Serialize(chunk, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Assert
        json.Should().Contain("\"type\":\"metadata\"");
        json.Should().Contain($"\"{analysisId}\"");
        json.Should().Contain("\"documentName\":\"test.pdf\"");
    }

    [Fact]
    public void AnalysisStreamChunk_CompletedIncludesTokenUsage()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var tokenUsage = new TokenUsage(1000, 500);

        // Act
        var chunk = AnalysisStreamChunk.Completed(analysisId, tokenUsage);
        var json = JsonSerializer.Serialize(chunk, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Assert
        json.Should().Contain("\"type\":\"done\"");
        json.Should().Contain("\"done\":true");
        json.Should().Contain("\"input\":1000");
        json.Should().Contain("\"output\":500");
    }

    [Fact]
    public void AnalysisStreamChunk_ErrorIncludesMessage()
    {
        // Act
        var chunk = AnalysisStreamChunk.FromError("Something went wrong");
        var json = JsonSerializer.Serialize(chunk, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Assert
        json.Should().Contain("\"type\":\"error\"");
        json.Should().Contain("\"done\":true");
        json.Should().Contain("\"error\":\"Something went wrong\"");
    }

    #endregion

    #region Export Format Validation Tests

    [Fact]
    public void ExportFormat_Email_HasValue0()
    {
        // Assert
        ((int)ExportFormat.Email).Should().Be(0);
    }

    [Fact]
    public void ExportFormat_Teams_HasValue1()
    {
        // Assert
        ((int)ExportFormat.Teams).Should().Be(1);
    }

    [Fact]
    public void ExportFormat_Pdf_HasValue2()
    {
        // Assert
        ((int)ExportFormat.Pdf).Should().Be(2);
    }

    [Fact]
    public void ExportFormat_Docx_HasValue3()
    {
        // Assert
        ((int)ExportFormat.Docx).Should().Be(3);
    }

    #endregion

    #region SSE Format Tests

    [Fact]
    public void SSEFormat_ChunkShouldBeFormattedAsDataLine()
    {
        // Arrange
        var chunk = AnalysisStreamChunk.TextChunk("Test");
        var json = JsonSerializer.Serialize(chunk, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Act - SSE format: "data: {json}\n\n"
        var sseData = $"data: {json}\n\n";

        // Assert
        sseData.Should().StartWith("data: ");
        sseData.Should().EndWith("\n\n");
        sseData.Should().Contain("\"type\":\"chunk\"");
    }

    #endregion

    #region AnalysisDetailResult Tests

    [Fact]
    public void AnalysisDetailResult_CanBeInitialized()
    {
        // Arrange & Act
        var result = new AnalysisDetailResult
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            DocumentName = "test.pdf",
            Action = new AnalysisActionInfo(Guid.NewGuid(), "Summarize"),
            Status = "Completed",
            WorkingDocument = "# Summary\n\nThis is the analysis.",
            FinalOutput = "# Summary\n\nThis is the final analysis.",
            ChatHistory = [
                new ChatMessageInfo("user", "Summarize this", DateTime.UtcNow.AddMinutes(-5)),
                new ChatMessageInfo("assistant", "Here is the summary...", DateTime.UtcNow.AddMinutes(-4))
            ],
            TokenUsage = new TokenUsage(1500, 750),
            StartedOn = DateTime.UtcNow.AddMinutes(-5),
            CompletedOn = DateTime.UtcNow
        };

        // Assert
        result.Id.Should().NotBeEmpty();
        result.DocumentName.Should().Be("test.pdf");
        result.Status.Should().Be("Completed");
        result.ChatHistory.Should().HaveCount(2);
        result.TokenUsage!.Input.Should().Be(1500);
        result.TokenUsage.Output.Should().Be(750);
    }

    #endregion

    #region ExportResult Tests

    [Fact]
    public void ExportResult_CanIndicateSuccess()
    {
        // Arrange & Act
        var result = new ExportResult
        {
            ExportType = ExportFormat.Email,
            Success = true,
            Details = new ExportDetails { Status = "Created", EmailActivityId = Guid.NewGuid() }
        };

        // Assert
        result.Success.Should().BeTrue();
        result.ExportType.Should().Be(ExportFormat.Email);
        result.Details.Should().NotBeNull();
    }

    [Fact]
    public void ExportResult_CanIndicateFailure()
    {
        // Arrange & Act
        var result = new ExportResult
        {
            ExportType = ExportFormat.Teams,
            Success = false,
            Error = "Teams integration not configured"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Teams integration not configured");
    }

    #endregion
}
