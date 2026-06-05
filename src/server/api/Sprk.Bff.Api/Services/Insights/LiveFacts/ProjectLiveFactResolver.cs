using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Insights.LiveFacts;

/// <summary>
/// Production <see cref="ILiveFactResolver"/> for the <c>project:</c> subject scheme. Reads
/// <c>sprk_project</c> via <see cref="IGenericEntityService"/> and returns a deterministic
/// <see cref="FactArtifact"/> for each project-scoped predicate.
/// </summary>
/// <remarks>
/// <para>
/// <b>r2 Wave D5 (task 034) — NEW</b> per design-a6 §6.2. One of three per-entity
/// resolvers registered in <c>IReadOnlyDictionary&lt;string, ILiveFactResolver&gt;</c>
/// keyed by entity-type name. The dispatcher (<see cref="Sprk.Bff.Api.Services.Ai.Nodes.LiveFactNode"/>)
/// routes by entity-type derived from the parsed subject.
/// </para>
/// <para>
/// <b>Zone B placement</b> per SPEC §3.5 — lives under <c>Services/Insights/LiveFacts/</c>
/// and consumes <see cref="IGenericEntityService"/> only. ZERO AI-internal imports.
/// </para>
/// <para>
/// <b>Supported predicates (Phase 1.5 initial set per design-a6 §6.2)</b> — extend as
/// project-cost-prediction or project-status playbooks land. The actual
/// <c>sprk_project</c> attribute names below are placeholder mappings pending playbook-author
/// SME confirmation per D5-Q1; the resolver dispatch logic + shape (<c>{id,name}</c> for
/// lookups, plain string for option-set/status) is the load-bearing part:
/// <list type="bullet">
///   <item><c>projectName</c> → <c>sprk_name</c> (plain string)</item>
///   <item><c>projectManager</c> → <c>sprk_projectmanager</c> (LOOKUP → contact)</item>
///   <item><c>client</c> → <c>sprk_externalaccount</c> (LOOKUP → account)</item>
///   <item><c>projectStatus</c> → <c>sprk_status</c> (option set → display name string)</item>
///   <item><c>currentProjectFacts</c> → composite shape returning the above sub-values</item>
/// </list>
/// Any other predicate throws <see cref="LiveFactNotSupportedException"/>.
/// </para>
/// <para>
/// <b>Behavior parity with matter resolver</b>: confidence = 1.0; null-on-missing-row;
/// <see cref="LiveFactNotSupportedException"/> on unsupported predicate;
/// <c>producedBy.id = "dataverse://sprk_project"</c>; same exception handling for "row not
/// found" from <see cref="IGenericEntityService"/>.
/// </para>
/// <para>
/// <b>Inter-entity references</b> (A6-D7): when a predicate returns a reference to another
/// entity (e.g., the <c>client</c> account), the value shape is <c>{id, name}</c> only — no
/// recursion into the referenced entity. Playbooks that need facts from BOTH this entity
/// AND a related entity add a second <c>LiveFactNode</c> with the related subject (e.g.,
/// <c>matter:&lt;guid&gt;</c>).
/// </para>
/// <para>
/// <b>Lifetime</b>: registered as <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped"/>
/// in <c>InsightsModule</c> per A6-D8 (matches matter resolver + <see cref="IGenericEntityService"/>).
/// </para>
/// </remarks>
public sealed class ProjectLiveFactResolver : ILiveFactResolver
{
    /// <summary>Logical name of the Dataverse entity this resolver reads.</summary>
    internal const string ProjectEntityName = "sprk_project";

    /// <summary>The subject scheme prefix this resolver supports.</summary>
    internal const string ProjectSubjectScheme = "project:";

    /// <summary>ProducedBy.Id for all FactArtifacts emitted by this resolver.</summary>
    internal const string ProducerId = "dataverse://sprk_project";

    /// <summary>ProducedBy.Version for all FactArtifacts.</summary>
    internal const string ProducerVersion = "v1";

    /// <summary>
    /// Fields the resolver pulls from sprk_project to satisfy the Phase 1.5 initial predicate
    /// set. Kept minimal — broader reads happen via dedicated query services.
    /// </summary>
    private static readonly string[] ReadColumns =
    [
        "sprk_projectid",
        "sprk_name",
        "sprk_projectmanager",
        "sprk_externalaccount",
        "sprk_status"
    ];

