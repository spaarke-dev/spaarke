namespace Sprk.Bff.Api.Services.Ai.Testing;

/// <summary>
/// Generates sample/mock data for playbook test execution.
/// Used in Mock test mode to simulate node outputs without calling AI services.
/// </summary>
/// <remarks>
/// Sample data is generated based on:
/// - Node type (aiAnalysis, aiCompletion, condition, etc.)
/// - Scope definitions (if linked to node)
/// - Output variable expectations
///
/// All mock data is clearly labeled to distinguish from real results.
/// </remarks>
public class MockDataGenerator : IMockDataGenerator
{
    private readonly IScopeResolverService _scopeResolver;
    private readonly ILogger<MockDataGenerator> _logger;
    private static readonly Random _random = new();

    public MockDataGenerator(
        IScopeResolverService scopeResolver,
        ILogger<MockDataGenerator> logger)
    {
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MockNodeOutput> GenerateNodeOutputAsync(
        CanvasNode node,
        IDictionary<string, object>? executionContext,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating mock output for node {NodeId} of type {NodeType}", node.Id, node.Type);

        var startTime = DateTime.UtcNow;

        // Simulate processing delay (50-200ms for mock mode)
        var delay = _random.Next(50, 200);
        await Task.Delay(delay, cancellationToken);

        var output = node.Type switch
        {
            "aiAnalysis" => await GenerateAiAnalysisOutputAsync(node, executionContext, cancellationToken),
            "aiCompletion" => GenerateAiCompletionOutput(node),
            "condition" => GenerateConditionOutput(node, executionContext),
            "deliverOutput" => GenerateDeliverOutputOutput(node),
            "sendEmail" => GenerateSendEmailOutput(node),
            "updateRecord" => GenerateUpdateRecordOutput(node),
            "createTask" => GenerateCreateTaskOutput(node),
            _ => GenerateGenericOutput(node)
        };

        var durationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        return new MockNodeOutput
        {
            NodeId = node.Id,
            NodeType = node.Type,
            Output = output,
            IsMock = true,
            GeneratedAt = DateTime.UtcNow,
            DurationMs = durationMs,
            OutputVariable = node.OutputVariable
        };
    }

    /// <inheritdoc />
    public MockDocumentContext GenerateMockDocument(string? documentType = null)
    {
        var docType = documentType ?? "Generic Document";

        return new MockDocumentContext
        {
            DocumentId = $"mock-doc-{Guid.NewGuid():N}",
            DocumentType = docType,
            FileName = $"sample-{docType.ToLowerInvariant().Replace(" ", "-")}.pdf",
            FileSize = _random.Next(50000, 5000000), // 50KB - 5MB
            PageCount = _random.Next(1, 50),
            ExtractedText = GenerateMockTextContent(docType),
            Metadata = new Dictionary<string, object>
            {
                ["author"] = "Mock Author",
                ["createdDate"] = DateTime.UtcNow.AddDays(-_random.Next(1, 365)).ToString("O"),
                ["lastModified"] = DateTime.UtcNow.AddDays(-_random.Next(0, 30)).ToString("O"),
                ["isMockData"] = true
            }
        };
    }

    /// <inheritdoc />
    public Dictionary<string, object> GenerateScopeBasedOutput(
        string scopeType,
        Guid? scopeId,
        string? outputSchema)
    {
        // Generate output based on scope type
        return scopeType.ToLowerInvariant() switch
        {
            "action" => GenerateActionScopeOutput(scopeId),
            "skill" => GenerateSkillScopeOutput(scopeId),
            "knowledge" => GenerateKnowledgeScopeOutput(scopeId),
            "tool" => GenerateToolScopeOutput(scopeId, outputSchema),
            _ => new Dictionary<string, object>
            {
                ["result"] = "Mock result",
                ["scopeType"] = scopeType,
                ["isMock"] = true
            }
        };
    }

    private Task<object> GenerateAiAnalysisOutputAsync(
        CanvasNode node,
        IDictionary<string, object>? context,
        CancellationToken cancellationToken)
    {
        // Check if node has linked scopes for more realistic output
        var actionId = GetConfigValue<Guid?>(node.Config, "ActionId");
        var skills = GetConfigValue<Guid[]>(node.Config, "Skills") ?? Array.Empty<Guid>();

        var baseOutput = new Dictionary<string, object>
        {
            ["summary"] = GenerateMockSummary(),
            ["confidence"] = Math.Round(0.85 + _random.NextDouble() * 0.14, 2),
            ["analysisType"] = actionId.HasValue ? "scope-based" : "general",
            ["isMock"] = true
        };

        // Add entity extraction if configured
        if (GetConfigValue<bool?>(node.Config, "ExtractEntities") == true)
        {
            baseOutput["entities"] = GenerateMockEntities();
        }

        // Add risk assessment if skills suggest it
        if (skills.Length > 0)
        {
            baseOutput["skillResults"] = skills.Select(s => new
            {
                skillId = s,
                result = $"Mock skill result for {s}",
                confidence = Math.Round(0.80 + _random.NextDouble() * 0.19, 2)
            }).ToArray();
        }

        return Task.FromResult<object>(baseOutput);
    }

    private object GenerateAiCompletionOutput(CanvasNode node)
    {
        return new Dictionary<string, object>
        {
            ["text"] = "This is a mock AI completion response. In a real test, this would contain " +
                      "AI-generated text based on the prompt configuration.",
            ["tokensUsed"] = _random.Next(100, 500),
            ["finishReason"] = "stop",
            ["isMock"] = true
        };
    }

    private object GenerateConditionOutput(CanvasNode node, IDictionary<string, object>? context)
    {
        // Randomly choose branch for mock mode
        var result = _random.NextDouble() > 0.3;

        return new Dictionary<string, object>
        {
            ["result"] = result,
            ["branch"] = result ? "true" : "false",
            ["condition"] = GetConfigValue<string>(node.Config, "Condition") ?? "default condition",
            ["evaluatedAt"] = DateTime.UtcNow,
            ["isMock"] = true
        };
    }

    private object GenerateDeliverOutputOutput(CanvasNode node)
    {
        var format = GetConfigValue<string>(node.Config, "OutputFormat") ?? "json";

        return new Dictionary<string, object>
        {
            ["delivered"] = true,
            ["format"] = format,
            ["destination"] = GetConfigValue<string>(node.Config, "Destination") ?? "default",
            ["byteSize"] = _random.Next(1000, 50000),
            ["deliveredAt"] = DateTime.UtcNow,
            ["isMock"] = true
        };
    }

    private object GenerateSendEmailOutput(CanvasNode node)
    {
        return new Dictionary<string, object>
        {
            ["sent"] = true,
            ["messageId"] = $"mock-msg-{Guid.NewGuid():N}",
            ["recipients"] = new[] { "mock-recipient@example.com" },
            ["subject"] = "Mock Email Subject",
            ["sentAt"] = DateTime.UtcNow,
            ["isMock"] = true
        };
    }

    private object GenerateUpdateRecordOutput(CanvasNode node)
    {
        return new Dictionary<string, object>
        {
            ["updated"] = true,
            ["recordId"] = Guid.NewGuid(),
            ["entityType"] = GetConfigValue<string>(node.Config, "EntityType") ?? "sprk_document",
            ["fieldsUpdated"] = new[] { "field1", "field2" },
            ["updatedAt"] = DateTime.UtcNow,
            ["isMock"] = true
        };
    }

    private object GenerateCreateTaskOutput(CanvasNode node)
    {
        return new Dictionary<string, object>
        {
            ["created"] = true,
            ["taskId"] = Guid.NewGuid(),
            ["title"] = "Mock Task",
            ["assignedTo"] = "mock-user@example.com",
            ["dueDate"] = DateTime.UtcNow.AddDays(7),
            ["createdAt"] = DateTime.UtcNow,
            ["isMock"] = true
        };
    }

    private object GenerateGenericOutput(CanvasNode node)
    {
        return new Dictionary<string, object>
        {
            ["nodeType"] = node.Type,
            ["status"] = "completed",
            ["result"] = "Mock execution completed",
            ["isMock"] = true
        };
    }

    private Dictionary<string, object> GenerateActionScopeOutput(Guid? actionId)
    {
        return new Dictionary<string, object>
        {
            ["actionId"] = actionId?.ToString() ?? "unknown",
            ["summary"] = GenerateMockSummary(),
            ["keyFindings"] = new[]
            {
                "Mock finding 1: Document contains standard terms",
                "Mock finding 2: No significant risks identified",
                "Mock finding 3: All required sections present"
            },
            ["confidence"] = 0.92,
            ["isMock"] = true
        };
    }

    private Dictionary<string, object> GenerateSkillScopeOutput(Guid? skillId)
    {
        return new Dictionary<string, object>
        {
            ["skillId"] = skillId?.ToString() ?? "unknown",
            ["analysis"] = "Mock skill analysis result",
            ["relevantSections"] = new[] { "Section 1", "Section 3", "Section 7" },
            ["recommendations"] = new[]
            {
                "Review clause 4.2 for compliance",
                "Consider updating termination terms"
            },
            ["isMock"] = true
        };
    }

    private Dictionary<string, object> GenerateKnowledgeScopeOutput(Guid? knowledgeId)
    {
        return new Dictionary<string, object>
        {
            ["knowledgeId"] = knowledgeId?.ToString() ?? "unknown",
            ["matchedContent"] = "Mock knowledge content match",
            ["relevanceScore"] = 0.87,
            ["citations"] = new[]
            {
                new { source = "Mock Reference 1", page = 12 },
                new { source = "Mock Reference 2", page = 45 }
            },
            ["isMock"] = true
        };
    }

    private Dictionary<string, object> GenerateToolScopeOutput(Guid? toolId, string? outputSchema)
    {
        // If output schema provided, generate matching mock data
        // For now, return generic tool output
        return new Dictionary<string, object>
        {
            ["toolId"] = toolId?.ToString() ?? "unknown",
            ["extractedData"] = new Dictionary<string, object>
            {
                ["field1"] = "Mock value 1",
                ["field2"] = "Mock value 2",
                ["numericField"] = 12345.67,
                ["dateField"] = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd"),
                ["arrayField"] = new[] { "Item 1", "Item 2", "Item 3" }
            },
            ["validationPassed"] = true,
            ["isMock"] = true
        };
    }

    private string GenerateMockSummary()
    {
        var summaries = new[]
        {
            "This document contains standard business terms and conditions with typical provisions for liability, termination, and dispute resolution.",
            "The analyzed content presents a comprehensive overview of the subject matter with clear structure and well-defined sections.",
            "Based on mock analysis, this document follows standard formatting and includes all expected components for its document type.",
            "The document has been processed successfully. Key sections have been identified and relevant information extracted."
        };

        return summaries[_random.Next(summaries.Length)];
    }

    private object[] GenerateMockEntities()
    {
        return new object[]
        {
            new { type = "Person", value = "John Smith", confidence = 0.95 },
            new { type = "Organization", value = "Acme Corporation", confidence = 0.92 },
            new { type = "Date", value = "2026-01-15", confidence = 0.98 },
            new { type = "Currency", value = "$50,000.00", confidence = 0.97 },
            new { type = "Location", value = "San Francisco, CA", confidence = 0.89 }
        };
    }

    private string GenerateMockTextContent(string documentType)
    {
        return $"""
            [MOCK DOCUMENT CONTENT]

            Document Type: {documentType}
            Generated for testing purposes.

            This is simulated text content that would typically be extracted
            from an actual document. In production, Document Intelligence
            would extract the real text content.

            Sample sections:
            1. Introduction and Overview
            2. Terms and Conditions
            3. Obligations and Responsibilities
            4. Financial Terms
            5. Termination Clauses

            [END MOCK CONTENT]
            """;
    }

    /// <summary>
    /// Safely retrieves a typed value from the node config dictionary.
    /// </summary>
    private static T? GetConfigValue<T>(Dictionary<string, object?>? config, string key)
    {
        if (config == null || !config.TryGetValue(key, out var value) || value == null)
        {
            return default;
        }

        try
        {
            // Handle JSON element deserialization
            if (value is System.Text.Json.JsonElement jsonElement)
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }

            // Direct cast if possible
            if (value is T typedValue)
            {
                return typedValue;
            }

            // Try conversion
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }
}

/// <summary>
/// Interface for mock data generation in test mode.
/// </summary>
public interface IMockDataGenerator
{
    /// <summary>
    /// Generate mock output for a specific node type.
    /// </summary>
    Task<MockNodeOutput> GenerateNodeOutputAsync(
        CanvasNode node,
        IDictionary<string, object>? executionContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generate a mock document context for testing.
    /// </summary>
    MockDocumentContext GenerateMockDocument(string? documentType = null);

    /// <summary>
    /// Generate output based on scope definitions.
    /// </summary>
    Dictionary<string, object> GenerateScopeBasedOutput(
        string scopeType,
        Guid? scopeId,
        string? outputSchema);
}

/// <summary>
/// Mock node output result.
/// </summary>
public record MockNodeOutput
{
    public required string NodeId { get; init; }
    public required string NodeType { get; init; }
    public required object Output { get; init; }
    public bool IsMock { get; init; } = true;
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public int DurationMs { get; init; }
    public string? OutputVariable { get; init; }
}

/// <summary>
/// Mock document context for testing without real documents.
/// </summary>
public record MockDocumentContext
{
    public required string DocumentId { get; init; }
    public required string DocumentType { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public int PageCount { get; init; }
    public string? ExtractedText { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
