using System.Globalization;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Which core record a rate card is scoped to (FR-08: at most one; none = the whole firm).</summary>
public sealed record RateCardCoreRecord(string EntityLogicalName, Guid RecordId);

/// <summary>What the caller asked to import. Server-derived fields (<see cref="CallerObjectId"/>) never come from the request body.</summary>
public sealed record RateCardImportRequest(
    Guid FirmId, string Name, RateCardCoreRecord? Core, Guid? CallerObjectId);

/// <summary>A successful import: the Draft card and what was written.</summary>
public sealed record RateCardImportResult(
    Guid RateCardId, Guid SourceDocumentId, string Status, int RateCount, int TimekeepersCreated, int TimekeepersReused);

/// <summary>A successful publish: the card is Published and these timekeepers moved from Pending to Approved.</summary>
public sealed record RateCardPublishResult(Guid RateCardId, string Status, int TimekeepersApproved);

public enum RateCardOutcomeKind
{
    /// <summary>The operation completed.</summary>
    Done,

    /// <summary>The input has problems (<see cref="RateCardOutcome{T}.Errors"/>): HTTP 400, nothing written.</summary>
    Invalid,

    /// <summary>Refused for another stable reason (<see cref="RateCardOutcome{T}.ErrorCode"/> with its status).</summary>
    Refused,

    /// <summary>No owner team could be resolved for the new rows (<see cref="RateCardOutcome{T}.OwnerRefusal"/>): HTTP 409, nothing written.</summary>
    OwnerRefused,
}

/// <summary>The result of an import or publish, mapped to HTTP by <c>RateCardImportEndpoints</c>.</summary>
public sealed class RateCardOutcome<T> where T : class
{
    private RateCardOutcome() { }

    public RateCardOutcomeKind Kind { get; private init; }
    public T? Value { get; private init; }
    public IReadOnlyList<RateCardImportError> Errors { get; private init; } = Array.Empty<RateCardImportError>();
    public string? ErrorCode { get; private init; }
    public int StatusCode { get; private init; }
    public string? Detail { get; private init; }
    public RecordOwnerResolution? OwnerRefusal { get; private init; }

    internal static RateCardOutcome<T> Done(T value) => new() { Kind = RateCardOutcomeKind.Done, Value = value };

    internal static RateCardOutcome<T> Invalid(IReadOnlyList<RateCardImportError> errors) =>
        new() { Kind = RateCardOutcomeKind.Invalid, Errors = errors };

    internal static RateCardOutcome<T> Refused(int statusCode, string errorCode, string detail) =>
        new() { Kind = RateCardOutcomeKind.Refused, StatusCode = statusCode, ErrorCode = errorCode, Detail = detail };

    internal static RateCardOutcome<T> OwnerRefused(RecordOwnerResolution resolution) =>
        new() { Kind = RateCardOutcomeKind.OwnerRefused, OwnerRefusal = resolution };
}

/// <summary>
/// Rate card and timekeeper import and publish (ebilling-r1 task 078, FR-29 / FR-08): the template, the all-or-nothing import of a
/// workbook into a Draft <c>sprk_ratecard</c> with its <c>sprk_ratecardrate</c> rows and Pending <c>sprk_timekeeper</c> rows, and the
/// publish that moves a Draft card to Published.
/// </summary>
/// <remarks>
/// <para><b>Server-side owner (ADR-002).</b> Manual edits on the model-driven forms bypass the BFF, so publish re-checks the effective-date
/// overlap rule against the rows as they are now; publish is the one place that sets <c>Published</c>. Nothing is in force before it.</para>
/// <para><b>Source workbook.</b> Stored only through <see cref="SpeFileStore"/> in the Procedure Library container
/// (<c>Finance:ProcedureLibraryContainerId</c>, D-55, internal only) and linked as the card's source document. Its <c>sprk_document</c> row
/// is created UNLINKED: no matter, project or work assignment lookup (DEF-015, owner 2026-10-15). The card itself carries the scope.</para>
/// <para><b>All or nothing.</b> Every row is validated before the first write. The writes are sequential (the shared Dataverse client has
/// no transactional create), so a failure part-way deletes what this call created, in reverse order, and the original failure is rethrown.
/// A crash between a create and its compensation can leave Draft rows; none is in force.</para>
/// <para><b>Ownership.</b> Rows are created by the application, so each is assigned to the owner team resolved through
/// <see cref="IRecordOwnershipResolver"/> (the core record's team, else the caller's business-unit team); an unresolved owner refuses
/// before anything is written.</para>
/// </remarks>
public class RateCardImportService
{
    internal const string RateCardEntity = "sprk_ratecard";
    internal const string RateEntity = "sprk_ratecardrate";
    internal const string TimekeeperEntity = "sprk_timekeeper";
    internal const string DocumentEntity = "sprk_document";
    internal const string FirmEntity = "sprk_organization";

    internal const int CardDraft = 1;
    internal const int CardPublished = 659490000;
    internal const int TimekeeperApproved = 1;
    internal const int TimekeeperPending = 659490000;

    // Stable error codes (ADR-019).
    internal const string CodeInvalid = "ratecard.import_invalid";
    internal const string CodeContainerNotConfigured = "ratecard.container_not_configured";
    internal const string CodeNotFound = "ratecard.not_found";
    internal const string CodeNotDraft = "ratecard.not_draft";
    internal const string CodeOverlap = "ratecard.publish_invalid";
    internal const string CodeImportFailed = "ratecard.import_failed";
    internal const string CodeResultTruncated = "ratecard.result_truncated";

