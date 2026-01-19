using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Scopes;

/// <summary>
/// Provides "Extend" functionality for scopes with single-level inheritance.
/// Creates child scopes that inherit from a parent and can override specific fields.
/// </summary>
/// <remarks>
/// <para>
/// Inheritance Model:
/// - Child scope links to parent via parentid lookup field
/// - Non-overridden fields inherit values from parent
/// - Overridden fields store child-specific values
/// - Single-level only (no grandchildren)
/// - Parent changes propagate to non-overridden child fields
/// </para>
/// <para>
/// Task 044: Implement "Extend" with inheritance
/// </para>
/// </remarks>
public sealed class ScopeInheritanceService : IScopeInheritanceService
{
    private readonly IScopeResolverService _scopeResolver;
    private readonly IOwnershipValidator _ownershipValidator;
    private readonly IScopeCopyService _scopeCopyService;
    private readonly ILogger<ScopeInheritanceService> _logger;

    public ScopeInheritanceService(
        IScopeResolverService scopeResolver,
        IOwnershipValidator ownershipValidator,
        IScopeCopyService scopeCopyService,
        ILogger<ScopeInheritanceService> logger)
    {
        _scopeResolver = scopeResolver;
        _ownershipValidator = ownershipValidator;
        _scopeCopyService = scopeCopyService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ExtendScopeResult> ExtendScopeAsync(
        Guid parentScopeId,
        ScopeType scopeType,
        string childDisplayName,
        Dictionary<string, object>? initialOverrides,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Extending {ScopeType} scope {ParentId} to create '{ChildName}'",
            scopeType,
            parentScopeId,
            childDisplayName);

        // Validate parent exists and is not already a child
        var parentValidation = await ValidateParentAsync(parentScopeId, scopeType, cancellationToken);
        if (!parentValidation.IsValid)
        {
            return new ExtendScopeResult
            {
                Success = false,
                ErrorMessage = parentValidation.ErrorMessage
            };
        }

        // Generate unique name for child
        var uniqueName = await _scopeCopyService.GenerateUniqueNameAsync(
            childDisplayName,
            scopeType,
            cancellationToken);

        // Apply CUST- prefix
        var logicalName = _ownershipValidator.EnsureCustomerPrefix(
            GenerateLogicalName(uniqueName));

        // Create child scope with parent link
        var result = await CreateChildScopeAsync(
            parentScopeId,
            scopeType,
            logicalName,
            uniqueName,
            initialOverrides,
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Created child {ScopeType} '{Name}' extending parent {ParentId}",
                scopeType,
                result.ChildDisplayName,
                parentScopeId);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<EffectiveScopeData> GetEffectiveScopeAsync(
        Guid scopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Resolving effective values for {ScopeType} scope {ScopeId}",
            scopeType,
            scopeId);

        // Load scope and determine if it has a parent
        var scopeInfo = await LoadScopeInfoAsync(scopeId, scopeType, cancellationToken);

        if (scopeInfo.ParentId == null)
        {
            // No parent - return scope's own values
            return new EffectiveScopeData
            {
                ScopeId = scopeId,
                ParentId = null,
                Values = scopeInfo.Values,
                OverriddenFields = Array.Empty<string>(),
                InheritedFields = Array.Empty<string>()
            };
        }

        // Has parent - merge values
        var parentInfo = await LoadScopeInfoAsync(scopeInfo.ParentId.Value, scopeType, cancellationToken);
        return MergeWithParent(scopeInfo, parentInfo);
    }

    /// <inheritdoc />
    public Task<bool> CanExtendAsync(
        Guid scopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken)
    {
        // A scope can be extended if:
        // 1. It exists
        // 2. It doesn't already have a parent (single-level inheritance)
        return ValidateParentAsync(scopeId, scopeType, cancellationToken)
            .ContinueWith(t => t.Result.IsValid);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScopeChildInfo>> GetChildrenAsync(
        Guid parentScopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Loading children of {ScopeType} scope {ParentId}",
            scopeType,
            parentScopeId);

        // In full implementation, query Dataverse for scopes with parentid = parentScopeId
        // For now, return empty list
        return Array.Empty<ScopeChildInfo>();
    }

    /// <inheritdoc />
    public async Task HandleParentDeletionAsync(
        Guid parentScopeId,
        ScopeType scopeType,
        ParentDeletionStrategy strategy,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling deletion of parent {ScopeType} scope {ParentId} with strategy {Strategy}",
            scopeType,
            parentScopeId,
            strategy);

