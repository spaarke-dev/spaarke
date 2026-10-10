using System.Text;
using System.Text.Json.Nodes;
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
/// <para>
/// <b>Editable triage taxonomy (spaarke-ontology-platform-r1 D-117(b)).</b> The code-constant prompt carries no
/// firm-specific vocabulary. When <see cref="AiClassificationOptions.TriageTaxonomyChoicesRef"/> is set (default:
/// the <c>sprk_triagecategory</c> rows the TRIAGE-EMAIL Action also uses), the live category names and their
/// <c>sprk_classifierguidance</c> are read through the SAME <see cref="LookupChoicesResolver"/> path as the Action
/// (enabled rows, same guidance budget) and appended to the system prompt, and the schema gains a
/// <c>triageCategory</c> field whose enum is those names. Refinement is then a data edit, never a code change. It
/// is additive: <c>category</c> and every other field keep their vocabulary, and no extra model call is made
/// (FR-05). If the taxonomy cannot be read, or the option is empty, the pre-D-117 prompt and schema are sent
/// unchanged and <c>triageCategory</c> is null.
/// </para>
/// </remarks>
public sealed class CommunicationClassificationAi : ICommunicationClassificationAi
{
    private readonly IOpenAiClient _openAi;
    private readonly IOptions<DocumentIntelligenceOptions> _docIntel;
    private readonly LookupChoicesResolver _choicesResolver;
    private readonly IOptions<AiClassificationOptions> _classificationOptions;
    private readonly ILogger<CommunicationClassificationAi> _logger;

    public CommunicationClassificationAi(
        IOpenAiClient openAi,
        IOptions<DocumentIntelligenceOptions> docIntel,
        LookupChoicesResolver choicesResolver,
        IOptions<AiClassificationOptions> classificationOptions,
        ILogger<CommunicationClassificationAi> logger)
    {
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
        _docIntel = docIntel ?? throw new ArgumentNullException(nameof(docIntel));
        _choicesResolver = choicesResolver ?? throw new ArgumentNullException(nameof(choicesResolver));
        _classificationOptions = classificationOptions ?? throw new ArgumentNullException(nameof(classificationOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>JSON property the taxonomy choice is returned in (matches <see cref="CommunicationClassificationResult.TriageCategory"/>).</summary>
    private const string TriageCategoryProperty = "triageCategory";

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
    public async Task<CommunicationClassificationResult?> ClassifyAsync(
        string subject,
        string bodyText,
        CancellationToken ct = default)
    {
        var taxonomy = await ResolveTaxonomyAsync(ct);

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
            new SystemChatMessage(taxonomy is null ? SystemPrompt : ComposeSystemPrompt(taxonomy.PromptLines)),
            new UserChatMessage(userPrompt),
        };

        var deploymentName = _docIntel.Value.SummarizeModel;

        var result = await _openAi.GetStructuredCompletionAsync<CommunicationClassificationResult>(
            messages,
            taxonomy is null ? ClassificationSchema : BuildSchemaWithTaxonomy(taxonomy.Names),
            "communication_classification",
            deploymentName,
            ct);

        return taxonomy is null || result is null ? result : CanonicalizeTriageCategory(result, taxonomy.Names);
    }

    /// <summary>The live taxonomy: names (the schema enum) and the prompt-facing "name — guidance" lines.</summary>
    private sealed record TriageTaxonomy(string[] Names, string[] PromptLines);

    /// <summary>
    /// Reads the configured taxonomy through <see cref="LookupChoicesResolver"/> (the TRIAGE-EMAIL Action's path).
    /// Returns null, and the caller sends the pre-D-117 prompt, when the option is empty or nothing resolves. The
    /// resolver already logs and records telemetry for read failures; a guidance-read failure leaves bare names.
    /// </summary>
    private async Task<TriageTaxonomy?> ResolveTaxonomyAsync(CancellationToken ct)
    {
        var choicesRef = _classificationOptions.Value.TriageTaxonomyChoicesRef;
        if (string.IsNullOrWhiteSpace(choicesRef))
        {
            return null;
        }

        IReadOnlyDictionary<string, string[]> resolved;
        try
        {
            resolved = await _choicesResolver.ResolveChoicesReferenceAsync(choicesRef, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Communication classification: triage taxonomy {Ref} could not be read; classifying without it (non-fatal).",
                choicesRef);
            return null;
        }

        if (!resolved.TryGetValue(choicesRef, out var names) || names.Length == 0)
        {
            _logger.LogWarning(
                "Communication classification: triage taxonomy {Ref} resolved no rows; classifying without it (non-fatal).",
                choicesRef);
            return null;
        }

        var lines = resolved.TryGetValue(LookupChoicesResolver.GuidanceKey(choicesRef), out var guidanceLines)
                    && guidanceLines.Length == names.Length
            ? guidanceLines
            : names;

        // Two rows with the same name would put a duplicate value in the schema enum, which strict structured output
        // may reject (and then every email would lose rung 5). Keep the first row per name, names and lines aligned.
        var distinct = names
            .Select((name, i) => (Name: name, Line: lines[i]))
            .DistinctBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        return new TriageTaxonomy(distinct.Select(p => p.Name).ToArray(), distinct.Select(p => p.Line).ToArray());
    }

    /// <summary>
    /// The base prompt plus the taxonomy section. Only the section is data-driven: every word of it after the fixed
    /// lead-in comes from the taxonomy rows, so editing a row's name or guidance changes this prompt directly.
    /// </summary>
    private static string ComposeSystemPrompt(IReadOnlyList<string> taxonomyLines)
    {
        var sb = new StringBuilder(SystemPrompt.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Also produce:");
        sb.AppendLine(
            "- triageCategory: the ONE category from the firm's triage taxonomy below that best describes what this email "
            + "is about, judged from the subject and body. Emit only the name before the dash, exactly as written; the text "
            + "after it describes when to use it. Always choose the closest one. This is independent of the free-form "
            + "category above, which keeps its own short vocabulary.");
        sb.AppendLine();
        sb.AppendLine("Triage taxonomy:");
        foreach (var line in taxonomyLines)
        {
            sb.Append("- ").AppendLine(line);
        }

        return sb.ToString();
    }

    /// <summary>
    /// <see cref="ClassificationSchema"/> plus a required <c>triageCategory</c> string constrained to the live names
    /// (the same <c>enum</c> shape <c>ActionRunner</c> injects for TRIAGE-EMAIL's category).
    /// </summary>
    private static BinaryData BuildSchemaWithTaxonomy(IReadOnlyList<string> names)
    {
        var schema = JsonNode.Parse(ClassificationSchema.ToString())!.AsObject();

        var enumValues = new JsonArray();
        foreach (var name in names)
        {
            enumValues.Add(name);
        }

        schema["properties"]!.AsObject()[TriageCategoryProperty] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = enumValues,
        };
        schema["required"]!.AsArray().Add(TriageCategoryProperty);

        return BinaryData.FromString(schema.ToJsonString());
    }

    /// <summary>
    /// Strict structured output already enforces the enum; this keeps the field a canonical taxonomy name even if
    /// a deployment returns a case variant, and drops anything that is not one.
    /// </summary>
    private static CommunicationClassificationResult CanonicalizeTriageCategory(
        CommunicationClassificationResult result, IReadOnlyList<string> names)
    {
        if (result.TriageCategory is null)
        {
            return result;
        }

        var trimmed = result.TriageCategory.Trim();
        var canonical = names.FirstOrDefault(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
        return string.Equals(canonical, result.TriageCategory, StringComparison.Ordinal)
            ? result
            : result with { TriageCategory = canonical };
    }
}
