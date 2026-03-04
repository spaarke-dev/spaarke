using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Services.Ai.Schemas;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Renders a prompt from either flat text (legacy) or JSON Prompt Schema (JPS) format
/// into an assembled prompt string ready for AI model consumption.
/// </summary>
/// <remarks>
/// <para>
/// This is a pure function service — given inputs, it produces a deterministic
/// <see cref="RenderedPrompt"/> with no side effects beyond diagnostic logging.
/// </para>
/// <para>
/// Format detection: if the raw prompt starts with '{' and contains "$schema",
/// it is parsed as JPS; otherwise it is treated as flat text (legacy path).
/// On JPS parse failure, falls back to flat text with a warning log.
/// </para>
/// </remarks>
public sealed class PromptSchemaRenderer
{
    private readonly ILogger<PromptSchemaRenderer> _logger;

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public PromptSchemaRenderer(ILogger<PromptSchemaRenderer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Renders a prompt from raw text (flat text or JPS JSON) plus runtime context
    /// into an assembled <see cref="RenderedPrompt"/>.
    /// </summary>
    /// <param name="rawPrompt">
    /// The <c>sprk_systemprompt</c> value — either flat text or JPS JSON.
    /// </param>
    /// <param name="skillContext">
    /// Pre-built skill context from resolved scopes (prompt fragments).
    /// </param>
    /// <param name="knowledgeContext">
    /// Pre-built knowledge context from resolved scopes.
    /// </param>
    /// <param name="documentText">
    /// Extracted document text to include in the prompt.
    /// </param>
    /// <param name="templateParameters">
    /// Handlebars-like template parameters for future template substitution.
    /// </param>
    /// <param name="downstreamNodes">
    /// Downstream node info for <c>$choices</c> resolution (Phase 3, nullable for now).
    /// </param>
    /// <param name="additionalKnowledge">
    /// Pre-resolved <c>$knowledge</c> named references from the JPS <c>scopes.$knowledge</c> section.
    /// The caller resolves <c>$ref</c> values by querying Dataverse; the renderer only merges them
    /// into the assembled prompt. Pass null when no named references exist.
    /// </param>
    /// <param name="additionalSkills">
    /// Pre-resolved <c>$skill</c> named references from the JPS <c>scopes.$skills</c> section.
    /// The caller resolves <c>$ref</c> values by querying Dataverse <c>sprk_analysisskill</c>;
    /// the renderer only merges them into the assembled prompt. Pass null when no named references exist.
    /// </param>
    /// <returns>A <see cref="RenderedPrompt"/> with the assembled text and format metadata.</returns>
    public RenderedPrompt Render(
        string? rawPrompt,
        string? skillContext,
        string? knowledgeContext,
        string? documentText,
        Dictionary<string, object?>? templateParameters,
        IReadOnlyList<DownstreamNodeInfo>? downstreamNodes,
        IReadOnlyList<ResolvedKnowledgeRef>? additionalKnowledge = null,
        IReadOnlyList<ResolvedSkillRef>? additionalSkills = null)
    {
        // Step 1: Format detection
        if (string.IsNullOrWhiteSpace(rawPrompt))
        {
            return new RenderedPrompt
            {
                PromptText = string.Empty,
                Format = PromptFormat.FlatText
            };
        }

        if (IsJpsFormat(rawPrompt))
        {
            return RenderJps(rawPrompt, skillContext, knowledgeContext, documentText, downstreamNodes, additionalKnowledge, additionalSkills);
        }

        // Flat text legacy path — return rawPrompt as-is.
        // Placeholder substitution ({document}, {parameters}, etc.) stays in the handler.
        return new RenderedPrompt
        {
            PromptText = rawPrompt,
            Format = PromptFormat.FlatText
        };
    }

    /// <summary>
    /// Detects whether the raw prompt is JPS format by checking for opening brace
    /// and the presence of a "$schema" key.
    /// </summary>
    private static bool IsJpsFormat(string rawPrompt)
    {
        return rawPrompt.TrimStart().StartsWith('{') && rawPrompt.Contains("\"$schema\"");
    }

    /// <summary>
    /// Parses and renders a JPS JSON prompt into assembled prompt text.
    /// Falls back to flat text on parse failure.
    /// </summary>
    private RenderedPrompt RenderJps(
        string rawPrompt,
        string? skillContext,
        string? knowledgeContext,
        string? documentText,
        IReadOnlyList<DownstreamNodeInfo>? downstreamNodes,
        IReadOnlyList<ResolvedKnowledgeRef>? additionalKnowledge,
        IReadOnlyList<ResolvedSkillRef>? additionalSkills)
    {
        PromptSchema schema;

        try
        {
            schema = JsonSerializer.Deserialize<PromptSchema>(rawPrompt, DeserializeOptions)
                     ?? throw new JsonException("Deserialized to null");
        }
        catch (JsonException)
        {
            // ADR-015: MUST NOT log prompt content — only log diagnostic identifier
            _logger.LogWarning("Failed to parse JPS schema; falling back to flat text rendering");

            return new RenderedPrompt
            {
                PromptText = rawPrompt,
                Format = PromptFormat.FlatText
            };
        }

        // Resolve $choices references before assembling the prompt.
        // This injects enum values from downstream node field mappings.
        var resolvedFields = ResolveChoices(schema.Output?.Fields, downstreamNodes);
        if (resolvedFields != null && schema.Output != null)
        {
            schema = schema with
            {
                Output = schema.Output with { Fields = resolvedFields }
            };
        }

        var sb = new StringBuilder();

        // 1. Role (opening line)
        if (!string.IsNullOrWhiteSpace(schema.Instruction.Role))
        {
            sb.AppendLine(schema.Instruction.Role);
            sb.AppendLine();
        }

        // 2. Task
        sb.AppendLine(schema.Instruction.Task);
        sb.AppendLine();

        // 3. Constraints (numbered list)
        if (schema.Instruction.Constraints is { Count: > 0 })
        {
            sb.AppendLine("## Constraints");
            for (var i = 0; i < schema.Instruction.Constraints.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {schema.Instruction.Constraints[i]}");
            }
            sb.AppendLine();
        }

        // 4. Context
        if (!string.IsNullOrWhiteSpace(schema.Instruction.Context))
        {
            sb.AppendLine(schema.Instruction.Context);
            sb.AppendLine();
        }

        // 5. Document
        if (!string.IsNullOrWhiteSpace(documentText))
        {
            sb.AppendLine("## Document");
            sb.AppendLine();
            sb.AppendLine(documentText);
            sb.AppendLine();
        }

        // 6. Skills context (N:N scopes + $skill named references)
        RenderSkillSection(sb, skillContext, schema.Scopes?.Skills, additionalSkills);

        // 7. Knowledge context (N:N scopes + $knowledge named references)
        RenderKnowledgeSection(sb, knowledgeContext, schema.Scopes?.Knowledge, additionalKnowledge);

        // 8. Examples (few-shot)
        if (schema.Examples is { Count: > 0 })
        {
            sb.AppendLine("## Examples");
            sb.AppendLine();
            foreach (var example in schema.Examples)
            {
                sb.AppendLine($"Input: \"{example.Input}\"");
                sb.AppendLine("Expected output:");
                sb.AppendLine(JsonSerializer.Serialize(example.Output, new JsonSerializerOptions { WriteIndented = true }));
                sb.AppendLine();
            }
        }

        // 9. Output instructions
        if (schema.Output?.Fields is { Count: > 0 })
        {
            if (schema.Output.StructuredOutput)
            {
                // Structured output mode — constrained decoding handles the schema
                sb.AppendLine("Return valid JSON matching the provided schema.");
            }
            else
            {
                // Text-based output instructions
                sb.AppendLine("## Output Format");
                sb.AppendLine();
                sb.AppendLine("Return valid JSON with the following fields:");
                foreach (var field in schema.Output.Fields)
                {
                    sb.Append($"- {field.Name} ({field.Type})");

                    if (!string.IsNullOrWhiteSpace(field.Description))
                    {
                        sb.Append($": {field.Description}");
                    }

                    // Append enum values if present
                    if (field.Enum is { Count: > 0 })
                    {
                        sb.Append($" — one of: {string.Join(", ", field.Enum)}");
                    }

                    // Append numeric constraints
                    if (field.Minimum.HasValue || field.Maximum.HasValue)
                    {
                        var parts = new List<string>();
                        if (field.Minimum.HasValue)
                            parts.Add(field.Minimum.Value.ToString("G"));
                        if (field.Maximum.HasValue)
                            parts.Add(field.Maximum.Value.ToString("G"));
                        sb.Append($" ({string.Join("-", parts)})");
                    }

                    // Append max length
                    if (field.MaxLength.HasValue)
                    {
                        sb.Append($" (max {field.MaxLength.Value} chars)");
                    }

                    sb.AppendLine();
                }
            }
        }

        // Step 7: Generate JSON Schema for constrained decoding (if structuredOutput is true)
        var jsonSchema = GenerateJsonSchema(schema.Output, "prompt_response");

        return new RenderedPrompt
        {
            PromptText = sb.ToString().TrimEnd(),
            Format = PromptFormat.JsonPromptSchema,
            JsonSchema = jsonSchema,
            SchemaName = jsonSchema != null ? "prompt_response" : null
        };
    }

    /// <summary>
    /// Generates a JSON Schema Draft-07 object from the output section's field definitions.
    /// Used for Azure OpenAI constrained decoding (<c>response_format</c>) when
    /// <c>structuredOutput</c> is true.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated schema follows JSON Schema Draft-07 format with:
    /// - All fields listed in the <c>required</c> array
    /// - <c>additionalProperties: false</c> (Azure OpenAI requirement)
    /// - Type-specific constraints (enum, maxLength, minimum, maximum)
    /// - Array items schema (defaults to <c>{ "type": "string" }</c> when not specified)
    /// </para>
    /// </remarks>
    /// <param name="output">The output section containing field definitions and structuredOutput flag.</param>
    /// <param name="schemaName">A name identifier for the schema (unused in the schema itself, for caller reference).</param>
    /// <returns>
    /// A <see cref="JsonObject"/> representing the JSON Schema, or null if output is null,
    /// has no fields, or <c>structuredOutput</c> is false.
    /// </returns>
    private static JsonObject? GenerateJsonSchema(OutputSection? output, string schemaName)
    {
        if (output is null || output.Fields is not { Count: > 0 })
            return null;

        if (!output.StructuredOutput)
            return null;

        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var field in output.Fields)
        {
            properties[field.Name] = MapFieldToJsonSchema(field);
            required.Add(field.Name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };
    }

    /// <summary>
    /// Maps a single <see cref="OutputFieldDefinition"/> to its JSON Schema representation.
    /// </summary>
    /// <remarks>
    /// Type mappings:
    /// <list type="bullet">
    /// <item><c>string</c> — includes optional description, enum, maxLength</item>
    /// <item><c>number</c> — includes optional description, minimum, maximum</item>
    /// <item><c>boolean</c> — includes optional description</item>
    /// <item><c>array</c> — includes items schema (from field.Items or defaults to string)</item>
    /// <item><c>object</c> — includes optional description; uses field.Items as properties schema if provided</item>
    /// </list>
    /// </remarks>
    private static JsonObject MapFieldToJsonSchema(OutputFieldDefinition field)
    {
        var prop = new JsonObject
        {
            ["type"] = field.Type
        };

        // Description (applies to all types)
        if (!string.IsNullOrWhiteSpace(field.Description))
        {
            prop["description"] = field.Description;
        }

        // Enum values (typically for string fields, but allowed on any type)
        if (field.Enum is { Count: > 0 })
        {
            var enumArray = new JsonArray();
            foreach (var value in field.Enum)
            {
                enumArray.Add(value);
            }
            prop["enum"] = enumArray;
        }

        // Type-specific constraints
        switch (field.Type)
        {
            case "string":
                if (field.MaxLength.HasValue)
                {
                    prop["maxLength"] = field.MaxLength.Value;
                }
                break;

            case "number":
                if (field.Minimum.HasValue)
                {
                    prop["minimum"] = field.Minimum.Value;
                }
                if (field.Maximum.HasValue)
                {
                    prop["maximum"] = field.Maximum.Value;
                }
                break;

            case "array":
                if (field.Items.HasValue)
                {
                    // Items is a JsonElement — convert to JsonNode for inclusion
                    prop["items"] = JsonNode.Parse(field.Items.Value.GetRawText());
                }
                else
                {
                    // Default items schema: string array
                    prop["items"] = new JsonObject { ["type"] = "string" };
                }
                break;

            case "object":
                if (field.Items.HasValue)
                {
                    // For object type, Items contains the properties schema
                    var itemsNode = JsonNode.Parse(field.Items.Value.GetRawText());
                    if (itemsNode is JsonObject itemsObj)
                    {
                        // Merge the items object properties into the field schema
                        foreach (var kvp in itemsObj.ToArray())
                        {
                            itemsObj.Remove(kvp.Key);
                            prop[kvp.Key] = kvp.Value;
                        }
                    }
                }
                break;

                // boolean and other types: no additional constraints beyond description/enum
        }

        return prop;
    }

    /// <summary>
    /// Renders the knowledge context section by merging N:N scope knowledge, inline <c>$knowledge</c> entries,
    /// and caller-resolved <c>$knowledge</c> named references into grouped sections with appropriate headings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Knowledge is assembled from three sources (in order of precedence):
    /// <list type="number">
    /// <item>N:N scope knowledge (<paramref name="knowledgeContext"/>) — always rendered first under "Reference Knowledge"</item>
    /// <item>Inline <c>$knowledge</c> entries (<c>KnowledgeReference.Inline</c>) — rendered directly, grouped by <c>as</c> label</item>
    /// <item>Resolved <c>$ref</c> knowledge (<paramref name="additionalKnowledge"/>) — matched by name, grouped by label</item>
    /// </list>
    /// </para>
    /// <para>
    /// The <c>as</c> label controls section grouping:
    /// <c>"reference"</c> or null → "Reference Knowledge",
    /// <c>"definitions"</c> → "Definitions",
    /// <c>"examples"</c> → "Examples".
    /// Other labels are title-cased and used directly.
    /// </para>
    /// <para>
    /// ADR-015: MUST NOT log knowledge content — only diagnostic identifiers.
    /// </para>
    /// </remarks>
    private void RenderKnowledgeSection(
        StringBuilder sb,
        string? knowledgeContext,
        IReadOnlyList<KnowledgeReference>? schemaKnowledge,
        IReadOnlyList<ResolvedKnowledgeRef>? additionalKnowledge)
    {
        var hasNnContext = !string.IsNullOrWhiteSpace(knowledgeContext);
        var hasSchemaKnowledge = schemaKnowledge is { Count: > 0 };
        var hasAdditionalKnowledge = additionalKnowledge is { Count: > 0 };

        // Fast path: nothing to render
        if (!hasNnContext && !hasSchemaKnowledge && !hasAdditionalKnowledge)
            return;

        // Collect all knowledge entries grouped by their section heading.
        // N:N context always goes under "Reference Knowledge" and takes precedence.
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // 1. N:N scope knowledge (highest precedence — rendered first)
        if (hasNnContext)
        {
            var heading = LabelToHeading(null);
            GetOrCreateSection(sections, heading).Add(knowledgeContext!);
        }

        // 2. Inline $knowledge entries from the schema (no external resolution needed)
        if (hasSchemaKnowledge)
        {
            foreach (var knowledgeRef in schemaKnowledge!)
            {
                if (!string.IsNullOrWhiteSpace(knowledgeRef.Inline))
                {
                    var heading = LabelToHeading(knowledgeRef.As);
                    GetOrCreateSection(sections, heading).Add(knowledgeRef.Inline);
                }
            }
        }

        // 3. Resolved $ref knowledge (caller resolved these from Dataverse)
        if (hasAdditionalKnowledge && hasSchemaKnowledge)
        {
            // Match resolved knowledge to schema $ref entries by name.
            // Build a lookup from ref name → resolved content.
            var resolvedLookup = new Dictionary<string, ResolvedKnowledgeRef>(StringComparer.OrdinalIgnoreCase);
            foreach (var resolved in additionalKnowledge!)
            {
                // Use the Name for matching (corresponds to the record name from $ref)
                if (!resolvedLookup.ContainsKey(resolved.Name))
                {
                    resolvedLookup[resolved.Name] = resolved;
                }
            }

            foreach (var knowledgeRef in schemaKnowledge!)
            {
                if (string.IsNullOrWhiteSpace(knowledgeRef.Ref))
                    continue;

                // Parse the $ref value: "knowledge:{record-name}"
                var refName = ParseKnowledgeRefName(knowledgeRef.Ref);
                if (refName is null)
                {
                    _logger.LogWarning(
                        "$knowledge reference has invalid format (expected 'knowledge:{{name}}'); skipping");
                    continue;
                }

                if (!resolvedLookup.TryGetValue(refName, out var resolved))
                {
                    // Graceful degradation: reference could not be resolved by caller
                    _logger.LogWarning(
                        "$knowledge reference '{RefName}' was not resolved by caller; skipping",
                        refName);
                    continue;
                }

                // Use the schema's "as" label if present, otherwise use the resolved label
                var label = knowledgeRef.As ?? resolved.Label;
                var heading = LabelToHeading(label);
                GetOrCreateSection(sections, heading).Add(resolved.Content);
            }
        }
        else if (hasAdditionalKnowledge && !hasSchemaKnowledge)
        {
            // Additional knowledge provided but no schema knowledge section to match against.
            // Render all additional knowledge under their labels (or default heading).
            foreach (var resolved in additionalKnowledge!)
            {
                var heading = LabelToHeading(resolved.Label);
                GetOrCreateSection(sections, heading).Add(resolved.Content);
            }
        }

        // Render all collected sections
        foreach (var (heading, contents) in sections)
        {
            sb.AppendLine($"## {heading}");
            sb.AppendLine();
            foreach (var content in contents)
            {
                sb.AppendLine(content);
                sb.AppendLine();
            }
        }
    }

    /// <summary>
    /// Renders the skill context section by merging N:N scope skills and caller-resolved
    /// <c>$skill</c> named references into a single "Additional Analysis Instructions" section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Skills are assembled from two sources (in order of precedence):
    /// <list type="number">
    /// <item>N:N scope skills (<paramref name="skillContext"/>) — always rendered first, takes precedence</item>
    /// <item>Resolved <c>$ref</c> skills (<paramref name="additionalSkills"/>) — matched by name to schema <c>$skills</c> entries</item>
    /// </list>
    /// </para>
    /// <para>
    /// Unlike <c>$knowledge</c>, skills do not support <c>as</c> labels or inline content.
    /// All skills render under the same "Additional Analysis Instructions" heading.
    /// </para>
    /// <para>
    /// ADR-015: MUST NOT log skill content — only diagnostic identifiers.
    /// </para>
    /// </remarks>
    private void RenderSkillSection(
        StringBuilder sb,
        string? skillContext,
        IReadOnlyList<JsonElement>? schemaSkills,
        IReadOnlyList<ResolvedSkillRef>? additionalSkills)
    {
        var hasNnContext = !string.IsNullOrWhiteSpace(skillContext);
        var hasSchemaSkills = schemaSkills is { Count: > 0 };
        var hasAdditionalSkills = additionalSkills is { Count: > 0 };

        // Fast path: nothing to render
        if (!hasNnContext && !hasAdditionalSkills)
            return;

        var fragments = new List<string>();

        // 1. N:N scope skills (highest precedence — rendered first)
        if (hasNnContext)
        {
            fragments.Add(skillContext!);
        }

        // 2. Resolved $ref skills (caller resolved these from Dataverse)
        if (hasAdditionalSkills && hasSchemaSkills)
        {
            // Match resolved skills to schema $ref entries by name.
            var resolvedLookup = new Dictionary<string, ResolvedSkillRef>(StringComparer.OrdinalIgnoreCase);
            foreach (var resolved in additionalSkills!)
            {
                if (!resolvedLookup.ContainsKey(resolved.Name))
                {
                    resolvedLookup[resolved.Name] = resolved;
                }
            }

            foreach (var skillElement in schemaSkills!)
            {
                // Schema $skills entries can be strings ("inline") or objects ({"$ref": "skill:name"})
                if (skillElement.ValueKind == JsonValueKind.String)
                {
                    // "inline" marker — N:N scopes already handled above
                    continue;
                }

                if (skillElement.ValueKind != JsonValueKind.Object)
                    continue;

                if (!skillElement.TryGetProperty("$ref", out var refProp) ||
                    refProp.ValueKind != JsonValueKind.String)
                    continue;

                var refValue = refProp.GetString();
                if (string.IsNullOrWhiteSpace(refValue))
                    continue;

                var refName = ParseSkillRefName(refValue);
                if (refName is null)
                {
                    _logger.LogWarning(
                        "$skill reference has invalid format (expected 'skill:{{name}}'); skipping");
                    continue;
                }

                if (!resolvedLookup.TryGetValue(refName, out var resolved))
                {
                    // Graceful degradation: reference could not be resolved by caller
                    _logger.LogWarning(
                        "$skill reference '{RefName}' was not resolved by caller; skipping",
                        refName);
                    continue;
                }

                fragments.Add(resolved.PromptFragment);
            }
        }
        else if (hasAdditionalSkills && !hasSchemaSkills)
        {
            // Additional skills provided but no schema skills section to match against.
            // Render all additional skills directly.
            foreach (var resolved in additionalSkills!)
            {
                fragments.Add(resolved.PromptFragment);
            }
        }

        // Render all collected skill fragments under a single heading
        if (fragments.Count > 0)
        {
            sb.AppendLine("## Additional Analysis Instructions");
            sb.AppendLine();
            foreach (var fragment in fragments)
            {
                sb.AppendLine(fragment);
                sb.AppendLine();
            }
        }
    }

    /// <summary>
    /// Maps a contextual <c>as</c> label to a section heading string.
    /// </summary>
    private static string LabelToHeading(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "Reference Knowledge";

        return label.ToLowerInvariant() switch
        {
            "reference" => "Reference Knowledge",
            "definitions" => "Definitions",
            "examples" => "Examples",
            _ => char.ToUpperInvariant(label[0]) + label[1..] // Title-case unknown labels
        };
    }

    /// <summary>
    /// Parses the record name from a <c>$ref</c> value with format <c>"knowledge:{record-name}"</c>.
    /// </summary>
    /// <returns>The record name, or null if the format is invalid.</returns>
    private static string? ParseKnowledgeRefName(string refValue)
    {
        const string prefix = "knowledge:";
        if (!refValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var name = refValue[prefix.Length..];
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Parses the record name from a <c>$ref</c> value with format <c>"skill:{record-name}"</c>.
    /// </summary>
    /// <returns>The record name, or null if the format is invalid.</returns>
    private static string? ParseSkillRefName(string refValue)
    {
        const string prefix = "skill:";
        if (!refValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var name = refValue[prefix.Length..];
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Gets or creates a section entry in the dictionary for the given heading.
    /// </summary>
    private static List<string> GetOrCreateSection(Dictionary<string, List<string>> sections, string heading)
    {
        if (!sections.TryGetValue(heading, out var list))
        {
            list = new List<string>();
            sections[heading] = list;
        }
        return list;
    }

    /// <summary>
    /// Resolves <c>$choices</c> references on output fields by looking up downstream node
    /// field mappings and injecting valid option keys into each field's <see cref="OutputFieldDefinition.Enum"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference format: <c>"downstream:{outputVariable}.{fieldName}"</c>
    /// (e.g., <c>"downstream:update_doc.sprk_documenttype"</c>).
    /// </para>
    /// <para>
    /// The downstream node's <c>ConfigJson</c> is expected to contain a <c>fieldMappings</c> array
    /// where each entry has <c>field</c>, <c>type</c>, and <c>options</c> properties. The <c>options</c>
    /// object maps display keys (strings) to Dataverse option-set values (integers).
    /// Only the display keys are extracted and injected as enum values.
    /// </para>
    /// <para>
    /// Graceful degradation: if a <c>$choices</c> reference cannot be resolved (missing downstream
    /// node, missing field, invalid JSON), a warning is logged and the field is returned unchanged.
    /// </para>
    /// </remarks>
    /// <param name="fields">Output field definitions, some of which may have <c>$choices</c> references.</param>
    /// <param name="downstreamNodes">Available downstream node info for resolution.</param>
    /// <returns>
    /// A new list of fields with <c>$choices</c> resolved to <c>enum</c> values, or null if no fields exist.
    /// Fields without <c>$choices</c> are returned unchanged.
    /// </returns>
    private IReadOnlyList<OutputFieldDefinition>? ResolveChoices(
        IReadOnlyList<OutputFieldDefinition>? fields,
        IReadOnlyList<DownstreamNodeInfo>? downstreamNodes)
    {
        if (fields is null or { Count: 0 })
            return fields;

        // Fast path: if no fields have $choices, return as-is
        var hasChoices = false;
        foreach (var field in fields)
        {
            if (!string.IsNullOrWhiteSpace(field.Choices))
            {
                hasChoices = true;
                break;
            }
        }

        if (!hasChoices)
            return fields;

        if (downstreamNodes is null or { Count: 0 })
        {
            _logger.LogWarning(
                "$choices references found in output fields but no downstream nodes provided; choices will not be resolved");
            return fields;
        }

        var resolved = new List<OutputFieldDefinition>(fields.Count);

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Choices))
            {
                resolved.Add(field);
                continue;
            }

            var choiceValues = ResolveChoiceReference(field.Choices, field.Name, downstreamNodes);
            if (choiceValues != null)
            {
                // Merge: $choices-resolved values replace any existing enum.
                // If the field already has enum values, $choices takes precedence
                // (single source of truth from the downstream node).
                resolved.Add(field with { Enum = choiceValues });
            }
            else
            {
                // Graceful degradation: keep the field unchanged
                resolved.Add(field);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Resolves a single <c>$choices</c> reference string to an array of valid option keys.
    /// </summary>
    /// <param name="choicesRef">
    /// The $choices reference value (e.g., <c>"downstream:update_doc.sprk_documenttype"</c>).
    /// </param>
    /// <param name="fieldName">The field name for diagnostic logging.</param>
    /// <param name="downstreamNodes">Available downstream nodes to search.</param>
    /// <returns>Array of option keys, or null if resolution failed.</returns>
    private string[]? ResolveChoiceReference(
        string choicesRef,
        string fieldName,
        IReadOnlyList<DownstreamNodeInfo> downstreamNodes)
    {
        // Parse reference: "downstream:{outputVariable}.{fieldName}"
        const string prefix = "downstream:";
        if (!choicesRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "$choices reference on field '{FieldName}' does not start with 'downstream:'; skipping resolution",
                fieldName);
            return null;
        }

        var refBody = choicesRef[prefix.Length..];
        var dotIndex = refBody.IndexOf('.');
        if (dotIndex <= 0 || dotIndex >= refBody.Length - 1)
        {
            _logger.LogWarning(
                "$choices reference on field '{FieldName}' has invalid format (expected 'downstream:{{outputVariable}}.{{field}}'); skipping resolution",
                fieldName);
            return null;
        }

        var outputVariable = refBody[..dotIndex];
        var targetFieldName = refBody[(dotIndex + 1)..];

        // Find matching downstream node by output variable
        DownstreamNodeInfo? matchingNode = null;
        foreach (var node in downstreamNodes)
        {
            if (string.Equals(node.OutputVariable, outputVariable, StringComparison.OrdinalIgnoreCase))
            {
                matchingNode = node;
                break;
            }
        }

        if (matchingNode is null)
        {
            _logger.LogWarning(
                "$choices reference on field '{FieldName}' refers to downstream node '{OutputVariable}' which was not found; skipping resolution",
                fieldName, outputVariable);
            return null;
        }

        if (string.IsNullOrWhiteSpace(matchingNode.ConfigJson))
        {
            _logger.LogWarning(
                "$choices reference on field '{FieldName}' matched downstream node '{OutputVariable}' but it has no ConfigJson; skipping resolution",
                fieldName, outputVariable);
            return null;
        }

        // Parse ConfigJson to extract fieldMappings options
        return ExtractChoiceKeysFromConfig(matchingNode.ConfigJson, targetFieldName, fieldName, outputVariable);
    }

    /// <summary>
    /// Extracts option keys from a downstream node's ConfigJson fieldMappings.
    /// </summary>
    /// <remarks>
    /// Expected ConfigJson structure:
    /// <code>
    /// {
    ///   "fieldMappings": [
    ///     {
    ///       "field": "sprk_documenttype",
    ///       "type": "choice",
    ///       "options": { "contract": 100000000, "invoice": 100000001 }
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </remarks>
    private string[]? ExtractChoiceKeysFromConfig(
        string configJson,
        string targetFieldName,
        string fieldName,
        string outputVariable)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("fieldMappings", out var fieldMappings) ||
                fieldMappings.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "$choices resolution for field '{FieldName}': downstream node '{OutputVariable}' ConfigJson has no 'fieldMappings' array",
                    fieldName, outputVariable);
                return null;
            }

            foreach (var mapping in fieldMappings.EnumerateArray())
            {
                if (!mapping.TryGetProperty("field", out var fieldProp))
                    continue;

                var mappingFieldName = fieldProp.GetString();
                if (!string.Equals(mappingFieldName, targetFieldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Found the matching field mapping — extract option keys
                if (!mapping.TryGetProperty("options", out var options) ||
                    options.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogWarning(
                        "$choices resolution for field '{FieldName}': downstream field '{TargetField}' on '{OutputVariable}' has no 'options' object",
                        fieldName, targetFieldName, outputVariable);
                    return null;
                }

                var keys = new List<string>();
                foreach (var prop in options.EnumerateObject())
                {
                    keys.Add(prop.Name);
                }

                if (keys.Count == 0)
                {
                    _logger.LogWarning(
                        "$choices resolution for field '{FieldName}': downstream field '{TargetField}' on '{OutputVariable}' has empty options",
                        fieldName, targetFieldName, outputVariable);
                    return null;
                }

                _logger.LogDebug(
                    "$choices resolved for field '{FieldName}': {ChoiceCount} values from '{OutputVariable}.{TargetField}'",
                    fieldName, keys.Count, outputVariable, targetFieldName);

                return keys.ToArray();
            }

            _logger.LogWarning(
                "$choices resolution for field '{FieldName}': no fieldMapping with field '{TargetField}' found in downstream node '{OutputVariable}'",
                fieldName, targetFieldName, outputVariable);
            return null;
        }
        catch (JsonException)
        {
            // ADR-015: MUST NOT log config content — only log identifiers
            _logger.LogWarning(
                "$choices resolution for field '{FieldName}': failed to parse ConfigJson from downstream node '{OutputVariable}'",
                fieldName, outputVariable);
            return null;
        }
    }
}

/// <summary>
/// Describes a downstream node for <c>$choices</c> resolution.
/// Contains the output variable name and optional configuration JSON
/// that includes field mappings with option sets.
/// </summary>
/// <param name="OutputVariable">
/// The output variable name of the downstream node (e.g., "update_doc").
/// </param>
/// <param name="ConfigJson">
/// The downstream node's configuration JSON containing field mappings and options.
/// </param>
public sealed record DownstreamNodeInfo(
    string OutputVariable,
    string? ConfigJson
);

/// <summary>
/// A pre-resolved <c>$knowledge</c> named reference ready for prompt assembly.
/// The caller resolves these from Dataverse by querying <c>sprk_analysisknowledge</c>
/// by <c>sprk_name</c> and loading <c>sprk_content</c>.
/// </summary>
/// <remarks>
/// <para>
/// This record is produced by the orchestration layer (e.g., AiAnalysisNodeExecutor)
/// and consumed by <see cref="PromptSchemaRenderer"/> to merge named knowledge
/// references into the assembled prompt alongside N:N scope knowledge.
/// </para>
/// <para>
/// ADR-015: Content must not be logged; only <see cref="Name"/> may appear in diagnostics.
/// </para>
/// </remarks>
/// <param name="Name">
/// The record name from the <c>$ref</c> value (e.g., "standard-contract-clauses"
/// from <c>"knowledge:standard-contract-clauses"</c>).
/// Used to match against schema <c>KnowledgeReference.$ref</c> entries.
/// </param>
/// <param name="Content">
/// The knowledge content (<c>sprk_content</c>) loaded from Dataverse.
/// </param>
/// <param name="Label">
/// Optional contextual label from the schema's <c>"as"</c> field
/// (e.g., "reference", "definitions", "examples").
/// Controls which section heading the content is rendered under.
/// Null defaults to "Reference Knowledge".
/// </param>
public sealed record ResolvedKnowledgeRef(
    string Name,
    string Content,
    string? Label
);

/// <summary>
/// A pre-resolved <c>$skill</c> named reference ready for prompt assembly.
/// The caller resolves these from Dataverse by querying <c>sprk_analysisskill</c>
/// by <c>sprk_name</c> and loading <c>sprk_promptfragment</c>.
/// </summary>
/// <remarks>
/// <para>
/// This record is produced by the orchestration layer (e.g., AiAnalysisNodeExecutor)
/// and consumed by <see cref="PromptSchemaRenderer"/> to merge named skill
/// references into the assembled prompt alongside N:N scope skills.
/// </para>
/// <para>
/// ADR-015: Content must not be logged; only <see cref="Name"/> may appear in diagnostics.
/// </para>
/// </remarks>
/// <param name="Name">
/// The record name from the <c>$ref</c> value (e.g., "liability-analysis"
/// from <c>"skill:liability-analysis"</c>).
/// Used to match against schema <c>ScopesSection.$skills</c> entries.
/// </param>
/// <param name="PromptFragment">
/// The skill prompt fragment (<c>sprk_promptfragment</c>) loaded from Dataverse.
/// </param>
public sealed record ResolvedSkillRef(string Name, string PromptFragment);
