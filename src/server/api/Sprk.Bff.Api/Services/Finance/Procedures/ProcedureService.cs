using System.Data;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

/// <summary>
/// The Procedure tables' names and option values (ebilling-r1 task 080, read live on spaarkedev1 2026-10-22: notes/080-procedure-schema.md and
/// notes/088-stored-file-schema.md). Global choices start at 100000000 (ontology F3-a).
/// </summary>
public static class ProcedureEntity
{
    public const string Entity = "sprk_procedure";
    public const string EntitySet = "sprk_procedures";
    public const string VersionEntity = "sprk_procedureversion";
    public const string VersionEntitySet = "sprk_procedureversions";
    public const string StoredFileEntity = "sprk_storedfile";

    // sprk_procedure
    public const string NameColumn = "sprk_name";
    public const string FirmFacingNameColumn = "sprk_firmfacingname";
    public const string AreaColumn = "sprk_area";
    public const string TypeColumn = "sprk_proceduretype";
    public const string AppliesToColumn = "sprk_appliesto";
    public const string FirmColumn = "sprk_firm";
    public const string IssuedByColumn = "sprk_issuedby";
    public const string ExternalContactColumn = "sprk_externalcontact";
    public const string InternalContactColumn = "sprk_internalcontact";
    public const string StatusColumn = "sprk_status";
    public const string NotesColumn = "sprk_notes";

    public const int StatusActive = 100000000;
    public const int StatusRetired = 100000001;

    // sprk_procedureversion
    public const string VersionProcedureColumn = "sprk_procedure";
    public const string VersionNumberColumn = "sprk_versionnumber";
    public const string VersionPreviousColumn = "sprk_previousversion";
    public const string VersionEffectiveFromColumn = "sprk_effectivefrom";
    public const string VersionEffectiveToColumn = "sprk_effectiveto";
    public const string VersionStatusColumn = "sprk_status";

    public const int VersionStatusDraft = 100000000;
    public const int VersionStatusActive = 100000001;
    public const int VersionStatusSuperseded = 100000002;
    public const int VersionStatusRetired = 100000003;

    // sprk_storedfile (088)
    public const string StoredFileProcedureVersionColumn = "sprk_procedureversion";
    public const string StoredFilePurposeColumn = "sprk_purpose";
    public const string StoredFileStatusColumn = "sprk_filestatus";
    public const int StoredFilePurposeProcedureSource = 100000000;
    public const int StoredFileStatusServable = 100000001;
}

/// <summary>A Procedure's Type (<c>sprk_proceduretype</c>, global choice). Values are the option values.</summary>
public enum ProcedureType
{
    Guideline = 100000000,
    RateCard = 100000001,
    BillingInstructions = 100000002,
    InternalStandard = 100000003,
    Other = 100000004,
}

/// <summary>Who a Procedure governs (<c>sprk_appliesto</c>, global choice). Values are the option values.</summary>
public enum ProcedureAppliesTo
{
    AllRecords = 100000000,
    Firm = 100000001,
    AssignedRecordsOnly = 100000002,
}

/// <summary>Create a Procedure. Every id member is optional; Type and Applies to are names (<see cref="ProcedureType"/>, <see cref="ProcedureAppliesTo"/>).</summary>
public sealed record ProcedureCreateRequest(
    string? Name, string? FirmFacingName, string? Type, string? AppliesTo, Guid? FirmId, Guid? IssuedById, Guid? ExternalContactId,
    Guid? InternalContactId, string? Notes);

/// <summary>Change a Procedure. A null member is left as it is; <see cref="ClearFirm"/> removes the firm.</summary>
public sealed record ProcedureUpdateRequest(
    string? Name, string? FirmFacingName, string? Type, string? AppliesTo, Guid? FirmId, bool ClearFirm, Guid? IssuedById,
    Guid? ExternalContactId, Guid? InternalContactId, string? Notes);

/// <summary>One Procedure Version as the admin sees it.</summary>
public sealed record ProcedureVersionView(
    Guid Id, int VersionNumber, string Status, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, Guid? PreviousVersionId);

/// <summary>A Procedure with its versions (internal callers only; step 0 and the caller's Read on the Procedure guard the route).</summary>
public sealed record ProcedureView(
    Guid Id, string Name, string? FirmFacingName, string Type, string AppliesTo, Guid? FirmId, Guid? IssuedById, Guid? ExternalContactId,
    Guid? InternalContactId, string Status, string? Notes, Guid? RuleSetId, IReadOnlyList<ProcedureVersionView> Versions);

/// <summary>A new Procedure Version, and how many rules it copied from the version before it (model 7.2).</summary>
public sealed record ProcedureVersionCreated(ProcedureVersionView Version, Guid? CopiedFromVersionId, int RulesCopied);

/// <summary>A published Procedure Version: the version it superseded, and the rules it put in force.</summary>
public sealed record ProcedureVersionPublished(
    ProcedureVersionView Version, Guid? SupersededVersionId, IReadOnlyList<PublishedRuleVersion> Rules, int RulesRejected);

/// <summary>
/// The read-only confirmation shown before a Procedure is retired (model 15 answer 2). <see cref="AffectedText"/> and
/// <see cref="VisibleOpenInvoices"/> are the caller-scoped count ("N open invoices you can see are affected."), both null when it cannot be told.
/// There is deliberately no other number and no "others hidden" flag.
/// </summary>
public sealed record ProcedureRetirePreview(string Warning, string? AffectedText, int? VisibleOpenInvoices);

/// <summary>
/// A retired Procedure. <see cref="CloseComplete"/> is false when some open findings could not be closed now (the policies stay Off, so the
/// job's D-170 sweep closes them); the count is caller-scoped, as the preview's.
/// </summary>
public sealed record ProcedureRetired(
    Guid ProcedureId, string Status, int RulesSwitchedOff, bool CloseComplete, string? AffectedText, int? VisibleOpenInvoices, string? Message);

/// <summary>
/// The Procedure and Procedure Version service (ebilling-r1 task 081, EB-D-148; procedure model sections 3, 7.2, 8.2 and 15 answers 1-2):
/// create a Procedure with its one rule set, create a version (copying the rules of the version before it), change a version's dates, publish
/// it, retire it, and retire the Procedure behind a read-only, caller-scoped confirmation preview.
/// </summary>
/// <remarks>
/// <para><b>The BFF owns the invariants (ADR-002; no plugin).</b> I-P1: dates are DateOnly, half-open [from, to); in-force (Active or
/// Superseded) versions never overlap; publishing version n from D sets the previous Active version's <c>to</c> = D and marks it Superseded in
/// the SAME transaction as version n going Active and its rules going Published, so the predecessor write failing leaves nothing Active
/// (<see cref="IGenericEntityService.ExecuteTransactionAsync"/> is all or nothing). I-P3: a Rate card names its firm
/// (<c>procedure.firm_required</c>). I-P4: once any version has been in force (Active, Superseded, or Retired after a publish), Applies to and Firm are locked (<c>procedure.scope_locked</c>).
/// I-P5: retiring a version closes nothing and leaves its policies enabled; retiring the Procedure switches every policy of its rule set Off
/// and closes their open Signals PolicyRetired through <see cref="SignalClosureService.ClosePolicySignalsRetiredAsync"/>, the same selection
/// the preview counts from (<see cref="SignalClosureService.SelectOpenSignalsOfPolicyAsync"/>). Both opt in to the invoice subject columns
/// (<c>withBillingSubject</c>); the job sweep and the admin retire close select the same rows without them (ontology review F1).</para>
/// <para><b>Identity.</b> The routes have already refused flagged-external callers (step 0) and checked the caller's own rights (Write or Read
/// on the Procedure, the billing-admin privilege, or Create on <c>sprk_procedure</c>). The writes then run as the BFF application user, because
/// the dates, status, Applies to and Firm are field-secured to it (I-P2). A new Procedure and its versions are owned by the caller's
/// business-unit team (<see cref="IRecordOwnershipResolver"/> with no parents, model 5.1); the rule set and the copied policy versions are
/// hard-deny rows, asked through <see cref="RuleSetService.ContentOwnerContext"/> (EB-D-168 / EB-D-168a: the restricted-content owner team).
/// Every new row records the caller as creator person.</para>
/// <para><b>Serialised on the Procedure row.</b> Every write includes a conditional update of the Procedure row (row version as read), so a
/// publish, a date change, a version create or a scope change racing a retire (or each other) is refused 409 <c>procedure.concurrent_edit</c>
/// with nothing written, never interleaved.</para>
/// <para>§11: <i>Existing</i> — <see cref="RuleSetService"/> holds rule sets and their policy versions; no service writes
/// <c>sprk_procedure</c> or <c>sprk_procedureversion</c> (080 created them). <i>Extension</i> — the rule-level work (copy-forward, the
/// re-keyed publish) is added to <see cref="RuleSetService"/>; a Procedure is the document and its versions, not the rules (model 3.3, O-1,
/// O-15), so its lifecycle is this new service. <i>Cost of doing nothing</i> — no Procedure or version can be created or published, so no
/// billing rule is ever in force under 082's resolver, and retiring a Procedure has no safe path.</para>
/// </remarks>
public class ProcedureService
{
    // Stable error codes (ADR-019).
    internal const string CodeInvalid = "procedure.invalid";
    internal const string CodeFirmRequired = "procedure.firm_required";
    internal const string CodeNotFound = "procedure.not_found";
    internal const string CodeVersionNotFound = "procedure.version_not_found";
    internal const string CodeScopeLocked = "procedure.scope_locked";
    internal const string CodeVersionOverlap = "procedure.version_overlap";
    internal const string CodeRetired = "procedure.retired";
    internal const string CodeDraftExists = "procedure.draft_exists";
    internal const string CodeVersionNotDraft = "procedure.version_not_draft";
    internal const string CodeVersionNotEditable = "procedure.version_not_editable";
    internal const string CodePublishInvalid = "procedure.publish_invalid";
    internal const string CodeConcurrentEdit = "procedure.concurrent_edit";
    internal const string CodeVersionConflict = "procedure.version_conflict";
    internal const string CodeTooManyRules = "procedure.too_many_rules";
    internal const string CodeRuleSetMissing = "procedure.ruleset_missing";
    internal const string CodeWriteFailed = "procedure.write_failed";

