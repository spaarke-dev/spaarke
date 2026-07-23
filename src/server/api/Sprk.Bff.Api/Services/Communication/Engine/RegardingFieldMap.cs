namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Single source of truth mapping a Dataverse entity logical name → its <c>sprk_communication</c>
/// regarding lookup field (ADR-024 polymorphic regarding family). Used by the rungs to write typed
/// regarding lookups for caller-supplied and thread-inherited associations across all supported
/// targets. Mirrors the priority list the engine already uses when picking the primary regarding
/// entity for the denormalized resolver fields.
/// </summary>
public static class RegardingFieldMap
{
    /// <summary>Entity logical name → regarding lookup field, in ADR-024 priority order.</summary>
    public static readonly IReadOnlyList<(string EntityLogicalName, string RegardingField)> All =
    [
        ("sprk_matter", "sprk_regardingmatter"),
        ("sprk_project", "sprk_regardingproject"),
        ("sprk_invoice", "sprk_regardinginvoice"),
        ("sprk_servicerequest", "sprk_regardingservicerequest"),
        ("sprk_workassignment", "sprk_regardingworkassignment"),
        ("sprk_event", "sprk_regardingevent"),
        ("sprk_budget", "sprk_regardingbudget"),
        ("sprk_analysis", "sprk_regardinganalysis"),
        ("sprk_organization", "sprk_regardingorganization"),
        ("account", "sprk_regardingaccount"),
        ("contact", "sprk_regardingperson"),
    ];

    private static readonly Dictionary<string, string> EntityToField =
        All.ToDictionary(x => x.EntityLogicalName, x => x.RegardingField, StringComparer.OrdinalIgnoreCase);

    /// <summary>All regarding lookup field names (for copying a parent communication's regarding).</summary>
    public static readonly IReadOnlyList<string> AllRegardingFields =
        All.Select(x => x.RegardingField).ToArray();

    /// <summary>Resolve the regarding lookup field for an entity logical name; null when unmapped.</summary>
    public static string? FieldFor(string entityLogicalName) =>
        EntityToField.TryGetValue(entityLogicalName, out var field) ? field : null;
}
