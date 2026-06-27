using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// R6 Pillar 2 (tasks D-A-07 / FR-07 + D-A-08 / FR-08) — DTO contract tests for the
/// AnalysisTool.AvailableInContexts discriminator + ToolAvailabilityContext enum
/// + Dataverse option-set value mapper + AnalysisTool.JsonSchema field + JSON
/// well-formedness mapper.
///
/// These tests guard the AnalysisTool DTO contract that downstream tasks
/// (010 adapter, 011 chat resolver, 012 batch migration) all depend on.
/// </summary>
public class AnalysisToolDtoTests
{
    // -------------------------------------------------------------------------
    // Enum value contract — must match canonical Spaarke Dataverse option-set
    // values used by sprk_availableincontexts on sprk_analysistool.
    // Changing these values is a breaking change to Dataverse-side stored rows.
    // -------------------------------------------------------------------------

    [Fact]
    public void ToolAvailabilityContext_Playbook_MatchesDataverseOptionSetValue()
    {
        ((int)ToolAvailabilityContext.Playbook).Should().Be(100000000);
    }

    [Fact]
    public void ToolAvailabilityContext_Chat_MatchesDataverseOptionSetValue()
    {
        ((int)ToolAvailabilityContext.Chat).Should().Be(100000001);
    }

    [Fact]
    public void ToolAvailabilityContext_Both_MatchesDataverseOptionSetValue()
    {
        ((int)ToolAvailabilityContext.Both).Should().Be(100000002);
    }

    [Fact]
    public void ToolAvailabilityContext_HasExactlyThreeValues()
    {
        // FR-07 binding: exactly three discriminator values. If a fourth is added,
        // FR-07 has been amended and every consumer of AvailableInContexts must be reviewed.
        var values = Enum.GetValues<ToolAvailabilityContext>();
        values.Should().HaveCount(3);
        values.Should().BeEquivalentTo(new[]
        {
            ToolAvailabilityContext.Playbook,
            ToolAvailabilityContext.Chat,
            ToolAvailabilityContext.Both
        });
    }

    // -------------------------------------------------------------------------
    // DTO default — backward-compat per FR-07:
    // AvailableInContexts is nullable. Default DTO leaves it unset (null) and
    // consumers treat null as Playbook.
    // -------------------------------------------------------------------------

    [Fact]
    public void AnalysisTool_DefaultConstruction_AvailableInContextsIsNull()
    {
        // Pre-R6 rows in Dataverse won't have sprk_availableincontexts populated.
        // The DTO must default to null so the mapper can apply the
        // "null → Playbook" backward-compat rule at resolve time.
        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "SYS-DocumentSearch",
            Type = ToolType.Custom
        };

        tool.AvailableInContexts.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Round-trip serialization — JSON serializer (System.Text.Json) must
    // preserve all three enum values + null. This is the over-the-wire
    // contract between BFF endpoints and clients.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(ToolAvailabilityContext.Playbook)]
    [InlineData(ToolAvailabilityContext.Chat)]
    [InlineData(ToolAvailabilityContext.Both)]
    public void AnalysisTool_SerializeDeserialize_RoundTripsAvailableInContexts(ToolAvailabilityContext value)
    {
        // Arrange
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "TestTool",
            Type = ToolType.Custom,
            HandlerClass = "TestHandler",
            AvailableInContexts = value
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<AnalysisTool>(json);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.AvailableInContexts.Should().Be(value);
    }

