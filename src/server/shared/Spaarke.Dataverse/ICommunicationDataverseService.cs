using Microsoft.Xrm.Sdk;

namespace Spaarke.Dataverse;

/// <summary>
/// Communication account, association query, and contact/account lookup operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface ICommunicationDataverseService
{
    Task<Entity[]> QueryCommunicationAccountsAsync(string filter, string select, CancellationToken ct = default);
    Task<bool> ExistsCommunicationByGraphMessageIdAsync(string graphMessageId, CancellationToken ct = default);
    Task<Entity?> GetCommunicationByGraphMessageIdAsync(string graphMessageId, CancellationToken ct = default);
    Task<Entity?> GetCommunicationByInternetMessageIdAsync(string internetMessageId, CancellationToken ct = default);
    Task<Entity?> QueryContactByEmailAsync(string emailAddress, CancellationToken ct = default);

    /// <summary>
    /// Resolves active <c>contact</c> records whose <c>fullname</c> EXACTLY matches <paramref name="fullName"/>
    /// (case-insensitive). Used by the Association Engine's contact-name match rung to resolve a full name
    /// extracted from an email's subject/body/attachment to the contact(s) it names. Returns all exact matches
    /// (duplicate-named contacts are legitimate — the reviewer picks); empty when none match. Sibling to
    /// <see cref="QueryContactByEmailAsync"/> (both are contact-resolution on this ISP-segregated interface —
    /// no new service), differing only in the lookup key (name vs email).
    /// </summary>
    Task<IReadOnlyList<Entity>> QueryContactsByFullNameAsync(string fullName, CancellationToken ct = default);

    /// <summary>
    /// Resolves a contact's record memberships (matter contact, project team, counsel, etc.) from the
    /// <c>sprk_userentityassociation</c> junction (ADR-034) — person side = Contact (personidtype 2).
    /// Each returned entity carries <c>sprk_entitylogicalname</c>, <c>sprk_entityrecordid</c>, and
    /// <c>sprk_role</c>. Returns empty when the contact has no materialized memberships (the junction is
    /// populated by the R3 membership Phase-2 write paths). Used by the participant-correlation rung.
    /// </summary>
    Task<IReadOnlyList<Entity>> QueryContactMembershipsAsync(Guid contactId, CancellationToken ct = default);
    Task<Entity?> QueryAccountByDomainAsync(string domain, CancellationToken ct = default);
    Task<Entity?> QueryOrganizationByDomainAsync(string domain, CancellationToken ct = default);
    Task<Entity?> QueryMatterByReferenceNumberAsync(string referenceNumber, CancellationToken ct = default);
    Task<Entity?> QueryRecordTypeRefAsync(string entityLogicalName, CancellationToken ct = default);

    /// <summary>
    /// Reads the FULL <c>sprk_recordtype_ref</c> catalog (active rows), each carrying
    /// <c>sprk_recordlogicalname</c>, <c>sprk_regardingfield</c>, and <c>sprk_regardingrecordnumberfield</c>.
    /// The catalog is the per-tenant roster the identifier reverse-lookup rung (FR-01) reads to learn WHICH
    /// number field to reverse-look-up per record type — so onboarding a tenant requires ONLY catalog config,
    /// no code change. Read DEFENSIVELY by the caller (trim; skip rows with a null/blank number field). Returns
    /// empty when the catalog is empty. Sibling to the single-row <see cref="QueryRecordTypeRefAsync"/> on this
    /// ISP-segregated interface (same table, whole-roster read vs one row) — no new service.
    /// </summary>
    Task<IReadOnlyList<Entity>> QueryAllRecordTypeRefsAsync(CancellationToken ct = default);

    /// <summary>
    /// Value-based reverse lookup (FR-01): resolves active records of <paramref name="entityLogicalName"/> whose
    /// <paramref name="numberFieldLogicalName"/> EQUALS <paramref name="value"/> (exact equality). This is the
    /// catalog-driven seam the identifier rung uses to match an email's identifier token against every record
    /// type's number field by VALUE — no numbering scheme is decoded in code. Returns each match carrying its id
    /// + the number field. Returns empty (never throws) when the entity/field name or value is null/blank, so a
    /// dirty catalog row degrades to no-match (NFR-04). Returns 2+ entities when duplicate numbers exist
    /// (legitimate — the caller surfaces the ambiguity, never guesses).
    /// </summary>
    Task<IReadOnlyList<Entity>> QueryRecordsByNumberFieldAsync(
        string entityLogicalName, string numberFieldLogicalName, string value, CancellationToken ct = default);

    Task<Guid?> QuerySystemUserByAzureAdOidAsync(string azureAdObjectId, CancellationToken ct = default);
}
