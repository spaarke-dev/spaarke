using System.Globalization;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>What the removal sweep does with one contact (<see cref="ExternalAccessRemovalSweep.Decide"/>).</summary>
internal enum RemovalSweepAction
{
    /// <summary>Nothing: an active contact, an inactive one holding neither a binding nor a grant, or a marked one that is clean.</summary>
    None,

    /// <summary>An inactive, unmarked contact that holds a binding or an active grant: mark it, then revoke.</summary>
    MarkAndRevoke,

    /// <summary>A marked contact that still holds an active grant row: the removal did not complete, so it runs again.</summary>
    RerunRemoval,

    /// <summary>The marker read is not proven (or failed): the contact is skipped and logged, never marked.</summary>
    SkipMarkerUnproven,
}

/// <summary>One contact the sweep acted on, or would act on in report-only mode.</summary>
internal sealed record RemovalSweepContact(Guid ContactId, RemovalSweepAction Action, PersonAdministrationStatus? Status);

/// <summary>What one sweep pass found and did.</summary>
internal sealed class RemovalSweepReport
{
    public bool Writes { get; init; }
    public int MaxContactsPerRun { get; init; }
    public bool ScanFailed { get; set; }
    public string? ScanError { get; set; }
    public bool ScanTruncated { get; set; }
    public int Candidates { get; set; }
    public int MarkerUnproven { get; set; }
    public int ReadFailed { get; set; }
    public int Clean { get; set; }
    public int DeferredByCap { get; set; }
    public int Completed { get; set; }
    public int Incomplete { get; set; }
    public List<RemovalSweepContact> Contacts { get; } = new();

    /// <summary>Rows written: contacts whose removal ran in write mode (complete or not).</summary>
    public int Changed => Writes ? Completed + Incomplete : 0;
}

/// <summary>
/// The removal sweep: a contact that staff deactivated while it still holds external access is removed the same way an
/// administrator removes a person. One pass of <see cref="IdentityLinkReconciliationJob"/> in this BFF's own environment.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> A contact the sweep marks is (a) inactive (<c>statecode</c> 1), (b) unmarked on a PROVEN read of
/// <c>sprk_externalaccessremovedon</c>, and (c) holds a binding (<c>sprk_externalobjectid</c>) or at least one active grant row
/// as grantee. Nothing else is marked: an active contact never, an inactive one with neither a binding nor a grant never, and a
/// contact whose marker read is not proven is skipped and logged. A marked contact runs again only while the LIVE grant table
/// still holds an active row for it; a marked contact that is clean is not touched. No flag is stored.</para>
/// <para><b>One removal.</b> Every contact is removed through <see cref="ExternalPersonRemovalService.RemoveAsync"/>, the
/// method <c>POST /remove-person</c> calls: the marker first, then the shared revoke of every grant (the SPE cleanup with it),
/// the person's organization-grant container permissions, the cache eviction and the invite, access and standing-grant
/// clears. It changes neither the statecode nor the binding. A source-scan guard pins that the sweep reaches no part of the
/// removal any other way.</para>
/// <para><b>Gates.</b> Writes need BOTH <c>IdentityLink:Reconciliation:WritesEnabled</c> and
/// <see cref="RemovalSweepWritesEnabledConfigKey"/>. With either absent, empty, unparseable or false the pass is report-only:
/// it logs what it would do and writes nothing.</para>
/// <para><b>Bounded.</b> At most <see cref="MaxContactsPerRunConfigKey"/> contacts (default
/// <see cref="DefaultMaxContactsPerRun"/>) are acted on per run. Once the cap is reached the remaining candidates are neither
/// read nor decided: they are counted (<see cref="RemovalSweepReport.DeferredByCap"/>) and left for the next run, and the run
/// reports partial.</para>
/// <para><b>Audit.</b> Every removal the sweep runs is audited with the sweep as the actor (<see cref="SweepRemovalAuditEvent"/>)
/// through <see cref="ExternalAccessAudit.Write"/>, under <see cref="ExternalAccessAudit.Category"/>. No email is logged.</para>
/// <para><b>CLAUDE.md §11.</b> Existing: <see cref="ExternalPersonRemovalService"/> removes one person on an administrator's
/// request; <see cref="IdentityLinkReconciliationJob"/> is the scheduled pass over contacts. Extension: the sweep holds only the
/// selection and the gate; the removal is that service, called, and the schedule is that job (no new job, no DI
/// registration). Cost of doing nothing: a contact staff deactivate keeps its binding and its grant rows, so its access is
/// never revoked and the removed marker is never written.</para>
/// </remarks>
internal sealed class ExternalAccessRemovalSweep
{
    /// <summary>
    /// The sweep's own write gate, ANDed with <c>IdentityLink:Reconciliation:WritesEnabled</c>. Absent, empty or unparseable =
    /// report-only. §11: Existing — the link switch gates every write of the job. Extension — reusing it alone would start
    /// marking contacts on any deploy to an environment where linking is already on. Cost of doing nothing: a BFF deploy alone
    /// would begin removing people's access before anyone reviewed a report-only run.
    /// </summary>
    internal const string RemovalSweepWritesEnabledConfigKey = "IdentityLink:Reconciliation:RemovalSweepWritesEnabled";

