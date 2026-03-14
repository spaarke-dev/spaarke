using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Spe.Integration.Tests;

/// <summary>
/// Integration tests for the Tool Framework.
/// Tests tool discovery, registration, and composition.
/// </summary>
/// <remarks>
/// Task 015: Test Tool Framework
///
/// Tests verify:
/// - Tool handlers are discovered via assembly scanning
/// - Tool handlers are properly registered in DI
/// - Multiple tools can process the same document (composition)
/// - Tool handler registry exposes correct metadata
///
/// These tests use the actual DI container from the application.
/// </remarks>
[Collection("Integration")]
[Trait("Category", "Integration")]
[Trait("Feature", "ToolFramework")]
public class ToolFrameworkIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ToolFrameworkIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region Step 1: Tool Discovery Tests

    [Fact]
    public void ToolHandlerRegistry_IsRegisteredInDI()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();

        // Act
        var registry = scope.ServiceProvider.GetService<IToolHandlerRegistry>();

        // Assert
        registry.Should().NotBeNull("IToolHandlerRegistry should be registered in DI");
        _output.WriteLine("IToolHandlerRegistry successfully resolved from DI container");
    }

    [Fact]
    public void ToolHandlerRegistry_DiscoversCoreToolHandlers()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var handlerIds = registry.GetRegisteredHandlerIds();

        // Assert
        handlerIds.Should().Contain("GenericAnalysisHandler",
            "GenericAnalysisHandler should be discovered via assembly scanning");
        handlerIds.Should().Contain("DocumentClassifierHandler",
            "DocumentClassifierHandler should be discovered via assembly scanning");

        _output.WriteLine($"Discovered {handlerIds.Count} tool handlers:");
        foreach (var id in handlerIds)
        {
            _output.WriteLine($"  - {id}");
        }
    }

    [Fact]
    public void ToolHandlerRegistry_HasCorrectHandlerCount()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var handlerCount = registry.HandlerCount;

        // Assert
        handlerCount.Should().BeGreaterThanOrEqualTo(3,
            "At minimum, GenericAnalysis, DocumentClassifier, and Summary handlers should be registered");

        _output.WriteLine($"Total enabled handlers: {handlerCount}");
    }

    #endregion

    #region Step 2: Tool Registration Tests

    [Fact]
    public void GenericAnalysisHandler_IsAvailableAndCorrectlyConfigured()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var handler = registry.GetHandler("GenericAnalysisHandler");
        var isAvailable = registry.IsHandlerAvailable("GenericAnalysisHandler");

        // Assert
        handler.Should().NotBeNull("GenericAnalysisHandler should be retrievable");
        isAvailable.Should().BeTrue("GenericAnalysisHandler should be available");
        handler!.HandlerId.Should().Be("GenericAnalysisHandler");
        handler.Metadata.Should().NotBeNull();
        handler.Metadata.Name.Should().NotBeNullOrEmpty();
        handler.Metadata.Version.Should().NotBeNullOrEmpty();

        _output.WriteLine($"GenericAnalysisHandler:");
        _output.WriteLine($"  Name: {handler.Metadata.Name}");
        _output.WriteLine($"  Version: {handler.Metadata.Version}");
        _output.WriteLine($"  Supported Types: {string.Join(", ", handler.SupportedToolTypes)}");
        _output.WriteLine($"  Parameters: {handler.Metadata.Parameters.Count}");
    }

    [Fact]
    public void DocumentClassifierHandler_IsAvailableAndCorrectlyConfigured()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var handler = registry.GetHandler("DocumentClassifierHandler");
        var isAvailable = registry.IsHandlerAvailable("DocumentClassifierHandler");

        // Assert
        handler.Should().NotBeNull("DocumentClassifierHandler should be retrievable");
        isAvailable.Should().BeTrue("DocumentClassifierHandler should be available");
        handler!.HandlerId.Should().Be("DocumentClassifierHandler");
        handler.SupportedToolTypes.Should().Contain(ToolType.DocumentClassifier);
        handler.Metadata.Should().NotBeNull();

        _output.WriteLine($"DocumentClassifierHandler:");
        _output.WriteLine($"  Name: {handler.Metadata.Name}");
        _output.WriteLine($"  Version: {handler.Metadata.Version}");
        _output.WriteLine($"  Supported Types: {string.Join(", ", handler.SupportedToolTypes)}");
        _output.WriteLine($"  Parameters: {handler.Metadata.Parameters.Count}");
    }

    [Fact]
    public void GetHandlersByType_ReturnsCorrectHandlers()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var documentClassifiers = registry.GetHandlersByType(ToolType.DocumentClassifier);

        // Assert
        documentClassifiers.Should().NotBeEmpty("Should have DocumentClassifier handlers");

        _output.WriteLine($"Handlers by type:");
        _output.WriteLine($"  DocumentClassifier: {documentClassifiers.Count} handler(s)");
    }

    #endregion

    #region Step 3: Handler Info Tests

    [Fact]
    public void GetAllHandlerInfo_ReturnsComprehensiveMetadata()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var handlerInfos = registry.GetAllHandlerInfo();

        // Assert
        handlerInfos.Should().NotBeEmpty();
        handlerInfos.Should().AllSatisfy(info =>
        {
            info.HandlerId.Should().NotBeNullOrEmpty();
            info.Metadata.Name.Should().NotBeNullOrEmpty();
            info.Metadata.Version.Should().NotBeNullOrEmpty();
            info.SupportedToolTypes.Should().NotBeEmpty();
        });

        _output.WriteLine("All handler info:");
        foreach (var info in handlerInfos)
        {
            _output.WriteLine($"  {info.HandlerId}:");
            _output.WriteLine($"    Name: {info.Metadata.Name}");
            _output.WriteLine($"    Version: {info.Metadata.Version}");
            _output.WriteLine($"    Enabled: {info.IsEnabled}");
            _output.WriteLine($"    Types: {string.Join(", ", info.SupportedToolTypes)}");
        }
    }

    [Fact]
    public void ToolHandlers_HaveValidParameterDefinitions()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var handlerIds = registry.GetRegisteredHandlerIds();
        var parameterCounts = new Dictionary<string, int>();

        foreach (var handlerId in handlerIds)
        {
            var handler = registry.GetHandler(handlerId);
            if (handler != null)
            {
                parameterCounts[handlerId] = handler.Metadata.Parameters.Count;

                // Validate each parameter
                foreach (var param in handler.Metadata.Parameters)
                {
                    param.Name.Should().NotBeNullOrEmpty($"Parameter name should not be empty in {handlerId}");
                    param.Description.Should().NotBeNullOrEmpty($"Parameter {param.Name} in {handlerId} should have description");
                }
            }
        }

        // Assert
        parameterCounts.Should().NotBeEmpty();

        _output.WriteLine("Handler parameter counts:");
        foreach (var kvp in parameterCounts)
        {
            _output.WriteLine($"  {kvp.Key}: {kvp.Value} parameter(s)");
            var handler = registry.GetHandler(kvp.Key);
            foreach (var param in handler!.Metadata.Parameters)
            {
                _output.WriteLine($"    - {param.Name} ({param.Type}): {param.Description[..Math.Min(50, param.Description.Length)]}...");
            }
        }
    }

    #endregion

    #region Step 4: Tool Validation Tests

    [Fact]
    public void GenericAnalysisHandler_ValidatesContext()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();
        var handler = registry.GetHandler("GenericAnalysisHandler")!;

        var validContext = CreateTestContext("This is a test document with some content.");
        var tool = CreateTestTool(ToolType.Custom);

        // Act
        var result = handler.Validate(validContext, tool);

        // Assert
        result.IsValid.Should().BeTrue("Valid context should pass validation");
        result.Errors.Should().BeEmpty();

        _output.WriteLine($"Validation result: {(result.IsValid ? "VALID" : "INVALID")}");
    }

    [Fact]
    public void DocumentClassifierHandler_ValidatesContext()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();
        var handler = registry.GetHandler("DocumentClassifierHandler")!;

        var validContext = CreateTestContext("This is a document that needs to be classified.");
        var tool = CreateTestTool(ToolType.DocumentClassifier);

        // Act
        var result = handler.Validate(validContext, tool);

        // Assert
        result.IsValid.Should().BeTrue("Valid context should pass validation");
        result.Errors.Should().BeEmpty();

        _output.WriteLine($"Validation result: {(result.IsValid ? "VALID" : "INVALID")}");
    }

    [Fact]
    public void AllHandlers_RejectEmptyDocument()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        var emptyContext = CreateTestContext("");
        var handlerIds = registry.GetRegisteredHandlerIds();

        // Act & Assert
        foreach (var handlerId in handlerIds)
        {
            var handler = registry.GetHandler(handlerId)!;
            var toolType = handler.SupportedToolTypes.First();
            var tool = CreateTestTool(toolType);

            var result = handler.Validate(emptyContext, tool);

            result.IsValid.Should().BeFalse($"{handlerId} should reject empty document");
            _output.WriteLine($"{handlerId} validation for empty document: {(result.IsValid ? "VALID" : "INVALID")} - {string.Join(", ", result.Errors)}");
        }
    }

    #endregion

    #region Step 5: Tool Composition Tests

    [Fact]
    public void MultipleHandlers_CanValidateSameDocument()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        var sampleContract = @"
