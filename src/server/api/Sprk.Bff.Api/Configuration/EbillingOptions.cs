using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the eBilling capability (outside-counsel invoice submission, extraction, review
/// and AP export). Bound to the "Ebilling" configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one eBilling flag</b> (ebilling-r1 spec FR-45, ADR-018 "Flag Scope Discipline" rule 2).
/// <see cref="Enabled"/> is the only kill switch for the whole capability; there are no per-service
/// flags. Every eBilling route chains <c>RequireEbillingEnabled()</c> after its authorization filter
/// (<c>EbillingEnabledFilter</c>), and every eBilling job handler checks <see cref="Enabled"/> itself
/// (ADR-018: "check same flag in both endpoints and async handlers"). The flag never replaces
/// authorization.
/// </para>
/// <para>
/// <b>Per-environment behaviour:</b> <see cref="Enabled"/> defaults to <c>false</c>, so a host with
/// no "Ebilling" section starts with eBilling off. It is switched on in dev first (task 053); other
/// environments stay off until the owner enables them. The existing Finance routes
/// (<c>/api/finance/invoice-review/reject</c>, rollups) are governed by <see cref="FinanceOptions"/> and
/// are NOT affected by this flag. <c>/api/finance/invoice-review/confirm</c> IS (ebilling-r1 050a, D-134):
/// it creates an invoice submission, which only the eBilling job processes.
/// </para>
/// </remarks>
public class EbillingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ebilling";

    /// <summary>Lower bound (inclusive) of <see cref="MaxSubmissionFileSizeMb"/>.</summary>
    public const int MinSubmissionFileSizeMb = 1;

    /// <summary>Upper bound (inclusive) of <see cref="MaxSubmissionFileSizeMb"/>.</summary>
    public const int MaxSubmissionFileSizeMbCeiling = 100;

    /// <summary>
    /// Master switch for eBilling. Default <c>false</c> (off). When false every eBilling endpoint
    /// returns 503 ProblemDetails with <c>errorCode</c> <c>ebilling.disabled</c>, and eBilling job
    /// handlers do no work.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Largest invoice PDF a submission may upload, in megabytes. Default 25. Must be within
    /// 1..100; an out-of-range value fails host startup (ValidateOnStart), not the first request.
    /// Read by the submission operation (task 044) for its 413 response.
    /// </summary>
    [Range(MinSubmissionFileSizeMb, MaxSubmissionFileSizeMbCeiling)]
    public int MaxSubmissionFileSizeMb { get; set; } = 25;
}
