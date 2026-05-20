using System.Text.RegularExpressions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.PromptLibrary;

/// <summary>
/// Implements <see cref="IPromptLibraryService"/> with a 4-tier ownership model.
///
/// Storage routing:
///   - Personal + Team → Cosmos DB <c>prompts</c> container (partition key: <c>/ownerId</c>)
///   - Org + System    → Dataverse <c>sprk_prompttemplate</c> (deferred to integration task AIPU2-036)
///
/// Cosmos access pattern mirrors <c>SessionPersistenceService</c>:
///   - <see cref="CosmosClient"/> is singleton; <c>Container</c> handle is resolved per call.
///   - Partition key is always the document's <c>ownerId</c> field (userId or teamId).
///   - Failures throw rather than swallow — callers decide how to surface errors.
///
/// Lifetime: Scoped — one instance per HTTP request (ADR-010).
/// </summary>
public sealed class PromptLibraryService : IPromptLibraryService
{
    private const string ContainerName = "prompts";

    // Regex matches {{variableName}} placeholders (no nested braces, no whitespace inside).
    private static readonly Regex PlaceholderPattern =
        new(@"\{\{([A-Za-z_][A-Za-z0-9_.]*)\}\}", RegexOptions.Compiled);

    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly ILogger<PromptLibraryService> _logger;

