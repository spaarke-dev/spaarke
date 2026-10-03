using System.Globalization;
using System.Text.Json;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 141 — keeps every licensed systemuser linked to its contact
/// (<c>contact.sprk_externalobjectid</c> = <c>systemuser.azureactivedirectoryobjectid</c> AND
/// <c>systemuser.sprk_primarycontact</c> pointing at that contact) or carrying a durable collision flag — in this
/// BFF's own environment AND in every environment this BFF provisions users into.
/// </summary>
/// <remarks>
/// <para><b>Why a job at all.</b> Systemusers are created OUTSIDE the product (Entra / PPAC sync), so no BFF
/// request ever sees most of them being born — and an internal user named in an Assigned-To field needs the
/// link even if they never sign in to the BFF (C9 auto-grants, the internal No Access List, briefing
/// "assigned to" matching all key on it). The inline link at first resolution and the link at
/// <c>RegistrationDataverseService.CreateSystemUserAsync</c> are the fast paths; this is the safety net
/// (DATAVERSE-WRITE-PATH-ARCHITECTURE WP-5).</para>
///
/// <para><b>Which environments.</b> First this BFF's own (<c>Dataverse:ServiceUrl</c>, through the DI-registered
/// binder). Then its PROVISIONING TARGETS: the environment registration writes to when no target is given
/// (<c>DATAVERSE_URL</c>) and every ACTIVE <c>sprk_dataverseenvironment</c> row — the only environments the approve
/// endpoint provisions into. A registration link that does not land there (a fault, a deny, a lost race, a
/// collision) is therefore re-decided here on the next run, and a flag written there is re-evaluated and cleared
/// here — the gap the second verifier round recorded (notes/task-141-identity-binding.md §12.1) is closed, not
/// documented. Each target is reconciled exactly like the own environment, through the registration service's
/// existing per-environment token path (ADR-028: the BFF's own credential, no new secret). One environment's
/// failure fails the run but never stops the others.</para>
///
/// <para><b>Two passes per environment, one reason to change</b> (identity binding — CLAUDE.md §11.5;
/// deliberately NOT folded into <see cref="ExternalAccessReconciliationJob"/>, whose single reason to change is the
/// lifecycle of grant and membership rows). Pass 1 walks enabled interactive systemusers and runs the SAME decision
/// every other path runs (<see cref="ContactIdentityBinder.EnsureSystemUserLinkAsync(SystemUserIdentityRow, bool, CancellationToken)"/>):
/// verify, link, bind, create, or flag — never re-pointing or clearing an existing link. Pass 2 walks contacts
/// with an open flag and re-evaluates EVERY recorded party: a flag is cleared only when no party still collides
/// and no systemuser collided with the contact this run, and pruned to the parties that still collide otherwise
/// (<see cref="ContactBindingDecision.ReconcileFlag"/>). Pass 2 runs only after a COMPLETE pass 1 — not after a
/// failed or truncated scan, and not when the binding column could not be probed: a flag is only cleared on
/// evidence, never on a partial view.</para>
///
/// <para><b>The safety convention, copied from <see cref="ExternalAccessReconciliationJob"/>.</b> Linking a
/// systemuser to an existing contact hands that user the contact's grants — the same class of write as that
/// job's R1. So: registered through <c>AddScheduledJob</c>; writes gated on <see cref="WritesEnabledConfigKey"/>,
/// named positively, so absent / empty / unparseable all mean REPORT-ONLY — in EVERY environment; a before-state
/// line logged for every row it would change, in both modes, before anything is written; a failed scan recorded
/// as a FAILED run, never as "nothing to reconcile"; and no throw from <see cref="ExecuteAsync"/> (ADR-036 A1 rule
/// 4 — reconciliation is idempotent by construction, so the next tick picks up whatever this one could not
/// finish).</para>
///
/// <para><b>Idempotent per row, so no chunk claims.</b> Every write is conditional: a bind and a link carry the
/// row version read with the row (<c>If-Match</c>), a create carries the uniqueness mirror whose alternate key's
/// unique index refuses a second contact for the oid (owner round 4 item 4, B2), a collision party is recorded once per
/// contact, and a flag is pruned or cleared only on the row version its verdict was made on. A second run — or a
/// second instance, or a BFF deployed against a target environment reconciling it too — re-decides from current
/// data and finds nothing to do, which is the at-most-once property ADR-036 A1 rule 3 asks for, without a claim
/// store.</para>
///
/// <para><b>Cheap when nothing changed.</b> The scan expands each user's linked contact, so a verified user costs
/// no query beyond its page. Every 5 minutes because the owner's rule for access changes is "minutes, never
/// hourly" (round 3 R3/R4) and Assigned-To grants depend on this link.</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process <c>Spaarke.Scheduling</c> host —
/// BFF domain code (the binder), BFF-owned tables, BFF identity, low volume (B2/B3). The only Functions-leaning
/// signal (one dispatch per schedule, F3) is met in place by the host's lease. No new package, client or store.</para>
/// </remarks>
public sealed class IdentityLinkReconciliationJob : IScheduledJob
{
    /// <summary>Stable job id.</summary>
    public const string JobIdConstant = "identity-link-reconciliation";

