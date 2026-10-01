using System.Globalization;
using System.Text.Json;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 141 — keeps every licensed systemuser linked to its contact
/// (<c>contact.sprk_externalobjectid</c> = <c>systemuser.azureactivedirectoryobjectid</c> AND
/// <c>systemuser.sprk_primarycontact</c> pointing at that contact) or carrying a durable collision flag.
/// </summary>
/// <remarks>
/// <para><b>Why a job at all.</b> Systemusers are created OUTSIDE the product (Entra / PPAC sync), so no BFF
/// request ever sees most of them being born — and an internal user named in an Assigned-To field needs the
/// link even if they never sign in to the BFF (C9 auto-grants, the internal No Access List, briefing
/// "assigned to" matching all key on it). The inline link at first resolution and the link at
/// <c>RegistrationDataverseService.CreateSystemUserAsync</c> are the fast paths; this is the safety net
/// (DATAVERSE-WRITE-PATH-ARCHITECTURE WP-5).</para>
///
/// <para><b>Two passes, one reason to change</b> (identity binding — CLAUDE.md §11.5; deliberately NOT folded
/// into <see cref="ExternalAccessReconciliationJob"/>, whose single reason to change is the lifecycle of grant
/// and membership rows). Pass 1 walks enabled interactive systemusers and runs the SAME decision every other
/// path runs (<see cref="ContactIdentityBinder.EnsureSystemUserLinkAsync(SystemUserIdentityRow, bool, CancellationToken)"/>):
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
/// named positively, so absent / empty / unparseable all mean REPORT-ONLY; a before-state line logged for every
/// row it would change, in both modes, before anything is written; a failed scan recorded as a FAILED run, never
/// as "nothing to reconcile"; and no throw from <see cref="ExecuteAsync"/> (ADR-036 A1 rule 4 — reconciliation
/// is idempotent by construction, so the next tick picks up whatever this one could not finish).</para>
///
/// <para><b>Idempotent per row, so no chunk claims.</b> Every write is conditional: a bind and a link carry the
/// row version read with the row (<c>If-Match</c>), a create is create-only through the alternate key
/// (<c>If-None-Match: *</c>), a collision party is recorded once per contact, and a flag is pruned or cleared only
/// on the row version its verdict was made on. A second run — or a second instance — re-decides from current data
/// and finds nothing to do, which is the at-most-once property ADR-036 A1 rule 3 asks for, without a claim store.
/// (The create's uniqueness guarantee depends on the alternate key, which is PENDING an owner decision: Dataverse
/// refuses an alternate key on a field-secured column — notes/task-141-identity-binding.md §9.)</para>
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
    /// The binder is resolved per RUN from a scope (the <see cref="ExternalAccessReconciliationJob"/>
    /// convention), not injected: the scheduler instantiates every job when it reads its registrations, and a
    /// job must not drag the Dataverse store, its credential and its configuration into that moment.
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
        + "identity collisions for an operator, and clears flags whose collision has been resolved. Never re-points "
        + "or clears an existing link. Report-only until IdentityLink:Reconciliation:WritesEnabled is true.";

    /// <summary>Report-only unless the switch parses to <c>true</c>.</summary>
    internal bool WritesEnabled => ContactIdentityBinder.LinkWritesEnabled(_configuration);

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var writes = WritesEnabled;
        var mode = writes ? ModeWrite : ModeReportOnly;
        var report = new RunReport();
        var status = StatusOk;
        var problems = new List<string>();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var binder = scope.ServiceProvider.GetRequiredService<ContactIdentityBinder>();

            var readability = await binder.Store.ProbeBindingReadabilityAsync(cancellationToken).ConfigureAwait(false);
            report.Readability = readability.ToString();
            if (readability is BindingReadability.Masked or BindingReadability.Failed)
            {
                // Under masking every bound contact reads as unbound; deciding anything would be deciding blind.
                status = StatusError;
                problems.Add($"The binding column reads as {readability} for the BFF identity; nothing was decided or written.");
                _logger.LogError(
                    "[ID-LINK-RECON] Binding column {Readability} — run aborted before any decision. correlationId={CorrelationId}",
                    readability, context.CorrelationId);
            }
            else
            {
                await ReconcileSystemUsersAsync(binder, report, writes, mode, context, cancellationToken).ConfigureAwait(false);

                if (report.UserScanFailed || report.UserScanTruncated)
                {
                    problems.Add("Pass 2 (flag clearing) skipped: pass 1 did not see every systemuser, so no flag can be cleared on evidence.");
                }
                else
                {
                    await ClearResolvedFlagsAsync(binder.Store, report, writes, mode, context, cancellationToken).ConfigureAwait(false);
                }
            }
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

        if (report.UserScanFailed)
        {
            status = StatusError;
            problems.Add($"The systemuser scan failed ({report.UserScanError}); the run is NOT 'nothing to reconcile'.");
        }

        if (report.FlagScanFailed)
        {
            status = StatusError;
            problems.Add($"The flagged-contact scan failed ({report.FlagScanError}).");
        }

        if (report.UserScanTruncated)
        {
            problems.Add($"Stopped after {MaxPages} pages of systemusers; later users were not reconciled.");
        }

        var failed = report.Count(SystemUserLinkOutcome.Failed) + report.ClearFailed;
        if (failed > 0)
        {
            problems.Add($"{failed} write(s) did not complete; the next run retries them.");
        }

        if (status == StatusOk && problems.Count > 0)
        {
            status = StatusPartial;
        }

        var duration = _timeProvider.GetElapsedTime(started);
        _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "[ID-LINK-RECON] heartbeat status={Status} mode={Mode} readability={Readability} usersScanned={Scanned} "
            + "verified={Verified} linked={Linked} boundAndLinked={Bound} createdAndLinked={Created} "
            + "boundLinkedContact={BoundLinked} flagged={Flagged} flagAlreadyPresent={FlagPresent} denied={Denied} "
            + "failed={Failed} flagsScanned={FlagsScanned} flagsCleared={Cleared} flagsPruned={Pruned} flagsKept={Kept} "
            + "truncated={Truncated} durationMs={DurationMs} attempt={Attempt} trigger={Trigger} correlationId={CorrelationId}",
            status, mode, report.Readability, report.UsersScanned,
            report.Count(SystemUserLinkOutcome.Verified), report.Count(SystemUserLinkOutcome.Linked),
            report.Count(SystemUserLinkOutcome.BoundAndLinked), report.Count(SystemUserLinkOutcome.CreatedAndLinked),
            report.Count(SystemUserLinkOutcome.BoundLinkedContact), report.Count(SystemUserLinkOutcome.Flagged),
            report.Count(SystemUserLinkOutcome.FlagAlreadyPresent), report.Count(SystemUserLinkOutcome.Denied),
            report.Count(SystemUserLinkOutcome.Failed), report.FlagsScanned, report.Cleared, report.Pruned, report.Kept,
            report.UserScanTruncated, (long)duration.TotalMilliseconds, context.Attempt, context.Trigger,
            context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: report.Changed,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(report.ToResult(status, mode, writes), ResultJsonOptions));
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
                    "[ID-LINK-RECON] Systemuser scan failed on page {Page} ({Error}); the run is recorded FAILED. correlationId={CorrelationId}",
                    pages + 1, report.UserScanError, context.CorrelationId);
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
                    "[ID-LINK-RECON] decision mode={Mode} systemuser={SystemUserId} email={Email} outcome={Outcome} "
                    + "contact={ContactId} code={Code} reason={Reason} correlationId={CorrelationId}",
                    mode, user.SystemUserId, user.InternalEmail, result.Outcome, result.ContactId, result.DenyCode,
                    result.Reason, context.CorrelationId);

                foreach (var change in result.Changes)
                {
                    _logger.LogInformation(
                        "[ID-LINK-RECON] before-state mode={Mode} systemuser={SystemUserId} entity={Entity} rowId={RowId} "
                        + "before=[{Before}] after=[{After}] correlationId={CorrelationId}",
                        mode, user.SystemUserId, change.Entity, change.RowId, change.Before, change.After,
                        context.CorrelationId);
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
                    "[ID-LINK-RECON] before-state mode={Mode} entity=contact rowId={RowId} "
                    + "before=[flag parties={Parties} first=[reason={Reason} oid={Oid} plane={Plane} on={On:o}]] after=[{After}] "
                    + "correlationId={CorrelationId}",
                    mode, contact.ContactId, flag.Parties.Count, flag.Reason, flag.CollidingOid, flag.CollidingPlane,
                    flag.FlaggedOn, after, context.CorrelationId);

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
    /// The recorded parties of <paramref name="flag"/> whose collision still holds. A party that is a systemuser
    /// re-decided in pass 1 holds iff that decision flagged this contact; one scanned but not decided (a lookup or
    /// a write failed) holds — no evidence is not evidence of resolution; any other party (a token-plane caller,
    /// an invite, a systemuser no longer in scope) is re-evaluated from current data.
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
            if (party.Oid is { } oid && report.DecidedOids.Contains(oid))
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

    /// <summary>Per-run counts.</summary>
    private sealed class RunReport
    {
        private readonly Dictionary<SystemUserLinkOutcome, List<Guid>> _byOutcome = new();

        public string Readability = "(not probed)";
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

        public object ToResult(string status, string mode, bool writesEnabled) => new
        {
            status,
            mode,
            writesEnabled,
            readability = Readability,
            systemUsers = new
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
            },
            flags = new
            {
                scanned = FlagsScanned,
                cleared = Cleared,
                pruned = Pruned,
                kept = Kept,
                clearFailed = ClearFailed,
                scanFailed = FlagScanFailed,
                sampleClearedContactIds = ClearedIds.Take(MaxSampledIds)
                    .Select(id => id.ToString("D", CultureInfo.InvariantCulture)).ToArray(),
            },
        };
    }
}
