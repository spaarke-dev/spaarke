using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Maps a regarding-target entity logical name to its primary display-name attribute and its Web API entity set.
/// Shared by the denorm writer (<see cref="IncomingAssociationResolver"/>), the add-in suggestion preview
/// (<c>OfficeCommunicationsEndpoints.GetSuggestionsByMessageIdAsync</c>) and the invoice review service.
/// </summary>
/// <remarks>
/// <para><b>Delegates to the ONE catalogue</b> in <see cref="RegardingRecordType"/> (Spaarke.Dataverse), which the
/// sprk_event writer also uses (spaarke-ontology-platform-r1 task 097, review M2). This file used to hold its own
/// copy, and two of its entries were wrong against live metadata: <c>sprk_analysis</c> → <c>sprk_analyses</c> (the
/// set is <c>sprk_analysises</c>; the wrong name 404s, which is indistinguishable from "the caller may not read
/// this record") and <c>sprk_organization</c> → <c>sprk_name</c> (the primary name is
/// <c>sprk_organizationname</c>). Keeping the members here keeps every call site unchanged.</para>
/// </remarks>
public static class RegardingNameFields
{
    /// <summary>The primary display-name attribute for <paramref name="entityLogicalName"/>, or <c>null</c>.</summary>
    public static string? PrimaryNameField(string entityLogicalName) =>
        RegardingRecordType.GetPrimaryNameField(entityLogicalName);

    /// <summary>
    /// The OData entity-set (collection) name for <paramref name="entityLogicalName"/>, or <c>null</c>
    /// for a type this catalogue does not cover.
    /// </summary>
    public static string? EntitySetName(string entityLogicalName) =>
        RegardingRecordType.GetEntitySetNameByLogicalName(entityLogicalName);
}
