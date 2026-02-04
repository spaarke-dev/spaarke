using Sprk.Bff.Api.Api.FieldMappings.Dtos;

namespace Sprk.Bff.Api.Api.FieldMappings;

/// <summary>
/// Validates type compatibility between field mapping source and target types.
/// Mirrors the TypeScript STRICT_TYPE_COMPATIBILITY matrix from FieldMappingTypes.ts.
/// </summary>
/// <remarks>
/// Type compatibility rules (Strict mode):
/// - Lookup   -> [Lookup, Text]
/// - Text     -> [Text, Memo]
/// - Memo     -> [Text, Memo]
/// - OptionSet-> [OptionSet, Text]
/// - Number   -> [Number, Text]
/// - DateTime -> [DateTime, Text]
/// - Boolean  -> [Boolean, Text]
/// </remarks>
public static class TypeCompatibilityValidator
{
    /// <summary>
    /// Strict compatibility matrix. Key is source type, value is array of compatible target types.
    /// Mirrors TypeScript: STRICT_TYPE_COMPATIBILITY from FieldMappingTypes.ts
    /// </summary>
    private static readonly Dictionary<string, string[]> StrictCompatible = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Lookup"] = ["Lookup", "Text"],
        ["Text"] = ["Text", "Memo"],
        ["Memo"] = ["Text", "Memo"],
        ["OptionSet"] = ["OptionSet", "Text"],
        ["Number"] = ["Number", "Text"],
        ["DateTime"] = ["DateTime", "Text"],
        ["Boolean"] = ["Boolean", "Text"]
    };

    /// <summary>
    /// All known field types for validation.
    /// </summary>
    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lookup", "Text", "Memo", "OptionSet", "Number", "DateTime", "Boolean"
    };

    /// <summary>
    /// Compatibility levels returned in the response.
    /// </summary>
    private static class CompatibilityLevel
    {
        public const string Exact = "exact";
        public const string SafeConversion = "safe_conversion";
        public const string Incompatible = "incompatible";
    }

    /// <summary>
    /// Validates type compatibility between source and target field types.
    /// </summary>
    /// <param name="sourceType">Source field type name.</param>
    /// <param name="targetType">Target field type name.</param>
    /// <returns>Validation result with isValid, errors, warnings, and compatible types.</returns>
    public static ValidateMappingResponse Validate(string sourceType, string targetType)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Normalize and validate source type
        var normalizedSource = NormalizeTypeName(sourceType);
        var normalizedTarget = NormalizeTypeName(targetType);

        // Validate that types are known
        if (!KnownTypes.Contains(normalizedSource))
        {
            errors.Add($"Unknown source field type: '{sourceType}'. Valid types are: {string.Join(", ", KnownTypes)}");
        }

        if (!KnownTypes.Contains(normalizedTarget))
        {
            errors.Add($"Unknown target field type: '{targetType}'. Valid types are: {string.Join(", ", KnownTypes)}");
        }

        // If types are unknown, return early
        if (errors.Count > 0)
        {
            return new ValidateMappingResponse
            {
                IsValid = false,
                Errors = [.. errors],
                Warnings = [],
                CompatibleTargetTypes = [],
                CompatibilityLevel = CompatibilityLevel.Incompatible
            };
        }

        // Get compatible target types for this source
        var compatibleTypes = GetCompatibleTargetTypes(normalizedSource);

        // Check exact match
        if (string.Equals(normalizedSource, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return new ValidateMappingResponse
            {
                IsValid = true,
                Errors = [],
                Warnings = [],
                CompatibleTargetTypes = compatibleTypes,
                CompatibilityLevel = CompatibilityLevel.Exact
            };
        }

        // Check if target is in compatible list
        var isCompatible = StrictCompatible.TryGetValue(normalizedSource, out var allowedTargets)
            && allowedTargets.Any(t => string.Equals(t, normalizedTarget, StringComparison.OrdinalIgnoreCase));

        if (isCompatible)
        {
            // Add warning for conversion to Text (lossy conversion)
            if (string.Equals(normalizedTarget, "Text", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedSource, "Text", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedSource, "Memo", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Converting {normalizedSource} to Text - value will be formatted as string.");
            }

            return new ValidateMappingResponse
            {
                IsValid = true,
                Errors = [],
                Warnings = [.. warnings],
                CompatibleTargetTypes = compatibleTypes,
                CompatibilityLevel = CompatibilityLevel.SafeConversion
            };
        }

        // Incompatible
        errors.Add(
            $"Cannot convert {normalizedSource} to {normalizedTarget} in Strict mode. " +
            $"Compatible target types for {normalizedSource}: {string.Join(", ", compatibleTypes)}.");

        return new ValidateMappingResponse
        {
            IsValid = false,
            Errors = [.. errors],
            Warnings = [],
            CompatibleTargetTypes = compatibleTypes,
            CompatibilityLevel = CompatibilityLevel.Incompatible
        };
    }

    /// <summary>
    /// Gets the list of compatible target types for a given source type.
    /// </summary>
    /// <param name="sourceType">Source field type name.</param>
    /// <returns>Array of compatible target type names.</returns>
    public static string[] GetCompatibleTargetTypes(string sourceType)
    {
        var normalized = NormalizeTypeName(sourceType);

        if (!StrictCompatible.TryGetValue(normalized, out var compatibleTypes))
        {
            return [];
        }

        // Return distinct list (includes exact match)
        return compatibleTypes
            .Select(NormalizeTypeName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Normalizes a type name to consistent casing.
    /// </summary>
    private static string NormalizeTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return typeName;
        }

        // Normalize to the canonical casing from KnownTypes
        var match = KnownTypes.FirstOrDefault(t =>
            string.Equals(t, typeName.Trim(), StringComparison.OrdinalIgnoreCase));

        return match ?? typeName.Trim();
    }
}
