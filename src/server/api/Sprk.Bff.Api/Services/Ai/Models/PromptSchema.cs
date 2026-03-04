using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Schemas;

/// <summary>
/// Root record for the JSON Prompt Schema (JPS) format.
/// JPS provides a structured, composable schema for AI prompts stored in
/// <c>sprk_systemprompt</c> on <c>sprk_analysisaction</c>.
/// </summary>
/// <remarks>
/// Format detection: if the stored prompt starts with '{' and contains "$schema",
/// it is parsed as JPS; otherwise it is treated as flat text.
/// </remarks>
public sealed record PromptSchema
{
    /// <summary>
    /// Schema URI identifying this as a JPS document.
    /// Expected value: <c>https://spaarke.com/schemas/prompt/v1</c>.
    /// </summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    /// <summary>
    /// Schema version number. Defaults to 1.
    /// </summary>
    [JsonPropertyName("$version")]
    public int Version { get; init; } = 1;

    /// <summary>
    /// The core AI instruction section containing role, task, constraints, and context.
    /// </summary>
    [JsonPropertyName("instruction")]
    public required InstructionSection Instruction { get; init; }

    /// <summary>
    /// Input configuration describing what the AI receives (document, prior outputs, parameters).
    /// </summary>
    [JsonPropertyName("input")]
    public InputSection? Input { get; init; }

    /// <summary>
    /// Output configuration defining the expected response structure and field definitions.
    /// </summary>
    [JsonPropertyName("output")]
    public OutputSection? Output { get; init; }

    /// <summary>
    /// Explicit scope references for skills and knowledge sources that supplement N:N relationships.
    /// </summary>
    [JsonPropertyName("scopes")]
    public ScopesSection? Scopes { get; init; }

    /// <summary>
    /// Few-shot learning examples with input/output pairs.
    /// </summary>
    [JsonPropertyName("examples")]
    public IReadOnlyList<ExampleEntry>? Examples { get; init; }

    /// <summary>
    /// Provenance and classification metadata (author, creation date, tags).
    /// </summary>
    [JsonPropertyName("metadata")]
    public MetadataSection? Metadata { get; init; }
}

/// <summary>
/// The core AI instruction containing the task description and behavioral constraints.
/// </summary>
public sealed record InstructionSection
{
    /// <summary>
    /// System-level identity for the AI (e.g., "You are a contract analysis specialist").
    /// Rendered as the opening line of the assembled prompt.
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>
    /// The specific work the AI must perform. This is the most important field in the schema.
    /// </summary>
    [JsonPropertyName("task")]
    public required string Task { get; init; }

    /// <summary>
    /// Behavioral constraints rendered as a numbered list under "## Constraints".
    /// </summary>
    [JsonPropertyName("constraints")]
    public IReadOnlyList<string>? Constraints { get; init; }

    /// <summary>
    /// Additional context for the AI. Supports Handlebars template variables
    /// (e.g., <c>{{run.parameters.jurisdiction}}</c>).
    /// </summary>
    [JsonPropertyName("context")]
    public string? Context { get; init; }
}

/// <summary>
/// Input configuration describing document requirements, upstream dependencies, and parameters.
/// </summary>
public sealed record InputSection
{
    /// <summary>
    /// Document text configuration (required flag, max length, placeholder template).
    /// </summary>
    [JsonPropertyName("document")]
    public DocumentInput? Document { get; init; }

    /// <summary>
    /// Declares upstream node output dependencies for validation and documentation.
    /// The actual data flows through Handlebars templates at runtime.
    /// </summary>
    [JsonPropertyName("priorOutputs")]
    public IReadOnlyList<PriorOutputReference>? PriorOutputs { get; init; }

    /// <summary>
    /// Additional parameters as key-value pairs. Values support Handlebars template variables.
    /// </summary>
    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; init; }
}

