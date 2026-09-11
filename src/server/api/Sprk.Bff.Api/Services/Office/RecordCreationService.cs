// spaarkeai-word-add-in-r1 task 030 (FR-13): the shared server-side record-creation service.
//
// Placement Justification (bff-extensions.md) and the CLAUDE.md §11 three-question answers are in
// projects/spaarkeai-word-add-in-r1/notes/030-creation-service-decisions.md §3-§4. In short: it runs inline in
// POST /api/office/quickcreate/{entityType} (a user is waiting; ADR-001 rules out Functions), it composes the
// existing IGenericEntityService + IFieldMappingDataverseService seams (no new Dataverse client), and it is the
// one implementation task 031 (Project) and the post-r1 wizard-migration evaluation are meant to call.
//
// NUMBERING IS OUT OF SCOPE (owner decision 2026-09-11): sprk_matternumber will be assigned by a planned, separate
// server-side record-numbering component that triggers on create. Until it exists, matters created here have no
// number. This service never writes it. See projects/spaarkeai-word-add-in-r1/notes/030-numbering-handoff.md.

using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Office;
// Models.Office declares its own EntityReference (the add-in's association DTO); every reference here is the SDK's.
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Input to <see cref="RecordCreationService.CreateAsync"/>.
/// </summary>
public sealed record RecordCreationRequest
{
    /// <summary>The entity to create. <b>Matter only</b> in r1 — Project is task 031, Invoice stays on the minimal path.</summary>
    public required QuickCreateEntityType EntityType { get; init; }

    /// <summary>Record name (required; trimmed).</summary>
    public required string Name { get; init; }

    /// <summary>Optional description (trimmed; ignored when blank).</summary>
    public string? Description { get; init; }

    /// <summary>The caller's Entra object id — for logs only; ownership comes from <see cref="OwnerSystemUserId"/>.</summary>
    public required string CallerUserId { get; init; }

    /// <summary>
    /// The caller's Dataverse <c>systemuserid</c>, resolved server-side (never from the client). <b>Load-bearing</b>:
    /// when it is absent or unparseable the service refuses rather than creating an app-owned record.
    /// </summary>
    public string? OwnerSystemUserId { get; init; }

    /// <summary>
    /// The <c>sprk_mattertype_ref</c> id. The pane will always send it (owner decision 2026-09-11; the client task
    /// adding the required field is pending). A type that resolves sets the <c>sprk_mattertype</c> lookup. A missing,
    /// empty or unknown type is NEVER a rejection: the matter is created without the lookup, with a warning.
    /// </summary>
    public Guid? MatterTypeId { get; init; }

    /// <summary>
    /// Optional record-context entity logical name (e.g. <c>sprk_project</c>) — the Field Mapping Framework source.
    /// </summary>
    /// <remarks>
    /// 🔴 The source record is read <b>app-only</b>. The CALLER'S Read right on it MUST already have been
    /// established upstream — <c>QuickCreateSourceAccessFilter</c> does that for the quick-create route. A new
    /// caller of this service must do the same, or it turns this into a way to copy fields out of any record.
    /// </remarks>
    public string? SourceEntityLogicalName { get; init; }

    /// <summary>Optional record-context id; see <see cref="SourceEntityLogicalName"/>.</summary>
    public Guid? SourceRecordId { get; init; }
}

/// <summary>Why a creation was refused. Every kind means <b>no row was written</b>.</summary>
public enum RecordCreationFailureKind
{
    /// <summary>The request cannot be honoured as given (wrong entity type, blank name).</summary>
    InvalidInput,

    /// <summary>The caller has no resolvable Dataverse user, so the record cannot be owned by them.</summary>
    OwnerUnresolved
}

/// <summary>A structured refusal. <see cref="Code"/> is a stable identifier; <see cref="Detail"/> is user-safe text.</summary>
public sealed record RecordCreationFailure(RecordCreationFailureKind Kind, string Code, string Detail);

/// <summary>
/// Outcome of <see cref="RecordCreationService.CreateAsync"/>: the created record, or a
/// <see cref="RecordCreationFailure"/>. A business outcome is never an exception.
/// </summary>
public sealed record RecordCreationResult
{
    /// <summary>The created record id (empty on failure).</summary>
    public Guid RecordId { get; init; }

    /// <summary>The created record's entity logical name.</summary>
    public string LogicalName { get; init; } = string.Empty;