    [Fact]
    public void AnalysisTool_SerializeDeserialize_NullAvailableInContextsPreserved()
    {
        // Arrange — backward-compat invariant: null wire value remains null DTO value.
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "PreR6Tool",
            Type = ToolType.Custom,
            AvailableInContexts = null
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<AnalysisTool>(json);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.AvailableInContexts.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Dataverse option-set mapper — AnalysisToolService.MapAvailableInContexts
    // converts raw int? from Dataverse into ToolAvailabilityContext?.
    // -------------------------------------------------------------------------

    [Fact]
    public void MapAvailableInContexts_NullRaw_ReturnsNull()
    {
        // Pre-R6 rows whose column is unpopulated (or new rows that haven't
        // been migrated yet). FR-07 backward-compat — callers treat null as Playbook.
        AnalysisToolService.MapAvailableInContexts(null).Should().BeNull();
    }

    [Theory]
    [InlineData(100000000, ToolAvailabilityContext.Playbook)]
    [InlineData(100000001, ToolAvailabilityContext.Chat)]
    [InlineData(100000002, ToolAvailabilityContext.Both)]
    public void MapAvailableInContexts_KnownRaw_ReturnsCorrectEnum(int rawValue, ToolAvailabilityContext expected)
    {
        AnalysisToolService.MapAvailableInContexts(rawValue).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99999)]
    [InlineData(100000003)]
    public void MapAvailableInContexts_UnknownRaw_ReturnsNull(int rawValue)
    {
        // Forward-compat safety: an unknown option-set value (e.g., admin added a
        // new option that BFF doesn't yet know about) maps to null rather than
        // throwing. Callers fall back to Playbook (backward-compat per FR-07).
        AnalysisToolService.MapAvailableInContexts(rawValue).Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Record copy semantics — AnalysisTool is a positional record; the `with`
    // expression must preserve AvailableInContexts (verifies the property is
    // a true record member, not a forgotten manually-added field).
    // -------------------------------------------------------------------------

    [Fact]
    public void AnalysisTool_WithExpression_PreservesAvailableInContexts()
    {
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "TestTool",
            Type = ToolType.Custom,
            AvailableInContexts = ToolAvailabilityContext.Chat
        };

        var modified = original with { Name = "RenamedTool" };

        modified.AvailableInContexts.Should().Be(ToolAvailabilityContext.Chat);
        modified.Name.Should().Be("RenamedTool");
    }

    // =========================================================================
    // R6 Pillar 2 (task D-A-08 / FR-08) — JsonSchema field tests
    // =========================================================================

    // -------------------------------------------------------------------------
    // DTO default — backward-compat per FR-08:
    // JsonSchema is nullable on the DTO. Default DTO leaves it unset (null) and
    // consumers (chat resolver in task 011) enforce the "required-for-chat" rule.
    // -------------------------------------------------------------------------

    [Fact]
    public void AnalysisTool_DefaultConstruction_JsonSchemaIsNull()
    {
        // Pre-R6 playbook-only rows in Dataverse don't have sprk_jsonschema populated.
        // The DTO must default to null so playbook-only tools remain assignable.
        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "SYS-PlaybookOnlyTool",
            Type = ToolType.Custom
        };

        tool.JsonSchema.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Round-trip serialization — JSON serializer (System.Text.Json) must
    // preserve JsonSchema string + null. This is the over-the-wire contract
    // between BFF endpoints and clients, and (more importantly) between the
    // Dataverse fetch path and the ToolHandlerToAIFunctionAdapter (task 010).
    // -------------------------------------------------------------------------

    [Fact]
    public void AnalysisTool_SerializeDeserialize_RoundTripsJsonSchemaPopulated()
    {
        // Arrange — a representative tool JSON Schema (Draft 2020-12-ish).
        const string schema = """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query" },
            "pageSize": { "type": "integer", "minimum": 1, "maximum": 50 }
          },
          "required": ["query"]
        }
        """;

        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "ChatSearchTool",
            Type = ToolType.Custom,
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = schema
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<AnalysisTool>(json);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.JsonSchema.Should().Be(schema);
    }

    [Fact]
    public void AnalysisTool_SerializeDeserialize_NullJsonSchemaPreserved()
    {
        // Backward-compat invariant: null wire value remains null DTO value
        // (pre-R6 playbook-only tools whose column is unpopulated).
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "PreR6PlaybookTool",
            Type = ToolType.Custom,
            JsonSchema = null
        };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<AnalysisTool>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.JsonSchema.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Record copy semantics — `with` expression must preserve JsonSchema.
    // -------------------------------------------------------------------------

    [Fact]
    public void AnalysisTool_WithExpression_PreservesJsonSchema()
    {
        const string schema = """{"type":"object","properties":{"x":{"type":"integer"}}}""";
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "TestTool",
            Type = ToolType.Custom,
            JsonSchema = schema
        };

        var modified = original with { Name = "RenamedTool" };

