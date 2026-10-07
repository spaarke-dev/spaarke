using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// An in-memory POA share table behind <see cref="IDataverseRecordShareService"/> (task 063). It models what the
/// system-user share endpoints must assume about Dataverse — including, on the UNSAFE side, the cases Microsoft Learn
/// does not document.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>GrantAccess ADDS</b> to an existing share (undocumented; the reported behaviour). Additive is the unsafe
/// reading: a downgrade sent through GrantAccess keeps the old Write and Delete here, so a test can catch it.</item>
/// <item><b>ModifyAccess REPLACES</b> the mask (documented) and THROWS for a principal with no share (undocumented), so a
/// Modify sent where a Grant belonged fails loudly instead of passing against a lenient fake.</item>
/// <item><b>RevokeAccess</b> removes the share and THROWS for a principal with no share (undocumented), so a revoke the
/// endpoint should have skipped is caught.</item>
/// <item>With <see cref="FailReads"/> set the soft read answers an EMPTY list and the strict read THROWS — the real
/// pair's contracts — so a handler that decided a write from the soft read would visibly write on a failed read.</item>
/// </list>
/// Rights literals convert with Dataverse's own AccessRights values, held here as a SECOND copy on purpose: a fake that
/// borrowed the production table would agree with whatever that table said.
/// </remarks>
public sealed class FakeRecordShareTable : IDataverseRecordShareService
{
    private static readonly IReadOnlyDictionary<string, int> DataverseRightBits = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["ReadAccess"] = 1,
        ["WriteAccess"] = 2,
        ["AppendAccess"] = 4,
        ["AppendToAccess"] = 16,
        ["CreateAccess"] = 32,
        ["DeleteAccess"] = 65536,
        ["ShareAccess"] = 262144,
        ["AssignAccess"] = 524288,
    };

    /// <summary>Writes address the plural entity set and reads the logical name; the fake keys both to one record.</summary>
    /// <remarks>Task 149 added the child tables the secure-child share synchronizer writes, spelled as the live entity
    /// sets (2026-10-02; note <c>sprk_analysises</c>) — a second copy on purpose, like the rights table.</remarks>
    private static readonly IReadOnlyDictionary<string, string> LogicalNameOfEntitySet = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sprk_projects"] = "sprk_project",
        ["sprk_matters"] = "sprk_matter",
        ["sprk_workassignments"] = "sprk_workassignment",
        ["sprk_documents"] = "sprk_document",
        ["sprk_todos"] = "sprk_todo",
        ["sprk_events"] = "sprk_event",
        ["sprk_communications"] = "sprk_communication",
        ["sprk_communicationattachments"] = "sprk_communicationattachment",
        ["sprk_memos"] = "sprk_memo",
        ["sprk_analysises"] = "sprk_analysis",
        ["sprk_fileversions"] = "sprk_fileversion",
        // Task 147 r1 E2: the ribbon's "New Budget" creates a budget through the child-record route, which mirrors inline.
        ["sprk_budgets"] = "sprk_budget",
        // Task 147 r1c: the remaining census tables the child-record routes create and mirror.
        ["sprk_invoices"] = "sprk_invoice",
        ["sprk_reportcards"] = "sprk_reportcard",
        ["sprk_kpiassessments"] = "sprk_kpiassessment",
        ["sprk_billingevents"] = "sprk_billingevent",
    };

    /// <summary>Makes the batched strict read throw for this table only (task 149: one table's read failing).</summary>
    public string? FailBatchReadsOf { get; set; }

    /// <summary>Makes the strict single read throw for this one record only (task 149: a root whose shares cannot be read).</summary>
    public (string LogicalName, Guid RecordId)? FailReadsOfRecord { get; set; }

    /// <summary>Makes every write on this one record throw (task 149: a partial fan-out).</summary>
    public (string EntitySet, Guid RecordId)? FailWritesOnRecord { get; set; }

    private readonly object _gate = new();
    private readonly Dictionary<(string LogicalName, Guid RecordId, DataversePrincipalRef Principal), (int Mask, DateTimeOffset ModifiedOn)> _shares = new();
    private int _tick;

    /// <summary>Every write attempted, in order — <c>"GrantAccess ReadAccess"</c>, <c>"ModifyAccess …"</c>, <c>"RevokeAccess"</c>.</summary>
    public List<string> Writes { get; } = new();

    /// <summary>Makes every read fail: the soft read answers empty, the strict read throws.</summary>
    public bool FailReads { get; set; }

    /// <summary>Makes the strict read throw once any write has been attempted — a read-back that fails.</summary>
    public bool FailReadBackAfterWrite { get; set; }

    /// <summary>Makes every write throw this, after recording the attempt.</summary>
    public Exception? WriteFailure { get; set; }

    /// <summary>Makes writes "succeed" without storing anything — Dataverse accepting a call and not keeping what was asked.</summary>
    public bool IgnoreWrites { get; set; }

    /// <summary>How many strict reads were made.</summary>
    public int StrictReads { get; private set; }

    public void Seed(string logicalName, Guid recordId, DataversePrincipalRef principal, int mask, DateTimeOffset? modifiedOn = null)
    {
        lock (_gate)
            _shares[(logicalName, recordId, principal)] = (mask, modifiedOn ?? NextInstant());
    }

    /// <summary>The stored mask of one principal's share, or <c>null</c> when it holds none.</summary>
    public int? MaskOf(string logicalName, Guid recordId, DataversePrincipalRef principal)
    {
        lock (_gate)
            return _shares.TryGetValue((logicalName, recordId, principal), out var share) ? share.Mask : null;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _shares.Clear();
            Writes.Clear();
            FailReads = false;
            FailReadBackAfterWrite = false;
            WriteFailure = null;
            IgnoreWrites = false;
            StrictReads = 0;
            FailBatchReadsOf = null;
            FailReadsOfRecord = null;
            FailWritesOnRecord = null;
            WriteLog.Clear();
        }
    }

    /// <summary>Every write attempted, with its target — task 149's child writes are asserted per record and principal.</summary>
    public List<(string Action, string EntitySet, Guid RecordId, DataversePrincipalRef Principal, string? Rights)> WriteLog { get; } = new();

    private void Target(string action, string entitySetName, Guid recordId, DataversePrincipalRef principal, string? rights)
    {
        lock (_gate)
        {
            WriteLog.Add((action, entitySetName, recordId, principal, rights));
            if (FailWritesOnRecord is { } fail && fail.EntitySet == entitySetName && fail.RecordId == recordId)
                throw new InvalidOperationException($"Simulated write failure on {entitySetName}({recordId}).");
        }
    }

    public Task GrantAccessAsync(
        string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv,
        CancellationToken ct = default)
    {
        Target("GrantAccess", entitySetName, recordId, principal, accessRightsCsv);
        Write($"GrantAccess {accessRightsCsv}", () =>
        {
            var key = Key(entitySetName, recordId, principal);
            var existing = _shares.TryGetValue(key, out var share) ? share.Mask : 0;
            _shares[key] = (existing | Mask(accessRightsCsv), NextInstant());
        });

        return Task.CompletedTask;
    }

    public Task ModifyAccessAsync(
        string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv,
        CancellationToken ct = default)
    {
        Target("ModifyAccess", entitySetName, recordId, principal, accessRightsCsv);
        Write($"ModifyAccess {accessRightsCsv}", () =>
        {
            var key = Key(entitySetName, recordId, principal);
            if (!_shares.ContainsKey(key))
                throw new InvalidOperationException(
                    "ModifyAccess for a principal holding no share is undocumented; the fake refuses it.");

            _shares[key] = (Mask(accessRightsCsv), NextInstant());
        });

        return Task.CompletedTask;
    }

    public Task RevokeAccessAsync(
        string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct = default)
    {
        Target("RevokeAccess", entitySetName, recordId, principal, null);
        Write("RevokeAccess", () =>
        {
            if (!_shares.Remove(Key(entitySetName, recordId, principal)))
                throw new InvalidOperationException(
                    "RevokeAccess for a principal holding no share is undocumented; the fake refuses it.");
        });

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(
        string entityLogicalName, Guid recordId, CancellationToken ct = default)
        => Task.FromResult(FailReads ? Array.Empty<DataversePrincipalAccess>() : Rows(entityLogicalName, recordId));

    public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(
        string entityLogicalName, Guid recordId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            StrictReads++;
            if (FailReads || (FailReadBackAfterWrite && Writes.Count > 0))
                throw new InvalidOperationException("Simulated failure reading the shares.");
            if (FailReadsOfRecord is { } fail && fail.LogicalName == entityLogicalName && fail.RecordId == recordId)
                throw new InvalidOperationException($"Simulated failure reading the shares on {entityLogicalName}({recordId}).");

            return Task.FromResult(Rows(entityLogicalName, recordId));
        }
    }

    /// <summary>
    /// The batched strict read (task 149): every record asked about is in the answer, or it throws — like the real one.
    /// </summary>
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
        string entityLogicalName, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
    {
        lock (_gate)
        {
            StrictReads++;
            if (FailReads || FailBatchReadsOf == entityLogicalName)
                throw new InvalidOperationException($"Simulated failure reading the shares on {entityLogicalName} rows.");

            IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>> answer =
                recordIds.Distinct().ToDictionary(id => id, id => Rows(entityLogicalName, id));
            return Task.FromResult(answer);
        }
    }

    private void Write(string description, Action apply)
    {
        lock (_gate)
        {
            Writes.Add(description);

            if (WriteFailure is not null)
                throw WriteFailure;

            if (!IgnoreWrites)
                apply();
        }
    }

    private IReadOnlyList<DataversePrincipalAccess> Rows(string logicalName, Guid recordId)
    {
        lock (_gate)
            return _shares
                .Where(s => s.Key.LogicalName == logicalName && s.Key.RecordId == recordId)
                .Select(s => new DataversePrincipalAccess(s.Key.Principal, s.Value.Mask, s.Value.ModifiedOn))
                .ToList();
    }

    private static (string, Guid, DataversePrincipalRef) Key(string entitySetName, Guid recordId, DataversePrincipalRef principal)
        => LogicalNameOfEntitySet.TryGetValue(entitySetName, out var logicalName)
            ? (logicalName, recordId, principal)
            : throw new InvalidOperationException($"'{entitySetName}' is not a root entity set the share endpoints write.");

    private static int Mask(string accessRightsCsv)
        => accessRightsCsv.Split(',').Aggregate(0, (mask, name) =>
            mask | (DataverseRightBits.TryGetValue(name, out var bit)
                ? bit
                : throw new InvalidOperationException($"'{name}' is not a Dataverse AccessRights name.")));

    private DateTimeOffset NextInstant() => DateTimeOffset.UnixEpoch.AddMinutes(++_tick);
}
