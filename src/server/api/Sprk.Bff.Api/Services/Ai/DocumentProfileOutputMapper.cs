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
/// #2237 (uac-r2): only the named profile columns are mapped (<see cref="ProfileColumns"/>; fail closed). The output is
/// built by a model and a configuration (the Action's <c>sprk_outputschemajson</c>), not by a person, and this writer runs
/// as the BFF identity, so a key that is not a profile column is dropped whatever it names: a document pointer or
/// relocation column (<c>sprk_graphitemid</c>, <c>sprk_graphitemidbound</c>, ...), a link source, the owner, an unknown
/// name. A key is mapped only in its plain form (a case or surrounding-whitespace variant of a listed column, written under
/// the listed name); an <c>@odata.bind</c> or <c>_{column}_value</c> form is dropped even when it names a listed column.
/// Each key is first asked the app-only refusal the generic writers ask
/// (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>: a document's Access Permission and own-narrowing record,
/// task 189, and its D7 link columns, task 188). No listed column is one it refuses, so today it drops nothing the list
/// would not; it is kept for its reason codes in the log and so the refusal still holds if the list is ever widened by
/// mistake. The dropped keys are logged with one Warning (the document id, the key names and the reason codes; no values)
/// and the rest of the profile is mapped. Every consumer writes this method's output, so the filter is here, where none
/// can skip it.
/// </para>
/// </remarks>
public static class DocumentProfileOutputMapper
{
    /// <summary>
    /// #2237: the <c>sprk_document</c> columns a Document Profiler output may set — the top-level properties of the shipped
    /// ACT-011 <c>sprk_outputschemajson</c> (the live Action and <c>.claude/skills/jps-action-create/examples/document-profiler.json</c>).
    /// <c>sprk_filetype</c> and <c>sprk_searchprofile</c> are not listed: this mapper computes them itself. Widening this
    /// set widens what a configuration can make the BFF identity write; a pinned test holds it.
    /// </summary>
    internal static readonly IReadOnlySet<string> ProfileColumns = new HashSet<string>(StringComparer.Ordinal)
    {
        "sprk_filesummary",
        "sprk_filetldr",
        "sprk_filekeywords",
        "sprk_documenttype",
        "sprk_extractorganization",
        "sprk_extractpeople",
    };

    /// <summary>#2237: the reason code logged for a key that is not a profile column (or not in its plain form).</summary>
    internal const string NotProfileColumnReasonCode = "sdap.ai.document_profile_column_not_mapped";

    /// <summary>
    /// Maps the AI structured output into a Dataverse field dictionary and appends the deterministic
    /// <c>sprk_filetype</c> (from the file extension). The returned dictionary is ready for
    /// <c>IDocumentDataverseService.UpdateDocumentFieldsAsync</c>. It holds only <see cref="ProfileColumns"/> plus the two
    /// computed columns (#2237); <paramref name="documentId"/> is logged when a key is dropped.
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

        // #2237: the dropped keys and their reason codes, logged once below.
        var droppedKeys = new SortedSet<string>(StringComparer.Ordinal);
        var dropCodes = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var prop in root.EnumerateObject())
        {
            var kind = prop.Value.ValueKind;

            // Asked per key, as Repair's mapping asks it, so every refused key is dropped and the rest is still mapped.
            // Redundant with the list below today (no listed column is refused); kept for its reason codes and in case
            // the list is ever widened by mistake.
            if (InheritedAccessPermission.RefuseAppOnlyWrite(ExternalDocumentNarrowing.DocumentTable, [prop.Name]) is { } refusal)
            {
                // The normalized name is one of the refusal's own constants, never model text.
                droppedKeys.Add(SecureRootInheritance.NormalizeColumn(prop.Name));
                dropCodes.Add(refusal.RefusalCode ?? InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
                continue;
            }

            // Fail closed: only a listed profile column, in its plain form, is mapped, under its listed name. A form the
            // normalization collapses (@odata.bind, _x_value) is dropped, since the writer would use the raw key.
            var column = SecureRootInheritance.NormalizeColumn(prop.Name);
            if (!ProfileColumns.Contains(column)
                || !string.Equals(prop.Name.Trim(), column, StringComparison.OrdinalIgnoreCase))
            {
                var key = prop.Name.Trim();
                droppedKeys.Add(key.Length > 100 ? key[..100] : key);
                dropCodes.Add(NotProfileColumnReasonCode);
                continue;
            }

            var name = column;

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

        if (droppedKeys.Count > 0)
        {
            logger.LogWarning(
                "[DOC-PROFILE] sprk_document {DocumentId}: the AI output named {DroppedCount} key(s) that are not profile " +
                "columns this mapper writes; not written: {DroppedKeys} ({ReasonCodes}). The rest of the profile is written.",
                documentId, droppedKeys.Count, string.Join(",", droppedKeys), string.Join(",", dropCodes));
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
