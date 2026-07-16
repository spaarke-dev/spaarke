using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Communication;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="ICommunicationClassificationAi"/>: a thin wrapper over
/// <see cref="IOpenAiClient"/>'s guaranteed-valid-JSON structured-completion primitive.
/// </summary>
/// <remarks>
/// <para>
/// Per ADR-007 facade pattern: narrow surface, single concrete class, no behavior beyond assembling the
/// prompt + schema and delegating. All resilience / retry / circuit-breaker concerns live inside
/// <see cref="IOpenAiClient"/>.
/// </para>
/// <para>
/// <b>Deployment.</b> Uses <see cref="DocumentIntelligenceOptions.SummarizeModel"/> (default
/// <c>gpt-4o-mini</c>) — the cheap, fast classification-tier deployment, matching the Finance classification
/// path (ADR-016 budget: classification runs on the small model).
/// </para>
/// <para>
/// <b>Structured-output, not a Dataverse JPS Action (Path-A deviation, see interface remarks).</b> The system
/// prompt is a self-contained code constant; the response is constrained by <see cref="ClassificationSchema"/>.
/// The prompt MAY later migrate to a Dataverse playbook <c>Description</c> per ADR-014 (follow-up).
/// </para>
/// </remarks>
public sealed class CommunicationClassificationAi : ICommunicationClassificationAi
{
    private readonly IOpenAiClient _openAi;
    private readonly IOptions<DocumentIntelligenceOptions> _docIntel;

    public CommunicationClassificationAi(
        IOpenAiClient openAi,
        IOptions<DocumentIntelligenceOptions> docIntel)
    {
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
        _docIntel = docIntel ?? throw new ArgumentNullException(nameof(docIntel));
    }

    /// <summary>
    /// System prompt: instructs extract+classify for legal-ops email association/triage. Identifies candidate
    /// record TYPES (never IDs) from the allowed set, category, urgency, obligations, suggested actions, and a
    /// rationale; flags — never decides — privilege (ADR-015).
    /// </summary>
    private const string SystemPrompt =
        """
        You are a legal-operations email triage assistant for a matter-centric practice. You extract and
        classify an inbound or outbound business email to help associate it with the right records and to
        drive downstream triage. You do NOT make filing, retention, or privilege DECISIONS — you produce
        SIGNALS a human reviewer will act on.

        Given the email subject and body, produce:
        - candidateRecordTypes: which KINDS of records this email likely relates to. Choose zero or more from
          the allowed set ONLY: sprk_matter (a legal matter), sprk_project, sprk_invoice (billing/payment),
          sprk_organization (an outside company/counsel/vendor as a legal entity), sprk_event (a calendared
          event/deadline/hearing). Return record TYPES only — you cannot know specific record IDs.
        - category: a short content category (e.g. court-notice, invoice, esign-completion, scheduling,
          general-correspondence). Null if genuinely undetermined.
        - urgency: one of routine, elevated, or urgent, based on explicit deadlines or escalation language.
        - obligations: concrete obligations the email implies (e.g. deadline-response, executed-document,
          payment-due, produce-documents). Empty if none.
        - suggestedActions: next actions for the reviewer (e.g. calendar-deadline, route-to-billing,
          link-to-matter, escalate). Empty if none.
        - privilegeFlagged: true ONLY when the content shows attorney-client / work-product privilege markers.
          This is a FLAG for the reviewer, never a decision.
        - rationale: one or two sentences explaining the classification, suitable for an audit trail.

        Be conservative: prefer fewer, well-justified signals over speculation. If the email carries no useful
        classification signal, return empty arrays, null category/urgency/rationale, and privilegeFlagged=false.
        """;

    /// <summary>
    /// Strict JSON schema for structured output. <c>candidateRecordTypes</c> is <c>$choices</c>-constrained to
    /// the ADR-024 regarding family the classifier can reason about (matter / project / invoice / organization
    /// / event). All properties required + <c>additionalProperties:false</c> per the Azure OpenAI
    /// structured-output contract (matches <c>FinanceJsonSchemas</c>).
    /// </summary>
    private static readonly BinaryData ClassificationSchema = BinaryData.FromString(
        """
        {
          "type": "object",
          "properties": {
            "candidateRecordTypes": {
              "type": "array",
              "items": {
                "type": "string",
                "enum": ["sprk_matter", "sprk_project", "sprk_invoice", "sprk_organization", "sprk_event"]
              }
            },
            "category": { "type": ["string", "null"] },
            "urgency": { "type": ["string", "null"] },
            "obligations": {
              "type": "array",
              "items": { "type": "string" }
            },
            "suggestedActions": {
              "type": "array",
              "items": { "type": "string" }
            },
            "privilegeFlagged": { "type": "boolean" },
            "rationale": { "type": ["string", "null"] }
          },
          "required": ["candidateRecordTypes", "category", "urgency", "obligations", "suggestedActions", "privilegeFlagged", "rationale"],
          "additionalProperties": false
        }
        """);

    /// <inheritdoc />
    public Task<CommunicationClassificationResult?> ClassifyAsync(
        string subject,
        string bodyText,
        CancellationToken ct = default)
    {
        var userPrompt =
            $"""
            Classify the following email for legal-operations association and triage.

            <subject>
            {subject}
            </subject>
            <body>
            {bodyText}
            </body>
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(userPrompt),
        };

        var deploymentName = _docIntel.Value.SummarizeModel;

        return ClassifyCoreAsync(messages, deploymentName, ct);
    }

    private async Task<CommunicationClassificationResult?> ClassifyCoreAsync(
        IReadOnlyList<ChatMessage> messages,
        string deploymentName,
        CancellationToken ct)
    {
        return await _openAi.GetStructuredCompletionAsync<CommunicationClassificationResult>(
            messages,
            ClassificationSchema,
            "communication_classification",
            deploymentName,
            ct);
    }
}