        modified.JsonSchema.Should().Be(schema);
        modified.Name.Should().Be("RenamedTool");
    }

    // -------------------------------------------------------------------------
    // Dataverse JsonSchema mapper — AnalysisToolService.MapJsonSchema validates
    // well-formedness and treats null/whitespace as "no schema set" (FR-08).
    // Malformed JSON is logged and mapped to null rather than passed to the LLM.
    // -------------------------------------------------------------------------

    [Fact]
    public void MapJsonSchema_NullRaw_ReturnsNull()
    {
        // Pre-R6 playbook-only rows: column is null → DTO field stays null.
        AnalysisToolService.MapJsonSchema(null, Guid.NewGuid(), NullLogger.Instance)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void MapJsonSchema_EmptyOrWhitespace_ReturnsNull(string rawValue)
    {
        // Whitespace-only strings are treated the same as null — no schema set.
        AnalysisToolService.MapJsonSchema(rawValue, Guid.NewGuid(), NullLogger.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void MapJsonSchema_ValidJson_ReturnsRawValue()
    {
        // Well-formed JSON passes through unchanged (the adapter at task 010
        // is responsible for JSON Schema semantic validation, not this mapper).
        const string schema = """
        {
          "type": "object",
          "properties": { "query": { "type": "string" } },
          "required": ["query"]
        }
        """;

        var result = AnalysisToolService.MapJsonSchema(schema, Guid.NewGuid(), NullLogger.Instance);

        result.Should().Be(schema);
    }

    [Theory]
    [InlineData("""{"type":"string"}""")]
    [InlineData("true")]   // Draft 2020-12: a boolean is a valid root schema (true = accept everything)
    [InlineData("false")]  // Draft 2020-12: false = reject everything (still a valid schema)
    public void MapJsonSchema_ValidSchemaJsonShapes_ReturnsRawValue(string rawValue)
    {
        // R6 audit item 1: the mapper now requires semantic JSON Schema validity, not just
        // well-formedness. Per JSON Schema Draft 2020-12, a schema is either an object or
        // a boolean — every other JSON value form (string, number, null, array) is rejected.
        AnalysisToolService.MapJsonSchema(rawValue, Guid.NewGuid(), NullLogger.Instance)
            .Should().Be(rawValue);
    }

    [Theory]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"a string\"")]
    public void MapJsonSchema_WellFormedJsonButInvalidSchemaShape_ReturnsNull(string rawValue)
    {
        // R6 audit item 1: well-formed JSON that is not a valid JSON Schema (per Draft
        // 2020-12: must be object or boolean) is now rejected by the semantic validator
        // and mapped to null with a warning. The chat resolver (task 011) refuses to
        // expose tools whose schema is null, so the LLM never sees a malformed contract.
        AnalysisToolService.MapJsonSchema(rawValue, Guid.NewGuid(), NullLogger.Instance)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("{type: object}")]  // unquoted keys — not legal JSON
    [InlineData("{\"type\": \"object\",}")]  // trailing comma — not legal JSON
    [InlineData("undefined")]
    public void MapJsonSchema_MalformedJson_ReturnsNullAndDoesNotThrow(string rawValue)
    {
        // FR-08 contract: malformed JSON must NEVER be silently passed downstream
        // to the LLM. The mapper logs + returns null so the chat resolver
        // (task 011) can refuse to expose the tool with a clear diagnostic trail.
        var act = () => AnalysisToolService.MapJsonSchema(rawValue, Guid.NewGuid(), NullLogger.Instance);

        act.Should().NotThrow();
        AnalysisToolService.MapJsonSchema(rawValue, Guid.NewGuid(), NullLogger.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void MapJsonSchema_LargeValidSchema_ReturnsRawValue()
    {
        // Verify the mapper handles JSON near the Dataverse column ceiling
        // (sprk_jsonschema MaxLength=100000). Production tool schemas can
        // run to several KB (e.g., DocumentSearch with filters + pagination).
        var sb = new System.Text.StringBuilder();
        sb.Append("""{"type":"object","properties":{""");
        for (int i = 0; i < 500; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"\"prop{i}\":{{\"type\":\"string\"}}");
        }
        sb.Append("}}");
        var largeSchema = sb.ToString();

        // Sanity: schema must be valid and within Dataverse Memo bounds
        largeSchema.Length.Should().BeLessThan(100000);

        var result = AnalysisToolService.MapJsonSchema(largeSchema, Guid.NewGuid(), NullLogger.Instance);

        result.Should().Be(largeSchema);
    }

    // =========================================================================
    // R6 audit item 1 (2026-06-07) — semantic JSON Schema validation.
    // MapJsonSchema now performs Draft 2020-12 meta-schema validation in
    // addition to JSON well-formedness. Malformed schemas are mapped to null
    // (silent-warning behavior preserved) so the chat resolver refuses to
    // expose the tool to the LLM.
    // =========================================================================

    [Fact]
    public void MapJsonSchema_SemanticInvalid_PropertiesValueIsNumber_ReturnsNull()
    {
        // Well-formed JSON, but "properties.query" is the integer 42 — must be a schema
        // object. Draft 2020-12 meta-schema rejects this; mapper returns null.
        const string schema = """{"type":"object","properties":{"query":42}}""";

        AnalysisToolService.MapJsonSchema(schema, Guid.NewGuid(), NullLogger.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void MapJsonSchema_SemanticInvalid_RequiredValueIsString_ReturnsNull()
    {
        // "required" must be an array of strings per Draft 2020-12. Here it is a string.
        const string schema = """{"type":"object","required":"query"}""";

        AnalysisToolService.MapJsonSchema(schema, Guid.NewGuid(), NullLogger.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void MapJsonSchema_SemanticInvalid_TypeIsObjectNotString_ReturnsNull()
    {
        // "type" must be a string or array of strings; here it is an object.
        const string schema = """{"type":{"oops":true}}""";

        AnalysisToolService.MapJsonSchema(schema, Guid.NewGuid(), NullLogger.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void MapJsonSchema_SemanticValid_StandardFunctionCallingShape_ReturnsRawValue()
    {
        // Canonical LLM function-calling schema (the shape every chat-available tool ships).
        // Must round-trip through the new validator unchanged.
        const string schema = """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query" },
            "topK": { "type": "integer", "default": 5, "minimum": 1, "maximum": 50 }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """;

        AnalysisToolService.MapJsonSchema(schema, Guid.NewGuid(), NullLogger.Instance)
            .Should().Be(schema);
    }

    [Fact]
    public void MapJsonSchema_SemanticInvalid_LogsWarning()
    {
        // R6 audit item 1: malformed-schema warning is emitted so admins see the
        // failure in BFF logs as soon as the row is loaded (no silent LLM-time failure).
        var logger = new TestLogger();
        const string badSchema = """{"type":"object","properties":{"query":42}}""";

        var result = AnalysisToolService.MapJsonSchema(badSchema, Guid.NewGuid(), logger);

        result.Should().BeNull();
        logger.WarningCount.Should().BeGreaterThan(0,
            "R6-audit-1: malformed schemas must emit a warning so admins can fix the row");
        logger.LastWarningMessage.Should().Contain("R6-audit-1",
            "warning must carry the R6-audit-1 marker so log filters can find it");
    }

    // =========================================================================
    // R6 Wave 7b (2026-06-08) — RequiredCapability field tests.
    // The per-playbook capability filter infrastructure that the data-driven
    // block in SprkChatAgentFactory.ResolveTools() consults to skip tools whose
    // sprk_requiredcapability isn't in the current playbook's capability set.
    // Wave 7c (VerifyCitations) + Wave 8 (LegalResearch / WebSearch /
    // CodeInterpreter) + Wave 9 (WorkingDocumentTools) populate this field on
    // their migrated rows.
    // =========================================================================

    [Fact]
    public void AnalysisTool_DefaultConstruction_RequiredCapabilityIsNull()
    {
        // Wave 7b backward-compat: pre-Wave-7b rows have null RequiredCapability
        // → always-available behavior matches today's hardcoded chat-tool registration.
        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "SYS-AnalysisQuery",
            Type = ToolType.Custom
        };

        tool.RequiredCapability.Should().BeNull();
    }

    [Theory]
    [InlineData("verify_citations")]
    [InlineData("write_back")]
    [InlineData("web_search")]
    [InlineData("code_interpreter")]
    [InlineData("legal_research")]
    [InlineData("reanalyze")]
    public void AnalysisTool_SerializeDeserialize_RoundTripsRequiredCapability(string capability)
    {
        // Arrange — every canonical capability value must survive a System.Text.Json
        // round-trip. The BFF returns this field to clients via the listing endpoints;
        // the wire contract must remain stable.
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "GatedTool",
            Type = ToolType.Custom,
            AvailableInContexts = ToolAvailabilityContext.Chat,
            RequiredCapability = capability
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<AnalysisTool>(json);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.RequiredCapability.Should().Be(capability);
    }

    [Fact]
    public void AnalysisTool_SerializeDeserialize_NullRequiredCapabilityPreserved()
    {
        // Backward-compat invariant: null wire value remains null DTO value (rows
        // without a capability gate stay always-available).
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "UnGatedTool",
            Type = ToolType.Custom,
            RequiredCapability = null
        };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<AnalysisTool>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.RequiredCapability.Should().BeNull();
    }

    [Fact]
    public void AnalysisTool_WithExpression_PreservesRequiredCapability()
    {
        // Wave 7b: verify the property is a true record member (caught at compile
        // time if someone forgets the { get; init; } accessor).
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "VerifyCitations",
            Type = ToolType.Custom,
            RequiredCapability = "verify_citations"
        };

        var modified = original with { Name = "RenamedVerifyCitations" };

        modified.RequiredCapability.Should().Be("verify_citations");
        modified.Name.Should().Be("RenamedVerifyCitations");
    }

    // -------------------------------------------------------------------------
    // MapRequiredCapability — null / whitespace / value contract.
    // -------------------------------------------------------------------------

    [Fact]
    public void MapRequiredCapability_NullRaw_ReturnsNull()
    {
        // Pre-Wave-7b rows: column is null → DTO field stays null (always-available).
        AnalysisToolService.MapRequiredCapability(null).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void MapRequiredCapability_EmptyOrWhitespace_ReturnsNull(string rawValue)
    {
        // Whitespace-only strings are treated the same as null — no capability gate set.
        AnalysisToolService.MapRequiredCapability(rawValue).Should().BeNull();
    }

    [Theory]
    [InlineData("verify_citations", "verify_citations")]
    [InlineData("write_back", "write_back")]
    [InlineData("web_search", "web_search")]
    [InlineData("VERIFY_CITATIONS", "VERIFY_CITATIONS")]  // case preserved (filter is case-insensitive)
    public void MapRequiredCapability_KnownCapability_ReturnsValue(string rawValue, string expected)
    {
        // Canonical capability constants pass through unchanged. Case is preserved
        // because the chat-side filter performs case-insensitive comparison.
        AnalysisToolService.MapRequiredCapability(rawValue).Should().Be(expected);
    }

    [Theory]
    [InlineData(" verify_citations ", "verify_citations")]
    [InlineData("  write_back", "write_back")]
    [InlineData("web_search   ", "web_search")]
    public void MapRequiredCapability_TrimsSurroundingWhitespace(string rawValue, string expected)
    {
        // Defensive: admins editing the column in Power Apps may add leading/trailing
        // whitespace. Trim so the case-insensitive comparator at the filter still matches.
        AnalysisToolService.MapRequiredCapability(rawValue).Should().Be(expected);
    }

    [Fact]
    public void MapRequiredCapability_UnknownCapability_ReturnsValueAsTyped()
    {
        // Forward-compat safety: an unknown capability value (e.g., admin types
        // "future_capability" before the matching playbook capability is defined)
        // passes through unchanged. The filter then no-ops for current playbooks
        // (no match), effectively withholding the tool until a playbook with that
        // capability exists — matching the desired "gate first, define later" UX.
        AnalysisToolService.MapRequiredCapability("future_capability")
            .Should().Be("future_capability");
    }

    private sealed class TestLogger : ILogger
    {
        public int WarningCount { get; private set; }
        public string? LastWarningMessage { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
                LastWarningMessage = formatter(state, exception);
            }
        }
    }
}
