using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Insights.LiveFacts;

/// <summary>
/// Production <see cref="ILiveFactResolver"/> for the <c>matter:</c> subject scheme. Reads
/// <c>sprk_matter</c> via <see cref="IGenericEntityService"/> and returns a deterministic
/// <see cref="FactArtifact"/> for each predicate the D-P14 <c>predict-matter-cost</c>
/// synthesis playbook needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>r2 Wave D5 (task 034)</b> — RENAMED from <c>DataverseLiveFactResolver</c> as part of the
/// multi-entity subject design (Wave A6 / design-a6-multi-entity.md §6.1). The matter resolver
/// is now one of three per-entity resolvers (matter, project, invoice) keyed by entity-type
/// name in <c>IReadOnlyDictionary&lt;string, ILiveFactResolver&gt;</c> per A6-D1. Behavior
/// is preserved 1:1 from r1 (D-P14 predict-matter-cost playbook continues to work without
/// change).
/// </para>
/// <para>
/// <b>Zone B placement</b> per SPEC §3.5 — lives under <c>Services/Insights/LiveFacts/</c>
/// and consumes <see cref="IGenericEntityService"/> only. ZERO AI-internal imports (no
/// <c>IOpenAiClient</c>, no <c>IPlaybookService</c>, no <c>PlaybookExecutionEngine</c>).
/// Verified by the §3.5.4 forbidden-imports grep in <c>.github/workflows/insights-eval.yml</c>.
/// </para>
/// <para>
/// <b>Supported predicates (Phase 1, preserved into Phase 1.5)</b> — see
/// <c>projects/ai-spaarke-insights-engine-r1/notes/sprk-matter-livefact-predicates.md</c>
/// for the schema mapping rationale:
/// <list type="bullet">
///   <item><c>attorney</c> → <c>sprk_assignedattorney1</c> (LOOKUP → contact)</item>
///   <item><c>client</c> → <c>sprk_externalaccount</c> (LOOKUP → account)</item>
///   <item><c>matterType</c> → <c>sprk_mattertype</c> (LOOKUP → sprk_mattertype_ref)</item>
///   <item><c>opposingCounsel</c> → <c>sprk_assignedlawfirm2</c> (LOOKUP → sprk_organization)</item>
///   <item><c>currentMatterFacts</c> → composite shape returning all 4 sub-values in
///   <see cref="Value.Raw"/> as a JSON object (matches the existing
///   <c>predict-matter-cost.playbook.json</c> LiveFactNode config exactly)</item>
/// </list>
/// Any other predicate throws <see cref="LiveFactNotSupportedException"/>; the
/// <see cref="Sprk.Bff.Api.Services.Ai.Nodes.LiveFactNode"/> consumer catches this and
/// surfaces a node-level <c>InvalidConfiguration</c> error so playbook authors see
/// misconfigured predicates immediately (graceful degradation).
/// </para>
/// <para>
/// <b>Subject format</b>: <c>matter:{guid}</c>. The <c>matter:</c> prefix is mandatory;
/// the suffix must parse as a <see cref="Guid"/>. Invalid formats throw
/// <see cref="LiveFactNotSupportedException"/> so playbook errors surface as
/// node-level <c>InvalidConfiguration</c> rather than runtime exceptions. r2 Wave D5
/// preserves the per-resolver subject parsing for backward compatibility; the dispatch
/// layer (<see cref="Sprk.Bff.Api.Services.Ai.Nodes.LiveFactNode"/>) also validates the
/// scheme via <see cref="ISubjectParser"/> before routing here.
/// </para>
/// <para>
/// <b>Confidence</b>: every returned <see cref="FactArtifact"/> carries
/// <see cref="FactArtifact.Confidence"/> = 1.0 (deterministic system-of-record per
/// <c>design.md §2.1</c>). Facts are stated directly without hedging.
/// </para>
/// <para>
/// <b>Lifetime</b>: registered as <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped"/>
/// in <c>InsightsModule</c> to match <see cref="IGenericEntityService"/>'s typical scoped
/// lifetime in this codebase (consistent with <see cref="Precedents.DataversePrecedentBoard"/>).
/// </para>
/// </remarks>
public sealed class MatterLiveFactResolver : ILiveFactResolver
{
    /// <summary>Logical name of the Dataverse entity this resolver reads.</summary>
    internal const string MatterEntityName = "sprk_matter";

