using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// The scheduled repair pass (field-mapping-server-write-path-r1 task 052b; spec FR-25, NFR-01, NFR-03, NFR-04): every 5 minutes,
/// each row created OUTSIDE the product after the go-live instant gets what the create pipeline would have given it — its field
/// mapping, its owner and its core-ancestor stamp — through Repair mode (<see cref="RecordCreationService.RepairAsync"/>),
/// fill-only, in at most one conditional write per row.
/// </summary>
/// <remarks>
/// <para><b>Which rows (forward-only, task 050's marker).</b> Per table, rows with <c>createdon</c> in
/// [max(go-live, watermark − <see cref="WindowOverlap"/>), now − <see cref="SettleTime"/>) whose <c>createdby</c> is not a product
/// principal: the BFF's own
/// identity (WhoAmI), its Dataverse Web API client's managed identity when <see cref="WebApiIdentityConfigKey"/> is set, the ontology
/// writer's when it is configured, and the retired identities in
/// <see cref="ProductPrincipalIdsConfigKey"/>. People are never in the set. The rule is asked three times: by the query, by this
/// job over every scanned row, and by Repair over the row it read (<see cref="RecordRepairPassContext"/>), before any write. A
/// run that cannot decide the set (WhoAmI or the writer's identity faulted, a configured id that does not parse) reads no table
/// and writes nothing, and fails.</para>
/// <para><b>Off until configured.</b> With <see cref="GoLiveConfigKey"/> unset the job reads and writes nothing and logs a warning
/// each run; a value that is not an ISO 8601 round-trip instant ("O") logs an error and the run fails. Nothing in this repository
/// sets it.</para>
/// <para><b>Tables.</b> Every registry table (<see cref="RecordTableRules.All"/>) except <see cref="ExcludedTables"/>: the owner's
/// exclusions (<c>task</c>, <c>account</c>), the tables only their own writer creates, the append-only Decision Record, and the
/// Procedure Version (created by its own service). A <c>contact</c> is owner-only: no mapping (its registry entry), owned by the
/// customer business unit's default team through <c>IRecordOwnershipResolver</c>. While that team cannot be resolved (asked once
/// per run, review F2-b) the contact table is not swept: its watermark stays and its rows wait, and the run is partial.</para>
/// <para><b>Claims (ADR-036 A1 rule 3).</b> The host's lease holds the tick. Each table is a unit: an owner-scoped claim
/// (<c>IIdempotencyService</c>, owner = the run id) and, once the table is done for the run, a marker keyed to the run, so a host
/// retry of the run no-ops a marked table. A table another runner holds is not read; the run reports it and fails.</para>
/// <para><b>State (the distributed cache, as the stamp reconciliation's cursor).</b> Per table: the watermark (the end of the last
/// completed window), the window and cursor of a sweep the row cap stopped, and the rows to ask again with their attempt count.
/// Without a readable state store a table is not swept (every row is still kept for a later run).</para>
/// <para><b>Retry (A1 rule 4, NFR-04).</b> A row whose outcome <see cref="RecordRepairOutcome.NeedsRetry"/> is asked again by the
/// next run, up to <see cref="MaxAttempts"/> faults, then logged as abandoned (a contact that waits for the customer team's
/// configuration uses no attempt; the retry set is not capped, review K8). The job itself does not throw: the next tick is the
/// retry. <b>Heartbeat</b> (A1 rule 5): one structured line per attempt, with the counts and the correlation id, also in the
/// result JSON. A run is successful only when every table finished with nothing left to ask again.</para>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process <c>Spaarke.Scheduling</c> host, registered with
/// <c>AddScheduledJob</c> — BFF domain code (Repair mode, the ownership resolver, the field-mapping service) over BFF-owned
/// tables, BFF identity, low volume (rows created outside the product). No new scheduler, store, package or endpoint.</para>
/// <para>Component Justification (CLAUDE.md §11): (1) Existing — <c>CoreAncestorStampReconciliationJob</c> repairs the stamp only,
/// and only for rows filed under an intermediate; Repair mode had no caller. (2) Extension — the stamp job's question (stale
/// stamps) and cursor differ from this one's (rows the product did not create, forward-only); merging them would tie the stamp
/// safety net to the go-live switch. This job reuses Repair mode, the claim store and the state store. (3) Cost of doing nothing
/// — a row created on a form, by a flow or by an import never gets its mapping, its team owner or its stamp (FR-25).</para>
/// </remarks>
public sealed class RecordRepairPassJob : IScheduledJob
{
    public const string JobIdConstant = "record-repair-pass";