        var children = await GetChildrenAsync(parentScopeId, scopeType, cancellationToken);

        if (children.Count == 0)
        {
            return; // No children to handle
        }

        switch (strategy)
        {
            case ParentDeletionStrategy.PromoteChildren:
                // Remove parent link, making children standalone
                foreach (var child in children)
                {
                    await PromoteToStandaloneAsync(child.ScopeId, scopeType, cancellationToken);
                }
                break;

            case ParentDeletionStrategy.DeleteChildren:
                // Delete all children (cascade)
                foreach (var child in children)
                {
                    await DeleteScopeAsync(child.ScopeId, scopeType, cancellationToken);
                }
                break;

            case ParentDeletionStrategy.PreventDeletion:
                throw new InvalidOperationException(
                    $"Cannot delete {scopeType} scope {parentScopeId} because it has {children.Count} child scope(s). " +
                    "Delete or reassign children first.");
        }
    }

    /// <summary>
    /// Validate that a scope can be used as a parent.
    /// </summary>
    private async Task<(bool IsValid, string? ErrorMessage)> ValidateParentAsync(
        Guid parentScopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken)
    {
        var scopeInfo = await LoadScopeInfoAsync(parentScopeId, scopeType, cancellationToken);

        if (scopeInfo == null)
        {
            return (false, $"{scopeType} scope {parentScopeId} not found.");
        }

        // Check single-level inheritance rule
        if (scopeInfo.ParentId != null)
        {
            return (false, $"Cannot extend scope {parentScopeId} because it already extends another scope. Only single-level inheritance is supported.");
        }

        return (true, null);
    }

    /// <summary>
    /// Create a child scope with parent link.
    /// </summary>
    private Task<ExtendScopeResult> CreateChildScopeAsync(
        Guid parentScopeId,
        ScopeType scopeType,
        string logicalName,
        string displayName,
        Dictionary<string, object>? initialOverrides,
        CancellationToken cancellationToken)
    {
        // In full implementation:
        // 1. Create new scope record in Dataverse
        // 2. Set parentid lookup to parentScopeId
        // 3. Set ownershiptype to Customer
        // 4. Store initial overrides in override tracking fields

        var overriddenFields = initialOverrides?.Keys.ToList() ?? new List<string>();

        return Task.FromResult(new ExtendScopeResult
        {
            Success = true,
            ChildId = Guid.NewGuid(),
            ChildName = logicalName,
            ChildDisplayName = displayName,
            ParentId = parentScopeId,
            OverriddenFields = overriddenFields
        });
    }

    /// <summary>
    /// Load scope information including parent link.
    /// </summary>
    private Task<ScopeInfo> LoadScopeInfoAsync(
        Guid scopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken)
    {
        // In full implementation, load from Dataverse
        // For now, return stub data
        return Task.FromResult(new ScopeInfo
        {
            ScopeId = scopeId,
            ScopeType = scopeType,
            ParentId = null,
            Values = new Dictionary<string, object>
            {
                ["name"] = "Stub Scope",
                ["description"] = "Stub description"
            },
            OverriddenFields = new HashSet<string>()
        });
    }

    /// <summary>
    /// Merge child scope values with parent values.
    /// </summary>
    private static EffectiveScopeData MergeWithParent(ScopeInfo child, ScopeInfo parent)
    {
        var effectiveValues = new Dictionary<string, object>();
        var inheritedFields = new List<string>();
        var overriddenFields = new List<string>();

        // Start with parent values
        foreach (var (key, value) in parent.Values)
        {
            if (child.OverriddenFields.Contains(key))
            {
                // Use child's override
                effectiveValues[key] = child.Values.GetValueOrDefault(key, value);
                overriddenFields.Add(key);
            }
            else
            {
                // Inherit from parent
                effectiveValues[key] = value;
                inheritedFields.Add(key);
            }
        }

        // Add any child-only fields
        foreach (var (key, value) in child.Values)
        {
            if (!effectiveValues.ContainsKey(key))
            {
                effectiveValues[key] = value;
                overriddenFields.Add(key);
            }
        }

        return new EffectiveScopeData
        {
            ScopeId = child.ScopeId,
            ParentId = parent.ScopeId,
            Values = effectiveValues,
            OverriddenFields = overriddenFields,
            InheritedFields = inheritedFields
        };
    }

    /// <summary>
    /// Promote a child scope to standalone (remove parent link).
    /// </summary>
    private Task PromoteToStandaloneAsync(
        Guid scopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Promoting {ScopeType} scope {ScopeId} to standalone",
            scopeType,
            scopeId);

        // In full implementation:
        // 1. Load child scope and its effective values
        // 2. Copy inherited values to child's own fields
        // 3. Clear parent link
        // 4. Clear override tracking

        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete a scope.
    /// </summary>
    private Task DeleteScopeAsync(
        Guid scopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Deleting {ScopeType} scope {ScopeId}",
            scopeType,
            scopeId);

        // In full implementation, delete from Dataverse
        return Task.CompletedTask;
    }

    /// <summary>
    /// Generate a logical name from display name.
    /// </summary>
    private static string GenerateLogicalName(string displayName)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            displayName.ToLowerInvariant(),
            @"[^a-z0-9\s-]",
            "");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", "-");
        return normalized.Trim('-');
    }
}