    /// <summary>
    /// The most contacts one run acts on. §11: Existing — the job's paging ceiling bounds reads, not writes. Extension — the
    /// ceiling is 20 000 rows, far too many removals for one 5-minute tick. Cost of doing nothing: a first write run over a
    /// backlog would remove every candidate at once, with no chance to stop it part way.
    /// </summary>
    internal const string MaxContactsPerRunConfigKey = "IdentityLink:Reconciliation:RemovalSweepMaxContactsPerRun";

    /// <summary>The per-run cap when <see cref="MaxContactsPerRunConfigKey"/> is absent or not a positive integer.</summary>
    internal const int DefaultMaxContactsPerRun = 25;

    /// <summary>The rows each candidate query reads. A page above it reports the scan truncated.</summary>
    internal const int MaxCandidatesPerQuery = 500;

    /// <summary>
    /// The audit verb of a removal the sweep ran (an event id on the audit trace). Distinct from 9821 (an administrator's
    /// removal), whose record names the administrator and the leg of their authority: the sweep has neither, and a query for
    /// administrator removals must not count scheduled ones. §11: Existing — 9821. Extension — reusing it would mix two
    /// actors under one verb with different fields. Cost of doing nothing: a sweep removal leaves no audit record.
    /// </summary>
    internal static readonly EventId SweepRemovalAuditEvent = new(9823, "ExternalAccessPersonRemovedBySweep");

    /// <summary>The actor named on the sweep's audit record.</summary>
    internal const string SweepActor = "identity-link-reconciliation removal sweep";

    private const string ContactEntitySet = "contacts";

    private readonly DataverseWebApiClient _dataverse;
    private readonly IContactIdentityStore _store;
    private readonly Func<ExternalPersonRemovalService> _removal;
    private readonly ILogger _logger;
    private readonly ILogger _auditLogger;

    /// <param name="removal">Builds the shared removal; called only when a write-mode run acts on a contact.</param>
    public ExternalAccessRemovalSweep(
        DataverseWebApiClient dataverse,
        IContactIdentityStore store,
        Func<ExternalPersonRemovalService> removal,
        ILogger logger,
        ILogger auditLogger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _removal = removal ?? throw new ArgumentNullException(nameof(removal));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    }

