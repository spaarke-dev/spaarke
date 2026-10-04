namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Maps a regarding-target entity logical name to its primary display-name attribute.
/// Single source of truth (§11 reuse) shared by the denorm writer
/// (<see cref="IncomingAssociationResolver"/>, which resolves the FILED primary's
/// <c>sprk_regardingrecordname</c>) and the add-in suggestion preview
/// (<c>OfficeCommunicationsEndpoints.GetSuggestionsByMessageIdAsync</c>, task 042 — which resolves
/// UN-filed candidate names so the picker shows a real name, not a GUID). Returns <c>null</c> for
/// entity types with no known primary-name attribute (the caller falls back to the id).
/// <para>
/// Also the name source of <c>TaskActionCore</c>'s ADR-024 regarding pair (unified-access-control-r2 task 156, owner
/// decisions round 8 item 2), which is why <c>sprk_communication</c> is listed: a playbook or communication follow-up task
/// is most often filed under the email it follows up. <c>sprk_communication</c> is never an association candidate
/// (<c>RegardingFieldMap</c> does not list it), so the entry changes nothing for the two callers above.
/// </para>
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
        // Verified live 2026-10-02 (spaarkedev1 describe, read-only): sprk_name NVARCHAR(850), e.g. "Email: <subject>".
        "sprk_communication" => "sprk_name",
        "sprk_workassignment" => "sprk_name",
        "sprk_servicerequest" => "sprk_name",
        "sprk_budget" => "sprk_name",
        "sprk_analysis" => "sprk_name",
        "sprk_organization" => "sprk_name",
        "contact" => "fullname",
        "account" => "name",
        _ => null,
    };

    /// <summary>
    /// The OData entity-set (collection) name for <paramref name="entityLogicalName"/>, or <c>null</c>
    /// for a type this catalogue does not cover.
    /// </summary>
    /// <remarks>
    /// Added by unified-access-control-r2 task 127 (#1020), which needed to read candidate records
    /// through the DELEGATED Dataverse client — that client speaks OData, which addresses collections
    /// by entity-set name rather than by logical name.
    /// <para>
    /// Written as an explicit map rather than by pluralising the logical name, because Dataverse's
    /// pluralisation is not a simple suffix rule (<c>sprk_analysis</c> → <c>sprk_analyses</c>), and a
    /// wrong guess here fails as a 404 that would be indistinguishable from "the caller may not read
    /// this record" — silently dropping a candidate the caller was entitled to see.
    /// </para>
    /// <para>
    /// ⚠️ Keep the key set identical to <see cref="PrimaryNameField"/>: a type that has a display name
    /// must also be addressable, and <c>RegardingNameFieldsTests</c> pins that agreement.
    /// </para>
    /// </remarks>
    public static string? EntitySetName(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_matters",
        "sprk_project" => "sprk_projects",
        "sprk_invoice" => "sprk_invoices",
        "sprk_event" => "sprk_events",
        "sprk_communication" => "sprk_communications",
        "sprk_workassignment" => "sprk_workassignments",
        "sprk_servicerequest" => "sprk_servicerequests",
        "sprk_budget" => "sprk_budgets",
        "sprk_analysis" => "sprk_analyses",
        "sprk_organization" => "sprk_organizations",
        "contact" => "contacts",
        "account" => "accounts",
        _ => null,
    };
}