/// <summary>
/// Configuration for document text input to the AI.
/// </summary>
public sealed record DocumentInput
{
    /// <summary>
    /// Whether a document is required for this prompt to execute.
    /// </summary>
    [JsonPropertyName("required")]
    public bool? Required { get; init; }

    /// <summary>
    /// Maximum character length for the document text input.
    /// </summary>
    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; init; }

    /// <summary>
    /// Handlebars template placeholder for document text injection
    /// (e.g., <c>{{document.extractedText}}</c>).
    /// </summary>
    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; init; }
}

/// <summary>
/// Declares a dependency on an upstream node's output for validation and documentation.
/// </summary>
public sealed record PriorOutputReference
{
    /// <summary>
    /// The output variable name of the upstream node (e.g., "classify").
    /// </summary>
    [JsonPropertyName("variable")]
    public required string Variable { get; init; }

    /// <summary>
    /// Specific fields referenced from the upstream output
    /// (e.g., <c>["output.documentType", "output.confidence"]</c>).
    /// </summary>
    [JsonPropertyName("fields")]
    public required IReadOnlyList<string> Fields { get; init; }

    /// <summary>
    /// Human-readable description of what this upstream dependency provides.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Output configuration defining the expected AI response structure.
/// </summary>
public sealed record OutputSection
{
    /// <summary>
    /// Output field definitions with types, constraints, and descriptions.
    /// </summary>
    [JsonPropertyName("fields")]
    public required IReadOnlyList<OutputFieldDefinition> Fields { get; init; }

    /// <summary>
    /// When true, uses Azure OpenAI JSON Schema constrained decoding to guarantee valid JSON output.
    /// When false, output instructions are rendered as text in the prompt.
    /// </summary>
    [JsonPropertyName("structuredOutput")]
    public bool StructuredOutput { get; init; }
}

/// <summary>
/// Definition of a single output field including type, constraints, and metadata.
/// </summary>
public sealed record OutputFieldDefinition
{
    /// <summary>
    /// Field name in the JSON output (e.g., "summary", "documentType").
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Field data type: <c>string</c>, <c>number</c>, <c>boolean</c>, <c>array</c>, or <c>object</c>.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Human-readable description of what this field represents.
    /// Rendered as part of the prompt's output instructions.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Fixed set of valid string values (e.g., <c>["low", "medium", "high", "critical"]</c>).
    /// </summary>
    [JsonPropertyName("enum")]
    public IReadOnlyList<string>? Enum { get; init; }

    /// <summary>
    /// Auto-inject valid values from a downstream node's field mapping options.
    /// Format: <c>"downstream:{outputVariable}.{fieldName}"</c>
    /// (e.g., <c>"downstream:update_doc.sprk_documenttype"</c>).
    /// </summary>
    [JsonPropertyName("$choices")]
    public string? Choices { get; init; }

    /// <summary>
    /// Schema for array items when <see cref="Type"/> is <c>"array"</c>.
    /// Can describe nested objects with properties.
    /// </summary>
    [JsonPropertyName("items")]
    public JsonElement? Items { get; init; }

    /// <summary>
    /// Maximum string length constraint.
    /// </summary>
    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; init; }

    /// <summary>
    /// Minimum numeric value constraint.
    /// </summary>
    [JsonPropertyName("minimum")]
    public double? Minimum { get; init; }

    /// <summary>
    /// Maximum numeric value constraint.
    /// </summary>
    [JsonPropertyName("maximum")]
    public double? Maximum { get; init; }
}

/// <summary>
/// Explicit scope references that supplement N:N scope relationships.
/// </summary>
public sealed record ScopesSection
{
    /// <summary>
    /// Skill references. Use <c>"inline"</c> to include N:N scopes,
    /// or provide explicit named references.
    /// </summary>
    [JsonPropertyName("$skills")]
    public IReadOnlyList<JsonElement>? Skills { get; init; }