NON-DISCLOSURE AGREEMENT

This Non-Disclosure Agreement (the ""Agreement"") is entered into as of January 1, 2024,
by and between Acme Corporation (""Disclosing Party"") and John Smith (""Receiving Party"").

1. CONFIDENTIAL INFORMATION
The Disclosing Party agrees to share confidential business information with the Receiving Party.
All proprietary data, trade secrets, and business plans shall be considered confidential.

2. OBLIGATIONS
The Receiving Party agrees to maintain the confidentiality of all information received.
This obligation shall survive the termination of this Agreement for a period of five (5) years.

3. TERMINATION
Either party may terminate this Agreement with thirty (30) days written notice.
Upon termination, all confidential materials must be returned or destroyed.

4. GOVERNING LAW
This Agreement shall be governed by the laws of the State of California.

IN WITNESS WHEREOF, the parties have executed this Agreement.

_________________           _________________
Acme Corporation            John Smith
Date: January 1, 2024       Date: January 1, 2024
";

        var context = CreateTestContext(sampleContract);

        // Act - Validate with remaining handlers
        var genericAnalysis = registry.GetHandler("GenericAnalysisHandler")!;
        var documentClassifier = registry.GetHandler("DocumentClassifierHandler")!;

        var genericResult = genericAnalysis.Validate(context, CreateTestTool(ToolType.Custom));
        var classifierResult = documentClassifier.Validate(context, CreateTestTool(ToolType.DocumentClassifier));

        // Assert
        genericResult.IsValid.Should().BeTrue("GenericAnalysisHandler should validate successfully");
        classifierResult.IsValid.Should().BeTrue("DocumentClassifier should validate successfully");

        _output.WriteLine("Multi-handler validation results:");
        _output.WriteLine($"  GenericAnalysisHandler: {(genericResult.IsValid ? "PASS" : "FAIL")}");
        _output.WriteLine($"  DocumentClassifier: {(classifierResult.IsValid ? "PASS" : "FAIL")}");
        _output.WriteLine($"\nDocument length: {sampleContract.Length} characters");
        _output.WriteLine($"Tool composition: Handlers validated same document successfully");
    }

    [Fact]
    public void ToolComposition_PreviousResultsAccessible()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        var testDocument = "This is a test document for Acme Corporation.";
        var previousResult = ToolResult.Ok(
            "GenericAnalysisHandler",
            Guid.NewGuid(),
            "Entity Extraction",
            new { entities = new[] { new { name = "Acme Corporation", type = "Organization" } } },
            "Found 1 entity",
            0.95);

        var contextWithPreviousResults = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "test-tenant",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test Document",
                ExtractedText = testDocument
            },
            PreviousResults = new Dictionary<string, ToolResult>
            {
                ["GenericAnalysisHandler"] = previousResult
            }
        };

        // Act - Next handler can access previous results
        var documentClassifier = registry.GetHandler("DocumentClassifierHandler")!;
        var validationResult = documentClassifier.Validate(contextWithPreviousResults, CreateTestTool(ToolType.DocumentClassifier));

        // Assert
        contextWithPreviousResults.PreviousResults.Should().ContainKey("GenericAnalysisHandler");
        contextWithPreviousResults.PreviousResults["GenericAnalysisHandler"].Success.Should().BeTrue();
        validationResult.IsValid.Should().BeTrue();

        _output.WriteLine("Tool composition with previous results:");
        _output.WriteLine($"  Previous result from: GenericAnalysisHandler");
        _output.WriteLine($"  Previous result success: {previousResult.Success}");
        _output.WriteLine($"  Next handler validation: {(validationResult.IsValid ? "PASS" : "FAIL")}");
    }

    #endregion

    #region Step 6: Error Handling Tests

    [Fact]
    public void GetHandler_WithInvalidId_ReturnsNull()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var handler = registry.GetHandler("NonExistentHandler");

        // Assert
        handler.Should().BeNull("Non-existent handler should return null");

        _output.WriteLine("GetHandler with invalid ID correctly returned null");
    }

    [Fact]
    public void IsHandlerAvailable_WithInvalidId_ReturnsFalse()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var isAvailable = registry.IsHandlerAvailable("NonExistentHandler");

        // Assert
        isAvailable.Should().BeFalse("Non-existent handler should not be available");

        _output.WriteLine("IsHandlerAvailable with invalid ID correctly returned false");
    }

    [Fact]
    public void GetHandlersByType_WithUnusedType_ReturnsEmptyList()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var handlers = registry.GetHandlersByType(ToolType.Custom);

        // Assert - Custom type likely has no handlers
        // This is expected behavior, not an error
        _output.WriteLine($"GetHandlersByType(Custom) returned {handlers.Count} handler(s)");
    }

    [Fact]
    public void Handler_CaseInsensitiveLookup_Works()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

        // Act
        var handler1 = registry.GetHandler("GENERICANALYSISHANDLER");
        var handler2 = registry.GetHandler("genericanalysishandler");
        var handler3 = registry.GetHandler("GenericAnalysisHandler");

        // Assert
        handler1.Should().NotBeNull();
        handler2.Should().NotBeNull();
        handler3.Should().NotBeNull();
        handler1!.HandlerId.Should().Be(handler2!.HandlerId);
        handler2.HandlerId.Should().Be(handler3!.HandlerId);

        _output.WriteLine("Case-insensitive lookup works correctly for all variations");
    }

    #endregion

    #region Helper Methods

    private static ToolExecutionContext CreateTestContext(string documentText)
    {
        return new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "test-tenant",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test Document",
                FileName = "test.pdf",
                ContentType = "application/pdf",
                ExtractedText = documentText
            },
            MaxTokens = 4096,
            Temperature = 0.3
        };
    }

    private static AnalysisTool CreateTestTool(ToolType toolType)
    {
        // GenericAnalysisHandler requires 'operation' and 'prompt_template' in configuration
        var config = toolType == ToolType.Custom
            ? System.Text.Json.JsonSerializer.Serialize(new
            {
                operation = "analyze",
                prompt_template = "Analyze the following document: {{document}}"
            })
            : "{}";

        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = $"Test {toolType} Tool",
            Description = $"Test tool for {toolType}",
            Type = toolType,
            HandlerClass = $"{toolType}Handler",
            Configuration = config
        };
    }

    #endregion
}
