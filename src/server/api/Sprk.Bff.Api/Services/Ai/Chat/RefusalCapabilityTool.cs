using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// The honest-refusal outcome of the agent-turn loop (FR-P2-04 /
/// ADR-039 grounded-execution clause (d); canonical doc §3.10.7.2 Layer 4).
/// Projects the tenant's <c>no_match_handler</c> Binding row
/// (<see cref="ConsumerTypes.NoMatchHandler"/>) into the loop's tool list. When
/// an utterance matches no other capability/tool and cannot be answered as a
/// cited ad-hoc read, the model invokes THIS tool; the refusal it relays is the
/// rendered output of the Binding's prompted Action (REF-CHAT@v1) — catalog
/// data, never hardcoded copy, never an improvised apology.
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>:
/// (1) <i>Existing</i> — overlaps <see cref="BindingCapabilityTool"/>, which projects
/// catalog Bindings and executes them by id through
/// <see cref="SessionDispatchOrchestrator"/>. (2) <i>Extension</i> — the generic
/// dispatch cannot serve the refusal: <see cref="SessionDispatchOrchestrator"/>
/// requires session files with extractable text (it errors "No session files were
/// available" at zero files), while a refusal MUST work in a file-less session;
/// the refusal additionally terminates the turn (never suspends — see
/// <see cref="PendingPlanManager"/> reference note below) and carries the
/// <c>dispatch_refused</c> telemetry contract. Both classes converge on the SAME
/// executor + ledger seam (<see cref="IActionRunner"/> → <see cref="IOutputRouter"/>)
/// — no parallel execution stack. (3) <i>Cost-of-doing-nothing</i> — FR-P2-04
/// acceptance fails concretely: an off-catalog utterance in a file-less session
/// yields "No session files were available" (or free-form model apology) instead
/// of the tenant's refusal template, and no <c>dispatch_refused</c> event lands.
/// </para>
/// <para>
/// <b>ONE dispatch protocol (ADR-039)</b>: the refusal is a LOOP OUTCOME, not a
/// fallback dispatcher. There is no server-side re-detection of "no match" —
/// the model concluding that no projected capability serves the request and
/// invoking this tool IS the fourth outcome of the G-P2 contract
/// (do / clarify-then-do / cited ad-hoc answer / honest refusal). Which Binding
/// renders the refusal is decided by catalog data only: the enabled
/// <c>sprk_playbookconsumer</c> row whose <c>sprk_consumertype</c> is
/// <see cref="ConsumerTypes.NoMatchHandler"/> (per-tenant — the row lives in the
/// tenant's Dataverse). No appsettings key, no hardcoded template.
/// </para>
/// <para>
/// <b>ADR-040 storage precedes rendering</b>: the rendered refusal is written to
/// the session ledger as an addressable <see cref="SessionOutput"/> (via
/// <see cref="IOutputRouter.RouteAsync"/>) BEFORE this tool returns — the text
/// the model relays is read back FROM the stored entry (render follows store,
/// same contract as the Click path).
/// </para>
/// <para>
/// <b>NFR-07 telemetry hygiene</b>: the <c>dispatch_refused</c> event
/// (<see cref="AiTelemetry.RecordDispatchRefused"/> counter + the
/// <c>[FR-P2-04][dispatch_refused]</c> structured log) carries identifiers only —
/// tenant, session, binding, ucid, ledger key. The utterance and the model's
/// <c>unsupported_request</c> label NEVER appear in logs/metrics (the label is
/// additionally in <see cref="AgentTurnContract.AlwaysRedactedArgNames"/> so the
/// ToolChain ledger audit redacts it by name). The refusal payload itself lives
/// only in the session ledger (ADR-015 Tier 3, user-owned/GDPR-erasable).
/// </para>
/// <para>
/// <b>Refusals never suspend</b>: unlike side-effecting capabilities, the refusal
/// terminates the turn — it never creates a <see cref="PendingPlanManager"/> gate
/// entry (task 031's store is for write-shaped dispatch; a refusal has no side
/// effect to confirm).
/// </para>
/// <para>
/// <b>Scope discipline (ADR-010)</b>: captures the ROOT
/// <see cref="IServiceProvider"/> and opens a fresh scope per invocation — same
/// rule as <see cref="BindingCapabilityTool"/>.
/// </para>
/// </remarks>
public sealed class RefusalCapabilityTool : AIFunction
{
    /// <summary>
    /// The single argument the model supplies: a short neutral label of what the
    /// user asked for, used only as the prompted Action's input so the rendered
    /// refusal can acknowledge the request. Always redacted from the ToolChain
    /// audit by name (NFR-07 — see <see cref="AgentTurnContract.AlwaysRedactedArgNames"/>).
    /// </summary>
    public const string UnsupportedRequestArgName = "unsupported_request";

    /// <summary>Cap on the refusal text handed back to the model (mirrors <see cref="BindingCapabilityTool.MaxResultChars"/> intent; refusals are short).</summary>
    internal const int MaxResultChars = 4_000;

