// §11 justification (CLAUDE.md, ebilling-r1 task 113):
//   (1) Existing — InvoiceReviewReadService (100) builds the review read model; ProcedureResolver (082) computes the current Procedure resolution;
//       ProcedureSnapshot (082d) stores the resolution an invoice was judged under. Nothing compares the two.
//   (2) Extension — the read model could hold the comparison inline, but it runs the resolver APP-ONLY with its own timeout and fault rules (model
//       8.2, uac-r2 C2 and C3), which the caller-scoped read model does not have; keeping it apart also keeps the one "never for an external
//       caller" decision in one place. The current side is built with ProcedureSnapshot.Build, the builder that wrote the stored side.
//   (3) Cost of doing nothing — after an override removal, a Procedure retire or a newly published version, a reviewer approves an invoice
//       judged under rules that no longer apply, with no sign of it (O-21 revised: no automatic re-check).
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

/// <summary>
/// The Procedure-change banner of one invoice (ebilling-r1 task 113, model 8.2, 15 answer 6): ONE state and its text, and nothing else. No diff, name,
/// id, count or reason is ever a member of it. Serialised on the review route's response only, and only for an internal caller of an open invoice.
/// </summary>
/// <param name="State"><see cref="ProcedureChangeBanner.Changed"/>, <see cref="ProcedureChangeBanner.NotChanged"/>,
/// <see cref="ProcedureChangeBanner.NotYetChecked"/> or <see cref="ProcedureChangeBanner.CouldNotBeChecked"/>.</param>
/// <param name="Text">The sentence the reviewer reads; omitted for <see cref="ProcedureChangeBanner.NotChanged"/>.</param>
public sealed record ProcedureChangeBannerDto(
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text);

/// <summary>
/// Computes the read-time Procedure-change banner (ebilling-r1 task 113, model 8.2). For an open invoice and an internal caller it re-runs the
/// Procedure resolver ONCE for the invoice (<see cref="ProcedureResolver.ReadAsync"/>, app-only), resolves every work date the evaluation judged in
/// memory, and compares the result with the resolution stored in the invoice's evaluation snapshot (<see cref="ProcedureSnapshot"/>). Nothing is
/// stored: no column, no marker, no cache entry.
/// </summary>
/// <remarks>
/// <para><b>What is compared.</b> The snapshot's <c>chain</c>, <c>overrides</c> and <c>resolutions</c> members: the core-record chain, every override
/// row that decided anything (mode, record, distance, Procedure, pinned version, rule, firm and the reason), and per work date the Procedures applied
/// with their Procedure Version, the rules (policy versions) in force, the rules and Procedures excluded and the gaps. The current side is built with
/// the SAME builder that wrote the stored side (<see cref="ProcedureSnapshot.Build(BillingProcedureBasis)"/>) over the dates the evaluator judges
/// (the invoice date, and each Fee and Expense line's date, or the invoice date when it has none), so the two differ only by what they were built
/// from. Any difference is <see cref="Changed"/>: a removed or added override, a record rule switched off, a Procedure retired or newly assigned, a
/// version ended, retired or newly published (a first version included), a date moved across a version boundary. The snapshot's <c>applications</c>
/// member (how precedence treated each rule per subject) is not compared: it is the evaluator's output over the findings, not the resolution, and
/// every input it is derived from is in the members above.</para>
/// <para><b>States.</b> <see cref="NotYetChecked"/>: the invoice has no usable snapshot (no submission yet, a submission still processing, a row stored
/// before the snapshot existed, a snapshot a patch or the size cap dropped, a damaged or unknown-version member). <see cref="CouldNotBeChecked"/>:
/// any fault, a 429, a timeout, or an invoice that does not name exactly one core record. A fault never reads as <see cref="NotChanged"/>.</para>
/// <para><b>Who and when.</b> A result is returned only when step 0 classed the caller internal (<see cref="FinanceStepZeroVerdict.Internal"/>, task
/// 099, read from the request, never resolved a second time here) and the invoice is open (In Review; "pending" is an In Review invoice whose
/// submission is still processing). Otherwise <c>null</c>: the member is absent. An invoice no Procedure judged is exempt too (it was judged only by
/// the submission's own system finding): one with no firm (the resolver needs a firm), and one whose stored findings carry no decision, which is how
/// the pipeline records that no evaluation ran (no firm at submission, or the Procedures could not be resolved on the last delivery). A null
/// snapshot alone is not that marker (a correction, an added line and the size cap store one too, and read "Not yet checked").</para>
/// <para><b>Rights.</b> The resolver runs with the app identity (the injected <see cref="IGenericEntityService"/>), exactly like the evaluation, so a
/// caller who cannot read a distance-1 parent gets neither a false banner nor a fault. The result is one state, never the basis.</para>
/// <para><b>Cost.</b> One resolution per invoice: the resolver's own reads, each once, none per line. Every work date is resolved in memory.</para>
/// </remarks>
public sealed class ProcedureChangeBanner
{
    public const string Changed = "Changed";
    public const string NotChanged = "NotChanged";
    public const string NotYetChecked = "NotYetChecked";
    public const string CouldNotBeChecked = "CouldNotBeChecked";