    /// <summary>The scope-lock refusal text (model 3.1 I-P4).</summary>
    internal const string ScopeLockedDetail = "Create a new Procedure to change who it governs.";

    /// <summary>The retire confirmation warning (model 15 answer 2), verbatim.</summary>
    internal const string RetireWarning =
        "Retiring this Procedure will close all related rules and remove pending decisions on active invoices.";

    /// <summary>The most requests one transaction carries (Dataverse's 1,000-request bound, as <see cref="RuleSetService.MaxPerTransaction"/>).</summary>
    internal const int MaxPerTransaction = RuleSetService.MaxPerTransaction;

    private const int MaxNameChars = 200;

    private static readonly string[] ProcedureColumns =
    {
        ProcedureEntity.NameColumn, ProcedureEntity.FirmFacingNameColumn, ProcedureEntity.AreaColumn, ProcedureEntity.TypeColumn,
        ProcedureEntity.AppliesToColumn, ProcedureEntity.FirmColumn, ProcedureEntity.IssuedByColumn, ProcedureEntity.ExternalContactColumn,
        ProcedureEntity.InternalContactColumn, ProcedureEntity.StatusColumn, ProcedureEntity.NotesColumn, RuleSetService.RowVersionColumn,
    };

    private static readonly string[] VersionColumns =
    {
        ProcedureEntity.VersionProcedureColumn, ProcedureEntity.VersionNumberColumn, ProcedureEntity.VersionPreviousColumn,
        ProcedureEntity.VersionEffectiveFromColumn, ProcedureEntity.VersionEffectiveToColumn, ProcedureEntity.VersionStatusColumn,
        RuleSetService.RowVersionColumn,
    };

    private readonly IGenericEntityService _entities;
    private readonly RuleSetService _ruleSets;
    private readonly SignalClosureService _closure;
    private readonly AffectedInvoiceCounter _counter;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly ILogger<ProcedureService> _logger;

    public ProcedureService(
        IGenericEntityService entities,
        RuleSetService ruleSets,
        SignalClosureService closure,
        AffectedInvoiceCounter counter,
        IRecordOwnershipResolver ownership,
        ILogger<ProcedureService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ruleSets = ruleSets ?? throw new ArgumentNullException(nameof(ruleSets));
        _closure = closure ?? throw new ArgumentNullException(nameof(closure));
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Procedure
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a Procedure (Billing area, Active) and its one Draft rule set in one transaction. Writes nothing when the input is invalid.</summary>
    public virtual async Task<RuleSetOutcome<ProcedureView>> CreateAsync(ProcedureCreateRequest request, Guid? callerObjectId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<RuleSetError>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaxNameChars)
        {
            errors.Add(Error("name", $"name is required and may be at most {MaxNameChars} characters."));
        }

        var firmFacing = string.IsNullOrWhiteSpace(request.FirmFacingName) ? name : request.FirmFacingName.Trim();
        if (firmFacing.Length > MaxNameChars)
        {
            errors.Add(Error("firmFacingName", $"firmFacingName may be at most {MaxNameChars} characters."));
        }

        if (!TryParse<ProcedureType>(request.Type, out var type))
        {
            errors.Add(Error("type", "type must be Guideline, RateCard, BillingInstructions, InternalStandard or Other."));
        }

        var appliesTo = ProcedureAppliesTo.AllRecords;
        if (request.AppliesTo is not null && !TryParse(request.AppliesTo, out appliesTo))
        {
            errors.Add(Error("appliesTo", "appliesTo must be AllRecords, Firm or AssignedRecordsOnly."));
        }

        await CheckLookupsAsync(errors, request.FirmId, request.IssuedById, request.ExternalContactId, request.InternalContactId, ct);
        if (errors.Count > 0)
        {
            return RuleSetOutcome<ProcedureView>.Invalid(CodeInvalid, errors);
        }

        if (FirmRequired(type, appliesTo) && request.FirmId is null)
        {
            return RuleSetOutcome<ProcedureView>.Invalid(CodeFirmRequired, new[] { FirmRequiredError(type) });
        }

        var owner = await _ownership.ResolveOwnerAsync(RuleSetService.CallerOwnerContext(callerObjectId), ct);
        if (!owner.IsOwned)
        {
            return RuleSetOutcome<ProcedureView>.OwnerRefused(owner);
        }

        // The Procedure's rule set is a hard-deny row (EB-D-168a): its own question, before anything is written.
        var ruleSetOwner = await _ownership.ResolveOwnerAsync(RuleSetService.ContentOwnerContext(RuleSetService.RuleSetEntity, callerObjectId), ct);
        if (!ruleSetOwner.IsOwned)
        {
            return RuleSetOutcome<ProcedureView>.OwnerRefused(ruleSetOwner);
        }

        var procedureId = Guid.NewGuid();
        var procedure = new Entity(ProcedureEntity.Entity, procedureId)
        {
            [ProcedureEntity.NameColumn] = name,
            [ProcedureEntity.FirmFacingNameColumn] = firmFacing,
            [ProcedureEntity.AreaColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
            [ProcedureEntity.TypeColumn] = new OptionSetValue((int)type),
            [ProcedureEntity.AppliesToColumn] = new OptionSetValue((int)appliesTo),
            [ProcedureEntity.StatusColumn] = new OptionSetValue(ProcedureEntity.StatusActive),
        };
        SetLookup(procedure, ProcedureEntity.FirmColumn, RuleSetService.FirmEntity, request.FirmId);
        SetLookup(procedure, ProcedureEntity.IssuedByColumn, RuleSetService.FirmEntity, request.IssuedById);
        SetLookup(procedure, ProcedureEntity.ExternalContactColumn, "contact", request.ExternalContactId);
        SetLookup(procedure, ProcedureEntity.InternalContactColumn, "contact", request.InternalContactId);
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            procedure[ProcedureEntity.NotesColumn] = request.Notes;
        }

        owner.ApplyTo(procedure);
        var ruleSetId = Guid.NewGuid();
        var ruleSet = RuleSetService.NewProcedureRuleSet(ruleSetId, name, procedureId, request.FirmId, InitialVisibleToFirms(type));
        ruleSetOwner.ApplyTo(ruleSet);

        switch (await SendAsync(new OrganizationRequest[] { new CreateRequest { Target = procedure }, new CreateRequest { Target = ruleSet } }, procedureId, "create"))
        {
            case SendResult.Done:
                _logger.LogInformation("Procedure {ProcedureId} created ({Type}, {AppliesTo}) with rule set {RuleSetId}", procedureId, type, appliesTo, ruleSetId);
                return RuleSetOutcome<ProcedureView>.Done(new ProcedureView(
                    procedureId, name, firmFacing, type.ToString(), appliesTo.ToString(), request.FirmId, request.IssuedById, request.ExternalContactId,
                    request.InternalContactId, "Active", procedure.GetAttributeValue<string>(ProcedureEntity.NotesColumn), ruleSetId,
                    Array.Empty<ProcedureVersionView>()));
            case SendResult.Unknown:
                // Never "nothing was saved" here: a retry would create a second Procedure. The id lets the caller look before retrying.
                return WriteFailed<ProcedureView>(
                    $"The Procedure may or may not have been created (id {procedureId}). Open it before trying again, so it is not created twice.");
            default:
                return WriteFailed<ProcedureView>("The Procedure could not be created and nothing was saved. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>A Procedure with its versions; 404 when it does not exist.</summary>
    public virtual async Task<RuleSetOutcome<ProcedureView>> GetAsync(Guid procedureId, CancellationToken ct)
    {
        var procedure = await LoadProcedureAsync(procedureId, ct);
        if (procedure is null)
        {
            return NotFound<ProcedureView>();
        }

        var versions = await LoadVersionsAsync(procedureId, ct);
        var ruleSet = await _ruleSets.LoadProcedureRuleSetAsync(procedureId, ct);
        return RuleSetOutcome<ProcedureView>.Done(View(procedure, versions, ruleSet?.Id));
    }

    /// <summary>
    /// Changes a Procedure's details. I-P3: a Rate card keeps a firm (400 <c>procedure.firm_required</c>). I-P4: Applies to and Firm change only
    /// until a version has been in force (Active, Superseded, or Retired after a publish; 409 <c>procedure.scope_locked</c>); its rule set's scope and firm follow, in the same transaction.
    /// </summary>
    public virtual async Task<RuleSetOutcome<ProcedureView>> UpdateAsync(Guid procedureId, ProcedureUpdateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var procedure = await LoadProcedureAsync(procedureId, ct);
        if (procedure is null)
        {
            return NotFound<ProcedureView>();
        }

        if (IsRetired(procedure))
        {
            return Refused<ProcedureView>(StatusCodes.Status409Conflict, CodeRetired, "A retired Procedure cannot be changed.");
        }

        var errors = new List<RuleSetError>();
        var fields = new Dictionary<string, object>();
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length is 0 or > MaxNameChars) errors.Add(Error("name", $"name may not be blank and may be at most {MaxNameChars} characters."));
            else fields[ProcedureEntity.NameColumn] = name;
        }

        if (request.FirmFacingName is not null)
        {
            var firmFacing = request.FirmFacingName.Trim();
            if (firmFacing.Length is 0 or > MaxNameChars) errors.Add(Error("firmFacingName", $"firmFacingName may not be blank and may be at most {MaxNameChars} characters."));
            else fields[ProcedureEntity.FirmFacingNameColumn] = firmFacing;
        }

        var type = (ProcedureType)(procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.TypeColumn)?.Value ?? (int)ProcedureType.Other);
        if (request.Type is not null)
        {
            if (TryParse<ProcedureType>(request.Type, out var parsed)) { type = parsed; fields[ProcedureEntity.TypeColumn] = new OptionSetValue((int)parsed); }
            else errors.Add(Error("type", "type must be Guideline, RateCard, BillingInstructions, InternalStandard or Other."));
        }

        var currentAppliesTo = (ProcedureAppliesTo)(procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.AppliesToColumn)?.Value ?? (int)ProcedureAppliesTo.AllRecords);
        var appliesTo = currentAppliesTo;
        if (request.AppliesTo is not null)
        {
            if (TryParse<ProcedureAppliesTo>(request.AppliesTo, out var parsed)) appliesTo = parsed;
            else errors.Add(Error("appliesTo", "appliesTo must be AllRecords, Firm or AssignedRecordsOnly."));
        }

