using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Insights.Prompts;

/// <summary>
/// Default <see cref="IInsightsPromptLoader"/>. Reads prompt + schema from disk
/// (relative to <see cref="AppContext.BaseDirectory"/>) and caches them per-basename.
/// </summary>
/// <remarks>
/// <para>
/// <b>Path convention</b>: <c>{BaseDirectory}/Services/Ai/Insights/Prompts/{basename}.txt</c>
/// + <c>{BaseDirectory}/Services/Ai/Insights/Prompts/{basename}.schema.json</c>. The
/// csproj copies these files to the output dir at build time
/// (<c>&lt;Content Include="Services\Ai\Insights\Prompts\*.txt"...&gt;</c>).
/// </para>
/// <para>
/// <b>Caching</b>: prompts are cached forever (process-lifetime). Restart the app to
/// pick up changes — matches the deploy model. Cache uses <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// for safe concurrent first-access.
/// </para>
/// </remarks>
internal sealed class InsightsPromptLoader : IInsightsPromptLoader
{
    private const string PromptsRelativePath = "Services/Ai/Insights/Prompts";

    private readonly ConcurrentDictionary<string, InsightsPrompt> _cache = new(StringComparer.Ordinal);
    private readonly string _promptsDirectory;
    private readonly ILogger<InsightsPromptLoader> _logger;

    public InsightsPromptLoader(ILogger<InsightsPromptLoader> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _promptsDirectory = Path.Combine(AppContext.BaseDirectory, PromptsRelativePath);
    }

    /// <inheritdoc />
    public InsightsPrompt Get(string basename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basename);

        return _cache.GetOrAdd(basename, key =>
        {
            var templatePath = Path.Combine(_promptsDirectory, $"{key}.txt");
            var schemaPath = Path.Combine(_promptsDirectory, $"{key}.schema.json");

            if (!File.Exists(templatePath))
            {
                _logger.LogError(
                    "InsightsPromptLoader: template not found at {Path} (basedir={BaseDir})",
                    templatePath, AppContext.BaseDirectory);
                throw new FileNotFoundException(
                    $"Insights prompt template '{key}.txt' was not found at expected path " +
                    $"'{templatePath}'. Check that the csproj copies " +
                    $"'{PromptsRelativePath}/*.txt' to the output directory.",
                    templatePath);
            }
            if (!File.Exists(schemaPath))
            {
                _logger.LogError(
                    "InsightsPromptLoader: schema not found at {Path}",
                    schemaPath);
                throw new FileNotFoundException(
                    $"Insights prompt schema '{key}.schema.json' was not found at expected " +
                    $"path '{schemaPath}'. Check that the csproj copies " +
                    $"'{PromptsRelativePath}/*.json' to the output directory.",
                    schemaPath);
            }

            var template = File.ReadAllText(templatePath);
            var schemaJson = File.ReadAllText(schemaPath);
            var schemaName = key.Replace('.', '_').Replace('-', '_');

            _logger.LogInformation(
                "InsightsPromptLoader loaded prompt {Basename}: templateLength={TemplateLength} schemaLength={SchemaLength}",
                key, template.Length, schemaJson.Length);

            return new InsightsPrompt(
                Template: template,
                SchemaJson: schemaJson,
                SchemaName: schemaName);
        });
    }
}
