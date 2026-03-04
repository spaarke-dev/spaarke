using System.Text.Json;
using Sprk.Bff.Api.Services.Ai.Schemas;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Merges a base <see cref="PromptSchema"/> with a node-level override.
/// </summary>
/// <remarks>
/// <para>
/// Default behavior: arrays concatenate, scalars replace.
/// </para>
/// <para>
/// Directive: if the override's constraints array contains the special marker string
/// <c>"__replace"</c>, the entire constraints array is replaced (marker is stripped from output).
/// Likewise, if the override's output fields array contains a field named <c>"__replace"</c>,
/// the entire output fields array is replaced (marker field is stripped).
/// </para>
/// <para>
/// This is a pure static utility — no DI needed.
/// </para>
/// </remarks>
public static class PromptSchemaOverrideMerger
{
    /// <summary>
    /// Special marker string that, when present in an override's constraints or
    /// output fields, triggers full replacement of that section instead of concatenation.
    /// </summary>
    internal const string ReplaceDirective = "__replace";

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Merges the base schema with a node-level override.
    /// Returns the base unchanged when override is null.
    /// </summary>
    /// <param name="baseSchema">The base <see cref="PromptSchema"/> from the Action's system prompt.</param>
    /// <param name="schemaOverride">
    /// An optional override from the node's <c>ConfigJson.promptSchemaOverride</c>.
    /// May be null, in which case the base schema is returned as-is.
    /// </param>
    /// <returns>A new <see cref="PromptSchema"/> with merged values.</returns>
    public static PromptSchema Merge(PromptSchema baseSchema, PromptSchema? schemaOverride)
    {
        if (schemaOverride is null)
            return baseSchema;

        // Merge instruction section
        var mergedInstruction = MergeInstruction(baseSchema.Instruction, schemaOverride.Instruction);

        // Merge output section
        var mergedOutput = MergeOutput(baseSchema.Output, schemaOverride.Output);

        // Merge input section (override replaces if present)
        var mergedInput = schemaOverride.Input ?? baseSchema.Input;

        // Merge examples (concatenate by default)
        var mergedExamples = MergeExamples(baseSchema.Examples, schemaOverride.Examples);

        // Merge scopes (override replaces if present)
        var mergedScopes = schemaOverride.Scopes ?? baseSchema.Scopes;

        // Merge metadata (override replaces if present)
        var mergedMetadata = schemaOverride.Metadata ?? baseSchema.Metadata;

        return baseSchema with
        {
            Instruction = mergedInstruction,
            Input = mergedInput,
            Output = mergedOutput,
            Scopes = mergedScopes,
            Examples = mergedExamples,
            Metadata = mergedMetadata
        };
    }

    /// <summary>
    /// Extracts a <see cref="PromptSchema"/> override from a node's ConfigJson.
    /// Returns null if ConfigJson is missing, has no <c>promptSchemaOverride</c> property,
    /// or the override cannot be parsed.
    /// </summary>
    /// <remarks>
    /// Since <see cref="PromptSchema"/> and <see cref="InstructionSection"/> have <c>required</c>
    /// properties (<c>instruction</c> and <c>task</c>), this method injects sentinel defaults
    /// for any missing required fields before deserialization. Callers should treat empty/sentinel
    /// values as "not provided" (the merge logic already handles this via null/empty checks).
    /// </remarks>
    /// <param name="configJson">The node's raw ConfigJson string.</param>
    /// <returns>
    /// The parsed <see cref="PromptSchema"/> override, or null if not present or invalid.
    /// </returns>
    public static PromptSchema? ExtractOverride(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("promptSchemaOverride", out var overrideElement))
                return null;

            if (overrideElement.ValueKind != JsonValueKind.Object)
                return null;

            // The override may have a partial instruction section (e.g., only constraints,
            // no task). Since PromptSchema.Instruction and InstructionSection.Task are
            // required, we normalize the JSON before deserializing to fill in defaults.
            var normalized = NormalizeOverrideJson(overrideElement);