    /// <summary>The subject scheme prefix this resolver supports.</summary>
    internal const string MatterSubjectScheme = "matter:";

    /// <summary>ProducedBy.Id for all FactArtifacts emitted by this resolver.</summary>
    internal const string ProducerId = "dataverse://sprk_matter";

    /// <summary>ProducedBy.Version for all FactArtifacts (deterministic queries are versioned).</summary>
    internal const string ProducerVersion = "v1";

    /// <summary>
    /// Fields the resolver pulls from sprk_matter to satisfy the 5 Phase 1 predicates.
    /// Kept minimal — broader reads happen via dedicated query services, not this resolver.
    /// </summary>
    private static readonly string[] ReadColumns =
    [
        "sprk_matterid",
        "sprk_matternumber",
        "sprk_assignedattorney1",
        "sprk_externalaccount",
        "sprk_mattertype",
        "sprk_assignedlawfirm2"
    ];

    // Predicate names — these MUST match what the predict-matter-cost playbook references
    // in LiveFactNode ConfigJson. Changing them is a breaking change to the playbook contract.
    private const string PredicateAttorney = "attorney";
    private const string PredicateClient = "client";
    private const string PredicateMatterType = "matterType";
    private const string PredicateOpposingCounsel = "opposingCounsel";
    private const string PredicateCurrentMatterFacts = "currentMatterFacts";

    private readonly IGenericEntityService _entityService;
    private readonly ILogger<MatterLiveFactResolver> _logger;

