using System.Text.Json;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="ICommunicationProposeAi"/>: resolves + runs the catalog-authored
/// <c>PROPOSE-FIELD-UPDATES</c> Action (<c>sprk_actioncode = "propose-field-updates"</c>) via the Linear AI
/// Consumer primitives (<see cref="IActionResolver"/> / <see cref="IActionRunner"/>) — the SAME mechanism
/// <c>MatterPreFillService</c>/<see cref="CommunicationTriageAi"/> use for their own catalog Actions.
/// </summary>
/// <remarks>
/// <para>
/// Per ADR-007/ADR-013 facade pattern: narrow surface, single concrete class, no behavior beyond resolving
/// the Action, composing the operand, delegating to <see cref="IActionRunner"/>, and parsing the structured
/// result into raw <see cref="ProposedFieldCandidate"/>s. The allow-list gate, coercion, old-value read, and
/// verify-cited-text DROP are the caller's job (task 030 enrichment step) — this facade never touches the
/// target record and never decides what is stored.
/// </para>
/// <para>
/// <b>No second classification pass (FR-05/FR-09) — structural.</b> The constructor has NO dependency on
/// <see cref="ICommunicationClassificationAi"/>/<c>IOpenAiClient</c>; it CANNOT invoke a classification even
/// by mistake. The already-produced triage output is supplied on <see cref="CommunicationProposeRequest.Triage"/>
/// as grounding only.
/// </para>
/// <para>
/// <b>Best-effort (NFR-04).</b> Every failure mode (Action not routed, completion failure, malformed
/// structured output) is caught and logged; the method returns <c>null</c> rather than throwing.
/// </para>
/// </remarks>
public sealed class CommunicationProposeAi : ICommunicationProposeAi
{
    private readonly IActionResolver _actionResolver;
    private readonly IActionRunner _actionRunner;
    private readonly ILogger<CommunicationProposeAi> _logger;

    public CommunicationProposeAi(
        IActionResolver actionResolver,
        IActionRunner actionRunner,
        ILogger<CommunicationProposeAi> logger)
    {
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProposedFieldCandidate>?> ProposeAsync(
        CommunicationProposeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Fields is not { Count: > 0 })
        {
            // No enabled allow-list fields ⇒ nothing to propose. Skip the LLM call entirely.
            return Array.Empty<ProposedFieldCandidate>();
        }

        AnalysisAction action;
        try
        {
            action = await _actionResolver.ResolveAsync(ConsumerTypes.EmailPropose, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // NFR-04: no Binding/Action routed yet (or a transient Dataverse read failure) ⇒ no proposals,
            // never a throw into the enrichment path.
            _logger.LogWarning(ex, "Email propose skipped: PROPOSE-FIELD-UPDATES Action could not be resolved for consumerType '{ConsumerType}'.", ConsumerTypes.EmailPropose);
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
            ConsumerType = ConsumerTypes.EmailPropose,
            TenantId = request.TenantId,
        };

        JsonElement output;
        try
        {
            output = await _actionRunner.RunAsync(action, boundInputs, runContext, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email propose skipped: PROPOSE-FIELD-UPDATES completion failed.");
            return null;
        }

        return ParseResult(output);
    }

    /// <summary>
    /// Builds the Action's declared <c>{record, fields, triage, message}</c> input shape (mirrors
    /// <c>infra/dataverse/actions/propose-field-updates.action.json</c>'s <c>input</c> section 1:1) as the
    /// structured operand rendered under the prompt's <c>## Input</c> section.
    /// </summary>
    private static JsonElement BuildInput(CommunicationProposeRequest request)
    {
        var payload = new
        {
            record = new
            {
                entityLogicalName = request.EntityLogicalName,
            },
            fields = request.Fields.Select(f => new
            {
                targetField = f.FieldLogicalName,
                fieldType = f.FieldType,
                extractionGuidance = f.ExtractionGuidance ?? string.Empty,
            }).ToArray(),
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

    /// <summary>Parses the Action's <c>{ proposals: [...] }</c> structured output into raw candidates.
    /// Missing/malformed entries are skipped rather than throwing — a partially-usable proposal set is still
    /// useful (the caller applies the allow-list + verify-cited-text gates regardless).</summary>
    private static IReadOnlyList<ProposedFieldCandidate> ParseResult(JsonElement output)
    {
        if (output.ValueKind != JsonValueKind.Object
            || !output.TryGetProperty("proposals", out var proposals)
            || proposals.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ProposedFieldCandidate>();
        }

        var list = new List<ProposedFieldCandidate>();
        foreach (var item in proposals.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var field = GetString(item, "field");
            var newValue = GetString(item, "newValue");
            if (string.IsNullOrWhiteSpace(field) || newValue is null)
            {
                // A proposal with no field or no proposed value is unusable — skip it.
                continue;
            }

            var citation = ParseCitation(item);
            if (citation is null || string.IsNullOrWhiteSpace(citation.QuotedText))
            {
                // NFR-06: a proposal with no citation / no quoted text cannot be verified — skip it here;
                // the caller's verify-cited-text gate is the second line of defense.
                continue;
            }

            list.Add(new ProposedFieldCandidate(
                Field: field!,
                NewValue: newValue,
                Citation: citation,
                Reason: GetString(item, "reason") ?? string.Empty,
                Confidence: GetDouble(item, "confidence")));
        }

        return list;
    }

    private static ProposalCitation? ParseCitation(JsonElement proposal)
    {
        if (!proposal.TryGetProperty("citation", out var citation) || citation.ValueKind != JsonValueKind.Object)
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
