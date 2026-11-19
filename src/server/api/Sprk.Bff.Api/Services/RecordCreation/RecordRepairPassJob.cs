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
/// (<c>IIdempotencyService</c>, owner = the run id), released when the table is done. No per-run marker: the host retries a run
/// only when the job throws, which it never does, and Repair is fill-only, so a second sweep of a row writes nothing. A table
/// another runner holds is not read; the run reports it and fails.</para>
/// <para><b>State (the distributed cache, as the stamp reconciliation's cursor).</b> Per table: the watermark (the end of the last
/// completed window), the window and cursor of a sweep the row cap stopped, and the rows to ask again with their attempt count.
/// Without a readable state store a table is not swept (every row is still kept for a later run).</para>
/// <para><b>Retry (A1 rule 4, NFR-04).</b> A row whose outcome <see cref="RecordRepairOutcome.NeedsRetry"/> is asked again by the
/// next run, up to <see cref="MaxAttempts"/> faults, then logged as abandoned (a contact that waits for the customer team's
/// configuration uses no attempt; the retry set is not capped, review K8). The job itself does not throw: the next tick is the
/// retry. <b>Heartbeat</b> (A1 rule 5): one structured line per attempt, with the counts and the correlation id, also in the
/// result JSON. A run is successful only when every table finished with nothing left to ask again.</para>
/// <para><b>On demand (task 052c).</b> An administrator's run through <c>POST /api/admin/jobs/record-repair-pass/runs</c> repairs
/// ONE table over ONE <c>createdon</c> window (<see cref="RecordRepairOnDemandRun"/>), through this same job, the host's lease, the
/// same per-table claim, the same forward-only and product-principal rules and the same Repair mode; only the selection differs.
/// It is on demand only for a <see cref="JobRunTrigger.ManualAdmin"/> run carrying the route's typed value
/// (<see cref="OnDemandRunOf"/>). The job checks the window against go-live itself before reading anything. It never reads or
/// writes the scheduled pass's state. It pages the window (id order) up to <see cref="DefaultOnDemandRowBudget"/> rows, then reports it as
/// truncated with the last id covered, which the next run takes as <c>afterId</c>; it scans no later than the scheduled pass would
/// (run start minus <see cref="SettleTime"/>) and reports a window that reached past that point as not clean.</para>
/// <para><b>Report (task 052d).</b> Each scheduled run also reports, writing nothing, the Signal and Decision Record rows created
/// at or after go-live that hold one half of the core pair only or whose pair columns were updated
/// (<see cref="RecordRepairCorePairReport"/>, its own state entries), and each swept table's retry-set size and the ids abandoned
/// this run, in the result JSON and the heartbeat.</para>
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

    /// <summary>
    /// Task 052c (review F4): the most rows ONE on-demand run reads, across as many pages of <see cref="MaxRowsPerTableConfigKey"/>
    /// as it takes. 500 keeps a run inside the table claim (<c>ClaimDuration</c>, 15 minutes) at a pessimistic second per row (each
    /// row is one Repair: a read and at most one conditional write). A window with more rows stops here and names the last id it
    /// covered; the administrator continues with <c>afterId</c>, so no row is out of reach however narrow the window.
    /// </summary>
    internal const int DefaultOnDemandRowBudget = 500;

    /// <summary>How many runs ask a row again before it is logged as abandoned.</summary>
    internal const int MaxAttempts = 12;

    /// <summary>Task 052d: the abandoned ids listed per table in the result and the heartbeat (every one is also logged).</summary>
    internal const int MaxAbandonedIdsListed = 50;

    /// <summary>A window ends this long before the run starts, so a row still being written by its creator is left for the next run.</summary>
    internal static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(2);

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";
    internal const string StatusNotConfigured = "not_configured";

    /// <summary>Task 052c: the run's mode in the heartbeat and the result.</summary>
    internal const string ModeScheduled = "scheduled";

    internal const string ModeOnDemand = "on_demand";

    internal const string LogPrefix = "[RECORD-REPAIR-PASS]";

    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(15);
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

    /// <summary>The on-demand row budget (<see cref="DefaultOnDemandRowBudget"/>). Settable only so a test can use a small one.</summary>
    internal int OnDemandRowBudget { get; init; } = DefaultOnDemandRowBudget;

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

        // Task 052c: on-demand mode only for an administrator's run that carries the route's typed value; anything else is the
        // scheduled pass.
        var onDemand = OnDemandRunOf(context);
        if (onDemand is null && context.Parameters?.ContainsKey(RecordRepairOnDemandRun.ParameterKey) == true)
        {
            _logger.LogWarning(
                "{Prefix} The run's {Key} parameter was not used: an on-demand run starts only from the admin route, as a manual run. "
                + "This run is the scheduled pass. trigger={Trigger} correlationId={CorrelationId}",
                LogPrefix, RecordRepairOnDemandRun.ParameterKey, context.Trigger, context.CorrelationId);
        }

        var goLiveSetting = ReadGoLive(_configuration[GoLiveConfigKey], out var goLive);
        switch (goLiveSetting)
        {
            case GoLiveSetting.Unset:
                // Off: nothing is read, nothing is written. Logged as a warning (review K9): off is the default until go-live is
                // chosen, so it is not an error; the heartbeat says not_configured each run. An on-demand run cannot do what the
                // administrator asked, so it fails.
                _logger.LogWarning(
                    "{Prefix} {Key} is not configured, so the repair pass is off: nothing was read or written. correlationId={CorrelationId}",
                    LogPrefix, GoLiveConfigKey, context.CorrelationId);
                return Finish(StatusNotConfigured, success: onDemand is null, counts,
                    ["The repair pass is not configured (" + GoLiveConfigKey + " is unset)."], context, started, goLive: null, onDemand);

            case GoLiveSetting.Invalid:
                _logger.LogError(
                    "{Prefix} {Key} is not an ISO 8601 round-trip instant (\"O\", for example 2026-11-01T00:00:00.0000000+00:00), so the "
                    + "repair pass is off: nothing was read or written. correlationId={CorrelationId}",
                    LogPrefix, GoLiveConfigKey, context.CorrelationId);
                return Finish(StatusError, success: false, counts, [GoLiveConfigKey + " does not parse as an instant; nothing was read or written."],
                    context, started, goLive: null, onDemand);
        }

        // Task 052c (forward-only, the job's own check): the route is not the only guard. A run whose window starts before go-live,
        // reaches into the future, or names a table this pass does not sweep reads nothing and fails.
        if (onDemand is not null
            && RecordRepairOnDemandRun.Check(onDemand.Table, onDemand.CreatedFrom, onDemand.CreatedTo, goLive, runStart, Tables) is { } refused)
        {
            _logger.LogError(
                "{Prefix} The on-demand run {Run} is refused ({Reason}): nothing was read or written. correlationId={CorrelationId}",
                LogPrefix, onDemand, refused.Code, context.CorrelationId);
            return Finish(StatusError, success: false, counts, [refused.Detail + " Nothing was read or written."], context, started, goLive,
                onDemand);
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
                return Finish(StatusError, success: false, counts, problems, context, started, goLive, onDemand);
            }

            var idempotency = services.GetRequiredService<IIdempotencyService>();
            var state = services.GetService<IDistributedCache>();
            var repair = services.GetRequiredService<RecordCreationService>();
            var pass = new RecordRepairPassContext(goLive, principals);
            var until = runStart - SettleTime;

            if (onDemand is not null)
            {
                // Task 052c: one table, one window. The scheduled pass's state (watermark, sweep, retries) is neither read nor written.
                if (string.Equals(onDemand.Table, ContactTable, StringComparison.OrdinalIgnoreCase)
                    && !await CustomerContactTeamResolvesAsync(services, context, cancellationToken).ConfigureAwait(false))
                {
                    counts.TablesWaiting++;
                    problems.Add($"{onDemand.Table}: the customer business unit's default Owner team cannot be resolved; not swept.");
                }
                else
                {
                    await RunOnDemandTableAsync(onDemand, entities, idempotency, repair, pass, until, counts, problems, context, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else if (state is null)
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

                // Task 052d: the core-pair report over the two tables the pass never writes (reads only; its own state entries).
                // Scheduled runs only: an on-demand run repairs one other table over one window.
                counts.Report = await new RecordRepairCorePairReport(entities, idempotency, state, pass, _logger, context)
                    .RunAsync(until, cancellationToken).ConfigureAwait(false);
                problems.AddRange(counts.Report.Problems);
            }

            counts.MetadataReads = pass.MetadataReads;
            status = counts.ScanFailures > 0 || counts.StateFailures > 0
                ? StatusError
                : counts.ClaimHeld > 0 || counts.Truncated > 0 || counts.Failed > 0 || counts.PendingRetry > 0 || counts.OutsidePass > 0
                  || counts.TablesWaiting > 0 || counts.NeedsRetry > 0 || counts.SettleClamped > 0 || counts.Report is { IsClean: false }
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

        return Finish(status, success: status == StatusOk, counts, problems, context, started, goLive, onDemand);
    }

    /// <summary>
    /// Task 052c: the on-demand run this context carries, or <c>null</c> for the scheduled pass. Only a
    /// <see cref="JobRunTrigger.ManualAdmin"/> run whose parameters hold a <see cref="RecordRepairOnDemandRun"/> instance (built by
    /// the admin route through its validation) under <see cref="RecordRepairOnDemandRun.ParameterKey"/> is on demand: a scheduled
    /// tick never is, and a persisted <c>ConfigJson</c> reaches the run as one string, which is never that type.
    /// </summary>
    internal static RecordRepairOnDemandRun? OnDemandRunOf(JobRunContext context) =>
        context.Trigger == JobRunTrigger.ManualAdmin
        && context.Parameters is { } parameters
        && parameters.TryGetValue(RecordRepairOnDemandRun.ParameterKey, out var value)
        && value is RecordRepairOnDemandRun run
            ? run
            : null;

    /// <summary>How <see cref="GoLiveConfigKey"/> reads (see <see cref="ReadGoLive"/>).</summary>
    internal enum GoLiveSetting
    {
        Unset,
        Invalid,
        Set,
    }

    /// <summary>
    /// The go-live setting: unset (the pass is off), not an ISO 8601 round-trip instant (review K7: exactly <c>"O"</c>, which
    /// carries its offset; no culture-dependent or partial form), or set (<paramref name="goLive"/>, UTC). Read by the job each run
    /// and by the admin route before it starts an on-demand run.
    /// </summary>
    internal static GoLiveSetting ReadGoLive(string? raw, out DateTimeOffset goLive)
    {
        goLive = default;
        if (string.IsNullOrWhiteSpace(raw))
            return GoLiveSetting.Unset;

        return DateTimeOffset.TryParseExact(raw.Trim(), "O", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out goLive)
            ? GoLiveSetting.Set
            : GoLiveSetting.Invalid;
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
        var owner = context.RunId.ToString("N", CultureInfo.InvariantCulture);

        if (!await idempotency.TryAcquireProcessingLockAsync(claimKey, owner, ClaimDuration, ct).ConfigureAwait(false))
        {
            counts.ClaimHeld++;
            problems.Add($"{table}: another runner holds it; not swept this run.");
            _logger.LogWarning("{Prefix} {Table} is claimed by another runner; not swept. correlationId={CorrelationId}",
                LogPrefix, table, context.CorrelationId);
            return;
        }

        try
        {
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
                    counts.AbandonedOf(table).Add(id);
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
            counts.RetrySets[table] = state.Retry.Count; // task 052d (K8): each table's retry-set size
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
        }
        finally
        {
            try
            {
                await idempotency.ReleaseProcessingLockAsync(claimKey, owner, CancellationToken.None).ConfigureAwait(false);
            }
            // The claim is advisory: Repair is fill-only and conditional, so a repeated row writes nothing.
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "{Prefix} Claim bookkeeping for {Key} failed; it expires on its own", LogPrefix, claimKey);
            }
        }
    }

    /// <summary>
    /// Task 052c: one on-demand run — the table's claim (the same key as the scheduled pass, owner = this run id), then the window
    /// scanned page by page (pages of <see cref="MaxRowsPerTable"/>, ordered by id, continuing after the last id read) until it is
    /// exhausted or <see cref="OnDemandRowBudget"/> rows were read, each row asked through the same rule and Repair as the
    /// scheduled pass. The scan's upper bound is the window's end or the scheduled pass's own settle point (run start minus
    /// <see cref="SettleTime"/>), whichever is earlier: a row still being written by its creator is left, and the run says so.
    /// The scheduled pass's state is neither read nor written: its watermark, sweep cursor and retry set stay as they are, and a row
    /// that needs asking again is reported (<c>needsRetry</c>), not kept. A window with more rows than the budget reports truncated
    /// with the last id covered, and the run's <c>afterId</c> continues from there.
    /// </summary>
    private async Task RunOnDemandTableAsync(
        RecordRepairOnDemandRun run,
        IGenericEntityService entities,
        IIdempotencyService idempotency,
        RecordCreationService repair,
        RecordRepairPassContext pass,
        DateTimeOffset settledUntil,
        RunCounts counts,
        List<string> problems,
        JobRunContext context,
        CancellationToken ct)
    {
        var table = run.Table;
        var scanUntil = run.CreatedTo <= settledUntil ? run.CreatedTo : settledUntil;
        counts.ScanUntil = scanUntil;
        counts.AfterId = run.AfterId;

        if (scanUntil < run.CreatedTo)
        {
            counts.SettleClamped++;
            problems.Add($"{table}: rows created after {scanUntil.ToString("O", CultureInfo.InvariantCulture)} (the last "
                         + $"{SettleTime.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes) were left, as the scheduled pass leaves them; "
                         + "run the rest of the window again later.");
        }

        if (scanUntil <= run.CreatedFrom)
        {
            // The whole window lies inside the settle time: nothing is eligible yet, so nothing is read and no claim is taken.
            return;
        }

        var claimKey = $"{JobIdConstant}:{table}";
        var owner = context.RunId.ToString("N", CultureInfo.InvariantCulture);

        if (!await idempotency.TryAcquireProcessingLockAsync(claimKey, owner, ClaimDuration, ct).ConfigureAwait(false))
        {
            counts.ClaimHeld++;
            problems.Add($"{table}: another runner holds it; nothing was read or written.");
            _logger.LogWarning("{Prefix} {Table} is claimed by another runner; the on-demand run reads nothing. correlationId={CorrelationId}",
                LogPrefix, table, context.CorrelationId);
            return;
        }

        try
        {
            var budget = Math.Max(1, OnDemandRowBudget);
            var pageSize = Math.Min(MaxRowsPerTable, budget);
            var after = run.AfterId;

            while (true)
            {
                var page = Math.Min(pageSize, budget - counts.Scanned);
                var scan = await ScanAsync(entities, table, run.CreatedFrom, scanUntil, pass.ProductPrincipals, after, page, ct)
                    .ConfigureAwait(false);
                if (scan is null)
                {
                    counts.ScanFailures++;
                    problems.Add($"{table}: the scan failed, so no further row was read.");
                    return;
                }

                foreach (var row in scan.Rows)
                {
                    ct.ThrowIfCancellationRequested();
                    counts.Scanned++;

                    if (pass.RowOutsideThePass(row) is { } outside)
                    {
                        counts.OutsidePass++;
                        _logger.LogWarning("{Prefix} {Table} {Id}: the scan returned a row outside the pass ({Reason}); not repaired. correlationId={CorrelationId}",
                            LogPrefix, table, row.Id, outside, context.CorrelationId);
                        continue;
                    }

                    var outcome = await repair.RepairAsync(table, row.Id, ct, pass).ConfigureAwait(false);
                    Count(outcome, counts);
                    if (outcome.NeedsRetry)
                    {
                        counts.NeedsRetry++;
                    }
                }

                if (!scan.Truncated)
                {
                    break; // the window is exhausted
                }

                after = scan.Rows[^1].Id;
                if (counts.Scanned >= budget)
                {
                    counts.Truncated++;
                    counts.LastIdCovered = after;
                    problems.Add($"{table}: more than {budget} rows in the window; rows through id {after:D} were covered. "
                                 + $"Narrow the window, or run it again with afterId {after:D}.");
                    break;
                }
            }

            if (counts.NeedsRetry > 0)
            {
                problems.Add($"{table}: {counts.NeedsRetry} row(s) could not be finished; run the window again.");
            }
        }
        finally
        {
            try
            {
                await idempotency.ReleaseProcessingLockAsync(claimKey, owner, CancellationToken.None).ConfigureAwait(false);
            }
            // The claim is advisory: Repair is fill-only and conditional, so a repeated row writes nothing.
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

        // Review F1-a / 052f: the owner was not written because the row has a child along an unaccepted Assign cascade.
        if (outcome.Fields.Any(f => f.ReasonCode == RecordRepairReason.OwnerAssignCascades))
        {
            counts.OwnerAssignCascades++;
            counts.OwnerAssignCascadesByTable[outcome.Table] = counts.OwnerAssignCascadesByTable.GetValueOrDefault(outcome.Table) + 1;
        }

        // Task 052f (the race, K1): children that moved with the owner write after the check; logged by Repair with table and id.
        counts.OwnerAssignCascadeRaced += outcome.RacedChildren.Count;
        if (outcome.RaceUnchecked)
        {
            counts.OwnerAssignCascadeRaceUnchecked++;
        }

        // Task 052g (design 052e §3.3): a confirmed re-own across business units, and the rows Repair left to the secure pass
        // (an answer, or undecided: counted in both, the undecided ones also on their own).
        if (outcome.Wrote && outcome.MovedBusinessUnit is not null)
        {
            counts.OwnerMovedBusinessUnit++;
        }

        if (outcome.Fields.Any(f => f.ReasonCode is RecordRepairReason.OwnerLeftToSecurePass or RecordRepairReason.OwnerIsolationUnknown))
        {
            counts.OwnerLeftToSecurePass++;
        }

        if (outcome.Fields.Any(f => f.ReasonCode == RecordRepairReason.OwnerIsolationUnknown))
        {
            counts.OwnerIsolationUnknown++;
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
        string status, bool success, RunCounts counts, List<string> problems, JobRunContext context, long started, DateTimeOffset? goLive,
        RecordRepairOnDemandRun? onDemand)
    {
        var duration = _timeProvider.GetElapsedTime(started);
        var mode = onDemand is null ? ModeScheduled : ModeOnDemand;
        var createdFrom = onDemand?.CreatedFrom.ToString("O", CultureInfo.InvariantCulture);
        var createdTo = onDemand?.CreatedTo.ToString("O", CultureInfo.InvariantCulture);
        var scanUntil = counts.ScanUntil?.ToString("O", CultureInfo.InvariantCulture);
        var afterId = counts.AfterId?.ToString("D", CultureInfo.InvariantCulture);
        var lastIdCovered = counts.LastIdCovered?.ToString("D", CultureInfo.InvariantCulture);

        // Task 052d (K6, K8): each swept table's retry-set size and the ids abandoned this run (listed up to MaxAbandonedIdsListed
        // per table; each abandoned row is also logged on its own). The heartbeat names the tables whose set is not empty.
        var retrySets = counts.RetrySets.Select(kv =>
        {
            var abandoned = counts.AbandonedIds.GetValueOrDefault(kv.Key) ?? [];
            return new
            {
                table = kv.Key,
                size = kv.Value,
                abandoned = abandoned.Take(MaxAbandonedIdsListed).Select(id => id.ToString("D", CultureInfo.InvariantCulture)).ToArray(),
                abandonedTruncated = abandoned.Count > MaxAbandonedIdsListed,
            };
        }).ToArray();
        var retrySetsText = string.Join(",", counts.RetrySets.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}={kv.Value}"));
        var abandonedText = string.Join(",", retrySets.Where(r => r.abandoned.Length > 0)
            .Select(r => $"{r.table}:{string.Join("|", r.abandoned)}{(r.abandonedTruncated ? "|..." : string.Empty)}"));
        var byTableText = string.Join(",", counts.OwnerAssignCascadesByTable.Select(kv => $"{kv.Key}={kv.Value}"));
        var report = counts.Report;
        var findings = report?.Findings.Select(f => new
        {
            table = f.Table,
            id = f.Id.ToString("D", CultureInfo.InvariantCulture),
            finding = f.Code,
            auditTime = f.AuditTime?.ToString("O", CultureInfo.InvariantCulture),
            changedBy = f.ChangedBy?.ToString("D", CultureInfo.InvariantCulture),
        }).ToArray() ?? [];

        _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "{Prefix} heartbeat status={Status} mode={Mode} table={Table} createdFrom={CreatedFrom} createdTo={CreatedTo} scanUntil={ScanUntil} "
            + "afterId={AfterId} lastIdCovered={LastIdCovered} attempt={Attempt} "
            + "goLive={GoLive} scanned={Scanned} repaired={Repaired} noOp={NoOp} "
            + "skipped={Skipped} failed={Failed} ownerRefused={OwnerRefused} ownerAssignCascades={OwnerAssignCascades} "
            + "tablesWaiting={TablesWaiting} outsidePass={OutsidePass} retried={Retried} "
            + "pendingRetry={PendingRetry} needsRetry={NeedsRetry} abandoned={Abandoned} claimHeld={ClaimHeld} truncated={Truncated} "
            + "scanFailures={ScanFailures} stateFailures={StateFailures} metadataReads={MetadataReads} "
            + "retrySets={RetrySets} abandonedIds={AbandonedIds} corePairHalf={CorePairHalf} corePairChanged={CorePairChanged} "
            + "corePairUndecided={CorePairUndecided} corePairRowsUnread={CorePairRowsUnread} reportTruncated={ReportTruncated} "
            + "reportFailures={ReportFailures} ownerAssignCascadesByTable={OwnerAssignCascadesByTable} "
            + "ownerAssignCascadeRaced={OwnerAssignCascadeRaced} ownerAssignCascadeRaceUnchecked={OwnerAssignCascadeRaceUnchecked} "
            + "ownerMovedBusinessUnit={OwnerMovedBusinessUnit} ownerLeftToSecurePass={OwnerLeftToSecurePass} "
            + "ownerIsolationUnknown={OwnerIsolationUnknown} "
            + "durationMs={DurationMs} "
            + "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            LogPrefix, status, mode, onDemand?.Table ?? "(all)", createdFrom ?? "-", createdTo ?? "-", scanUntil ?? "-", afterId ?? "-", lastIdCovered ?? "-", context.Attempt,
            goLive?.ToString("O", CultureInfo.InvariantCulture) ?? "(unset)", counts.Scanned,
            counts.Repaired, counts.NoOp, counts.Skipped, counts.Failed, counts.OwnerRefused, counts.OwnerAssignCascades,
            counts.TablesWaiting, counts.OutsidePass, counts.Retried,
            counts.PendingRetry, counts.NeedsRetry, counts.Abandoned, counts.ClaimHeld, counts.Truncated, counts.ScanFailures,
            counts.StateFailures, counts.MetadataReads,
            retrySetsText.Length > 0 ? retrySetsText : "-", abandonedText.Length > 0 ? abandonedText : "-",
            report?.Half ?? 0, report?.Changed ?? 0, report?.Undecided ?? 0, report?.RowsUnread ?? 0,
            (report?.Truncated ?? 0) + (report?.FindingsTruncated == true ? 1 : 0), report?.Failures ?? 0,
            byTableText.Length > 0 ? byTableText : "-", counts.OwnerAssignCascadeRaced, counts.OwnerAssignCascadeRaceUnchecked,
            counts.OwnerMovedBusinessUnit, counts.OwnerLeftToSecurePass, counts.OwnerIsolationUnknown,
            (long)duration.TotalMilliseconds, context.Trigger, context.RunId,
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
                    mode,
                    table = onDemand?.Table,
                    createdFrom,
                    createdTo,
                    scanUntil,
                    afterId,
                    lastIdCovered,
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
                    counts.OwnerAssignCascadesByTable,
                    counts.OwnerAssignCascadeRaced,
                    counts.OwnerAssignCascadeRaceUnchecked,
                    counts.OwnerMovedBusinessUnit,
                    counts.OwnerLeftToSecurePass,
                    counts.OwnerIsolationUnknown,
                    counts.TablesWaiting,
                    counts.OutsidePass,
                    counts.Retried,
                    counts.PendingRetry,
                    counts.NeedsRetry,
                    counts.Abandoned,
                    counts.ClaimHeld,
                    counts.Truncated,
                    counts.SettleClamped,
                    counts.ScanFailures,
                    counts.StateFailures,
                    counts.MetadataReads,
                    retrySets,
                    corePairHalf = report?.Half ?? 0,
                    corePairChanged = report?.Changed ?? 0,
                    corePairUndecided = report?.Undecided ?? 0,
                    corePairRowsUnread = report?.RowsUnread ?? 0,
                    reportRowsRead = report?.RowsRead ?? 0,
                    reportAuditRowsRead = report?.AuditRowsRead ?? 0,
                    reportTruncated = report?.Truncated ?? 0,
                    reportFailures = report?.Failures ?? 0,
                    reportClaimHeld = report?.ClaimHeld ?? 0,
                    corePairFindingsTruncated = report?.FindingsTruncated ?? false,
                    corePairFindings = findings,
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

        /// <summary>Task 052f: the rows kept with their owner for an unaccepted child, per table.</summary>
        public SortedDictionary<string, int> OwnerAssignCascadesByTable { get; } = new(StringComparer.Ordinal);

        /// <summary>Task 052f (the race, K1): children that moved with an owner write after the check did not find them.</summary>
        public int OwnerAssignCascadeRaced;

        /// <summary>Task 052f (the race): re-owned rows whose children could not be read again after the write.</summary>
        public int OwnerAssignCascadeRaceUnchecked;

        /// <summary>Task 052g: rows re-owned (confirmed) to a team in another business unit.</summary>
        public int OwnerMovedBusinessUnit;

        /// <summary>Task 052g: rows whose owner Repair left to the secure pass (isolated, or whether isolated could not be told).</summary>
        public int OwnerLeftToSecurePass;

        /// <summary>Task 052g: of those, the rows whose isolation could not be told (asked again).</summary>
        public int OwnerIsolationUnknown;
        public int TablesWaiting;
        public int OutsidePass;
        public int Retried;
        public int PendingRetry;

        /// <summary>Task 052c: rows an on-demand run could not finish (it keeps no retry set; the administrator runs the window again).</summary>
        public int NeedsRetry;

        public int Abandoned;
        public int ClaimHeld;
        public int Truncated;

        /// <summary>Task 052c: 1 when an on-demand run left the window's last settle-time minutes unread (the scheduled pass leaves them too).</summary>
        public int SettleClamped;

        /// <summary>Task 052c: the upper bound an on-demand run scanned to (the window end, or the settle point when that is earlier).</summary>
        public DateTimeOffset? ScanUntil;

        /// <summary>Task 052c: the id an on-demand run continued after (its <c>afterId</c>).</summary>
        public Guid? AfterId;

        /// <summary>Task 052c: when the row budget stopped an on-demand run, the last row id it covered.</summary>
        public Guid? LastIdCovered;
        public int ScanFailures;
        public int StateFailures;
        public int MetadataReads;

        /// <summary>Task 052d (K8): each swept table's retry-set size after this run.</summary>
        public SortedDictionary<string, int> RetrySets { get; } = new(StringComparer.Ordinal);

        /// <summary>Task 052d (K6): the ids abandoned this run, per table.</summary>
        public Dictionary<string, List<Guid>> AbandonedIds { get; } = new(StringComparer.Ordinal);

        public List<Guid> AbandonedOf(string table) =>
            AbandonedIds.TryGetValue(table, out var ids) ? ids : AbandonedIds[table] = [];

        /// <summary>Task 052d: the core-pair report of a scheduled run; <c>null</c> when it did not run.</summary>
        public RecordRepairCorePairReport.Result? Report;
    }
}