    public MatterLiveFactResolver(
        IGenericEntityService entityService,
        ILogger<MatterLiveFactResolver> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<FactArtifact?> ResolveAsync(
        string subject,
        string predicate,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // Parse "matter:{guid}" — only matter: scheme supported by this resolver.
        // Invalid format surfaces as LiveFactNotSupportedException so the LiveFactNode
        // emits a node-level InvalidConfiguration error (graceful authoring feedback).
        var matterId = ParseMatterSubject(subject);
        if (matterId is null)
        {
            throw new LiveFactNotSupportedException(subject, predicate);
        }

        // Read the matter row. The Spaarke.Dataverse IGenericEntityService returns
        // null/throws when the row doesn't exist; we map "not found" to null per
        // the ILiveFactResolver contract so LiveFactNode emits NodeErrorCodes.InternalError
        // with "Subject not found in Dataverse".
        Entity? matter;
        try
        {
            matter = await _entityService.RetrieveAsync(
                MatterEntityName,
                matterId.Value,
                ReadColumns,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            && (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug(
                "MatterLiveFactResolver: matter {MatterId} not found in Dataverse; returning null",
                matterId);
            return null;
        }

        if (matter is null)
        {
            return null;
        }

        // Dispatch on predicate name. Unsupported predicates throw LiveFactNotSupportedException
        // (NOT null) so playbook authoring errors surface immediately rather than silently
        // producing a "subject not found" error downstream.
        return predicate switch
        {
            PredicateAttorney => BuildLookupFact(matter, subject, predicate, "sprk_assignedattorney1", tenantId),
            PredicateClient => BuildLookupFact(matter, subject, predicate, "sprk_externalaccount", tenantId),
            PredicateMatterType => BuildLookupFact(matter, subject, predicate, "sprk_mattertype", tenantId),
            PredicateOpposingCounsel => BuildLookupFact(matter, subject, predicate, "sprk_assignedlawfirm2", tenantId),
            PredicateCurrentMatterFacts => BuildCompositeFact(matter, subject, predicate, tenantId),
            _ => throw new LiveFactNotSupportedException(subject, predicate)
        };
    }

    /// <summary>
    /// Parses <c>matter:{guid}</c>. Returns null on any format violation; the caller
    /// throws <see cref="LiveFactNotSupportedException"/> so the node executor emits
    /// a clean InvalidConfiguration error.
    /// </summary>
    internal static Guid? ParseMatterSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        if (!subject.StartsWith(MatterSubjectScheme, StringComparison.OrdinalIgnoreCase)) return null;

        var suffix = subject.AsSpan(MatterSubjectScheme.Length).Trim();
        return Guid.TryParse(suffix, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Build a FactArtifact for a single lookup field. Returns null when the lookup is
    /// unset on the matter row (the field simply has no value); LiveFactNode surfaces this
    /// as "Subject 'X' not found" via the ILiveFactResolver contract for null returns
    /// (per its existing error handling).
    /// </summary>
    private FactArtifact? BuildLookupFact(
        Entity matter,
        string subject,
        string predicate,
        string fieldName,
        string tenantId)
    {
        var lookup = matter.GetAttributeValue<EntityReference>(fieldName);
        if (lookup is null || lookup.Id == Guid.Empty)
        {
            _logger.LogDebug(
                "MatterLiveFactResolver: matter {MatterId} has no value for field {Field}; returning null for predicate {Predicate}",
                matter.Id, fieldName, predicate);
            return null;
        }

        // For matterType (which targets sprk_mattertype_ref), the Phase 1 playbook
        // synthesis prompt wants the display name as a plain string. For other lookups
        // (attorney/client/opposingCounsel), the prompt wants a small object with
        // {id, name}. EntityReference.Name is populated when the source query includes
        // the field (Dataverse auto-resolves lookup display names on RetrieveAsync).
        JsonElement valueRaw;
        string displayHint;

        if (predicate == PredicateMatterType)
        {
            // Plain string per playbook expectation. Fall back to Guid string if Name is unset
            // (some dev environments may not have the reference table populated yet).
            var typeName = lookup.Name ?? lookup.Id.ToString();
            valueRaw = JsonSerializer.SerializeToElement(typeName);
            displayHint = "text";
        }
        else
        {
            valueRaw = JsonSerializer.SerializeToElement(new
            {
                id = lookup.Id.ToString(),
                name = lookup.Name ?? string.Empty
            });
            displayHint = "entity-reference";
        }

        return BuildFact(subject, predicate, valueRaw, displayHint, tenantId, matter.Id);
    }

    /// <summary>
    /// Build the composite <c>currentMatterFacts</c> Fact — a single FactArtifact whose
    /// <see cref="Value.Raw"/> is a JSON object containing all 4 sub-predicates. This
    /// shape matches the existing predict-matter-cost playbook's LiveFactNode ConfigJson
    /// (predicate = "currentMatterFacts") so the playbook continues to work unchanged.
    /// </summary>
    private FactArtifact BuildCompositeFact(
        Entity matter,
        string subject,
        string predicate,
        string tenantId)
    {
        var attorney = matter.GetAttributeValue<EntityReference>("sprk_assignedattorney1");
        var client = matter.GetAttributeValue<EntityReference>("sprk_externalaccount");
        var matterType = matter.GetAttributeValue<EntityReference>("sprk_mattertype");
        var opposingCounsel = matter.GetAttributeValue<EntityReference>("sprk_assignedlawfirm2");

        var composite = new
        {
            attorney = attorney is null || attorney.Id == Guid.Empty
                ? null
                : (object)new { id = attorney.Id.ToString(), name = attorney.Name ?? string.Empty },
            client = client is null || client.Id == Guid.Empty
                ? null
                : (object)new { id = client.Id.ToString(), name = client.Name ?? string.Empty },
            matterType = matterType is null || matterType.Id == Guid.Empty
                ? null
                : (object)(matterType.Name ?? matterType.Id.ToString()),
            opposingCounsel = opposingCounsel is null || opposingCounsel.Id == Guid.Empty
                ? null
                : (object)new { id = opposingCounsel.Id.ToString(), name = opposingCounsel.Name ?? string.Empty }
        };

        var valueRaw = JsonSerializer.SerializeToElement(composite);
        return BuildFact(subject, predicate, valueRaw, "matter-facts", tenantId, matter.Id);
    }

    /// <summary>
    /// Compose the canonical FactArtifact envelope per design.md §2.1 + SPEC §3.4.1.
    /// </summary>
    private static FactArtifact BuildFact(
        string subject,
        string predicate,
        JsonElement valueRaw,
        string displayHint,
        string tenantId,
        Guid matterId)
    {
        return new FactArtifact
        {
            Id = $"fact:{subject}:{predicate}",
            Subject = subject,
            Predicate = predicate,
            Value = new Value
            {
                Raw = valueRaw,
                DisplayHint = displayHint
            },
            Evidence = new[]
            {
                new EvidenceRef
                {
                    RefType = "fact-source",
                    Ref = $"dataverse://{MatterEntityName}/{matterId}#{predicate}"
                }
            },
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy
            {
                Kind = "query",
                Id = ProducerId,
                Version = ProducerVersion
            },
            Scope = new Scope
            {
                TenantId = tenantId,
                MatterId = matterId.ToString()
            },
            TenantId = tenantId,
            Confidence = 1.0
        };
    }
}