    /// <summary>True only when the link switch AND <see cref="RemovalSweepWritesEnabledConfigKey"/> parse to <c>true</c>.</summary>
    internal static bool WritesEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ContactIdentityBinder.LinkWritesEnabled(configuration)
               && bool.TryParse(configuration[RemovalSweepWritesEnabledConfigKey], out var enabled) && enabled;
    }

    /// <summary>The per-run cap: a positive integer from configuration (at most <see cref="MaxCandidatesPerQuery"/>), else the default.</summary>
    internal static int MaxContactsPerRun(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return int.TryParse(configuration[MaxContactsPerRunConfigKey], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap)
               && cap > 0
            ? Math.Min(cap, MaxCandidatesPerQuery)
            : DefaultMaxContactsPerRun;
    }

    /// <summary>
    /// The closed selection, from live reads of one contact. Pure. Only a proven-empty marker on an inactive contact holding
    /// a binding or an active grant is marked; only a marked contact still holding an active grant runs again.
    /// </summary>
    internal static RemovalSweepAction Decide(int? stateCode, bool holdsBinding, bool holdsActiveGrant, RemovedMarkerRead marker)
        => marker switch
        {
            RemovedMarkerRead.NotRemoved => stateCode == 1 && (holdsBinding || holdsActiveGrant)
                ? RemovalSweepAction.MarkAndRevoke
                : RemovalSweepAction.None,
            RemovedMarkerRead.Removed => holdsActiveGrant ? RemovalSweepAction.RerunRemoval : RemovalSweepAction.None,
            _ => RemovalSweepAction.SkipMarkerUnproven,
        };

    /// <summary>
    /// The candidate contacts query: inactive, bound and unmarked. A HINT only: every candidate is decided on its own live
    /// reads (<see cref="Decide"/>), so a filter that a column's field security changes never marks anyone by itself.
    /// </summary>
    internal static string InactiveBoundUnmarkedFilter
        => $"statecode eq 1 and {ContactBindingDecision.ExternalObjectIdColumn} ne null and "
           + $"{ContactBindingDecision.ExternalAccessRemovedOnColumn} eq null";

    /// <summary>
    /// The candidate grants query: active person grants whose contact is inactive or marked. A HINT only, as for
    /// <see cref="InactiveBoundUnmarkedFilter"/>.
    /// </summary>
    internal static string ActiveGrantsOfInactiveOrMarkedContactsFilter
        => "statecode eq 0 and _sprk_contact_value ne null and "
           + $"(sprk_Contact/statecode eq 1 or sprk_Contact/{ContactBindingDecision.ExternalAccessRemovedOnColumn} ne null)";

    /// <summary>Runs one pass. <paramref name="writes"/> is both gates ANDed (<see cref="WritesEnabled"/>).</summary>
    public async Task<RemovalSweepReport> RunAsync(
        bool writes, int maxContactsPerRun, string environment, string? correlationId, CancellationToken ct)
    {
        var mode = writes ? IdentityLinkReconciliationJob.ModeWrite : IdentityLinkReconciliationJob.ModeReportOnly;
        var report = new RemovalSweepReport { Writes = writes, MaxContactsPerRun = maxContactsPerRun };

        var candidates = await FindCandidatesAsync(report, ct).ConfigureAwait(false);
        if (report.ScanFailed)
        {
            _logger.LogError(
                "[ID-LINK-RECON] removal-sweep {Environment}: the candidate scan failed ({Error}); no contact was decided. "
                + "correlationId={CorrelationId}", environment, report.ScanError, correlationId);
            return report;
        }

        report.Candidates = candidates.Count;
        var acted = 0;
        foreach (var contactId in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // Slice C5: the cap is checked BEFORE the live reads, so a run past its cap reads nothing more; the rest are
            // counted and left for the next run.
            if (acted >= maxContactsPerRun)
            {
                report.DeferredByCap++;
                continue;
            }

            var action = await DecideLiveAsync(report, contactId, ct).ConfigureAwait(false);
            if (action is null)
            {
                continue; // a read failed; logged, retried next run
            }

            switch (action.Value)
            {
                case RemovalSweepAction.None:
                    report.Clean++;
                    continue;
                case RemovalSweepAction.SkipMarkerUnproven:
                    report.MarkerUnproven++;
                    _logger.LogWarning(
                        "[ID-LINK-RECON] removal-sweep mode={Mode} environment={Environment} contact={ContactId}: the removed "
                        + "marker's read is not proven, so the contact is skipped and not marked. correlationId={CorrelationId}",
                        mode, environment, contactId, correlationId);
                    continue;
            }

            acted++;
            _logger.LogInformation(
                "[ID-LINK-RECON] removal-sweep mode={Mode} environment={Environment} contact={ContactId} action={Action} "
                + "before=[statecode unchanged, binding kept] correlationId={CorrelationId}",
                mode, environment, contactId, action.Value, correlationId);

            if (!writes)
            {
                report.Contacts.Add(new RemovalSweepContact(contactId, action.Value, null));
                continue;
            }

            // Slice C5: audited whatever happens, so a removal that throws is recorded as interrupted, never lost.
            var outcome = PersonAdministrationOutcome.Interrupted;
            try
            {
                outcome = await _removal().RemoveAsync(contactId, correlationId, ct).ConfigureAwait(false);
            }
            finally
            {
                Audit(contactId, action.Value, outcome, correlationId);
            }

            if (outcome.Status == PersonAdministrationStatus.Completed) report.Completed++; else report.Incomplete++;
            report.Contacts.Add(new RemovalSweepContact(contactId, action.Value, outcome.Status));
        }

        if (report.DeferredByCap > 0)
        {
            _logger.LogInformation(
                "[ID-LINK-RECON] removal-sweep {Environment}: {Deferred} contact(s) left for the next run (cap {Cap}). "
                + "correlationId={CorrelationId}", environment, report.DeferredByCap, maxContactsPerRun, correlationId);
        }

        return report;
    }

    /// <summary>The live reads of one contact, and the decision. Null when a read failed (logged).</summary>
    private async Task<RemovalSweepAction?> DecideLiveAsync(RemovalSweepReport report, Guid contactId, CancellationToken ct)
    {
        ContactBindingRow? row;
        RemovedMarkerRead marker;
        bool holdsActiveGrant;
        try
        {
            var lookup = await _store.GetContactAsync(contactId, ct).ConfigureAwait(false);
            if (lookup.Status != LookupStatus.Read)
            {
                report.ReadFailed++;
                _logger.LogWarning("[ID-LINK-RECON] removal-sweep contact={ContactId}: the contact could not be read ({Status}).",
                    contactId, lookup.Status);
                return null;
            }

            row = lookup.Rows.FirstOrDefault(r => r.ContactId == contactId);
            if (row is null)
            {
                report.Clean++;
                return null; // gone since the scan
            }

            marker = await _store.ReadRemovedMarkerAsync(contactId, ct).ConfigureAwait(false);
            var grants = await ExternalGrantLifecycle.QueryActiveRowsForContactAsync(_dataverse, contactId, 1, ct)
                .ConfigureAwait(false);
            holdsActiveGrant = grants.Count > 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            report.ReadFailed++;
            _logger.LogWarning(ex, "[ID-LINK-RECON] removal-sweep contact={ContactId}: a live read failed; not decided.", contactId);
            return null;
        }

        return Decide(row.StateCode, !string.IsNullOrWhiteSpace(row.RawOid), holdsActiveGrant, marker);
    }

    /// <summary>The candidate contact ids: holders of active grants first (they still have access), then bound ones.</summary>
    private async Task<List<Guid>> FindCandidatesAsync(RemovalSweepReport report, CancellationToken ct)
    {
        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();
        try
        {
            var grants = await _dataverse.QueryAsync<ExternalGrantRow>(
                ExternalGrantLifecycle.EntitySet,
                filter: ActiveGrantsOfInactiveOrMarkedContactsFilter,
                select: "sprk_externalrecordaccessid,_sprk_contact_value",
                top: MaxCandidatesPerQuery + 1,
                cancellationToken: ct).ConfigureAwait(false);
            report.ScanTruncated |= grants.Count > MaxCandidatesPerQuery;
            foreach (var grant in grants.Take(MaxCandidatesPerQuery))
            {
                if (grant.ContactId is { } id && id != Guid.Empty && seen.Add(id)) ids.Add(id);
            }

            var contacts = await _dataverse.QueryAsync<CandidateContactRow>(
                ContactEntitySet,
                filter: InactiveBoundUnmarkedFilter,
                select: "contactid",
                top: MaxCandidatesPerQuery + 1,
                cancellationToken: ct).ConfigureAwait(false);
            report.ScanTruncated |= contacts.Count > MaxCandidatesPerQuery;
            foreach (var contact in contacts.Take(MaxCandidatesPerQuery))
            {
                if (contact.ContactId != Guid.Empty && seen.Add(contact.ContactId)) ids.Add(contact.ContactId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            report.ScanFailed = true;
            report.ScanError = ex.Message;
        }

        return ids;
    }

    private void Audit(Guid contactId, RemovalSweepAction action, PersonAdministrationOutcome outcome, string? correlationId)
        => ExternalAccessAudit.Write(_auditLogger, audit => audit.LogInformation(SweepRemovalAuditEvent,
            "[EXT-PERSON-AUDIT] sweep-remove: actor {Actor}, action {SweepAction}, contact {ContactId}, statecode at start "
            + "{StateCode}, status {Status}, steps [{Steps}], grants revoked {GrantsRevoked}, grants not completed "
            + "[{GrantsNotCompleted}], correlation {CorrelationId}.",
            SweepActor, action, contactId, outcome.StateCodeAtStart?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
            outcome.Status,
            string.Join("; ", outcome.Steps.Select(s => $"{s.Step}={(s.Completed ? "completed" : "not completed")}")),
            outcome.GrantsRevoked,
            string.Join(", ", outcome.GrantsNotCompleted ?? Array.Empty<Guid>()),
            correlationId));

    /// <summary>The one column the candidate contacts query reads.</summary>
    internal sealed class CandidateContactRow
    {
        [JsonPropertyName("contactid")]
        public Guid ContactId { get; set; }
    }
}