    /// <summary>Every 5 minutes (spec FR-25: a native row is repaired within about 10 minutes).</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    /// <summary>The go-live instant (ISO 8601, with an offset). Unset = the job is off: it reads and writes nothing.</summary>
    internal const string GoLiveConfigKey = "RecordRepair:Pass:GoLive";

    /// <summary>Retired product identities (comma-separated <c>systemuser</c> ids) whose rows the pass never writes.</summary>
    internal const string ProductPrincipalIdsConfigKey = "RecordRepair:Pass:ProductPrincipalIds";

    /// <summary>Rows swept per table per run (default <see cref="DefaultMaxRowsPerTable"/>); a larger window continues next run.</summary>
    internal const string MaxRowsPerTableConfigKey = "RecordRepair:Pass:MaxRowsPerTable";

    /// <summary>The ontology writer's identity setting: when set, the writer's <c>systemuser</c> is a product principal.</summary>
    internal const string OntologyWriterIdentityConfigKey = "Ontology:Writer:ManagedIdentityClientId";

    /// <summary>
    /// Review K6: the managed identity the BFF's Dataverse Web API client authenticates as (<c>DataverseWebApiService</c> reads
    /// this key first, then <see cref="WebApiIdentityFallbackConfigKey"/>). When set, the application user whose
    /// <c>applicationid</c> is that client id is a product principal: rows the Web API client creates are stamped with it.
    /// </summary>
    internal const string WebApiIdentityConfigKey = "ManagedIdentity:ClientId";

    /// <summary>Review K6: <c>DataverseWebApiService</c>'s fallback for <see cref="WebApiIdentityConfigKey"/>.</summary>
    internal const string WebApiIdentityFallbackConfigKey = "Graph:ManagedIdentity:ClientId";

    /// <summary>Review K5: each window starts this long before the watermark (Repair is fill-only and idempotent, so a row
    /// read twice is written at most once), so a row whose <c>createdon</c> lands just behind a saved watermark is not missed.</summary>
    internal static readonly TimeSpan WindowOverlap = TimeSpan.FromMinutes(15);

    /// <summary>Review F2-b: the table the customer-team gate applies to.</summary>
    internal const string ContactTable = RecordOwnershipResolver.ContactEntity;

    internal const int DefaultMaxRowsPerTable = 200;

    /// <summary>How many runs ask a row again before it is logged as abandoned.</summary>
    internal const int MaxAttempts = 12;

    /// <summary>A window ends this long before the run starts, so a row still being written by its creator is left for the next run.</summary>
    internal static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(2);

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";
    internal const string StatusNotConfigured = "not_configured";

    internal const string LogPrefix = "[RECORD-REPAIR-PASS]";

    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MarkerLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan StateLifetime = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// The registry tables the pass never reads: the owner's exclusions (<see cref="RecordCreationService.NotRepairedTables"/>),
    /// the append-only and not-created-here tables Repair skips, and the tables only their own writer creates (every row of
    /// those is a product principal's).
    /// </summary>
    internal static readonly IReadOnlySet<string> ExcludedTables = new HashSet<string>(
        RecordCreationService.NotRepairedTables
            .Concat(RecordCreationService.AppendOnlyTables)
            .Concat(RecordCreationService.NotCreatedHereTables)
            .Concat(RecordTableRules.All.Where(r => r.CreatedOnlyByItsWriter).Select(r => r.Table)),
        StringComparer.OrdinalIgnoreCase);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RecordRepairPassJob> _logger;

    public RecordRepairPassJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<RecordRepairPassJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The tables the pass sweeps, in registry order. Settable only so a test can sweep a subset.</summary>
    internal IReadOnlyList<string> Tables { get; init; } =
        RecordTableRules.All.Select(r => r.Table).Where(t => !ExcludedTables.Contains(t)).ToArray();

