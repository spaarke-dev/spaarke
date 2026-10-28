// Readability proof for a set of field-secured columns of one table, read app-only by the BFF.
//
// A field-secured column the BFF cannot read comes back EMPTY, exactly like a column that holds no value, so an empty read
// cannot be trusted on its own. The rule (docs/data-model/access-inheritance.md, "When a record is trusted"): an EMPTY value
// is decided on only once the BFF's read of the column is proven, by any one of
//   - the BFF's own user holds the System Administrator role (matched by its platform role template id, never by name);
//   - a field security profile the BFF's user belongs to — directly or through a team — grants Read on the column.
// A non-empty value read from one row is NOT proof: a field share can grant one record without the column. Without proof the
// value is undetermined, and every caller fails closed. A proof holds for ProofLifetime, then it is made again.
//
// Broker-only (ADR-028): every read is app-only. The SDK source reads through the already-registered IGenericEntityService
// (the BFF's own environment); the identity-binding store brings a Web API source for the environment it targets. No OBO.

using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>How a readability proof ended.</summary>
public enum ColumnReadProofOutcome
{
    /// <summary>The BFF's read of every column is proven: an empty value means "not set".</summary>
    Proven,

    /// <summary>No leg proves the read of at least one column: an empty value is undetermined (fail closed).</summary>
    Unproven,

    /// <summary>The proof's own reads faulted (a throttle, a 5xx, a network fault, a refused read): fail closed, retryable.</summary>
    Faulted,
}

/// <summary>
/// The platform reads a <see cref="SecuredColumnReadProof"/> makes. One implementation per Dataverse channel: the SDK
/// (<see cref="SdkColumnReadPermissionSource"/>, the BFF's own environment) and the identity-binding store's Web API channel
/// (the environment that store targets). A fault throws; the proof reports it as <see cref="ColumnReadProofOutcome.Faulted"/>.
/// </summary>
internal interface IColumnReadPermissionSource
{
    /// <summary>The BFF's own Dataverse user (the caller of these reads), or null when it cannot be read.</summary>
    Task<Guid?> ReadOwnUserIdAsync(CancellationToken ct);

    /// <summary>
    /// Whether <paramref name="user"/> holds a role whose template is <see cref="SecuredColumnReadProof.SystemAdministratorRoleTemplateId"/>
    /// (the System Administrator role, or a business unit's copy of it).
    /// </summary>
    Task<bool> HoldsSystemAdministratorAsync(Guid user, CancellationToken ct);

    /// <summary>
    /// Which of <paramref name="columns"/> of <paramref name="table"/> a field security profile of <paramref name="user"/>
    /// grants Read on — the user's own profiles, or (<paramref name="throughTeam"/>) the profiles of the teams it is a member of.
    /// </summary>
    Task<IReadOnlyCollection<string>> ColumnsGrantingReadAsync(
        Guid user, string table, IReadOnlyCollection<string> columns, bool throughTeam, CancellationToken ct);
}

/// <summary>
/// Proves that the BFF's own Dataverse user can READ every one of a set of field-secured columns, so that an EMPTY value
/// read from one of them may be taken as "not set" rather than "hidden". A proof holds for <see cref="ProofLifetime"/>
/// (one lifetime for every column), then it is made again; a failed proof is not remembered and is attempted again at the
/// next use. A fault is <see cref="ColumnReadProofOutcome.Faulted"/> (fail closed); cancellation propagates.
/// </summary>
internal sealed class SecuredColumnReadProof
{
    /// <summary>How long a proof holds before it is made again. The one lifetime for every proven column.</summary>
    internal static readonly TimeSpan ProofLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The platform's role template id of the System Administrator role, whose holders read every field-secured column. It is
    /// the same in every environment and is carried by each business unit's copy of the role. The role is matched by this id,
    /// never by its display name, which an administrator can change or give to another role.
    /// </summary>
    internal static readonly Guid SystemAdministratorRoleTemplateId = Guid.Parse("627090ff-40a3-4053-8790-584edc5be201");

    /// <summary>Field-permission value for "allowed".</summary>
    internal const int FieldPermissionAllowed = 4;

    /// <summary>
    /// Every platform table the proof reads. The BFF's application user must be able to read each of them, or every proof
    /// fails closed (scripts/Set-ExternalAccessContactSecuritySchema.ps1 -Verify asserts it).
    /// </summary>
    internal static readonly IReadOnlyList<string> PlatformTablesRead =
    [
        "systemuser", "role", "systemuserroles", "fieldpermission", "systemuserprofiles", "teamprofiles", "teammembership",
    ];