    /// <summary>Every 5 minutes.</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    /// <summary>
    /// The owner switch. Absent, empty or unparseable = REPORT-ONLY. The same switch gates the inline link
    /// (<see cref="ContactIdentityBinder.LinkWritesEnabledConfigKey"/>).
    /// </summary>
    internal const string WritesEnabledConfigKey = ContactIdentityBinder.LinkWritesEnabledConfigKey;

    /// <summary>The paging ceiling per scan (× 500 rows). Past it the run reports TRUNCATED.</summary>
    internal const int MaxPages = 40;

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    internal const string ModeReportOnly = "report-only";
    internal const string ModeWrite = "write";

    /// <summary>Bounded sample of ids per outcome in ResultJson; the complete list is in the per-row log lines.</summary>
    internal const int MaxSampledIds = 50;

    /// <summary>The configuration key naming the environment registration provisions into by default.</summary>
    internal const string RegistrationEnvironmentConfigKey = "DATAVERSE_URL";

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly HashSet<SystemUserLinkOutcome> Definitive = new()
    {
        SystemUserLinkOutcome.Verified, SystemUserLinkOutcome.Linked, SystemUserLinkOutcome.BoundAndLinked,
        SystemUserLinkOutcome.CreatedAndLinked, SystemUserLinkOutcome.BoundLinkedContact,
        SystemUserLinkOutcome.Flagged, SystemUserLinkOutcome.FlagAlreadyPresent,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentityLinkReconciliationJob> _logger;

    /// <remarks>
    /// The binder (and, for the provisioning targets, the registration service and environment registry) is
    /// resolved per RUN from a scope (the <see cref="ExternalAccessReconciliationJob"/> convention), not injected:
    /// the scheduler instantiates every job when it reads its registrations, and a job must not drag the Dataverse
    /// store, its credential and its configuration into that moment.
    /// </remarks>
    public IdentityLinkReconciliationJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<IdentityLinkReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Identity Link Reconciliation";

    /// <inheritdoc />
    public string Description =>
        "Links every enabled interactive systemuser to its contact by Entra oid (bind, create, or link), flags "
        + "identity collisions for an operator, and clears flags whose collision has been resolved — in this BFF's "
        + "own environment and in every environment it provisions users into (DATAVERSE_URL and the active "
        + "sprk_dataverseenvironment rows). Never re-points or clears an existing link. Report-only until "
        + "IdentityLink:Reconciliation:WritesEnabled is true.";

    /// <summary>Report-only unless the switch parses to <c>true</c>.</summary>
    internal bool WritesEnabled => ContactIdentityBinder.LinkWritesEnabled(_configuration);

    /// <summary>
    /// The provisioning-target environments to reconcile besides this BFF's own: <paramref name="registrationEnvironment"/>
    /// (<c>DATAVERSE_URL</c>) and every <paramref name="registryEnvironments"/> URL, trailing slash trimmed, blanks
    /// dropped, de-duplicated case-insensitively, and never <paramref name="ownEnvironment"/> (already reconciled
    /// through the DI binder). Pure.
    /// </summary>
    public static IReadOnlyList<string> ProvisioningTargetUrls(
        string? ownEnvironment, string? registrationEnvironment, IEnumerable<string?> registryEnvironments)
    {
        ArgumentNullException.ThrowIfNull(registryEnvironments);
        static string? Normalise(string? url) => string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');

        var own = Normalise(ownEnvironment);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<string>();
        foreach (var candidate in new[] { registrationEnvironment }.Concat(registryEnvironments).Select(Normalise))
        {
            if (candidate is null || string.Equals(candidate, own, StringComparison.OrdinalIgnoreCase) || !seen.Add(candidate))
            {
                continue;
            }

            targets.Add(candidate);
        }

        return targets;
    }

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var writes = WritesEnabled;
        var mode = writes ? ModeWrite : ModeReportOnly;
        var own = new RunReport(Normalised(_configuration["Dataverse:ServiceUrl"]) ?? "(own environment)");
        var targets = new ProvisioningTargets();
        var status = StatusOk;
        var problems = new List<string>();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var binder = scope.ServiceProvider.GetRequiredService<ContactIdentityBinder>();
            await ReconcileEnvironmentAsync(binder, own, writes, mode, context, cancellationToken).ConfigureAwait(false);
            await ReconcileProvisioningTargetsAsync(scope.ServiceProvider, targets, writes, mode, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled; rows after the cancellation point were not reconciled.");
        }
        catch (Exception ex) // a scheduled job's last line of defence: report the failure, never let it escape
        {
            status = StatusError;
            problems.Add(ex.Message);
            _logger.LogError(ex, "[ID-LINK-RECON] Run failed. attempt={Attempt} correlationId={CorrelationId}",
                context.Attempt, context.CorrelationId);
        }

        if (own.Assess(problems, prefix: null))
        {
            status = StatusError;
        }

        if (targets.RegistryFailed)
        {
            status = StatusError;
            problems.Add($"The environment registry (sprk_dataverseenvironment) could not be read ({targets.RegistryError}); "
                + "provisioning targets other than DATAVERSE_URL were NOT reconciled.");
        }

        foreach (var target in targets.Environments)
        {
            if (target.Assess(problems, prefix: $"[{target.Environment}] "))
            {
                status = StatusError;
            }
        }

        if (status == StatusOk && problems.Count > 0)
        {
            status = StatusPartial;
        }

        var duration = _timeProvider.GetElapsedTime(started);
        foreach (var report in new[] { own }.Concat(targets.Environments))
        {
            LogHeartbeat(report, status, mode, duration, context);
        }

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: own.Changed + targets.Environments.Sum(t => t.Changed),
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(ToResult(own, targets, status, mode, writes), ResultJsonOptions));
    }

    /// <summary>Probe, pass 1, pass 2 — for one environment.</summary>
    private async Task ReconcileEnvironmentAsync(
        ContactIdentityBinder binder, RunReport report, bool writes, string mode, JobRunContext context, CancellationToken ct)
    {
        var readability = await binder.Store.ProbeBindingReadabilityAsync(ct).ConfigureAwait(false);
        report.Readability = readability.ToString();
        if (readability is BindingReadability.Masked or BindingReadability.Failed)
        {
            // Under masking every bound contact reads as unbound; deciding anything would be deciding blind.
            report.ProbeBlocked = true;
            _logger.LogError(
                "[ID-LINK-RECON] {Environment}: binding column {Readability} — environment aborted before any decision. "
                + "correlationId={CorrelationId}",
                report.Environment, readability, context.CorrelationId);
            return;
        }

        await ReconcileSystemUsersAsync(binder, report, writes, mode, context, ct).ConfigureAwait(false);

        if (report.UserScanFailed || report.UserScanTruncated)
        {
            report.PassTwoSkipped = true;
            return;
        }

        await ClearResolvedFlagsAsync(binder.Store, report, writes, mode, context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The environments this BFF provisions users into (third fix round, closing the registration-link gap): the
    /// registration default (<c>DATAVERSE_URL</c>) and every ACTIVE registry row, each reconciled like the own
    /// environment. Not configured when <c>DATAVERSE_URL</c> is absent — then this BFF registers nobody anywhere.
    /// </summary>
    private async Task ReconcileProvisioningTargetsAsync(
        IServiceProvider services, ProvisioningTargets targets, bool writes, string mode, JobRunContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_configuration[RegistrationEnvironmentConfigKey])
            || services.GetService<RegistrationDataverseService>() is not { } registration)
        {
            return; // registration is not configured on this host: it provisions nobody, so there is no target
        }

        targets.Configured = true;

        IReadOnlyList<string?> registryUrls = Array.Empty<string?>();
        try
        {
            var registry = services.GetService<DataverseEnvironmentService>()
                ?? throw new InvalidOperationException("DataverseEnvironmentService is not registered.");
            registryUrls = (await registry.GetActiveEnvironmentsAsync(ct).ConfigureAwait(false))
                .Select(e => e.DataverseUrl)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The registration default is still reconciled below; only the registry's rows are unknown this run.
            targets.RegistryFailed = true;
            targets.RegistryError = ex.Message;
            _logger.LogError(ex,
                "[ID-LINK-RECON] The environment registry could not be read; provisioning targets beyond DATAVERSE_URL "
                + "are not reconciled this run. correlationId={CorrelationId}", context.CorrelationId);
        }

        foreach (var url in ProvisioningTargetUrls(_configuration["Dataverse:ServiceUrl"], registration.DataverseBaseUrl, registryUrls))
        {
            ct.ThrowIfCancellationRequested();
            var report = new RunReport(url);
            targets.Environments.Add(report);
            try
            {
                await ReconcileEnvironmentAsync(registration.ContactBinderFor(url), report, writes, mode, context, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) // one target's fault fails the run, never the other environments
            {
                report.Fault = ex.Message;
                _logger.LogError(ex,
                    "[ID-LINK-RECON] {Environment}: reconciliation failed; the next run retries it. correlationId={CorrelationId}",
                    url, context.CorrelationId);
            }
        }
    }

    private async Task ReconcileSystemUsersAsync(
        ContactIdentityBinder binder, RunReport report, bool writes, string mode, JobRunContext context, CancellationToken ct)
    {
        string? continuation = null;
        var pages = 0;
        do
        {
            ct.ThrowIfCancellationRequested();
            var page = await binder.Store.ScanInteractiveSystemUsersAsync(continuation, ct).ConfigureAwait(false);
            if (page.Status != LookupStatus.Read)
            {
                report.UserScanFailed = true;
                report.UserScanError = page.Error ?? page.Status.ToString();
                _logger.LogError(
                    "[ID-LINK-RECON] {Environment}: systemuser scan failed on page {Page} ({Error}); the run is recorded FAILED. "
                    + "correlationId={CorrelationId}",
                    report.Environment, pages + 1, report.UserScanError, context.CorrelationId);
                return;
            }

            foreach (var user in page.Rows)
            {
                ct.ThrowIfCancellationRequested();
                report.UsersScanned++;
                if (user.Oid is { } scannedOid)
                {
                    report.ScannedOids.Add(scannedOid);
                }

                var result = await binder.EnsureSystemUserLinkAsync(user, writes, ct).ConfigureAwait(false);
                report.Record(result);

                if (user.Oid is { } oid && Definitive.Contains(result.Outcome))
                {
                    report.DecidedOids.Add(oid);
                    foreach (var flagged in result.FlaggedContactIds)
                    {
                        report.CollisionsThisRun.Add((flagged, oid));
                    }
                }

                _logger.LogInformation(
                    "[ID-LINK-RECON] decision mode={Mode} environment={Environment} systemuser={SystemUserId} email={Email} "
                    + "outcome={Outcome} contact={ContactId} code={Code} reason={Reason} correlationId={CorrelationId}",
                    mode, report.Environment, user.SystemUserId, user.InternalEmail, result.Outcome, result.ContactId,
                    result.DenyCode, result.Reason, context.CorrelationId);

                foreach (var change in result.Changes)
                {
                    _logger.LogInformation(
                        "[ID-LINK-RECON] before-state mode={Mode} environment={Environment} systemuser={SystemUserId} "
                        + "entity={Entity} rowId={RowId} before=[{Before}] after=[{After}] correlationId={CorrelationId}",
                        mode, report.Environment, user.SystemUserId, change.Entity, change.RowId, change.Before,
                        change.After, context.CorrelationId);
                }
            }

            continuation = page.Continuation;
            pages++;
        }
        while (continuation is not null && pages < MaxPages);

        report.UserScanTruncated = continuation is not null;
    }

    private async Task ClearResolvedFlagsAsync(
        IContactIdentityStore store, RunReport report, bool writes, string mode, JobRunContext context, CancellationToken ct)
    {
        string? continuation = null;
        var pages = 0;
        do
        {
            ct.ThrowIfCancellationRequested();
            var page = await store.ScanFlaggedContactsAsync(continuation, ct).ConfigureAwait(false);
            if (page.Status != LookupStatus.Read)
            {
                report.FlagScanFailed = true;
                report.FlagScanError = page.Error ?? page.Status.ToString();
                return;
            }

            foreach (var contact in page.Rows)
            {
                if (contact.Flag is not { } flag)
                {
                    continue;
                }

                report.FlagsScanned++;
                var stillHolding = await PartiesStillHoldingAsync(store, report, contact, flag, ct).ConfigureAwait(false);

                // Any systemuser decided THIS run that collided with this contact keeps the flag, whoever the
                // recorded parties are (verifier finding 3: one party resolving must not clear another's live
                // collision on the same contact).
                var collidesThisRun = report.CollisionsThisRun.Any(c => c.ContactId == contact.ContactId);
                var verdict = ContactBindingDecision.ReconcileFlag(flag, stillHolding, collidesThisRun);
                if (verdict.Action == FlagReconciliationAction.Keep)
                {
                    report.Kept++;
                    continue;
                }

                var after = verdict.Remaining is { } remaining
                    ? $"flag parties={remaining.Parties.Count} first=[reason={remaining.Reason} oid={remaining.CollidingOid}]"
                    : "flag=(none)";
                _logger.LogInformation(
                    "[ID-LINK-RECON] before-state mode={Mode} environment={Environment} entity=contact rowId={RowId} "
                    + "before=[flag parties={Parties} first=[reason={Reason} oid={Oid} plane={Plane} on={On:o}]] after=[{After}] "
                    + "correlationId={CorrelationId}",
                    mode, report.Environment, contact.ContactId, flag.Parties.Count, flag.Reason, flag.CollidingOid,
                    flag.CollidingPlane, flag.FlaggedOn, after, context.CorrelationId);

                var clearing = verdict.Action == FlagReconciliationAction.Clear;
                if (clearing)
                {
                    report.ClearedIds.Add(contact.ContactId);
                }

                if (!writes)
                {
                    if (clearing) report.Cleared++; else report.Pruned++;
                    continue;
                }

                // Conditional on the version this verdict was made on: a party appended since the scan read the row
                // makes the write fail (412) and the flag stays for the next run to re-evaluate. A row read without a
                // version is neither pruned nor cleared — an unconditional write could drop a party appended meanwhile.
                if (DataverseContactIdentityStore.RowVersionPrecondition(contact.ETag) is null)
                {
                    report.ClearFailed++;
                    continue;
                }

                var write = clearing
                    ? await store.ClearCollisionFlagAsync(contact.ContactId, contact.ETag, ct).ConfigureAwait(false)
                    : await store.WriteCollisionFlagAsync(contact.ContactId, verdict.Remaining!, contact.ETag, ct).ConfigureAwait(false);
                if (write.Status == StoreWriteStatus.Written)
                {
                    if (clearing) report.Cleared++; else report.Pruned++;
                    report.Changed++;
                }
                else
                {
                    report.ClearFailed++;
                }
            }

            continuation = page.Continuation;
            pages++;
        }
        while (continuation is not null && pages < MaxPages);
    }

    /// <summary>
    /// The recorded parties of <paramref name="flag"/> whose collision still holds. A key-mirror conflict is decided
    /// from the holder row itself (it is a fact about the unique index, and a report-only pass 1 — which attempts no
    /// write — cannot see it). Otherwise a party that is a systemuser re-decided in pass 1 holds iff that decision
    /// flagged this contact; one scanned but not decided (a lookup or a write failed) holds — no evidence is not
    /// evidence of resolution; any other party (a token-plane caller, an invite, a systemuser no longer in scope) is
    /// re-evaluated from current data.
    /// </summary>
    private static async Task<List<CollisionParty>> PartiesStillHoldingAsync(
        IContactIdentityStore store, RunReport report, ContactBindingRow contact, CollisionFlag flag, CancellationToken ct)
    {
        var holding = new List<CollisionParty>();
        ContactLookup? emailCarriers = null;
        ReferenceLookup? refs = null;

        foreach (var party in flag.Parties)
        {
            bool holds;
            if (party.Reason == IdentityCollisionReason.KeyMirrorConflict)
            {
                holds = ContactBindingDecision.CollisionStillHolds(
                    party, contact, ContactLookup.Of(), ContactLookup.Of(), ReferenceLookup.Of());
            }
            else if (party.Oid is { } oid && report.DecidedOids.Contains(oid))
            {
                holds = report.CollisionsThisRun.Contains((contact.ContactId, oid));
            }
            else if (party.Oid is { } undecided && report.ScannedOids.Contains(undecided))
            {
                holds = true;
            }
            else
            {
                emailCarriers ??= string.IsNullOrWhiteSpace(contact.Email)
                    ? ContactLookup.Of()
                    : await store.FindActiveContactsByEmailAsync(contact.Email, ct).ConfigureAwait(false);
                refs ??= await store.FindSystemUsersLinkingAsync(new[] { contact.ContactId }, ct).ConfigureAwait(false);
                var oidCarriers = party.Oid is { } o
                    ? await store.FindContactsByOidAsync(o, ct).ConfigureAwait(false)
                    : ContactLookup.Of();
                holds = ContactBindingDecision.CollisionStillHolds(party, contact, emailCarriers, oidCarriers, refs);
            }

            if (holds)
            {
                holding.Add(party);
            }
        }

        return holding;
    }

    private void LogHeartbeat(RunReport report, string status, string mode, TimeSpan duration, JobRunContext context)
        => _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "[ID-LINK-RECON] heartbeat status={Status} environmentStatus={EnvironmentStatus} mode={Mode} environment={Environment} readability={Readability} "
            + "usersScanned={Scanned} verified={Verified} linked={Linked} boundAndLinked={Bound} createdAndLinked={Created} "
            + "boundLinkedContact={BoundLinked} flagged={Flagged} flagAlreadyPresent={FlagPresent} denied={Denied} "
            + "failed={Failed} flagsScanned={FlagsScanned} flagsCleared={Cleared} flagsPruned={Pruned} flagsKept={Kept} "
            + "truncated={Truncated} durationMs={DurationMs} attempt={Attempt} trigger={Trigger} correlationId={CorrelationId}",
            status, report.Status, mode, report.Environment, report.Readability, report.UsersScanned,
            report.Count(SystemUserLinkOutcome.Verified), report.Count(SystemUserLinkOutcome.Linked),
            report.Count(SystemUserLinkOutcome.BoundAndLinked), report.Count(SystemUserLinkOutcome.CreatedAndLinked),
            report.Count(SystemUserLinkOutcome.BoundLinkedContact), report.Count(SystemUserLinkOutcome.Flagged),
            report.Count(SystemUserLinkOutcome.FlagAlreadyPresent), report.Count(SystemUserLinkOutcome.Denied),
            report.Count(SystemUserLinkOutcome.Failed), report.FlagsScanned, report.Cleared, report.Pruned, report.Kept,
            report.UserScanTruncated, (long)duration.TotalMilliseconds, context.Attempt, context.Trigger,
            context.CorrelationId);

    private static object ToResult(RunReport own, ProvisioningTargets targets, string status, string mode, bool writesEnabled) => new
    {
        status,
        mode,
        writesEnabled,
        environment = own.Environment,
        readability = own.Readability,
        systemUsers = own.SystemUsersResult(),
        flags = own.FlagsResult(),
        provisioningTargets = new
        {
            configured = targets.Configured,
            registryReadFailed = targets.RegistryFailed,
            environments = targets.Environments.Select(t => new
            {
                environment = t.Environment,
                status = t.Status,
                readability = t.Readability,
                fault = t.Fault,
                systemUsers = t.SystemUsersResult(),
                flags = t.FlagsResult(),
            }).ToArray(),
        },
    };

    private static string? Normalised(string? url) => string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');

    /// <summary>The provisioning-target pass's state.</summary>
    private sealed class ProvisioningTargets
    {
        public bool Configured;
        public bool RegistryFailed;
        public string? RegistryError;
        public readonly List<RunReport> Environments = new();
    }

    /// <summary>Per-environment counts.</summary>
    private sealed class RunReport
    {
        private readonly Dictionary<SystemUserLinkOutcome, List<Guid>> _byOutcome = new();

        public RunReport(string environment) => Environment = environment;

        public string Environment { get; }

        /// <summary>This environment's own status (ok / partial / error), set by <see cref="Assess"/>.</summary>
        public string Status = StatusOk;
        public string Readability = "(not probed)";
        public bool ProbeBlocked;
        public bool PassTwoSkipped;
        public string? Fault;
        public int UsersScanned;
        public bool UserScanFailed;
        public bool UserScanTruncated;
        public string? UserScanError;
        public bool FlagScanFailed;
        public string? FlagScanError;
        public int FlagsScanned;
        public int Cleared;
        public int Pruned;
        public int Kept;
        public int ClearFailed;
        public int Changed;
        public readonly HashSet<Guid> ScannedOids = new();
        public readonly HashSet<Guid> DecidedOids = new();
        public readonly HashSet<(Guid ContactId, Guid Oid)> CollisionsThisRun = new();
        public readonly List<Guid> ClearedIds = new();

        public int Count(SystemUserLinkOutcome outcome) => _byOutcome.TryGetValue(outcome, out var ids) ? ids.Count : 0;

        public void Record(SystemUserLinkResult result)
        {
            if (!_byOutcome.TryGetValue(result.Outcome, out var ids))
            {
                _byOutcome[result.Outcome] = ids = new List<Guid>();
            }

            ids.Add(result.SystemUserId);
            if (result.WritesApplied && result.Outcome is not (SystemUserLinkOutcome.Failed or SystemUserLinkOutcome.Denied))
            {
                Changed++;
            }
        }

        /// <summary>Adds this environment's problems; true when one of them makes the run a FAILED run.</summary>
        public bool Assess(List<string> problems, string? prefix)
        {
            var p = prefix ?? string.Empty;
            var error = false;
            var before = problems.Count;

            if (Fault is not null)
            {
                error = true;
                problems.Add($"{p}Reconciliation failed ({Fault}); the next run retries it.");
            }

            if (ProbeBlocked)
            {
                error = true;
                problems.Add(Readability == nameof(BindingReadability.Masked)
                    ? $"{p}The binding column is MASKED for the BFF identity (field-level security without Read); nothing was decided or written."
                    : $"{p}The environment could not be read with the BFF identity (masking probe {Readability}: no access, the "
                      + "identity-binding schema not applied, or unreachable); nothing was decided or written.");
            }

            if (PassTwoSkipped)
            {
                problems.Add($"{p}Pass 2 (flag clearing) skipped: pass 1 did not see every systemuser, so no flag can be cleared on evidence.");
            }

            if (UserScanFailed)
            {
                error = true;
                problems.Add($"{p}The systemuser scan failed ({UserScanError}); the run is NOT 'nothing to reconcile'.");
            }

            if (FlagScanFailed)
            {
                error = true;
                problems.Add($"{p}The flagged-contact scan failed ({FlagScanError}).");
            }

            if (UserScanTruncated)
            {
                problems.Add($"{p}Stopped after {MaxPages} pages of systemusers; later users were not reconciled.");
            }

            var failed = Count(SystemUserLinkOutcome.Failed) + ClearFailed;
            if (failed > 0)
            {
                problems.Add($"{p}{failed} write(s) did not complete; the next run retries them.");
            }

            Status = error ? StatusError : problems.Count > before ? StatusPartial : StatusOk;
            return error;
        }

        public object SystemUsersResult() => new
        {
            scanned = UsersScanned,
            truncated = UserScanTruncated,
            scanFailed = UserScanFailed,
            outcomes = _byOutcome.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => new
                {
                    count = kvp.Value.Count,
                    sampleSystemUserIds = kvp.Value.Take(MaxSampledIds)
                        .Select(id => id.ToString("D", CultureInfo.InvariantCulture)).ToArray(),
                }),
        };

        public object FlagsResult() => new
        {
            scanned = FlagsScanned,
            cleared = Cleared,
            pruned = Pruned,
            kept = Kept,
            clearFailed = ClearFailed,
            scanFailed = FlagScanFailed,
            sampleClearedContactIds = ClearedIds.Take(MaxSampledIds)
                .Select(id => id.ToString("D", CultureInfo.InvariantCulture)).ToArray(),
        };
    }
}
