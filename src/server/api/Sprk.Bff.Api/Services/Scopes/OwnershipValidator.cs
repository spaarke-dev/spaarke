using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Scopes;

/// <summary>
/// Validates ownership rules for scope and playbook entities.
/// Implements the immutability rules for SYS- prefixed scopes.
/// </summary>
/// <remarks>
/// <para>
/// Ownership Model:
/// - SYS- prefix: System-provided scopes that are immutable (read-only)
/// - CUST- prefix: Customer-created scopes that are fully editable
/// </para>
/// <para>
/// Rules:
/// - SYS- scopes cannot be modified (update/delete blocked)
/// - SYS- scopes can be read and used in playbooks
/// - SYS- scopes can be used as base for "Save As" (creates CUST- copy)
/// - CUST- scopes can be created, updated, deleted by their owners
/// </para>
/// </remarks>
public sealed class OwnershipValidator : IOwnershipValidator
{
    private const string SystemPrefix = "SYS-";
    private const string CustomerPrefix = "CUST-";

    private readonly ILogger<OwnershipValidator> _logger;

    public OwnershipValidator(ILogger<OwnershipValidator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsSystemScope(string name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
               name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsCustomerScope(string name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
               name.StartsWith(CustomerPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsImmutable(string name)
    {
        // System scopes are immutable
        return IsSystemScope(name);
    }

    /// <inheritdoc />
    public bool CanModify(string name)
    {
        // Customer scopes can be modified
        return IsCustomerScope(name) || !IsSystemScope(name);
    }

    /// <inheritdoc />
    public bool CanDelete(string name)
    {
        // Only customer scopes can be deleted
        return IsCustomerScope(name);
    }

    /// <inheritdoc />
    public OwnershipValidationResult ValidateModification(
        string scopeName,
        string scopeType,
        OwnershipOperation operation)
    {
        if (string.IsNullOrWhiteSpace(scopeName))
        {
            return OwnershipValidationResult.Failure(
                "Scope name cannot be empty.",
                "INVALID_SCOPE_NAME");
        }

        if (IsSystemScope(scopeName))
        {
            var operationName = operation switch
            {
                OwnershipOperation.Update => "modify",
                OwnershipOperation.Delete => "delete",
                OwnershipOperation.Rename => "rename",
                _ => "modify"
            };

            _logger.LogWarning(
                "Blocked attempt to {Operation} system scope '{ScopeName}' of type '{ScopeType}'",
                operationName, scopeName, scopeType);

            return OwnershipValidationResult.Failure(
                $"Cannot {operationName} system scope '{scopeName}'. System scopes (SYS- prefix) are immutable. " +
                "Use 'Save As' to create an editable copy.",
                "SYSTEM_SCOPE_IMMUTABLE");
        }

        return OwnershipValidationResult.Success();
    }

    /// <inheritdoc />
    public OwnershipValidationResult ValidateCreation(string name, string scopeType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OwnershipValidationResult.Failure(
                "Scope name cannot be empty.",
                "INVALID_SCOPE_NAME");
        }

        if (name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Blocked attempt to create scope with system prefix: '{Name}' (type: {ScopeType})",
                name, scopeType);

            return OwnershipValidationResult.Failure(
                $"Cannot create scope with system prefix '{SystemPrefix}'. " +
                "Only system-provided scopes can have this prefix.",
                "INVALID_SCOPE_PREFIX");
        }

        return OwnershipValidationResult.Success();
    }

    /// <inheritdoc />
    public string EnsureCustomerPrefix(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        if (name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot add customer prefix to a system scope name: {name}");
        }

        if (name.StartsWith(CustomerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return $"{CustomerPrefix}{name}";
    }

    /// <inheritdoc />
    public string StripPrefix(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name[SystemPrefix.Length..];
        }

        if (name.StartsWith(CustomerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name[CustomerPrefix.Length..];
        }

        return name;
    }

    /// <inheritdoc />
    public OwnershipType GetOwnershipType(string name)
    {
        if (IsSystemScope(name))
        {
            return OwnershipType.System;
        }

        return OwnershipType.Customer;
    }

    /// <inheritdoc />
    public ProblemDetails CreateProblemDetails(OwnershipValidationResult result, string? instance = null)
    {
        if (result.IsValid)
        {
            throw new InvalidOperationException("Cannot create ProblemDetails for a valid result.");
        }

        return new ProblemDetails
        {
            Type = "https://spaarke.com/problems/ownership-validation",
            Title = "Ownership Validation Failed",
            Status = StatusCodes.Status403Forbidden,
            Detail = result.ErrorMessage,
            Instance = instance,
            Extensions =
            {
                ["errorCode"] = result.ErrorCode
            }
        };
    }
}

/// <summary>
/// Interface for ownership validation operations.
/// </summary>
public interface IOwnershipValidator
{
    /// <summary>
    /// Checks if a scope name indicates a system-owned scope.
    /// </summary>
    bool IsSystemScope(string name);

    /// <summary>
    /// Checks if a scope name indicates a customer-owned scope.
    /// </summary>
    bool IsCustomerScope(string name);

    /// <summary>
    /// Checks if a scope is immutable based on its name.
    /// </summary>
    bool IsImmutable(string name);

    /// <summary>
    /// Checks if a scope can be modified (update) based on ownership.
    /// </summary>
    bool CanModify(string name);

    /// <summary>
    /// Checks if a scope can be deleted based on ownership.
    /// </summary>
    bool CanDelete(string name);

    /// <summary>
    /// Validates that a modification operation is allowed.
    /// Returns a result with error details if validation fails.
    /// </summary>
    OwnershipValidationResult ValidateModification(
        string scopeName,
        string scopeType,
        OwnershipOperation operation);

    /// <summary>
    /// Validates that a creation operation is allowed.
    /// </summary>
    OwnershipValidationResult ValidateCreation(string name, string scopeType);

    /// <summary>
    /// Ensures a name has the CUST- prefix for customer scopes.
    /// Throws if the name has SYS- prefix.
    /// </summary>
    string EnsureCustomerPrefix(string name);

    /// <summary>
    /// Removes ownership prefix (SYS- or CUST-) from a name.
    /// </summary>
    string StripPrefix(string name);

    /// <summary>
    /// Determines the ownership type based on scope name.
    /// </summary>
    OwnershipType GetOwnershipType(string name);

    /// <summary>
    /// Creates a ProblemDetails response for a failed validation.
    /// </summary>
    ProblemDetails CreateProblemDetails(OwnershipValidationResult result, string? instance = null);
}

/// <summary>
/// Operations that modify scope data.
/// </summary>
public enum OwnershipOperation
{
    /// <summary>Update scope data.</summary>
    Update,

    /// <summary>Delete scope.</summary>
    Delete,

    /// <summary>Rename scope.</summary>
    Rename
}

/// <summary>
/// Ownership type enumeration.
/// </summary>
public enum OwnershipType
{
    /// <summary>System-provided scope (immutable).</summary>
    System,

    /// <summary>Customer-created scope (editable).</summary>
    Customer
}

/// <summary>
/// Result of ownership validation.
/// </summary>
public record OwnershipValidationResult
{
    /// <summary>
    /// Whether the validation passed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static OwnershipValidationResult Success() =>
        new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static OwnershipValidationResult Failure(string message, string errorCode) =>
        new() { IsValid = false, ErrorMessage = message, ErrorCode = errorCode };
}
