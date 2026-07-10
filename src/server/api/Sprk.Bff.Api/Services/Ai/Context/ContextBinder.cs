using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// Default <see cref="IContextBinder"/> — the platform input-resolution seam (ADR-043 Move 1 / E-10;
/// re-scopes task 053). Resolves an Action's declared inputs into grounding CONTEXT
/// (<see cref="ContextEnvelope"/>) + the typed OPERAND, and writes the ContextEnvelope fingerprint via
/// <see cref="ChatSessionManager.AppendContextFingerprintAsync"/> (task-038's dark writer seam, now live).
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — nothing on the platform resolved
/// an Action's declared <c>selectionText</c>/<c>changesText</c>/<c>documentText</c>/<c>ledger_resolution</c>
/// inputs; the dispatch path read only session <c>fileIds</c> and the declared inputs were "accepted and
/// ignored". (2) <i>Extension</i> — the resolution is shared by the dispatch engine now and (E-12) the node
/// engine + narrator; embedding it in <see cref="LinearConsumers.ActionRunner"/> would fit it to one consumer
/// and force a reshape in E-12 (the exact recurrence Phase E cures). (3) <i>Cost-of-doing-nothing</i> — the
/// compose args-text dispatch (compose-B2) 422s / no-ops because its declared operand never reaches the
/// prompt, and task-038's <c>AppendContextFingerprintAsync</c> stays a dark seam with no production writer.
/// </para>
/// <para>
/// <b>ADR-013 placement</b>: in-zone AI code (<c>Services/Ai/Context/</c>); it consumes
/// <see cref="ChatSessionManager"/> (in-zone) and <see cref="ContextEnvelope"/> (PublicContracts). External
/// CRUD code binds to the PublicContracts facade, not to this type. <b>ADR-010</b>: an interface is warranted —
/// three completion consumers converge on it and it is a genuine module boundary (mockable per ADR-038).
/// </para>
/// </remarks>
public sealed class ContextBinder : IContextBinder
{
    private readonly ChatSessionManager _sessionManager;
    private readonly IOrganizationalContextProvider _organizationalContextProvider;
    private readonly ICallerContactResolver _callerContactResolver;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<ContextBinder> _logger;

    /// <summary>
    /// The closed structured-operand vocabulary (ADR-039). Checked in this deterministic order so the
    /// resolved operand is a pure function of the declared schema + args (no runtime model judgement).
    /// </summary>
    private static readonly (string Field, OperandKind Kind)[] OperandVocabulary =
    {
        ("selectionText", OperandKind.SelectionText),
        ("changesText", OperandKind.ChangesText),
        ("documentText", OperandKind.DocumentText),
    };

    /// <summary>The Args member carrying an ADR-040 read-by-reference: <c>{ "ledger_resolution": { "key": "{bindingId}@t{n}" } }</c>.</summary>
    private const string LedgerResolutionMember = "ledger_resolution";
    private const string LedgerResolutionKeyMember = "key";

