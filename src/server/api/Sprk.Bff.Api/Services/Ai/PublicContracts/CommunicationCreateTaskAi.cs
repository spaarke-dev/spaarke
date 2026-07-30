using System.Globalization;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="ICommunicationCreateTaskAi"/>: resolves + runs the
/// catalog-authored <c>CREATE-TASK-FROM-EMAIL</c> Action (<c>sprk_actioncode =
/// "create-task-from-email"</c>) via the Linear AI Consumer primitives (<see cref="IActionResolver"/> /
/// <see cref="IActionRunner"/>) — the SAME mechanism <c>CommunicationTriageAi</c>/<c>CommunicationProposeAi</c>
/// use for their own catalog Actions.
/// </summary>
/// <remarks>
/// <para>
/// Per ADR-007/ADR-013 facade pattern: narrow surface, single concrete class, no behavior beyond resolving
/// the Action, composing the operand, delegating to <see cref="IActionRunner"/>, and parsing the structured
/// result into raw <see cref="TaskCandidate"/>s. The verify-cited-text check, the deadline-bearing → confirm
/// gate, and the actual create-or-store decision are the caller's job (task 040 enrichment step) — this
/// facade never touches Dataverse and never decides what is created or stored.
/// </para>
/// <para>
/// <b>No second classification pass (FR-05/FR-14) — structural.</b> The constructor has NO dependency on
/// <see cref="ICommunicationClassificationAi"/>/<c>IOpenAiClient</c>; it CANNOT invoke a classification even
/// by mistake. The already-produced triage output is supplied on
/// <see cref="CommunicationCreateTaskRequest.Triage"/> as grounding only.
/// </para>
/// <para>
/// <b>Best-effort (NFR-04).</b> Every failure mode (Action not routed, completion failure, malformed
/// structured output) is caught and logged; the method returns <c>null</c> rather than throwing.
/// </para>
/// </remarks>
public sealed class CommunicationCreateTaskAi : ICommunicationCreateTaskAi
{
    private readonly IActionResolver _actionResolver;
    private readonly IActionRunner _actionRunner;
    private readonly ILogger<CommunicationCreateTaskAi> _logger;

    public CommunicationCreateTaskAi(
        IActionResolver actionResolver,
        IActionRunner actionRunner,
        ILogger<CommunicationCreateTaskAi> logger)
    {
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaskCandidate>?> ExtractAsync(
        CommunicationCreateTaskRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AnalysisAction action;
        try
        {
            action = await _actionResolver.ResolveAsync(ConsumerTypes.EmailCreateTask, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // NFR-04: no Binding/Action routed yet (or a transient Dataverse read failure) ⇒ no candidate
            // tasks, never a throw into the enrichment path.
            _logger.LogWarning(ex, "Email create-task skipped: CREATE-TASK-FROM-EMAIL Action could not be resolved for consumerType '{ConsumerType}'.", ConsumerTypes.EmailCreateTask);
            return null;
        }

        var input = BuildInput(request);
        var boundInputs = new BoundInputs
        {
            Context = ContextEnvelopeReferenceProducer.Assemble(),
            Operand = new ResolvedOperand
            {
                Channel = OperandChannel.Input,
                Kind = OperandKind.PreResolved,
                Input = input,
            },
        };

        var runContext = new LinearRunContext
        {
            ConsumerType = ConsumerTypes.EmailCreateTask,
            TenantId = request.TenantId,
        };

        JsonElement output;
        try
        {
            output = await _actionRunner.RunAsync(action, boundInputs, runContext, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email create-task skipped: CREATE-TASK-FROM-EMAIL completion failed.");
            return null;
        }

        return ParseResult(output);
    }

    /// <summary>
    /// Builds the Action's declared <c>{record, triage, message}</c> input shape (mirrors
    /// <c>infra/dataverse/actions/create-task-from-email.action.json</c>'s <c>input</c> section 1:1) as the
    /// structured operand rendered under the prompt's <c>## Input</c> section.
    /// </summary>
    private static JsonElement BuildInput(CommunicationCreateTaskRequest request)
    {
        var payload = new
        {
            record = new
            {
                entityLogicalName = request.EntityLogicalName,
            },
            triage = request.Triage is null
                ? null
                : new
                {
                    category = request.Triage.Category,
                    summary = request.Triage.Summary,
                    obligations = request.Triage.Obligations,
                    priority = request.Triage.Priority,
                },
            message = new
            {
                subject = request.Subject,
                bodyText = request.BodyText,
                attachmentText = request.AttachmentText ?? string.Empty,
            },
        };

        using var doc = JsonSerializer.SerializeToDocument(payload);
        return doc.RootElement.Clone();
    }

    /// <summary>Parses the Action's <c>{ tasks: [...] }</c> structured output into raw candidates. Missing/
    /// malformed entries are skipped rather than throwing — a partially-usable candidate set is still useful
    /// (the caller applies the verify-cited-text + deadline-bearing gates regardless).</summary>
    private static IReadOnlyList<TaskCandidate> ParseResult(JsonElement output)
    {
        if (output.ValueKind != JsonValueKind.Object
            || !output.TryGetProperty("tasks", out var tasks)
            || tasks.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TaskCandidate>();
        }

        var list = new List<TaskCandidate>();
        foreach (var item in tasks.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var subject = GetString(item, "subject");
            if (string.IsNullOrWhiteSpace(subject))
            {
                // A candidate with no subject is unusable — skip it.
                continue;
            }

            var citation = ParseCitation(item);
            if (citation is null || string.IsNullOrWhiteSpace(citation.QuotedText))
            {
                // NFR-06: a candidate with no citation / no quoted text cannot be verified — skip it here;
                // the caller's verify-cited-text gate is the second line of defense.
                continue;
            }

            list.Add(new TaskCandidate(
                Subject: subject!,
                Description: GetString(item, "description") ?? string.Empty,
                DueDate: ParseDueDate(GetString(item, "dueDate")),
                Citation: citation,
                Reason: GetString(item, "reason") ?? string.Empty,
                Confidence: GetDouble(item, "confidence")));
        }

        return list;
    }

    /// <summary>Parses the Action's raw <c>dueDate</c> string (ISO-8601 <c>yyyy-MM-dd</c>, or a full
    /// timestamp) to a <see cref="DateTime"/>; null/blank/unparseable ⇒ <c>null</c> — a candidate with no
    /// concretely-stated deadline is NEVER treated as deadline-bearing (never guessed).</summary>
    private static DateTime? ParseDueDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static ProposalCitation? ParseCitation(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("citation", out var citation) || citation.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var quotedText = GetString(citation, "quotedText");
        if (quotedText is null)
        {
            return null;
        }

        return new ProposalCitation(
            Source: GetString(citation, "source"),
            Locator: GetString(citation, "locator"),
            QuotedText: quotedText);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double GetDouble(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var d)
            ? d
            : 0.0;
}