    // Predicate names — Phase 1.5 initial set per design-a6 §6.2. Confirm with playbook
    // authoring SME during D5-Q1 resolution if the project-cost-prediction playbook needs
    // additional predicates beyond this initial 4-predicate + composite set.
    private const string PredicateProjectName = "projectName";
    private const string PredicateProjectManager = "projectManager";
    private const string PredicateClient = "client";
    private const string PredicateProjectStatus = "projectStatus";
    private const string PredicateCurrentProjectFacts = "currentProjectFacts";

    private readonly IGenericEntityService _entityService;
    private readonly ILogger<ProjectLiveFactResolver> _logger;

    public ProjectLiveFactResolver(
        IGenericEntityService entityService,
        ILogger<ProjectLiveFactResolver> logger)
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

        // Parse "project:{guid}" — only project: scheme supported by this resolver.
        // Defensive check (the dispatcher already validates via ISubjectParser; this is
        // belt-and-suspenders for direct-call mis-routings).
        var projectId = ParseProjectSubject(subject);
        if (projectId is null)
        {
            throw new LiveFactNotSupportedException(subject, predicate);
        }

        Entity? project;
        try
        {
            project = await _entityService.RetrieveAsync(
                ProjectEntityName,
                projectId.Value,
                ReadColumns,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            && (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug(
                "ProjectLiveFactResolver: project {ProjectId} not found in Dataverse; returning null",
                projectId);
            return null;
        }

        if (project is null)
        {
            return null;
        }

        return predicate switch
        {
            PredicateProjectName => BuildStringFact(project, subject, predicate, "sprk_name", tenantId),
            PredicateProjectManager => BuildLookupFact(project, subject, predicate, "sprk_projectmanager", tenantId),
            PredicateClient => BuildLookupFact(project, subject, predicate, "sprk_externalaccount", tenantId),
            PredicateProjectStatus => BuildOptionSetFact(project, subject, predicate, "sprk_status", tenantId),
            PredicateCurrentProjectFacts => BuildCompositeFact(project, subject, predicate, tenantId),
            _ => throw new LiveFactNotSupportedException(subject, predicate)
        };
    }

    /// <summary>
    /// Parses <c>project:{guid}</c>. Returns null on any format violation; the caller
    /// throws <see cref="LiveFactNotSupportedException"/>.
    /// </summary>
    internal static Guid? ParseProjectSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        if (!subject.StartsWith(ProjectSubjectScheme, StringComparison.OrdinalIgnoreCase)) return null;

        var suffix = subject.AsSpan(ProjectSubjectScheme.Length).Trim();
        return Guid.TryParse(suffix, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Build a FactArtifact for a plain string field (e.g., <c>sprk_name</c>). Returns null
    /// when the field is empty.
    /// </summary>
    private FactArtifact? BuildStringFact(
        Entity project,
        string subject,
        string predicate,
        string fieldName,
        string tenantId)
    {
        var value = project.GetAttributeValue<string>(fieldName);
        if (string.IsNullOrWhiteSpace(value))
        {
            _logger.LogDebug(
                "ProjectLiveFactResolver: project {ProjectId} has no value for field {Field}; returning null for predicate {Predicate}",
                project.Id, fieldName, predicate);
            return null;
        }

        var valueRaw = JsonSerializer.SerializeToElement(value);
        return BuildFact(subject, predicate, valueRaw, "text", tenantId, project.Id);
    }

    /// <summary>
    /// Build a FactArtifact for a single lookup field. Returns null when unset. Inter-entity
    /// references are shaped <c>{id, name}</c> only (A6-D7 — no recursion into the referenced
    /// entity).
    /// </summary>
    private FactArtifact? BuildLookupFact(
        Entity project,
        string subject,
        string predicate,
        string fieldName,
        string tenantId)
    {
        var lookup = project.GetAttributeValue<EntityReference>(fieldName);
        if (lookup is null || lookup.Id == Guid.Empty)
        {
            _logger.LogDebug(
                "ProjectLiveFactResolver: project {ProjectId} has no value for field {Field}; returning null for predicate {Predicate}",
                project.Id, fieldName, predicate);
            return null;
        }

        var valueRaw = JsonSerializer.SerializeToElement(new
        {
            id = lookup.Id.ToString(),
            name = lookup.Name ?? string.Empty
        });
        return BuildFact(subject, predicate, valueRaw, "entity-reference", tenantId, project.Id);
    }

    /// <summary>
    /// Build a FactArtifact for an option-set field. Returns the option-set display value
    /// (or its numeric Value as a string when the display lookup is unavailable in the
    /// runtime metadata) as a plain string. Returns null when unset.
    /// </summary>
    private FactArtifact? BuildOptionSetFact(
        Entity project,
        string subject,
        string predicate,
        string fieldName,
        string tenantId)
    {
        var optionSet = project.GetAttributeValue<OptionSetValue>(fieldName);
        if (optionSet is null)
        {
            _logger.LogDebug(
                "ProjectLiveFactResolver: project {ProjectId} has no value for option-set field {Field}; returning null for predicate {Predicate}",
                project.Id, fieldName, predicate);
            return null;
        }

        // FormattedValue (display label) is populated by Dataverse when the read query includes
        // the formatted value extension. Fall back to numeric value when label unavailable —
        // the synthesis prompt's project-status mapping can still reason over the numeric value.
        string displayValue;
        if (project.FormattedValues.TryGetValue(fieldName, out var formatted)
            && !string.IsNullOrWhiteSpace(formatted))
        {
            displayValue = formatted;
        }
        else
        {
            displayValue = optionSet.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var valueRaw = JsonSerializer.SerializeToElement(displayValue);
        return BuildFact(subject, predicate, valueRaw, "text", tenantId, project.Id);
    }

    /// <summary>
    /// Build the composite <c>currentProjectFacts</c> Fact — a single FactArtifact whose
    /// <see cref="Value.Raw"/> is a JSON object containing all 4 sub-predicates. Matches the
    /// matter-facts composite shape pattern for synthesis-prompt parity.
    /// </summary>
    private FactArtifact BuildCompositeFact(
        Entity project,
        string subject,
        string predicate,
        string tenantId)
    {
        var name = project.GetAttributeValue<string>("sprk_name");
        var manager = project.GetAttributeValue<EntityReference>("sprk_projectmanager");
        var client = project.GetAttributeValue<EntityReference>("sprk_externalaccount");
        var status = project.GetAttributeValue<OptionSetValue>("sprk_status");

        string? statusDisplay = null;
        if (status is not null)
        {
            statusDisplay = project.FormattedValues.TryGetValue("sprk_status", out var formatted)
                && !string.IsNullOrWhiteSpace(formatted)
                ? formatted
                : status.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var composite = new
        {
            projectName = string.IsNullOrWhiteSpace(name) ? null : name,
            projectManager = manager is null || manager.Id == Guid.Empty
                ? null
                : (object)new { id = manager.Id.ToString(), name = manager.Name ?? string.Empty },
            client = client is null || client.Id == Guid.Empty
                ? null
                : (object)new { id = client.Id.ToString(), name = client.Name ?? string.Empty },
            projectStatus = statusDisplay
        };

        var valueRaw = JsonSerializer.SerializeToElement(composite);
        return BuildFact(subject, predicate, valueRaw, "project-facts", tenantId, project.Id);
    }

    /// <summary>
    /// Compose the canonical FactArtifact envelope per design.md §2.1 + SPEC §3.4.1.
    /// </summary>
    /// <remarks>
    /// <b>Scope.MatterId is intentionally null</b> for project subjects. Per design-a6 §4.4
    /// writer behavior, project Observations have <c>scope.entityType="project"</c> +
    /// <c>scope.entityId={projectGuid}</c> only — but those new fields land in task 035
    /// (Wave D6 index scope migration). Until then, projects emit with bare TenantId scope.
    /// </remarks>
    private static FactArtifact BuildFact(
        string subject,
        string predicate,
        JsonElement valueRaw,
        string displayHint,
        string tenantId,
        Guid projectId)
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
                    Ref = $"dataverse://{ProjectEntityName}/{projectId}#{predicate}"
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
                TenantId = tenantId
                // MatterId intentionally null per design-a6 §4.4 (project subjects do not
                // populate scope.matterId). scope.entityType/entityId land in task 035.
            },
            TenantId = tenantId,
            Confidence = 1.0
        };
    }
}