    /// <param name="sessionManager">Session ledger + fingerprint writer (ADR-040 store-before-render).</param>
    /// <param name="logger">Diagnostic logger.</param>
    /// <param name="organizationalContextProvider">
    /// Read-only inbound Organizational-scope provider seam (task 060, FR-B-11). Optional — when the DI
    /// container has no registration (or a caller constructs <see cref="ContextBinder"/> directly, e.g.
    /// existing tests), the Binder falls back to <see cref="NullOrganizationalContextProvider"/> so the
    /// Organizational slice is always an ADR-032 empty Null-Object default rather than a null-reference
    /// failure.
    /// </param>
    /// <param name="callerContactResolver">
    /// Deterministic claims→Dataverse-contact resolver (task 055, FR-B-06). Optional — when the DI
    /// container has no registration (or a caller constructs <see cref="ContextBinder"/> directly, e.g.
    /// existing tests), the Binder falls back to <see cref="NullCallerContactResolver"/> (ADR-032 empty
    /// default) so the User slice's <c>CallerContactId</c> stays honestly null rather than throwing.
    /// </param>
    /// <param name="httpContextAccessor">
    /// Ambient caller-principal source for the live HTTP dispatch path (task 055, FR-B-06). Optional —
    /// null in background/pre-session binds and in most tests. <see cref="ContextBindingRequest.Caller"/>,
    /// when supplied, always takes precedence over the ambient <c>HttpContext.User</c>.
    /// </param>
    public ContextBinder(
        ChatSessionManager sessionManager,
        ILogger<ContextBinder> logger,
        IOrganizationalContextProvider? organizationalContextProvider = null,
        ICallerContactResolver? callerContactResolver = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _organizationalContextProvider = organizationalContextProvider ?? new NullOrganizationalContextProvider();
        _callerContactResolver = callerContactResolver ?? NullCallerContactResolver.Instance;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public bool HasStructuredOperand(string? inputSchemaJson, JsonElement? args)
    {
        if (args is not { ValueKind: JsonValueKind.Object } obj)
        {
            return false;
        }

        if (TryReadLedgerResolutionKey(obj, out _))
        {
            return true;
        }

        var declared = GetDeclaredProperties(inputSchemaJson);
        return TryFindDeclaredOperandField(obj, declared, out _, out _, out _);
    }

    /// <inheritdoc />
    public async Task<BoundInputs> BindAsync(ContextBindingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var operand = ResolveOperand(request);
        var callerContactId = await ResolveCallerContactIdAsync(request, ct).ConfigureAwait(false);
        var envelope = await AssembleEnvelopeAsync(request, callerContactId, ct).ConfigureAwait(false);
        var (fingerprint, updatedSession) = await WriteFingerprintAsync(request, envelope, ct).ConfigureAwait(false);

        return new BoundInputs
        {
            Context = envelope,
            Operand = operand,
            Fingerprint = fingerprint,
            UpdatedSession = updatedSession,
        };
    }

    // ── Caller-contact resolution (task 055, FR-B-06): deterministic claims→Dataverse-contact,
    //    server-side only. No branch here reads request.Args / an LLM completion for "me" — the ONLY
    //    inputs are an explicit pre-resolved id or the caller's own ClaimsPrincipal. ──────────────────

    private async Task<string?> ResolveCallerContactIdAsync(ContextBindingRequest request, CancellationToken ct)
    {
        // An explicit pre-resolved id (e.g. a caller that already ran its own claims resolution) wins.
        // This is still server-side-only — no dispatch path populates ContextBindingRequest.CallerContactId
        // from client Args or a model completion (grep-verified: the only writers are this Binder and the
        // request's own construction site).
        if (!string.IsNullOrWhiteSpace(request.CallerContactId))
        {
            return request.CallerContactId;
        }

        var principal = request.Caller ?? _httpContextAccessor?.HttpContext?.User;
        var resolution = await _callerContactResolver.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!resolution.IsResolved)
        {
            _logger.LogDebug(
                "ContextBinder: caller-contact resolution unresolved (reason={Reason}) — User slice " +
                "CallerContactId stays null (FR-B-06 fail-honestly; never a guessed/nearest contact).",
                resolution.UnresolvedReason);
            return null;
        }

        return resolution.ContactId;
    }

    // ── Operand resolution (deterministic over declared schema — ADR-039 / ADR-040) ──────────────