    /// <summary>The sentence of <see cref="Changed"/> (the owner's text, 2026-10-21).</summary>
    public const string ChangedText = "The procedures that apply to this invoice have changed; re-check to apply them.";

    /// <summary>The sentence of <see cref="NotYetChecked"/>.</summary>
    public const string NotYetCheckedText = "Not yet checked";

    /// <summary>The sentence of <see cref="CouldNotBeChecked"/>.</summary>
    public const string CouldNotBeCheckedText = "Procedure changes could not be checked";

    /// <summary>How long the resolution may take. Past it the banner is <see cref="CouldNotBeChecked"/> and the review is returned without waiting.</summary>
    internal static readonly TimeSpan ResolutionTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] ComparedMembers = ["chain", "overrides", "resolutions"];

    private readonly IGenericEntityService _entities;
    private readonly ILogger<ProcedureChangeBanner> _logger;
    private readonly TimeProvider _clock;

    public ProcedureChangeBanner(IGenericEntityService entities, ILogger<ProcedureChangeBanner> logger, TimeProvider? clock = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// The banner of <paramref name="invoice"/>, or <c>null</c> when it has none (the member is absent). Never throws for a fault of its own.
    /// </summary>
    /// <param name="invoice">The invoice row as the review reads it (firm, core lookups and invoice date).</param>
    /// <param name="lineRows">The invoice's line rows as the review reads them (cost type and work date).</param>
    /// <param name="findingsJson">The own submission's stored <c>sprk_findingsjson</c>, read app-only by the caller; null when there is none.</param>
    /// <param name="stepZero">Step 0's verdict for this request: only <see cref="FinanceStepZeroVerdict.Internal"/> gets a banner.</param>
    /// <param name="open">The invoice is open (In Review).</param>
    /// <exception cref="OperationCanceledException">The REQUEST was cancelled (a timeout of the resolution is a state, not this).</exception>
    public async Task<ProcedureChangeBannerDto?> ComputeAsync(
        Entity invoice, IReadOnlyList<Entity> lineRows, string? findingsJson, FinanceStepZeroVerdict? stepZero, bool open, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(lineRows);

        // Who sees it (uac-r2 N3, C4): only a caller step 0 classed internal. Nothing is read or computed for anyone else.
        if (stepZero is not FinanceStepZeroVerdict.Internal || !open)
        {
            return null;
        }

        // Exempt: an invoice with no firm was judged only by the submission's system finding; no Procedure judged it and none can be resolved.
        if (invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id is not { } firm || firm == Guid.Empty)
        {
            return null;
        }

        if (ParseFindings(findingsJson) is not { } root)
        {
            return Of(NotYetChecked);
        }

        // Exempt: no evaluation ran (the stored findings carry no decision of 073). The submission was held before any rule was judged (it has
        // no firm yet, or its Procedures could not be resolved on the last delivery), so only the system submission finding judged it and no
        // Procedure's version can have changed under it. This is NOT "the snapshot is null": a correction, an added line and the size cap also
        // store a null snapshot, and those invoices WERE evaluated (they keep a decision) and read "Not yet checked".
        if (root["decision"] is null)
        {
            return null;
        }

        var stored = StoredSnapshotOf(root, out var receivedOn);
        if (stored is null)
        {
            return Of(NotYetChecked);
        }

        try
        {
            if (InvoiceLineDecisionService.SingleCoreOf(invoice) is not { } single)
            {
                return CouldNot(invoice.Id, "core", null);
            }

            var dates = WorkDatesOf(invoice, lineRows, receivedOn);
            if (dates.Count == 0)
            {
                return CouldNot(invoice.Id, "dates", null);
            }

            // The token stops a resolver that honours it; WaitAsync holds the limit even for a Dataverse call that ignores it.
            using var timeout = new CancellationTokenSource(ResolutionTimeout, _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                var resolution = await ProcedureResolver.ReadAsync(
                        _entities, _logger, new CoreRecordReference(KindOf(single.Entity), single.Id), firm, linked.Token)
                    .WaitAsync(ResolutionTimeout, _clock, ct).ConfigureAwait(false);
                var current = ProcedureSnapshot.Build(new BillingProcedureBasis(
                    resolution.Chain, dates.Select(resolution.RulesOn).ToList(), []));
                return Of(SameResolution(stored, current) ? NotChanged : Changed);
            }
            catch (TimeoutException)
            {
                return CouldNot(invoice.Id, "timeout", null);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                return CouldNot(invoice.Id, "timeout", null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A 429, a Dataverse fault, a resolver fault (two versions in force during a publish), anything: a visible state, never "no change"
            // and never a failed page. The log names the type only: the message of a resolver or Dataverse fault names rows.
            return CouldNot(invoice.Id, "fault", ex);
        }
    }

    private ProcedureChangeBannerDto CouldNot(Guid invoiceId, string cause, Exception? ex)
    {
        _logger.LogWarning(
            "Invoice {InvoiceId}: procedure changes could not be checked ({Cause}, {FaultType}).", invoiceId, cause, ex?.GetType().Name ?? "none");
        return Of(CouldNotBeChecked);
    }

    private static ProcedureChangeBannerDto Of(string state) => new(state, state switch
    {
        Changed => ChangedText,
        NotYetChecked => NotYetCheckedText,
        CouldNotBeChecked => CouldNotBeCheckedText,
        _ => null,
    });

    /// <summary>The stored findings as an object, or null: no findings, or a damaged or unparseable text.</summary>
    private static JsonObject? ParseFindings(string? findingsJson)
    {
        if (string.IsNullOrWhiteSpace(findingsJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(findingsJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The stored snapshot member of <paramref name="root"/> and the received date the evaluator fell back on, or null: there is no usable
    /// snapshot (no member, a member that is JSON null, a damaged member, an unknown snapshot version).
    /// </summary>
    private static JsonObject? StoredSnapshotOf(JsonObject root, out DateOnly? receivedOn)
    {
        receivedOn = null;
        if (ProcedureSnapshot.Read(root) is null || root[ProcedureSnapshot.Member] is not JsonObject member)
        {
            return null;
        }

        receivedOn = root["receivedOn"] is JsonValue value && value.TryGetValue<string>(out var text)
            && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
        return member;
    }

    /// <summary>
    /// The distinct work dates the evaluation judges, in order (<see cref="BillingJudgedDates"/>, the evaluator's rule): the invoice date (else the
    /// received date, as the evaluator falls back), and each Fee and Expense line's date (else the invoice date). The stored columns, which already
    /// hold any correction. Lines that are not evaluated (an Adjustment, a row with no cost type) contribute nothing.
    /// </summary>
    internal static IReadOnlyList<DateOnly> WorkDatesOf(Entity invoice, IReadOnlyList<Entity> lineRows, DateOnly? receivedOn)
    {
        var invoiceDate = invoice.GetAttributeValue<DateTime?>("sprk_invoicedate") is { } stored ? DateOnly.FromDateTime(stored) : receivedOn;
        if (invoiceDate is not { } fallback)
        {
            return [];
        }

        return BillingJudgedDates.Of(
            fallback,
            lineRows
                .Where(r => BillingJudgedDates.IsEvaluated(r.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value))
                .Select(r => r.GetAttributeValue<DateTime?>(InvoiceSubmissionPersister.LineEventDate) is { } date ? DateOnly.FromDateTime(date) : (DateOnly?)null));
    }

    private static bool SameResolution(JsonObject stored, JsonObject current) =>
        ComparedMembers.All(member => JsonNode.DeepEquals(stored[member], current[member]));

    private static CoreRecordKind KindOf(string entity) => entity switch
    {
        "sprk_matter" => CoreRecordKind.Matter,
        "sprk_project" => CoreRecordKind.Project,
        "sprk_workassignment" => CoreRecordKind.WorkAssignment,
        _ => throw new InvalidOperationException("The invoice's core record is of an unknown table."),
    };
}
