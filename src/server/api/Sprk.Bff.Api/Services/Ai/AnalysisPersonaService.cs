using System.Text.Json.Serialization;
using Azure.Core;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Focused service for AnalysisPersona LIST operations against the <c>sprk_aipersona</c> Dataverse entity.
/// </summary>
/// <remarks>
/// <para>
/// R6 Pillar 1 (D-A-02). Mirrors <see cref="AnalysisActionService"/> / <see cref="AnalysisSkillService"/>
/// in shape: thin Dataverse OData LIST + paginated <see cref="ScopeListResult{T}"/> return.
/// Persona resolution methods (Resolve*Persona — task 003) and CRUD (task 003+) are scoped OUT of this
/// task to keep the IScopeResolverService surface change minimal per coordination with task 003.
/// </para>
/// <para>
/// Per refined ADR-013 (2026-05-20): the persona LIST is Zone B (CRUD-side), so this service does NOT
/// inject any AI internals (<c>IOpenAiClient</c>, <c>IPlaybookService</c>, etc.). It is a thin
/// Dataverse HTTP client + OData query builder.
/// </para>
/// </remarks>
public class AnalysisPersonaService : DataverseHttpServiceBase
{
    public AnalysisPersonaService(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<AnalysisPersonaService> logger)
        : base(httpClient, configuration, credential, logger)
    {
    }

