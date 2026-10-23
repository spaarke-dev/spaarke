// Readability proof for ONE field-secured column, read app-only by the BFF.
//
// A field-secured column the BFF cannot read comes back EMPTY, exactly like a column that holds no value, so an empty read
// cannot be trusted on its own. The rule (docs/data-model/access-inheritance.md, "When a record is trusted"): an EMPTY value
// is decided on only once the BFF's read of the column is proven, by any one of
//   - a non-empty value read from the column by this process;
//   - the BFF's own user holds the System Administrator role;
//   - a field security profile the BFF's user belongs to — directly or through a team — grants Read on the column.
// Without proof the value is undetermined, and every caller fails closed.
//
// Broker-only (ADR-028): reads Dataverse APP-ONLY through the already-registered IGenericEntityService. No OBO.

using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Proves, at first use, that the BFF's own Dataverse user can READ one field-secured column, so that an EMPTY value read
/// from it may be taken as "not set" rather than "hidden". A proof, once made, holds for the life of the instance; a
/// failed proof is not remembered and is attempted again at the next use. A read fault is "not proven" (fail closed).
/// </summary>
internal sealed class SecuredColumnReadProof
{
    /// <summary>The platform role whose holders read every field-secured column.</summary>
    internal const string SystemAdministratorRoleName = "System Administrator";

    /// <summary>Field-permission value for "allowed".</summary>
    internal const int FieldPermissionAllowed = 4;

    private readonly IGenericEntityService _dataverse;
    private readonly ILogger _logger;
    private volatile bool _proven;

    public SecuredColumnReadProof(IGenericEntityService dataverse, string table, string column, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentNullException.ThrowIfNull(logger);
        _dataverse = dataverse;
        Table = table;
        Column = column;
        _logger = logger;
    }

    /// <summary>The column's table.</summary>
    public string Table { get; }

    /// <summary>The column's logical name.</summary>
    public string Column { get; }

    /// <summary>A non-empty value was read from the column: the BFF reads it (proof leg 1).</summary>
    public void RecordNonEmptyRead() => _proven = true;

    /// <summary>
    /// Whether the BFF's read of the column is proven. <c>false</c> on any fault (fail closed); cancellation propagates.
    /// </summary>
    public async Task<bool> IsProvenAsync(CancellationToken ct)
    {
        if (_proven)
        {
            return true;
        }

        try
        {
            var me = await ReadOwnUserIdAsync(ct).ConfigureAwait(false);
            if (me is not { } user)
            {
                return Unproven("the BFF's own user could not be read");
            }

            if (await HoldsSystemAdministratorAsync(user, ct).ConfigureAwait(false))
            {
                return Proven("the BFF's user holds the System Administrator role");
            }

            if (await ProfileGrantsReadAsync(user, throughTeam: false, ct).ConfigureAwait(false))
            {
                return Proven("a field security profile the BFF's user belongs to grants Read");
            }

            if (await ProfileGrantsReadAsync(user, throughTeam: true, ct).ConfigureAwait(false))
            {
                return Proven("a field security profile of a team the BFF's user belongs to grants Read");
            }

            return Unproven("no field security profile of the BFF's user, direct or through a team, grants Read on it");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[FLS-READ] Whether the BFF can read {Table}.{Column} could not be established; an empty value is not trusted.",
                Table, Column);
            return false;
        }
    }

    private async Task<Guid?> ReadOwnUserIdAsync(CancellationToken ct)
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

    private async Task<bool> HoldsSystemAdministratorAsync(Guid user, CancellationToken ct)
    {
        var query = new QueryExpression("role") { ColumnSet = new ColumnSet("roleid"), TopCount = 1 };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, SystemAdministratorRoleName);
        var link = query.AddLink("systemuserroles", "roleid", "roleid", JoinOperator.Inner);
        link.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, user);
        return (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.Count > 0;
    }

    private async Task<bool> ProfileGrantsReadAsync(Guid user, bool throughTeam, CancellationToken ct)
    {
        var query = new QueryExpression("fieldpermission") { ColumnSet = new ColumnSet("fieldpermissionid"), TopCount = 1 };
        query.Criteria.AddCondition("entityname", ConditionOperator.Equal, Table);
        query.Criteria.AddCondition("attributelogicalname", ConditionOperator.Equal, Column);
        query.Criteria.AddCondition("canread", ConditionOperator.Equal, FieldPermissionAllowed);

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

        return (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.Count > 0;
    }

    private bool Proven(string how)
    {
        _proven = true;
        _logger.LogInformation("[FLS-READ] The BFF's read of {Table}.{Column} is proven: {How}.", Table, Column, how);
        return true;
    }

    private bool Unproven(string why)
    {
        _logger.LogWarning("[FLS-READ] The BFF's read of {Table}.{Column} is not proven: {Why}. An empty value is not trusted.",
            Table, Column, why);
        return false;
    }
}
