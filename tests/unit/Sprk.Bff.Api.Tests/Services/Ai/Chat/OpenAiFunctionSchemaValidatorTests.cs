using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// G-P3 UAT round-1 H1 (2026-07-07) — projection-time validation of catalog
/// schemas against the OpenAI function-parameters subset. Root incident: the
/// CREATE-TASK@v1 Action row's <c>sprk_inputschema</c> carried
/// <c>"required": true</c> INSIDE the due_date/assign_to property definitions;
/// Azure OpenAI validates every known keyword anywhere in every projected tool
/// schema and rejects the ENTIRE request (<c>invalid_function_parameters</c> —
/// "True is not of type 'array'"), 400-failing EVERY text-path turn. The
/// validator lets one malformed row cost only its OWN tool.
/// </summary>
public class OpenAiFunctionSchemaValidatorTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // The exact UAT payload — the regression pin
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The CREATE-TASK@v1 schema EXACTLY as seeded by task 042 (property-level
    /// "required": true inside due_date + assign_to) — the payload that took
    /// down every G-P3 text-path turn.
    /// </summary>
    private const string UatInvalidCreateTaskSchema =
        """{"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"},"description":"Optional subset of session file ids the task should be grounded in. Omit to use all session files."},"due_date":{"type":"string","required":true,"elicitation_prompt":"What's the due date for this task?","description":"The task's due date as the user stated it (e.g. 7/9/2026)."},"assign_to":{"type":"string","required":true,"elicitation_prompt":"Should I assign it to you or someone else?","description":"Who the task is assigned to — 'me' or a person's name."}},"required":["due_date","assign_to"]}""";

    /// <summary>The corrected shape now live on spaarkedev1 (object-level required array only).</summary>
    private const string FixedCreateTaskSchema =
        """{"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"},"description":"Optional subset of session file ids the task should be grounded in. Omit to use all session files."},"due_date":{"type":"string","elicitation_prompt":"What's the due date for this task?","description":"The task's due date as the user stated it (e.g. 7/9/2026)."},"assign_to":{"type":"string","elicitation_prompt":"Should I assign it to you or someone else?","description":"Who the task is assigned to — 'me' or a person's name."}},"required":["due_date","assign_to"]}""";

    [Fact]
    public void FindFirstError_ExactUatPayload_PropertyLevelRequiredTrue_IsInvalid()
    {
        var error = OpenAiFunctionSchemaValidator.FindFirstError(UatInvalidCreateTaskSchema);

        error.Should().NotBeNull(
            "the exact G-P3 UAT payload (property-level \"required\": true) must be caught " +
            "at projection time — OpenAI rejects the WHOLE request over it");
        error.Should().Contain("required").And.Contain("array",
            "the error names the offending keyword so an operator can fix the row");
        error.Should().Contain("due_date",
            "the keyword path names the offending property (NFR-07-safe: schema structure, not content)");
    }

    [Fact]
    public void FindFirstError_CorrectedCreateTaskSchema_IsValid()
    {
        OpenAiFunctionSchemaValidator.FindFirstError(FixedCreateTaskSchema).Should().BeNull(
            "the corrected spaarkedev1 shape (object-level required array + custom " +
            "elicitation_prompt keywords) is valid OpenAI function parameters");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tolerated shapes (degrade-upstream or OpenAI-accepted)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindFirstError_NullOrWhitespace_IsValid(string? raw)
    {
        OpenAiFunctionSchemaValidator.FindFirstError(raw).Should().BeNull(
            "schema-less Bindings project the safe default schema — nothing invalid reaches OpenAI");
    }

    [Fact]
    public void FindFirstError_MalformedJson_IsValid_BecauseProjectionDegradesToDefault()
    {
        OpenAiFunctionSchemaValidator.FindFirstError("{not json").Should().BeNull(
            "malformed maker JSON degrades to the default schema in ParseSchema (routing tolerance) — " +
            "it never reaches OpenAI, so it is not an exclusion condition");
    }

    [Fact]
    public void FindFirstError_NonObjectRoot_IsValid_BecauseProjectionDegradesToDefault()
    {
        OpenAiFunctionSchemaValidator.FindFirstError("[1,2,3]").Should().BeNull(
            "a non-object root degrades to the default schema in ParseSchema — never reaches OpenAI");
    }

    [Fact]
    public void FindFirstError_LegacyArgsFormat_IsTolerated()
    {
        // The pre-042 legacy format (SUM-CHAT / CLS-CHAT / DAILY-BRIEFING before the H2
        // normalization): "args" is an unknown keyword, so OpenAI projects an effectively
        // empty permissive schema and accepts the request. Tolerating it here keeps
        // un-normalized environments running (their capabilities keep working, just with
        // zero arg documentation to the model).
        const string legacy =
            """{"args":[{"name":"fileIds","type":"array","required":false,"elicitation":"Which uploaded file(s) should I summarize?"}]}""";

        OpenAiFunctionSchemaValidator.FindFirstError(legacy).Should().BeNull(
            "unknown keywords are ignored by OpenAI — the legacy args wrapper is tolerated, not fatal");
    }

    [Fact]
    public void FindFirstError_CustomMakerKeywords_AreTolerated()
    {
        const string schema =
            """{"type":"object","properties":{"x":{"type":"string","elicitation_prompt":"What is x?","ledger_resolution":"none"}}}""";

        OpenAiFunctionSchemaValidator.FindFirstError(schema).Should().BeNull(
            "elicitation_prompt / ledger_resolution are the 042-established maker-metadata keywords");
    }

    [Fact]
    public void FindFirstError_MissingRootType_IsTolerated()
    {
        OpenAiFunctionSchemaValidator.FindFirstError("""{"properties":{"x":{"type":"string"}}}""")
            .Should().BeNull("OpenAI accepted root-typeless schemas for months (legacy rows) — no protocol gain in rejecting them");
    }

    [Fact]
    public void FindFirstError_CompositionAndNestedShapes_AreValid()
    {
        const string schema =
            """{"type":"object","properties":{"choice":{"anyOf":[{"type":"string"},{"type":"integer"}]},"list":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"}},"required":["id"]}}},"additionalProperties":false}""";

        OpenAiFunctionSchemaValidator.FindFirstError(schema).Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rejected shapes (each 400-fails the whole request at OpenAI)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FindFirstError_RequiredAsString_IsInvalid()
    {
        OpenAiFunctionSchemaValidator.FindFirstError(
            """{"type":"object","properties":{"x":{"type":"string"}},"required":"x"}""")
            .Should().NotBeNull("required must be an array — a string is rejected by OpenAI");
    }

    [Fact]
    public void FindFirstError_RequiredArrayWithNonStringEntries_IsInvalid()
    {
        OpenAiFunctionSchemaValidator.FindFirstError(
            """{"type":"object","properties":{"x":{"type":"string"}},"required":[1,2]}""")
            .Should().NotBeNull("required entries must be property-name strings");
    }

    [Fact]
    public void FindFirstError_IllegalTypeName_IsInvalid()
    {
        OpenAiFunctionSchemaValidator.FindFirstError(
            """{"type":"object","properties":{"x":{"type":"text"}}}""")
            .Should().NotBeNull("'text' is not a JSON-Schema type — OpenAI rejects unknown type names");
    }

    [Fact]
    public void FindFirstError_RootTypeNotObject_IsInvalid()
    {
        OpenAiFunctionSchemaValidator.FindFirstError("""{"type":"string"}""")
            .Should().NotBeNull("function parameters are always an object envelope");
    }

    [Fact]
    public void FindFirstError_ArrayWithoutItems_IsInvalid()
    {
        // Valid Draft 2020-12 JSON Schema — but OpenAI rejects array schemas without
        // items. This is exactly the class the meta-schema validation on the
        // sprk_analysistool leg could NOT catch.
        OpenAiFunctionSchemaValidator.FindFirstError(
            """{"type":"object","properties":{"ids":{"type":"array"}}}""")
            .Should().NotBeNull("OpenAI rejects array schemas that do not declare items");
    }

    [Fact]
    public void FindFirstError_PropertiesAsArray_IsInvalid()
    {
        OpenAiFunctionSchemaValidator.FindFirstError(
            """{"type":"object","properties":[{"name":"x"}]}""")
            .Should().NotBeNull("properties must be an object map, not an array");
    }

    [Fact]
    public void FindFirstError_PropertyValueNotAnObject_IsInvalid()
    {
        OpenAiFunctionSchemaValidator.FindFirstError(
            """{"type":"object","properties":{"x":"string"}}""")
            .Should().NotBeNull("each property value must be a schema object");
    }

    [Fact]
    public void FindFirstError_EnumNotAnArray_IsInvalid()
    {
        OpenAiFunctionSchemaValidator.FindFirstError(
            """{"type":"object","properties":{"x":{"type":"string","enum":"a"}}}""")
            .Should().NotBeNull("enum must be an array");
    }

    [Fact]
    public void FindFirstError_AdditionalPropertiesAsString_IsInvalid()
    {
        OpenAiFunctionSchemaValidator.FindFirstError(
            """{"type":"object","additionalProperties":"false"}""")
            .Should().NotBeNull("additionalProperties must be a boolean or schema object — a string is rejected");
    }

    [Fact]
    public void FindFirstError_NestedInvalidRequired_IsFoundAtDepth()
    {
        // The invalid keyword sits two levels deep (inside items) — the walk must find it.
        var error = OpenAiFunctionSchemaValidator.FindFirstError(
            """{"type":"object","properties":{"list":{"type":"array","items":{"type":"object","properties":{"y":{"type":"string","required":true}}}}}}""");

        error.Should().NotBeNull("OpenAI validates keywords at EVERY depth of every tool schema");
        error.Should().Contain("required");
    }
}
