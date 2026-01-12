using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for conditional branching based on expression evaluation.
/// Uses JSON condition expressions to determine execution paths.
/// </summary>
/// <remarks>
/// <para>
/// Condition configuration is read from node.ConfigJson with structure:
/// </para>
/// <code>
/// {
///   "condition": {
///     "operator": "eq",           // Comparison: eq, ne, gt, lt, gte, lte, contains, startsWith, endsWith
///     "left": "{{analysis.output.riskLevel}}",
///     "right": "high"
///   },
///   "trueBranch": "highRiskPath",  // Node to enable when true
///   "falseBranch": "normalPath"    // Node to enable when false
/// }
/// </code>
/// <para>
/// For logical operators (and, or), use nested conditions:
/// </para>
/// <code>
/// {
///   "condition": {
///     "operator": "and",
///     "conditions": [
///       { "operator": "eq", "left": "{{a.output.x}}", "right": "y" },
///       { "operator": "gt", "left": "{{a.output.score}}", "right": 0.5 }
///     ]
///   },
///   "trueBranch": "path1",
///   "falseBranch": "path2"
/// }
/// </code>
/// </remarks>
public sealed class ConditionNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ITemplateEngine _templateEngine;
    private readonly ILogger<ConditionNodeExecutor> _logger;

    public ConditionNodeExecutor(
        ITemplateEngine templateEngine,
        ILogger<ConditionNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.Condition
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("Condition node requires configuration (ConfigJson)");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        try
        {
            var config = JsonSerializer.Deserialize<ConditionNodeConfig>(context.Node.ConfigJson, JsonOptions);
            if (config is null)
            {
                errors.Add("Failed to parse condition configuration");
            }
            else
            {
                if (config.Condition is null)
                {
                    errors.Add("Condition expression is required");
                }
                else
                {
                    var conditionErrors = ValidateConditionExpression(config.Condition, "condition");
                    errors.AddRange(conditionErrors);
                }

                if (string.IsNullOrWhiteSpace(config.TrueBranch) && string.IsNullOrWhiteSpace(config.FalseBranch))
                {
                    errors.Add("At least one branch (trueBranch or falseBranch) must be specified");
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid condition configuration JSON: {ex.Message}");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <summary>
    /// Validates a condition expression recursively.
    /// </summary>
    private static List<string> ValidateConditionExpression(ConditionExpression condition, string path)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(condition.Operator))
        {
            errors.Add($"{path}: operator is required");
            return errors;
        }

        var op = condition.Operator.ToLowerInvariant();

        // Logical operators require nested conditions
        if (op is "and" or "or")
        {
            if (condition.Conditions is null || condition.Conditions.Count < 2)
            {
                errors.Add($"{path}: '{op}' operator requires at least 2 conditions");
            }
            else
            {
                for (int i = 0; i < condition.Conditions.Count; i++)
                {
                    errors.AddRange(ValidateConditionExpression(condition.Conditions[i], $"{path}.conditions[{i}]"));
                }
            }
        }
        else if (op == "not")
        {
            if (condition.Condition is null)
            {
                errors.Add($"{path}: 'not' operator requires a nested condition");
            }
            else
            {
                errors.AddRange(ValidateConditionExpression(condition.Condition, $"{path}.condition"));
            }
        }
        else
        {
            // Comparison operators require left operand (right can be null for exists checks)
            var validOps = new[] { "eq", "ne", "gt", "lt", "gte", "lte", "contains", "startswith", "endswith", "exists" };
            if (!validOps.Contains(op))
            {
                errors.Add($"{path}: unknown operator '{condition.Operator}'. Valid operators: {string.Join(", ", validOps)}, and, or, not");
            }

            if (string.IsNullOrWhiteSpace(condition.Left))
            {
                errors.Add($"{path}: 'left' operand is required for '{op}' operator");
            }
        }

        return errors;
    }

    /// <inheritdoc />
    public Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogDebug(
            "Executing Condition node {NodeId} ({NodeName})",
            context.Node.Id,
            context.Node.Name);

        return Task.FromResult(ExecuteInternal(context, startedAt));
    }

    /// <summary>
    /// Internal synchronous execution logic for condition evaluation.
    /// </summary>
    private NodeOutput ExecuteInternal(NodeExecutionContext context, DateTimeOffset startedAt)
    {
        try
        {
            // Validate first
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Parse configuration
            var config = JsonSerializer.Deserialize<ConditionNodeConfig>(context.Node.ConfigJson!, JsonOptions)!;

            // Build template context from previous outputs
            var templateContext = BuildTemplateContext(context);

            // Evaluate the condition
            var result = EvaluateCondition(config.Condition!, templateContext);

            _logger.LogDebug(
                "Condition node {NodeId} evaluated to {Result}",
                context.Node.Id,
                result);

            // Determine which branch to take
            var selectedBranch = result ? config.TrueBranch : config.FalseBranch;

            _logger.LogInformation(
                "Condition node {NodeId} completed - result: {Result}, branch: {Branch}",
                context.Node.Id,
                result,
                selectedBranch ?? "(none)");

            // Return result with branch decision
            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new ConditionResult
                {
                    Result = result,
                    SelectedBranch = selectedBranch,
                    TrueBranch = config.TrueBranch,
                    FalseBranch = config.FalseBranch
                },
                textContent: $"Condition evaluated to {result}, selected branch: {selectedBranch ?? "none"}",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Condition node {NodeId} failed: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Failed to evaluate condition: {ex.Message}",
                NodeErrorCodes.ConditionError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Evaluates a condition expression against the template context.
    /// </summary>
    private bool EvaluateCondition(ConditionExpression condition, Dictionary<string, object?> templateContext)
    {
        var op = condition.Operator!.ToLowerInvariant();

        return op switch
        {
            "and" => condition.Conditions!.All(c => EvaluateCondition(c, templateContext)),
            "or" => condition.Conditions!.Any(c => EvaluateCondition(c, templateContext)),
            "not" => !EvaluateCondition(condition.Condition!, templateContext),
            _ => EvaluateComparison(condition, templateContext)
        };
    }

    /// <summary>
    /// Evaluates a comparison condition.
    /// </summary>
    private bool EvaluateComparison(ConditionExpression condition, Dictionary<string, object?> templateContext)
    {
        var op = condition.Operator!.ToLowerInvariant();

        // Resolve left operand (always a template expression)
        var leftValue = ResolveOperand(condition.Left!, templateContext);

        // Handle exists operator (checks if value is non-null/non-empty)
        if (op == "exists")
        {
            return leftValue is not null && !string.IsNullOrEmpty(leftValue.ToString());
        }

        // Resolve right operand
        var rightValue = ResolveOperand(condition.Right, templateContext);

        // Compare values
        return op switch
        {
            "eq" => AreEqual(leftValue, rightValue),
            "ne" => !AreEqual(leftValue, rightValue),
            "gt" => CompareNumeric(leftValue, rightValue) > 0,
            "lt" => CompareNumeric(leftValue, rightValue) < 0,
            "gte" => CompareNumeric(leftValue, rightValue) >= 0,
            "lte" => CompareNumeric(leftValue, rightValue) <= 0,
            "contains" => StringContains(leftValue, rightValue),
            "startswith" => StringStartsWith(leftValue, rightValue),
            "endswith" => StringEndsWith(leftValue, rightValue),
            _ => throw new InvalidOperationException($"Unknown operator: {op}")
        };
    }

    /// <summary>
    /// Resolves an operand value, rendering templates if needed.
    /// </summary>
    private object? ResolveOperand(object? operand, Dictionary<string, object?> templateContext)
    {
        if (operand is null)
            return null;

        if (operand is string strOperand)
        {
            // Check if it's a template expression
            if (_templateEngine.HasVariables(strOperand))
            {
                var rendered = _templateEngine.Render(strOperand, templateContext);
                // Try to parse as number or boolean
                return ParseValue(rendered);
            }
            return strOperand;
        }

        // JsonElement from deserialization
        if (operand is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetDouble(out var d) ? d : element.GetInt64(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        return operand;
    }

    /// <summary>
    /// Parses a string value into appropriate type (number, boolean, or string).
    /// </summary>
    private static object? ParseValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        // Try boolean
        if (bool.TryParse(value, out var boolResult))
            return boolResult;

        // Try number
        if (double.TryParse(value, out var doubleResult))
            return doubleResult;

        return value;
    }

    /// <summary>
    /// Compares two values for equality with type coercion.
    /// </summary>
    private static bool AreEqual(object? left, object? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;

        // Compare as strings if either is string
        if (left is string || right is string)
            return string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);

        // Compare as numbers if both can be converted
        if (TryGetDouble(left, out var leftNum) && TryGetDouble(right, out var rightNum))
            return Math.Abs(leftNum - rightNum) < 0.0001;

        // Compare as booleans
        if (left is bool leftBool && right is bool rightBool)
            return leftBool == rightBool;

        return Equals(left, right);
    }

    /// <summary>
    /// Compares two values numerically.
    /// </summary>
    private static int CompareNumeric(object? left, object? right)
    {
        if (!TryGetDouble(left, out var leftNum))
            throw new InvalidOperationException($"Cannot compare non-numeric value: {left}");

        if (!TryGetDouble(right, out var rightNum))
            throw new InvalidOperationException($"Cannot compare non-numeric value: {right}");

        return leftNum.CompareTo(rightNum);
    }

    private static bool TryGetDouble(object? value, out double result)
    {
        result = 0;
        if (value is null)
            return false;

        if (value is double d)
        {
            result = d;
            return true;
        }

        if (value is int i)
        {
            result = i;
            return true;
        }

        if (value is long l)
        {
            result = l;
            return true;
        }

        if (value is string s && double.TryParse(s, out var parsed))
        {
            result = parsed;
            return true;
        }

        return false;
    }

    private static bool StringContains(object? left, object? right)
    {
        var leftStr = left?.ToString() ?? string.Empty;
        var rightStr = right?.ToString() ?? string.Empty;
        return leftStr.Contains(rightStr, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StringStartsWith(object? left, object? right)
    {
        var leftStr = left?.ToString() ?? string.Empty;
        var rightStr = right?.ToString() ?? string.Empty;
        return leftStr.StartsWith(rightStr, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StringEndsWith(object? left, object? right)
    {
        var leftStr = left?.ToString() ?? string.Empty;
        var rightStr = right?.ToString() ?? string.Empty;
        return leftStr.EndsWith(rightStr, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds template context dictionary from previous node outputs.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            // Add the entire output as a nested object for template access
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? JsonSerializer.Deserialize<object>(output.StructuredData.Value.GetRawText())
                    : null,
                text = output.TextContent,
                success = output.Success,
                confidence = output.Confidence
            };
        }

        return templateContext;
    }
}

/// <summary>
/// Configuration for Condition node from ConfigJson.
/// </summary>
internal sealed record ConditionNodeConfig
{
    /// <summary>
    /// The condition expression to evaluate.
    /// </summary>
    public ConditionExpression? Condition { get; init; }

    /// <summary>
    /// Node output variable to enable when condition is true.
    /// </summary>
    public string? TrueBranch { get; init; }

    /// <summary>
    /// Node output variable to enable when condition is false.
    /// </summary>
    public string? FalseBranch { get; init; }
}

/// <summary>
/// Represents a condition expression for evaluation.
/// </summary>
internal sealed record ConditionExpression
{
    /// <summary>
    /// Operator: eq, ne, gt, lt, gte, lte, contains, startsWith, endsWith, exists, and, or, not.
    /// </summary>
    public string? Operator { get; init; }

    /// <summary>
    /// Left operand (template expression like "{{node.output.value}}").
    /// Required for comparison operators.
    /// </summary>
    public string? Left { get; init; }

    /// <summary>
    /// Right operand (literal value or template expression).
    /// Required for comparison operators except 'exists'.
    /// </summary>
    public object? Right { get; init; }

    /// <summary>
    /// Nested conditions for 'and'/'or' operators.
    /// </summary>
    public List<ConditionExpression>? Conditions { get; init; }

    /// <summary>
    /// Single nested condition for 'not' operator.
    /// </summary>
    public ConditionExpression? Condition { get; init; }
}

/// <summary>
/// Result of condition evaluation for downstream processing.
/// </summary>
public sealed record ConditionResult
{
    /// <summary>
    /// Whether the condition evaluated to true.
    /// </summary>
    public bool Result { get; init; }

    /// <summary>
    /// The selected branch based on the result.
    /// </summary>
    public string? SelectedBranch { get; init; }

    /// <summary>
    /// The configured true branch (for orchestrator reference).
    /// </summary>
    public string? TrueBranch { get; init; }

    /// <summary>
    /// The configured false branch (for orchestrator reference).
    /// </summary>
    public string? FalseBranch { get; init; }
}
