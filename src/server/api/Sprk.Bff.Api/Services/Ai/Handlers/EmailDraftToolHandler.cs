using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>email.draft</c> tool — creates a Spaarke
/// <c>sprk_communication</c> DRAFT record for user review in the Communication (Email)
/// service (spaarke-ai-architecture-redesign-r1 task 041, FR-P3-02).
/// </summary>
/// <remarks>
/// <para>
/// <b>Delegation contract (spec FR-P3-02 owner clarification)</b>: the draft is a record in
/// Spaarke's OWN Communication (Email) service — explicitly NOT an Outlook draft. The record
/// uses the same <c>sprk_communication</c> column contract that
/// <c>Services/Communication/CommunicationService</c> writes (name / subject / body / to / cc /
/// type / direction / bodyformat / regarding association), so the Communication UI treats it as
/// a first-class draft. Sending remains USER-INITIATED through the Communication service (which
/// sends via Graph); this handler performs no Graph call of any kind.
/// </para>
/// <para>
/// <b>DRAFT-ONLY invariant (server-pinned)</b>: <c>statuscode</c> is hardcoded to
/// <c>Draft (1)</c> and <c>statecode</c> to <c>Active (0)</c> INSIDE this handler — the model's
/// arguments carry no status vocabulary, and the closed argument parser ignores everything
/// outside the declared schema, so no prompt content can produce a sent/queued communication.
/// Assistant-initiated send is the named FR-P4-07 deferral.
/// </para>
/// <para>
/// <b>SIDE-EFFECT tool (communicate)</b>: the <c>sprk_analysistool</c> row declares
/// <c>sprk_sideeffectclass = Communicate (100000002)</c>. The ONE confirmation gate
/// (<c>SideEffectGateAIFunction</c>, FR-P2-02) suspends/resumes this tool BY THAT DECLARED
/// CLASS — deliberately, NO gating or confirmation logic lives in this class; when invoked,
/// it executes. (Never gate by tool-name lists — ADR-039.)
/// </para>
/// <para>
/// <b>User-OBO ONLY (spec MUST rule)</b>: the record is created through
/// <see cref="IDataverseUserClient"/> under the CALLING USER's exchanged token — a user who
/// cannot create communications fails with their own access error; no app-only path is
/// reachable from this class (same posture as the task-009 dataverse.* write handlers).
/// </para>
/// <para>
/// <b>Ledger grounding (ADR-039/ADR-040)</b>: <c>source_refs</c> carries the addressable
/// ledger keys (<c>{bindingId}@t{n}</c>) and/or record citation paths the draft was composed
/// from (e.g. the draft-correspondence capability output and the matter created earlier in the
/// session). They are identifiers only (NFR-07-safe), echoed on the tool result and recorded on
/// the turn's ToolChain by the loop contract, and denormalized onto the record's
/// <c>sprk_regardingrecordid</c>/association fields where a regarding target is supplied.
/// </para>
/// <para>
/// <b>§11 Component Justification</b>: (1) <i>Existing</i> — <c>dataverse.create_record</c> can
/// insert arbitrary rows, but it would require the model to know the sprk_communication column
/// contract free-form, would gate as generic <c>write</c> rather than <c>communicate</c>, and —
/// decisive — could not server-pin the DRAFT-ONLY invariant (a steered model could set
/// <c>statuscode = Send</c>). (2) <i>Extension</i> — teaching create_record communication
/// defaults would smuggle per-capability branching into the frozen GA-contract generic tool
/// (the r7 anti-pattern ADR-039 deletes). (3) <i>Cost of doing nothing</i> — FR-P3-02's G-P3
/// step ("client-letter draft in the Communication service") has no executor, and the
/// communicate side-effect class has no first consumer proving the gate's communicate leg.
/// </para>
/// <para>
/// <b>ADR-015 / NFR-07</b>: telemetry carries recipient/source-ref COUNTS, outcome, duration,
/// and the created record id — never subject/body text or email addresses.
/// </para>
/// </remarks>
public sealed class EmailDraftToolHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(EmailDraftToolHandler);

    /// <summary>Communication table logical name (the Communication service's contract).</summary>
    internal const string CommunicationTable = "sprk_communication";

    // Dataverse option-set values come from the Communication service's OWN enums
    // (Services/Communication/Models — same assembly) so the pinned discriminators
    // cannot drift from the service that owns the sprk_communication contract.
    // (code-review task-041: enum refs replace mirrored int constants.)
    private const int StatusDraft = (int)CommunicationStatus.Draft;
    private const int StateActive = 0;                    // statecode Active (no enum exists)
    private const int TypeEmail = (int)CommunicationType.Email;
    private const int DirectionOutgoing = (int)CommunicationDirection.Outgoing;
    private const int BodyFormatPlainText = (int)BodyFormat.PlainText;
    private const int BodyFormatHtml = (int)BodyFormat.HTML;

    private const int MaxRecipients = 20;
    private const int MaxSourceRefs = 20;
    private const int MaxSubjectLength = 400;
    private const int NameTruncateLength = 200;

    /// <summary>
    /// Regarding tables the Communication service maps to first-class regarding lookups.
    /// MIRRORS <c>CommunicationService.RegardingLookupMap</c> (table → lookup column) — the
    /// Communication service owns this contract; extending it happens THERE first.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> RegardingLookupColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = "sprk_regardingmatter",
            ["sprk_organization"] = "sprk_regardingorganization",
            ["contact"] = "sprk_regardingperson",
            ["sprk_project"] = "sprk_regardingproject",
            ["sprk_analysis"] = "sprk_regardinganalysis",
            ["sprk_budget"] = "sprk_regardingbudget",
            ["sprk_invoice"] = "sprk_regardinginvoice",
            ["sprk_workassignment"] = "sprk_regardingworkassignment",
        };

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<EmailDraftToolHandler> _logger;

    public EmailDraftToolHandler(
        IDataverseUserClient dataverse,
        ILogger<EmailDraftToolHandler> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "SYS-Email Draft",
        Description: "Creates a DRAFT email record in Spaarke's Communication service for the user to review " +
                     "and send. Never sends email — sending is always user-initiated from the Communication " +
                     "service. Compose the subject and body from grounded session outputs (summaries, records " +
                     "created this session) and cite their ledger keys in source_refs. " +
                     "SIDE-EFFECT tool (communicate): invocation is confirmation-gated.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "subject",
                "Email subject line (required, non-empty).",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "body",
                "Full email body text (required). Plain text by default; set body_format to 'html' when the body is HTML.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "to",
                "Recipient email addresses (required, at least one).",
                ToolParameterType.Array,
                Required: true),
            new ToolParameterDefinition(
                "cc",
                "Optional CC email addresses.",
                ToolParameterType.Array,
                Required: false),
            new ToolParameterDefinition(
                "body_format",
                "Body format: 'text' (default) or 'html'.",
                ToolParameterType.String,
                Required: false),
            new ToolParameterDefinition(
                "regarding",
                "Optional record this draft is about, as {\"table\": \"sprk_matter\", \"recordId\": \"guid\", \"name\": \"display name\"}. " +
                "Supported tables: sprk_matter, sprk_organization, contact, sprk_project, sprk_analysis, sprk_budget, sprk_invoice, sprk_workassignment.",
                ToolParameterType.Object,
                Required: false),
            new ToolParameterDefinition(
                "source_refs",
                "Ledger output keys ({bindingId}@t{n}) and/or record citation paths (tables/{table}/records/{guid}) " +
                "the draft content was composed from. Cite every session output you used.",
                ToolParameterType.Array,
                Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "EmailDraftToolHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "EmailDraftToolHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed));

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (!TryParseArgs(context.ToolArgumentsJson, out _, out var error))
            return ToolValidationResult.Failure(error!);

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

        if (!TryParseArgs(context.ToolArgumentsJson, out var args, out var parseError))
        {
            return Error(tool, parseError!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        try
        {
            // Entity-set resolution under the USER's token (task-009 pattern): a user for whom
            // the communication table is invisible fails HERE, before any write is attempted.
            var metaResponse = await _dataverse.GetAsync(
                $"EntityDefinitions(LogicalName='{CommunicationTable}')?$select=EntitySetName,PrimaryIdAttribute",
                cancellationToken).ConfigureAwait(false);
            if (!metaResponse.IsSuccess)
            {
                return LogOutcome(context, args, MapClientError(tool, metaResponse, startedAt), stopwatch);
            }
            var entitySetName = GetString(metaResponse.Body!.Value, "EntitySetName");
            var primaryIdAttribute = GetString(metaResponse.Body!.Value, "PrimaryIdAttribute");
            if (entitySetName is null || primaryIdAttribute is null)
            {
                return LogOutcome(context, args,
                    Error(tool, $"Table '{CommunicationTable}' has no entity-set / primary-id metadata.", ToolErrorCodes.InternalError, startedAt),
                    stopwatch);
            }

            // Build the record ITEM server-side (Communication-service column contract).
            // DRAFT-ONLY: statuscode/statecode are pinned constants — never model-supplied.
            var item = BuildCommunicationItem(args, context);

            // Reuse the task-009 write mapper: metadata-driven lookup navigation-property
            // resolution for the regarding association, OData-annotation smuggling blocked.
            var mapped = await DataverseWriteItemMapper.MapAsync(_dataverse, CommunicationTable, item, cancellationToken).ConfigureAwait(false);
            if (mapped.ValidationError is not null)
            {
                return LogOutcome(context, args, Error(tool, mapped.ValidationError, ToolErrorCodes.ValidationFailed, startedAt), stopwatch);
            }
            if (mapped.ClientFailure is not null)
            {
                return LogOutcome(context, args, MapClientError(tool, mapped.ClientFailure, startedAt), stopwatch);
            }

            var response = await _dataverse.PostAsync(
                $"/api/data/v9.2/{entitySetName}",
                mapped.Item!.JsonBody,
                preferRepresentation: true,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                // Privilege-denied create surfaces the USER's own access error — never escalates.
                return LogOutcome(context, args, MapClientError(tool, response, startedAt), stopwatch);
            }

            var warnings = new List<string>();
            Guid? communicationId = null;
            if (response.Body is { } body &&
                body.ValueKind == JsonValueKind.Object &&
                body.TryGetProperty(primaryIdAttribute, out var idProp) &&
                idProp.ValueKind == JsonValueKind.String &&
                Guid.TryParse(idProp.GetString(), out var parsedId))
            {
                communicationId = parsedId;
            }
            else
            {
                warnings.Add("The draft record was created but Dataverse did not echo the created row; the record id " +
                             "could not be determined. The user can locate the draft in the Communication service.");
            }

            var citationPath = communicationId.HasValue
                ? DataverseRecordCitations.RecordPath(CommunicationTable, communicationId.Value)
                : null;

            var result = ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = "email.draft",
                    status = "draft",
                    communicationId = communicationId?.ToString("D"),
                    path = citationPath,
                    recipientCount = args.To.Count,
                    ccCount = args.Cc.Count,
                    regardingTable = args.RegardingTable,
                    regardingRecordId = args.RegardingRecordId?.ToString("D"),
                    sourceRefs = args.SourceRefs
                },
                summary: communicationId.HasValue
                    ? $"Created DRAFT communication {communicationId:D} ({args.To.Count} recipient(s)) in the Spaarke " +
                      "Communication service. NO email was sent — tell the user the draft is ready for their review, " +
                      "and that they can edit and send it from Communications."
                    : $"Created a DRAFT communication ({args.To.Count} recipient(s)); id not echoed. NO email was sent — " +
                      "the user reviews and sends from the Communication service.",
                confidence: 1.0,
                execution: Timed(startedAt),
                warnings: warnings);

            if (communicationId.HasValue)
            {
                result = result with
                {
                    Metadata = new Dictionary<string, object?>
                    {
                        [ToolResultMetadataKeys.Citations] = new[]
                        {
                            DataverseRecordCitations.ForRecord(CommunicationTable, communicationId.Value)
                        }
                    }
                };
            }

            return LogOutcome(context, args, result, stopwatch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(tool, "email.draft was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[email.draft] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "email.draft failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── item construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <c>sprk_communication</c> item in the write-mapper's GA item shape.
    /// The DRAFT-ONLY pins (<c>statuscode = 1</c>, <c>statecode = 0</c>) and the
    /// outgoing-email discriminators are CONSTANTS here — the parsed args carry no status,
    /// direction, or type vocabulary, so no model/prompt content can alter them.
    /// </summary>
    private static JsonElement BuildCommunicationItem(EmailDraftArgs args, ChatInvocationContext context)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("sprk_name", Truncate($"Email: {args.Subject}", NameTruncateLength));
            writer.WriteNumber("sprk_communicationtype", TypeEmail);
            writer.WriteNumber("statuscode", StatusDraft);          // DRAFT-ONLY (server-pinned)
            writer.WriteNumber("statecode", StateActive);
            writer.WriteNumber("sprk_direction", DirectionOutgoing);
            writer.WriteNumber("sprk_bodyformat", args.IsHtml ? BodyFormatHtml : BodyFormatPlainText);
            writer.WriteString("sprk_to", string.Join("; ", args.To));
            if (args.Cc.Count > 0)
            {
                writer.WriteString("sprk_cc", string.Join("; ", args.Cc));
            }
            writer.WriteString("sprk_subject", args.Subject);
            writer.WriteString("sprk_body", args.Body);
            writer.WriteString("sprk_correlationid", context.DecisionId.ToString("N"));

            if (args.RegardingTable is not null && args.RegardingRecordId is { } regardingId)
            {
                // Communication-service association contract (regarding lookup + denormalized
                // fields). Lookup uses the mapper's object shape so the navigation property is
                // resolved from metadata (custom-lookup casing safe).
                writer.WriteNumber("sprk_associationcount", 1);
                writer.WritePropertyName(RegardingLookupColumns[args.RegardingTable]);
                writer.WriteStartObject();
                writer.WriteString("relatedTable", args.RegardingTable);
                writer.WriteString("recordId", regardingId.ToString("D"));
                writer.WriteEndObject();
                writer.WriteString("sprk_regardingrecordid", Truncate(regardingId.ToString("D"), 100));
                if (!string.IsNullOrWhiteSpace(args.RegardingName))
                {
                    writer.WriteString("sprk_regardingrecordname", Truncate(args.RegardingName!, 100));
                }
            }

            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    // ── argument parsing (closed vocabulary — unknown members are ignored) ─────

    internal sealed record EmailDraftArgs(
        string Subject,
        string Body,
        IReadOnlyList<string> To,
        IReadOnlyList<string> Cc,
        bool IsHtml,
        string? RegardingTable,
        Guid? RegardingRecordId,
        string? RegardingName,
        IReadOnlyList<string> SourceRefs);

    internal static bool TryParseArgs(string? argsJson, out EmailDraftArgs args, out string? error)
    {
        args = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            error = "Tool arguments JSON is required (expected { \"subject\": \"…\", \"body\": \"…\", \"to\": [\"…\"] }).";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must be a JSON object.";
                return false;
            }

            var subject = ReadString(root, "subject");
            if (string.IsNullOrWhiteSpace(subject))
            {
                error = "A non-empty 'subject' string is required.";
                return false;
            }
            if (subject!.Length > MaxSubjectLength)
            {
                error = $"'subject' exceeds {MaxSubjectLength} characters.";
                return false;
            }

            var bodyText = ReadString(root, "body");
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                error = "A non-empty 'body' string is required.";
                return false;
            }

            if (!TryReadEmailArray(root, "to", required: true, out var to, out error))
            {
                return false;
            }
            if (!TryReadEmailArray(root, "cc", required: false, out var cc, out error))
            {
                return false;
            }

            var format = ReadString(root, "body_format")?.Trim().ToLowerInvariant();
            if (format is not null && format is not ("text" or "html"))
            {
                error = "'body_format' must be 'text' or 'html'.";
                return false;
            }

            string? regardingTable = null;
            Guid? regardingRecordId = null;
            string? regardingName = null;
            if (root.TryGetProperty("regarding", out var regarding) && regarding.ValueKind != JsonValueKind.Null)
            {
                if (regarding.ValueKind != JsonValueKind.Object)
                {
                    error = "'regarding' must be an object: {\"table\": \"…\", \"recordId\": \"guid\"}.";
                    return false;
                }
                var table = ReadString(regarding, "table")?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(table) || !RegardingLookupColumns.ContainsKey(table!))
                {
                    error = "'regarding.table' must be one of: " + string.Join(", ", RegardingLookupColumns.Keys) + ".";
                    return false;
                }
                var recordIdText = ReadString(regarding, "recordId");
                if (!Guid.TryParse(recordIdText, out var parsedRegardingId))
                {
                    error = "'regarding.recordId' must be a GUID. Find it via dataverse.search_data / dataverse.read_query, " +
                            "or use the id of a record created earlier this session.";
                    return false;
                }
                regardingTable = table;
                regardingRecordId = parsedRegardingId;
                regardingName = ReadString(regarding, "name");
            }

            var sourceRefs = new List<string>();
            if (root.TryGetProperty("source_refs", out var refs) && refs.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in refs.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(r.GetString()))
                    {
                        sourceRefs.Add(r.GetString()!.Trim());
                    }
                }
                if (sourceRefs.Count > MaxSourceRefs)
                {
                    error = $"'source_refs' exceeds {MaxSourceRefs} entries.";
                    return false;
                }
            }

            args = new EmailDraftArgs(
                subject.Trim(),
                bodyText!,
                to,
                cc,
                IsHtml: format == "html",
                regardingTable,
                regardingRecordId,
                regardingName,
                sourceRefs);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadEmailArray(
        JsonElement root, string property, bool required,
        out IReadOnlyList<string> values, out string? error)
    {
        values = Array.Empty<string>();
        error = null;

        if (!root.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                error = $"'{property}' is required: an array with at least one recipient email address.";
                return false;
            }
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = $"'{property}' must be an array of email address strings.";
            return false;
        }

        var list = new List<string>();
        foreach (var entry in element.EnumerateArray())
        {
            var address = entry.ValueKind == JsonValueKind.String ? entry.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }
            // Light shape check only — full validation belongs to the Communication service
            // at (user-initiated) send time.
            var at = address!.IndexOf('@');
            if (at <= 0 || at == address.Length - 1)
            {
                error = $"'{property}' contains an entry that is not an email address.";
                return false;
            }
            list.Add(address);
        }

        if (required && list.Count == 0)
        {
            error = $"'{property}' must contain at least one recipient email address.";
            return false;
        }
        if (list.Count > MaxRecipients)
        {
            error = $"'{property}' exceeds {MaxRecipients} recipients.";
            return false;
        }

        values = list;
        return true;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private ToolResult LogOutcome(ChatInvocationContext context, EmailDraftArgs args, ToolResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        // ADR-015 / NFR-07: counts + outcome + duration + deterministic IDs only — never
        // subject/body text or email addresses.
        _logger.LogInformation(
            "[email.draft][ADR-015] outcome={Outcome} recipientCount={RecipientCount} sourceRefCount={SourceRefCount} " +
            "hasRegarding={HasRegarding} decisionId={DecisionId} durationMs={DurationMs}",
            result.Success ? "ok" : result.ErrorCode, args.To.Count, args.SourceRefs.Count,
            args.RegardingRecordId.HasValue, context.DecisionId, stopwatch.ElapsedMilliseconds);
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

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