    /// <summary>The name actually written (a field-mapping rule may have replaced the requested one).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Non-fatal diagnostics (field-mapping skips, no or unknown matter type, BU defaults unavailable).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Set when the creation was refused; <see langword="null"/> on success.</summary>
    public RecordCreationFailure? Failure { get; init; }

    /// <summary>True when a record was created.</summary>
    public bool Succeeded => Failure is null;

    internal static RecordCreationResult Failed(RecordCreationFailure failure) => new() { Failure = failure };
}

/// <summary>
/// Creates Dataverse records server-side with the completeness the client <c>Create*Wizard</c> components give
/// them in the browser — a load-bearing owner, business-unit defaults, and the Field Mapping Framework. r1 scope is
/// <b>Matter only</b> (FR-13; task 031 owns Project, whose semantics differ).
/// </summary>
/// <remarks>
/// <para><b>Matter pipeline</b> (mirrors <c>matterService.createMatter</c>, minus numbering):</para>
/// <list type="number">
///   <item><description>Name / description; the <c>sprk_mattertype</c> lookup when the supplied type resolves (one
///   existence read — an unknown type is dropped with a warning, never rejected).</description></item>
///   <item><description>BU defaults from the OWNER's business unit — <c>sprk_searchindexname</c> (INV-5 guarded) and
///   the <c>sprk_ai_search_index</c> lookup — mirroring <c>EntityCreationService.applyUserBuDefaults</c>.
///   <c>sprk_containerid</c> is deliberately NOT written (unified-access-control-r2 task 076, W1).</description></item>
///   <item><description>Field Mapping Framework — one profile read, one source read, rules applied by
///   <see cref="CreateTimeFieldMapping"/>; a missing profile is a silent no-op.</description></item>
///   <item><description>Owner — <c>ownerid</c> = the caller, refused when unresolved.</description></item>
/// </list>
/// <para><b><c>sprk_matternumber</c> is never written here</b> (owner decision 2026-09-11) — not directly, and not
/// through a field-mapping rule of any type (the protected-attribute check is case-insensitive). Numbering is left to
/// a planned separate on-create component so this path can never pre-empt or collide with it.</para>
/// <para><b>Deliberate deviations from the client engine</b> (notes/030 §8): mapping may not touch
/// <c>sprk_matternumber</c>, <c>ownerid</c> or <c>sprk_containerid</c>; a mapping that blanks the name or writes a
/// non-matter-type value into <c>sprk_mattertype</c> is reverted with a warning.</para>
/// <para><b>ADR-044</b>: every GUID handled here is a <see cref="Guid"/> value — canonical by construction. The one
/// string GUID, <see cref="RecordCreationRequest.OwnerSystemUserId"/>, is parsed with
/// <see cref="Guid.TryParse(string?, out Guid)"/>. Body GUIDs (<c>matterTypeId</c>, <c>sourceRecordId</c>) are bound
/// by System.Text.Json, which accepts only the bare "D" form (either case) and fails a brace-wrapped value closed with a
/// 400 before this service runs — so clients must send <c>cleanGuid</c> output. Reads are typed SDK calls; no GUID is
/// interpolated into an OData string.</para>
/// <para><b>ADR-010</b>: concrete, one registration (<c>OfficeModule</c>). No interface — there is one
/// implementation, and the contract test substitutes the Dataverse boundary, not this class.</para>
/// </remarks>
public sealed class RecordCreationService
{
    internal const string MatterEntity = "sprk_matter";
    internal const string MatterNameAttribute = "sprk_mattername";
    internal const string MatterDescriptionAttribute = "sprk_matterdescription";
    internal const string MatterNumberAttribute = "sprk_matternumber";
    internal const string MatterTypeAttribute = "sprk_mattertype";
    internal const string MatterTypeEntity = "sprk_mattertype_ref";
    internal const string MatterTypeIdAttribute = "sprk_mattertype_refid";
    internal const string OwnerAttribute = "ownerid";
    internal const string ContainerAttribute = "sprk_containerid";
    internal const string SystemUserEntity = "systemuser";
    internal const string UserBusinessUnitAttribute = "businessunitid";
    internal const string BusinessUnitEntity = "businessunit";
    internal const string SearchIndexNameAttribute = "sprk_searchindexname";
    internal const string SearchIndexLookupAttribute = "sprk_ai_search_index";
    internal const string SearchIndexEntity = "sprk_aisearchindex";

