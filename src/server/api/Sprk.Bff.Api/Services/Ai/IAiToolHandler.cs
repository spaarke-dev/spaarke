namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Interface for AI tool handlers called by playbook workflows.
/// Simpler than IAnalysisToolHandler - used for playbook-driven orchestration, not interactive document analysis.
/// </summary>
/// <remarks>
/// Tool handlers are called by playbooks during workflow execution.
/// Examples: FinancialCalculationToolHandler, DataverseUpdateToolHandler, InvoiceExtractionToolHandler
///
/// Per ADR-013: AI extends BFF API pattern. Playbooks orchestrate tool handlers for complex workflows.
/// </remarks>
public interface IAiToolHandler
{
    /// <summary>
    /// Gets the unique tool name that identifies this handler.
    /// Used by playbooks to invoke the correct handler.
    /// </summary>
    string ToolName { get; }

    /// <summary>
    /// Executes the tool handler with the provided parameters.
    /// </summary>
    Task<PlaybookToolResult> ExecuteAsync(ToolParameters parameters, CancellationToken ct);
}

/// <summary>
/// Parameter bag for tool handler execution.
/// </summary>
public class ToolParameters
{
    private readonly Dictionary<string, object> _parameters;

    public ToolParameters(Dictionary<string, object> parameters)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    public string GetString(string key)
    {
        if (!_parameters.TryGetValue(key, out var value))
            throw new ArgumentException($"Parameter '{key}' not found");
        return value?.ToString() ?? string.Empty;
    }

    public Guid GetGuid(string key)
    {
        if (!_parameters.TryGetValue(key, out var value))
            throw new ArgumentException($"Parameter '{key}' not found");

        if (value is Guid guidValue)
            return guidValue;

        if (value is string stringValue && Guid.TryParse(stringValue, out var parsedGuid))
            return parsedGuid;

        throw new ArgumentException($"Parameter '{key}' is not a valid Guid");
    }

    public bool TryGetGuid(string key, out Guid value)
    {
        value = Guid.Empty;

        if (!_parameters.TryGetValue(key, out var paramValue))
            return false;

        if (paramValue is Guid guidValue)
        {
            value = guidValue;
            return true;
        }

        if (paramValue is string stringValue && Guid.TryParse(stringValue, out var parsedGuid))
        {
            value = parsedGuid;
            return true;
        }

        return false;
    }

    public decimal GetDecimal(string key)
    {
        if (!_parameters.TryGetValue(key, out var value))
            throw new ArgumentException($"Parameter '{key}' not found");

        if (value is decimal decimalValue)
            return decimalValue;

        if (value is int intValue)
            return intValue;

        if (value is double doubleValue)
            return (decimal)doubleValue;

        if (value is string stringValue && decimal.TryParse(stringValue, out var parsedDecimal))
            return parsedDecimal;

        throw new ArgumentException($"Parameter '{key}' is not a valid decimal");
    }

    public Dictionary<string, object> GetDictionary(string key)
    {
        if (!_parameters.TryGetValue(key, out var value))
            throw new ArgumentException($"Parameter '{key}' not found");

        if (value is Dictionary<string, object> dict)
            return dict;

        throw new ArgumentException($"Parameter '{key}' is not a dictionary");
    }

    public int GetInt32(string key)
    {
        if (!_parameters.TryGetValue(key, out var value))
            throw new ArgumentException($"Parameter '{key}' not found");

        if (value is int intValue)
            return intValue;

        if (value is string stringValue && int.TryParse(stringValue, out var parsedInt))
            return parsedInt;

        throw new ArgumentException($"Parameter '{key}' is not a valid integer");
    }

    public bool TryGetValue<T>(string key, out T? value)
    {
        if (_parameters.TryGetValue(key, out var rawValue) && rawValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }
}

/// <summary>
/// Result of playbook tool handler execution.
/// </summary>
public record PlaybookToolResult
{
    public bool Success { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }

    public static PlaybookToolResult CreateSuccess(object? data = null) => new()
    {
        Success = true,
        Data = data
    };

    public static PlaybookToolResult CreateError(string error) => new()
    {
        Success = false,
        Error = error
    };
}