    /// <summary>
    /// The ontology writer's <c>systemuser</c> id, asked only when <see cref="OntologyWriterIdentityConfigKey"/> is set. Settable only
    /// so a test can stand in for the writer's own connection; by default its <c>WhoAmI</c>.
    /// </summary>
    internal Func<IServiceProvider, CancellationToken, Task<Guid>> OntologyWriterIdentity { get; init; } =
        (services, ct) => services.GetRequiredService<OntologyWriterDataverseClient>().GetWriterUserIdAsync(ct);

    public string JobId => JobIdConstant;

    public string DisplayName => "Record Repair Pass";

    public string Description =>
        "Gives each row created outside the product after the go-live instant its field mapping, owner and core-ancestor stamp "
        + "(Repair mode, fill-only). Runs every 5 minutes; off until " + GoLiveConfigKey + " is set.";

    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var runStart = _timeProvider.GetUtcNow();
        var counts = new RunCounts();
        var problems = new List<string>();
        string status;

        var rawGoLive = _configuration[GoLiveConfigKey];
        if (string.IsNullOrWhiteSpace(rawGoLive))
        {
            // Off: nothing is read, nothing is written. Logged as a warning (review K9): off is the default until go-live is
            // chosen, so it is not an error; the heartbeat says not_configured each run.
            _logger.LogWarning(
                "{Prefix} {Key} is not configured, so the repair pass is off: nothing was read or written. correlationId={CorrelationId}",
                LogPrefix, GoLiveConfigKey, context.CorrelationId);
            return Finish(StatusNotConfigured, success: true, counts, ["The repair pass is not configured (" + GoLiveConfigKey + " is unset)."],
                context, started, goLive: null);
        }

        // Review K7: exactly the ISO 8601 round-trip form ("O", which carries its offset); no culture-dependent or partial form.
        if (!DateTimeOffset.TryParseExact(rawGoLive.Trim(), "O", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal,
                out var goLive))
        {
            _logger.LogError(
                "{Prefix} {Key} is not an ISO 8601 round-trip instant (\"O\", for example 2026-11-01T00:00:00.0000000+00:00), so the "
                + "repair pass is off: nothing was read or written. correlationId={CorrelationId}",
                LogPrefix, GoLiveConfigKey, context.CorrelationId);
            return Finish(StatusError, success: false, counts, [GoLiveConfigKey + " does not parse as an instant; nothing was read or written."],
                context, started, goLive: null);
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var entities = services.GetRequiredService<IGenericEntityService>();

            var principals = await ProductPrincipalsAsync(services, entities, context, cancellationToken).ConfigureAwait(false);
            if (principals is null)
            {
                problems.Add("The product principals could not be decided, so no table was read and nothing was written.");
                return Finish(StatusError, success: false, counts, problems, context, started, goLive);
            }

            var idempotency = services.GetRequiredService<IIdempotencyService>();
            var state = services.GetService<IDistributedCache>();
            var repair = services.GetRequiredService<RecordCreationService>();
            var pass = new RecordRepairPassContext(goLive, principals);
            var until = runStart - SettleTime;

            if (state is null)
            {
                problems.Add("No state store (IDistributedCache) is registered, so no table was swept.");
                counts.StateFailures++;
            }
            else
            {
                foreach (var table in Tables)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Review F2-b: the contact table waits, untouched, while the customer team cannot be resolved.
                    if (string.Equals(table, ContactTable, StringComparison.OrdinalIgnoreCase)
                        && !await CustomerContactTeamResolvesAsync(services, context, cancellationToken).ConfigureAwait(false))
                    {
                        counts.TablesWaiting++;
                        problems.Add($"{table}: the customer business unit's default Owner team cannot be resolved; not swept this run.");
                        continue;
                    }

                    await RunTableAsync(table, entities, idempotency, state, repair, pass, until, counts, problems, context, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            counts.MetadataReads = pass.MetadataReads;
            status = counts.ScanFailures > 0 || counts.StateFailures > 0
                ? StatusError
                : counts.ClaimHeld > 0 || counts.Truncated > 0 || counts.Failed > 0 || counts.PendingRetry > 0 || counts.OutsidePass > 0
                  || counts.TablesWaiting > 0
                    ? StatusPartial
                    : StatusOk;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled before every table was swept.");
        }
        // A scheduled job's last line of defence: record the failure, never let it escape unrecorded.
        catch (Exception ex)
        {
            status = StatusError;
            problems.Add(ex.Message);
            _logger.LogError(ex, "{Prefix} Run failed; the pass did not complete. correlationId={CorrelationId}", LogPrefix, context.CorrelationId);
        }

        return Finish(status, success: status == StatusOk, counts, problems, context, started, goLive);
    }

    /// <summary>
    /// Review F2-b: whether the customer business unit's single default Owner team resolves, asked ONCE per run before the contact
    /// table (the identity-contact creates' own lookup, <see cref="ContactIdentityBinder.ResolveCreateOwnerAsync"/>). It decides no
    /// owner — each contact's owner is still the resolver's answer — it only says whether sweeping the table can re-own anything.
    /// When it does not resolve (unset, not one default team, no lookup composed, or the lookup faulted) the table is not swept: its
    /// watermark does not move and no row's attempt is counted, so the rows wait for the configuration. One line is logged.
    /// </summary>
    private async Task<bool> CustomerContactTeamResolvesAsync(IServiceProvider services, JobRunContext context, CancellationToken ct)
    {
        var lookup = services.GetService<ContactIdentityBinder>();
        ContactIdentityBinder.OwnerTeamStatus status;
        try
        {
            status = lookup is null
                ? ContactIdentityBinder.OwnerTeamStatus.Unconfigured
                : (await lookup.ResolveCreateOwnerAsync("Repair pass", "the contact table", ct).ConfigureAwait(false)).Status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Prefix} The customer business unit's default Owner team could not be read; the {Table} table is not "
                                   + "swept this run. correlationId={CorrelationId}", LogPrefix, ContactTable, context.CorrelationId);
            return false;
        }

