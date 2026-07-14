using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Maps the "Document Profiler" Action (ACT-011) structured output — whose top-level
/// property names ARE the target <c>sprk_document</c> field names per the Action's
/// <c>sprk_outputschemajson</c> — into a Dataverse-ready field dictionary.
/// </summary>
/// <remarks>
/// <para>
/// Single source of truth for the ACT-011 output → <c>sprk_document</c> column mapping.
/// Extracted from <c>AnalysisEndpoints.BuildDocumentProfileFields</c> (FR-P3-05 document-profile
/// pipeline) so BOTH document-profile consumers — the Document Upload wizard
/// (<c>AnalysisEndpoints.ExecuteDocumentProfilePipelineAsync</c>) and the Compose create-on-save
/// OBO facade (<c>DocumentProfileAi</c>, spaarkeai-compose-r2) — run the identical mapping and
/// cannot drift (the ADR-043 §5 anti-drift discipline: one wiring, one place). §11 default-to-reuse.
/// </para>
/// <para>
/// Special handling: <c>sprk_documenttype</c> → Choice coercion via <see cref="DocumentTypeMapper"/>;
/// arrays/objects → JSON blobs; <c>sprk_searchprofile</c> computed deterministically via
/// <see cref="DocumentProfileFieldMapper.BuildSearchProfile"/>; <c>sprk_filetype</c> derived from the
/// file extension (deterministic — not asked of the LLM).
/// </para>
/// </remarks>
public static class DocumentProfileOutputMapper
{
    /// <summary>
    /// Maps the AI structured output into a Dataverse field dictionary and appends the deterministic
    /// <c>sprk_filetype</c> (from the file extension). The returned dictionary is ready for
    /// <c>IDocumentDataverseService.UpdateDocumentFieldsAsync</c>.
    /// </summary>
    public static Dictionary<string, object?> BuildFields(
        JsonElement root,
        string fileName,
        ParentEntityContext? parentEntity,
        ILogger logger)
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object)
        {
            return fields;
        }

        // Sibling dict of stringified outputs used for BuildSearchProfile below.
        var stringOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in root.EnumerateObject())
        {
            var name = prop.Name;
            var kind = prop.Value.ValueKind;

            switch (kind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    continue;

                case JsonValueKind.String:
                    {
                        var stringValue = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(stringValue)) continue;

                        if (name.Equals("sprk_documenttype", StringComparison.OrdinalIgnoreCase))
                        {
                            var optionValue = DocumentTypeMapper.ToDataverseValue(stringValue);
                            if (optionValue.HasValue)
                            {
                                // Dataverse SDK Choice/OptionSet attributes require OptionSetValue,
                                // not a raw int. R5 Doc 06 Choice-field coercion pattern.
                                fields[name] = new OptionSetValue(optionValue.Value);
                                stringOutputs[name] = stringValue;
                            }
                            else
                            {
                                logger.LogWarning(
                                    "Could not coerce documentType='{DocType}' to a Dataverse Choice value; dropping the field",
                                    stringValue);
                            }
                        }
                        else
                        {
                            fields[name] = stringValue;
                            stringOutputs[name] = stringValue;
                        }
                        break;
                    }

                case JsonValueKind.Array:
                case JsonValueKind.Object:
                    {
                        var jsonBlob = prop.Value.GetRawText();
                        fields[name] = jsonBlob;
                        stringOutputs[name] = jsonBlob;
                        break;
                    }

                default:
                    {
                        var raw = prop.Value.GetRawText();
                        fields[name] = raw;
                        stringOutputs[name] = raw;
                        break;
                    }
            }
        }

        var searchProfile = DocumentProfileFieldMapper.BuildSearchProfile(
            stringOutputs,
            parentEntityName: parentEntity?.EntityName,
            parentEntityType: parentEntity?.EntityType,
            fileName: fileName);
        if (!string.IsNullOrWhiteSpace(searchProfile))
        {
            fields["sprk_searchprofile"] = searchProfile;
        }

        // sprk_filetype is deterministic (comes from the file extension) — don't ask the
        // LLM for it. Column is 10 chars; extension without leading dot, upper-cased.
        var extension = Path.GetExtension(fileName)?.TrimStart('.').ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(extension))
        {
            fields["sprk_filetype"] = extension.Length > 10 ? extension[..10] : extension;
        }

        return fields;
    }
}
