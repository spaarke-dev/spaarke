namespace Sprk.Bff.Api.Services.Ai.Insights.Prompts;

/// <summary>
/// Loads versioned Insights prompt templates + their JSON schemas from disk. The prompt
/// files ship as <c>Content</c> in the BFF csproj (<c>CopyToOutputDirectory=PreserveNewest</c>)
/// so they're read from the deployed app's output directory at runtime.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a service rather than direct File.ReadAllText calls</b>: file IO is the kind
/// of thing that benefits from a single chokepoint with structured logging + caching.
/// The interface seam also makes the orchestrator unit-testable without a real prompts
/// directory.
/// </para>
/// <para>
/// <b>Caching</b>: prompts are read once + cached in memory (they don't change between
/// process restarts). Implementations are Singleton-safe.
/// </para>
/// </remarks>
public interface IInsightsPromptLoader
{
    /// <summary>
    /// Get a prompt template by basename (e.g., <c>"classification.v1"</c>) plus the
    /// matching JSON schema (loaded from <c>{basename}.schema.json</c>).
    /// </summary>
    /// <param name="basename">Template basename without extension (e.g., <c>"classification.v1"</c>).</param>
    /// <returns>The loaded prompt + schema.</returns>
    /// <exception cref="FileNotFoundException">When either the .txt or .schema.json file is missing.</exception>
    InsightsPrompt Get(string basename);
}

/// <summary>
/// A loaded prompt template + its JSON schema. Both come from disk (the .txt and
/// .schema.json files in <c>Services/Ai/Insights/Prompts/</c>).
/// </summary>
/// <param name="Template">The prompt template text (the .txt file contents).</param>
/// <param name="SchemaJson">The JSON schema as a raw string (the .schema.json file contents).</param>
/// <param name="SchemaName">A short schema name suitable for
/// <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/> (basename with dots replaced by underscores).</param>
public sealed record InsightsPrompt(string Template, string SchemaJson, string SchemaName);
