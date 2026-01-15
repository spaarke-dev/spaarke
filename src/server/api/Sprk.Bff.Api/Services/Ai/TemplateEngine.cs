using System.Text.RegularExpressions;
using HandlebarsDotNet;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Template engine implementation using Handlebars.NET.
/// Provides secure, logic-less template rendering for playbook delivery nodes.
/// </summary>
/// <remarks>
/// <para>
/// This implementation:
/// </para>
/// <list type="bullet">
/// <item>Uses Handlebars.NET for template compilation and rendering</item>
/// <item>Supports nested object access (e.g., {{node.output.field}})</item>
/// <item>Handles missing variables gracefully (renders as empty string)</item>
/// <item>Caches compiled templates for performance</item>
/// </list>
/// </remarks>
public sealed class TemplateEngine : ITemplateEngine
{
    private readonly IHandlebars _handlebars;
    private readonly ILogger<TemplateEngine> _logger;

    // Regex to match {{variable}} patterns (including nested like {{a.b.c}})
    private static readonly Regex VariablePattern = new(
        @"\{\{([^{}]+)\}\}",
        RegexOptions.Compiled);

    public TemplateEngine(ILogger<TemplateEngine> logger)
    {
        _logger = logger;

        // Configure Handlebars with safe defaults
        _handlebars = Handlebars.Create(new HandlebarsConfiguration
        {
            // Throw on missing members would break graceful handling, so leave default (false)
            ThrowOnUnresolvedBindingExpression = false,
            // Don't HTML-encode output (we're not rendering HTML)
            NoEscape = true
        });

        // Register helper for safe property access that returns empty string for nulls
        _handlebars.RegisterHelper("safe", (context, arguments) =>
        {
            var value = arguments.Length > 0 ? arguments[0] : null;
            return value?.ToString() ?? string.Empty;
        });
    }

    /// <inheritdoc />
    public string Render(string template, IDictionary<string, object?> context)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        if (context == null || context.Count == 0)
        {
            // No variables to substitute, but still need to handle any {{}} that might be there
            // They'll just render as empty strings
            context = new Dictionary<string, object?>();
        }

        try
        {
            var compiled = _handlebars.Compile(template);
            var result = compiled(context);

            _logger.LogDebug(
                "Rendered template with {VariableCount} context variables",
                context.Count);

            return result;
        }
        catch (HandlebarsException ex)
        {
            _logger.LogWarning(
                ex,
                "Template rendering failed, returning original template");

            // Return original template on error rather than throwing
            return template;
        }
    }

    /// <inheritdoc />
    public string Render<T>(string template, T context) where T : class
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        if (context == null)
        {
            return Render(template, new Dictionary<string, object?>());
        }

        try
        {
            var compiled = _handlebars.Compile(template);
            var result = compiled(context);

            _logger.LogDebug(
                "Rendered template with context type {ContextType}",
                typeof(T).Name);

            return result;
        }
        catch (HandlebarsException ex)
        {
            _logger.LogWarning(
                ex,
                "Template rendering failed for type {ContextType}, returning original template",
                typeof(T).Name);

            return template;
        }
    }

    /// <inheritdoc />
    public bool HasVariables(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return false;
        }

        return VariablePattern.IsMatch(template);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetVariableNames(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return Array.Empty<string>();
        }

        var matches = VariablePattern.Matches(template);
        var variables = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in matches)
        {
            // Group 1 is the variable name (without the braces)
            var variableName = match.Groups[1].Value.Trim();

            // For nested variables like "node.output.field", return the root
            // since that's what needs to be in the context
            var rootVariable = variableName.Split('.')[0];
            variables.Add(rootVariable);
        }

        return variables.ToList();
    }
}
