using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// unified-access-control-r2 task 190 (D7 T7b, #2126; a backfill): links each legacy <c>.eml</c>
/// archive document (<c>sprk_isemailarchive = true</c>) that has no communication link to THE communication its RECORDED
/// key names — the one <c>sprk_communication</c> whose <c>sprk_internetmessageid</c> equals the document's
/// <c>sprk_emailmessageid</c>, exactly — so task 188's document narrowing (<c>ExternalDocumentNarrowing</c>, leg 2) sees the
/// communication the document comes from. Everything it cannot link is reported by reason, by id, for the owner.
/// </summary>
/// <remarks>
/// <para><b>Component justification</b> (CLAUDE.md §11). <i>Existing</i> — <see cref="CrossPathLink.FindAndLinkArchiveDocumentsAsync"/>
/// writes the same link from the capture side when a communication is created, and the Office save writes it in the document
/// create (task 130); <see cref="CommunicationThreadFilingSweepJob"/> and <c>DocumentContainerMigrationJob</c> are the same
/// sweep shape for other invariants. <i>Extension</i> — the capture-side link runs once per NEW communication, so it never
/// revisits an archive saved before its communication existed or saved with a mailbox item id; it also writes
/// unconditionally and does not report, and the two sweeps carry other invariants. <i>Cost of doing nothing</i> — the
/// archives saved through the three add-in gap paths before task 130 stay unlinked, so leg 2 of the narrowing never sees
/// their communication and an internal-only email's <c>.eml</c> (and its attachments, through the parent walk) stays visible
/// to partners by root grants (dev census 2026-10-21: 120 unlinked archives, 29 of them matchable, covering 39 children).</para>
///
/// <para><b>Each run</b> reads a bounded slice of candidates, in id order, after a per-instance cursor: <c>sprk_document</c>
/// rows, active or inactive, with <c>sprk_isemailarchive = true</c> and no <c>sprk_relatedcommunication</c>. Each candidate
/// gets exactly one reason (<see cref="LinkReason"/>):</para>
/// <list type="bullet">
/// <item><c>hasParent</c> — the archive itself has a <c>sprk_parentdocument</c>: never linked, whatever its key. The
/// narrowing takes the FIRST communication link on its parent walk, so an own link on such an archive could pre-empt a
/// parent's internal-only link and SHOW it to partners. No writer creates such archives today; leaving them unlinked keeps
/// "this sweep can only hide, never show" true in every case;</item>
/// <item><c>noKey</c> — no <c>sprk_emailmessageid</c>;</item>
/// <item><c>exchangeItemId</c> — a mailbox item id saved before word-add-in-r1 task 121, not an RFC 5322 Message-ID
/// (<see cref="OfficeEmailEnricher.IsRfcMessageId"/>, reused): it names the message in one mailbox only and is joined to
/// nothing, so no communication query is made for it;</item>
/// <item>otherwise the communications whose <c>sprk_internetmessageid</c> equals the key are read (at most
/// <see cref="MatchProbeTop"/>). Dataverse string equality ignores case, so the answer is checked again with ordinal (exact)
/// equality: <c>matched</c> only when exactly ONE row comes back AND it is an exact match; <c>ambiguous</c> when more than one
/// comes back (rows that differ only by case included); <c>noCommunication</c> when none matches exactly. The column is
/// the communication's unique alternate key, so <c>ambiguous</c> is a defence, not an expected case. Ambiguous and
/// noCommunication are never linked — no heuristic matching, a wrong link would decide visibility by the wrong email.</item>
/// </list>
///
/// <para><b>The write</b> (Apply only): <c>sprk_relatedcommunication</c> on the document, set to the matched communication, and
/// nothing else — never <c>sprk_accesspermission</c> (whether unmatched rows become Restricted is the owner's call), never a
/// clear or a re-point. It is CONDITIONAL on the row version read with the candidate
/// (<see cref="IGenericEntityService.UpdateIfRowVersionMatchesAsync"/>), so a document changed since it was read — linked by
/// the capture side meanwhile, for one — is not overwritten: it is counted (<c>changedSinceRead</c>) and the next pass
/// looks at it afresh. The job runs as the BFF app identity, the only Writers principal on that column's field-security
/// profile. It does not ask <c>InheritedAccessPermission.RefuseAppOnlyWrite</c>: that refusal guards the generic writers
/// whose payload a model or a configuration built, and this is one of the BFF's own typed link writers it names as the
/// legitimate path (a fixed key, a value read from the exact match).</para>
///
/// <para><b>Children.</b> Attachment children are not linked one by one: the narrowing walks <c>sprk_parentdocument</c>
/// (up to three hops) and the first link found decides, so linking the parent <c>.eml</c> covers them. The report counts the
/// DIRECT children (<c>sprk_parentdocument</c> → the archive, any state) of the linked archives (<c>childrenCovered</c>) and of
/// the unmatched ones (<c>childrenOfUnmatched</c>), so the owner sees the whole unmatched footprint.</para>
///
/// <para><b>Dry run by default.</b> The job writes only when <see cref="ApplyKey"/> is <c>true</c> (an App Service setting,
/// <c>EmailArchiveLinkSweep__Apply</c>; absent or unparseable = dry run). A dry run reads, classifies and counts
/// (<c>wouldLink</c>) and calls nothing that writes. The report is the run's <see cref="JobRunResult.ResultJson"/>, ids and
/// counts only (never a message id): per-reason counts, and per-reason document-id lists capped at
/// <see cref="MaxListedPerRun"/> (every row is counted). Linked rows are listed with the communication they now point at.
/// Run history lives in memory (<c>InMemoryBackgroundJobStore</c>) and is lost on restart, so save the Apply run's
/// <c>ResultJson</c> from <c>/status</c> or <c>/history</c> straight away; the per-link Information log line ("Linked document
/// {id} to communication {id}") is the durable record (the same correction for the reference job is #2226).</para>
///
/// <para><b>Communications that already have an archive</b> (<c>communicationAlreadyArchived</c>: a count and the capped
/// document-id list). An ATTRIBUTE of matched rows, not a reason: such a row still links. D7 needs every archive linked (an
/// unlinked archive of an internal-only email stays visible to partners), so a second archive on one communication is
/// linked too. Which archive a consumer picks when a communication has several is #2225; this job does not decide it. Read
/// once per page (an <c>In</c> over the page's communications) before any write, so a row this run links never counts
/// itself. Dev, 2026-10-21: 0 of the 29 matchable; 6 communications already have more than one archive.</para>
///
/// <para><b>Use.</b> Registered DISABLED (it never fires on its own); the owner runs it on demand with
/// <c>POST /api/admin/jobs/email-archive-communication-link-sweep/trigger</c> (SystemAdmin), reads the report, then sets
/// <c>EmailArchiveLinkSweep__Apply=true</c> and triggers again. No endpoint was added. <c>POST /api/admin/jobs/{jobId}/enable</c>
/// lasts only until the next restart (the enabled flag lives in memory, and every restart re-seeds the job as disabled), so
/// it is not a standing safety net. The key columns (<c>sprk_emailmessageid</c>, <c>sprk_internetmessageid</c>) are
/// user-writable (#2227): keep the job registered disabled, and never <c>/enable</c> it with Apply on until #2227 is
/// fixed.</para>
///
/// <para><b>Apply gate.</b> After Apply, a linked communication cannot be deleted while its archive exists: the
/// relationship is Delete = Restrict (#2186). Name it when asking for Apply.</para>
///
/// <para><b>Batches.</b> A run examines at most <see cref="MaxDocumentsPerRunKey"/> candidates (default
/// <see cref="DefaultMaxDocumentsPerRun"/>, which is <see cref="MaxListedPerRun"/>). A configured value above
/// <see cref="MaxListedPerRun"/> is clamped to it and the report says so (<c>batch.clamped</c>), so every run's id lists are
/// complete; the next run continues after the last row (<c>startAfter</c> / <c>endAt</c>), and a
/// run that reaches the end reports <c>passComplete</c> and starts the next pass at the top. The cursor is per instance (as
/// <see cref="CommunicationThreadFilingSweepJob"/>'s): a restart or another instance starts a pass over, which repeats only
/// reads — a linked row is no longer a candidate.</para>
///
/// <para><b>ADR-036 A1.</b> Rule 1: the scheduler's lease — one run at a time. Rule 2: the job holds no state but its cursor;
/// it reads what it needs each run. Rule 3: every step is keyed on observed state (a linked document is no longer a
/// candidate; the write is conditional on the version read), so a re-run links nothing twice and reports the same unmatched
/// set. Rule 4: a candidate read that faults is a FAILED run (the slice was not examined), never "nothing to do"; a per-row
/// fault (its communication query or its write) is counted and the run continues; the next pass revisits both, so the job
/// does not throw. Rule 5: one heartbeat per attempt. Rule 6: <c>AddScheduledJob</c> in <c>CommunicationModule</c>.</para>
///
/// <para><b>Changed and failed rows</b> (<c>changedSinceRead</c>, <c>failed</c>) are counted only, not listed; the next pass
/// revisits them, since a row that was not linked is still a candidate.</para>
///
/// <para><b>Container derivation and the task 166 migration.</b> Linking a document changes the container
/// <c>RecordContainerResolver.DeriveDocumentContainersAsync</c> derives for it (its communication's answer is consulted, and a
/// secure answer dominates). Under the interim pointer rule a new link only ADDS admissions to its two halves (the archive
/// path and the secure-record test), but the round-37 disjunct that serves a file the BFF identity placed follows the derived
/// container, so a file the BFF relocated BEFORE the link was set can be refused after it. Run this job's Apply BEFORE task 166's gate-24 migration pass and its <c>-Verify</c>. If Apply runs
/// after gate 24, re-run the migration pass and <c>-Verify</c> before the strict flip (gate 25): the migration's
/// <c>-Verify</c> (<c>wouldNewlyRefuse</c>) is the existing check that catches a file left outside its derived container.</para>
///
/// <para><b>Status.</b> <c>ok</c> only when nothing failed: a per-row fault, a row changed since it was read, or a child
/// count that could not be read makes the run <c>partial</c>; a candidate read that faults makes it <c>error</c>.</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process scheduler — a BFF-owned link on BFF-owned
/// tables, written by the BFF identity (the column's only writer), low volume, operator triggered. No new package, store,
/// host or endpoint.</para>
/// </remarks>
public sealed class EmailArchiveCommunicationLinkSweepJob : IScheduledJob
{
    /// <summary>Stable job id — the admin trigger, status and history routes key off it.</summary>
    public const string JobIdConstant = "email-archive-communication-link-sweep";