/// <summary>
/// Interface for scope inheritance operations.
/// </summary>
public interface IScopeInheritanceService
{
    /// <summary>
    /// Create a child scope that extends a parent scope.
    /// </summary>
    /// <param name="parentScopeId">Parent scope ID to extend.</param>
    /// <param name="scopeType">Type of scope.</param>
    /// <param name="childDisplayName">Display name for the child scope.</param>
    /// <param name="initialOverrides">Optional initial field overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with child scope details.</returns>
    Task<ExtendScopeResult> ExtendScopeAsync(
        Guid parentScopeId,
        ScopeType scopeType,
        string childDisplayName,
        Dictionary<string, object>? initialOverrides,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get effective scope values with inheritance applied.
    /// </summary>
    /// <param name="scopeId">Scope ID.</param>
    /// <param name="scopeType">Type of scope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Effective values with inheritance merged.</returns>
    Task<EffectiveScopeData> GetEffectiveScopeAsync(
        Guid scopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Check if a scope can be extended (not already a child).
    /// </summary>
    Task<bool> CanExtendAsync(
        Guid scopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get all child scopes of a parent.
    /// </summary>
    Task<IReadOnlyList<ScopeChildInfo>> GetChildrenAsync(
        Guid parentScopeId,
        ScopeType scopeType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Handle parent scope deletion.
    /// </summary>
    Task HandleParentDeletionAsync(
        Guid parentScopeId,
        ScopeType scopeType,
        ParentDeletionStrategy strategy,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of extending a scope.
/// </summary>
public record ExtendScopeResult
{
    /// <summary>Whether the extension succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>ID of the newly created child scope.</summary>
    public Guid ChildId { get; init; }

    /// <summary>Logical name of the child scope.</summary>
    public string ChildName { get; init; } = string.Empty;

    /// <summary>Display name of the child scope.</summary>
    public string ChildDisplayName { get; init; } = string.Empty;

    /// <summary>ID of the parent scope.</summary>
    public Guid ParentId { get; init; }

    /// <summary>Fields that are initially overridden.</summary>
    public IReadOnlyList<string> OverriddenFields { get; init; } = Array.Empty<string>();

    /// <summary>Error message if extension failed.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Effective scope data with inheritance applied.
/// </summary>
public record EffectiveScopeData
{
    /// <summary>Scope ID.</summary>
    public Guid ScopeId { get; init; }

    /// <summary>Parent scope ID if any.</summary>
    public Guid? ParentId { get; init; }

    /// <summary>Effective field values.</summary>
    public Dictionary<string, object> Values { get; init; } = new();

    /// <summary>Fields that are overridden in this scope.</summary>
    public IReadOnlyList<string> OverriddenFields { get; init; } = Array.Empty<string>();

    /// <summary>Fields that are inherited from parent.</summary>
    public IReadOnlyList<string> InheritedFields { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Information about a child scope.
/// </summary>
public record ScopeChildInfo
{
    /// <summary>Child scope ID.</summary>
    public Guid ScopeId { get; init; }

    /// <summary>Child scope name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Child scope display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// Strategy for handling parent scope deletion.
/// </summary>
public enum ParentDeletionStrategy
{
    /// <summary>Promote children to standalone scopes.</summary>
    PromoteChildren,

    /// <summary>Delete children along with parent (cascade).</summary>
    DeleteChildren,

    /// <summary>Prevent deletion if children exist.</summary>
    PreventDeletion
}

/// <summary>
/// Internal scope information for inheritance operations.
/// </summary>
internal record ScopeInfo
{
    public Guid ScopeId { get; init; }
    public ScopeType ScopeType { get; init; }
    public Guid? ParentId { get; init; }
    public Dictionary<string, object> Values { get; init; } = new();
    public HashSet<string> OverriddenFields { get; init; } = new();
}
