namespace Sprk.Bff.Api.Api.Admin.Models;

/// <summary>
/// Request body of <c>POST /api/admin/jobs/record-repair-pass/runs</c> (field-mapping-server-write-path-r1 task 052c): repair ONE
/// table's rows created in ONE window. The bounds are strings so the route can refuse one without an offset (a typed
/// <see cref="DateTimeOffset"/> would read it as local time).
/// </summary>
/// <param name="Table">A table the scheduled repair pass sweeps (logical name, for example <c>sprk_event</c>).</param>
/// <param name="CreatedFrom">First <c>createdon</c> instant (inclusive), ISO 8601 with an offset; not before go-live.</param>
/// <param name="CreatedTo">End of the window (exclusive), ISO 8601 with an offset; not after now.</param>
public sealed record RecordRepairRunRequest(string? Table, string? CreatedFrom, string? CreatedTo);
