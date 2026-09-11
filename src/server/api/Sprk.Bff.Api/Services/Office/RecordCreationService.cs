// spaarkeai-word-add-in-r1 task 030 (FR-13): the shared server-side record-creation service.
//
// Placement Justification (bff-extensions.md) and the CLAUDE.md §11 three-question answers are in
// projects/spaarkeai-word-add-in-r1/notes/030-creation-service-decisions.md §3-§4. In short: it runs inline in
// POST /api/office/quickcreate/{entityType} (a user is waiting; ADR-001 rules out Functions), it composes the
// existing IGenericEntityService + IFieldMappingDataverseService seams (no new Dataverse client), and it is the
// one implementation task 031 (Project) and the post-r1 wizard-migration evaluation are meant to call.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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

    /// <summary>Optional <c>sprk_mattertype_ref</c> id. Drives <c>sprk_mattertype</c> and the number's type code.</summary>
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
    /// <summary>The request cannot be honoured as given (wrong entity type, blank name, empty type id).</summary>
    InvalidInput,

    /// <summary>The caller has no resolvable Dataverse user, so the record cannot be owned by them.</summary>
    OwnerUnresolved,

    /// <summary>The supplied matter type does not exist.</summary>
    MatterTypeNotFound,

    /// <summary>The matter type could not be read, so the number cannot be derived.</summary>
    MatterTypeLookupFailed,

    /// <summary>The matter type's code is not usable for the <c>{CODE}-{6 digits}</c> format.</summary>
    MatterTypeCodeUnusable,

    /// <summary>Every candidate number collided (the uniqueness probe exhausted its attempts).</summary>
    NumberExhausted,

    /// <summary>The uniqueness probe itself failed or gave no answer, so no candidate could be verified.</summary>
    NumberProbeFailed
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

    /// <summary>The number assigned (Matter: <c>sprk_matternumber</c>), or null when none could be derived.</summary>
    public string? Number { get; init; }

    /// <summary>Non-fatal diagnostics (field-mapping skips, missing number, BU defaults unavailable).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Set when the creation was refused; <see langword="null"/> on success.</summary>
    public RecordCreationFailure? Failure { get; init; }

    /// <summary>True when a record was created.</summary>
    public bool Succeeded => Failure is null;

    internal static RecordCreationResult Failed(RecordCreationFailure failure) => new() { Failure = failure };
}

/// <summary>
/// Creates Dataverse records server-side with the completeness the client <c>Create*Wizard</c> components give
/// them in the browser: a generated number, a load-bearing owner, business-unit defaults, and the Field Mapping
/// Framework. r1 scope is <b>Matter only</b> (FR-13; task 031 owns Project, whose semantics differ).
/// </summary>
/// <remarks>
/// <para><b>Matter pipeline</b> (mirrors <c>matterService.createMatter</c>, with its invariants hardened):</para>
/// <list type="number">
///   <item><description>Name / description; the requested <c>sprk_mattertype</c>.</description></item>
///   <item><description>BU defaults from the OWNER's business unit — <c>sprk_searchindexname</c> (INV-5 guarded) and
///   the <c>sprk_ai_search_index</c> lookup — mirroring <c>EntityCreationService.applyUserBuDefaults</c>.
///   <c>sprk_containerid</c> is deliberately NOT written (unified-access-control-r2 task 076, W1).</description></item>
///   <item><description>Field Mapping Framework — one profile read, one source read, rules applied by
///   <see cref="CreateTimeFieldMapping"/>; a missing profile is a silent no-op.</description></item>
///   <item><description>Number — <c>{sprk_mattertypecode}-{6 digits}</c> from the type actually on the payload
///   (requested or mapped), uniqueness-probed, at most <see cref="MaxNumberAttempts"/> candidates, structured
///   failure on exhaustion.</description></item>
///   <item><description>Owner — <c>ownerid</c> = the caller, refused when unresolved.</description></item>
/// </list>
/// <para><b>Deliberate deviations from the client engine</b> (notes/030 §8): the client applies field mapping last,
/// so a rule could overwrite the number. Here mapping runs BEFORE numbering (so the type code matches the type
/// written) and may not touch <c>sprk_matternumber</c>, <c>ownerid</c> or <c>sprk_containerid</c>; a mapping that
/// blanks the name or writes a non-matter-type value into <c>sprk_mattertype</c> is reverted with a warning.</para>
/// <para><b>ADR-044</b>: every GUID here is a <see cref="Guid"/> value (canonical by construction) or parsed with
/// <see cref="Guid.TryParse(string?, out Guid)"/>, which accepts registry-format input; the uniqueness probe and every
/// read are typed SDK queries — no GUID or candidate is interpolated into an OData string.</para>
/// <para><b>ADR-010</b>: concrete, one registration (<c>OfficeModule</c>). No interface — there is one
/// implementation, and the contract test substitutes the Dataverse boundary, not this class.</para>
/// </remarks>
public sealed class RecordCreationService
{
    internal const string MatterEntity = "sprk_matter";
    internal const string MatterIdAttribute = "sprk_matterid";
    internal const string MatterNameAttribute = "sprk_mattername";
    internal const string MatterDescriptionAttribute = "sprk_matterdescription";
    internal const string MatterNumberAttribute = "sprk_matternumber";
    internal const string MatterTypeAttribute = "sprk_mattertype";
    internal const string MatterTypeEntity = "sprk_mattertype_ref";
    internal const string MatterTypeCodeAttribute = "sprk_mattertypecode";
    internal const string OwnerAttribute = "ownerid";
    internal const string ContainerAttribute = "sprk_containerid";
    internal const string SystemUserEntity = "systemuser";
    internal const string UserBusinessUnitAttribute = "businessunitid";
    internal const string BusinessUnitEntity = "businessunit";
    internal const string SearchIndexNameAttribute = "sprk_searchindexname";
    internal const string SearchIndexLookupAttribute = "sprk_ai_search_index";
    internal const string SearchIndexEntity = "sprk_aisearchindex";