    /// <summary>Daily at 03:45 UTC when an admin enables the job; registered disabled, so it never fires until then.</summary>
    internal const string DefaultCronSchedule = "45 3 * * *";

    /// <summary>The write switch (<c>EmailArchiveLinkSweep__Apply</c>): <c>true</c> = write; absent or anything else = dry run.</summary>
    public const string ApplyKey = "EmailArchiveLinkSweep:Apply";

    /// <summary>Candidates examined per run (<c>EmailArchiveLinkSweep__MaxDocumentsPerRun</c>).</summary>
    public const string MaxDocumentsPerRunKey = "EmailArchiveLinkSweep:MaxDocumentsPerRun";

    internal const int DefaultMaxDocumentsPerRun = MaxListedPerRun;

    /// <summary>Rows per candidate query. Settable only so a test can reach the paging.</summary>
    internal int ScanPageSize { get; init; } = 100;

    /// <summary>The communications read per key: enough to tell one from more than one.</summary>
    internal const int MatchProbeTop = 3;

    /// <summary>Each report list holds at most this many ids (the admin history surface stays small); all are counted.</summary>
    internal const int MaxListedPerRun = 200;

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    internal const string LogPrefix = "[EMAIL-ARCHIVE-LINK-SWEEP]";

    private const string DocumentTable = "sprk_document";
    private const string DocumentPk = "sprk_documentid";
    private const string ParentDocumentField = "sprk_parentdocument";
    private const string VersionNumberField = "versionnumber";
    private const string CommunicationTable = "sprk_communication";
    private const string CommunicationPk = "sprk_communicationid";
    private const string InternetMessageIdField = "sprk_internetmessageid";

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailArchiveCommunicationLinkSweepJob> _logger;

