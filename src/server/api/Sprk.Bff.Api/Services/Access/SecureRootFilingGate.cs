using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// unified-access-control-r2 task 158 (owner rounds 6 and 31) — the calls a BFF writer of a <c>sprk_workassignment</c> or
/// <c>sprk_project</c> makes around a write, so a record filed under a secure matter or project is secure in the same
/// operation. A RE-FILE: <see cref="CheckAsync"/> BEFORE the write (an unreadable parent flag, or a recorded creator walled
/// off the record or a secure parent, refuses — nothing written) and <see cref="SecureAfterWriteAsync"/> AFTER it
/// (provisioning's own steps, through <see cref="SecureRootInheritance"/>). A CREATE (task 158 r1):
/// <see cref="PlanCreateAsync"/> BEFORE it (refused, ordinary, or INTO isolation) and, for an isolated create,
/// <see cref="CompleteIsolatedCreateAsync"/> after it.
/// </summary>
/// <remarks>
/// <para><b>Why a gate and not the service itself.</b> The writers are singletons and scoped services composed in many
/// hosts (the tool framework's assembly scan, the playbook nodes, the Office module) — the 156 precedent
/// (<c>CoreAncestorAfterWriteRestamp</c>). <see cref="SecureRootInheritance"/> is scoped and composes provisioning's
/// dependencies; this singleton holds only a scope factory and resolves it per call, so a writer gains one cheap,
/// always-registered constructor dependency (§10 F.1: registered beside the restamper by <c>AddCoreAncestorResolver</c>,
/// which every composition with a writer calls). A row of any other table costs nothing: no scope is created.</para>
/// <para><b>Fail closed.</b> A composition in which the inheritance cannot be resolved REFUSES a filing write of a work
/// assignment or project (<see cref="RecordOwnerRefusal.ParentUndetermined"/>) — never "not secure". The after-write call
/// never throws: a failure is logged and the record is left to <see cref="SecureRootInheritanceJob"/> (≤ 5 minutes).</para>
/// </remarks>
public sealed class SecureRootFilingGate
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecureRootFilingGate> _logger;

    public SecureRootFilingGate(IServiceScopeFactory scopeFactory, ILogger<SecureRootFilingGate> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// BEFORE the write. <c>null</c>: proceed. Otherwise a refusal to answer in the writer's own contract, with nothing
    /// written. See <see cref="SecureRootInheritance.CheckRefileAsync"/> for the value shapes accepted.
    /// </summary>
    public async Task<RecordOwnerResolution?> CheckAsync(
        string table, Guid? recordId, IEnumerable<KeyValuePair<string, object?>> writes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (!SecureRootInheritance.Inherits(table))
            return null;

        var materialized = writes.ToArray();
        if (AccessInheritance.IsNamedIn(materialized))
        {
            // Task 175 fix round (verifier F1-1): refused whatever the host — the access record is the cascade's alone.
            return RecordOwnerResolution.Refused(AccessInheritance.ServerOnlyReasonCode,
                $"{AccessInheritance.Column} is written only by Spaarke, so it was not written");
        }

        if (AccessFollowsParent.NamesSecureFlag(materialized))
        {
            // Task 175 fix round 2 (K3): the secure flag is the transitions' and the cascade's alone, whatever the host.
            return RecordOwnerResolution.Refused(AccessFollowsParent.SecureFlagReasonCode, SecureRootInheritance.SecureFlagRefusalText);
        }

        var filing = SecureRootInheritance.FilingColumnsOf(table.Trim().ToLowerInvariant());
        // Task 175 (owner round 84): a write of sprk_accesspermission / sprk_issecure on an existing record is checked too —
        // refused when the record has a parent (its access follows it).
        if (!materialized.Any(w => filing.Contains(SecureRootInheritance.NormalizeColumn(w.Key))
                                   || (recordId is not null
                                       && AccessFollowsParent.LockedColumns.Contains(SecureRootInheritance.NormalizeColumn(w.Key)))))
            return null;

        using var scope = _scopeFactory.CreateScope();
        var inheritance = scope.ServiceProvider.GetService<SecureRootInheritance>();
        if (inheritance is null)
        {
            _logger.LogError(
                "[SECURE-INHERIT] A {Table} write changes what it is filed under (or its access), and this host cannot check whether that " +
                "record is secure (SecureRootInheritance is not registered). Refused (fail closed).", table);
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.ParentUndetermined,
                "whether the record it would be filed under is secure cannot be checked here, so it was not written");
        }

        return await inheritance.CheckRefileAsync(table, recordId, materialized, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Task 158 r1 (owner round 31 item 2): BEFORE a CREATE — how it is made. See
    /// <see cref="SecureRootInheritance.PlanCreateAsync"/>: refused (nothing written), ordinary, or created INTO isolation
    /// for <paramref name="creatorSystemUserId"/>. A host without the inheritance refuses a create that files the row under
    /// anything (fail closed); a create that files it under nothing is ordinary and costs no scope.
    /// </summary>
    public async Task<SecureRootCreatePlan> PlanCreateAsync(
        string table, IEnumerable<KeyValuePair<string, object?>> writes, Guid creatorSystemUserId, CancellationToken ct,
        Func<IReadOnlyList<SecureFilingParent>, CancellationToken, Task<RecordOwnerResolution?>>? callerMayFileUnder = null)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (!SecureRootInheritance.Inherits(table))
            return SecureRootCreatePlan.Ordinary;

        var materialized = writes.ToArray();
        var filing = SecureRootInheritance.FilingColumnsOf(table.Trim().ToLowerInvariant());
        if (AccessInheritance.IsNamedIn(materialized))
        {
            // Task 175 fix round (verifier F1-1): refused whatever the host — the access record is the cascade's alone.
            return SecureRootCreatePlan.Refused(RecordOwnerResolution.Refused(AccessInheritance.ServerOnlyReasonCode,
                $"{AccessInheritance.Column} is written only by Spaarke, so it was not created"));
        }

        if (AccessFollowsParent.NamesSecureFlag(materialized))
        {
            // Task 175 fix round 2 (K3): a create never carries the secure flag — a secure create is planned here, not asked.
            return SecureRootCreatePlan.Refused(RecordOwnerResolution.Refused(AccessFollowsParent.SecureFlagReasonCode,
                SecureRootInheritance.SecureFlagRefusalText));
        }

        if (!materialized.Any(w => filing.Contains(SecureRootInheritance.NormalizeColumn(w.Key))))
            return SecureRootCreatePlan.Ordinary;

        using var scope = _scopeFactory.CreateScope();
        var inheritance = scope.ServiceProvider.GetService<SecureRootInheritance>();
        if (inheritance is null)
        {
            _logger.LogError(
                "[SECURE-INHERIT] A new {Table} is filed under a record, and this host cannot check whether that record is " +
                "secure (SecureRootInheritance is not registered). Refused (fail closed).", table);
            return new SecureRootCreatePlan(
                RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined,
                    "whether the record it would be filed under is secure cannot be checked here, so it was not created"),
                false, null, Array.Empty<SecureFilingParent>());
        }

        return await inheritance.PlanCreateAsync(table, materialized, creatorSystemUserId, ct, callerMayFileUnder)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Task 158 r1 (owner round 31 item 2): AFTER a create made INTO isolation — its creator shared (read back), its container,
    /// its parents' sharees; a row whose creator could not be shared is deleted again. See
    /// <see cref="SecureRootInheritance.CompleteIsolatedCreateAsync"/>. Never throws: a host without the inheritance (which
    /// cannot have planned an isolated create) or a fault answers a failed result naming the record.
    /// </summary>
    public async Task<SecureRootInheritResult> CompleteIsolatedCreateAsync(
        string table, Guid recordId, Guid creatorSystemUserId, string? traceId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inheritance = scope.ServiceProvider.GetRequiredService<SecureRootInheritance>();
            return await inheritance.CompleteIsolatedCreateAsync(
                    table, recordId, creatorSystemUserId, traceId ?? Guid.NewGuid().ToString("N"), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[SECURE-INHERIT] Completing the isolated create of {Table} {RecordId} failed; the secure-root inheritance job " +
                "completes it.", table, recordId);
            return new SecureRootInheritResult(table, recordId, SecureRootInheritOutcome.Failed,
                SecureRootInheritance.ReasonUnexpectedResult, "completing the secure create failed",
                Array.Empty<SecureFilingParent>(), null);
        }
    }

    /// <summary>
    /// AFTER the write (a create passes its own columns too, so a row filed under nothing costs no read): secures the record
    /// when it is now filed under a secure record. <paramref name="writtenColumns"/> <c>null</c> means "unknown: always
    /// check". Never throws; runs to completion once the write landed (no caller token).
    /// </summary>
    public async Task<SecureRootInheritResult?> SecureAfterWriteAsync(
        string table, Guid recordId, IEnumerable<string>? writtenColumns, string? traceId)
    {
        if (!SecureRootInheritance.Inherits(table))
            return null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inheritance = scope.ServiceProvider.GetService<SecureRootInheritance>();
            if (inheritance is null)
            {
                _logger.LogError(
                    "[SECURE-INHERIT] {Table} {RecordId} was written, and this host cannot secure it (SecureRootInheritance is " +
                    "not registered); the secure-root inheritance job does.", table, recordId);
                return null;
            }

            return await inheritance.SecureAfterWriteAsync(table, recordId, writtenColumns, traceId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[SECURE-INHERIT] Securing {Table} {RecordId} after its write failed; the secure-root inheritance job retries it.",
                table, recordId);
            return null;
        }
    }
}
