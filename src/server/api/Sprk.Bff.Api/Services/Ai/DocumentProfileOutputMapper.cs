using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;

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
/// <para>
/// #2237 (uac-r2): a property naming a column Spaarke sets itself is never mapped. The output is built by a model and a
/// configuration, not by a person, so every key is asked the app-only refusal the generic writers ask
/// (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>): a document's Access Permission and own-narrowing record
/// (task 189) and its D7 link columns (<see cref="InheritedAccessPermission.DocumentLinkColumns"/>, task 188), in any
/// form (<c>@odata.bind</c>, <c>_{column}_value</c>, any case). The refused keys are dropped with one Warning (the document
/// id, the columns and the reason codes; no values) and the rest of the profile is mapped. Every consumer writes this
/// method's output, so the filter is here, where none can skip it.
/// </para>
/// </remarks>
public static class DocumentProfileOutputMapper
{
    /// <summary>
    /// Maps the AI structured output into a Dataverse field dictionary and appends the deterministic
    /// <c>sprk_filetype</c> (from the file extension). The returned dictionary is ready for
    /// <c>IDocumentDataverseService.UpdateDocumentFieldsAsync</c>. It never holds a column the app-only refusal refuses
    /// on a document (#2237); <paramref name="documentId"/> is logged when one is dropped.
    /// </summary>
    public static Dictionary<string, object?> BuildFields(
        Guid documentId,
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

        // #2237: the columns the app-only refusal refuses (normalized names) and its reason codes, logged once below.
        var refusedColumns = new SortedSet<string>(StringComparer.Ordinal);
        var refusalCodes = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var prop in root.EnumerateObject())
        {
            var name = prop.Name;
            var kind = prop.Value.ValueKind;

            // Asked per key, as Repair's mapping asks it, so every refused key is dropped and the rest is still mapped.
            if (InheritedAccessPermission.RefuseAppOnlyWrite(ExternalDocumentNarrowing.DocumentTable, [name]) is { } refusal)
            {
                // The normalized name is one of the refusal's own constants, never model text.
                refusedColumns.Add(SecureRootInheritance.NormalizeColumn(name));
                refusalCodes.Add(refusal.RefusalCode ?? InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
                continue;
            }

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

        if (refusedColumns.Count > 0)
        {
            logger.LogWarning(
                "[DOC-PROFILE] sprk_document {DocumentId}: the AI output named {RefusedCount} column(s) Spaarke sets itself; " +
                "not written: {RefusedColumns} ({ReasonCodes}). The rest of the profile is written.",
                documentId, refusedColumns.Count, string.Join(",", refusedColumns), string.Join(",", refusalCodes));
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