    private const decimal MaxRate = 1_000_000_000m;

    /// <summary>Rows per <see cref="IGenericEntityService.BulkUpdateAsync"/> call: one ExecuteTransaction, capped by Dataverse at 1,000 requests.</summary>
    internal const int MaxApprovalsPerTransaction = 1000;

    // Values per ConditionOperator.In. Dataverse turns each value into a SQL parameter (SQL Server refuses more than ~2,100), and an
    // import may name up to 5,000 timekeepers or contact emails, so every In read runs in batches of this size.
    internal const int MaxInValues = 500;

    private readonly IGenericEntityService _entities;
    private readonly IDocumentDataverseService _documents;
    private readonly SpeFileStore _spe;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IOptionsMonitor<FinanceOptions> _finance;
    private readonly ILogger<RateCardImportService> _logger;

    public RateCardImportService(
        IGenericEntityService entities,
        IDocumentDataverseService documents,
        SpeFileStore spe,
        IRecordOwnershipResolver ownership,
        IOptionsMonitor<FinanceOptions> finance,
        ILogger<RateCardImportService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _finance = finance ?? throw new ArgumentNullException(nameof(finance));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The template workbook (generated from code; the column set lives in <see cref="RateCardWorkbook"/>).</summary>
    public virtual byte[] BuildTemplate() => RateCardWorkbook.BuildTemplate();

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Import
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates <paramref name="workbook"/> and, when every row is valid, stores it in the Procedure Library and creates the Draft card,
    /// its rates and the Pending timekeepers. Any invalid row returns <see cref="RateCardOutcomeKind.Invalid"/> with every problem and
    /// writes nothing (no record, no file).
    /// </summary>
    public virtual async Task<RateCardOutcome<RateCardImportResult>> ImportAsync(
        RateCardImportRequest request, byte[] workbook, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workbook);

        var driveId = _finance.CurrentValue.ProcedureLibraryContainerId;
        if (string.IsNullOrWhiteSpace(driveId))
        {
            return RateCardOutcome<RateCardImportResult>.Refused(
                StatusCodes.Status503ServiceUnavailable, CodeContainerNotConfigured,
                "The Procedure Library container is not configured in this environment, so a rate card cannot be imported.");
        }

        // Reads only from here to the ownership resolution: nothing is written until every row is valid.
        var header = new List<RateCardImportError>();
        if (!await ExistsAsync(FirmEntity, "sprk_organizationid", request.FirmId, ct))
        {
            header.Add(new RateCardImportError(null, null, "firmId", "The firm was not found."));
        }

        if (request.Core is { } core && !await ExistsAsync(core.EntityLogicalName, core.EntityLogicalName + "id", core.RecordId, ct))
        {
            header.Add(new RateCardImportError(null, null, "coreRecord", "The core record was not found."));
        }

        var read = RateCardWorkbook.Read(new MemoryStream(workbook, writable: false));
        if (read.Errors.Count > 0)
        {
            return RateCardOutcome<RateCardImportResult>.Invalid(header.Concat(read.Errors).ToList());
        }

        var (lookups, rosterTruncated) = await LoadLookupsAsync(request.FirmId, read, ct);
        if (rosterTruncated)
        {
            // The roster read does not page; a partial roster would let the import create duplicates of timekeepers it did not see.
            return RateCardOutcome<RateCardImportResult>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated,
                "The firm has more timekeepers than can be matched in one pass, so the import was not run.");
        }

        var validated = Validate(read, lookups);
        var errors = header.Concat(validated.Errors).ToList();
        if (errors.Count > 0)
        {
            return RateCardOutcome<RateCardImportResult>.Invalid(errors);
        }