    /// <summary>The last candidate of the previous run of this pass; null = the next run starts the pass.</summary>
    private Guid? _cursor;

    public EmailArchiveCommunicationLinkSweepJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<EmailArchiveCommunicationLinkSweepJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Email Archive Communication Link Sweep";

    /// <inheritdoc />
    public string Description =>
        "Links each .eml archive document that has no communication link to the one communication whose internet message "
        + "id equals the document's recorded message id, exactly; ambiguous and unmatched archives are never linked and are "
        + "reported by reason and id. Never clears or re-points a link and writes no other column. Dry run unless "
        + ApplyKey + " is true. Disabled until enabled; run on demand with POST /api/admin/jobs/" + JobIdConstant
        + "/trigger.";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var apply = bool.TryParse(_configuration[ApplyKey], out var a) && a;
        // K2: the batch never exceeds what one report lists, so every run's id lists are complete.
        var configuredBatch = PositiveInt(_configuration[MaxDocumentsPerRunKey], DefaultMaxDocumentsPerRun);
        var maxDocuments = Math.Min(configuredBatch, MaxListedPerRun);
        var report = new LinkReport();
        var problems = new List<string>();
        var status = StatusOk;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();

            await SweepAsync(entityService, apply, maxDocuments, report, problems, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled before the run finished.");
        }
        // A scheduled job's last line of defence: record the failure, never let it escape unrecorded.
        catch (Exception ex)
        {
            status = StatusError;
            problems.Add(ex.Message);
            _logger.LogError(ex, "{Prefix} Run failed. correlationId={CorrelationId}", LogPrefix, context.CorrelationId);
        }

