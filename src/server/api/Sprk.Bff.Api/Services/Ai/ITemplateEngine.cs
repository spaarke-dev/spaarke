namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for rendering templates with variable substitution.
/// Used by delivery nodes (email, task, etc.) to populate templates with playbook execution data.
/// </summary>
/// <remarks>
/// <para>
/// Supports Handlebars-style syntax:
/// </para>
/// <list type="bullet">
/// <item>Simple variables: {{variableName}}</item>
/// <item>Nested access: {{node.output.field}}</item>
/// <item>Missing variables render as empty string</item>
/// </list>
/// </remarks>
public interface ITemplateEngine
{
    /// <summary>
    /// Renders a template string by substituting variables with values from the context.
    /// </summary>
    /// <param name="template">The template string with {{variable}} placeholders.</param>
    /// <param name="context">Dictionary of variable names to values. Values can be nested objects.</param>
    /// <returns>The rendered string with all variables substituted.</returns>
    /// <example>
    /// <code>
    /// var result = templateEngine.Render(
    ///     "Hello {{name}}, you have {{count}} items.",
    ///     new Dictionary&lt;string, object?&gt; { ["name"] = "John", ["count"] = 5 });
    /// // Returns: "Hello John, you have 5 items."
    /// </code>
    /// </example>
    string Render(string template, IDictionary<string, object?> context);

    /// <summary>
    /// Renders a template using a strongly-typed context object.
    /// Properties of the object become available as variables.
    /// </summary>
    /// <typeparam name="T">The type of the context object.</typeparam>
    /// <param name="template">The template string with {{variable}} placeholders.</param>
    /// <param name="context">Object whose properties provide variable values.</param>
    /// <returns>The rendered string with all variables substituted.</returns>
    string Render<T>(string template, T context) where T : class;

    /// <summary>
    /// Checks if a template string contains any variable placeholders.
    /// </summary>
    /// <param name="template">The template string to check.</param>
    /// <returns>True if the template contains at least one {{variable}} placeholder.</returns>
    bool HasVariables(string template);

    /// <summary>
    /// Extracts all variable names from a template string.
    /// </summary>
    /// <param name="template">The template string to parse.</param>
    /// <returns>List of unique variable names found in the template.</returns>
    IReadOnlyList<string> GetVariableNames(string template);
}
