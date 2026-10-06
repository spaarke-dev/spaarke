using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Api.Agent;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>dataverse.create_record</c> tool — inserts one row into
/// a Dataverse table over the user-OBO Web API
/// (spaarke-ai-architecture-redesign-r1 task 009, FR-P0-07 write half).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 contract freeze</b>: name + argument shape mirror the GA Dataverse MCP
/// <c>create_record</c> tool — <c>create_record(tablename, item)</c> where <c>item</c> is a
/// key/value object of column logical names (lookups as
/// <c>{"relatedTable","name","recordId"}</c>; choice columns as numeric option values;
/// multi-select as comma-separated values). See <see cref="DataverseToolNames"/> for the
/// frozen-name citation and <see cref="DataverseWriteItemMapper"/> for the item contract.
/// </para>
/// <para>
/// <b>SIDE-EFFECT tool</b>: the <c>sprk_analysistool</c> row declares
/// <c>sprk_sideeffectclass = Write (100000001)</c>. The P2 confirmation gate (FR-P2-02,
/// task 031) suspends/resumes this tool BY THAT DECLARED CLASS — deliberately, NO gating or
/// confirmation logic lives in this handler; when invoked, it executes.
/// </para>
/// <para>
/// <b>User-OBO (spec MUST rule)</b>: the insert executes through
/// <see cref="IDataverseUserClient"/> under the CALLING USER's exchanged token. A create the
/// user lacks privileges for fails with the user's own access error (403 →
/// <see cref="DataverseUserClientErrorCodes.AccessDenied"/>); a table invisible to the user
/// 404s at metadata resolution BEFORE any write is attempted.
/// </para>
/// <para>
/// <b>The User-OBO rule is AMENDED for creates — owner round 7 item 3 (2026-10-02), CLAUDE.md §6.5 path B</b>
/// (unified-access-control-r2 task 146; supersedes the r2 path-A exception, which covered filed child rows only). The
/// G5 pattern applies to every create: the handler checks AS THE CALLER that they could create the row themselves
/// (Create/Append privileges, AppendTo on every record a lookup names, no field-secured or owner/audit column), then the
/// APPLICATION creates it OWNED BY THE TEAM the one <see cref="Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver"/>
/// names — the named Secure team under a secure parent, otherwise the parent's (or, unfiled, the caller's)
/// business-unit team — and records the caller in the table's Assigned-To / "for" column where one exists
/// (<see cref="OwnedChildWrite.ForPersonColumns"/>). The creator is kept (a run-as-user create) only where the
/// resolver's own rules keep it: per-user tables, unfiled communications/threads (E1/E2), and tables with no user/team
/// ownership (<see cref="OwnedChildWrite.PathFor"/>); such a create filed under a SECURE record is refused. A table
/// outside the ownership set that would be owned by the Secure team is refused too. A work assignment or project filed
/// under a secure matter or project (task 158, owner rounds 6 and 31) is created INTO isolation — by the application, owned
/// by the named Secure Record Owners team, flagged in the create, its <c>sprk_createdbyperson</c> the caller — after the
/// as-caller pre-check (including AppendTo on each secure parent) and the caller's No Access check against every secure
/// parent and the record itself; then provisioning's own re-entry steps share it to the caller (read back; a share that
/// fails deletes the row again), give it its own container and its parents' sharees. No business-unit-visible window,
/// and never a row nobody can open (S5). The amendment is recorded in spaarke-ai-architecture-redesign-r1's spec (A-UAC146)
/// and the task 146 note §13; provisioning's app-only follow-on steps for a work assignment or project created under a
/// secure record are its extension A-UAC158 (§6.5 path B, ACCEPTED by owner round 32, 2026-10-04; task 158 note §14).
/// </para>
/// <para>
/// <b>The creator stamp (unified-access-control-r2 task 133; owner round 7 item 2; task 146 c1-r1).</b> A row the
/// application creates records the PERSON who asked for it in the server-stamped, field-secured
/// <c>sprk_createdbyperson</c> (<see cref="RecordCreatorPerson"/>) — IN THE CREATE PAYLOAD, through the owner decision
/// (<c>RecordOwnershipContext.RequestedBy</c> → <c>RecordOwnerResolution.StampCreatorOn</c>, in
/// <see cref="OwnedChildWrite.CreateAsync"/>). That supersedes task 133's interim shape (a user-OBO create followed by
/// one app-only column update), which is removed: there is exactly one stamp. An item that names the column is refused
/// before any Dataverse call (pre-suspend in <c>ValidateChat</c>, and again here): a caller never chooses who created a
/// record. A run-as-user create (per-user tables, unfiled communications, organization-owned tables) needs no stamp — its
/// <c>createdby</c> is the person.
/// </para>
/// <para>
/// <b>ADR-015 / NFR-07</b>: telemetry carries table logical name, column COUNT, outcome,
/// duration, and the created record id — never column values.
/// </para>
/// </remarks>
public sealed partial class DataverseCreateRecordHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DataverseCreateRecordHandler);

    /// <summary>
    /// G-P3 UAT round-5 R5-E (2026-07-07): HARD block on <c>sprk_document</c> creates.
    /// Round-3's R3-4 fix banned fileless document creation at the DESCRIPTION level only —
    /// the model ignored the guidance and created a bare, broken sprk_document row (no SPE
    /// file, empty profile, Similar Documents errors). Guidance is not enforcement: this
    /// handler now REJECTS the table outright (<see cref="ToolErrorCodes.ValidationFailed"/>,
    /// before any Dataverse call) in BOTH <see cref="ValidateChat"/> (the gate-resume leg
    /// runs it before <see cref="ExecuteChatAsync"/>) and <see cref="ExecuteChatAsync"/>
    /// (defense in depth). Spaarke documents require the full ingestion pipeline (file
    /// upload → SPE storage → document profile → indexing) that only the Document Upload
    /// wizard drives. Exposed internal for the enforcement tests.
    /// </summary>
    internal const string BlockedTableLogicalName = "sprk_document";

    /// <summary>
    /// The single rejection message for blocked <c>sprk_document</c> creates. Serves BOTH
    /// audiences on purpose: the gate failure leg persists this text verbatim into the ❌
    /// transcript message (user-facing honesty) AND into conversation history the model
    /// reads on the next turn (model-facing remediation — never retry, offer alternatives).
    /// </summary>
    internal const string SprkDocumentCreateBlockedMessage =
        "Spaarke document records can't be created from chat — this create was REJECTED and nothing " +
        "was written. Documents need the full ingestion pipeline (file upload → secure storage → " +
        "document profile) that the Document Upload wizard drives; a row created here would be a " +
        "broken, fileless document. Use the Document Upload wizard to add a document, or upload the " +
        "file into this chat and I can work with it in-session. Do NOT retry this create — " +
        "sprk_document is blocked on this tool unconditionally and no argument change makes it " +
        "valid; offer an alternative instead (Document Upload wizard, create a task, or draft an email).";

    /// <summary>
    /// Refusal-affordance composer (spaarke-ai-architecture-redesign-r2 task 040, FR-A1-11,
    /// design D-F0(d), NFR-10): appends a SERVER-composed, actionable Document Upload wizard
    /// deep-link to <see cref="SprkDocumentCreateBlockedMessage"/> so the R5-E hard-block
    /// refusal is never a dead end. The link travels on the SAME refusal-message channel the
    /// R5-E block already relies on (<see cref="ToolValidationResult.Errors"/> in
    /// <see cref="ValidateChat"/> — the gate-resume leg's first stop — and
    /// <see cref="ToolResult.ErrorMessage"/> in <see cref="ExecuteChatAsync"/> defense-in-depth);
    /// composing it via <see cref="HandoffUrlBuilder"/> keeps the URL server-trusted (D-F0(b): the
    /// model never invents the link a user clicks), and it never grants or softens the block
    /// itself — the create is REJECTED unconditionally regardless of this link's composition.
    /// Host-scopes to the chat session's bound matter (<paramref name="matterId"/>) when known;
    /// degrades to a valid unscoped wizard link otherwise (see
    /// <see cref="HandoffUrlBuilder.BuildDocumentUploadWizardUrl(Guid?)"/>).
    /// </summary>
    internal string BuildBlockedMessageWithAffordance(Guid? matterId)
    {
        var url = _handoffUrlBuilder.BuildDocumentUploadWizardUrl(matterId);
        return $"{SprkDocumentCreateBlockedMessage} Open the correct path now: [Document Upload wizard]({url})";
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex LogicalNameRegex();

    /// <summary>
    /// The refusal for an item naming the server-stamped creator column (task 133). Model-facing and user-safe: it
    /// names the column and that dropping it makes the create valid.
    /// </summary>
    internal const string CreatorPersonColumnRefusal =
        "The column 'sprk_createdbyperson' records who created the record and is set by the server only, so this " +
        "create was REJECTED and nothing was written. Omit that column and create the record again: it is filled in " +
        "with the calling user automatically.";

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<DataverseCreateRecordHandler> _logger;
    private readonly HandoffUrlBuilder _handoffUrlBuilder;
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownership;
    private readonly Spaarke.Dataverse.IFieldMappingDataverseService _appOnly;
    private readonly Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService _identity;
    private readonly IServiceScopeFactory? _scopes;
    private readonly Sprk.Bff.Api.Services.Access.SecureRootFilingGate _rootFiling;

    /// <param name="scopes">Task 142 (L1): the scope the Assigned-To materializer is resolved from after a root is
    /// created. Optional so a host composing the tool framework without the external-access module still resolves this
    /// handler (CLAUDE.md §10 F.1); the materializer itself is looked up with GetService and skipped when absent.</param>
    public DataverseCreateRecordHandler(
        IDataverseUserClient dataverse,
        ILogger<DataverseCreateRecordHandler> logger,
        HandoffUrlBuilder handoffUrlBuilder,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Spaarke.Dataverse.IFieldMappingDataverseService appOnly,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        Sprk.Bff.Api.Services.Access.SecureRootFilingGate rootFiling,
        IServiceScopeFactory? scopes = null)
    {
        _scopes = scopes;
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handoffUrlBuilder = handoffUrlBuilder ?? throw new ArgumentNullException(nameof(handoffUrlBuilder));
        // Task 146 r2 (S1 / G5): both unconditionally registered (MetadataServiceExtensions / GraphModule), so this
        // handler's tool-framework registration gains no asymmetric dependency (CLAUDE.md §10 F.1).
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _appOnly = appOnly ?? throw new ArgumentNullException(nameof(appOnly));
        // Owner round 7 item 3: the caller's LINKED contact (task 141) names them in a contact-typed "for" column.
        // Unconditionally registered (MembershipModule), so no asymmetric registration (CLAUDE.md §10 F.1).
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        // Task 158 (owner round 6): a work assignment or project created under a secure matter or project is secured
        // through provisioning's own steps. Registered beside the restamper by AddCoreAncestorResolver, which
        // AddToolFramework calls too — no asymmetric registration (CLAUDE.md §10 F.1).
        _rootFiling = rootFiling ?? throw new ArgumentNullException(nameof(rootFiling));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Dataverse Create Record",
        // FR-A-01 (AIR2-020): mirror of the authored sprk_description in infra/dataverse/sprk_analysistool-dataverse-create-record-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: @"Inserts one row into a Dataverse table under the calling user's permissions and returns the created record id as a citable path (tables/{table}/records/{guid}). Call dataverse.describe first if the table's schema is unknown — do NOT guess column logical names from display names. Values: strings/numbers/booleans for simple fields; numeric option values for choice columns (NEVER labels — use dataverse.describe to find the numeric values); comma-separated numeric values for multi-select choice; lookup fields as {""relatedTable"": ""..."", ""recordId"": ""guid""} — recordId is REQUIRED: resolve the target record's GUID FIRST via dataverse.search_data or dataverse.read_query IN THE SAME TURN you draft the proposal, BEFORE asking the user to confirm; a lookup without recordId is rejected. NEVER use OData annotations as item keys — no '@odata.bind', no '@' or '.' in any key: the ONLY way to set a lookup on this transport is the {relatedTable, recordId} object form, and choice columns take plain numeric values. If you cannot resolve a GUID for an optional lookup or the numeric value for an optional choice, OMIT that column and mention the value in a text column instead — never guess. sprk_matter write contract (from live metadata — verify anything else with dataverse.describe): sprk_mattername (text, REQUIRED); sprk_matternumber (text); sprk_matterdescription (multiline text); sprk_mattertype is a LOOKUP to sprk_mattertype_ref and sprk_practicearea is a LOOKUP to sprk_practicearea_ref — resolve the reference row's GUID via dataverse.read_query first and send {""relatedTable"":""sprk_mattertype_ref"",""recordId"":""guid""} (same pattern for practice area with sprk_practicearea_ref), or OMIT the column and put the value in sprk_matterdescription. Records you create belong to the calling user automatically — never set owner/assignee columns for the requesting user themselves. HARD RULE — sprk_document creates are BLOCKED: this tool REJECTS any create on the sprk_document table before anything is written (VALIDATION_FAILED). Spaarke document records require the full ingestion pipeline (file upload → SharePoint Embedded storage → document profile → indexing) that only the Document Upload wizard drives; no chat tool can upload file content. Never attempt or retry a sprk_document create — no argument change makes it valid. If asked to create a document or save chat output 'to documents', say honestly that documents can't be created from chat and point the user to the Document Upload wizard (or offer an alternative: work with a file uploaded into this chat, create a task, draft an email, or open a workspace tab). WRITE tool: executes with the user's own privileges; if the user cannot create rows in the table, the call fails with their access error. Spaarke entity map: 'matter' = sprk_matter (name column sprk_mattername), 'project' = sprk_project (sprk_projectname), 'document' = sprk_document (sprk_documentname — creation BLOCKED on this tool, Document Upload wizard only); people = contact, companies = account.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "tablename",
                "The logical (schema) name of the table to insert into (e.g. 'account', 'sprk_event'). " +
                "Use dataverse.describe with path 'tables/' to find logical names.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "item",
                "Record field values as key-value pairs. Keys are PLAIN column logical names from dataverse.describe — " +
                "NEVER OData annotations ('sprk_column@odata.bind' is invalid on this transport and is rejected). " +
                "Values: strings, numbers, or booleans for simple fields. For lookup/customer fields use an object: " +
                "{\"relatedTable\": \"account\", \"name\": \"Contoso Ltd\", \"recordId\": \"guid\"} — recordId is " +
                "REQUIRED on this transport (resolve it first via dataverse.search_data / dataverse.read_query; " +
                "never send a lookup with only a name). For choice fields use the numeric option value, never the " +
                "label. For multi-select choice use comma-separated values like \"100000002,100000004\". Omit any " +
                "optional column whose value you cannot resolve precisely rather than guessing.",
                ToolParameterType.Object,
                Required: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "DataverseCreateRecordHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "DataverseCreateRecordHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed));

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (!TryParseArgs(context.ToolArgumentsJson, out var tablename, out var item, out var error))
            return ToolValidationResult.Failure(error!);

        // Task 133: the creator column is server-stamped — refused pre-suspend, so no confirm dialog is shown for a
        // create that can never run.
        if (RecordCreatorPerson.IsNamedIn(item))
            return ToolValidationResult.Failure(CreatorPersonColumnRefusal);

        // R5-E HARD RULE: sprk_document creates never execute. This ValidateChat now runs at
        // TWO points on the gated path, so the rejection fires before any Dataverse wire call on
        // every path: (1) AIR2-034 (FR-A1-05) PRE-SUSPEND — SideEffectGateAIFunction runs it via
        // ToolHandlerToAIFunctionAdapter.ValidateForGate BEFORE suspending, so a doomed
        // sprk_document create renders this honest ❌ + affordance and is NEVER shown a confirm
        // dialog (no Confirm→❌ dead-end); and (2) the gate-resume leg (TypedHandlerResumeExecutor)
        // re-runs it under the confirming user's OBO scope BEFORE ExecuteChatAsync (defense in
        // depth / TOCTOU). AIR2-040 (FR-A1-11): the refusal carries a server-composed, host-scoped
        // Document Upload deep-link — the block itself is unconditional and unaffected by the affordance.
        if (string.Equals(tablename, BlockedTableLogicalName, StringComparison.Ordinal))
            return ToolValidationResult.Failure(BuildBlockedMessageWithAffordance(context.MatterId));

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        if (!TryParseArgs(context.ToolArgumentsJson, out var tablename, out var item, out var parseError))
        {
            return Error(tool, parseError!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        // Task 133 (defense in depth — ValidateChat already rejects): the creator column is server-stamped.
        if (RecordCreatorPerson.IsNamedIn(item))
        {
            return LogOutcome(context, tablename,
                Error(tool, CreatorPersonColumnRefusal, ToolErrorCodes.ValidationFailed, startedAt), stopwatch);
        }

        // R5-E HARD RULE (defense in depth — ValidateChat already rejects): sprk_document
        // creates are blocked BEFORE any Dataverse call. TryParseArgs lower-cases the
        // table name, so casing variants cannot slip past. AIR2-040 (FR-A1-11): same
        // affordance-carrying refusal as ValidateChat, for parity on this defense-in-depth path.
        if (string.Equals(tablename, BlockedTableLogicalName, StringComparison.Ordinal))
        {
            return LogOutcome(context, tablename,
                Error(tool, BuildBlockedMessageWithAffordance(context.MatterId), ToolErrorCodes.ValidationFailed, startedAt),
                stopwatch);
        }

        try
        {
            // Entity-set + primary-id resolution under the USER's token (read-handler pattern):
            // a table invisible to the user 404s here, BEFORE any write is attempted.
            var metaResponse = await _dataverse.GetAsync(
                $"EntityDefinitions(LogicalName='{tablename}')?$select=EntitySetName,PrimaryIdAttribute,OwnershipType",
                cancellationToken).ConfigureAwait(false);
            if (!metaResponse.IsSuccess)
            {
                return LogOutcome(context, tablename, MapClientError(tool, metaResponse, startedAt), stopwatch);
            }
            var entitySetName = GetString(metaResponse.Body!.Value, "EntitySetName");
            var primaryIdAttribute = GetString(metaResponse.Body!.Value, "PrimaryIdAttribute");
            var ownershipType = GetString(metaResponse.Body!.Value, "OwnershipType");
            if (entitySetName is null || primaryIdAttribute is null)
            {
                return LogOutcome(context, tablename,
                    Error(tool, $"Table '{tablename}' has no entity-set / primary-id metadata.", ToolErrorCodes.InternalError, startedAt),
                    stopwatch);
            }

            var mapped = await DataverseWriteItemMapper.MapAsync(_dataverse, tablename, item, cancellationToken).ConfigureAwait(false);
            if (mapped.ValidationError is not null)
            {
                return LogOutcome(context, tablename, Error(tool, mapped.ValidationError, ToolErrorCodes.ValidationFailed, startedAt), stopwatch);
            }
            if (mapped.ClientFailure is not null)
            {
                return LogOutcome(context, tablename, MapClientError(tool, mapped.ClientFailure, startedAt), stopwatch);
            }

            // Owner round 7 item 3 (G5 for every create; §6.5 path B): checked as the caller, created by the application
            // owned by the resolver's team, the caller named in the table's "for" column. The creator is kept only where
            // the resolver's own rules keep it (OwnedChildWrite.PathFor).
            if (OwnedChildWrite.PathFor(tablename, mapped.Item!, ownershipType) == OwnedChildWrite.WritePath.Owned)
            {
                var (forMapped, serverSet, forFailure) = await WithForPersonAsync(
                    tablename, item, mapped.Item!, cancellationToken).ConfigureAwait(false);
                if (forFailure is not null)
                {
                    return LogOutcome(context, tablename, MapClientError(tool, forFailure, startedAt), stopwatch);
                }

                // Task 158 r1 (owner round 31): a work assignment or project filed under a SECURE matter or project is
                // created INTO isolation - owned by the named Secure Record Owners team, flagged in the create, for the
                // caller (sprk_createdbyperson) - never as an ordinary row of the caller's business unit first. Decided
                // before any write and after the caller's own G5 check: an unreadable parent flag, a caller without
                // AppendTo on a secure parent, or a caller walled off (or not checkable against) the No Access list of a
                // secure parent or of the record itself, refuses with nothing created.
                var owned = await OwnedChildWrite.CreateAsync(
                    _dataverse, _ownership, _appOnly, tablename, forMapped, serverSet,
                    CallerObjectId(context), cancellationToken, _rootFiling).ConfigureAwait(false);

                if (owned.PlanRefusal is { } rootRefusal)
                {
                    return LogOutcome(context, tablename,
                        Error(tool, $"The record was NOT created: {rootRefusal.Reason} ({rootRefusal.RefusalCode}).",
                            rootRefusal.RefusalCode ?? ToolErrorCodes.ValidationFailed, startedAt),
                        stopwatch);
                }

                // Created into isolation -> completed now: the caller's share (read back; a share that fails deletes the row
                // again), its own container, its secure parents' sharees.
                Sprk.Bff.Api.Services.Access.SecureRootInheritResult? secured = null;
                if (owned.Isolated is not null && owned.CreatedId is { } isolatedRoot)
                {
                    // The same person the plan checked and the create stamped (sprk_createdbyperson; set with Isolated) —
                    // never a second WhoAmI, whose failure would name nobody and remove a row its creator can open.
                    secured = await _rootFiling.CompleteIsolatedCreateAsync(
                            tablename, isolatedRoot, owned.IsolatedFor!.Value, context.DecisionId.ToString("N"))
                        .ConfigureAwait(false);
                }

                // Task 142 (L1, owner Q5 + R3) on the owned path too (batch 4 integration): a project / matter / work
                // assignment the application created gets its "Assigned *" contacts' grant or share now. After the create
                // committed; never throws, never fails this create (a non-root table is a no-op).
                if (owned.CreatedId is { } ownedId && secured is not { RowRemoved: true })
                {
                    await Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer.RunAfterWriteAsync(
                        _scopes, tablename, ownedId, writtenColumns: null, grantorOid: null, _logger, cancellationToken)
                        .ConfigureAwait(false);
                }

                return LogOutcome(context, tablename,
                    OwnedCreateResult(tool, tablename, forMapped, owned, startedAt, secured), stopwatch);
            }

            // A create that keeps its creator (per-user / unfiled communication / no user-team ownership) filed under a
            // SECURE record cannot be re-owned here and must not sit in the caller's ordinary business unit: refused
            // (fail closed). Filed under ordinary records — or under nothing — it stays a run-as-user create.
            if (await RefuseSecureFilingAsync(tool, tablename, mapped.Item!, context, startedAt, cancellationToken)
                    .ConfigureAwait(false) is { } refusal)
            {
                return LogOutcome(context, tablename, refusal, stopwatch);
            }

            // Prefer: return=representation so the created row (incl. primary id) comes back
            // without a second GET — task-008 review knob on PostAsync.
            var response = await _dataverse.PostAsync(
                $"/api/data/v9.2/{entitySetName}",
                mapped.Item!.JsonBody,
                preferRepresentation: true,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                // Privilege-denied create surfaces the USER's own access error — never escalates.
                return LogOutcome(context, tablename, MapClientError(tool, response, startedAt), stopwatch);
            }

            var warnings = new List<string>();
            Guid? createdId = null;
            if (response.Body is { } body &&
                body.ValueKind == JsonValueKind.Object &&
                body.TryGetProperty(primaryIdAttribute, out var idProp) &&
                idProp.ValueKind == JsonValueKind.String &&
                Guid.TryParse(idProp.GetString(), out var parsedId))
            {
                createdId = parsedId;
            }
            else
            {
                warnings.Add("The record was created but Dataverse did not echo the created row; the record id could not be determined. " +
                             "Use dataverse.search_data or dataverse.read_query to locate the new record.");
            }

            // Task 142 (L1, owner Q5 + R3): a created project/matter/work assignment's "Assigned *" contacts get their
            // Collaborate grant or share now. After the create committed; never throws, never fails this create.
            if (createdId is { } createdRoot)
            {
                await Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer.RunAfterWriteAsync(
                    _scopes, tablename, createdRoot, writtenColumns: null, grantorOid: null, _logger, cancellationToken)
                    .ConfigureAwait(false);
            }

            var citationPath = createdId.HasValue
                ? DataverseRecordCitations.RecordPath(tablename, createdId.Value)
                : null;

            var result = ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = DataverseToolNames.CreateRecord,
                    tablename,
                    recordId = createdId?.ToString("D"),
                    path = citationPath,
                    columnsSet = mapped.Item.Columns,
                    columnCount = mapped.Item.Columns.Count
                },
                summary: createdId.HasValue
                    ? $"Created record {createdId:D} in '{tablename}' ({mapped.Item.Columns.Count} columns set, under the calling user's permissions)."
                    : $"Created a record in '{tablename}' ({mapped.Item.Columns.Count} columns set); id not echoed.",
                confidence: 1.0,
                execution: Timed(startedAt),
                warnings: warnings);

            if (createdId.HasValue)
            {
                result = result with
                {
                    Metadata = new Dictionary<string, object?>
                    {
                        [ToolResultMetadataKeys.Citations] = new[] { DataverseRecordCitations.ForRecord(tablename, createdId.Value) },
                        // R4-6: user-facing outcome sentence (the transcript ✅ message renders
                        // this verbatim — no instruction-to-model text). R4-3: the record
                        // reference the gate-resume seam composes the clickable MDA link from.
                        [ToolResultMetadataKeys.UserSummary] =
                            $"Record created in '{tablename}' (id {createdId:D}).",
                        [ToolResultMetadataKeys.CreatedRecord] = new ToolCreatedRecord(tablename, createdId.Value)
                    }
                };
            }

            return LogOutcome(context, tablename, result, stopwatch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(tool, "dataverse.create_record was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[dataverse.create_record] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "dataverse.create_record failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>The caller's Entra object id (the chat context's <c>oid</c>), when known.</summary>
    private static Guid? CallerObjectId(ChatInvocationContext context) =>
        Guid.TryParse(context.UserId, out var oid) && oid != Guid.Empty ? oid : null;

    /// <summary>
    /// Owner round 7 item 3: the item with the caller named in the table's "for" column
    /// (<see cref="OwnedChildWrite.ForPersonColumns"/>) — re-mapped, so the column's navigation property comes from metadata
    /// like any other lookup — and that column as SERVER-set (it names the caller, so it costs no AppendTo check). A value
    /// the request supplies is never overwritten; a caller with no linked contact (task 141) leaves a contact column blank
    /// (logged). Nothing is added for a table with no "for" column.
    /// </summary>
    private async Task<(DataverseWriteItemMapper.MappedItem Item, IReadOnlySet<string>? ServerSet, DataverseUserResponse? Failure)>
        WithForPersonAsync(string tablename, JsonElement item, DataverseWriteItemMapper.MappedItem mapped, CancellationToken ct)
    {
        if (OwnedChildWrite.ForPersonColumns.GetValueOrDefault(tablename) is not { } forColumn
            || OwnedChildWrite.Sets(item, forColumn.Column))
        {
            return (mapped, null, null);
        }

        var me = await OwnedChildWrite.WhoAmIAsync(_dataverse, ct).ConfigureAwait(false);
        if (me.Failure is { } failure)
            return (mapped, null, failure);

        Guid? person = me.SystemUserId;
        if (string.Equals(forColumn.RelatedTable, "contact", StringComparison.OrdinalIgnoreCase))
        {
            person = await LinkedContactAsync(me.SystemUserId, ct).ConfigureAwait(false);
            if (person is null)
            {
                _logger.LogWarning(
                    "assigned_unset: entity={Entity} column={Column} reason=caller_has_no_linked_contact — the record is created "
                    + "owned by its team with the 'for' column blank (never a team, never an email match; task 146 b2)",
                    tablename, forColumn.Column);
                return (mapped, null, null);
            }
        }

        var remapped = await DataverseWriteItemMapper.MapAsync(
            _dataverse, tablename, OwnedChildWrite.WithLookup(item, forColumn.Column, forColumn.RelatedTable, person.Value), ct)
            .ConfigureAwait(false);
        if (remapped.ClientFailure is { } remapFailure)
            return (mapped, null, remapFailure);
        if (remapped.Item is null)
        {
            // The table's metadata does not carry the column as a lookup of that table: create without it (logged).
            _logger.LogWarning(
                "assigned_unset: entity={Entity} column={Column} reason=column_not_mapped — {Error}",
                tablename, forColumn.Column, remapped.ValidationError);
            return (mapped, null, null);
        }

        return (remapped.Item, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { forColumn.Column }, null);
    }

    /// <summary>The caller's LINKED contact (task 141's <c>PersonIdentity.ContactId</c>) — never an email match; null when
    /// there is none or it cannot be read.</summary>
    private async Task<Guid?> LinkedContactAsync(Guid systemUserId, CancellationToken ct)
    {
        try
        {
            var person = await _identity.ResolveAsync(systemUserId, ct).ConfigureAwait(false);
            return person.ContactId is { } contactId && contactId != Guid.Empty ? contactId : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[dataverse.create_record] the caller's linked contact could not be resolved");
            return null;
        }
    }

    /// <summary>The tool result of an owned (G5) create: the same success shape as a run-as-user create, or the
    /// caller's denial, the owner refusal (its stable code and reason — reached only after AppendTo on every record the
    /// row names), a secure-filing refusal, or the caller's own Dataverse error.</summary>
    private ToolResult OwnedCreateResult(
        AnalysisTool tool, string tablename, DataverseWriteItemMapper.MappedItem item, OwnedChildWrite.Outcome owned,
        DateTimeOffset startedAt, Sprk.Bff.Api.Services.Access.SecureRootInheritResult? secured = null)
    {
        if (owned.ClientFailure is { } failure)
            return MapClientError(tool, failure, startedAt);
        if (owned.Denied is { } denied)
            return Error(tool, denied, DataverseUserClientErrorCodes.AccessDenied, startedAt);
        if (owned.SecureFilingRefused is { } secureRefusal)
            return Error(tool, secureRefusal, ToolErrorCodes.ValidationFailed, startedAt);
        if (owned.OwnerRefusal is not null || owned.CreatedId is not { } createdId)
        {
            var reason = owned.OwnerRefusal;
            return Error(tool,
                $"The record was NOT created: its owner could not be decided — {reason?.Reason} ({reason?.RefusalCode}).",
                reason?.RefusalCode ?? ToolErrorCodes.InternalError, startedAt);
        }

        // Task 158 r1 (owner round 31 item 2): created into isolation, but it could not be shared to the caller — the row
        // was deleted again, so the create is refused (never a row nobody can open).
        if (secured is { RowRemoved: true })
        {
            return Error(tool,
                $"The record was NOT created: a '{tablename}' filed under a secure record is created secure and shared to you, " +
                $"and that share could not be made ({secured.ReasonCode}), so the new record was removed again. " +
                "Nothing was created.",
                secured.ReasonCode ?? ToolErrorCodes.InternalError, startedAt);
        }

        // Created secure and shared to the caller, but a later step (its own container, its parents' sharees) did not
        // complete — never reported as a plain success (ADR-003). Nobody outside its sharing sees it meanwhile; the
        // secure-root inheritance job completes it within minutes.
        // Created into isolation, NOT shared to the caller, and the delete failed too: say exactly that (task 158 r1).
        // Task 158 r1c-v1 (verifier item 7): a self-heal is promised only when the job can deliver it — never after
        // provisioning REFUSED (e.g. the caller walled off between the plan and the provisioning), which every run refuses again.
        if (secured is { RowStranded: true })
        {
            return Error(tool,
                $"Record {createdId:D} was created in '{tablename}' as a secure record, but it could not be shared to you and " +
                $"could not be removed again ({secured.ReasonCode}). " +
                (secured.CompletesAutomatically
                    ? "It is shared to you automatically once that step succeeds (it is retried every few minutes); until then " +
                      "only an administrator can open it."
                    : "It will not be shared to you automatically: only an administrator can open it, and an administrator needs " +
                      "to review and remove it."),
                secured.ReasonCode ?? ToolErrorCodes.InternalError, startedAt);
        }

        // (Shared to the caller means provisioning got past its refusals — every refusal comes before the creator's share — so
        // what is left is a fault the job retries: the promise below holds.)
        if (secured is { IsComplete: false })
        {
            return Error(tool,
                $"Record {createdId:D} was created in '{tablename}' as a secure record shared to you, but securing it could not " +
                $"be finished yet ({secured.ReasonCode}). It is completed automatically within a few minutes; " +
                "until then only you (and the people already given access) can open it.",
                secured.ReasonCode ?? ToolErrorCodes.InternalError, startedAt);
        }

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new
            {
                tool = DataverseToolNames.CreateRecord,
                tablename,
                recordId = createdId.ToString("D"),
                path = DataverseRecordCitations.RecordPath(tablename, createdId),
                columnsSet = item.Columns,
                columnCount = item.Columns.Count
            },
            summary: $"Created record {createdId:D} in '{tablename}' ({item.Columns.Count} columns set). It is owned by the " +
                     "team of the record it is filed under (or the calling user's own team), checked against the calling " +
                     "user's permissions.",
            confidence: 1.0,
            execution: Timed(startedAt)) with
        {
            Metadata = new Dictionary<string, object?>
            {
                [ToolResultMetadataKeys.Citations] = new[] { DataverseRecordCitations.ForRecord(tablename, createdId) },
                [ToolResultMetadataKeys.UserSummary] = $"Record created in '{tablename}' (id {createdId:D}).",
                [ToolResultMetadataKeys.CreatedRecord] = new ToolCreatedRecord(tablename, createdId)
            }
        };
    }

    /// <summary>
    /// For a row this handler cannot re-own (a table outside the ownership set, or a root) that names a project, matter
    /// or work assignment: AppendTo on each named record AS THE CALLER, then the one resolver's answer — refused when it
    /// is the Secure team (or a refusal), so the row never sits in the caller's ordinary business unit under a secure
    /// record (task 146 r2). <c>null</c> = proceed as a run-as-user create.
    /// </summary>
    private async Task<ToolResult?> RefuseSecureFilingAsync(
        AnalysisTool tool, string tablename, DataverseWriteItemMapper.MappedItem item, ChatInvocationContext context,
        DateTimeOffset startedAt, CancellationToken ct)
    {
        var parents = OwnedChildWrite.ParentsOf(item);
        if (parents.Count == 0)
            return null;

        var me = await OwnedChildWrite.WhoAmIAsync(_dataverse, ct).ConfigureAwait(false);
        if (me.Failure is { } failure)
            return MapClientError(tool, failure, startedAt);

        var appendTo = await OwnedChildWrite.CheckCallerMayAppendToAsync(
            _dataverse, me.SystemUserId,
            item.Lookups.Where(l => Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsOwnershipParent(l.RelatedTable)),
            ct).ConfigureAwait(false);
        if (appendTo.Denied is { } denied)
            return Error(tool, denied, DataverseUserClientErrorCodes.AccessDenied, startedAt);

        var owner = await _ownership.ResolveOwnerAsync(
            Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ForParents(parents, CallerObjectId(context), me.SystemUserId),
            ct).ConfigureAwait(false);
        if (owner.IsRefused || owner.IsSecureOwner)
        {
            return Error(tool,
                $"A '{tablename}' record cannot be filed under a secure record from chat, and was NOT created" +
                (owner.IsRefused ? $" ({owner.RefusalCode})." : ".") +
                " Create it from the record itself.",
                owner.RefusalCode ?? ToolErrorCodes.ValidationFailed, startedAt);
        }

        return null;
    }

    private ToolResult LogOutcome(ChatInvocationContext context, string tablename, ToolResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        // ADR-015 / NFR-07: entity + outcome + duration + deterministic IDs only — never values.
        _logger.LogInformation(
            "[dataverse.create_record][ADR-015] entity={Entity} outcome={Outcome} decisionId={DecisionId} durationMs={DurationMs}",
            tablename, result.Success ? "ok" : result.ErrorCode, context.DecisionId, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private ToolResult MapClientError(AnalysisTool tool, DataverseUserResponse response, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            response.ErrorMessage ?? "Dataverse request failed.",
            response.ErrorCode,
            Timed(startedAt));

    private ToolResult Error(AnalysisTool tool, string message, string code, DateTimeOffset startedAt) =>
        ToolResult.Error(HandlerId, tool.Id, tool.Name, message, code, Timed(startedAt));

    private static ToolExecutionMetadata Timed(DateTimeOffset startedAt) =>
        new() { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow };

    internal static bool TryParseArgs(string? argsJson, out string tablename, out JsonElement item, out string? error)
    {
        tablename = string.Empty;
        item = default;
        error = null;

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            error = "Tool arguments JSON is required (expected { \"tablename\": \"…\", \"item\": { … } }).";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must be a JSON object.";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("tablename", out var tableProp) ||
                tableProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(tableProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'tablename' string (the table's logical name).";
                return false;
            }
            tablename = tableProp.GetString()!.Trim().ToLowerInvariant();
            if (!LogicalNameRegex().IsMatch(tablename))
            {
                error = $"'{tablename}' is not a valid table logical name.";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("item", out var itemProp) || itemProp.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must include an 'item' object of column logical names to values.";
                return false;
            }
            item = itemProp.Clone();
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