        if (status == StatusOk)
        {
            // ADR-003 for a writer: a slice that could not be read is a FAILED run, never "nothing to do".
            if (report.EnumerationFailed)
                status = StatusError;
            else if (report.Failed > 0 || report.ChangedSinceRead > 0 || report.ChildCountFailed
                     || report.AlreadyArchivedCheckFailed)
                status = StatusPartial;
        }

        var duration = _timeProvider.GetElapsedTime(started);
        var mode = apply ? "apply" : "dry-run";
        var level = status != StatusOk ? LogLevel.Warning
            : !apply && report.Matched > 0 ? LogLevel.Warning
            : LogLevel.Information;

        // THE HEARTBEAT (ADR-036 A1 rule 5): one line per attempt, with nothing to do as well. Counts only.
        _logger.Log(
            level,
            "{Prefix} heartbeat status={Status} mode={Mode} examined={Examined} matched={Matched} linked={Linked} "
            + "hasParent={HasParent} noKey={NoKey} exchangeItemId={ExchangeItemId} noCommunication={NoCommunication} ambiguous={Ambiguous} "
            + "changedSinceRead={ChangedSinceRead} failed={Failed} childrenCovered={ChildrenCovered} "
            + "childrenOfUnmatched={ChildrenOfUnmatched} communicationAlreadyArchived={CommunicationAlreadyArchived} passComplete={PassComplete} attempt={Attempt} durationMs={DurationMs} "
            + "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            LogPrefix, status, mode, report.Examined, report.Matched, report.Linked, report.HasParent.Count, report.NoKey.Count, report.ExchangeItemId.Count,
            report.NoCommunication.Count, report.Ambiguous.Count, report.ChangedSinceRead, report.Failed, report.ChildrenCovered,
            report.ChildrenOfUnmatched, report.CommunicationAlreadyArchived.Count, report.PassComplete, context.Attempt, (long)duration.TotalMilliseconds, context.Trigger,
            context.RunId, context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: report.Examined,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    mode,
                    apply,
                    attempt = context.Attempt,
                    batch = new { configured = configuredBatch, effective = maxDocuments, clamped = configuredBatch > maxDocuments },
                    archives = report.ToJson(apply),
                },
                ResultJsonOptions));
    }

    private async Task SweepAsync(
        IGenericEntityService entityService, bool apply, int maxDocuments, LinkReport report, List<string> problems,
        JobRunContext context, CancellationToken ct)
    {
        report.StartAfter = _cursor;
        var last = _cursor;
        var passComplete = false;

        while (report.Examined < maxDocuments)
        {
            ct.ThrowIfCancellationRequested();

            var take = Math.Min(ScanPageSize, maxDocuments - report.Examined);
            IReadOnlyList<Entity> documents;
            try
            {
                documents = (await entityService.RetrieveMultipleAsync(BuildCandidateQuery(last, take), ct).ConfigureAwait(false)).Entities;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                report.EnumerationFailed = true;
                problems.Add($"The unlinked email archives could not be read, so none after {last?.ToString("D") ?? "the start"} were examined ({ex.Message}).");
                _logger.LogError(ex, "{Prefix} The unlinked email archives could not be read; the run is FAILED. correlationId={CorrelationId}",
                    LogPrefix, context.CorrelationId);
                break;
            }

            var covered = new List<Guid>();
            var unmatched = new List<Guid>();
            var matched = new List<(Entity Document, Guid CommunicationId)>();
            foreach (var document in documents)
            {
                ct.ThrowIfCancellationRequested();
                last = document.Id;
                report.Examined++;

                var (reason, communicationId) = await ClassifyAsync(entityService, document, context, ct).ConfigureAwait(false);
                if (reason == LinkReason.Failed)
                {
                    report.Failed++;
                    continue;
                }

                if (reason != LinkReason.Matched)
                {
                    report.Unmatched(reason, document.Id);
                    unmatched.Add(document.Id);
                    continue;
                }

                report.Matched++;
                matched.Add((document, communicationId));
            }

            // F1: which matched communications ALREADY have a linked archive, read once for the page and before any write
            // here, so a row this run links never counts against itself. An attribute of matched rows, not a reason: they link.
            await CountAlreadyArchivedAsync(entityService, matched, report, context, ct).ConfigureAwait(false);

            foreach (var (document, communicationId) in matched)
            {
                ct.ThrowIfCancellationRequested();
                if (!apply)
                {
                    report.AddLinked(document.Id, communicationId);
                    covered.Add(document.Id);
                    continue;
                }

                switch (await LinkAsync(entityService, document, communicationId, context, ct).ConfigureAwait(false))
                {
                    case LinkOutcome.Linked:
                        report.Linked++;
                        report.AddLinked(document.Id, communicationId);
                        covered.Add(document.Id);
                        break;
                    case LinkOutcome.ChangedSinceRead:
                        report.ChangedSinceRead++;
                        break;
                    default:
                        report.Failed++;
                        break;
                }
            }

            await CountChildrenAsync(entityService, covered, unmatched, report, context, ct).ConfigureAwait(false);

            if (documents.Count < take)
            {
                passComplete = true;
                break;
            }
        }

        report.EndAt = last;
        report.PassComplete = passComplete;
        _cursor = passComplete ? null : last;
    }

    /// <summary>
    /// The next <paramref name="take"/> email archives (any state) with no communication link after <paramref name="after"/>,
    /// in id order, with the key and the row version.
    /// </summary>
    internal static QueryExpression BuildCandidateQuery(Guid? after, int take)
    {
        var query = new QueryExpression(DocumentTable)
        {
            ColumnSet = new ColumnSet(CrossPathLink.EmailMessageIdAttribute, ParentDocumentField, VersionNumberField),
            TopCount = take,
        };
        query.Criteria.AddCondition(CrossPathLink.IsEmailArchiveAttribute, ConditionOperator.Equal, true);
        query.Criteria.AddCondition(CrossPathLink.LinkedCommunicationAttribute, ConditionOperator.Null);
        if (after is { } cursor)
            query.Criteria.AddCondition(DocumentPk, ConditionOperator.GreaterThan, cursor);

        query.AddOrder(DocumentPk, OrderType.Ascending);
        return query;
    }

    /// <summary>The communications whose internet message id equals <paramref name="key"/> (Dataverse: ignoring case), at most <see cref="MatchProbeTop"/>.</summary>
    internal static QueryExpression BuildCommunicationQuery(string key)
    {
        var query = new QueryExpression(CommunicationTable)
        {
            ColumnSet = new ColumnSet(InternetMessageIdField),
            TopCount = MatchProbeTop,
        };
        query.Criteria.AddCondition(InternetMessageIdField, ConditionOperator.Equal, key);
        query.AddOrder(CommunicationPk, OrderType.Ascending);
        return query;
    }

    /// <summary>Why a candidate is or is not linked: exactly one per candidate.</summary>
    internal enum LinkReason
    {
        /// <summary>Exactly one communication came back and its internet message id equals the key exactly.</summary>
        Matched,

        /// <summary>The archive has a parent document: never linked (an own link could pre-empt the parent's).</summary>
        HasParent,

        /// <summary>The document records no message id.</summary>
        NoKey,

        /// <summary>The recorded key is a mailbox item id (pre task 121), not an RFC Message-ID: joined to nothing.</summary>
        ExchangeItemId,

        /// <summary>No communication's internet message id equals the key exactly.</summary>
        NoCommunication,

        /// <summary>More than one communication came back for the key (case-only differences included): never linked.</summary>
        Ambiguous,

        /// <summary>The communication query faulted: counted, the run continues, the next pass asks again.</summary>
        Failed,
    }

    /// <summary>
    /// Classifies <paramref name="key"/> against the communications the query returned for it: the pure rule, separate
    /// from the read. <paramref name="communications"/> is what Dataverse returned (its equality ignores case).
    /// </summary>
    internal static (LinkReason Reason, Guid CommunicationId) Classify(string? key, IReadOnlyList<Entity>? communications)
    {
        if (string.IsNullOrWhiteSpace(key))
            return (LinkReason.NoKey, Guid.Empty);

        if (!OfficeEmailEnricher.IsRfcMessageId(key))
            return (LinkReason.ExchangeItemId, Guid.Empty);

        var rows = communications ?? [];
        if (rows.Count > 1)
            return (LinkReason.Ambiguous, Guid.Empty);

        return rows.Count == 1
               && rows[0].Id != Guid.Empty
               && string.Equals(rows[0].GetAttributeValue<string>(InternetMessageIdField), key, StringComparison.Ordinal)
            ? (LinkReason.Matched, rows[0].Id)
            : (LinkReason.NoCommunication, Guid.Empty);
    }

    private async Task<(LinkReason Reason, Guid CommunicationId)> ClassifyAsync(
        IGenericEntityService entityService, Entity document, JobRunContext context, CancellationToken ct)
    {
        // K1: an archive with a parent is never linked — D7 takes the FIRST link on the walk, so its own link could pre-empt a
        // parent's internal-only link. Decided before the key, so no communication query is made for it.
        if (document.GetAttributeValue<EntityReference>(ParentDocumentField) is { Id: var parentId } && parentId != Guid.Empty)
            return (LinkReason.HasParent, Guid.Empty);

        var key = document.GetAttributeValue<string>(CrossPathLink.EmailMessageIdAttribute);
        var keyOnly = Classify(key, null);
        if (keyOnly.Reason is LinkReason.NoKey or LinkReason.ExchangeItemId)
            return keyOnly; // decided on the key alone: no communication query

        IReadOnlyList<Entity> communications;
        try
        {
            communications = (await entityService.RetrieveMultipleAsync(BuildCommunicationQuery(key!), ct).ConfigureAwait(false)).Entities;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The exception is not logged: a fault message may echo the query, and the key is a message id. Its type is enough
            // to tell a throttle from a schema fault.
            _logger.LogWarning(
                "{Prefix} The communication lookup for document {DocumentId} failed ({ExceptionType}); counted, the run continues. "
                + "correlationId={CorrelationId}",
                LogPrefix, document.Id, ex.GetType().Name, context.CorrelationId);
            return (LinkReason.Failed, Guid.Empty);
        }

        return Classify(key, communications);
    }

    private enum LinkOutcome
    {
        Linked,
        ChangedSinceRead,
        Failed,
    }

    /// <summary>
    /// THE write: <c>sprk_relatedcommunication</c> only, set to the exactly-matched communication, conditional on the
    /// version read with the candidate (still unlinked then). Never clears, never re-points, never writes another column.
    /// </summary>
    private async Task<LinkOutcome> LinkAsync(
        IGenericEntityService entityService, Entity document, Guid communicationId, JobRunContext context, CancellationToken ct)
    {
        var version = RowVersionOf(document);
        if (version is null)
        {
            // An unconditional write could overwrite a link set since the read: refuse it.
            _logger.LogWarning(
                "{Prefix} Document {DocumentId} carries no row version, so its link cannot be written conditionally; not linked. "
                + "correlationId={CorrelationId}",
                LogPrefix, document.Id, context.CorrelationId);
            return LinkOutcome.Failed;
        }

        try
        {
            await entityService.UpdateIfRowVersionMatchesAsync(
                DocumentTable,
                document.Id,
                version,
                new Dictionary<string, object>
                {
                    [CrossPathLink.LinkedCommunicationAttribute] = new EntityReference(CommunicationTable, communicationId),
                },
                ct).ConfigureAwait(false);
        }
        catch (System.Data.DBConcurrencyException)
        {
            _logger.LogInformation(
                "{Prefix} Document {DocumentId} changed since it was read; not linked, the next pass looks again. "
                + "correlationId={CorrelationId}",
                LogPrefix, document.Id, context.CorrelationId);
            return LinkOutcome.ChangedSinceRead;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "{Prefix} Linking document {DocumentId} to communication {CommunicationId} failed; counted, the run continues. "
                + "correlationId={CorrelationId}",
                LogPrefix, document.Id, communicationId, context.CorrelationId);
            return LinkOutcome.Failed;
        }

        _logger.LogInformation(
            "{Prefix} Linked document {DocumentId} to communication {CommunicationId}. correlationId={CorrelationId}",
            LogPrefix, document.Id, communicationId, context.CorrelationId);
        return LinkOutcome.Linked;
    }

    private static string? RowVersionOf(Entity document)
    {
        var version = document.RowVersion
            ?? (document.GetAttributeValue<long?>(VersionNumberField) is { } number
                ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null);
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    /// <summary>
    /// Counts the direct children (<c>sprk_parentdocument</c>, any state) of this page's linked (or, in a dry run, linkable)
    /// archives and of its unmatched ones, in one read. A fault leaves the counts short and marks the run partial.
    /// </summary>
    private async Task CountChildrenAsync(
        IGenericEntityService entityService, IReadOnlyList<Guid> covered, IReadOnlyList<Guid> unmatched, LinkReport report,
        JobRunContext context, CancellationToken ct)
    {
        if (covered.Count == 0 && unmatched.Count == 0)
            return;

        IReadOnlyList<Entity> children;
        try
        {
            var page = await entityService.RetrieveMultipleAsync(BuildChildQuery(covered.Concat(unmatched)), ct).ConfigureAwait(false);
            children = page.Entities;
            if (page.MoreRecords)
                report.ChildCountFailed = true; // more children than one page holds: the counts are a floor, said so
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            report.ChildCountFailed = true;
            _logger.LogWarning(ex, "{Prefix} The children of this page's archives could not be counted; the run is partial. "
                + "correlationId={CorrelationId}", LogPrefix, context.CorrelationId);
            return;
        }

        var coveredSet = covered.ToHashSet();
        var unmatchedSet = unmatched.ToHashSet();
        foreach (var child in children)
        {
            var parent = child.GetAttributeValue<EntityReference>(ParentDocumentField)?.Id;
            if (parent is not { } p)
                continue;
            if (coveredSet.Contains(p))
                report.ChildrenCovered++;
            else if (unmatchedSet.Contains(p))
                report.ChildrenOfUnmatched++;
        }
    }

    /// <summary>
    /// F1: counts the matched candidates whose communication already has a linked <c>.eml</c> archive, with one read for the
    /// page (an <c>In</c> over the page's distinct communications). A fault leaves the count short and marks the run partial;
    /// the rows still link (the count is a report attribute, not a gate).
    /// </summary>
    private async Task CountAlreadyArchivedAsync(
        IGenericEntityService entityService, IReadOnlyList<(Entity Document, Guid CommunicationId)> matched, LinkReport report,
        JobRunContext context, CancellationToken ct)
    {
        if (matched.Count == 0)
            return;

        HashSet<Guid> archived;
        try
        {
            var page = await entityService.RetrieveMultipleAsync(
                BuildArchivedCommunicationQuery(matched.Select(m => m.CommunicationId)), ct).ConfigureAwait(false);
            if (page.MoreRecords)
                report.AlreadyArchivedCheckFailed = true; // more archives than one page holds: the count is a floor, said so
            archived = page.Entities
                .Select(e => e.GetAttributeValue<EntityReference>(CrossPathLink.LinkedCommunicationAttribute)?.Id)
                .Where(id => id is { } v && v != Guid.Empty)
                .Select(id => id!.Value)
                .ToHashSet();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            report.AlreadyArchivedCheckFailed = true;
            _logger.LogWarning(ex, "{Prefix} Whether this page's communications already have an archive could not be read; the run "
                + "is partial. correlationId={CorrelationId}", LogPrefix, context.CorrelationId);
            return;
        }

        foreach (var (document, communicationId) in matched)
        {
            if (archived.Contains(communicationId))
                report.CommunicationAlreadyArchived.Add(document.Id);
        }
    }

    /// <summary>The <c>.eml</c> archives (any state) already linked to one of <paramref name="communications"/>.</summary>
    internal static QueryExpression BuildArchivedCommunicationQuery(IEnumerable<Guid> communications)
    {
        var query = new QueryExpression(DocumentTable) { ColumnSet = new ColumnSet(CrossPathLink.LinkedCommunicationAttribute) };
        query.Criteria.AddCondition(CrossPathLink.IsEmailArchiveAttribute, ConditionOperator.Equal, true);
        query.Criteria.AddCondition(
            CrossPathLink.LinkedCommunicationAttribute, ConditionOperator.In, communications.Distinct().Cast<object>().ToArray());
        return query;
    }

    /// <summary>The documents (any state) whose <c>sprk_parentdocument</c> is one of <paramref name="parents"/>.</summary>
    internal static QueryExpression BuildChildQuery(IEnumerable<Guid> parents)
    {
        var query = new QueryExpression(DocumentTable) { ColumnSet = new ColumnSet(ParentDocumentField) };
        query.Criteria.AddCondition(ParentDocumentField, ConditionOperator.In, parents.Distinct().Cast<object>().ToArray());
        return query;
    }

    private static int PositiveInt(string? raw, int fallback) =>
        int.TryParse(raw, out var value) && value > 0 ? value : fallback;

    // ── Run report ───────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class LinkReport
    {
        public Guid? StartAfter { get; set; }
        public Guid? EndAt { get; set; }
        public bool PassComplete { get; set; }
        public bool EnumerationFailed { get; set; }
        public bool ChildCountFailed { get; set; }
        public bool AlreadyArchivedCheckFailed { get; set; }
        public CountedIds CommunicationAlreadyArchived { get; } = new();
        public int Examined { get; set; }
        public int Matched { get; set; }
        public int Linked { get; set; }
        public int ChangedSinceRead { get; set; }
        public int Failed { get; set; }
        public int ChildrenCovered { get; set; }
        public int ChildrenOfUnmatched { get; set; }
        public CountedIds HasParent { get; } = new();
        public CountedIds NoKey { get; } = new();
        public CountedIds ExchangeItemId { get; } = new();
        public CountedIds NoCommunication { get; } = new();
        public CountedIds Ambiguous { get; } = new();
        public List<object> LinkedSample { get; } = [];

        public void Unmatched(LinkReason reason, Guid documentId)
        {
            var list = reason switch
            {
                LinkReason.HasParent => HasParent,
                LinkReason.NoKey => NoKey,
                LinkReason.ExchangeItemId => ExchangeItemId,
                LinkReason.NoCommunication => NoCommunication,
                LinkReason.Ambiguous => Ambiguous,
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "not an unmatched reason"),
            };
            list.Add(documentId);
        }

        public void AddLinked(Guid documentId, Guid communicationId)
        {
            if (LinkedSample.Count < MaxListedPerRun)
                LinkedSample.Add(new { documentId, communicationId });
        }

        public object ToJson(bool apply) => new
        {
            startAfter = StartAfter,
            endAt = EndAt,
            passComplete = PassComplete,
            enumerationFailed = EnumerationFailed,
            examined = Examined,
            linked = apply ? Linked : 0,
            wouldLink = apply ? 0 : Matched,
            hasParent = HasParent.Count,
            noKey = NoKey.Count,
            exchangeItemId = ExchangeItemId.Count,
            noCommunication = NoCommunication.Count,
            ambiguous = Ambiguous.Count,
            changedSinceRead = ChangedSinceRead,
            failed = Failed,
            childrenCovered = ChildrenCovered,
            childrenOfUnmatched = ChildrenOfUnmatched,
            childCountFailed = ChildCountFailed,
            communicationAlreadyArchived = CommunicationAlreadyArchived.ToJson(),
            alreadyArchivedCheckFailed = AlreadyArchivedCheckFailed,
            linkedListed = LinkedSample.Count,
            linkedDocuments = LinkedSample,
            unmatched = new
            {
                hasParent = HasParent.ToJson(),
                noKey = NoKey.ToJson(),
                exchangeItemId = ExchangeItemId.ToJson(),
                noCommunication = NoCommunication.ToJson(),
                ambiguous = Ambiguous.ToJson(),
            },
        };
    }

    /// <summary>Every id counted; at most <see cref="MaxListedPerRun"/> listed.</summary>
    private sealed class CountedIds
    {
        private readonly List<Guid> _listed = [];

        public int Count { get; private set; }

        public void Add(Guid id)
        {
            Count++;
            if (_listed.Count < MaxListedPerRun)
                _listed.Add(id);
        }

        public object ToJson() => new { total = Count, listed = _listed.Count, documentIds = _listed };
    }
}