        var parents = request.Core is { } c
            ? new[] { new RecordOwnershipParent(c.EntityLogicalName, c.RecordId) }
            : Array.Empty<RecordOwnershipParent>();
        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(parents, request.CallerObjectId) with
            {
                RequestedBy = RecordRequester.Of(objectId: request.CallerObjectId),
            },
            ct);
        if (!owner.IsOwned)
        {
            return RateCardOutcome<RateCardImportResult>.OwnerRefused(owner);
        }

        return await WriteAsync(request, workbook, driveId, validated, lookups, owner, ct);
    }

    private async Task<RateCardOutcome<RateCardImportResult>> WriteAsync(
        RateCardImportRequest request, byte[] workbook, string driveId, ValidatedWorkbook validated,
        ImportLookups lookups, RecordOwnerResolution owner, CancellationToken ct)
    {
        var created = new List<(string Entity, Guid Id)>();
        string? uploadedItemId = null;

        try
        {
            // 1. The workbook, in the internal-only container, then its unlinked document row.
            var uploadPath = SpeUploadPath.SanitizeFileName(SourceFileName(request.Name));
            var file = await _spe.UploadSmallAsync(driveId, uploadPath, new MemoryStream(workbook, writable: false), ConflictBehavior.Rename, ct)
                ?? throw new InvalidOperationException("The Procedure Library upload returned no file.");
            uploadedItemId = file.Id;

            var documentId = Guid.Parse(await _documents.CreateDocumentAsync(
                new CreateDocumentRequest
                {
                    Name = file.Name,
                    ContainerId = driveId,
                    OwningTeamId = owner.OwningTeamId,
                    CreatedByPersonId = owner.CreatedByPerson,
                },
                ct));
            created.Add((DocumentEntity, documentId));

            await _documents.UpdateDocumentAsync(
                documentId.ToString(),
                new UpdateDocumentRequest
                {
                    GraphDriveId = driveId,
                    GraphItemId = file.Id,
                    FileName = file.Name,
                    FileSize = file.Size ?? workbook.LongLength,
                    MimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    HasFile = true,
                    FilePath = file.WebUrl,
                },
                ct);

            // 2. Timekeepers (Pending). A name already on the firm's roster is reused, not duplicated.
            var timekeeperIds = new Dictionary<string, Guid>(lookups.Roster, StringComparer.Ordinal);
            var createdTimekeepers = 0;
            foreach (var tk in validated.Timekeepers)
            {
                if (tk.ExistingId is { } existing)
                {
                    timekeeperIds[NormalizeName(tk.Name)] = existing;
                    continue;
                }

                var row = new Entity(TimekeeperEntity)
                {
                    ["sprk_name"] = tk.Name,
                    ["sprk_firm"] = new EntityReference(FirmEntity, request.FirmId),
                    ["sprk_classification"] = new OptionSetValue(tk.Classification),
                    ["sprk_effectivefrom"] = ToDate(tk.From),
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(TimekeeperPending),
                };
                if (tk.To is { } to)
                {
                    row["sprk_effectiveto"] = ToDate(to);
                }

                if (tk.ContactId is { } contactId)
                {
                    row["sprk_contact"] = new EntityReference("contact", contactId);
                }

                owner.ApplyTo(row);
                var id = await _entities.CreateAsync(row, ct);
                created.Add((TimekeeperEntity, id));
                timekeeperIds[NormalizeName(tk.Name)] = id;
                createdTimekeepers++;
            }

            // 3. The Draft card, then its rates (children: deleting the card deletes them).
            var card = new Entity(RateCardEntity)
            {
                ["sprk_name"] = request.Name,
                ["sprk_firm"] = new EntityReference(FirmEntity, request.FirmId),
                ["sprk_sourcedocument"] = new EntityReference(DocumentEntity, documentId),
                ["sprk_effectivefrom"] = ToDate(validated.Rates.Min(r => r.From)),
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(CardDraft),
            };
            if (validated.Rates.All(r => r.To is not null))
            {
                card["sprk_effectiveto"] = ToDate(validated.Rates.Max(r => r.To!.Value));
            }

            if (request.Core is { } core)
            {
                card[core.EntityLogicalName] = new EntityReference(core.EntityLogicalName, core.RecordId);
            }

            owner.ApplyTo(card);
            var cardId = await _entities.CreateAsync(card, ct);
            created.Add((RateCardEntity, cardId));

            foreach (var rate in validated.Rates)
            {
                var row = new Entity(RateEntity)
                {
                    ["sprk_name"] = rate.Name,
                    ["sprk_ratecard"] = new EntityReference(RateCardEntity, cardId),
                    ["sprk_approvedrate"] = new Money(rate.Approved),
                    ["sprk_rackrate"] = new Money(rate.Rack),
                    ["transactioncurrencyid"] = new EntityReference("transactioncurrency", rate.CurrencyId),
                    ["sprk_effectivefrom"] = ToDate(rate.From),
                };
                if (rate.To is { } to)
                {
                    row["sprk_effectiveto"] = ToDate(to);
                }

                if (rate.TimekeeperName is { } tkName)
                {
                    row["sprk_timekeeper"] = new EntityReference(TimekeeperEntity, timekeeperIds[NormalizeName(tkName)]);
                }
                else
                {
                    row["sprk_classification"] = new OptionSetValue(rate.Classification!.Value);
                }

                owner.ApplyTo(row);
                created.Add((RateEntity, await _entities.CreateAsync(row, ct)));
            }

            _logger.LogInformation(
                "Rate card {RateCardId} imported as Draft: {Rates} rates, {Created} timekeepers created, {Reused} reused",
                cardId, validated.Rates.Count, createdTimekeepers, validated.Timekeepers.Count - createdTimekeepers);

            return RateCardOutcome<RateCardImportResult>.Done(new RateCardImportResult(
                cardId, documentId, "Draft", validated.Rates.Count, createdTimekeepers, validated.Timekeepers.Count - createdTimekeepers));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client went away mid-import: the half-written rows and the uploaded file must not outlive the request.
            // (A Graph or HTTP timeout surfaces as an OperationCanceledException too, with the token NOT cancelled: it falls to the
            // catch below, which also cleans up and answers ratecard.import_failed.)
            _logger.LogWarning("Rate card import cancelled after {Written} writes; removing what it created", created.Count);
            await CompensateAsync(created, driveId, uploadedItemId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rate card import failed after {Written} writes; removing what it created", created.Count);
            await CompensateAsync(created, driveId, uploadedItemId);
            return RateCardOutcome<RateCardImportResult>.Refused(
                StatusCodes.Status500InternalServerError, CodeImportFailed,
                "The rate card could not be imported and nothing was saved. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>Best effort, reverse order, own token: a failed compensation is logged and never masks the original failure.</summary>
    private async Task CompensateAsync(List<(string Entity, Guid Id)> created, string driveId, string? itemId)
    {
        for (var i = created.Count - 1; i >= 0; i--)
        {
            try
            {
                await _entities.DeleteAsync(created[i].Entity, created[i].Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rate card import cleanup could not delete {Entity} {Id}; it is a Draft row and in force nowhere",
                    created[i].Entity, created[i].Id);
            }
        }

        if (itemId is not null)
        {
            try
            {
                await _spe.DeleteFileAsync(driveId, itemId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rate card import cleanup could not delete the uploaded workbook {ItemId}", itemId);
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Publish
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draft to Published. Re-checks the overlap rule on the card's rates as they are NOW (a form edit after import can break it), approves
    /// the card firm's Pending timekeepers the rates name, then sets the card Published last, so a retry after a partial failure completes.
    /// </summary>
    public virtual async Task<RateCardOutcome<RateCardPublishResult>> PublishAsync(Guid rateCardId, CancellationToken ct)
    {
        var cards = await _entities.RetrieveMultipleAsync(new QueryExpression(RateCardEntity)
        {
            ColumnSet = new ColumnSet("statuscode", "sprk_firm"),
            Criteria = { Conditions = { new ConditionExpression("sprk_ratecardid", ConditionOperator.Equal, rateCardId) } },
            TopCount = 1,
        }, ct);

        var card = cards.Entities.FirstOrDefault();
        if (card is null)
        {
            return RateCardOutcome<RateCardPublishResult>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rate card was not found.");
        }

        if (card.GetAttributeValue<OptionSetValue>("statuscode")?.Value != CardDraft)
        {
            return RateCardOutcome<RateCardPublishResult>.Refused(
                StatusCodes.Status409Conflict, CodeNotDraft, "Only a Draft rate card can be published.");
        }

        var rateRows = await _entities.RetrieveMultipleAsync(new QueryExpression(RateEntity)
        {
            ColumnSet = new ColumnSet("sprk_name", "sprk_timekeeper", "sprk_classification", "sprk_effectivefrom", "sprk_effectiveto"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("sprk_ratecard", ConditionOperator.Equal, rateCardId),
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0),
                },
            },
        }, ct);

        if (rateRows.MoreRecords)
        {
            // IGenericEntityService.RetrieveMultipleAsync does not page: past 5,000 rows the overlap check would silently skip rates.
            return RateCardOutcome<RateCardPublishResult>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated,
                "The rate card has more rates than can be checked in one pass; split it into smaller cards.");
        }

        var stored = rateRows.Entities.Select(StoredRate.Of).ToList();
        var errors = FindStoredRateProblems(stored);
        if (errors.Count > 0)
        {
            return RateCardOutcome<RateCardPublishResult>.Invalid(errors);
        }

        var firmId = card.GetAttributeValue<EntityReference>("sprk_firm")?.Id;
        var timekeeperIds = stored.Where(r => r.TimekeeperId is not null).Select(r => r.TimekeeperId!.Value).Distinct().Cast<object>().ToArray();
        var approvals = new List<(Guid id, Dictionary<string, object> fields)>();
        if (timekeeperIds.Length > 0 && firmId is not null)
        {
            var timekeepers = await RetrieveInBatchesAsync(timekeeperIds, batch => new QueryExpression(TimekeeperEntity)
            {
                ColumnSet = new ColumnSet("statuscode", "sprk_firm"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("sprk_timekeeperid", ConditionOperator.In, batch),
                        new ConditionExpression("sprk_firm", ConditionOperator.Equal, firmId.Value),
                    },
                },
            }, ct);

            approvals = timekeepers
                .Where(t => t.GetAttributeValue<OptionSetValue>("statuscode")?.Value == TimekeeperPending)
                .Select(t => (t.Id, new Dictionary<string, object>
                {
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(TimekeeperApproved),
                }))
                .ToList();
        }

        // BulkUpdateAsync is ONE ExecuteTransaction, which Dataverse caps at 1,000 requests, and an import may name up to 5,000 new
        // timekeepers. Each chunk is atomic; the whole is not. That is safe to retry: approving an Approved row changes nothing, and the
        // card is set Published only after the last chunk, so a failure part-way leaves the card Draft and a second publish finishes.
        foreach (var chunk in approvals.Chunk(MaxApprovalsPerTransaction))
        {
            await _entities.BulkUpdateAsync(TimekeeperEntity, chunk.ToList(), ct);
        }

        await _entities.UpdateAsync(RateCardEntity, rateCardId, new Dictionary<string, object>
        {
            ["statecode"] = new OptionSetValue(0),
            ["statuscode"] = new OptionSetValue(CardPublished),
        }, ct);

        _logger.LogInformation("Rate card {RateCardId} published; {Approved} timekeepers approved", rateCardId, approvals.Count);
        return RateCardOutcome<RateCardPublishResult>.Done(new RateCardPublishResult(rateCardId, "Published", approvals.Count));
    }

    /// <summary>A stored rate row, as read at publish.</summary>
    internal sealed record StoredRate(Guid Id, string Name, Guid? TimekeeperId, int? Classification, DateTime? From, DateTime? To)
    {
        internal static StoredRate Of(Entity e) => new(
            e.Id,
            e.GetAttributeValue<string>("sprk_name") ?? e.Id.ToString(),
            e.GetAttributeValue<EntityReference>("sprk_timekeeper")?.Id,
            e.GetAttributeValue<OptionSetValue>("sprk_classification")?.Value,
            e.GetAttributeValue<DateTime?>("sprk_effectivefrom"),
            e.GetAttributeValue<DateTime?>("sprk_effectiveto"));
    }

    /// <summary>
    /// The publish-time rules over the rows as stored: every rate names a timekeeper or a classification and has an Effective From, a
    /// range does not end before it starts, and no two ranges for the same timekeeper (or, for a rate by classification, the same
    /// classification) overlap.
    /// </summary>
    internal static IReadOnlyList<RateCardImportError> FindStoredRateProblems(IReadOnlyList<StoredRate> rates)
    {
        var errors = new List<RateCardImportError>();
        var usable = new List<(StoredRate Rate, string Key, DateOnly From, DateOnly? To)>();

        foreach (var rate in rates)
        {
            var label = $"Rate '{rate.Name}'";
            if (rate.TimekeeperId is null && rate.Classification is null)
            {
                errors.Add(new RateCardImportError(RateCardWorkbook.RatesSheet, null, "Timekeeper", $"{label} has neither a timekeeper nor a classification."));
                continue;
            }

            if (rate.From is not { } from)
            {
                errors.Add(new RateCardImportError(RateCardWorkbook.RatesSheet, null, RateCardWorkbook.RateEffectiveFrom, $"{label} has no Effective From."));
                continue;
            }

            var fromDate = DateOnly.FromDateTime(from);
            DateOnly? toDate = rate.To is { } to ? DateOnly.FromDateTime(to) : null;
            if (toDate is { } t && t < fromDate)
            {
                errors.Add(new RateCardImportError(RateCardWorkbook.RatesSheet, null, RateCardWorkbook.RateEffectiveTo, $"{label} ends before it starts."));
                continue;
            }

            var key = rate.TimekeeperId is { } tk ? "tk:" + tk : "cls:" + rate.Classification;
            usable.Add((rate, key, fromDate, toDate));
        }

        foreach (var group in usable.GroupBy(u => u.Key))
        {
            var ordered = group.OrderBy(u => u.From).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count; j++)
                {
                    if (Overlaps(ordered[i].From, ordered[i].To, ordered[j].From, ordered[j].To))
                    {
                        errors.Add(new RateCardImportError(
                            RateCardWorkbook.RatesSheet, null, RateCardWorkbook.RateEffectiveFrom,
                            $"Rate '{ordered[j].Rate.Name}' overlaps rate '{ordered[i].Rate.Name}' for the same " +
                            $"{(ordered[j].Rate.TimekeeperId is not null ? "timekeeper" : "classification")}."));
                    }
                }
            }
        }

        return errors;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Validation (pure)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The reference data the cell checks need, read once per import.</summary>
    /// <summary>A roster row as stored, with the values a sheet row is compared with before it may reuse the row.</summary>
    internal sealed record StoredTimekeeper(Guid Id, int? Classification, DateOnly? From, DateOnly? To, Guid? ContactId);

    internal sealed record ImportLookups(
        IReadOnlyDictionary<string, Guid> Roster,
        IReadOnlySet<string> AmbiguousRoster,
        IReadOnlyDictionary<string, IReadOnlyList<StoredTimekeeper>> RosterRows,
        IReadOnlyDictionary<string, Guid> ContactsByEmail,
        IReadOnlySet<string> AmbiguousEmails,
        IReadOnlyDictionary<string, Guid> CurrenciesByIso);

    internal sealed record ParsedTimekeeper(int Row, string Name, int Classification, DateOnly From, DateOnly? To, Guid? ContactId, Guid? ExistingId);

    internal sealed record ParsedRate(
        int Row, string Name, string Key, string? TimekeeperName, int? Classification, decimal Approved, decimal Rack,
        Guid CurrencyId, DateOnly From, DateOnly? To);

    internal sealed record ValidatedWorkbook(
        IReadOnlyList<ParsedTimekeeper> Timekeepers, IReadOnlyList<ParsedRate> Rates, IReadOnlyList<RateCardImportError> Errors);

    internal static ValidatedWorkbook Validate(WorkbookRead read, ImportLookups lookups)
    {
        var errors = new List<RateCardImportError>();
        var timekeepers = new List<ParsedTimekeeper>();
        var rates = new List<ParsedRate>();

        if (read.TimekeeperRows.Count == 0 && read.RateRows.Count == 0)
        {
            errors.Add(new RateCardImportError(null, null, null, "The workbook has no timekeeper or rate rows."));
            return new ValidatedWorkbook(timekeepers, rates, errors);
        }

        if (read.RateRows.Count == 0)
        {
            errors.Add(new RateCardImportError(RateCardWorkbook.RatesSheet, null, null, "The Rates sheet has no rows; a rate card needs at least one rate."));
        }

        // Timekeepers sheet.
        var sheetNames = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in read.TimekeeperRows)
        {
            var ok = true;
            void Fail(string column, string message)
            {
                errors.Add(new RateCardImportError(row.Sheet, row.Row, column, message));
                ok = false;
            }

            var name = row.Get(RateCardWorkbook.TkName);
            var normalized = string.Empty;
            if (name is null)
            {
                Fail(RateCardWorkbook.TkName, "Name is required.");
            }
            else if (name.Length > 850)
            {
                Fail(RateCardWorkbook.TkName, "Name is longer than 850 characters.");
            }
            else
            {
                normalized = NormalizeName(name);
                if (sheetNames.TryGetValue(normalized, out var firstRow))
                {
                    Fail(RateCardWorkbook.TkName, $"'{name}' is already listed in row {firstRow}.");
                }
                else
                {
                    sheetNames[normalized] = row.Row;
                }
            }

            var classification = 0;
            if (!TimekeeperClassifications.TryParse(row.Get(RateCardWorkbook.TkClassification), out classification))
            {
                Fail(RateCardWorkbook.TkClassification, row.Get(RateCardWorkbook.TkClassification) is null
                    ? "Classification is required."
                    : $"'{row.Get(RateCardWorkbook.TkClassification)}' is not a known classification.");
            }

            var (from, to) = ReadRange(row, RateCardWorkbook.TkEffectiveFrom, RateCardWorkbook.TkEffectiveTo, Fail);

            Guid? contactId = null;
            var email = row.Get(RateCardWorkbook.TkContactEmail);
            if (email is not null)
            {
                var key = email.ToLowerInvariant();
                if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
                {
                    Fail(RateCardWorkbook.TkContactEmail, $"'{email}' is not a valid email address.");
                }
                else if (lookups.AmbiguousEmails.Contains(key))
                {
                    Fail(RateCardWorkbook.TkContactEmail, $"More than one contact has the email address '{email}'.");
                }
                else if (!lookups.ContactsByEmail.TryGetValue(key, out var found))
                {
                    Fail(RateCardWorkbook.TkContactEmail, $"No contact has the email address '{email}'.");
                }
                else
                {
                    contactId = found;
                }
            }

            // A roster row is reused only when the sheet row says the same thing. A sheet period that overlaps no stored row of that name
            // (the annual renewal, a promotion) is a NEW Pending row; an overlapping period with different values is refused, because
            // reusing the row by name would silently discard the sheet's dates, classification or contact.
            Guid? existing = null;
            if (ok && lookups.RosterRows.TryGetValue(normalized, out var stored))
            {
                var overlapping = stored.Where(s => Overlaps(s.From ?? DateOnly.MinValue, s.To, from!.Value, to)).ToList();
                if (overlapping.Count > 1)
                {
                    Fail(RateCardWorkbook.TkName, $"More than one timekeeper named '{name}' is on the firm's roster for these dates; resolve that first.");
                }
                else if (overlapping.Count == 1)
                {
                    var difference = DescribeDifference(name!, overlapping[0], classification, from!.Value, to, contactId, email);
                    if (difference is { } d)
                    {
                        Fail(d.Column, d.Message);
                    }
                    else
                    {
                        existing = overlapping[0].Id;
                    }
                }
            }

            if (ok)
            {
                timekeepers.Add(new ParsedTimekeeper(row.Row, name!, classification, from!.Value, to, contactId, existing));
            }
        }

        // Rates sheet.
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in read.RateRows)
        {
            var ok = true;
            void Fail(string column, string message)
            {
                errors.Add(new RateCardImportError(row.Sheet, row.Row, column, message));
                ok = false;
            }

            var tkName = row.Get(RateCardWorkbook.RateTimekeeper);
            var clsText = row.Get(RateCardWorkbook.RateClassification);
            string? timekeeper = null;
            int? classification = null;
            string key = string.Empty;

            if (tkName is null && clsText is null)
            {
                Fail(RateCardWorkbook.RateTimekeeper, "Fill either Timekeeper or Classification.");
            }
            else if (tkName is not null && clsText is not null)
            {
                Fail(RateCardWorkbook.RateClassification, "Fill either Timekeeper or Classification, not both.");
            }
            else if (tkName is not null)
            {
                var normalized = NormalizeName(tkName);
                if (sheetNames.ContainsKey(normalized) || lookups.Roster.ContainsKey(normalized))
                {
                    timekeeper = tkName;
                    key = "tk:" + normalized;
                    referenced.Add(normalized);
                }
                else if (lookups.AmbiguousRoster.Contains(normalized))
                {
                    Fail(RateCardWorkbook.RateTimekeeper, $"More than one timekeeper named '{tkName}' is on the firm's roster.");
                }
                else
                {
                    Fail(RateCardWorkbook.RateTimekeeper, $"'{tkName}' is not on the Timekeepers sheet or the firm's roster.");
                }
            }
            else if (TimekeeperClassifications.TryParse(clsText, out var cls))
            {
                classification = cls;
                key = "cls:" + cls;
            }
            else
            {
                Fail(RateCardWorkbook.RateClassification, $"'{clsText}' is not a known classification.");
            }

            var approved = ReadMoney(row, RateCardWorkbook.RateApproved, Fail);
            var rack = ReadMoney(row, RateCardWorkbook.RateRack, Fail);
            if (approved is { } a && rack is { } r && a > r)
            {
                Fail(RateCardWorkbook.RateApproved, "Approved Rate may not be higher than Rack Rate.");
            }

            Guid currencyId = Guid.Empty;
            var currency = row.Get(RateCardWorkbook.RateCurrency);
            if (currency is null)
            {
                Fail(RateCardWorkbook.RateCurrency, "Currency is required.");
            }
            else if (!lookups.CurrenciesByIso.TryGetValue(currency.ToUpperInvariant(), out currencyId))
            {
                Fail(RateCardWorkbook.RateCurrency, $"'{currency}' is not a known currency code.");
            }

            var (from, to) = ReadRange(row, RateCardWorkbook.RateEffectiveFrom, RateCardWorkbook.RateEffectiveTo, Fail);

            if (ok)
            {
                var label = timekeeper ?? TimekeeperClassifications.LabelOf(classification!.Value);
                var name = $"{label} {from!.Value:yyyy-MM-dd}";
                rates.Add(new ParsedRate(
                    row.Row, name.Length > 200 ? name[..200] : name, key, timekeeper, classification, approved!.Value, rack!.Value,
                    currencyId, from.Value, to));
            }
        }

        // Overlap: same timekeeper, or (rates by classification) same classification.
        foreach (var group in rates.GroupBy(r => r.Key))
        {
            var ordered = group.OrderBy(r => r.From).ThenBy(r => r.Row).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count; j++)
                {
                    if (Overlaps(ordered[i].From, ordered[i].To, ordered[j].From, ordered[j].To))
                    {
                        var (later, earlier) = ordered[i].Row > ordered[j].Row ? (ordered[i], ordered[j]) : (ordered[j], ordered[i]);
                        errors.Add(new RateCardImportError(
                            RateCardWorkbook.RatesSheet, later.Row, RateCardWorkbook.RateEffectiveFrom,
                            $"The effective dates overlap row {earlier.Row} for the same " +
                            $"{(later.TimekeeperName is not null ? "timekeeper" : "classification")}."));
                    }
                }
            }
        }

        // Every NEW timekeeper needs a rate: publish approves the timekeepers a card's rates name, and nothing else links a timekeeper to its card.
        foreach (var tk in timekeepers.Where(t => t.ExistingId is null && !referenced.Contains(NormalizeName(t.Name))))
        {
            errors.Add(new RateCardImportError(
                RateCardWorkbook.TimekeepersSheet, tk.Row, RateCardWorkbook.TkName,
                $"'{tk.Name}' has no row on the Rates sheet; add a rate for this timekeeper."));
        }

        return new ValidatedWorkbook(timekeepers, rates, errors
            .OrderBy(e => e.Sheet is null ? 0 : e.Sheet == RateCardWorkbook.TimekeepersSheet ? 1 : 2)
            .ThenBy(e => e.Row ?? 0)
            .ToList());
    }

    /// <summary>
    /// What the sheet row says differently from the stored roster row it overlaps, or <c>null</c> when they agree. A blank contact cell
    /// says nothing about the contact, so it never differs; a blank Effective To is "open-ended" and does.
    /// </summary>
    private static (string Column, string Message)? DescribeDifference(
        string name, StoredTimekeeper stored, int classification, DateOnly from, DateOnly? to, Guid? contactId, string? email)
    {
        var onRoster = new List<string>();
        var onSheet = new List<string>();
        string? column = null;

        if (stored.From != from)
        {
            onRoster.Add($"Effective From {FormatDate(stored.From, "(none)")}");
            onSheet.Add($"Effective From {FormatDate(from, "(none)")}");
            column ??= RateCardWorkbook.TkEffectiveFrom;
        }

        if (stored.To != to)
        {
            onRoster.Add($"Effective To {FormatDate(stored.To, "(open-ended)")}");
            onSheet.Add($"Effective To {FormatDate(to, "(open-ended)")}");
            column ??= RateCardWorkbook.TkEffectiveTo;
        }

        if (stored.Classification != classification)
        {
            onRoster.Add($"classification {(stored.Classification is { } c ? TimekeeperClassifications.LabelOf(c) : "(none)")}");
            onSheet.Add($"classification {TimekeeperClassifications.LabelOf(classification)}");
            column ??= RateCardWorkbook.TkClassification;
        }

        if (contactId is { } sheetContact && stored.ContactId != sheetContact)
        {
            onRoster.Add(stored.ContactId is null ? "no contact" : "a different contact");
            onSheet.Add($"contact {email}");
            column ??= RateCardWorkbook.TkContactEmail;
        }

        return column is null
            ? null
            : (column, $"{name} is on the roster with {string.Join(" and ", onRoster)}; this sheet says {string.Join(" and ", onSheet)}. " +
                "Correct the sheet or the roster row, or start the sheet row after the roster row ends.");
    }

    private static string FormatDate(DateOnly? date, string whenEmpty) =>
        date is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : whenEmpty;

    private static (DateOnly? From, DateOnly? To) ReadRange(
        WorkbookRow row, string fromColumn, string toColumn, Action<string, string> fail)
    {
        DateOnly? from = null;
        DateOnly? to = null;
        var fromText = row.Get(fromColumn);
        if (fromText is null)
        {
            fail(fromColumn, $"{fromColumn} is required.");
        }
        else if (TryParseDate(fromText, out var f))
        {
            from = f;
        }
        else
        {
            fail(fromColumn, $"'{fromText}' is not a date; use yyyy-mm-dd.");
        }

        var toText = row.Get(toColumn);
        if (toText is not null)
        {
            if (TryParseDate(toText, out var t))
            {
                to = t;
                if (from is { } fromValue && t < fromValue)
                {
                    fail(toColumn, $"{toColumn} may not be before {fromColumn}.");
                    to = null;
                }
            }
            else
            {
                fail(toColumn, $"'{toText}' is not a date; use yyyy-mm-dd.");
            }
        }

        return (from, to);
    }

    private static decimal? ReadMoney(WorkbookRow row, string column, Action<string, string> fail)
    {
        var text = row.Get(column);
        if (text is null)
        {
            fail(column, $"{column} is required.");
            return null;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            fail(column, $"'{text}' is not a number.");
            return null;
        }

        if (value < 0 || value > MaxRate)
        {
            fail(column, $"{column} must be between 0 and {MaxRate:N0}.");
            return null;
        }

        if (decimal.Round(value, 2) != value)
        {
            fail(column, $"{column} may have at most 2 decimal places.");
            return null;
        }

        return value;
    }

    internal static bool TryParseDate(string text, out DateOnly date)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        // An Excel date cell is stored as a serial number of days (a time of day is the fraction, dropped here).
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial >= 1 && serial < 2958466)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serial)));
            return true;
        }

        date = default;
        return false;
    }

    /// <summary>Ranges are inclusive; an empty end is open-ended.</summary>
    internal static bool Overlaps(DateOnly aFrom, DateOnly? aTo, DateOnly bFrom, DateOnly? bTo) =>
        aFrom <= (bTo ?? DateOnly.MaxValue) && bFrom <= (aTo ?? DateOnly.MaxValue);

    internal static string NormalizeName(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static DateTime ToDate(DateOnly d) => new(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);

    private static string SourceFileName(string cardName)
    {
        var safe = new string(cardName.Select(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' ? ch : '_').ToArray()).Trim();
        if (safe.Length > 80)
        {
            safe = safe[..80].TrimEnd();
        }

        return $"Rate card - {(safe.Length == 0 ? "import" : safe)} - {DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reads
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<bool> ExistsAsync(string entity, string idColumn, Guid id, CancellationToken ct)
    {
        var result = await _entities.RetrieveMultipleAsync(new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(idColumn),
            Criteria = { Conditions = { new ConditionExpression(idColumn, ConditionOperator.Equal, id) } },
            TopCount = 1,
        }, ct);
        return result.Entities.Count > 0;
    }

    private static StoredTimekeeper StoredOf(Entity e) => new(
        e.Id,
        e.GetAttributeValue<OptionSetValue>("sprk_classification")?.Value,
        e.GetAttributeValue<DateTime?>("sprk_effectivefrom") is { } from ? DateOnly.FromDateTime(from) : null,
        e.GetAttributeValue<DateTime?>("sprk_effectiveto") is { } to ? DateOnly.FromDateTime(to) : null,
        e.GetAttributeValue<EntityReference>("sprk_contact")?.Id);

    private async Task<List<Entity>> RetrieveInBatchesAsync(object[] values, Func<object[], QueryExpression> query, CancellationToken ct)
    {
        var all = new List<Entity>();
        foreach (var batch in values.Chunk(MaxInValues))
        {
            all.AddRange((await _entities.RetrieveMultipleAsync(query(batch), ct)).Entities);
        }

        return all;
    }

    private async Task<(ImportLookups Lookups, bool RosterTruncated)> LoadLookupsAsync(Guid firmId, WorkbookRead read, CancellationToken ct)
    {
        var roster = await _entities.RetrieveMultipleAsync(new QueryExpression(TimekeeperEntity)
        {
            ColumnSet = new ColumnSet("sprk_name", "sprk_classification", "sprk_effectivefrom", "sprk_effectiveto", "sprk_contact"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("sprk_firm", ConditionOperator.Equal, firmId),
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0),
                },
            },
        }, ct);

        var byName = roster.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.GetAttributeValue<string>("sprk_name")))
            .GroupBy(e => NormalizeName(e.GetAttributeValue<string>("sprk_name")))
            .ToList();

        var emails = read.TimekeeperRows
            .Select(r => r.Get(RateCardWorkbook.TkContactEmail))
            .Where(e => e is not null)
            .Select(e => e!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var contactsByEmail = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var ambiguousEmails = new HashSet<string>(StringComparer.Ordinal);
        if (emails.Length > 0)
        {
            var contacts = await RetrieveInBatchesAsync(emails.Cast<object>().ToArray(), batch => new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("emailaddress1"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("emailaddress1", ConditionOperator.In, batch),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0),
                    },
                },
            }, ct);

            foreach (var group in contacts.GroupBy(e => e.GetAttributeValue<string>("emailaddress1").ToLowerInvariant()))
            {
                if (group.Count() == 1)
                {
                    contactsByEmail[group.Key] = group.First().Id;
                }
                else
                {
                    ambiguousEmails.Add(group.Key);
                }
            }
        }

        var currencies = await _entities.RetrieveMultipleAsync(new QueryExpression("transactioncurrency")
        {
            ColumnSet = new ColumnSet("isocurrencycode"),
            Criteria = { Conditions = { new ConditionExpression("statecode", ConditionOperator.Equal, 0) } },
        }, ct);

        var lookups = new ImportLookups(
            byName.Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().Id),
            byName.Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal),
            byName.ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<StoredTimekeeper>)g.Select(StoredOf).ToList()),
            contactsByEmail,
            ambiguousEmails,
            currencies.Entities
                .Where(e => !string.IsNullOrWhiteSpace(e.GetAttributeValue<string>("isocurrencycode")))
                .GroupBy(e => e.GetAttributeValue<string>("isocurrencycode").ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First().Id));
        return (lookups, roster.MoreRecords);
    }
}