    private readonly IColumnReadPermissionSource _source;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private long _provenUntilUtcTicks;

    public SecuredColumnReadProof(
        IColumnReadPermissionSource source, string table, IReadOnlyCollection<string> columns, ILogger logger,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(logger);
        var distinct = columns.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length == 0)
        {
            throw new ArgumentException("A readability proof needs at least one column.", nameof(columns));
        }

        _source = source;
        Table = table;
        Columns = distinct;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The proof for one column, read through the SDK (the BFF's own environment).</summary>
    public SecuredColumnReadProof(
        IGenericEntityService dataverse, string table, string column, ILogger logger, TimeProvider? time = null)
        : this(new SdkColumnReadPermissionSource(dataverse), table, new[] { column }, logger, time)
    {
    }

    /// <summary>The columns' table.</summary>
    public string Table { get; }

    /// <summary>The columns' logical names.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Whether the BFF's read of every column is proven. <c>false</c> unless proven (fail closed).</summary>
    public async Task<bool> IsProvenAsync(CancellationToken ct)
        => await ProveAsync(ct).ConfigureAwait(false) == ColumnReadProofOutcome.Proven;

    /// <summary>The proof: held while within <see cref="ProofLifetime"/>, otherwise made (again).</summary>
    public async Task<ColumnReadProofOutcome> ProveAsync(CancellationToken ct)
    {
        if (_time.GetUtcNow().UtcTicks < Volatile.Read(ref _provenUntilUtcTicks))
        {
            return ColumnReadProofOutcome.Proven;
        }

        try
        {
            var me = await _source.ReadOwnUserIdAsync(ct).ConfigureAwait(false);
            if (me is not { } user)
            {
                return Unproven(ColumnReadProofOutcome.Faulted, "the BFF's own user could not be read");
            }

            if (await _source.HoldsSystemAdministratorAsync(user, ct).ConfigureAwait(false))
            {
                return Proven("the BFF's user holds the System Administrator role");
            }

            var remaining = new HashSet<string>(Columns, StringComparer.OrdinalIgnoreCase);
            remaining.ExceptWith(await _source.ColumnsGrantingReadAsync(user, Table, Columns, throughTeam: false, ct)
                .ConfigureAwait(false));
            if (remaining.Count > 0)
            {
                remaining.ExceptWith(await _source.ColumnsGrantingReadAsync(user, Table, remaining.ToArray(), throughTeam: true, ct)
                    .ConfigureAwait(false));
            }

            return remaining.Count == 0
                ? Proven("a field security profile of the BFF's user, direct or through a team, grants Read on every column")
                : Unproven(ColumnReadProofOutcome.Unproven,
                    $"no field security profile of the BFF's user, direct or through a team, grants Read on "
                    + string.Join(", ", remaining.Order(StringComparer.OrdinalIgnoreCase)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _provenUntilUtcTicks, 0);
            _logger.LogWarning(ex,
                "[FLS-READ] Whether the BFF can read {Table}.{Columns} could not be established; an empty value is not trusted.",
                Table, string.Join(",", Columns));
            return ColumnReadProofOutcome.Faulted;
        }
    }

    private ColumnReadProofOutcome Proven(string how)
    {
        Volatile.Write(ref _provenUntilUtcTicks, (_time.GetUtcNow() + ProofLifetime).UtcTicks);
        _logger.LogInformation("[FLS-READ] The BFF's read of {Table}.{Columns} is proven: {How}.",
            Table, string.Join(",", Columns), how);
        return ColumnReadProofOutcome.Proven;
    }

    private ColumnReadProofOutcome Unproven(ColumnReadProofOutcome outcome, string why)
    {
        Volatile.Write(ref _provenUntilUtcTicks, 0);
        _logger.LogWarning(
            "[FLS-READ] The BFF's read of {Table}.{Columns} is not proven: {Why}. An empty value is not trusted.",
            Table, string.Join(",", Columns), why);
        return outcome;
    }
}

/// <summary>The proof's reads through the SDK (<see cref="IGenericEntityService"/>): the BFF's own environment, app-only.</summary>
internal sealed class SdkColumnReadPermissionSource : IColumnReadPermissionSource
{
    private readonly IGenericEntityService _dataverse;

    public SdkColumnReadPermissionSource(IGenericEntityService dataverse)
        => _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));

