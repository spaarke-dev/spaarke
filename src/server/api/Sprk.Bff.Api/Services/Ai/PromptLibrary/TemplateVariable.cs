namespace Sprk.Bff.Api.Services.Ai.PromptLibrary;

/// <summary>
/// The data type of a template variable.
/// </summary>
public enum TemplateVariableType
{
    /// <summary>Plain text value.</summary>
    String = 0,

    /// <summary>A reference to a Dataverse entity (e.g. matter.name, document.title).</summary>
    EntityRef = 1
}

/// <summary>
/// Describes a single variable placeholder within a <see cref="PromptTemplate"/>.
///
/// Placeholders use the <c>{{variableName}}</c> syntax in the template body.
/// When <see cref="Type"/> is <see cref="TemplateVariableType.EntityRef"/>, the client
/// should offer an entity-reference picker and pass the resolved string at render time.
/// </summary>
/// <param name="Name">Variable name — must match the placeholder key exactly (case-sensitive).</param>
/// <param name="Type">Whether the value is a plain string or an entity-reference picker.</param>
/// <param name="Description">Human-readable hint displayed in the variable input UI.</param>
/// <param name="Required">If <c>true</c>, <see cref="IPromptLibraryService.RenderAsync"/> will fail when this variable is absent.</param>
public record TemplateVariable(
    string Name,
    TemplateVariableType Type,
    string Description,
    bool Required = true);