    /// <summary>
    /// List all available personas with pagination/filtering/sorting per <see cref="ScopeListOptions"/>.
    /// </summary>
    public async Task<ScopeListResult<AnalysisPersona>> ListPersonasAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "[LIST PERSONAS] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}",
            options.Page, options.PageSize, options.NameFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["scopetype"] = "sprk_scopetype"
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_aipersonaid,sprk_name,sprk_description,sprk_systemprompt,sprk_scopetype,sprk_tags,sprk_availableadhoc,_sprk_parentpersonaid_value",
            expandClause: string.Empty,
            nameFieldPath: "sprk_name",
            categoryFieldPath: null,
            sortFieldMappings: sortMappings);

        var url = $"sprk_aipersonas?{query}";
        Logger.LogDebug("[LIST PERSONAS] Query URL: {Url}", url);

        var response = await Http.GetAsync(url, cancellationToken);
        await EnsureSuccessWithDiagnosticsAsync(response, "ListPersonasAsync", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<PersonaEntity>>(cancellationToken);
        if (result == null)
        {
            Logger.LogWarning("[LIST PERSONAS] Failed to deserialize response");
            return new ScopeListResult<AnalysisPersona>
            {
                Items = [],
                TotalCount = 0,
                Page = options.Page,
                PageSize = options.PageSize
            };
        }

        var personas = result.Value.Select(MapEntity).ToArray();

        Logger.LogInformation(
            "[LIST PERSONAS] Retrieved {Count} personas from Dataverse (Total: {TotalCount})",
            personas.Length, result.ODataCount ?? personas.Length);

        return new ScopeListResult<AnalysisPersona>
        {
            Items = personas,
            TotalCount = result.ODataCount ?? personas.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    #region Resolve (task 003 surface — most-specific-wins per Q1 / FR-03)

    /// <summary>
    /// Resolve effective persona for chat using Q1 most-specific-wins precedence:
    /// playbook-attached &gt; tenant CUST- &gt; global SYS-. Throws when no SYS- default exists
    /// (catastrophic seed-data failure — task 004 owns seeding).
    /// </summary>
    /// <remarks>
    /// R6 Pillar 1 (D-A-03). Wired by task 005 into <c>SprkChatAgentFactory.CreateAgentAsync</c>
    /// to replace the hardcoded <c>BuildDefaultSystemPrompt()</c> call site. NFR-01 binding:
    /// the persona <see cref="AnalysisPersona.SystemPrompt"/> augments the conversational system
    /// prompt; it never replaces conversational ability — composition is the caller's responsibility.
    /// </remarks>
    public async Task<AnalysisPersona> ResolvePersonaForChatAsync(
        string tenantId,
        Guid? playbookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId, nameof(tenantId));

        var effective = await GetEffectivePersonaAsync(tenantId, playbookId, cancellationToken);
        if (effective != null)
        {
            return effective;
        }

        // FR-04: no override AND no SYS- default = catastrophic seed-data failure. Surface clearly.
        Logger.LogError(
            "[RESOLVE PERSONA] No SYS- default persona seeded for tenant '{TenantId}' (playbookId={PlaybookId}). " +
            "Expected at least one SYS- global persona per FR-04 seeding (task 004).",
            tenantId, playbookId);

        throw new InvalidOperationException(
            $"No persona resolved for tenant '{tenantId}' (playbookId={playbookId}). " +
            "Expected at least one global SYS- persona seeded per R6 Pillar 1 / FR-04 (task 004 seed-row deployment).");
    }

    /// <summary>
    /// Get effective persona for chat: most-specific-wins. Returns <c>null</c> when no
    /// candidate matches at any precedence layer. Tenant-isolation enforced per NFR-14:
    /// CUST- queries are filtered on CUST- prefix; SYS- queries are global by definition.
    /// </summary>
    public async Task<AnalysisPersona?> GetEffectivePersonaAsync(
        string tenantId,
        Guid? playbookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId, nameof(tenantId));

        Logger.LogDebug(
            "[GET EFFECTIVE PERSONA] Resolving for tenant '{TenantId}' (playbookId={PlaybookId})",
            tenantId, playbookId);

        await EnsureAuthenticatedAsync(cancellationToken);

        // Precedence 1: Playbook-attached (most specific). Only checked when a playbook is bound.
        //
        // DEF-002 (R6 closeout, 2026-06-29) — Replaces the prior `QueryFirstByScopeTypeAsync`
        // call that filtered ONLY on `sprk_scopetype eq PlaybookAttached`. That stub matched the
        // FIRST PlaybookAttached-typed persona globally, regardless of WHICH playbook was bound,
        // so a tenant with one PlaybookAttached row would resolve it for every playbook session
        // (no playbook → persona linkage). The new `sprk_analysisplaybook.sprk_playbookpersona`
        // lookup column (added 2026-06-29) is the real FK; we fetch the playbook row, read the
        // `_sprk_playbookpersona_value` GUID, and resolve the persona by primary key.
        if (playbookId.HasValue)
        {
            var attached = await ResolvePlaybookAttachedPersonaAsync(playbookId.Value, cancellationToken);
            if (attached != null)
            {
                Logger.LogInformation(
                    "[GET EFFECTIVE PERSONA] Resolved PlaybookAttached persona '{Name}' for tenant '{TenantId}' (playbookId={PlaybookId})",
                    attached.Name, tenantId, playbookId);
                return attached;
            }
        }

        // Precedence 2: Tenant CUST- (overrides global). NFR-14 SYS-/CUST- boundary:
        // filter on CUST- name prefix so a SYS- row mis-tagged as Tenant scopeType can't leak.
        var tenantOverride = await QueryFirstByScopeTypeAsync(
            scopeType: (int)PersonaScopeType.Tenant,
            additionalFilter: $"startswith(sprk_name, '{CustomerPrefix}')",
            cancellationToken: cancellationToken);

        if (tenantOverride != null)
        {
            Logger.LogInformation(
                "[GET EFFECTIVE PERSONA] Resolved Tenant CUST- persona '{Name}' for tenant '{TenantId}'",
                tenantOverride.Name, tenantId);
            return tenantOverride;
        }

        // Precedence 3: Global SYS- (default fallback). NFR-14 SYS-/CUST- boundary:
        // filter on SYS- name prefix so a CUST- row mis-tagged as Global can't be returned.
        var sysDefault = await QueryFirstByScopeTypeAsync(
            scopeType: (int)PersonaScopeType.Global,
            additionalFilter: $"startswith(sprk_name, '{SystemPrefix}')",
            cancellationToken: cancellationToken);

        if (sysDefault != null)
        {
            Logger.LogInformation(
                "[GET EFFECTIVE PERSONA] Resolved Global SYS- persona '{Name}' for tenant '{TenantId}' (default fallback)",
                sysDefault.Name, tenantId);
            return sysDefault;
        }

        Logger.LogWarning(
            "[GET EFFECTIVE PERSONA] No persona found at any precedence layer for tenant '{TenantId}' (playbookId={PlaybookId})",
            tenantId, playbookId);
        return null;
    }

    /// <summary>
    /// DEF-002 (R6 closeout, 2026-06-29) — Resolves the persona attached to a specific playbook
    /// via the <c>sprk_analysisplaybook.sprk_playbookpersona</c> lookup column (added 2026-06-29).
    /// Returns <c>null</c> when the playbook row is missing, the lookup is unset, or the referenced
    /// persona row is missing / inactive. The caller (<see cref="GetEffectivePersonaAsync"/>) falls
    /// through to the CUST- / SYS- precedence layers in any of those cases (graceful degrade).
    /// </summary>
    /// <remarks>
    /// Two Dataverse round-trips: (1) GET <c>sprk_analysisplaybooks({playbookId})?$select=_sprk_playbookpersona_value</c>
    /// to read the lookup; (2) GET <c>sprk_aipersonas({personaId})?$select=...</c> to load the
    /// persona row. The two-trip pattern is intentional — using <c>$expand</c> would inline the
    /// persona row but also pull other lookup-related columns we don't need. ADR-014 caching
    /// hot-paths sit downstream (<c>IEmbeddingCache</c> + per-tenant Redis); per-call latency
    /// here is bounded by Dataverse round-trip time + connection reuse via the shared HttpClient.
    /// </remarks>
    private async Task<AnalysisPersona?> ResolvePlaybookAttachedPersonaAsync(
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        // (1) Fetch the playbook to read its sprk_playbookpersona lookup.
        var playbookUrl = $"sprk_analysisplaybooks({playbookId})?$select=_sprk_playbookpersona_value";
        var playbookResponse = await Http.GetAsync(playbookUrl, cancellationToken);

        if (!playbookResponse.IsSuccessStatusCode)
        {
            Logger.LogDebug(
                "[GET EFFECTIVE PERSONA] Playbook {PlaybookId} not found (status={StatusCode}); falling through to CUST-/SYS-.",
                playbookId, (int)playbookResponse.StatusCode);
            return null;
        }

        var playbookEntity = await playbookResponse.Content
            .ReadFromJsonAsync<PlaybookPersonaLookup>(cancellationToken);
        var attachedPersonaId = playbookEntity?.PlaybookPersonaValue;
        if (!attachedPersonaId.HasValue)
        {
            Logger.LogDebug(
                "[GET EFFECTIVE PERSONA] Playbook {PlaybookId} has no sprk_playbookpersona attached; falling through to CUST-/SYS-.",
                playbookId);
            return null;
        }

        // (2) Fetch the persona row by primary key.
        const string selectFields =
            "sprk_aipersonaid,sprk_name,sprk_description,sprk_systemprompt," +
            "sprk_scopetype,sprk_tags,sprk_availableadhoc,_sprk_parentpersonaid_value";

        var personaUrl = $"sprk_aipersonas({attachedPersonaId.Value})?$select={selectFields}";
        var personaResponse = await Http.GetAsync(personaUrl, cancellationToken);

        if (!personaResponse.IsSuccessStatusCode)
        {
            Logger.LogWarning(
                "[GET EFFECTIVE PERSONA] Playbook {PlaybookId} references persona {PersonaId} but the persona row is missing (status={StatusCode}); falling through.",
                playbookId, attachedPersonaId.Value, (int)personaResponse.StatusCode);
            return null;
        }

        var personaEntity = await personaResponse.Content.ReadFromJsonAsync<PersonaEntity>(cancellationToken);
        return personaEntity == null ? null : MapEntity(personaEntity);
    }

    /// <summary>
    /// OData query helper: first persona matching <c>sprk_scopetype eq {scopeType}</c>
    /// (+ optional additional filter), ordered by <c>sprk_name asc</c>, <c>$top=1</c>.
    /// </summary>
    private async Task<AnalysisPersona?> QueryFirstByScopeTypeAsync(
        int scopeType,
        string? additionalFilter,
        CancellationToken cancellationToken)
    {
        var filter = $"sprk_scopetype eq {scopeType}";
        if (!string.IsNullOrEmpty(additionalFilter))
        {
            filter = $"{filter} and {additionalFilter}";
        }

        const string selectFields =
            "sprk_aipersonaid,sprk_name,sprk_description,sprk_systemprompt," +
            "sprk_scopetype,sprk_tags,sprk_availableadhoc,_sprk_parentpersonaid_value";

        var url = $"sprk_aipersonas?$select={selectFields}&$filter={filter}&$orderby=sprk_name asc&$top=1";

        var response = await Http.GetAsync(url, cancellationToken);
        await EnsureSuccessWithDiagnosticsAsync(
            response, $"QueryFirstByScopeTypeAsync(scopeType={scopeType})", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<PersonaEntity>>(cancellationToken);
        var first = result?.Value.FirstOrDefault();
        return first == null ? null : MapEntity(first);
    }

    #endregion

    /// <summary>
    /// Maps a raw Dataverse <see cref="PersonaEntity"/> to the public <see cref="AnalysisPersona"/> DTO.
    /// API-side SYS-/CUST- prefix derivation: name prefix determines <see cref="ScopeOwnerType"/>
    /// (matches the 4-scope pattern; Dataverse has no enforcement).
    /// </summary>
    private static AnalysisPersona MapEntity(PersonaEntity entity)
    {
        var name = entity.Name ?? "Unnamed Persona";
        var ownerType = name.StartsWith("SYS-", StringComparison.OrdinalIgnoreCase)
            ? ScopeOwnerType.System
            : ScopeOwnerType.Customer;

        var scopeType = entity.ScopeType.HasValue
            ? (PersonaScopeType)entity.ScopeType.Value
            : PersonaScopeType.Global;

        return new AnalysisPersona
        {
            Id = entity.Id,
            Name = name,
            Description = entity.Description,
            SystemPrompt = entity.SystemPrompt ?? string.Empty,
            ScopeType = scopeType,
            Tags = entity.Tags,
            AvailableAdHoc = entity.AvailableAdHoc ?? false,
            OwnerType = ownerType,
            IsImmutable = ownerType == ScopeOwnerType.System,
            ParentScopeId = entity.ParentPersonaIdValue
        };
    }

    #region Private DTOs

    /// <summary>
    /// Raw Dataverse projection of <c>sprk_aipersona</c>. Internal — never returned to callers.
    /// </summary>
    internal class PersonaEntity
    {
        [JsonPropertyName("sprk_aipersonaid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        [JsonPropertyName("sprk_systemprompt")]
        public string? SystemPrompt { get; set; }

        [JsonPropertyName("sprk_scopetype")]
        public int? ScopeType { get; set; }

        [JsonPropertyName("sprk_tags")]
        public string? Tags { get; set; }

        [JsonPropertyName("sprk_availableadhoc")]
        public bool? AvailableAdHoc { get; set; }

        [JsonPropertyName("_sprk_parentpersonaid_value")]
        public Guid? ParentPersonaIdValue { get; set; }
    }

    /// <summary>
    /// DEF-002 (R6 closeout, 2026-06-29) — Minimal projection of <c>sprk_analysisplaybook</c>
    /// for reading just the <c>sprk_playbookpersona</c> lookup. Used by
    /// <see cref="ResolvePlaybookAttachedPersonaAsync"/> to avoid pulling the full playbook row
    /// (which has the large canvas JSON + config JSON blobs). Internal — never returned to callers.
    /// </summary>
    internal sealed class PlaybookPersonaLookup
    {
        [JsonPropertyName("_sprk_playbookpersona_value")]
        public Guid? PlaybookPersonaValue { get; set; }
    }

    #endregion
}
