using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// The <c>sprk_recordtype_ref</c> catalog seam (<see cref="ICommunicationDataverseService.QueryRecordTypeRefAsync"/>) that the
/// shared regarding builder types the ADR-024 pair from, as a small in-memory fake for writers that hand the builder that
/// interface (<c>SignalWriter</c>, task 141). Every other member of the interface throws: a writer that reaches one has grown a
/// dependency the test should see.
/// </summary>
internal sealed class RecordTypeCatalogStub : ICommunicationDataverseService
{
    /// <summary>
    /// The active catalog rows on spaarkedev1 (read-only query, 2026-10-13) for the tables a Signal subject can be. There is NO
    /// <c>sprk_communication</c> row, so a communication subject gets the untyped pair, as it does live.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, Guid> SpaarkeDev1 = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        ["sprk_matter"] = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df"),
        ["sprk_project"] = Guid.Parse("ca68b3bb-8600-f111-8407-7c1e520aa4df"),
        ["sprk_todo"] = Guid.Parse("d4a606e6-cf62-f111-ab0c-70a8a58ae145"),
        ["sprk_event"] = Guid.Parse("5e9b37ea-8600-f111-8406-7c1e525abd8b"),
        ["sprk_workassignment"] = Guid.Parse("e8e1608c-781e-f111-88b3-7ced8d1dc988"),
        ["sprk_servicerequest"] = Guid.Parse("144c6790-d58a-f111-8076-7ced8d174eb8"),
        ["sprk_document"] = Guid.Parse("b83dbfc1-8600-f111-8407-7c1e520aa4df"),
        ["sprk_invoice"] = Guid.Parse("8e4a04f1-8600-f111-8406-7c1e525abd8b"),
        // ebilling-r1 task 026: created on spaarkedev1 2026-10-16 by Deploy-EbillingSignalLineSubjectSchema.ps1 ("Invoice Line").
        ["sprk_billingevent"] = Guid.Parse("fb30d221-bbc9-f111-a05a-7c1e520a989f"),
    };

    private readonly Func<string, CancellationToken, Task<Entity?>> _query;

    /// <summary>A catalog of the given rows (logical name → row id); a table not listed has no row.</summary>
    public RecordTypeCatalogStub(IReadOnlyDictionary<string, Guid> catalog)
        : this((name, _) => Task.FromResult(catalog.TryGetValue(name, out var id)
            ? new Entity("sprk_recordtype_ref", id) { ["sprk_recorddisplayname"] = name }
            : null))
    {
    }

    /// <summary>A catalog answered by <paramref name="query"/> (for example a live read in a seam test).</summary>
    public RecordTypeCatalogStub(Func<string, CancellationToken, Task<Entity?>> query) =>
        _query = query ?? throw new ArgumentNullException(nameof(query));

    /// <summary>Every table name the catalog was asked about, in order.</summary>
    public List<string> Queried { get; } = new();

    public Task<Entity?> QueryRecordTypeRefAsync(string entityLogicalName, CancellationToken ct = default)
    {
        Queried.Add(entityLogicalName);
        return _query(entityLogicalName, ct);
    }

    public Task<Entity[]> QueryCommunicationAccountsAsync(string filter, string select, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> ExistsCommunicationByGraphMessageIdAsync(string graphMessageId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Entity?> GetCommunicationByGraphMessageIdAsync(string graphMessageId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Entity?> GetCommunicationByInternetMessageIdAsync(string internetMessageId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(Guid Id, bool WasDuplicate)> CreateCommunicationRaceProofAsync(Entity communication, string? internetMessageId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Entity?> QueryContactByEmailAsync(string emailAddress, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Entity>> QueryContactsByFullNameAsync(string fullName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Entity>> QueryContactMembershipsAsync(Guid contactId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Entity?> QueryAccountByDomainAsync(string domain, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Entity?> QueryOrganizationByDomainAsync(string domain, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Entity?> QueryMatterByReferenceNumberAsync(string referenceNumber, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Entity>> QueryAllRecordTypeRefsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Entity>> QueryRecordsByNumberFieldAsync(string entityLogicalName, string numberFieldLogicalName, string value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Entity>> QueryRecordsByNumberFieldValuesAsync(string entityLogicalName, string numberFieldLogicalName, IReadOnlyCollection<string> values, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Guid?> QuerySystemUserByAzureAdOidAsync(string azureAdObjectId, CancellationToken ct = default) => throw new NotSupportedException();
}