    /// <summary>
    /// Knowledge references. Supports named references (<c>$ref</c>) with contextual labels
    /// and inline content.
    /// </summary>
    [JsonPropertyName("$knowledge")]
    public IReadOnlyList<KnowledgeReference>? Knowledge { get; init; }
}

/// <summary>
/// A knowledge scope reference, either a named Dataverse record or inline content.
/// </summary>
public sealed record KnowledgeReference
{
    /// <summary>
    /// Named reference to a Dataverse <c>sprk_analysisknowledge</c> record.
    /// Format: <c>"knowledge:{record-name}"</c>.
    /// </summary>
    [JsonPropertyName("$ref")]
    public string? Ref { get; init; }

    /// <summary>
    /// Contextual label controlling the section heading when rendered
    /// (e.g., "reference", "definitions", "examples").
    /// </summary>
    [JsonPropertyName("as")]
    public string? As { get; init; }

    /// <summary>
    /// Inline text content used directly without Dataverse resolution.
    /// </summary>
    [JsonPropertyName("inline")]
    public string? Inline { get; init; }
}

/// <summary>
/// A few-shot learning example with input text and expected output.
/// </summary>
public sealed record ExampleEntry
{
    /// <summary>
    /// Example input text.
    /// </summary>
    [JsonPropertyName("input")]
    public required string Input { get; init; }

    /// <summary>
    /// Expected output matching the <c>output.fields</c> schema.
    /// Stored as a flexible JSON object to support any field structure.
    /// </summary>
    [JsonPropertyName("output")]
    public required JsonElement Output { get; init; }
}

/// <summary>
/// Provenance and classification metadata for the prompt schema.
/// </summary>
public sealed record MetadataSection
{
    /// <summary>
    /// Who created this schema (username or "builder-agent").
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>
    /// Authoring level: 0 (migration), 1 (form), 2 (JSON editor), 3 (AI agent).
    /// </summary>
    [JsonPropertyName("authorLevel")]
    public int? AuthorLevel { get; init; }

    /// <summary>
    /// ISO 8601 timestamp of when this schema was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }

    /// <summary>
    /// Human-readable description of this prompt's purpose.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Classification tags for search and organization.
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>
/// The result of rendering a prompt schema (or flat text) into an executable prompt.
/// Produced by <c>PromptSchemaRenderer.Render()</c>.
/// </summary>
public sealed record RenderedPrompt
{
    /// <summary>
    /// The fully assembled prompt text ready for AI model consumption.
    /// </summary>
    public required string PromptText { get; init; }

    /// <summary>
    /// JSON Schema for Azure OpenAI constrained decoding when <c>structuredOutput</c> is true.
    /// Null when structured output is not requested.
    /// </summary>
    public JsonObject? JsonSchema { get; init; }

    /// <summary>
    /// Schema name for Azure OpenAI response format naming.
    /// </summary>
    public string? SchemaName { get; init; }

    /// <summary>
    /// Indicates whether the source prompt was JPS or flat text.
    /// </summary>
    public PromptFormat Format { get; init; }
}

/// <summary>
/// Identifies the format of the source prompt text.
/// </summary>
public enum PromptFormat
{
    /// <summary>
    /// Legacy flat text prompt (no schema structure).
    /// </summary>
    FlatText = 0,

    /// <summary>
    /// JSON Prompt Schema (JPS) format with structured sections.
    /// </summary>
    JsonPromptSchema = 1
}

/// <summary>
/// Represents a knowledge reference that has been resolved from an external store (e.g., Dataverse).
/// Used by <see cref="PromptSchemaRenderer"/> to inject resolved <c>$knowledge</c> reference
/// content into the rendered prompt.
/// </summary>
/// <param name="Name">The record name (corresponds to the <c>$ref</c> value after the <c>knowledge:</c> prefix).</param>
/// <param name="Content">The resolved knowledge content text.</param>
/// <param name="Label">Optional display label for the knowledge section heading.</param>
public sealed record ResolvedKnowledgeRef(string Name, string Content, string? Label = null);