        if (status == ContactIdentityBinder.OwnerTeamStatus.Resolved)
            return true;

        _logger.LogWarning(
            "{Prefix} The customer business unit's default Owner team cannot be resolved ({Status}; check {Key}); the {Table} table is not "
            + "swept and its rows wait. correlationId={CorrelationId}",
            LogPrefix, status, ExternalAccessOptionsValidator.CustomerBusinessUnitKey, ContactTable, context.CorrelationId);
        return false;
    }

    /// <summary>
    /// Task 050's marker: the <c>systemuser</c> ids whose rows the product created, or <c>null</c> (logged) when the set cannot be
    /// decided — the run then reads nothing. Never guessed: a fault, an empty id or a configured entry that does not parse is null.
    /// </summary>
    private async Task<IReadOnlySet<Guid>?> ProductPrincipalsAsync(
        IServiceProvider services, IGenericEntityService entities, JobRunContext context, CancellationToken ct)
    {
        var principals = new HashSet<Guid>();

        foreach (var entry in (_configuration[ProductPrincipalIdsConfigKey] ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(entry, out var id) || id == Guid.Empty)
            {
                _logger.LogError("{Prefix} {Key} holds an entry that is not a systemuser id; the run writes nothing. correlationId={CorrelationId}",
                    LogPrefix, ProductPrincipalIdsConfigKey, context.CorrelationId);
                return null;
            }

            principals.Add(id);
        }

        try
        {
            var self = await entities.GetCurrentIdentityIdAsync(ct).ConfigureAwait(false);
            if (self == Guid.Empty)
            {
                _logger.LogError("{Prefix} WhoAmI named no user; the run writes nothing. correlationId={CorrelationId}", LogPrefix, context.CorrelationId);
                return null;
            }

            principals.Add(self);

            // Review K6: the Web API client's identity, when it runs as a configured managed identity.
            var webApiClientId = !string.IsNullOrWhiteSpace(_configuration[WebApiIdentityConfigKey])
                ? _configuration[WebApiIdentityConfigKey]
                : _configuration[WebApiIdentityFallbackConfigKey];
            if (!string.IsNullOrWhiteSpace(webApiClientId))
            {
                if (!Guid.TryParse(webApiClientId.Trim(), out var applicationId) || applicationId == Guid.Empty)
                {
                    _logger.LogError("{Prefix} {Key} is not a client id; the run writes nothing. correlationId={CorrelationId}",
                        LogPrefix, WebApiIdentityConfigKey, context.CorrelationId);
                    return null;
                }

                var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("systemuserid"), TopCount = 2, NoLock = true };
                query.Criteria.AddCondition("applicationid", ConditionOperator.Equal, applicationId);
                var users = (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
                if (users.Count != 1 || users[0].Id == Guid.Empty)
                {
                    _logger.LogError(
                        "{Prefix} The Web API client's identity ({Key}) matches {Count} application users, not one; the run writes nothing. "
                        + "correlationId={CorrelationId}", LogPrefix, WebApiIdentityConfigKey, users.Count, context.CorrelationId);
                    return null;
                }

                principals.Add(users[0].Id);
            }

            if (!string.IsNullOrWhiteSpace(_configuration[OntologyWriterIdentityConfigKey]))
            {
                var writer = await OntologyWriterIdentity(services, ct).ConfigureAwait(false);
                if (writer == Guid.Empty)
                {
                    _logger.LogError("{Prefix} The ontology writer's identity is empty; the run writes nothing. correlationId={CorrelationId}",
                        LogPrefix, context.CorrelationId);
                    return null;
                }

                principals.Add(writer);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "{Prefix} A product identity could not be read; the run writes nothing. correlationId={CorrelationId}",
                LogPrefix, context.CorrelationId);
            return null;
        }

        return principals;
    }

    /// <summary>One table: claim it, ask again the rows a previous run kept, sweep its window, save its state, mark it done for the run.</summary>
    private async Task RunTableAsync(
        string table,
        IGenericEntityService entities,
        IIdempotencyService idempotency,
        IDistributedCache store,
        RecordCreationService repair,
        RecordRepairPassContext pass,
        DateTimeOffset until,
        RunCounts counts,
        List<string> problems,
        JobRunContext context,
        CancellationToken ct)
    {
        var claimKey = $"{JobIdConstant}:{table}";
        var markerKey = $"{claimKey}:{context.RunId:N}";
        var owner = context.RunId.ToString("N", CultureInfo.InvariantCulture);

        if (await idempotency.IsEventProcessedAsync(markerKey, ct).ConfigureAwait(false))
        {
            counts.AlreadyDone++;
            return;
        }

        if (!await idempotency.TryAcquireProcessingLockAsync(claimKey, owner, ClaimDuration, ct).ConfigureAwait(false))
        {
            counts.ClaimHeld++;
            problems.Add($"{table}: another runner holds it; not swept this run.");
            _logger.LogWarning("{Prefix} {Table} is claimed by another runner; not swept. correlationId={CorrelationId}",
                LogPrefix, table, context.CorrelationId);
            return;
        }

        var done = false;
        try
        {
            // Check again UNDER the claim: the claim is check-then-set and fails open (#984).
            if (await idempotency.IsEventProcessedAsync(markerKey, ct).ConfigureAwait(false))
            {
                counts.AlreadyDone++;
                return;
            }

            var stateKey = $"{claimKey}:state";
            var state = await ReadStateAsync(store, stateKey, ct).ConfigureAwait(false);
            if (state is null)
            {
                counts.StateFailures++;
                problems.Add($"{table}: its state could not be read, so it was not swept.");
                return;
            }

            var handled = new HashSet<Guid>();

            // The rows an earlier run kept, first (their attempt count goes up each run they still need asking).
            foreach (var (id, attempts) in state.Retry.ToArray())
            {
                ct.ThrowIfCancellationRequested();
                handled.Add(id);
                counts.Retried++;
                var outcome = await repair.RepairAsync(table, id, ct, pass).ConfigureAwait(false);
                Count(outcome, counts);
                var counted = attempts + AttemptOf(outcome);
                if (!outcome.NeedsRetry)
                {
                    state.Retry.Remove(id);
                }
                else if (counted >= MaxAttempts)
                {
                    state.Retry.Remove(id);
                    counts.Abandoned++;
                    _logger.LogError(
                        "{Prefix} {Table} {Id} was not repaired after {Attempts} runs ({Status} {Reason}); it is no longer asked. correlationId={CorrelationId}",
                        LogPrefix, table, id, counted, outcome.Status, ReasonOf(outcome), context.CorrelationId);
                }
                else
                {
                    state.Retry[id] = counted;
                }
            }

            // The sweep: a window that an earlier run started and the cap stopped continues; otherwise a new one, starting
            // WindowOverlap before the watermark (review K5), never before go-live.
            var since = Max(pass.GoLive, state.Watermark is { } mark && mark <= until ? mark - WindowOverlap : pass.GoLive);
            var windowEnd = state.SweepUntil ?? until;
            var scan = await ScanAsync(entities, table, since, windowEnd, pass.ProductPrincipals, state.SweepAfter, MaxRowsPerTable, ct)
                .ConfigureAwait(false);

            if (scan is null)
            {
                counts.ScanFailures++;
                problems.Add($"{table}: the scan failed, so no new row was read.");
            }
            else
            {
                foreach (var row in scan.Rows)
                {
                    ct.ThrowIfCancellationRequested();
                    counts.Scanned++;
                    if (!handled.Add(row.Id))
                        continue;

                    if (pass.RowOutsideThePass(row) is { } outside)
                    {
                        // The query bounds both; a row it returned anyway is not written, and the run is not clean.
                        counts.OutsidePass++;
                        _logger.LogWarning("{Prefix} {Table} {Id}: the scan returned a row outside the pass ({Reason}); not repaired. correlationId={CorrelationId}",
                            LogPrefix, table, row.Id, outside, context.CorrelationId);
                        continue;
                    }

                    var outcome = await repair.RepairAsync(table, row.Id, ct, pass).ConfigureAwait(false);
                    Count(outcome, counts);
                    if (outcome.NeedsRetry)
                    {
                        state.Retry[row.Id] = AttemptOf(outcome);
                    }
                }

                if (scan.Truncated)
                {
                    counts.Truncated++;
                    state.SweepUntil = windowEnd;
                    state.SweepAfter = scan.Rows[^1].Id;
                    problems.Add($"{table}: more than {MaxRowsPerTable} rows in its window; the next run continues after row {state.SweepAfter:D}.");
                }
                else
                {
                    state.Watermark = windowEnd;
                    state.SweepUntil = null;
                    state.SweepAfter = null;
                }
            }

            counts.PendingRetry += state.Retry.Count;
            if (state.Retry.Count > 0)
            {
                problems.Add($"{table}: {state.Retry.Count} row(s) to ask again next run.");
            }

            if (!await WriteStateAsync(store, stateKey, state, ct).ConfigureAwait(false))
            {
                counts.StateFailures++;
                problems.Add($"{table}: its state could not be saved; the next run asks the same window again.");
                return;
            }

            done = true;
        }
        finally
        {
            try
            {
                if (done)
                {
                    await idempotency.MarkEventAsProcessedAsync(markerKey, MarkerLifetime, CancellationToken.None).ConfigureAwait(false);
                }

                await idempotency.ReleaseProcessingLockAsync(claimKey, owner, CancellationToken.None).ConfigureAwait(false);
            }
            // The marker and the claim are advisory: Repair is fill-only and conditional, so a repeated row writes nothing.
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "{Prefix} Claim bookkeeping for {Key} failed; it expires on its own", LogPrefix, claimKey);
            }
        }
    }

    /// <summary>
    /// One table's window: rows with <c>createdon</c> in [<paramref name="since"/>, <paramref name="until"/>), created by no product
    /// principal, after <paramref name="after"/>, ordered by id, at most <paramref name="cap"/>. <c>null</c> when the read faulted.
    /// </summary>
    private async Task<ScanResult?> ScanAsync(
        IGenericEntityService entities,
        string table,
        DateTimeOffset since,
        DateTimeOffset until,
        IReadOnlySet<Guid> principals,
        Guid? after,
        int cap,
        CancellationToken ct)
    {
        try
        {
            var result = await entities.RetrieveMultipleAsync(BuildScanQuery(table, since, until, principals, after, cap), ct)
                .ConfigureAwait(false);
            var rows = result.Entities.Take(cap).ToList();
            return new ScanResult(rows, Truncated: rows.Count > 0 && (result.MoreRecords || result.Entities.Count > cap));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "{Prefix} The scan of {Table} failed; nothing new was read for it", LogPrefix, table);
            return null;
        }
    }

    /// <summary>The scan query of one table (see <see cref="ScanAsync"/>).</summary>
    internal static QueryExpression BuildScanQuery(
        string table, DateTimeOffset since, DateTimeOffset until, IReadOnlySet<Guid> principals, Guid? after, int cap)
    {
        var idColumn = table + "id";
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(idColumn, RecordRepairPassContext.CreatedOnColumn, RecordRepairPassContext.CreatedByColumn),
            NoLock = true,
            PageInfo = new PagingInfo { Count = cap, PageNumber = 1 },
        };

        query.Criteria.AddCondition(RecordRepairPassContext.CreatedOnColumn, ConditionOperator.GreaterEqual, since.UtcDateTime);
        query.Criteria.AddCondition(RecordRepairPassContext.CreatedOnColumn, ConditionOperator.LessThan, until.UtcDateTime);
        query.Criteria.AddCondition(RecordRepairPassContext.CreatedByColumn, ConditionOperator.NotNull);
        if (principals.Count > 0)
        {
            query.Criteria.AddCondition(RecordRepairPassContext.CreatedByColumn, ConditionOperator.NotIn,
                principals.Order().Cast<object>().ToArray());
        }

        if (after is { } continueAfter)
        {
            query.Criteria.AddCondition(idColumn, ConditionOperator.GreaterThan, continueAfter);
        }

        query.AddOrder(idColumn, OrderType.Ascending);
        return query;
    }

    /// <summary>Rows swept per table per run (<see cref="MaxRowsPerTableConfigKey"/>); a missing or invalid value is the default.</summary>
    private int MaxRowsPerTable =>
        int.TryParse(_configuration[MaxRowsPerTableConfigKey], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap) && cap > 0
            ? Math.Min(cap, 5000)
            : DefaultMaxRowsPerTable;

    private static void Count(RecordRepairOutcome outcome, RunCounts counts)
    {
        switch (outcome.Status)
        {
            case RecordRepairStatus.Repaired:
                counts.Repaired++;
                break;
            case RecordRepairStatus.NothingToRepair:
                counts.NoOp++;
                break;
            case RecordRepairStatus.Skipped:
                counts.Skipped++;
                break;
            default:
                counts.Failed++;
                break;
        }

        if (outcome.OwnerRefusal is not null)
        {
            counts.OwnerRefused++;
        }

        // Review F1-a: the owner was not written because the table cascades on Assign.
        if (outcome.Fields.Any(f => f.ReasonCode == RecordRepairReason.OwnerAssignCascades))
        {
            counts.OwnerAssignCascades++;
        }
    }

    private static string? ReasonOf(RecordRepairOutcome outcome) => outcome.ReasonCode ?? outcome.OwnerRefusal?.Code;

    /// <summary>
    /// Review F2-b: what one more ask of a row counts toward <see cref="MaxAttempts"/> — 1 for a fault, 0 for a contact that waits
    /// for the customer team's configuration (<see cref="RecordOwnerRefusal.CustomerContactTeamUnresolved"/>): only real faults
    /// use up a row's attempts.
    /// </summary>
    private static int AttemptOf(RecordRepairOutcome outcome) =>
        outcome.OwnerRefusal?.Code == RecordOwnerRefusal.CustomerContactTeamUnresolved ? 0 : 1;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    /// <summary>The table's state, a new one when there is none, or <c>null</c> when the store faulted or the entry is unreadable.</summary>
    private async Task<TableState?> ReadStateAsync(IDistributedCache store, string key, CancellationToken ct)
    {
        try
        {
            var raw = await store.GetStringAsync(key, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(raw))
            {
                return new TableState();
            }

            var state = JsonSerializer.Deserialize<TableState>(raw, JsonOptions);
            return state is null ? null : state with { Retry = state.Retry ?? [] };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "{Prefix} The state {Key} could not be read", LogPrefix, key);
            return null;
        }
    }

    private async Task<bool> WriteStateAsync(IDistributedCache store, string key, TableState state, CancellationToken ct)
    {
        try
        {
            await store.SetStringAsync(key, JsonSerializer.Serialize(state, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StateLifetime }, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "{Prefix} The state {Key} could not be saved", LogPrefix, key);
            return false;
        }
    }

    private JobRunResult Finish(
        string status, bool success, RunCounts counts, List<string> problems, JobRunContext context, long started, DateTimeOffset? goLive)
    {
        var duration = _timeProvider.GetElapsedTime(started);

        _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "{Prefix} heartbeat status={Status} attempt={Attempt} goLive={GoLive} scanned={Scanned} repaired={Repaired} noOp={NoOp} "
            + "skipped={Skipped} failed={Failed} ownerRefused={OwnerRefused} ownerAssignCascades={OwnerAssignCascades} "
            + "tablesWaiting={TablesWaiting} outsidePass={OutsidePass} retried={Retried} "
            + "pendingRetry={PendingRetry} abandoned={Abandoned} claimHeld={ClaimHeld} alreadyDone={AlreadyDone} truncated={Truncated} "
            + "scanFailures={ScanFailures} stateFailures={StateFailures} metadataReads={MetadataReads} durationMs={DurationMs} "
            + "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            LogPrefix, status, context.Attempt, goLive?.ToString("O", CultureInfo.InvariantCulture) ?? "(unset)", counts.Scanned,
            counts.Repaired, counts.NoOp, counts.Skipped, counts.Failed, counts.OwnerRefused, counts.OwnerAssignCascades,
            counts.TablesWaiting, counts.OutsidePass, counts.Retried,
            counts.PendingRetry, counts.Abandoned, counts.ClaimHeld, counts.AlreadyDone, counts.Truncated, counts.ScanFailures,
            counts.StateFailures, counts.MetadataReads, (long)duration.TotalMilliseconds, context.Trigger, context.RunId,
            context.CorrelationId);

        return new JobRunResult(
            Success: success,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: counts.Repaired,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    attempt = context.Attempt,
                    correlationId = context.CorrelationId,
                    goLive = goLive?.ToString("O", CultureInfo.InvariantCulture),
                    counts.Scanned,
                    counts.Repaired,
                    counts.NoOp,
                    counts.Skipped,
                    counts.Failed,
                    counts.OwnerRefused,
                    counts.OwnerAssignCascades,
                    counts.TablesWaiting,
                    counts.OutsidePass,
                    counts.Retried,
                    counts.PendingRetry,
                    counts.Abandoned,
                    counts.ClaimHeld,
                    counts.AlreadyDone,
                    counts.Truncated,
                    counts.ScanFailures,
                    counts.StateFailures,
                    counts.MetadataReads,
                },
                JsonOptions));
    }

    /// <summary>One table's sweep: the rows read, and whether the cap stopped it.</summary>
    private sealed record ScanResult(List<Entity> Rows, bool Truncated);

    /// <summary>One table's state between runs (see the remarks).</summary>
    internal sealed record TableState
    {
        /// <summary>The end of the last completed window: the next window starts here.</summary>
        public DateTimeOffset? Watermark { get; set; }

        /// <summary>The end of a window the row cap stopped, kept until the window is finished.</summary>
        public DateTimeOffset? SweepUntil { get; set; }

        /// <summary>The last row id a stopped sweep read: it continues after this id.</summary>
        public Guid? SweepAfter { get; set; }

        /// <summary>The rows to ask again, with how many runs have asked each.</summary>
        public Dictionary<Guid, int> Retry { get; init; } = [];
    }

    private sealed class RunCounts
    {
        public int Scanned;
        public int Repaired;
        public int NoOp;
        public int Skipped;
        public int Failed;
        public int OwnerRefused;
        public int OwnerAssignCascades;
        public int TablesWaiting;
        public int OutsidePass;
        public int Retried;
        public int PendingRetry;
        public int Abandoned;
        public int ClaimHeld;
        public int AlreadyDone;
        public int Truncated;
        public int ScanFailures;
        public int StateFailures;
        public int MetadataReads;
    }
}