    private ResolvedOperand ResolveOperand(ContextBindingRequest request)
    {
        // (1) Pre-resolved structured operand (node engine inputBinding / narrator payload — the E-12 fit).
        if (PromptInputSection.HasInput(request.PreResolvedInput))
        {
            return new ResolvedOperand
            {
                Channel = OperandChannel.Input,
                Kind = OperandKind.PreResolved,
                Input = request.PreResolvedInput!.Value.Clone(),
            };
        }

        // (2) + (3) Structured operand from the dispatch args (ledger reference, then declared field).
        if (request.Args is { ValueKind: JsonValueKind.Object } args)
        {
            // (2) ADR-040 read-by-reference — the operand IS a prior SessionOutput's payload, by key.
            if (TryReadLedgerResolutionKey(args, out var ledgerKey))
            {
                var payload = ResolveLedgerReference(ledgerKey, request.LedgerOutputs);
                return new ResolvedOperand
                {
                    Channel = OperandChannel.Input,
                    Kind = OperandKind.LedgerResolution,
                    Input = payload,
                };
            }

            // (3) A declared operand field value (selectionText / changesText / documentText) → { field: value }.
            var declared = GetDeclaredProperties(request.InputSchemaJson);
            if (TryFindDeclaredOperandField(args, declared, out var field, out var kind, out var value))
            {
                var operandObject = JsonSerializer.SerializeToElement(
                    new Dictionary<string, JsonElement> { [field] = value });
                return new ResolvedOperand
                {
                    Channel = OperandChannel.Input,
                    Kind = kind,
                    Input = operandObject,
                };
            }
        }

        // (4) File-sourced grounding document → the shipped `## Document` channel (unregressed).
        if (request.FileDocument is not null)
        {
            return new ResolvedOperand
            {
                Channel = OperandChannel.Document,
                Kind = OperandKind.FileDocument,
                Document = request.FileDocument,
            };
        }

        // (5) No operand — the relaxed no-file / prompt-only run.
        return ResolvedOperand.None;
    }

    /// <summary>
    /// Resolves a <c>ledger_resolution</c> key to a prior <see cref="SessionOutput"/>'s payload (ADR-040
    /// read-by-reference). Loud on a dangling key or a truncation marker — never a silent fallback.
    /// </summary>
    private static JsonElement ResolveLedgerReference(string key, IReadOnlyList<SessionOutput>? ledgerOutputs)
    {
        var entry = ledgerOutputs?.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new InvalidOperationException(
                $"ContextBinder: ledger_resolution references '{key}', but no SessionOutput with that key " +
                "exists in the session ledger (ADR-040). The reference is dangling — no silent fallback.");
        }

        if (SessionLedger.IsTruncationMarker(entry.Payload))
        {
            throw new InvalidOperationException(
                $"ContextBinder: ledger_resolution references '{key}', but that SessionOutput's inline payload " +
                "was truncated (ADR-040 size cap) — feeding the lossy marker to a capability is forbidden. " +
                "The blob/SPE-pointer offload is the designed upgrade path.");
        }