    /// <summary>
    /// Default parameter schema when the catalog Action carries no
    /// <c>sprk_inputschema</c> — same maker-data tolerance as
    /// <see cref="BindingCapabilityTool"/>.
    /// </summary>
    private static readonly JsonElement DefaultSchema = JsonDocument.Parse(
        """
        {"type":"object","properties":{"unsupported_request":{"type":"string","description":"Short neutral label (10 words or fewer) of what the user asked for, e.g. 'document translation'. Do not copy the user's message verbatim."}},"required":["unsupported_request"],"additionalProperties":false}
        """).RootElement.Clone();

    private readonly Binding _binding;
    private readonly IServiceProvider _rootServices;
    private readonly string _tenantId;
    private readonly string _sessionId;
    private readonly ILogger _logger;
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _schema;

    public RefusalCapabilityTool(
        Binding binding,
        IServiceProvider rootServices,
        string tenantId,
        string sessionId,
        ILogger logger)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
        _tenantId = !string.IsNullOrWhiteSpace(tenantId) ? tenantId : throw new ArgumentException("tenantId required", nameof(tenantId));
        _sessionId = !string.IsNullOrWhiteSpace(sessionId) ? sessionId : throw new ArgumentException("sessionId required", nameof(sessionId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!string.Equals(binding.ConsumerType, ConsumerTypes.NoMatchHandler, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Binding {binding.BindingId} has consumer type '{binding.ConsumerType}' — the refusal tool " +
                $"projects only the tenant's '{ConsumerTypes.NoMatchHandler}' Binding (FR-P2-04).",
                nameof(binding));
        }

        if (string.IsNullOrWhiteSpace(binding.ToolDescription))
        {
            // Same catalog opt-in rule as every projected capability (ADR-039):
            // the maker-authored sprk_tooldescription IS the intent surface;
            // without it the refusal capability is not text-projectable.
            throw new ArgumentException(
                $"Binding {binding.BindingId} has no sprk_tooldescription — not text-projectable (ADR-039 catalog opt-in).",
                nameof(binding));
        }

        _name = BindingCapabilityTool.BuildFunctionName(binding.ConsumerType);
        _description = binding.ToolDescription!;
        _schema = ParseSchema(binding.InputSchemaJson);
    }

    /// <summary>The projected Binding (exposed for the surface pre-filter + tests — same contract as <see cref="BindingCapabilityTool.Binding"/>).</summary>
    public Binding Binding => _binding;

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string Description => _description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _schema;

    private static JsonElement ParseSchema(string? inputSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(inputSchemaJson))
        {
            return DefaultSchema;
        }

