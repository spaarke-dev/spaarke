namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Task 031 (spaarkeai-compose-r6, FR-05) — the PublicContracts facade by which the Compose template-merge
/// path (task 032's endpoint) obtains a RESOLVED firm/matter <c>.dotx</c>: fetched from the existing
/// Dataverse <c>template</c> entity store and with its <c>{{variable}}</c>s rendered through the existing
/// <c>ITemplateEngine</c>-backed Word text-render. Storage/variables ONLY — the OOXML part-merge itself is
/// <c>ComposeTemplatePartMergeEngine</c> (task 030, <c>Services/Compose</c>).
/// </summary>
/// <remarks>
/// Placement (ADR-013 / root §10 bullet 3): the implementation lives in <c>Services/Ai/Delivery</c> beside
/// its siblings (<c>EmailTemplateService</c> — the native-<c>template</c>-entity fetch pattern this reuses;
/// <c>WordTemplateService</c> — the <c>{{variable}}</c> docx text-render this reuses). Compose-side code
/// consumes THIS facade, never those internals. No parallel storage subsystem, no native
/// <c>documenttemplate</c> (spec FR-05 LOCKED / §11).
/// </remarks>
public interface IComposeTemplateSource
{
    /// <summary>
    /// Resolves a firm/matter template to ready-to-merge <c>.dotx</c> bytes: fetches the template record
    /// from the Dataverse <c>template</c> entity (by id, or by exact <c>title</c> when
    /// <paramref name="templateIdOrName"/> is not a GUID), reads its attached <c>.dotx</c>/<c>.docx</c>
    /// (annotation attachment — the established Dataverse template-storage pattern), and — when
    /// <paramref name="variables"/> is non-empty — renders <c>{{variable}}</c>s via the existing Word
    /// template text-render. Returns null when the template or its attachment cannot be found.
    /// </summary>
    /// <param name="templateIdOrName">Template record id (GUID) or exact template <c>title</c>.</param>
    /// <param name="variables">Variables for <c>{{placeholder}}</c> rendering; null/empty skips rendering
    /// (the raw stored bytes are returned — byte-faithful for variable-free house templates).</param>
    /// <param name="dataverseUrl">Environment URL (same caller-supplied convention as the sibling
    /// template services).</param>
    /// <param name="accessToken">Dataverse access token. The production caller (apply-template endpoint) passes an APP-ONLY token — templates are org-shared assets (personal templates are refused by the implementation).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<ComposeResolvedTemplate?> ResolveAsync(
        string templateIdOrName,
        Dictionary<string, object?>? variables,
        string dataverseUrl,
        string accessToken,
        CancellationToken cancellationToken);
}

/// <summary>A resolved firm/matter template: the (optionally variable-rendered) <c>.dotx</c> bytes plus
/// display metadata for the merge record.</summary>
/// <param name="TemplateBytes">The resolved <c>.dotx</c> package bytes (input to the part-merge engine).</param>
/// <param name="TemplateName">The template record's <c>title</c>.</param>
/// <param name="FileName">The stored attachment's file name.</param>
/// <param name="VariablesRendered">True when <c>{{variable}}</c> rendering ran over the stored bytes.</param>
public sealed record ComposeResolvedTemplate(
    byte[] TemplateBytes,
    string TemplateName,
    string FileName,
    bool VariablesRendered);
