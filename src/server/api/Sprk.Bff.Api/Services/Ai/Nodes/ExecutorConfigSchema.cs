// R7 spaarke-ai-platform-unification-r7 — Typed config schema DTOs (task 031 / FR-16)
//
// Drives Playbook Builder canvas form rendering (Wave 8 FR-23). Each INodeExecutor
// declares the shape of `sprk_configjson` it accepts via GetConfigSchema(); a Wave 3
// BFF endpoint (task 033, GET /api/ai/playbook-builder/executor-config-schemas) aggregates
// schemas across all registered executors and serves them to PlaybookBuilder, replacing
// free-form JSON editing with typed forms.
//
// Design authority: projects/spaarke-ai-platform-unification-r7/notes/spikes/getconfigschema-design.md
//   §2 — DTO shape (this file)
//   §3 — Wire/TypeScript equivalent
//   §4 — Placeholder/empty schema convention (this file's Empty() factory)
//   §6 — Endpoint contract (consumer; task 033 implements)
//   §7 — Forward-compat strategy (additive properties; append enum values)
//
// References: ADR-010 DI Minimalism (no IConfigSchemaProvider abstraction — method lives
//             on INodeExecutor directly); ADR-013 BFF AI Architecture (interface change is
//             additive, NOT a new dispatch axis); ADR-029 BFF Publish Hygiene (DTO + enum
//             additions are sub-KB IL — verified at Wave 3 task 036).

using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Typed configuration schema returned by <see cref="INodeExecutor.GetConfigSchema"/>.
/// Drives Playbook Builder canvas form rendering (Wave 8 FR-23) by declaring the shape
/// of <c>sprk_configjson</c> the executor accepts at runtime.
/// </summary>
/// <remarks>
/// <para>
/// The schema is the <b>maker contract</b> (what fields the canvas presents). The
/// <c>ConfigJson</c> payload is the <b>runtime contract</b> (what executors deserialize).
/// They MUST stay aligned — task 032 PR template includes a checklist item "schema field
/// names match config record <c>[JsonPropertyName]</c> attributes" to keep them locked.
/// </para>
/// <para>
/// Wire example (AiCompletion) — see design doc §3 / §8 for the full payload.
/// </para>
/// </remarks>
public sealed record ExecutorConfigSchema(
    [property: JsonPropertyName("executorTypeName")] string ExecutorTypeName,
    [property: JsonPropertyName("executorTypeValue")] int ExecutorTypeValue,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("fields")] IReadOnlyList<ConfigSchemaField> Fields)
{
    /// <summary>
    /// Builds an empty placeholder schema for executors with no maker-editable config
    /// (Start, ReturnResponse, DeliverOutput, Condition, Parallel, Wait, etc. — see design §4).
    /// Wire shape: <c>{ "executorTypeName": "...", "executorTypeValue": N, "description": "...", "fields": [] }</c>.
    /// </summary>
    /// <param name="type">The ExecutorType to embed in the schema's name + value fields.</param>
    /// <param name="description">Human-readable description displayed in the canvas as the "no configuration required" hint.</param>
    /// <remarks>
    /// Canvas behavior when <c>fields.length === 0</c>: render no config form (collapsed
    /// config section with the description as the empty-state hint). The empty array IS
    /// the contract — distinguishes "placeholder" from "we forgot to define schema."
    /// </remarks>
    public static ExecutorConfigSchema Empty(ExecutorType type, string description) =>
        new(type.ToString(), (int)type, description, Array.Empty<ConfigSchemaField>());
}

/// <summary>
/// Single field in an executor's config schema. JSON-serialized into the schema endpoint
/// payload (task 033) for canvas form rendering. See design doc §2 / §3 for wire contract.
/// </summary>
/// <param name="Name">
/// The <c>sprk_configjson</c> property name (camelCase by convention). MUST match the
/// <c>[JsonPropertyName]</c> attribute on the executor's private config record (see remarks
/// on <see cref="ExecutorConfigSchema"/>).
/// </param>
/// <param name="Type">
/// Allowed kind of the field — drives the canvas form widget chosen (text input, number
/// input, checkbox, JSON sub-editor, enum dropdown, etc.). See <see cref="SchemaFieldType"/>.
/// </param>
/// <param name="Required">
/// True if the canvas MUST require a value before saving. Should match the executor's
/// <c>Validate()</c> contract — required-here-but-not-validated would silently allow nulls.
/// </param>
/// <param name="Description">
/// Plain-text description shown as tooltip / help text on the canvas form widget. Markdown
/// NOT supported in v1 (design doc §10, deferred decision — XSS surface vs. marginal UX gain).
/// </param>
/// <param name="Default">
/// Optional default value rendered in the canvas form widget when the user adds the node.
/// Serializes as the raw JSON value (string, number, boolean, object, array, or null).
/// </param>
/// <param name="EnumValues">
/// Optional allowed string values — populated ONLY when <see cref="Type"/> is
/// <see cref="SchemaFieldType.Enum"/>. Drives canvas dropdown options.
/// </param>
public sealed record ConfigSchemaField(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] SchemaFieldType Type,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("default")] object? Default = null,
    [property: JsonPropertyName("enumValues")] IReadOnlyList<string>? EnumValues = null);

/// <summary>
/// Allowed kinds of config fields in an <see cref="ExecutorConfigSchema"/>. Forward-compat:
/// new kinds MUST be appended at the end — never inserted in the middle, since the numeric
/// values are part of the wire contract (design doc §7).
/// </summary>
/// <remarks>
/// <para>
/// JSON-serialized as the string name (e.g., <c>"String"</c>, <c>"Number"</c>) via
/// <see cref="JsonStringEnumConverter"/> so the TypeScript canvas side reads strings.
/// </para>
/// <para>
/// <see cref="Object"/> signals to the canvas "render a JSON sub-editor here" — preferred
/// over recursive typed forms for arbitrarily shaped nested payloads like JPS overrides
/// (design doc §5).
/// </para>
/// <para>
/// Canvas behavior on unknown values: read-only JSON view + "unsupported field type — update
/// Playbook Builder Code Page" hint (mirrors spec FR-27 "unknown executor type" warning).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchemaFieldType
{
    /// <summary>Single-line text input on the canvas.</summary>
    String = 0,

    /// <summary>Numeric input (integer or float) on the canvas.</summary>
    Number = 1,

    /// <summary>Checkbox on the canvas.</summary>
    Boolean = 2,

    /// <summary>JSON sub-editor (Monaco-style) on the canvas — used for arbitrarily shaped nested payloads.</summary>
    Object = 3,

    /// <summary>JSON array sub-editor on the canvas.</summary>
    Array = 4,

    /// <summary>Dropdown on the canvas, populated from <see cref="ConfigSchemaField.EnumValues"/>.</summary>
    Enum = 5
}