    public async Task<Guid?> ReadOwnUserIdAsync(CancellationToken ct)
    {
        var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("systemuserid"), TopCount = 1 };
        query.Criteria.AddCondition("systemuserid", ConditionOperator.EqualUserId);
        var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.FirstOrDefault();
        var id = row?.GetAttributeValue<Guid>("systemuserid") ?? Guid.Empty;
        if (id == Guid.Empty)
        {
            id = row?.Id ?? Guid.Empty;
        }

        return id == Guid.Empty ? null : id;
    }

    public async Task<bool> HoldsSystemAdministratorAsync(Guid user, CancellationToken ct)
    {
        var query = new QueryExpression("role") { ColumnSet = new ColumnSet("roleid"), TopCount = 1 };
        query.Criteria.AddCondition("roletemplateid", ConditionOperator.Equal, SecuredColumnReadProof.SystemAdministratorRoleTemplateId);
        var link = query.AddLink("systemuserroles", "roleid", "roleid", JoinOperator.Inner);
        link.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, user);
        return (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.Count > 0;
    }

    /// <summary>Page size of the field-permission read.</summary>
    internal const int FieldPermissionPageSize = 5000;

    /// <summary>
    /// The most pages the field-permission read follows. The rows are bounded by the asked columns times the user's
    /// profiles, so a read that needs more pages is not a real answer: it throws, and the proof is Faulted (fail closed).
    /// </summary>
    internal const int FieldPermissionMaxPages = 20;

    /// <remarks>
    /// The table is checked in code, not by the query: Dataverse refuses a QueryExpression/FetchXML condition on
    /// <c>fieldpermission.entityname</c> given a table's logical name (observed live through the Web API: the read faults,
    /// the attribute's value being expected as an integer), while an OData <c>$filter</c> on it is accepted. So the query selects <c>entityname</c> and asks
    /// the server only for the columns (<c>attributelogicalname</c> In), <c>canread</c> = allowed and the profile link; a
    /// returned row counts only when its <c>entityname</c> is a string equal to <paramref name="table"/>. A row whose table is
    /// missing or not a string never counts (fail closed). The table check is what keeps a grant on a same-named column of
    /// another table (for example <c>sprk_accesspermissiongrant</c>, on both <c>contact</c> and <c>sprk_organization</c>)
    /// from proving this table's column. Every page is read; a fault on any page throws.
    /// </remarks>
    public async Task<IReadOnlyCollection<string>> ColumnsGrantingReadAsync(
        Guid user, string table, IReadOnlyCollection<string> columns, bool throughTeam, CancellationToken ct)
    {
        var query = new QueryExpression("fieldpermission") { ColumnSet = new ColumnSet("attributelogicalname", "entityname") };
        query.Criteria.AddCondition("attributelogicalname", ConditionOperator.In, columns.Cast<object>().ToArray());
        query.Criteria.AddCondition("canread", ConditionOperator.Equal, SecuredColumnReadProof.FieldPermissionAllowed);

        if (throughTeam)
        {
            var teamProfiles = query.AddLink("teamprofiles", "fieldsecurityprofileid", "fieldsecurityprofileid", JoinOperator.Inner);
            var membership = teamProfiles.AddLink("teammembership", "teamid", "teamid", JoinOperator.Inner);
            membership.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, user);
        }
        else
        {
            var userProfiles = query.AddLink("systemuserprofiles", "fieldsecurityprofileid", "fieldsecurityprofileid", JoinOperator.Inner);
            userProfiles.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, user);
        }

        query.PageInfo = new PagingInfo { PageNumber = 1, Count = FieldPermissionPageSize };
        var granted = new List<string>();
        for (var page = 1; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"The field-permission read returned no result for page {page}.");
            granted.AddRange(result.Entities
                .Where(r => r.Attributes.TryGetValue("entityname", out var t)
                    && t is string rowTable && string.Equals(rowTable, table, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.GetAttributeValue<string>("attributelogicalname"))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!));

            if (!result.MoreRecords)
            {
                return granted;
            }

            if (page >= FieldPermissionMaxPages)
            {
                throw new InvalidOperationException(
                    $"The field-permission read had more than {FieldPermissionMaxPages} pages; it is not taken as an answer.");
            }

            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }
}