    /// <summary>
    /// Candidate numbers probed before giving up. The task's "retry up to 5 times / exhausts 5 retries" is
    /// implemented as five probed candidates in total (notes/030 §6).
    /// </summary>
    internal const int MaxNumberAttempts = 5;

    /// <summary>Six-digit suffix range — identical to the wizard's <c>100000 + random * 900000</c>.</summary>
    private const int NumberSuffixMin = 100000;
    private const int NumberSuffixMaxExclusive = 1000000;

    /// <summary>
    /// The live type codes (LITG, CMRCL, PAT, TMRK, EMPL — verified 2026-09-11) are upper-case letters, and the
    /// shipped numbers are <c>^[A-Z]+-\d{6}$</c>. A code outside that shape would mint an off-format number, so it
    /// is refused rather than written.
    /// </summary>
    private static readonly Regex TypeCodePattern =
        new("^[A-Z]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Attributes a field-mapping rule may not write: the probed number, the load-bearing owner, and the storage
    /// container (server-derived only — task 076 W1).
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
    /// Creates the record. Returns a structured <see cref="RecordCreationFailure"/> — never a partial or
    /// colliding write — when the record cannot be created with its invariants intact. Dataverse transport or
    /// rejection errors on the final create propagate (the caller's generic 500 path, unchanged from before).
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

        // The requested type is validated up front: a request naming a type that does not exist is refused
        // before anything else is read.
        var typeCodes = new Dictionary<Guid, string?>();
        EntityReference? requestedType = null;
        if (request.MatterTypeId is { } requestedTypeId)
        {
            if (requestedTypeId == Guid.Empty)
            {
                return RecordCreationResult.Failed(new RecordCreationFailure(
                    RecordCreationFailureKind.InvalidInput, "matter_type_invalid", "The matter type id is empty."));
            }

            var (code, failure) = await ReadMatterTypeCodeAsync(requestedTypeId, ct).ConfigureAwait(false);
            if (failure is not null)
            {
                return RecordCreationResult.Failed(failure);
            }

            typeCodes[requestedTypeId] = code;
            requestedType = new EntityReference(MatterTypeEntity, requestedTypeId);
            entity[MatterTypeAttribute] = requestedType;
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

        var finalType = ResolveFinalMatterType(entity, requestedType, warnings);

        // Number from the type ACTUALLY on the payload — requested, or written by a mapping rule.
        string? number = null;
        if (finalType is null)
        {
            // Interim behaviour pending the owner decision recorded in notes/030 §7: the format needs a type code
            // and a name-only request carries none. Nothing is invented; the gap is surfaced.
            warnings.Add(
                "No matter number was assigned because no matter type was supplied. A matter number is "
                + "{type code}-{6 digits}, so it needs a matter type. Open the matter and set its type to number it.");
        }
        else
        {
            if (!typeCodes.TryGetValue(finalType.Id, out var code))
            {
                var (mappedCode, failure) = await ReadMatterTypeCodeAsync(finalType.Id, ct).ConfigureAwait(false);
                if (failure is not null)
                {
                    return RecordCreationResult.Failed(failure);
                }

                code = mappedCode;
            }

            if (string.IsNullOrWhiteSpace(code) || !TypeCodePattern.IsMatch(code))
            {
                _logger.LogError(
                    "[RECORD-CREATE] Matter type {MatterTypeId} has code '{TypeCode}', which cannot produce a "
                    + "{{CODE}}-{{6 digits}} number. Refusing rather than writing an off-format number.",
                    finalType.Id, code);

                return RecordCreationResult.Failed(new RecordCreationFailure(
                    RecordCreationFailureKind.MatterTypeCodeUnusable,
                    "matter_type_code_unusable",
                    "The matter type's code cannot be used to number a matter (a matter number is "
                    + "{type code}-{6 digits}, and the code must be upper-case letters). The matter was not "
                    + "created. Ask an administrator to correct the matter type's code."));
            }

            var (generated, numberFailure) = await GenerateUniqueMatterNumberAsync(code, ct).ConfigureAwait(false);
            if (numberFailure is not null)
            {
                return RecordCreationResult.Failed(numberFailure);
            }

            number = generated;
            entity[MatterNumberAttribute] = number;
        }

        // Set LAST, after mapping, so nothing can overwrite it — owner attribution is load-bearing (task 030 step 4).
        entity[OwnerAttribute] = new EntityReference(SystemUserEntity, ownerId);

        var createdId = await _entities.CreateAsync(entity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[RECORD-CREATE] Matter {MatterId} created for caller {CallerUserId}: number={Number}, owner={OwnerId}, "
            + "warnings={WarningCount}",
            createdId, request.CallerUserId, number ?? "(none)", ownerId, warnings.Count);

        return new RecordCreationResult
        {
            RecordId = createdId,
            LogicalName = MatterEntity,
            Name = (string)entity[MatterNameAttribute],
            Number = number,
            Warnings = warnings
        };
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
    /// The matter type the number is derived from. A mapping rule that wrote something other than a
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
    // Numbering
    // ------------------------------------------------------------------------------------------------------

    private async Task<(string? Code, RecordCreationFailure? Failure)> ReadMatterTypeCodeAsync(
        Guid matterTypeId,
        CancellationToken ct)
    {
        try
        {
            var row = await _entities
                .RetrieveAsync(MatterTypeEntity, matterTypeId, [MatterTypeCodeAttribute], ct)
                .ConfigureAwait(false);

            return (row.GetAttributeValue<string>(MatterTypeCodeAttribute)?.Trim(), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFound(ex))
        {
            _logger.LogWarning(ex, "[RECORD-CREATE] Matter type {MatterTypeId} does not exist.", matterTypeId);
            return (null, new RecordCreationFailure(
                RecordCreationFailureKind.MatterTypeNotFound,
                "matter_type_not_found",
                "The selected matter type does not exist. The matter was not created."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RECORD-CREATE] Matter type {MatterTypeId} could not be read.", matterTypeId);
            return (null, new RecordCreationFailure(
                RecordCreationFailureKind.MatterTypeLookupFailed,
                "matter_type_lookup_failed",
                "The matter type could not be read, so a matter number could not be generated. The matter was "
                + "not created. Please try again."));
        }
    }

    /// <summary>
    /// Generate-then-probe, at most <see cref="MaxNumberAttempts"/> times. A candidate is only ever returned after
    /// the probe has confirmed no <c>sprk_matter</c> row carries it; a probe error — or a probe that returns no
    /// answer at all — returns a failure rather than an unverified value.
    /// </summary>
    /// <remarks>
    /// Residual (documented, notes/030 §6): probe-then-create leaves a TOCTOU window. Closing it outright needs a
    /// Dataverse alternate key on <c>sprk_matternumber</c> — a schema change outside this task.
    /// </remarks>
    private async Task<(string? Number, RecordCreationFailure? Failure)> GenerateUniqueMatterNumberAsync(
        string typeCode,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxNumberAttempts; attempt++)
        {
            var suffix = RandomNumberGenerator.GetInt32(NumberSuffixMin, NumberSuffixMaxExclusive);
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{typeCode}-{suffix}");

            bool taken;
            try
            {
                taken = await IsMatterNumberTakenAsync(candidate, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[RECORD-CREATE] Uniqueness probe for matter number {Candidate} failed on attempt {Attempt}. "
                    + "Refusing rather than writing an unverified number.",
                    candidate, attempt);

                return (null, new RecordCreationFailure(
                    RecordCreationFailureKind.NumberProbeFailed,
                    "matter_number_probe_failed",
                    "The new matter number could not be checked for uniqueness, so the matter was not created. "
                    + "Please try again."));
            }

            if (!taken)
            {
                return (candidate, null);
            }

            _logger.LogWarning(
                "[RECORD-CREATE] Matter number candidate {Candidate} is already in use (attempt {Attempt} of {Max}).",
                candidate, attempt, MaxNumberAttempts);
        }

        _logger.LogError(
            "[RECORD-CREATE] No unused matter number found for type code {TypeCode} after {Max} candidates.",
            typeCode, MaxNumberAttempts);

        return (null, new RecordCreationFailure(
            RecordCreationFailureKind.NumberExhausted,
            "matter_number_unavailable",
            $"No unused matter number could be found after {MaxNumberAttempts} attempts, so the matter was not "
            + "created. Please try again."));
    }

    /// <summary>
    /// The mandated uniqueness probe: a retrieve filtered on <c>sprk_matternumber eq {candidate}</c>, expressed as a
    /// typed <see cref="ConditionExpression"/> (no string-built filter). Inactive rows count — uniqueness is global.
    /// A missing result is an unanswered question, not "free", so it throws into the caller's probe-failure path.
    /// </summary>
    private async Task<bool> IsMatterNumberTakenAsync(string candidate, CancellationToken ct)
    {
        var query = new QueryExpression(MatterEntity)
        {
            ColumnSet = new ColumnSet(MatterIdAttribute),
            TopCount = 1
        };
        query.Criteria.AddCondition(MatterNumberAttribute, ConditionOperator.Equal, candidate);

        var matches = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (matches?.Entities is not { } rows)
        {
            throw new InvalidOperationException("The matter-number uniqueness probe returned no result.");
        }

        return rows.Count > 0;
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