    /// <summary>
    /// Attributes a field-mapping rule may not write on this path: the number (left to the planned numbering
    /// component — notes/030-numbering-handoff.md), the load-bearing owner, and the storage container
    /// (server-derived only — task 076 W1). Case-insensitive, so a mis-cased or padded target is caught too.
    /// </summary>
    private static readonly IReadOnlySet<string> ProtectedAttributes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MatterNumberAttribute, OwnerAttribute, ContainerAttribute };

    private readonly IGenericEntityService _entities;
    private readonly IFieldMappingDataverseService _fieldMappings;
    private readonly ILogger<RecordCreationService> _logger;

    public RecordCreationService(
        IGenericEntityService entities,
        IFieldMappingDataverseService fieldMappings,
        ILogger<RecordCreationService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _fieldMappings = fieldMappings ?? throw new ArgumentNullException(nameof(fieldMappings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates the record. Returns a structured <see cref="RecordCreationFailure"/> — never a partial write — when
    /// the record cannot be created with its invariants intact. Dataverse transport or rejection errors on the final
    /// create propagate (the caller's generic 500 path, unchanged from before).
    /// </summary>
    public async Task<RecordCreationResult> CreateAsync(RecordCreationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.EntityType != QuickCreateEntityType.Matter)
        {
            return RecordCreationResult.Failed(new RecordCreationFailure(
                RecordCreationFailureKind.InvalidInput,
                "entity_type_not_supported",
                $"Server-side creation supports Matter only; '{request.EntityType}' is created by another path."));
        }

        return await CreateMatterAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<RecordCreationResult> CreateMatterAsync(RecordCreationRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return RecordCreationResult.Failed(new RecordCreationFailure(
                RecordCreationFailureKind.InvalidInput, "name_required", "A matter requires a name."));
        }

        // Owner is LOAD-BEARING (task 030 step 4) — the old QuickCreate posture of "unresolved → app-owned" is
        // exactly how a pane-created matter ended up owned by nobody the user recognises.
        if (!TryParseGuid(request.OwnerSystemUserId, out var ownerId))
        {
            _logger.LogWarning(
                "[RECORD-CREATE] Refusing Matter create for caller {CallerUserId}: no resolved Dataverse systemuser, "
                + "so the record cannot be owned by the caller.",
                request.CallerUserId);

            return RecordCreationResult.Failed(new RecordCreationFailure(
                RecordCreationFailureKind.OwnerUnresolved,
                "owner_unresolved",
                "Your account could not be matched to a Dataverse user, so the new matter could not be assigned to "
                + "you and was not created. Ask an administrator to check that your user is provisioned in this "
                + "environment."));
        }

        var warnings = new List<string>();
        var entity = new Entity(MatterEntity);
        entity[MatterNameAttribute] = name;
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            entity[MatterDescriptionAttribute] = request.Description.Trim();
        }

        // Guid.Empty is how some clients say "unset": treated exactly like an absent type (owner decision: never reject).
        EntityReference? requestedType = null;
        var typeWarningAdded = false;
        if (request.MatterTypeId is { } requestedTypeId && requestedTypeId != Guid.Empty)
        {
            if (await MatterTypeIsKnownToBeMissingAsync(requestedTypeId, ct).ConfigureAwait(false))
            {
                warnings.Add(
                    "The selected matter type was not found, so the matter was created without a type. Open the "
                    + "matter and set its type.");
                typeWarningAdded = true;
            }
            else
            {
                requestedType = new EntityReference(MatterTypeEntity, requestedTypeId);
                entity[MatterTypeAttribute] = requestedType;
            }
        }

        await ApplyBusinessUnitDefaultsAsync(entity, ownerId, warnings, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.SourceEntityLogicalName)
            && request.SourceRecordId is { } sourceRecordId
            && sourceRecordId != Guid.Empty)
        {
            await ApplyFieldMappingsAsync(
                entity, request.SourceEntityLogicalName.Trim(), sourceRecordId, warnings, ct).ConfigureAwait(false);

            KeepRequestedNameIfMappingBlankedIt(entity, name, warnings);
        }

        if (ResolveFinalMatterType(entity, requestedType, warnings) is null && !typeWarningAdded)
        {
            // Owner decision 2026-09-11: the type is required in the pane, but a missing one is NOT a rejection.
            warnings.Add("No matter type was supplied. Open the matter and set its type.");
        }

        // Set LAST, after mapping, so nothing can overwrite it — owner attribution is load-bearing (task 030 step 4).
        entity[OwnerAttribute] = new EntityReference(SystemUserEntity, ownerId);

        var createdId = await _entities.CreateAsync(entity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[RECORD-CREATE] Matter {MatterId} created for caller {CallerUserId}: owner={OwnerId}, warnings={WarningCount}",
            createdId, request.CallerUserId, ownerId, warnings.Count);

        return new RecordCreationResult
        {
            RecordId = createdId,
            LogicalName = MatterEntity,
            Name = (string)entity[MatterNameAttribute],
            Warnings = warnings
        };
    }

    /// <summary>
    /// The one existence read for a supplied matter type. Returns <see langword="true"/> ONLY when Dataverse says the
    /// row does not exist — the case that would otherwise reach Dataverse as a dangling lookup and fail the whole create
    /// with a 500. Any other outcome (found, or a read that could not answer) keeps the lookup: a transient read
    /// failure must not strip a type the user chose, and Dataverse still validates the lookup on create.
    /// </summary>
    private async Task<bool> MatterTypeIsKnownToBeMissingAsync(Guid matterTypeId, CancellationToken ct)
    {
        try
        {
            await _entities
                .RetrieveAsync(MatterTypeEntity, matterTypeId, [MatterTypeIdAttribute], ct)
                .ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFound(ex))
        {
            _logger.LogWarning(
                "[RECORD-CREATE] Matter type {MatterTypeId} does not exist; creating the matter without a type.",
                matterTypeId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] Matter type {MatterTypeId} could not be checked; keeping the lookup and letting "
                + "Dataverse validate it on create.",
                matterTypeId);
            return false;
        }
    }

    /// <summary>
    /// A mapping rule may replace the name (client semantics), but may not leave the record without one — a
    /// Template whose placeholders all resolve empty, or a non-text value, reverts to the name the user entered.
    /// </summary>
    private static void KeepRequestedNameIfMappingBlankedIt(Entity entity, string requestedName, List<string> warnings)
    {
        if (entity.Attributes.TryGetValue(MatterNameAttribute, out var value)
            && value is string mappedName
            && !string.IsNullOrWhiteSpace(mappedName))
        {
            return;
        }

        entity[MatterNameAttribute] = requestedName;
        warnings.Add("A field-mapping rule produced an empty or non-text matter name, so the name you entered was kept.");
    }

    /// <summary>
    /// The matter type the record will carry. A mapping rule that wrote something other than a
    /// <c>sprk_mattertype_ref</c> reference into <c>sprk_mattertype</c> is reverted (to the requested type, if any)
    /// rather than being sent to Dataverse, where it would fail the whole create.
    /// </summary>
    private static EntityReference? ResolveFinalMatterType(
        Entity entity,
        EntityReference? requestedType,
        List<string> warnings)
    {
        if (!entity.Attributes.TryGetValue(MatterTypeAttribute, out var value) || value is null)
        {
            return null;
        }

        if (value is EntityReference reference
            && reference.Id != Guid.Empty
            && string.Equals(reference.LogicalName, MatterTypeEntity, StringComparison.OrdinalIgnoreCase))
        {
            return reference;
        }

        warnings.Add("A field-mapping rule set the matter type to a value that is not a matter type, so it was not applied.");
        if (requestedType is not null)
        {
            entity[MatterTypeAttribute] = requestedType;
            return requestedType;
        }

        entity.Attributes.Remove(MatterTypeAttribute);
        return null;
    }

    // ------------------------------------------------------------------------------------------------------
    // Business-unit defaults
    // ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server-side <c>EntityCreationService.applyUserBuDefaults</c> + <c>resolveUserBuDefaults</c>: the owner's
    /// business unit → <c>sprk_searchindexname</c> (INV-5: an explicit value wins) and the
    /// <c>sprk_ai_search_index</c> lookup.
    /// </summary>
    /// <remarks>
    /// Non-fatal by design. These are search-index ROUTING hints, not a storage location or an access boundary,
    /// and <c>SearchIndexNameResolver</c> already walks the record's owning business unit at index time — which,
    /// with <c>ownerid</c> = the caller, is this same business unit. A failed read therefore degrades to that
    /// fallback rather than blocking the create.
    /// </remarks>
    private async Task ApplyBusinessUnitDefaultsAsync(
        Entity entity,
        Guid ownerId,
        List<string> warnings,
        CancellationToken ct)
    {
        try
        {
            var user = await _entities
                .RetrieveAsync(SystemUserEntity, ownerId, [UserBusinessUnitAttribute], ct)
                .ConfigureAwait(false);

            if (user.GetAttributeValue<EntityReference>(UserBusinessUnitAttribute) is not { Id: var buId }
                || buId == Guid.Empty)
            {
                _logger.LogInformation(
                    "[RECORD-CREATE] Owner {OwnerId} has no business unit; no BU defaults applied.", ownerId);
                return;
            }

            var businessUnit = await _entities
                .RetrieveAsync(BusinessUnitEntity, buId, [SearchIndexNameAttribute, SearchIndexLookupAttribute], ct)
                .ConfigureAwait(false);

            var indexName = businessUnit.GetAttributeValue<string>(SearchIndexNameAttribute)?.Trim();
            if (!string.IsNullOrEmpty(indexName) && !HasExplicitValue(entity, SearchIndexNameAttribute))
            {
                entity[SearchIndexNameAttribute] = indexName;
            }

            if (businessUnit.GetAttributeValue<EntityReference>(SearchIndexLookupAttribute) is { Id: var indexId }
                && indexId != Guid.Empty
                && !HasExplicitValue(entity, SearchIndexLookupAttribute))
            {
                entity[SearchIndexLookupAttribute] = new EntityReference(SearchIndexEntity, indexId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] Business-unit defaults for owner {OwnerId} could not be read; the matter is created "
                + "without them and index routing falls back to its owning business unit.",
                ownerId);
            warnings.Add(
                "Business-unit search defaults could not be read, so they were not applied. Document indexing for "
                + "this matter falls back to its business unit's settings.");
        }
    }

    private static bool HasExplicitValue(Entity entity, string attribute)
        => entity.Attributes.TryGetValue(attribute, out var value)
           && value is not null
           && !(value is string text && string.IsNullOrWhiteSpace(text));

    // ------------------------------------------------------------------------------------------------------
    // Field Mapping Framework (create-time apply) — the I/O half; rule application is CreateTimeFieldMapping.
    // ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server-side <c>FieldMappingService.applyFieldMappings</c>: exactly one profile read (the existing
    /// <see cref="IFieldMappingDataverseService.GetFieldMappingProfileWithRulesAsync"/> reader, reused), at most one
    /// source read, then <see cref="CreateTimeFieldMapping.Apply"/>. Every failure is a warning — never a throw.
    /// No <c>source == target</c> guard (same-entity mapping is supported by the framework).
    /// </summary>
    private async Task ApplyFieldMappingsAsync(
        Entity target,
        string sourceEntity,
        Guid sourceRecordId,
        List<string> warnings,
        CancellationToken ct)
    {
        FieldMappingProfileEntity? profile;
        try
        {
            profile = await _fieldMappings
                .GetFieldMappingProfileWithRulesAsync(sourceEntity, target.LogicalName, activeRulesOnly: true, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] Field-mapping profile lookup {Source} -> {Target} failed; treated as no profile.",
                sourceEntity, target.LogicalName);
            warnings.Add("Field-mapping configuration could not be read, so no fields were copied from the related record.");
            return;
        }

        if (profile is null)
        {
            // No profile for this pair: the graceful no-op (no warning), same as the client engine's 404.
            return;
        }

        var rules = profile.Rules ?? [];
        if (rules.Count == 0)
        {
            warnings.Add($"Field-mapping profile \"{profile.Name}\" has no rules; nothing was applied.");
            return;
        }

        // One source read spanning every Copy field and every Concat/Template placeholder — never one per rule.
        var sourceColumns = CreateTimeFieldMapping.CollectSourceColumns(rules);
        Entity? source = null;
        if (sourceColumns.Count > 0)
        {
            try
            {
                source = await _entities
                    .RetrieveAsync(sourceEntity, sourceRecordId, sourceColumns.ToArray(), ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[RECORD-CREATE] Source record {Source}({SourceId}) could not be read for field mapping.",
                    sourceEntity, sourceRecordId);
                warnings.Add(
                    "The related record could not be read, so its Copy/Concat/Template field mappings were skipped.");
            }
        }

        CreateTimeFieldMapping.Apply(rules, target, source, ProtectedAttributes, warnings, _logger);
    }

    private static bool TryParseGuid(string? value, out Guid id)
        => Guid.TryParse(value?.Trim(), out id) && id != Guid.Empty;
}
