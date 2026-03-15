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
    Task<Entity?> QueryAccountByDomainAsync(string domain, CancellationToken ct = default);
    Task<Entity?> QueryMatterByReferenceNumberAsync(string referenceNumber, CancellationToken ct = default);
    Task<Entity?> QueryRecordTypeRefAsync(string entityLogicalName, CancellationToken ct = default);
    Task<Guid?> QuerySystemUserByAzureAdOidAsync(string azureAdObjectId, CancellationToken ct = default);
}
