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
        if (playbookId.HasValue)
        {
            var playbookAttached = await QueryFirstByScopeTypeAsync(
                scopeType: (int)PersonaScopeType.PlaybookAttached,
                additionalFilter: null,
                cancellationToken: cancellationToken);

            if (playbookAttached != null)
            {
                Logger.LogInformation(
                    "[GET EFFECTIVE PERSONA] Resolved PlaybookAttached persona '{Name}' for tenant '{TenantId}' (playbookId={PlaybookId})",
                    playbookAttached.Name, tenantId, playbookId);
                return playbookAttached;
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

    #endregion
}
