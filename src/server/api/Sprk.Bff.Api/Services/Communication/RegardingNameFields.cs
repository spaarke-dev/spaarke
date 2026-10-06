using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Maps a regarding-target entity logical name to its primary display-name attribute.
/// Single source of truth (§11 reuse) shared by the denorm writer
/// (<see cref="IncomingAssociationResolver"/>, which resolves the FILED primary's
/// <c>sprk_regardingrecordname</c>), the add-in suggestion preview and the communication suggest route
/// (<see cref="SuggestionCandidateAccess"/> — which reads each candidate AS THE CALLER), and the communication
/// routes that authorize a caller-named regarding record (<c>CommunicationRecordAuthorizationFilter</c>, task 161).
/// Returns <c>null</c> for entity types with no known primary-name attribute (the caller falls back to the id).
/// <para>
/// Also the name source of <c>TaskActionCore</c>'s ADR-024 regarding pair (unified-access-control-r2 task 156, owner
/// decisions round 8 item 2), which is why <c>sprk_communication</c> is listed in <see cref="PrimaryNameField"/>: a
/// playbook or communication follow-up task is most often filed under the email it follows up. It is a NAME-ONLY entry —
/// deliberately absent from <see cref="EntitySetName"/>, which the task 161 routes use as their regarding allow-list
/// (a record thread, a template merge, an association or an ad-hoc task can name only an addressable ADR-024 target;
/// <c>RegardingFieldMap</c> does not list <c>sprk_communication</c>, so a thread "regarding" an email would be written
/// with no typed regarding lookup). <c>TaskActionCore</c> reads the name by logical name, so it needs no entity set.
/// Reconciled at the sweep integration of 156 with 161; <c>RegardingNameFieldsTests</c> pins the one exception.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>Every value here is LIVE-VERIFIED</b> (spaarkedev1, read-only <c>EntityDefinitions</c> and
/// <c>Attributes</c> GETs, 2026-10-03, task 161 note §2) and pinned by <c>RegardingNameFieldsTests</c>. Task 161 found
/// and corrected two values that had been written from convention rather than metadata:</para>
/// <list type="bullet">
///   <item><c>sprk_organization</c>'s display name is <c>sprk_organizationname</c>. <c>sprk_name</c> does NOT exist on
///   that table, so every read that selected it failed with a 400 — the denorm writer recorded no name, and the
///   suggestion previews dropped every organization candidate's name.</item>
///   <item><c>sprk_analysis</c>'s entity set is <c>sprk_analysises</c>, not <c>sprk_analyses</c>. A wrong set name
///   fails as a 404, which a delegated read cannot tell apart from "the caller may not read this record" — so an
///   analysis candidate the caller WAS entitled to see was silently treated as hidden.</item>
/// </list>
/// <para>Both functions are case-SENSITIVE: callers normalize a caller-supplied type with
/// <c>ToLowerInvariant()</c> first (the regarding writers accept any case).</para>
/// </remarks>
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
        "sprk_reportcard" => "sprk_name",
        "sprk_analysis" => "sprk_name",
        "sprk_organization" => "sprk_organizationname",
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
    /// pluralisation is not a simple suffix rule (<c>sprk_analysis</c> → <c>sprk_analysises</c>), and a
    /// wrong guess here fails as a 404 that would be indistinguishable from "the caller may not read
    /// this record" — silently dropping a candidate the caller was entitled to see.
    /// </para>
    /// <para>
    /// ⚠️ Keep the key set identical to <see cref="PrimaryNameField"/>'s, except the ONE name-only entry
    /// <c>sprk_communication</c> (see the class summary): a type that has a display name must otherwise also be
    /// addressable, and <c>RegardingNameFieldsTests</c> pins that agreement and its single exception.
    /// </para>
    /// </remarks>
    public static string? EntitySetName(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_matters",
        "sprk_project" => "sprk_projects",
        "sprk_invoice" => "sprk_invoices",
        "sprk_event" => "sprk_events",
        "sprk_workassignment" => "sprk_workassignments",
        "sprk_servicerequest" => "sprk_servicerequests",
        "sprk_budget" => "sprk_budgets",
        "sprk_reportcard" => "sprk_reportcards",
        "sprk_analysis" => "sprk_analysises",
        "sprk_organization" => "sprk_organizations",
        "contact" => "contacts",
        "account" => "accounts",
        _ => null,
    };
}
