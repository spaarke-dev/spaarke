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
/// task 189, its D7 link columns, task 188, and its BFF-owned pointer, relocation, file-path and email-archive columns,
/// #2240). No listed column is one it refuses, so today it drops nothing the list
/// would not; it is kept for its reason codes in the log and so the refusal still holds if the list is ever widened by
/// mistake. The dropped keys are logged with one Warning (the document id, the key names and the reason codes; no values)
/// and the rest of the profile is mapped. Every consumer writes this method's output, so the filter is here, where none
/// can skip it.
/// </para>
/// <para>
/// #2239 round 2: a column is mapped from the FIRST key that names it; a later key naming the same column (a case variant)
/// is dropped with <see cref="DuplicateColumnReasonCode"/>, so it cannot replace a coerced Choice value with a raw string.
/// A model key naming a computed column (<see cref="ComputedColumns"/>) is skipped without a Warning: the mapper sets both
/// itself. When no profile column is mapped the result is EMPTY (no computed column either), so every consumer's
/// <c>fields.Count == 0</c> check treats a profile that carries nothing as a failure, not a success. Key text in the
/// Warning has control characters replaced with <c>?</c>, is capped at 100 characters, and at most 20 keys are listed.
/// </para>
/// </remarks>
public static class DocumentProfileOutputMapper
{
    /// <summary>
    /// #2237: the <c>sprk_document</c> columns a Document Profiler output may set — the six top-level properties of dev's
    /// live ACT-011 <c>sprk_outputschemajson</c>. The repo example
    /// (<c>.claude/skills/jps-action-create/examples/document-profiler.json</c>) also names <c>sprk_filetype</c>, which is
    /// not listed: this mapper computes it and <c>sprk_searchprofile</c> itself (<see cref="ComputedColumns"/>). Widening
    /// this set widens what a configuration can make the BFF identity write; a pinned test holds it.
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

    /// <summary>#2239: the reason code logged for a later key naming a profile column an earlier key already mapped.</summary>
    internal const string DuplicateColumnReasonCode = "sdap.ai.document_profile_column_duplicate";

    /// <summary>
    /// #2239: the columns this mapper computes itself (<c>sprk_filetype</c> from the file extension, <c>sprk_searchprofile</c>
    /// from the mapped profile). A model key naming one, in its plain form, is skipped without a Warning.
    /// </summary>
    internal static readonly IReadOnlySet<string> ComputedColumns = new HashSet<string>(StringComparer.Ordinal)
    {
        "sprk_filetype",
        "sprk_searchprofile",
    };

    /// <summary>#2239: the per-key length cap and the number of keys listed in the dropped-key Warning.</summary>
    internal const int WarningKeyMaxLength = 100;

    internal const int WarningMaxKeys = 20;

    /// <summary>
    /// Maps the AI structured output into a Dataverse field dictionary and appends the deterministic
    /// <c>sprk_filetype</c> (from the file extension). The returned dictionary is ready for
    /// <c>IDocumentDataverseService.UpdateDocumentFieldsAsync</c>. It holds only <see cref="ProfileColumns"/> plus the two
    /// computed columns (#2237), and is EMPTY when no profile column is mapped (#2239: callers treat empty as a failed
    /// profile); <paramref name="documentId"/> is logged when a key is dropped. A column is mapped from the first key that
    /// names it, whatever that key's value (a null or blank first value leaves the column unmapped).
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

        // #2239: the profile columns a key has already named (the first one is mapped; later ones are dropped).
        var claimedColumns = new HashSet<string>(StringComparer.Ordinal);

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

            var column = SecureRootInheritance.NormalizeColumn(prop.Name);
            var plainForm = string.Equals(prop.Name.Trim(), column, StringComparison.OrdinalIgnoreCase);

            // #2239: a computed column named by the model is skipped quietly; the mapper sets it below.
            if (plainForm && ComputedColumns.Contains(column))
            {
                continue;
            }

            // Fail closed: only a listed profile column, in its plain form, is mapped, under its listed name. A form the
            // normalization collapses (@odata.bind, _x_value) is dropped, since the writer would use the raw key.
            if (!ProfileColumns.Contains(column) || !plainForm)
            {
                droppedKeys.Add(WarningKey(prop.Name));
                dropCodes.Add(NotProfileColumnReasonCode);
                continue;
            }

            // #2239: a column is mapped from the first key that names it; a later one (a case variant) is dropped.
            if (!claimedColumns.Add(column))
            {
                droppedKeys.Add(WarningKey(prop.Name));
                dropCodes.Add(DuplicateColumnReasonCode);
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
            // #2239: at most WarningMaxKeys keys are listed; the count is the full one.
            var listedKeys = string.Join(",", droppedKeys.Take(WarningMaxKeys));
            if (droppedKeys.Count > WarningMaxKeys)
            {
                listedKeys += $",+{droppedKeys.Count - WarningMaxKeys} more";
            }

            logger.LogWarning(
                "[DOC-PROFILE] sprk_document {DocumentId}: the AI output named {DroppedCount} key(s) that are not profile " +
                "columns this mapper writes; not written: {DroppedKeys} ({ReasonCodes}). The rest of the profile is written.",
                documentId, droppedKeys.Count, listedKeys, string.Join(",", dropCodes));
        }

        // #2239: no profile column mapped — return EMPTY (no computed column), so each consumer's empty check fails the run.
        if (fields.Count == 0)
        {
            return fields;
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

    /// <summary>
    /// #2239: a model-named key as the Warning shows it — trimmed, every control character (CR, LF, tab, ...) replaced with
    /// <c>?</c> so it cannot forge a log line, and capped at <see cref="WarningKeyMaxLength"/> characters.
    /// </summary>
    internal static string WarningKey(string key)
    {
        var trimmed = key.Trim();
        var capped = trimmed.Length > WarningKeyMaxLength ? trimmed[..WarningKeyMaxLength] : trimmed;
        return string.Create(capped.Length, capped, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsControl(source[i]) ? '?' : source[i];
            }
        });
    }
}
