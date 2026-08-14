namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Maps a regarding-target entity logical name to its primary display-name attribute.
/// Single source of truth (§11 reuse) shared by the denorm writer
/// (<see cref="IncomingAssociationResolver"/>, which resolves the FILED primary's
/// <c>sprk_regardingrecordname</c>) and the add-in suggestion preview
/// (<c>OfficeCommunicationsEndpoints.GetSuggestionsByMessageIdAsync</c>, task 042 — which resolves
/// UN-filed candidate names so the picker shows a real name, not a GUID). Returns <c>null</c> for
/// entity types with no known primary-name attribute (the caller falls back to the id).
/// </summary>
public static class RegardingNameFields
{
    /// <summary>The primary display-name attribute for <paramref name="entityLogicalName"/>, or <c>null</c>.</summary>
    public static string? PrimaryNameField(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_mattername",
        "sprk_project" => "sprk_projectname",
        "sprk_invoice" => "sprk_name",
        "sprk_event" => "sprk_eventname",
        "sprk_workassignment" => "sprk_name",
        "sprk_servicerequest" => "sprk_name",
        "sprk_budget" => "sprk_name",
        "sprk_analysis" => "sprk_name",
        "sprk_organization" => "sprk_name",
        "contact" => "fullname",
        "account" => "name",
        _ => null,
    };
}