        try
        {
            using var doc = JsonDocument.Parse(inputSchemaJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.Clone()
                : DefaultSchema;
        }
        catch (JsonException)
        {
            // Maker data errors degrade, they never throw (routing tolerance rule).
            return DefaultSchema;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        await using var scope = _rootServices.CreateAsyncScope();
        var sessionManager = scope.ServiceProvider.GetService<ChatSessionManager>();
        var scopeResolver = scope.ServiceProvider.GetService<IScopeResolverService>();
        var actionRunner = scope.ServiceProvider.GetService<IActionRunner>();
        var outputRouter = scope.ServiceProvider.GetService<IOutputRouter>();
        var telemetry = scope.ServiceProvider.GetService<AiTelemetry>();

        if (sessionManager is null || scopeResolver is null || actionRunner is null || outputRouter is null)
        {
            // Compound AI gate OFF / partial environment — degrade honestly, never fabricate.
            _logger.LogWarning(
                "[FR-P2-04][dispatch_refused] refusal capability unavailable (missing services) — " +
                "tenant={TenantId} session={SessionId} binding={BindingId}",
                _tenantId, _sessionId, _binding.BindingId);
            telemetry?.RecordDispatchRefused(rendered: false, _tenantId);
            return BuildRenderFailureInstruction();
        }

        try
        {
            if (_binding.ActionId is null || _binding.ActionId.Value == Guid.Empty)
            {
                // Catalog authoring error — the refusal Binding must target a prompted Action.
                _logger.LogError(
                    "[FR-P2-04] no_match_handler Binding {BindingId} has no sprk_action target — " +
                    "refusal template cannot render. Fix the catalog row.",
                    _binding.BindingId);
                telemetry?.RecordDispatchRefused(rendered: false, _tenantId);
                return BuildRenderFailureInstruction();
            }

            var session = await sessionManager
                .GetSessionAsync(_tenantId, _sessionId, cancellationToken)
                .ConfigureAwait(false);
            if (session is null)
            {
                _logger.LogWarning(
                    "[FR-P2-04] refusal invoked for unknown session — tenant={TenantId} session={SessionId}",
                    _tenantId, _sessionId);
                telemetry?.RecordDispatchRefused(rendered: false, _tenantId);
                return BuildRenderFailureInstruction();
            }

            var action = await scopeResolver
                .GetActionAsync(_binding.ActionId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (action is null)
            {
                _logger.LogError(
                    "[FR-P2-04] no_match_handler Binding {BindingId} references Action {ActionId}, " +
                    "but that sprk_analysisaction row could not be loaded. Fix the Binding's sprk_action lookup.",
                    _binding.BindingId, _binding.ActionId.Value);
                telemetry?.RecordDispatchRefused(rendered: false, _tenantId);
                return BuildRenderFailureInstruction();
            }

            // ── Prompted render: the refusal copy is the Action's JPS (catalog data,
            // prompt-controlled, honest about what the platform can/cannot do). The
            // model's short unsupported_request label is the render input so the
            // refusal can acknowledge the request; it flows ONLY into the LLM prompt
            // and the Tier-3 ledger payload — never into logs/metrics (NFR-07).
            var requestedLabel = ReadUnsupportedRequestLabel(arguments);
            var documentText = new DocumentText
            {
                DocumentId = null,
                FileName = "off-catalog-request",
                ExtractedText =
                    $"The user asked for: {requestedLabel}\n" +
                    "This request matched no capability in the platform's closed catalog and could not " +
                    "be answered from the session's documents or connected records.",
            };
            var runContext = new LinearRunContext
            {
                ConsumerType = _binding.ConsumerType,
                TenantId = _tenantId,
            };

            var output = await actionRunner
                .RunAsync(action, documentText, runContext, cancellationToken)
                .ConfigureAwait(false);

            // ── ADR-040 SEAM: the universal ledger write BEFORE render. The refusal is
            // an addressable SessionOutput ({bindingId}@t{n}); the text relayed below is
            // read back FROM the stored entry (render follows store).
            var routed = await outputRouter
                .RouteAsync(session, _binding, output, sourceRefs: null, cancellationToken)
                .ConfigureAwait(false);

            // ── dispatch_refused telemetry (NFR-09 / ADR-016; identifiers only, NFR-07).
            telemetry?.RecordDispatchRefused(rendered: true, _tenantId);
            _logger.LogInformation(
                "[FR-P2-04][dispatch_refused] honest refusal rendered — tenant={TenantId} session={SessionId} " +
                "binding={BindingId} ucid={Ucid} ledgerKey={LedgerKey}",
                _tenantId, _sessionId, _binding.BindingId, _binding.Ucid ?? _binding.ConsumerType, routed.Entry.Key);

            var refusalText = ExtractRefusalText(routed.Entry.Payload);
            if (refusalText.Length > MaxResultChars)
            {
                refusalText = refusalText[..MaxResultChars] + "…";
            }

            return
                $"REFUSAL RENDERED (already stored to the session ledger as {routed.Entry.Key}). " +
                "Relay the following message to the user VERBATIM as your entire reply — do not shorten it, " +
                "add apologies, or invent capabilities or alternatives beyond it:\n\n" +
                refusalText;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The refusal capability itself failed. The turn STILL ends in a refusal —
            // count the backlog signal, then instruct the model to decline plainly
            // (a grounded terminal instruction, not template copy).
            _logger.LogError(ex,
                "[FR-P2-04][dispatch_refused] refusal template render failed — tenant={TenantId} " +
                "session={SessionId} binding={BindingId}",
                _tenantId, _sessionId, _binding.BindingId);
            telemetry?.RecordDispatchRefused(rendered: false, _tenantId);
            return BuildRenderFailureInstruction();
        }
    }

    /// <summary>
    /// The stored refusal payload's user-facing text: the REF-CHAT@v1 output
    /// schema's single <c>refusal</c> string. Shape drift degrades to the raw
    /// payload text — the model always receives the STORED content (render
    /// follows store), never a substitute.
    /// </summary>
    internal static string ExtractRefusalText(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("refusal", out var refusal)
            && refusal.ValueKind == JsonValueKind.String
            && refusal.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        return payload.ValueKind == JsonValueKind.String
            ? payload.GetString() ?? string.Empty
            : payload.GetRawText();
    }

    /// <summary>
    /// Deterministic instruction returned when the refusal template cannot render
    /// (missing services, catalog error, executor failure). This is loop-contract
    /// instruction — NOT refusal copy: the tenant's template remains the only
    /// user-facing refusal text; this path merely keeps the failure honest
    /// (ADR-039: never fabricate, never improvise capabilities).
    /// </summary>
    internal static string BuildRenderFailureInstruction() =>
        "REFUSAL (template unavailable): the request is outside the platform's available capabilities, " +
        "and the refusal template could not be rendered. Tell the user plainly that this request is not " +
        "something the assistant can do here. Do NOT guess, do NOT invent capabilities or alternatives, " +
        "and do NOT answer from general knowledge.";

    /// <summary>
    /// Tolerant read of the model-supplied <see cref="UnsupportedRequestArgName"/>
    /// label. Missing/wrong-shape degrades to a neutral placeholder — the render
    /// never throws for arg drift.
    /// </summary>
    internal static string ReadUnsupportedRequestLabel(AIFunctionArguments? arguments)
    {
        if (arguments is not null
            && arguments.TryGetValue(UnsupportedRequestArgName, out var raw))
        {
            var label = raw switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label!.Trim();
            }
        }

        return "(unspecified request)";
    }
}