        return entry.Payload.Clone();
    }

    private static bool TryReadLedgerResolutionKey(JsonElement args, out string key)
    {
        key = string.Empty;
        if (!args.TryGetProperty(LedgerResolutionMember, out var refEl) ||
            refEl.ValueKind != JsonValueKind.Object ||
            !refEl.TryGetProperty(LedgerResolutionKeyMember, out var keyEl) ||
            keyEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = keyEl.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        key = value;
        return true;
    }

    /// <summary>
    /// Finds the first closed-vocabulary operand field that is (a) declared by the schema when a schema is
    /// present, and (b) supplied with a non-empty value in the args. Deterministic vocabulary order.
    /// </summary>
    private static bool TryFindDeclaredOperandField(
        JsonElement args,
        IReadOnlySet<string>? declaredProperties,
        out string field,
        out OperandKind kind,
        out JsonElement value)
    {
        foreach (var (candidate, candidateKind) in OperandVocabulary)
        {
            // Respect the declared schema when present: an undeclared arg stays "accepted and ignored"
            // (the pre-E-10 contract). When no schema is declared, the closed vocabulary is the contract.
            if (declaredProperties is not null && !declaredProperties.Contains(candidate))
            {
                continue;
            }

            if (args.TryGetProperty(candidate, out var candidateValue) && IsNonEmptyOperandValue(candidateValue))
            {
                field = candidate;
                kind = candidateKind;
                value = candidateValue.Clone();
                return true;
            }
        }

        field = string.Empty;
        kind = OperandKind.None;
        value = default;
        return false;
    }

    private static bool IsNonEmptyOperandValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.Object or JsonValueKind.Array => true,
        _ => true,
    };

    /// <summary>
    /// The declared property names from a raw <c>sprk_inputschema</c> JSON (OpenAI function-parameters
    /// subset: <c>{ "properties": { … } }</c>). Null when no schema / malformed (tolerant reader — routing
    /// never throws on maker JSON); a null result means "no declaration to constrain against".
    /// </summary>
    private static IReadOnlySet<string>? GetDeclaredProperties(string? inputSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(inputSchemaJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(inputSchemaJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("properties", out var props) ||
                props.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in props.EnumerateObject())
            {
                names.Add(prop.Name);
            }
            return names;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Context envelope assembly (frozen task-015 contract for User/Workspace/Business/Memory/Semantic;
    //    the Organizational slice is read through IOrganizationalContextProvider — task 060, FR-B-11) ──

    private async Task<ContextEnvelope> AssembleEnvelopeAsync(
        ContextBindingRequest request, string? callerContactId, CancellationToken ct)
    {
        var envelope = ContextEnvelopeReferenceProducer.Assemble(
            userFragment: request.UserFragment,
            workspaceFragment: request.WorkspaceFragment,
            businessFragment: request.BusinessFragment,
            conversation: request.ConversationTail,
            memoryItems: request.MemoryItems,
            callerContactId: callerContactId);

        // Organizational slice (task 060, FR-B-11): read through the inbound provider seam rather than
        // the hardcoded present-but-empty shape the reference producer emits. With no real provider
        // registered, IOrganizationalContextProvider resolves to the ADR-032 Null-Object default and this
        // still yields an empty slice — never a failure.
        var orgResult = await _organizationalContextProvider
            .GetContextAsync(
                new OrganizationalContextRequest { CallerContactId = callerContactId },
                ct)
            .ConfigureAwait(false);

        return envelope with
        {
            Organizational = new OrganizationalSlice
            {
                Meta = new SliceMeta
                {
                    Stability = SliceStability.SemiStable,
                    BudgetTokens = null,
                    EstimatedTokens = 0,
                    ReferenceCount = orgResult.ReferenceCount,
                    Present = orgResult.ReferenceCount > 0,
                },
                ProviderImplemented = orgResult.ProviderImplemented,
            },
        };
    }

    // ── Fingerprint writer (task-038 dark seam → live; ADR-040 store-before-render; NFR-07) ──────

    private async Task<(SessionContextFingerprint? Entry, ChatSession? UpdatedSession)> WriteFingerprintAsync(
        ContextBindingRequest request,
        ContextEnvelope envelope,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.SessionId))
        {
            // No session to write against (e.g. a background/pre-session bind) — return the envelope only.
            return (null, null);
        }

        var sliceCount = ContextEnvelopeReferenceConsumer.PresentSlicesInOrder(envelope).Count;
        var entry = new SessionContextFingerprint
        {
            Turn = request.Turn,
            FingerprintId = ComputeFingerprintId(envelope),
            SliceCount = sliceCount,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // AppendContextFingerprintAsync re-fetches + appends + persists, returning the fingerprint-inclusive
        // session. The caller routes its output write on top of this instance so the output write does not
        // clobber the fingerprint (both are read-modify-write on the session — ADR-040 append-only).
        var updatedSession = await _sessionManager
            .AppendContextFingerprintAsync(request.TenantId!, request.SessionId!, entry, ct)
            .ConfigureAwait(false);

        return (entry, updatedSession);
    }

    /// <summary>
    /// A deterministic, content-free fingerprint id (NFR-07): a short hash of the envelope's
    /// identifiers-and-counts presence summary — never slice content.
    /// </summary>
    private static string ComputeFingerprintId(ContextEnvelope envelope)
    {
        var summary = ContextEnvelopeReferenceConsumer.RenderPresenceSummary(envelope);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(summary));
        return "ctx-" + Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }
}