            return JsonSerializer.Deserialize<PromptSchema>(
                normalized,
                DeserializeOptions);
        }
        catch (JsonException)
        {
            // Graceful degradation: if the override can't be parsed, skip it.
            return null;
        }
    }

    /// <summary>
    /// Normalizes an override JSON element to satisfy <c>required</c> properties.
    /// Ensures <c>instruction</c> exists and has a <c>task</c> field (defaults to empty string).
    /// The empty <c>task</c> is treated as "not provided" by merge logic.
    /// </summary>
    private static string NormalizeOverrideJson(JsonElement overrideElement)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            var hasInstruction = false;

            foreach (var prop in overrideElement.EnumerateObject())
            {
                if (prop.Name == "instruction")
                {
                    hasInstruction = true;
                    writer.WritePropertyName("instruction");
                    WriteNormalizedInstruction(writer, prop.Value);
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            // If no instruction section at all, add a minimal one
            if (!hasInstruction)
            {
                writer.WritePropertyName("instruction");
                writer.WriteStartObject();
                writer.WriteString("task", "");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Writes an instruction object, ensuring it has a <c>task</c> property.
    /// </summary>
    private static void WriteNormalizedInstruction(Utf8JsonWriter writer, JsonElement instruction)
    {
        if (instruction.ValueKind != JsonValueKind.Object)
        {
            // Not an object — write a minimal instruction
            writer.WriteStartObject();
            writer.WriteString("task", "");
            writer.WriteEndObject();
            return;
        }

        writer.WriteStartObject();

        var hasTask = false;
        foreach (var prop in instruction.EnumerateObject())
        {
            if (prop.Name == "task")
                hasTask = true;

            prop.WriteTo(writer);
        }

        if (!hasTask)
        {
            writer.WriteString("task", "");
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Merges instruction sections. Scalar fields (role, task, context) are replaced
    /// when present in the override. Constraints are concatenated by default unless
    /// the <c>__replace</c> directive is present.
    /// </summary>
    private static InstructionSection MergeInstruction(
        InstructionSection baseInstruction,
        InstructionSection? overrideInstruction)
    {
        if (overrideInstruction is null)
            return baseInstruction;

        // Scalar fields: replace if override provides a value
        var mergedRole = overrideInstruction.Role ?? baseInstruction.Role;
        var mergedTask = !string.IsNullOrWhiteSpace(overrideInstruction.Task)
            ? overrideInstruction.Task
            : baseInstruction.Task;
        var mergedContext = overrideInstruction.Context ?? baseInstruction.Context;

        // Constraints: check for __replace directive
        var mergedConstraints = MergeConstraints(
            baseInstruction.Constraints,
            overrideInstruction.Constraints);

        return baseInstruction with
        {
            Role = mergedRole,
            Task = mergedTask,
            Context = mergedContext,
            Constraints = mergedConstraints
        };
    }

    /// <summary>
    /// Merges constraint arrays. If the override contains <c>"__replace"</c>, the base
    /// constraints are fully replaced (the marker is stripped). Otherwise, arrays are concatenated.
    /// </summary>
    private static IReadOnlyList<string>? MergeConstraints(
        IReadOnlyList<string>? baseConstraints,
        IReadOnlyList<string>? overrideConstraints)
    {
        if (overrideConstraints is null or { Count: 0 })
            return baseConstraints;

        if (baseConstraints is null or { Count: 0 })
            return StripReplaceDirective(overrideConstraints);

        // Check for __replace directive
        if (HasReplaceDirective(overrideConstraints))
        {
            // Full replacement: return override constraints without the marker
            return StripReplaceDirective(overrideConstraints);
        }

        // Default: concatenate base + override
        var merged = new List<string>(baseConstraints.Count + overrideConstraints.Count);
        merged.AddRange(baseConstraints);
        merged.AddRange(overrideConstraints);
        return merged;
    }

    /// <summary>
    /// Merges output sections. Fields are concatenated by default unless
    /// the override contains a field named <c>"__replace"</c>.
    /// Scalar properties (structuredOutput) are replaced when present in override.
    /// </summary>
    private static OutputSection? MergeOutput(
        OutputSection? baseOutput,
        OutputSection? overrideOutput)
    {
        if (overrideOutput is null)
            return baseOutput;

        if (baseOutput is null)
            return StripReplaceFieldDirective(overrideOutput);

        // Merge fields
        var mergedFields = MergeOutputFields(baseOutput.Fields, overrideOutput.Fields);

        // structuredOutput: override takes precedence if the override has an output section
        return baseOutput with
        {
            Fields = mergedFields,
            StructuredOutput = overrideOutput.StructuredOutput || baseOutput.StructuredOutput
        };
    }

    /// <summary>
    /// Merges output field arrays. If override contains a field named <c>"__replace"</c>,
    /// the base fields are fully replaced (the marker field is stripped). Otherwise, concatenated.
    /// </summary>
    private static IReadOnlyList<OutputFieldDefinition> MergeOutputFields(
        IReadOnlyList<OutputFieldDefinition> baseFields,
        IReadOnlyList<OutputFieldDefinition> overrideFields)
    {
        if (overrideFields is { Count: 0 })
            return baseFields;

        if (baseFields is { Count: 0 })
            return StripReplaceFieldFromList(overrideFields);

        // Check for __replace directive (a field with name "__replace")
        if (HasReplaceFieldDirective(overrideFields))
        {
            return StripReplaceFieldFromList(overrideFields);
        }

        // Default: concatenate base + override
        var merged = new List<OutputFieldDefinition>(baseFields.Count + overrideFields.Count);
        merged.AddRange(baseFields);
        merged.AddRange(overrideFields);
        return merged;
    }

    /// <summary>
    /// Merges example arrays by concatenation (no __replace support for examples).
    /// </summary>
    private static IReadOnlyList<ExampleEntry>? MergeExamples(
        IReadOnlyList<ExampleEntry>? baseExamples,
        IReadOnlyList<ExampleEntry>? overrideExamples)
    {
        if (overrideExamples is null or { Count: 0 })
            return baseExamples;

        if (baseExamples is null or { Count: 0 })
            return overrideExamples;

        var merged = new List<ExampleEntry>(baseExamples.Count + overrideExamples.Count);
        merged.AddRange(baseExamples);
        merged.AddRange(overrideExamples);
        return merged;
    }

    /// <summary>
    /// Checks whether a constraints list contains the <c>"__replace"</c> directive string.
    /// </summary>
    private static bool HasReplaceDirective(IReadOnlyList<string> constraints)
    {
        foreach (var constraint in constraints)
        {
            if (string.Equals(constraint, ReplaceDirective, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns a new list with the <c>"__replace"</c> marker string removed.
    /// </summary>
    private static IReadOnlyList<string> StripReplaceDirective(IReadOnlyList<string> constraints)
    {
        var filtered = new List<string>(constraints.Count);
        foreach (var constraint in constraints)
        {
            if (!string.Equals(constraint, ReplaceDirective, StringComparison.Ordinal))
                filtered.Add(constraint);
        }
        return filtered;
    }

    /// <summary>
    /// Checks whether an output field list contains a field named <c>"__replace"</c>.
    /// </summary>
    private static bool HasReplaceFieldDirective(IReadOnlyList<OutputFieldDefinition> fields)
    {
        foreach (var field in fields)
        {
            if (string.Equals(field.Name, ReplaceDirective, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns a new list with the <c>"__replace"</c> marker field removed.
    /// </summary>
    private static IReadOnlyList<OutputFieldDefinition> StripReplaceFieldFromList(
        IReadOnlyList<OutputFieldDefinition> fields)
    {
        var filtered = new List<OutputFieldDefinition>(fields.Count);
        foreach (var field in fields)
        {
            if (!string.Equals(field.Name, ReplaceDirective, StringComparison.Ordinal))
                filtered.Add(field);
        }
        return filtered;
    }

    /// <summary>
    /// Strips the <c>"__replace"</c> marker field from an OutputSection, if present.
    /// </summary>
    private static OutputSection StripReplaceFieldDirective(OutputSection output)
    {
        if (!HasReplaceFieldDirective(output.Fields))
            return output;

        return output with { Fields = StripReplaceFieldFromList(output.Fields) };
    }
}
