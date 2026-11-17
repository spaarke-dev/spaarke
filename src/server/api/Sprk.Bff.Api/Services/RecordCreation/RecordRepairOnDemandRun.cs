using System.Globalization;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// One on-demand run of the repair pass (field-mapping-server-write-path-r1 task 052c; spec FR-25, NFR-01): ONE table the pass
/// repairs, ONE bounded window of <c>createdon</c>, started by an administrator through
/// <c>POST /api/admin/jobs/record-repair-pass/runs</c>. It covers rows the scheduled pass's watermark has passed and will not
/// revisit (052b notes K1, K6, K9).
/// </summary>
/// <remarks>
/// <para><b>Only a valid run exists.</b> The constructor is private: an instance comes only from <see cref="TryCreate"/>, which
/// applies every rule below. The admin route builds it and hands it to the job as a typed value in
/// <c>JobRunContext.Parameters</c> under <see cref="ParameterKey"/>; the job runs in on-demand mode only for a
/// <c>ManualAdmin</c> run that carries such an instance (<see cref="RecordRepairPassJob.OnDemandRunOf"/>). A persisted
/// <c>ConfigJson</c> reaches a run as one string, so it can never carry one.</para>
/// <para><b>The rules</b> (<see cref="Check"/>, asked by the route before any lease and again by the job before any read):
/// the table is one the scheduled pass sweeps; <c>createdFrom</c> &lt; <c>createdTo</c>, both ISO 8601 with an offset;
/// <c>createdTo</c> not after now; the window at most <see cref="MaxWindow"/>; <c>createdFrom</c> not before go-live (the pass is
/// forward-only: the window is refused, never clamped).</para>
/// <para>Component Justification (CLAUDE.md §11): (1) Existing — the generic trigger runs a job by id with no parameters, and
/// <see cref="RecordRepairPassJob"/> selects rows by its watermark only. (2) Extension — the job gains an on-demand mode; this
/// type is the one value that switches it on, so the rules live in one place for the route and the job. (3) Cost of doing
/// nothing — rows the watermark has passed (the invoice family before its guard, rows abandoned after their attempts, owners
/// held back by the Assign-cascade allow-list) are never repaired.</para>
/// </remarks>
internal sealed class RecordRepairOnDemandRun
{
    /// <summary>The key of the run in <c>JobRunContext.Parameters</c>. Only an instance of this type under it counts.</summary>
    internal const string ParameterKey = "recordRepairPass.onDemandRun";

