// R3 Part 1 — User-Record Membership Resolution (node executor)
// Task 041 (2026-06-21): Implements ExecutorType.LookupUserMembership = 52
// (added in task 040). Calls IMembershipResolverService IN-PROCESS per FR-1B.1
// (NOT an HTTP round-trip to /api/users/me/memberships/{entityType}) and binds
// the resolved IDs to the node's OutputVariable for downstream consumption
// (e.g., a downstream QueryDataverseNodeExecutor that filters by the IDs).
//
// DI pattern (per ADR-010 + AgentServiceNodeExecutor exemplar):
//   - Executor is Singleton (stateless; matches other INodeExecutor registrations).
//   - IMembershipResolverService is Scoped (consumes Scoped Dataverse/Redis clients).
//   - Bridge via IServiceScopeFactory.CreateScope() per ExecuteAsync invocation
//     (using-var ensures disposal). Mirrors AiAnalysisNodeExecutor + AgentServiceNodeExecutor.
//
// Config (read from PlaybookNodeDto.ConfigJson):
//   - entityType (required, string)       — Dataverse logical entity name (e.g., "sprk_matter").
//   - roles (optional, string[])          — passed through as MembershipResolveOptions.Roles
//                                           (case-insensitive role filter).
//   - includeRelated (optional, bool)     — FIXED 2026-08-21 (task 019 / FR-17 A-22). The
//                                           resolver's IncludeRelated (MembershipResolverService.
//                                           ResolveTransitiveAsync) is NOT a wildcard — each entry
//                                           must be a CONCRETE related-entity logical name that
//                                           1-hop-validates via Dataverse metadata. This node's
//                                           config schema has no field for naming those entities,
//                                           so there is no well-defined related-entity set to
//                                           pass; includeRelated=true is therefore a documented
//                                           no-op (IncludeRelated omitted/null) until a
//                                           relatedEntities-style field is added. See the
//                                           ExecuteAsync comment below + notes/task-019-*.md for
//                                           the open product-semantics question.
//   - outputVariable                      — required by the framework on PlaybookNodeDto itself
//                                           (NOT inside ConfigJson) — validated below.
//
// Output binding (NodeOutput.StructuredData):
//   {
//     "entityType": "sprk_matter",
//     "count": 47,
//     "ids": ["...", "..."],          // de-duplicated; suitable for downstream consumption
//     "byRole": { "owner": [...], "assignedAttorney": [...] },
//     "continuationToken": null,
//     "cacheExpiresAt": "2026-06-21T15:34:00Z"
//   }
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1B.1, AC-1B.1;
//            tasks/041-lookupusermembership-node-executor.poml;
//            ADR-010 (DI minimalism — Singleton with Scoped via factory);
//            ADR-013 (extend existing node-executor framework — no new patterns).

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor that resolves the executing user's record memberships for a
/// given Dataverse entity type via <see cref="IMembershipResolverService"/>
/// (in-process per FR-1B.1) and binds the resulting ID set to the node's
/// <c>OutputVariable</c> for downstream node consumption.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="INodeExecutor"/> for <see cref="ExecutorType.LookupUserMembership"/>
/// (value 52, added by task 040). Registered as a Singleton in
/// <c>AnalysisServicesModule.AddNodeExecutors</c> alongside the other executors.
/// </para>
/// <para>
/// Singleton-with-Scoped DI pattern: the executor injects
/// <see cref="IServiceScopeFactory"/> and calls <c>CreateScope()</c> once per
/// <see cref="ExecuteAsync"/> invocation, resolving the Scoped
/// <see cref="IMembershipResolverService"/> from the scoped provider. Mirrors
/// <see cref="AgentServiceNodeExecutor"/> + <see cref="AiAnalysisNodeExecutor"/>.
/// </para>
/// </remarks>
public sealed class LookupUserMembershipNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LookupUserMembershipNodeExecutor> _logger;

    public LookupUserMembershipNodeExecutor(
        IServiceScopeFactory scopeFactory,
        ILogger<LookupUserMembershipNodeExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.LookupUserMembership
    };

    // R7 task 085 / FR-23 — typed config schema for Playbook Builder canvas.
    // Derived from LookupUserMembershipNodeConfig: entityType (required), roles, includeRelated.
    private static readonly ExecutorConfigSchema ConfigSchemaInstance = new(
        ExecutorTypeName: nameof(ExecutorType.LookupUserMembership),
        ExecutorTypeValue: (int)ExecutorType.LookupUserMembership,
        Description: "Resolves the current user's record memberships for a given Dataverse entity type via IMembershipResolverService (FR-1B.1). Emits IDs + by-role map for downstream filter/template consumption.",
        Fields: new ConfigSchemaField[]
        {
            new(
                Name: "entityType",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Dataverse entity logical name to resolve memberships against (e.g., 'sprk_matter', 'sprk_document'). Required.",
                Default: null),
            new(
                Name: "roles",
                Type: SchemaFieldType.Array,
                Required: false,
                Description: "Optional case-insensitive role filter (e.g., ['owner', 'assignedAttorney']). Empty/null means 'all discovered roles for the entity'.",
                Default: null),
            new(
                Name: "includeRelated",
                Type: SchemaFieldType.Boolean,
                Required: false,
                Description: "Reserved for future 1-hop transitive expansion. NO-OP TODAY: this node has no field for naming the related entities the resolver's IncludeRelated contract requires (concrete entity names, not a wildcard) — see FR-17/A-22 (fixed 2026-08-21). Setting true currently has no effect.",
                Default: false)
        });

    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() => ConfigSchemaInstance;

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.OutputVariable))
        {
            errors.Add("LookupUserMembership node requires OutputVariable to be set on the node");
        }

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("LookupUserMembership node requires configuration (ConfigJson with 'entityType')");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        try
        {
            var config = JsonSerializer.Deserialize<LookupUserMembershipNodeConfig>(
                context.Node.ConfigJson, JsonOptions);
            if (config is null)
            {
                errors.Add("Failed to parse LookupUserMembership node configuration");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(config.EntityType))
                {
                    errors.Add("LookupUserMembership node requires 'entityType' in ConfigJson");
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid LookupUserMembership node configuration JSON: {ex.Message}");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        // OTEL span — child of the routing middleware span per the executor-span convention
        // adopted by AgentServiceNodeExecutor. ADR-015: only node.id, action_type, and outcome
        // are tagged — no caller PII or membership IDs.
        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.lookup_user_membership.node_execute", ActivityKind.Internal);
        activity?.SetTag("node.id", context.Node.Id.ToString());
        activity?.SetTag("node.name", context.Node.Name);
        activity?.SetTag("action_type", (int)ExecutorType.LookupUserMembership);

        _logger.LogDebug(
            "Executing LookupUserMembership node {NodeId} ({NodeName})",
            context.Node.Id, context.Node.Name);

        try
        {
            // Fail-fast validation — mirrors all other executors.
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                activity?.SetTag("node.outcome", "validation_failed");
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            var config = JsonSerializer.Deserialize<LookupUserMembershipNodeConfig>(
                context.Node.ConfigJson!, JsonOptions)!;
            var entityType = config.EntityType!.Trim();

            // Resolve the caller's systemuserid via the same convention used by
            // QueryDataverseNodeExecutor: PlaybookSchedulerService sets
            // NodeExecutionContext.UserId on the per-user execution context.
            var userId = ResolveUserId(context);
            if (userId is null || userId.Value == Guid.Empty)
            {
                activity?.SetTag("node.outcome", "missing_user");
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    "LookupUserMembership node requires NodeExecutionContext.UserId to be set " +
                    "(no caller identity available — playbook must be run in a per-user scheduling context)",
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Build resolver options from node config.
            //
            // FIXED 2026-08-21 (task 019 / FR-17 A-22): this used to pass the sentinel "*"
            // when includeRelated=true, under a comment claiming the resolver "accepts-but-
            // ignores" it in Phase 1A. That is no longer true — MembershipResolverService now
            // implements Phase 1D transitive expansion (task 054), and its IncludeRelated
            // contract requires each entry to be a CONCRETE related-entity logical name that
            // 1-hop-validates via Dataverse metadata (ResolveTransitiveAsync ->
            // DiscoverLookupsTargetingAsync). "*" is not a real entity name: pre-validation
            // (MembershipResolverService.ResolveAsync:164-185) lets it through (it contains
            // neither '.' nor '/'), but the metadata fetch for entity "*" then fails and
            // surfaces as MembershipDepthExceededException(reasonTag: "unknown-entity") — the
            // executor's catch(Exception) below turned that into NodeOutput.Error(InternalError)
            // on every includeRelated=true execution.
            //
            // This node's ConfigJson schema has ONLY a boolean includeRelated flag — unlike the
            // GET /api/users/me/memberships/{entityType}?includeRelated=documents,events HTTP
            // contract (MembershipEndpoints.cs), there is no field here for naming which related
            // entities to include. Per the project constraint (no well-defined related-entity set
            // at this node -> omit IncludeRelated rather than invent a value the resolver can't
            // resolve), IncludeRelated is always omitted (null) below. `includeRelated: true` is
            // therefore a documented NO-OP today, not an error — flagged as an open product-
            // semantics question in projects/unified-access-control-r2/notes/task-019-*.md: if
            // playbook authors need "these N related entities" transitive expansion from this
            // node, the config schema needs a relatedEntities: string[] field wired 1:1 to
            // MembershipResolveOptions.IncludeRelated (each entry still resolver-validated,
            // 1-hop cap per ADR-034/Q3), not a boolean.
            var options = new MembershipResolveOptions(
                Roles: NormalizeRoles(config.Roles),
                IdentityTypes: null,
                IncludeRelated: null,
                Limit: MembershipResolveOptions.DefaultLimit,
                ContinuationToken: null);

            if (config.IncludeRelated == true)
            {
                _logger.LogWarning(
                    "LookupUserMembership node {NodeId}: includeRelated=true was requested but " +
                    "this node has no config field for concrete related-entity names, so the " +
                    "request is a no-op (IncludeRelated omitted). See FR-17/A-22.",
                    context.Node.Id);
            }

            _logger.LogDebug(
                "LookupUserMembership node {NodeId}: resolving entityType={EntityType} " +
                "roles={RoleCount} includeRelated={IncludeRelated} for systemUserId={SystemUserId}",
                context.Node.Id,
                entityType,
                options.Roles?.Count ?? 0,
                config.IncludeRelated ?? false,
                userId.Value);

            // Singleton+Scoped DI bridge — CreateScope per execution, dispose at end.
            // Mirrors AgentServiceNodeExecutor + AiAnalysisNodeExecutor.
            using var scope = _scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider
                .GetRequiredService<IMembershipResolverService>();

            var response = await resolver.ResolveAsync(
                userId.Value,
                entityType,
                options,
                cancellationToken).ConfigureAwait(false);

            // Bind output. Use the response's already-deduplicated + sorted Ids list.
            // ByRole is included so downstream nodes that need per-role attribution can
            // use it via the StructuredData/template-engine surface.
            var outputData = new
            {
                entityType = response.EntityType,
                count = response.Count,
                ids = response.Ids,
                byRole = response.ByRole,
                continuationToken = response.ContinuationToken,
                cacheExpiresAt = response.CacheExpiresAt
            };

            _logger.LogInformation(
                "LookupUserMembership node {NodeId} completed -- entityType={EntityType}, count={Count}, roles={RoleCount}",
                context.Node.Id, response.EntityType, response.Count, response.ByRole.Count);

            activity?.SetTag("node.outcome", "success");
            activity?.SetTag("membership.entity_type", response.EntityType);
            activity?.SetTag("membership.count", response.Count);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                outputData,
                textContent: $"Resolved {response.Count} {response.EntityType} memberships across {response.ByRole.Count} role(s)",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "LookupUserMembership node {NodeId} was cancelled",
                context.Node.Id);

            activity?.SetTag("node.outcome", "cancelled");
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                "Node execution was cancelled",
                NodeErrorCodes.Cancelled,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (ArgumentException ex)
        {
            // MembershipResolverService throws ArgumentException on Guid.Empty / blank entityType —
            // surface as a validation error rather than an internal error.
            _logger.LogWarning(ex,
                "LookupUserMembership node {NodeId} rejected by resolver: {Message}",
                context.Node.Id, ex.Message);

            activity?.SetTag("node.outcome", "validation_failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Membership resolver rejected the request: {ex.Message}",
                NodeErrorCodes.ValidationFailed,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LookupUserMembership node {NodeId} failed: {ErrorMessage}",
                context.Node.Id, ex.Message);

            activity?.SetTag("node.outcome", "error");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Membership resolution failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Extracts the caller's Dataverse systemuserid from the execution context. Mirrors
    /// the convention in <see cref="QueryDataverseNodeExecutor"/>: primary path is
    /// <see cref="NodeExecutionContext.UserId"/> (set by PlaybookSchedulerService for
    /// per-user runs); fallback scans previous node outputs for a <c>userId</c> property.
    /// </summary>
    private static Guid? ResolveUserId(NodeExecutionContext context)
    {
        if (context.UserId.HasValue && context.UserId.Value != Guid.Empty)
        {
            return context.UserId.Value;
        }

        foreach (var (_, output) in context.PreviousOutputs)
        {
            if (!output.StructuredData.HasValue)
            {
                continue;
            }
            try
            {
                var data = output.StructuredData.Value;
                if (data.TryGetProperty("userId", out var userIdProp) &&
                    userIdProp.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(userIdProp.GetString(), out var parsed) &&
                    parsed != Guid.Empty)
                {
                    return parsed;
                }
            }
            catch
            {
                // Defensive — malformed previous output is not fatal.
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes the configured roles list — trims whitespace, drops empties, and
    /// returns null for an empty/missing list so the resolver applies "all roles".
    /// </summary>
    private static IReadOnlyList<string>? NormalizeRoles(IReadOnlyList<string>? configured)
    {
        if (configured is null || configured.Count == 0)
        {
            return null;
        }
        var cleaned = configured
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();
        return cleaned.Count == 0 ? null : cleaned;
    }
}

/// <summary>
/// Configuration for <see cref="LookupUserMembershipNodeExecutor"/> read from
/// <c>PlaybookNodeDto.ConfigJson</c>. Property names use camelCase per the playbook
/// JSON convention (consistent with <see cref="QueryDataverseNodeConfig"/>).
/// </summary>
internal sealed record LookupUserMembershipNodeConfig
{
    /// <summary>
    /// Dataverse entity logical name to resolve memberships against
    /// (e.g., <c>sprk_matter</c>, <c>sprk_document</c>). Required.
    /// </summary>
    [JsonPropertyName("entityType")]
    public string? EntityType { get; init; }

    /// <summary>
    /// Optional role filter (case-insensitive). Empty/null means "all discovered roles
    /// for the entity". Pass-through to <see cref="MembershipResolveOptions.Roles"/>.
    /// </summary>
    [JsonPropertyName("roles")]
    public IReadOnlyList<string>? Roles { get; init; }

    /// <summary>
    /// Reserved for future 1-hop transitive expansion (Q3 owner clarification: 1-hop max).
    /// NO-OP TODAY (fixed 2026-08-21, task 019 / FR-17 A-22): this node has no field for
    /// naming which related entities to include, and the resolver's IncludeRelated contract
    /// requires concrete related-entity names (not a wildcard) — so <c>true</c> currently has
    /// no effect; <see cref="ExecuteAsync"/> always omits IncludeRelated. Default <c>false</c>.
    /// </summary>
    [JsonPropertyName("includeRelated")]
    public bool? IncludeRelated { get; init; }
}