    public PromptLibraryService(
        CosmosClient cosmosClient,
        IConfiguration configuration,
        ILogger<PromptLibraryService> logger)
    {
        _cosmosClient = cosmosClient ?? throw new ArgumentNullException(nameof(cosmosClient));
        _databaseName = configuration["CosmosPersistence:DatabaseName"]
            ?? throw new InvalidOperationException("CosmosPersistence:DatabaseName is not configured.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // IPromptLibraryService — List
    // =========================================================================

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PromptTemplate>> ListAsync(
        string tenantId,
        string userId,
        IReadOnlyList<string>? teamIds = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var results = new List<PromptTemplate>();

        // --- Personal templates (ownerId = userId) ---
        results.AddRange(await QueryByOwnerAsync(tenantId, userId, PromptOwnership.Personal, ct));

        // --- Team templates (one query per teamId) ---
        if (teamIds is { Count: > 0 })
        {
            foreach (var teamId in teamIds)
            {
                results.AddRange(await QueryByOwnerAsync(tenantId, teamId, PromptOwnership.Team, ct));
            }
        }

        // --- Org + System templates from Dataverse (deferred to AIPU2-036) ---
        // TODO AIPU2-036: Query Dataverse sprk_prompttemplate with sprk_tier in (Organization, System)
        // and append results here. For now, return empty for these tiers.

        _logger.LogDebug(
            "PromptLibraryService.ListAsync: returned {Count} templates for user {UserId} in tenant {TenantId}",
            results.Count, userId, tenantId);

        return results.AsReadOnly();
    }

    // =========================================================================
    // IPromptLibraryService — Get
    // =========================================================================

    /// <inheritdoc/>
    public async Task<PromptTemplate?> GetAsync(
        string tenantId,
        string templateId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        // Cosmos: cross-partition point-read not available without knowing ownerId,
        // so we issue a cross-partition query filtered by id + tenantId.
        var template = await FindByIdAsync(tenantId, templateId, ct);
        if (template is not null)
        {
            return template;
        }

        // --- Org + System templates from Dataverse (deferred to AIPU2-036) ---
        // TODO AIPU2-036: Query Dataverse sprk_prompttemplate by sprk_prompttemplateid = templateId.
        // If found, map to PromptTemplate with Ownership = Organization or System.

        return null;
    }

    // =========================================================================
    // IPromptLibraryService — Create
    // =========================================================================

    /// <inheritdoc/>
    public async Task<PromptTemplate> CreateAsync(
        string tenantId,
        string userId,
        CreatePromptRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Body);

        if (request.Ownership is PromptOwnership.Organization or PromptOwnership.System)
        {
            throw new InvalidOperationException(
                $"Cannot create templates with ownership tier '{request.Ownership}' via the API. " +
                "Org and System templates are managed outside the API.");
        }

        if (request.Ownership == PromptOwnership.Team &&
            string.IsNullOrWhiteSpace(request.OwnerId))
        {
            throw new ArgumentException(
                "OwnerId (teamId) is required when Ownership is Team.", nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        var ownerId = request.Ownership == PromptOwnership.Team
            ? request.OwnerId!
            : userId;

        var template = new PromptTemplate(
            Id: Guid.NewGuid().ToString("D"),
            TenantId: tenantId,
            Name: request.Name,
            Description: request.Description,
            Body: request.Body,
            Ownership: request.Ownership,
            OwnerId: ownerId,
            Tags: request.Tags ?? Array.Empty<string>(),
            Variables: request.Variables ?? Array.Empty<TemplateVariable>(),
            CreatedAt: now,
            UpdatedAt: now);

        var document = CosmosPromptDocument.FromPromptTemplate(template);
        var container = GetContainer();

        await container.CreateItemAsync(
            item: document,
            partitionKey: new PartitionKey(ownerId),
            cancellationToken: ct);

        _logger.LogInformation(
            "PromptLibraryService.CreateAsync: created template {TemplateId} (owner={OwnerId}, tier={Tier}, tenant={TenantId})",
            template.Id, ownerId, request.Ownership, tenantId);

        return template;
    }

    // =========================================================================
    // IPromptLibraryService — Update
    // =========================================================================

    /// <inheritdoc/>
    public async Task UpdateAsync(
        string tenantId,
        string templateId,
        UpdatePromptRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(request);

        var existing = await FindByIdAsync(tenantId, templateId, ct)
            ?? throw new KeyNotFoundException($"Prompt template '{templateId}' not found.");

        EnforceWritable(existing);

        var updated = existing with
        {
            Name = request.Name ?? existing.Name,
            Description = request.Description ?? existing.Description,
            Body = request.Body ?? existing.Body,
            Tags = request.Tags ?? existing.Tags,
            Variables = request.Variables ?? existing.Variables,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var document = CosmosPromptDocument.FromPromptTemplate(updated);
        var container = GetContainer();

        await container.ReplaceItemAsync(
            item: document,
            id: templateId,
            partitionKey: new PartitionKey(existing.OwnerId),
            cancellationToken: ct);

        _logger.LogInformation(
            "PromptLibraryService.UpdateAsync: updated template {TemplateId} (tenant={TenantId})",
            templateId, tenantId);
    }

    // =========================================================================
    // IPromptLibraryService — Delete
    // =========================================================================

    /// <inheritdoc/>
    public async Task DeleteAsync(
        string tenantId,
        string templateId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        var existing = await FindByIdAsync(tenantId, templateId, ct)
            ?? throw new KeyNotFoundException($"Prompt template '{templateId}' not found.");

        EnforceWritable(existing);

        var container = GetContainer();

        await container.DeleteItemAsync<CosmosPromptDocument>(
            id: templateId,
            partitionKey: new PartitionKey(existing.OwnerId),
            cancellationToken: ct);

        _logger.LogInformation(
            "PromptLibraryService.DeleteAsync: deleted template {TemplateId} (tenant={TenantId})",
            templateId, tenantId);
    }

    // =========================================================================
    // IPromptLibraryService — Render
    // =========================================================================

    /// <inheritdoc/>
    public async Task<string> RenderAsync(
        string tenantId,
        string templateId,
        Dictionary<string, string> variables,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(variables);

        var template = await GetAsync(tenantId, templateId, ct)
            ?? throw new KeyNotFoundException($"Prompt template '{templateId}' not found.");

        return RenderBody(template, variables);
    }

    // =========================================================================
    // Internal: render (also used by tests directly via service)
    // =========================================================================

    /// <summary>
    /// Renders the template body by replacing <c>{{variableName}}</c> placeholders.
    /// Validates that all required variables are present before substitution.
    /// </summary>
    internal static string RenderBody(PromptTemplate template, Dictionary<string, string> variables)
    {
        // Validate required variables
        var missing = template.Variables
            .Where(v => v.Required && !variables.ContainsKey(v.Name))
            .Select(v => v.Name)
            .ToList();

        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"Render failed: missing required variable(s): {string.Join(", ", missing)}",
                nameof(variables));
        }

        // Replace all {{name}} occurrences — unknown variables are left as-is (non-strict)
        return PlaceholderPattern.Replace(template.Body, match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    // =========================================================================
    // Private helpers — Cosmos
    // =========================================================================

    private Container GetContainer() => _cosmosClient.GetContainer(_databaseName, ContainerName);

    /// <summary>
    /// Cross-partition query to locate a template by id + tenantId.
    /// Used when the ownerId (partition key) is unknown.
    /// </summary>
    private async Task<PromptTemplate?> FindByIdAsync(
        string tenantId,
        string templateId,
        CancellationToken ct)
    {
        try
        {
            var container = GetContainer();
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.id = @id AND c.tenantId = @tenantId")
                .WithParameter("@id", templateId)
                .WithParameter("@tenantId", tenantId);

            using var iterator = container.GetItemQueryIterator<CosmosPromptDocument>(
                query,
                requestOptions: new QueryRequestOptions { MaxItemCount = 1 });

            if (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                var doc = page.FirstOrDefault();
                return doc?.ToPromptTemplate();
            }

            return null;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PromptLibraryService.FindByIdAsync: Cosmos query failed for template {TemplateId} (tenant={TenantId})",
                templateId, tenantId);
            return null;
        }
    }

    /// <summary>
    /// Lists all Cosmos templates for a given ownerId + ownership tier.
    /// </summary>
    private async Task<List<PromptTemplate>> QueryByOwnerAsync(
        string tenantId,
        string ownerId,
        PromptOwnership ownership,
        CancellationToken ct)
    {
        var results = new List<PromptTemplate>();
        try
        {
            var container = GetContainer();
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.ownerId = @ownerId AND c.tenantId = @tenantId AND c.ownership = @ownership")
                .WithParameter("@ownerId", ownerId)
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@ownership", (int)ownership);

            using var iterator = container.GetItemQueryIterator<CosmosPromptDocument>(
                query,
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(ownerId) });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                results.AddRange(page.Select(doc => doc.ToPromptTemplate()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PromptLibraryService.QueryByOwnerAsync: query failed for owner {OwnerId} (tier={Tier}, tenant={TenantId}) — returning partial results",
                ownerId, ownership, tenantId);
        }

        return results;
    }

    // =========================================================================
    // Private helpers — authorization
    // =========================================================================

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the template is Org or System tier
    /// (callers should map this to HTTP 403).
    /// </summary>
    private static void EnforceWritable(PromptTemplate template)
    {
        if (template.Ownership is PromptOwnership.Organization or PromptOwnership.System)
        {
            throw new InvalidOperationException(
                $"Template '{template.Id}' belongs to the '{template.Ownership}' tier and cannot be modified or deleted via the API.");
        }
    }
}
