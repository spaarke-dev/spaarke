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
    Task<Guid?> QuerySystemUserByAzureAdOidAsync(string azureAdObjectId, CancellationToken ct = default);
}