        if (request.ClearFirm && request.FirmId is not null)
        {
            errors.Add(Error("firmId", "Give firmId or clearFirm, not both."));
        }

        await CheckLookupsAsync(errors, request.FirmId, request.IssuedById, request.ExternalContactId, request.InternalContactId, ct);
        if (errors.Count > 0)
        {
            return RuleSetOutcome<ProcedureView>.Invalid(CodeInvalid, errors);
        }

        var currentFirm = procedure.GetAttributeValue<EntityReference>(ProcedureEntity.FirmColumn)?.Id;
        var firm = request.ClearFirm ? null : request.FirmId ?? currentFirm;
        if (FirmRequired(type, appliesTo) && firm is null)
        {
            return RuleSetOutcome<ProcedureView>.Invalid(CodeFirmRequired, new[] { FirmRequiredError(type) });
        }

        var versions = await LoadVersionsAsync(procedureId, ct);
        var scopeChanges = appliesTo != currentAppliesTo || firm != currentFirm;
        if (scopeChanges && await ScopeLockedAsync(versions, ct))
        {
            return Refused<ProcedureView>(StatusCodes.Status409Conflict, CodeScopeLocked, ScopeLockedDetail);
        }

        if (appliesTo != currentAppliesTo) fields[ProcedureEntity.AppliesToColumn] = new OptionSetValue((int)appliesTo);
        if (firm != currentFirm) fields[ProcedureEntity.FirmColumn] = firm is { } f ? new EntityReference(RuleSetService.FirmEntity, f) : DBNull.Value;
        if (request.IssuedById is { } issuedBy) fields[ProcedureEntity.IssuedByColumn] = new EntityReference(RuleSetService.FirmEntity, issuedBy);
        if (request.ExternalContactId is { } external) fields[ProcedureEntity.ExternalContactColumn] = new EntityReference("contact", external);
        if (request.InternalContactId is { } internalContact) fields[ProcedureEntity.InternalContactColumn] = new EntityReference("contact", internalContact);
        if (request.Notes is not null) fields[ProcedureEntity.NotesColumn] = request.Notes.Length == 0 ? DBNull.Value : request.Notes;

        if (fields.Count == 0)
        {
            return RuleSetOutcome<ProcedureView>.Invalid(CodeInvalid, new[] { Error("edit", "Nothing to change: name at least one member.") });
        }

        var requests = new List<OrganizationRequest>();
        var update = ConditionalUpdate(ProcedureEntity.Entity, procedure, fields);
        if (update is null)
        {
            return WriteFailed<ProcedureView>("The Procedure could not be changed. Reload it and try again.");
        }