    /// <summary>
    /// The longest window one run takes. One run reads at most the pass's row cap (<c>RecordRepair:Pass:MaxRowsPerTable</c>), so a
    /// longer window would mostly report truncated; 31 days covers one calendar month (the unit an administrator usually states)
    /// and keeps each scan's <c>createdon</c> range bounded. A wider span is several runs.
    /// </summary>
    internal static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);

    /// <summary>ISO 8601 with an explicit offset or <c>Z</c>; with or without a fraction of a second.</summary>
    private static readonly string[] OffsetFormats =
    [
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];

    /// <summary>The same shapes with no offset: recognised only to name the refusal.</summary>
    private static readonly string[] LocalFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
    ];

    private RecordRepairOnDemandRun(string table, DateTimeOffset createdFrom, DateTimeOffset createdTo)
    {
        Table = table;
        CreatedFrom = createdFrom;
        CreatedTo = createdTo;
    }

    /// <summary>The table, spelled as the pass's table list spells it.</summary>
    internal string Table { get; }

    /// <summary>The first instant of the window (inclusive), UTC.</summary>
    internal DateTimeOffset CreatedFrom { get; }

    /// <summary>The end of the window (exclusive), UTC.</summary>
    internal DateTimeOffset CreatedTo { get; }

    /// <summary>
    /// A run for <paramref name="table"/> over [<paramref name="createdFrom"/>, <paramref name="createdTo"/>), or <c>null</c> and
    /// the refusal. <paramref name="goLive"/> is the configured go-live instant (the caller refuses an unset one first);
    /// <paramref name="tables"/> the tables the scheduled pass sweeps.
    /// </summary>
    internal static RecordRepairOnDemandRun? TryCreate(
        string? table,
        string? createdFrom,
        string? createdTo,
        DateTimeOffset goLive,
        DateTimeOffset now,
        IReadOnlyCollection<string> tables,
        out RecordRepairRunRefusal? refusal)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var listed = string.IsNullOrWhiteSpace(table)
            ? null
            : tables.FirstOrDefault(t => string.Equals(t, table.Trim(), StringComparison.OrdinalIgnoreCase));
        if (listed is null)
        {
            refusal = new RecordRepairRunRefusal(RecordRepairRunReason.TableNotRepaired,
                "The table is not one the repair pass repairs.");
            return null;
        }

        if (ParseInstant(createdFrom, "createdFrom", out var from) is { } fromRefusal)
        {
            refusal = fromRefusal;
            return null;
        }

        if (ParseInstant(createdTo, "createdTo", out var to) is { } toRefusal)
        {
            refusal = toRefusal;
            return null;
        }

        refusal = Check(listed, from, to, goLive, now, tables);
        return refusal is null ? new RecordRepairOnDemandRun(listed, from, to) : null;
    }

    /// <summary>
    /// <c>null</c> when a run of <paramref name="table"/> over [<paramref name="from"/>, <paramref name="to"/>) may start; otherwise
    /// why not. Pure. The job asks it again with its own go-live, clock and table list before it reads anything.
    /// </summary>
    internal static RecordRepairRunRefusal? Check(
        string table, DateTimeOffset from, DateTimeOffset to, DateTimeOffset goLive, DateTimeOffset now, IReadOnlyCollection<string> tables)
    {
        if (!tables.Contains(table, StringComparer.OrdinalIgnoreCase))
            return new RecordRepairRunRefusal(RecordRepairRunReason.TableNotRepaired, "The table is not one the repair pass repairs.");

        if (from >= to)
            return new RecordRepairRunRefusal(RecordRepairRunReason.WindowEmpty, "createdFrom must be before createdTo.");

        if (to - from > MaxWindow)
        {
            return new RecordRepairRunRefusal(RecordRepairRunReason.WindowTooLong,
                $"The window may be at most {MaxWindow.TotalDays.ToString(CultureInfo.InvariantCulture)} days; run a longer span as several windows.");
        }

        if (to > now)
            return new RecordRepairRunRefusal(RecordRepairRunReason.WindowInFuture, "createdTo must not be after the current time.");

        if (from < goLive)
        {
            return new RecordRepairRunRefusal(RecordRepairRunReason.BeforeGoLive,
                "The repair pass is forward-only: createdFrom must not be before the go-live instant ("
                + goLive.ToString("O", CultureInfo.InvariantCulture) + ").");
        }

        return null;
    }

    /// <summary>The run's window and table for the heartbeat and the result.</summary>
    public override string ToString() =>
        $"{Table} [{CreatedFrom.ToString("O", CultureInfo.InvariantCulture)}, {CreatedTo.ToString("O", CultureInfo.InvariantCulture)})";

    private static RecordRepairRunRefusal? ParseInstant(string? raw, string field, out DateTimeOffset value)
    {
        var text = raw?.Trim();
        if (!string.IsNullOrEmpty(text)
            && DateTimeOffset.TryParseExact(text, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            value = value.ToUniversalTime();
            return null;
        }

        value = default;
        if (!string.IsNullOrEmpty(text)
            && DateTime.TryParseExact(text, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return new RecordRepairRunRefusal(RecordRepairRunReason.TimestampOffsetMissing,
                $"{field} must carry its offset (for example 2026-11-01T00:00:00Z or 2026-11-01T00:00:00+01:00).");
        }

        return new RecordRepairRunRefusal(RecordRepairRunReason.TimestampInvalid,
            $"{field} must be an ISO 8601 instant with an offset (for example 2026-11-01T00:00:00Z).");
    }
}

/// <summary>Why an on-demand repair run was refused: a stable <see cref="RecordRepairRunReason"/> code and a plain sentence.</summary>
internal sealed record RecordRepairRunRefusal(string Code, string Detail);

/// <summary>The stable reason codes of a refused on-demand repair run (task 052c), carried as <c>reasonCode</c>.</summary>
internal static class RecordRepairRunReason
{
    /// <summary>The request body is not a JSON object with the run's fields.</summary>
    internal const string BodyInvalid = "record_repair_run.body_invalid";

    /// <summary>The table is not one the scheduled pass sweeps (unregistered, or excluded: task, account and the others).</summary>
    internal const string TableNotRepaired = "record_repair_run.table_not_repaired";

    /// <summary>A bound is missing or is not an ISO 8601 instant.</summary>
    internal const string TimestampInvalid = "record_repair_run.timestamp_invalid";

    /// <summary>A bound is an ISO 8601 date and time without its offset.</summary>
    internal const string TimestampOffsetMissing = "record_repair_run.timestamp_offset_missing";

    /// <summary><c>createdFrom</c> is not before <c>createdTo</c>.</summary>
    internal const string WindowEmpty = "record_repair_run.window_empty";

    /// <summary>The window is longer than <see cref="RecordRepairOnDemandRun.MaxWindow"/>.</summary>
    internal const string WindowTooLong = "record_repair_run.window_too_long";

    /// <summary><c>createdTo</c> is after the current time.</summary>
    internal const string WindowInFuture = "record_repair_run.window_in_future";

    /// <summary><c>createdFrom</c> is before the go-live instant: the pass is forward-only.</summary>
    internal const string BeforeGoLive = "record_repair_run.before_go_live";

    /// <summary>The go-live instant is not set: the pass is off, and no run starts.</summary>
    internal const string NotConfigured = "record_repair_run.not_configured";

    /// <summary>The go-live instant is set but does not parse: the pass is off, and no run starts.</summary>
    internal const string GoLiveInvalid = "record_repair_run.go_live_invalid";
}