        requests.Add(update);
        var ruleSet = await _ruleSets.LoadProcedureRuleSetAsync(procedureId, ct);
        var currentType = procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.TypeColumn)?.Value;
        var typeChanges = request.Type is not null && currentType != (int)type;
        if ((firm != currentFirm || typeChanges) && ruleSet is not null)
        {
            var setRow = new Entity(RuleSetService.RuleSetEntity, ruleSet.Id);
            if (firm != currentFirm)
            {
                foreach (var (column, value) in RuleSetService.ProcedureScope(firm))
                {
                    setRow[column] = value is DBNull ? null : value;
                }
            }

            // Visible to firms follows the type (set at create the same way): the rule set's own visibility route refuses a Procedure's set
            // (ruleset.procedure_owned), so the type is the one place it changes.
            if (typeChanges) setRow["sprk_visibletofirms"] = InitialVisibleToFirms(type);
            requests.Add(new UpdateRequest { Target = setRow });
        }

        switch (await SendAsync(requests, procedureId, "update"))
        {
            case SendResult.Done:
                // The answer is built from what was written (the conditional update matched the row as read), not re-read: a failed re-read
                // must not turn a committed change into an error.
                var written = new Entity(ProcedureEntity.Entity, procedureId);
                foreach (var attribute in procedure.Attributes) written[attribute.Key] = attribute.Value;
                foreach (var (column, value) in fields) written[column] = value is DBNull ? null : value;
                return RuleSetOutcome<ProcedureView>.Done(View(written, versions, ruleSet?.Id));
            case SendResult.Conflict:
                return Concurrent<ProcedureView>();
            case SendResult.Unknown:
                return WriteFailed<ProcedureView>("The change may or may not have been saved. Reload the Procedure before trying again.");
            default:
                return WriteFailed<ProcedureView>("The Procedure could not be changed and nothing was saved. Try again; if it keeps failing, contact support.");
        }
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Versions
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the next version as a Draft (number assigned here; previous version = the latest that left Draft) and, in the same transaction,
    /// copies that version's published rules as Draft policy versions of the same policies under it (model 7.2, <see cref="RuleSetService.PlanCopyForwardAsync"/>).
    /// One Draft at a time (409 <c>procedure.draft_exists</c>). A clash on the (Procedure, version number) key retries once, then 409.
    /// </summary>
    public virtual async Task<RuleSetOutcome<ProcedureVersionCreated>> CreateVersionAsync(
        Guid procedureId, DateOnly? effectiveFrom, DateOnly? effectiveTo, Guid? callerObjectId, CancellationToken ct)
    {
        if (effectiveFrom is { } from && effectiveTo is { } to && to <= from)
        {
            return RuleSetOutcome<ProcedureVersionCreated>.Invalid(CodeInvalid, new[] { Error("effectiveTo", "effectiveTo must be after effectiveFrom (the dates are half-open: [from, to)).") });
        }

        for (var attempt = 1; ; attempt++)
        {
            var procedure = await LoadProcedureAsync(procedureId, ct);
            if (procedure is null)
            {
                return NotFound<ProcedureVersionCreated>();
            }

            if (IsRetired(procedure))
            {
                return Refused<ProcedureVersionCreated>(StatusCodes.Status409Conflict, CodeRetired, "A retired Procedure cannot get a new version.");
            }

            var versions = await LoadVersionsAsync(procedureId, ct);
            if (versions.Any(v => v.Status == ProcedureEntity.VersionStatusDraft))
            {
                return Refused<ProcedureVersionCreated>(StatusCodes.Status409Conflict, CodeDraftExists,
                    "This Procedure already has a Draft version. Publish or retire it before creating another.");
            }

            var ruleSet = await _ruleSets.LoadProcedureRuleSetAsync(procedureId, ct);
            if (ruleSet is null)
            {
                return Refused<ProcedureVersionCreated>(StatusCodes.Status409Conflict, CodeRuleSetMissing, "The Procedure has no rule set.");
            }

            var owner = await _ownership.ResolveOwnerAsync(RuleSetService.CallerOwnerContext(callerObjectId), ct);
            if (!owner.IsOwned)
            {
                return RuleSetOutcome<ProcedureVersionCreated>.OwnerRefused(owner);
            }

            var number = versions.Count == 0 ? 1 : versions.Max(v => v.Number) + 1;
            var previous = versions.Where(v => v.Status is ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded)
                .MaxBy(v => v.Number);
            var versionId = Guid.NewGuid();
            var row = new Entity(ProcedureEntity.VersionEntity, versionId)
            {
                [ProcedureEntity.NameColumn] = VersionName(procedure.GetAttributeValue<string>(ProcedureEntity.NameColumn), number),
                [ProcedureEntity.VersionProcedureColumn] = new EntityReference(ProcedureEntity.Entity, procedureId),
                [ProcedureEntity.VersionNumberColumn] = number,
                [ProcedureEntity.VersionStatusColumn] = new OptionSetValue(ProcedureEntity.VersionStatusDraft),
            };
            if (previous is not null) row[ProcedureEntity.VersionPreviousColumn] = new EntityReference(ProcedureEntity.VersionEntity, previous.Id);
            if (effectiveFrom is { } f) row[ProcedureEntity.VersionEffectiveFromColumn] = ToDate(f);
            if (effectiveTo is { } t) row[ProcedureEntity.VersionEffectiveToColumn] = ToDate(t);
            owner.ApplyTo(row);

            var requests = new List<OrganizationRequest> { new CreateRequest { Target = row } };
            var copied = 0;
            if (previous is not null)
            {
                // The copied policy versions are hard-deny rows (EB-D-168a): their own question, before anything is written.
                var copyOwner = await _ownership.ResolveOwnerAsync(
                    RuleSetService.ContentOwnerContext(RuleSetService.VersionEntity, callerObjectId), ct);
                if (!copyOwner.IsOwned)
                {
                    return RuleSetOutcome<ProcedureVersionCreated>.OwnerRefused(copyOwner);
                }

                var plan = await _ruleSets.PlanCopyForwardAsync(ruleSet.Id, previous.Id, versionId, copyOwner, ct);
                if (plan.Truncated)
                {
                    return Refused<ProcedureVersionCreated>(StatusCodes.Status422UnprocessableEntity, CodeTooManyRules,
                        "The Procedure has more rules than can be copied in one pass.");
                }

                if (plan.Errors.Count > 0)
                {
                    return RuleSetOutcome<ProcedureVersionCreated>.Invalid(CodeInvalid, plan.Errors);
                }

                requests.AddRange(plan.Creates);
                copied = plan.Copied;
            }

            if (Touch(procedure) is not { } touch)
            {
                return WriteFailed<ProcedureVersionCreated>("The version could not be created. Reload the Procedure and try again.");
            }

            requests.Add(touch);
            if (requests.Count > MaxPerTransaction)
            {
                return Refused<ProcedureVersionCreated>(StatusCodes.Status422UnprocessableEntity, CodeTooManyRules,
                    "The Procedure has more rules than can be copied in one pass.");
            }

            var sent = await SendAsync(requests, procedureId, "create version");
            if (sent == SendResult.Done)
            {
                _logger.LogInformation("Procedure {ProcedureId}: version {Number} {VersionId} created as Draft, {Copied} rule(s) copied from {Previous}",
                    procedureId, number, versionId, copied, previous?.Id);
                return RuleSetOutcome<ProcedureVersionCreated>.Done(new ProcedureVersionCreated(
                    new ProcedureVersionView(versionId, number, "Draft", effectiveFrom, effectiveTo, previous?.Id), previous?.Id, copied));
            }

            if (sent == SendResult.Unknown)
            {
                // No fault came back: the version may exist. A retry could create a second Draft; the admin reloads instead.
                return WriteFailed<ProcedureVersionCreated>("The version may or may not have been created. Reload the Procedure before trying again.");
            }

            // A clash on the (Procedure, version number) key, or the Procedure row moved (another create landed first): re-read and retry once.
            if ((sent is SendResult.Conflict or SendResult.Failed) && attempt == 1
                && (sent == SendResult.Conflict || await VersionNumberTakenAsync(procedureId, number, ct)))
            {
                _logger.LogWarning("Procedure {ProcedureId}: version {Number} was taken (or the Procedure changed) while creating it; retrying once", procedureId, number);
                continue;
            }

            return sent == SendResult.Conflict || await VersionNumberTakenAsync(procedureId, number, ct)
                ? Refused<ProcedureVersionCreated>(StatusCodes.Status409Conflict, CodeVersionConflict,
                    "Another version of this Procedure was created at the same time. Reload the Procedure.")
                : WriteFailed<ProcedureVersionCreated>("The version could not be created and nothing was saved. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>
    /// Changes a version's dates (I-P1, half-open). A Draft's dates change freely; an Active version's change only if no other in-force version
    /// (Active or Superseded) then overlaps (409 <c>procedure.version_overlap</c>, nothing written). Superseded and Retired versions are not
    /// changed here (409 <c>procedure.version_not_editable</c>).
    /// </summary>
    public virtual async Task<RuleSetOutcome<ProcedureVersionView>> ChangeDatesAsync(
        Guid procedureId, Guid versionId, DateOnly? effectiveFrom, DateOnly? effectiveTo, bool clearEffectiveTo, CancellationToken ct)
    {
        if (effectiveFrom is null && effectiveTo is null && !clearEffectiveTo)
        {
            return RuleSetOutcome<ProcedureVersionView>.Invalid(CodeInvalid, new[] { Error("effectiveFrom", "Name effectiveFrom, effectiveTo or clearEffectiveTo.") });
        }

        if (effectiveTo is not null && clearEffectiveTo)
        {
            return RuleSetOutcome<ProcedureVersionView>.Invalid(CodeInvalid, new[] { Error("effectiveTo", "Give effectiveTo or clearEffectiveTo, not both.") });
        }

        var (procedure, versions, version, refusal) = await LoadForVersionAsync<ProcedureVersionView>(procedureId, versionId, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        if (version!.Status is not (ProcedureEntity.VersionStatusDraft or ProcedureEntity.VersionStatusActive))
        {
            return Refused<ProcedureVersionView>(StatusCodes.Status409Conflict, CodeVersionNotEditable,
                "Only a Draft or Active version's dates can be changed.");
        }

        var from = effectiveFrom ?? version.From;
        var to = clearEffectiveTo ? null : effectiveTo ?? version.To;
        if (from is { } f && to is { } t && t <= f)
        {
            return RuleSetOutcome<ProcedureVersionView>.Invalid(CodeInvalid, new[] { Error("effectiveTo", "effectiveTo must be after effectiveFrom (the dates are half-open: [from, to)).") });
        }

        if (version.Status == ProcedureEntity.VersionStatusActive)
        {
            if (from is null)
            {
                return RuleSetOutcome<ProcedureVersionView>.Invalid(CodeInvalid, new[] { Error("effectiveFrom", "An Active version keeps an effectiveFrom.") });
            }

            if (versions.Where(v => v.Id != versionId && IsInForce(v)).Any(v => Overlaps(from.Value, to, v.From!.Value, v.To)))
            {
                return Refused<ProcedureVersionView>(StatusCodes.Status409Conflict, CodeVersionOverlap,
                    "These dates overlap another version in force. A Procedure's versions in force never overlap.");
            }
        }

        var fields = new Dictionary<string, object>();
        if (effectiveFrom is { } newFrom) fields[ProcedureEntity.VersionEffectiveFromColumn] = ToDate(newFrom);
        if (effectiveTo is { } newTo) fields[ProcedureEntity.VersionEffectiveToColumn] = ToDate(newTo);
        if (clearEffectiveTo) fields[ProcedureEntity.VersionEffectiveToColumn] = DBNull.Value;

        if (ConditionalUpdate(ProcedureEntity.VersionEntity, version.Row, fields) is not { } update || Touch(procedure!) is not { } touch)
        {
            return WriteFailed<ProcedureVersionView>("The dates could not be changed. Reload the Procedure and try again.");
        }

        return await SendAsync(new OrganizationRequest[] { update, touch }, procedureId, "change dates") switch
        {
            SendResult.Done => RuleSetOutcome<ProcedureVersionView>.Done(version.View with { EffectiveFrom = from, EffectiveTo = to }),
            SendResult.Conflict => Concurrent<ProcedureVersionView>(),
            SendResult.Unknown => WriteFailed<ProcedureVersionView>("The dates may or may not have been changed. Reload the Procedure before trying again."),
            _ => WriteFailed<ProcedureVersionView>("The dates could not be changed and nothing was saved. Try again; if it keeps failing, contact support."),
        };
    }

    /// <summary>
    /// Publishes a Draft version from <paramref name="effectiveFrom"/> (or its own effective-from), in ONE transaction (I-P1): the version goes
    /// Active; the previous Active version gets <c>to</c> = that date and goes Superseded; the version's Accepted rules are marked Published and
    /// each policy's <c>sprk_currentversion</c> moves to its newest published version (model 7.2). Refused, with nothing written, without one
    /// Servable Procedure-source Stored File (400 <c>file</c>), without an effective-from (400), with a Proposed rule (400), or when the dates
    /// overlap another version in force (409 <c>procedure.version_overlap</c>). No invoice is re-checked (model 8.2).
    /// </summary>
    public virtual async Task<RuleSetOutcome<ProcedureVersionPublished>> PublishVersionAsync(
        Guid procedureId, Guid versionId, DateOnly? effectiveFrom, CancellationToken ct)
    {
        var (procedure, versions, version, refusal) = await LoadForVersionAsync<ProcedureVersionPublished>(procedureId, versionId, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        if (version!.Status != ProcedureEntity.VersionStatusDraft)
        {
            return Refused<ProcedureVersionPublished>(StatusCodes.Status409Conflict, CodeVersionNotDraft, "Only a Draft version can be published.");
        }

        var errors = new List<RuleSetError>();
        var from = effectiveFrom ?? version.From;
        if (from is null)
        {
            errors.Add(Error("effectiveFrom", "A version needs an effective-from date to be published."));
        }
        else if (version.To is { } to && to <= from)
        {
            errors.Add(Error("effectiveTo", "The version's effectiveTo must be after its effectiveFrom."));
        }

        var servable = await CountServableSourceFilesAsync(versionId, ct);
        if (servable != 1)
        {
            errors.Add(Error("file", servable == 0
                ? "A version needs its Procedure file (a Servable Stored File of purpose Procedure source) to be published."
                : "The version has more than one Servable Procedure file; remove the extra one before publishing."));
        }

        if (errors.Count > 0)
        {
            return RuleSetOutcome<ProcedureVersionPublished>.Invalid(CodePublishInvalid, errors);
        }

        var start = from!.Value;
        // I-P1: the previous Active version (the latest one that started before D) ends on D. One that starts on or after D would be hidden
        // entirely, and any other version in force must not overlap [D, to).
        var predecessor = versions.Where(v => v.Id != versionId && v.Status == ProcedureEntity.VersionStatusActive && v.From is not null)
            .MaxBy(v => v.From!.Value);
        if (predecessor is not null && predecessor.From!.Value >= start)
        {
            return Refused<ProcedureVersionPublished>(StatusCodes.Status409Conflict, CodeVersionOverlap,
                $"The effective date is not after the date the version it replaces came into force ({predecessor.From.Value:yyyy-MM-dd}).");
        }

        var predecessorTo = predecessor is null ? (DateOnly?)null : predecessor.To is { } pt && pt < start ? pt : start;
        foreach (var other in versions.Where(v => v.Id != versionId && IsInForce(v)))
        {
            var otherTo = other.Id == predecessor?.Id ? predecessorTo : other.To;
            if (Overlaps(start, version.To, other.From!.Value, otherTo))
            {
                return Refused<ProcedureVersionPublished>(StatusCodes.Status409Conflict, CodeVersionOverlap,
                    "The version's dates overlap another version in force. A Procedure's versions in force never overlap.");
            }
        }

        var ruleSet = await _ruleSets.LoadProcedureRuleSetAsync(procedureId, ct);
        if (ruleSet is null)
        {
            return Refused<ProcedureVersionPublished>(StatusCodes.Status409Conflict, CodeRuleSetMissing, "The Procedure has no rule set.");
        }

        var rules = await _ruleSets.PlanProcedureVersionPublishAsync(ruleSet.Id, versionId, start, ct);
        if (rules.Truncated)
        {
            return Refused<ProcedureVersionPublished>(StatusCodes.Status422UnprocessableEntity, CodeTooManyRules,
                "The version has more rules than can be published in one pass.");
        }

        if (rules.Errors.Count > 0)
        {
            return RuleSetOutcome<ProcedureVersionPublished>.Invalid(CodePublishInvalid, rules.Errors);
        }

        // The version, then its predecessor, then the rules and the Procedure row: one transaction, all or nothing.
        var versionFields = new Dictionary<string, object>
        {
            [ProcedureEntity.VersionStatusColumn] = new OptionSetValue(ProcedureEntity.VersionStatusActive),
            [ProcedureEntity.VersionEffectiveFromColumn] = ToDate(start),
        };
        var requests = new List<OrganizationRequest>();
        if (ConditionalUpdate(ProcedureEntity.VersionEntity, version.Row, versionFields) is not { } versionUpdate)
        {
            return WriteFailed<ProcedureVersionPublished>("The version could not be published. Reload the Procedure and try again.");
        }

        requests.Add(versionUpdate);
        if (predecessor is not null)
        {
            var predecessorFields = new Dictionary<string, object>
            {
                [ProcedureEntity.VersionStatusColumn] = new OptionSetValue(ProcedureEntity.VersionStatusSuperseded),
                [ProcedureEntity.VersionEffectiveToColumn] = ToDate(predecessorTo!.Value),
            };
            if (ConditionalUpdate(ProcedureEntity.VersionEntity, predecessor.Row, predecessorFields) is not { } predecessorUpdate)
            {
                return WriteFailed<ProcedureVersionPublished>("The version could not be published. Reload the Procedure and try again.");
            }

            requests.Add(predecessorUpdate);
        }

        requests.AddRange(rules.Updates);
        if (Touch(procedure!) is not { } touch)
        {
            return WriteFailed<ProcedureVersionPublished>("The version could not be published. Reload the Procedure and try again.");
        }

        requests.Add(touch);
        if (requests.Count > MaxPerTransaction)
        {
            return Refused<ProcedureVersionPublished>(StatusCodes.Status422UnprocessableEntity, CodeTooManyRules,
                "The version has more rules than can be published in one pass.");
        }

        // The last point at which the caller going away stops the publish.
        ct.ThrowIfCancellationRequested();
        switch (await SendAsync(requests, procedureId, "publish"))
        {
            case SendResult.Done:
                _logger.LogInformation("Procedure {ProcedureId}: version {VersionId} published from {From}; {Rules} rule(s) in force; superseded {Predecessor}",
                    procedureId, versionId, start, rules.Published.Count, predecessor?.Id);
                return RuleSetOutcome<ProcedureVersionPublished>.Done(new ProcedureVersionPublished(
                    version.View with { Status = "Active", EffectiveFrom = start }, predecessor?.Id, rules.Published, rules.Rejected));
            case SendResult.Conflict:
                return Concurrent<ProcedureVersionPublished>();
            case SendResult.Unknown:
                return WriteFailed<ProcedureVersionPublished>("The publish may or may not have completed. Reload the Procedure before trying again.");
            default:
                return WriteFailed<ProcedureVersionPublished>("The version could not be published and nothing was changed. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>
    /// Retires one version (I-P5, model 8.2): it is never selected again. Its policies stay enabled (other versions use them), nothing is
    /// re-checked and no Signal is closed. Retiring a Retired version is a no-op.
    /// </summary>
    public virtual async Task<RuleSetOutcome<ProcedureVersionView>> RetireVersionAsync(Guid procedureId, Guid versionId, CancellationToken ct)
    {
        var (procedure, _, version, refusal) = await LoadForVersionAsync<ProcedureVersionView>(procedureId, versionId, ct, allowRetiredProcedure: true);
        if (refusal is not null)
        {
            return refusal;
        }

        if (version!.Status == ProcedureEntity.VersionStatusRetired)
        {
            return RuleSetOutcome<ProcedureVersionView>.Done(version.View);
        }

        var fields = new Dictionary<string, object> { [ProcedureEntity.VersionStatusColumn] = new OptionSetValue(ProcedureEntity.VersionStatusRetired) };
        if (ConditionalUpdate(ProcedureEntity.VersionEntity, version.Row, fields) is not { } update || Touch(procedure!) is not { } touch)
        {
            return WriteFailed<ProcedureVersionView>("The version could not be retired. Reload the Procedure and try again.");
        }

        return await SendAsync(new OrganizationRequest[] { update, touch }, procedureId, "retire version") switch
        {
            SendResult.Done => RuleSetOutcome<ProcedureVersionView>.Done(version.View with { Status = "Retired" }),
            SendResult.Conflict => Concurrent<ProcedureVersionView>(),
            SendResult.Unknown => WriteFailed<ProcedureVersionView>("The version may or may not have been retired. Reload the Procedure before trying again."),
            _ => WriteFailed<ProcedureVersionView>("The version could not be retired and nothing was changed. Try again; if it keeps failing, contact support."),
        };
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Retire the Procedure (model 3.1 I-P5, 12 D-170, 15 answer 2)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The read-only confirmation (model 15 answer 2): the warning, and how many open invoices THE CALLER can Read whose open findings the retire
    /// would close. The Signal set is <see cref="SignalClosureService.SelectOpenSignalsOfPolicyAsync"/> per policy of the rule set: the same
    /// selection the close uses, never a second query. Counted by <see cref="AffectedInvoiceCounter"/>; when the count cannot be told (a rights
    /// fault, a read fault), the warning shows without it. Writes nothing.
    /// </summary>
    public virtual async Task<RuleSetOutcome<ProcedureRetirePreview>> PreviewRetireAsync(Guid procedureId, string? callerBearerToken, CancellationToken ct)
    {
        var procedure = await LoadProcedureAsync(procedureId, ct);
        if (procedure is null)
        {
            return NotFound<ProcedureRetirePreview>();
        }

        int? count = null;
        try
        {
            var signals = new List<OpenPolicySignal>();
            if (await _ruleSets.LoadProcedureRuleSetAsync(procedureId, ct) is { } ruleSet)
            {
                var (policies, truncated) = await _ruleSets.LoadPoliciesAsync(ruleSet.Id, ct);
                if (truncated)
                {
                    throw new InvalidOperationException("The rule set has more policies than one page.");
                }

                foreach (var policy in policies)
                {
                    signals.AddRange(await _closure.SelectOpenSignalsOfPolicyAsync(policy.Id, withBillingSubject: true, ct));
                }
            }

            count = await _counter.CountVisibleOpenInvoicesAsync(signals, callerBearerToken, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Procedure {ProcedureId}: the retire preview could not select the open findings; the warning shows without a count", procedureId);
        }

        return RuleSetOutcome<ProcedureRetirePreview>.Done(new ProcedureRetirePreview(RetireWarning, AffectedText(count), count));
    }

    /// <summary>
    /// Retires the Procedure (on confirm): the Procedure goes Retired and every policy of its rule set is switched Off, in one transaction per
    /// 1,000 rows (the Procedure in the first); then each policy's open Signals close <c>PolicyRetired</c> through
    /// <see cref="SignalClosureService.ClosePolicySignalsRetiredAsync"/> (the preview's selection, at confirm time: whatever is open then is
    /// closed, including Signals on invoices the caller cannot Read; a Signal a person resolved since is left as the close leaves it). A close
    /// that fails leaves the policy Off, so the job's D-170 sweep closes the rest, and the result says the close is incomplete. Calling it again
    /// on a Retired Procedure finishes an incomplete run. The result's count is the caller-scoped count of the invoices whose findings closed.
    /// No re-check, no banner write (model 8.2).
    /// </summary>
    public virtual async Task<RuleSetOutcome<ProcedureRetired>> RetireAsync(Guid procedureId, string? callerBearerToken, CancellationToken ct)
    {
        var procedure = await LoadProcedureAsync(procedureId, ct);
        if (procedure is null)
        {
            return NotFound<ProcedureRetired>();
        }

        var ruleSet = await _ruleSets.LoadProcedureRuleSetAsync(procedureId, ct);
        IReadOnlyList<RuleSetService.PolicyRow> policies = Array.Empty<RuleSetService.PolicyRow>();
        if (ruleSet is not null)
        {
            var loaded = await _ruleSets.LoadPoliciesAsync(ruleSet.Id, ct);
            if (loaded.Truncated)
            {
                return Refused<ProcedureRetired>(StatusCodes.Status422UnprocessableEntity, CodeTooManyRules,
                    "The Procedure has more rules than can be retired in one pass.");
            }

            policies = loaded.Policies;
        }

        // (1) Retired + every policy Off. Conditional on the Procedure row as read, so a publish racing the retire is refused, not interleaved.
        var requests = new List<OrganizationRequest>();
        if (!IsRetired(procedure))
        {
            if (ConditionalUpdate(ProcedureEntity.Entity, procedure, new Dictionary<string, object>
                {
                    [ProcedureEntity.StatusColumn] = new OptionSetValue(ProcedureEntity.StatusRetired),
                }) is not { } retire)
            {
                return WriteFailed<ProcedureRetired>("The Procedure could not be retired. Reload it and try again.");
            }

            requests.Add(retire);
        }

        var switchedOff = policies.Where(p => p.Enabled != false).ToList();
        requests.AddRange(switchedOff.Select(p => (OrganizationRequest)new UpdateRequest
        {
            Target = new Entity(RuleSetService.PolicyEntity, p.Id) { ["sprk_enabled"] = false },
        }));
        foreach (var chunk in requests.Chunk(MaxPerTransaction))
        {
            switch (await SendAsync(chunk, procedureId, "retire"))
            {
                case SendResult.Done:
                    continue;
                case SendResult.Conflict:
                    return Concurrent<ProcedureRetired>();
                default:
                    return WriteFailed<ProcedureRetired>(
                        "The Procedure could not be retired completely. Retire it again; if it keeps failing, contact support.");
            }
        }

        // (2) Close each policy's open Signals PolicyRetired (D-170), now. Every policy is Off from here, so the nightly sweep finishes any gap.
        var outcome = SignalClosureOutcome.None;
        var closed = new List<OpenPolicySignal>();
        foreach (var policy in policies)
        {
            try
            {
                var closure = await _closure.ClosePolicySignalsRetiredAsync(policy.Id, policy.Code, withBillingSubject: true, CancellationToken.None);
                outcome = outcome.Plus(closure.Outcome);
                closed.AddRange(closure.Closed);
            }
            catch (Exception ex)
            {
                // Every failure, a timeout included (the close runs with CancellationToken.None): the Procedure is already Retired and the policy
                // Off, so report the close incomplete and go on to the next policy; a retry or the nightly sweep finishes it.
                outcome = outcome with { PolicyFailures = outcome.PolicyFailures + 1 };
                _logger.LogError(ex, "Procedure {ProcedureId}: the open findings of policy {PolicyCode} could not be closed now; the policy is Off, so the nightly run closes them",
                    procedureId, policy.Code);
            }
        }

        // The retire is committed and the findings closed: nothing from here on may turn that into an error. The count is shown or left out.
        int? count;
        try
        {
            count = await _counter.CountVisibleOpenInvoicesAsync(closed, callerBearerToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Procedure {ProcedureId} retired; the affected-invoice count could not be made, so none is shown", procedureId);
            count = null;
        }

        _logger.LogInformation("Procedure {ProcedureId} retired: {Off} rule(s) switched Off, {Closed} finding(s) closed, complete={Complete}",
            procedureId, switchedOff.Count, outcome.Closed, outcome.IsComplete);
        return RuleSetOutcome<ProcedureRetired>.Done(new ProcedureRetired(
            procedureId, "Retired", switchedOff.Count, outcome.IsComplete, AffectedText(count), count,
            outcome.IsComplete ? null : "Some findings could not be closed now. The rules are off, so the nightly run closes the rest."));
    }

    /// <summary>"N open invoices you can see are affected." (model 15 answer 2); null when the count is unknown.</summary>
    internal static string? AffectedText(int? count) => count switch
    {
        null => null,
        1 => "1 open invoice you can see is affected.",
        _ => $"{count.Value.ToString(CultureInfo.InvariantCulture)} open invoices you can see are affected.",
    };

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reads
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One Procedure Version as read: its row (for the conditional write), its view and its window.</summary>
    internal sealed record VersionRow(Entity Row, Guid Id, int Number, int Status, DateOnly? From, DateOnly? To, ProcedureVersionView View);

    private async Task<Entity?> LoadProcedureAsync(Guid procedureId, CancellationToken ct)
    {
        if (procedureId == Guid.Empty)
        {
            return null;
        }

        var query = new QueryExpression(ProcedureEntity.Entity) { ColumnSet = new ColumnSet(ProcedureColumns), TopCount = 1 };
        query.Criteria.AddCondition("sprk_procedureid", ConditionOperator.Equal, procedureId);
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault(r => r.Id == procedureId);
    }

    private async Task<IReadOnlyList<VersionRow>> LoadVersionsAsync(Guid procedureId, CancellationToken ct)
    {
        var query = new QueryExpression(ProcedureEntity.VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
        query.Criteria.AddCondition(ProcedureEntity.VersionProcedureColumn, ConditionOperator.Equal, procedureId);
        query.Orders.Add(new OrderExpression(ProcedureEntity.VersionNumberColumn, OrderType.Ascending));
        var rows = await _entities.RetrieveMultipleAsync(query, ct);
        if (rows.MoreRecords)
        {
            throw new InvalidOperationException($"Procedure {procedureId} has more versions than one page.");
        }

        return rows.Entities
            .Where(r => r.GetAttributeValue<EntityReference>(ProcedureEntity.VersionProcedureColumn)?.Id == procedureId)
            .Select(ToVersion)
            .ToList();
    }

    /// <summary>The Procedure, its versions and the route's version — which must be one of THIS Procedure's (else the unknown-id 404).</summary>
    private async Task<(Entity? Procedure, IReadOnlyList<VersionRow> Versions, VersionRow? Version, RuleSetOutcome<T>? Refusal)> LoadForVersionAsync<T>(
        Guid procedureId, Guid versionId, CancellationToken ct, bool allowRetiredProcedure = false) where T : class
    {
        var procedure = await LoadProcedureAsync(procedureId, ct);
        if (procedure is null)
        {
            return (null, Array.Empty<VersionRow>(), null, NotFound<T>());
        }

        var versions = await LoadVersionsAsync(procedureId, ct);
        var version = versions.FirstOrDefault(v => v.Id == versionId);
        if (version is null)
        {
            return (procedure, versions, null, Refused<T>(StatusCodes.Status404NotFound, CodeVersionNotFound, "The version was not found in this Procedure."));
        }

        if (!allowRetiredProcedure && IsRetired(procedure))
        {
            return (procedure, versions, version, Refused<T>(StatusCodes.Status409Conflict, CodeRetired, "A retired Procedure cannot be changed."));
        }

        return (procedure, versions, version, null);
    }

    private async Task<int> CountServableSourceFilesAsync(Guid versionId, CancellationToken ct)
    {
        var query = new QueryExpression(ProcedureEntity.StoredFileEntity) { ColumnSet = new ColumnSet(ProcedureEntity.StoredFileProcedureVersionColumn), TopCount = 2 };
        query.Criteria.AddCondition(ProcedureEntity.StoredFileProcedureVersionColumn, ConditionOperator.Equal, versionId);
        query.Criteria.AddCondition(ProcedureEntity.StoredFilePurposeColumn, ConditionOperator.Equal, ProcedureEntity.StoredFilePurposeProcedureSource);
        query.Criteria.AddCondition(ProcedureEntity.StoredFileStatusColumn, ConditionOperator.Equal, ProcedureEntity.StoredFileStatusServable);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities
            .Count(r => r.GetAttributeValue<EntityReference>(ProcedureEntity.StoredFileProcedureVersionColumn)?.Id == versionId);
    }

    /// <summary>
    /// I-P4: whether a version has been in force, which locks Applies to and Firm. Active and Superseded versions were; a Retired one was when
    /// it put a rule in force (its publish marked policy versions under it Published), and a Draft retired without publishing never was.
    /// Known limit: a version published with no rule and then retired does not lock (it governed nothing, so nothing depends on its scope).
    /// </summary>
    private async Task<bool> ScopeLockedAsync(IReadOnlyList<VersionRow> versions, CancellationToken ct)
    {
        if (versions.Any(v => v.Status is ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded))
        {
            return true;
        }

        var retired = versions.Where(v => v.Status == ProcedureEntity.VersionStatusRetired).Select(v => (object)v.Id).ToArray();
        if (retired.Length == 0)
        {
            return false;
        }

        var query = new QueryExpression(RuleSetService.VersionEntity) { ColumnSet = new ColumnSet(RuleSetService.ProcedureVersionColumn), TopCount = 1 };
        query.Criteria.AddCondition(RuleSetService.ProcedureVersionColumn, ConditionOperator.In, retired);
        query.Criteria.AddCondition(RuleSetService.PublishStateColumn, ConditionOperator.Equal, RuleSetService.PublishStatePublished);
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.Any(r =>
            r.GetAttributeValue<EntityReference>(RuleSetService.ProcedureVersionColumn)?.Id is { } id && retired.Contains(id));
    }

    private async Task<bool> VersionNumberTakenAsync(Guid procedureId, int number, CancellationToken ct)
    {
        try
        {
            return (await LoadVersionsAsync(procedureId, ct)).Any(v => v.Number == number);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Procedure {ProcedureId}: could not re-read its versions to tell whether number {Number} was taken; not retrying",
                procedureId, number);
            return false;
        }
    }

    private async Task CheckLookupsAsync(List<RuleSetError> errors, Guid? firmId, Guid? issuedById, Guid? externalContactId, Guid? internalContactId, CancellationToken ct)
    {
        foreach (var (field, entity, id) in new (string, string, Guid?)[]
        {
            ("firmId", RuleSetService.FirmEntity, firmId), ("issuedById", RuleSetService.FirmEntity, issuedById),
            ("externalContactId", "contact", externalContactId), ("internalContactId", "contact", internalContactId),
        })
        {
            if (id is not { } value)
            {
                continue;
            }

            if (value == Guid.Empty || !await ExistsAsync(entity, value, ct))
            {
                errors.Add(Error(field, $"{field} was not found."));
            }
        }
    }

    private async Task<bool> ExistsAsync(string entity, Guid id, CancellationToken ct)
    {
        var query = new QueryExpression(entity) { ColumnSet = new ColumnSet(entity + "id"), TopCount = 1 };
        query.Criteria.AddCondition(entity + "id", ConditionOperator.Equal, id);
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.Count > 0;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Writes
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    private enum SendResult
    {
        Done,

        /// <summary>A row changed after it was read (or the version number was taken): nothing was written.</summary>
        Conflict,

        /// <summary>The transaction failed: nothing was written.</summary>
        Failed,

        /// <summary>No fault came back (e.g. a timeout): the transaction may have committed.</summary>
        Unknown,
    }

    /// <summary>One transaction, all or nothing, with <see cref="CancellationToken.None"/>: a caller who disconnects never leaves half a write.</summary>
    private async Task<SendResult> SendAsync(IReadOnlyList<OrganizationRequest> requests, Guid procedureId, string what)
    {
        try
        {
            await _entities.ExecuteTransactionAsync(requests, CancellationToken.None);
            return SendResult.Done;
        }
        catch (Exception ex) when (ex is DBConcurrencyException || SignalWriter.IsConcurrencyVersionMismatch(ex))
        {
            _logger.LogWarning("Procedure {ProcedureId}: the {What} was refused; a row changed after it was read. Nothing was written", procedureId, what);
            return SendResult.Conflict;
        }
        catch (DataverseTransactionException ex) when (ex.OutcomeUnknown)
        {
            _logger.LogError(ex, "Procedure {ProcedureId}: the {What} transaction returned no answer; it may have committed", procedureId, what);
            return SendResult.Unknown;
        }
        catch (OperationCanceledException ex)
        {
            // The send runs with CancellationToken.None, so a cancellation here is the client's timeout: the transaction may have committed.
            _logger.LogError(ex, "Procedure {ProcedureId}: the {What} transaction timed out; it may have committed", procedureId, what);
            return SendResult.Unknown;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Procedure {ProcedureId}: the {What} failed ({ExceptionType}); nothing was written", procedureId, what, ex.GetType().Name);
            return SendResult.Failed;
        }
    }

    /// <summary>An update of <paramref name="row"/> conditional on its row version as read; null when it was read without one (never written blind).</summary>
    private static UpdateRequest? ConditionalUpdate(string entity, Entity row, IReadOnlyDictionary<string, object> fields)
    {
        if (row.GetAttributeValue<long?>(RuleSetService.RowVersionColumn) is not { } rowVersion)
        {
            return null;
        }

        var target = new Entity(entity, row.Id) { RowVersion = rowVersion.ToString(CultureInfo.InvariantCulture) };
        foreach (var (column, value) in fields)
        {
            target[column] = value is DBNull ? null : value;
        }

        return new UpdateRequest { Target = target, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches };
    }

    /// <summary>The Procedure row rewritten with its own status, conditional on its row version: the lock every write of the Procedure takes.</summary>
    private static UpdateRequest? Touch(Entity procedure) => ConditionalUpdate(ProcedureEntity.Entity, procedure, new Dictionary<string, object>
    {
        [ProcedureEntity.StatusColumn] = procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.StatusColumn) ?? new OptionSetValue(ProcedureEntity.StatusActive),
    });

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>I-P3: a Rate card always names its firm; a Procedure that applies to a firm names it.</summary>
    internal static bool FirmRequired(ProcedureType type, ProcedureAppliesTo appliesTo) =>
        type == ProcedureType.RateCard || appliesTo == ProcedureAppliesTo.Firm;

    /// <summary>The initial "clause quotes visible to firms" flag of the Procedure's rule set (D-66): on for the documents firms receive
    /// (guidelines, rate cards, billing instructions), off for internal standards and anything else.</summary>
    internal static bool InitialVisibleToFirms(ProcedureType type) =>
        type is ProcedureType.Guideline or ProcedureType.RateCard or ProcedureType.BillingInstructions;

    private static RuleSetError FirmRequiredError(ProcedureType type) => Error("firmId", type == ProcedureType.RateCard
        ? "A Rate card Procedure always names its firm."
        : "A Procedure that applies to a firm names that firm.");

    private static bool IsRetired(Entity procedure) =>
        procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.StatusColumn)?.Value == ProcedureEntity.StatusRetired;

    /// <summary>Active and Superseded versions are in force for their windows (model 3.1: a Superseded version stays in force for its own window).</summary>
    private static bool IsInForce(VersionRow v) =>
        v.Status is ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded && v.From is not null;

    /// <summary>Half-open windows [from, to); a null <c>to</c> is open-ended.</summary>
    internal static bool Overlaps(DateOnly aFrom, DateOnly? aTo, DateOnly bFrom, DateOnly? bTo) =>
        aFrom < (bTo ?? DateOnly.MaxValue) && bFrom < (aTo ?? DateOnly.MaxValue);

    private static VersionRow ToVersion(Entity r)
    {
        var status = r.GetAttributeValue<OptionSetValue>(ProcedureEntity.VersionStatusColumn)?.Value ?? ProcedureEntity.VersionStatusDraft;
        var from = r.GetAttributeValue<DateTime?>(ProcedureEntity.VersionEffectiveFromColumn) is { } f ? DateOnly.FromDateTime(f) : (DateOnly?)null;
        var to = r.GetAttributeValue<DateTime?>(ProcedureEntity.VersionEffectiveToColumn) is { } t ? DateOnly.FromDateTime(t) : (DateOnly?)null;
        var number = r.GetAttributeValue<int?>(ProcedureEntity.VersionNumberColumn) ?? 0;
        return new VersionRow(r, r.Id, number, status, from, to, new ProcedureVersionView(
            r.Id, number, VersionStatusName(status), from, to, r.GetAttributeValue<EntityReference>(ProcedureEntity.VersionPreviousColumn)?.Id));
    }

    private static string VersionStatusName(int status) => status switch
    {
        ProcedureEntity.VersionStatusActive => "Active",
        ProcedureEntity.VersionStatusSuperseded => "Superseded",
        ProcedureEntity.VersionStatusRetired => "Retired",
        _ => "Draft",
    };

    private static ProcedureView View(Entity p, IReadOnlyList<VersionRow> versions, Guid? ruleSetId)
    {
        var type = p.GetAttributeValue<OptionSetValue>(ProcedureEntity.TypeColumn)?.Value;
        var appliesTo = p.GetAttributeValue<OptionSetValue>(ProcedureEntity.AppliesToColumn)?.Value;
        return new ProcedureView(
            p.Id,
            p.GetAttributeValue<string>(ProcedureEntity.NameColumn) ?? string.Empty,
            p.GetAttributeValue<string>(ProcedureEntity.FirmFacingNameColumn),
            type is { } t && Enum.IsDefined(typeof(ProcedureType), t) ? ((ProcedureType)t).ToString() : "Unknown",
            appliesTo is { } a && Enum.IsDefined(typeof(ProcedureAppliesTo), a) ? ((ProcedureAppliesTo)a).ToString() : "Unknown",
            p.GetAttributeValue<EntityReference>(ProcedureEntity.FirmColumn)?.Id,
            p.GetAttributeValue<EntityReference>(ProcedureEntity.IssuedByColumn)?.Id,
            p.GetAttributeValue<EntityReference>(ProcedureEntity.ExternalContactColumn)?.Id,
            p.GetAttributeValue<EntityReference>(ProcedureEntity.InternalContactColumn)?.Id,
            IsRetired(p) ? "Retired" : "Active",
            p.GetAttributeValue<string>(ProcedureEntity.NotesColumn),
            ruleSetId,
            versions.Select(v => v.View).ToList());
    }

    private static void SetLookup(Entity row, string column, string target, Guid? id)
    {
        if (id is { } value)
        {
            row[column] = new EntityReference(target, value);
        }
    }

    /// <summary>A DateOnly column's value (date-only behaviour: no time zone).</summary>
    /// <summary>A date-only column value, as the rest of Finance writes them (<c>RuleSetService.ToUtcDate</c>, <c>RateCardImportService.ToDate</c>).</summary>
    private static DateTime ToDate(DateOnly d) => new(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>"{Procedure name} v{n}", the Procedure name shortened so the whole fits the version's 200-character name column.</summary>
    internal static string VersionName(string? procedureName, int number)
    {
        var suffix = " v" + number.ToString(CultureInfo.InvariantCulture);
        var name = procedureName ?? string.Empty;
        return (name.Length + suffix.Length > MaxNameChars ? name[..(MaxNameChars - suffix.Length)].TrimEnd() : name) + suffix;
    }

    private static RuleSetError Error(string field, string message) => new(null, null, field, message, null);

    /// <summary>Exact member name, case-insensitive; never a number or a comma list.</summary>
    internal static bool TryParse<T>(string? text, out T value) where T : struct, Enum
    {
        value = default;
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(text?.Trim(), candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }

    private static RuleSetOutcome<T> NotFound<T>() where T : class =>
        Refused<T>(StatusCodes.Status404NotFound, CodeNotFound, "The Procedure was not found.");

    private static RuleSetOutcome<T> Concurrent<T>() where T : class =>
        Refused<T>(StatusCodes.Status409Conflict, CodeConcurrentEdit,
            "The Procedure changed while this was being saved (another publish, date change, version or retire), so nothing was written. Reload and try again.");

    private static RuleSetOutcome<T> WriteFailed<T>(string detail) where T : class =>
        Refused<T>(StatusCodes.Status500InternalServerError, CodeWriteFailed, detail);

    private static RuleSetOutcome<T> Refused<T>(int status, string code, string detail) where T : class =>
        RuleSetOutcome<T>.Refused(status, code, detail);
}
