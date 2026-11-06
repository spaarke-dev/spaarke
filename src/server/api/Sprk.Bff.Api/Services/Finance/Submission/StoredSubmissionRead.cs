using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// Ebilling-r1 task 095d-2a-1 (EB-D-186, EB-D-189 option A): the invoice as the analyzer read it, rebuilt from what intake stored, so the
/// re-run after Set firm evaluates the submission with its firm (<see cref="InvoiceSubmissionProcessor.EvaluateFromReadAsync"/>) without
/// reading the document again. Also the line coding intake stored (<see cref="StoredCoding"/>), which <see cref="RerunLineCoding"/> keeps
/// for every line whose code set does not change.
/// </summary>
/// <remarks>
/// <para><b>Sources.</b> The header: the values and confidences as READ (<c>provenance.header</c>, EB-D-170) and the section totals
/// (<c>provenance.sectionTotals</c>). The timekeeper summary names (<c>provenance.timekeeperSummary</c>). Each fee and expense line: its
/// columns as <see cref="InvoiceSubmissionPersister.BuildLines"/> wrote them (a value correction is in the column and feeds the re-run, Q4),
/// its <c>sprk_extractionconfidence</c> (confidence, page, box, repair and the <c>dateMissing</c> flag), and its coding block with the
/// observation quotes from <c>provenance.observations</c>.</para>
/// <para><b>Fail closed.</b> A submission stored before task 095d-2a-1 has none of the re-run facts, and a fact that cannot be read is not
/// guessed: the result is <see cref="Result.NotRerunnable"/> with a stable reason, logged with <c>Permanent=true</c> (an operator matter, not
/// a retry). Nothing is written here, so the hold stays.</para>
/// <para><b>Not rebuilt</b> (no step 1c-5 reads them): the firm name, client and matter names, the firm's matter reference, the PO as quoted,
/// a fee line's title value, the page count and the row boxes. Text columns hold at most 100 characters (the persister's cap).</para>
/// <para><b>Logs (ADR-015).</b> Ids, counts and reason codes only; never a name, a quote or a value.</para>
/// </remarks>
internal static class StoredSubmissionRead
{
    /// <summary>The reason a submission cannot be re-run: it was stored before intake kept the re-run facts (EB-D-189).</summary>
    internal const string FactsMissing = "ebilling.rerun.facts_missing";

    /// <summary>The reason a submission cannot be re-run: its stored findings, lines or coding cannot be read back consistently.</summary>
    internal const string StoredRowsInconsistent = "ebilling.rerun.stored_rows_inconsistent";

    /// <summary>The line columns the rebuild reads.</summary>
    internal static readonly string[] LineColumns =
    [
        InvoiceSubmissionPersister.LineSequence, InvoiceTotalsService.LineCostType, InvoiceSubmissionPersister.LineEventDate,
        InvoiceSubmissionPersister.LineTimekeeper, InvoiceSubmissionPersister.LineQuantity, InvoiceSubmissionPersister.LineRate,
        InvoiceTotalsService.LineAmount, InvoiceSubmissionPersister.LineDescription, InvoiceSubmissionPersister.LineTaskCodeAsBilled,
        InvoiceSubmissionPersister.LineActivityCodeAsBilled, InvoiceSubmissionPersister.LineExpenseCodeAsBilled,
        InvoiceSubmissionPersister.LineExtractionConfidence,
    ];

    /// <summary>The rebuild's answer: the read and the stored coding, or the reason it is refused.</summary>
    internal sealed record Result(ExtractedInvoice? Read, StoredCoding? Coding, string? RefusalReason)
    {
        /// <summary>True when the read and the coding were rebuilt.</summary>
        internal bool IsRerunnable => RefusalReason is null;

        internal static Result Rerunnable(ExtractedInvoice read, StoredCoding coding) => new(read, coding, null);

        internal static Result NotRerunnable(string reason) => new(null, null, reason);
    }

    /// <summary>
    /// Rebuilds the read and the stored coding of <paramref name="submissionId"/> from its findings JSON and its invoice's lines (any order;
    /// Adjustment and other non fee/expense lines are skipped, as the evaluation skips them). A refusal is logged with <c>Permanent=true</c>.
    /// </summary>
    internal static Result Rebuild(Guid submissionId, string? findingsJson, IEnumerable<Entity> lines, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(logger);

        var result = RebuildCore(findingsJson, lines);
        if (result.RefusalReason is { } reason)
        {
            logger.LogError(
                "Invoice submission {SubmissionId}: not re-runnable ({Reason}); nothing is written and the hold stays (Permanent={Permanent}).",
                submissionId, reason, true);
        }
        else
        {
            logger.LogInformation(
                "Invoice submission {SubmissionId}: read rebuilt from the stored rows ({LineCount} lines, {SummaryNameCount} summary names, {QuoteCount} observation quotes).",
                submissionId, result.Read!.Lines.Count, result.Read.TimekeeperSummary.Count, result.Coding!.QuoteCount);
        }

        return result;
    }

    private static Result RebuildCore(string? findingsJson, IEnumerable<Entity> lineRows)
    {
        var facts = ExtractionProvenance.ParseRerunFacts(findingsJson);
        var provenance = ExtractionProvenance.ParseSubmission(findingsJson);
        if (facts is null || provenance is null || !provenance.HasExtraction)
        {
            return Result.NotRerunnable(FactsMissing);
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(findingsJson!) as JsonObject ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return Result.NotRerunnable(StoredRowsInconsistent);
        }

        // The fee and expense lines in persisted order (sequence 1..N, the persister's index + 1); a gap or a repeat is not guessed around.
        var stored = new SortedDictionary<int, Entity>();
        foreach (var row in lineRows)
        {
            if (row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is not ((int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense))
            {
                continue;
            }

            if (row.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) is not { } sequence || sequence < 1 || !stored.TryAdd(sequence - 1, row))
            {
                return Result.NotRerunnable(StoredRowsInconsistent);
            }
        }

        if (stored.Count == 0 || stored.Keys.Last() != stored.Count - 1)
        {
            return Result.NotRerunnable(StoredRowsInconsistent);
        }

        var quotes = facts.Observations.ToLookup(q => q.Line);
        var lines = new List<ExtractedInvoiceLine>(stored.Count);
        var coded = new List<StoredLineCoding>(stored.Count);
        var rowIndex = new Dictionary<InvoiceLineType, int>();
        foreach (var (index, row) in stored)
        {
            if (ParseObject(row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExtractionConfidence)) is not { } extraction
                || extraction[ExtractionProvenance.LineDateMissingMember] is not JsonValue flag || !flag.TryGetValue<bool>(out var dateMissing))
            {
                return Result.NotRerunnable(FactsMissing);
            }

            var type = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)!.Value == (int)InvoiceLineCostType.Fee
                ? InvoiceLineType.Fee
                : InvoiceLineType.Expense;
            rowIndex[type] = rowIndex.TryGetValue(type, out var n) ? n + 1 : 0;
            lines.Add(Line(row, extraction, type, rowIndex[type], dateMissing));

            if (StoredLineCoding.Parse(index, extraction["coding"] as JsonObject, quotes[index].ToList()) is not { } lineCoding)
            {
                return Result.NotRerunnable(StoredRowsInconsistent);
            }

            coded.Add(lineCoding);
        }

        // A quote on a line that is not stored is not dropped silently.
        if (facts.Observations.Any(q => q.Line < 0 || q.Line >= stored.Count))
        {
            return Result.NotRerunnable(StoredRowsInconsistent);
        }

        if (StoredCoding.From(root, coded) is not { } coding)
        {
            return Result.NotRerunnable(StoredRowsInconsistent);
        }

        var read = new ExtractedInvoice
        {
            Header = Header(provenance, facts),
            Lines = lines,
            TimekeeperSummary = facts.TimekeeperSummaryNames.Select(name => new ExtractedTimekeeperSummaryRow
            {
                Name = new ExtractedInvoiceField<string?> { Value = name },
                Title = new ExtractedInvoiceField<string?>(),
                Hours = new ExtractedInvoiceField<decimal?>(),
                Rate = new ExtractedInvoiceField<decimal?>(),
                Amount = new ExtractedInvoiceField<decimal?>(),
            }).ToList(),
            PageCount = 0,
            AnalyzerId = provenance.AnalyzerId!,
            ApiVersion = provenance.ApiVersion!,
        };

        return Result.Rerunnable(read, coding);
    }

    // ── Header ──

    private static ExtractedInvoiceHeader Header(ExtractionProvenance.SubmissionProvenance provenance, ExtractionProvenance.RerunFacts facts)
    {
        ExtractionProvenance.HeaderField? Read(string name) => provenance.Header.TryGetValue(name, out var field) ? field : null;

        ExtractedInvoiceField<string?> TextField(string name) => new() { Value = Read(name)?.Value, Confidence = Read(name)?.Confidence };

        ExtractedInvoiceField<DateOnly?> DateField(string name) => new() { Value = DateOf(Read(name)?.Value), Confidence = Read(name)?.Confidence };

        ExtractedInvoiceField<decimal?> Total(string name) => new() { Value = facts.SectionTotals[name].Value, Confidence = facts.SectionTotals[name].Confidence };

        return new ExtractedInvoiceHeader
        {
            FirmName = new ExtractedInvoiceField<string?>(),
            FirmInvoiceNumber = TextField(BillingFieldNames.FirmInvoiceNumber),
            InvoiceDate = DateField(BillingFieldNames.InvoiceDate),
            BillingPeriodStart = DateField(BillingFieldNames.BillingPeriodStart),
            BillingPeriodEnd = DateField(BillingFieldNames.BillingPeriodEnd),
            ClientName = new ExtractedInvoiceField<string?>(),
            MatterName = new ExtractedInvoiceField<string?>(),
            FirmMatterReference = new ExtractedInvoiceField<string?>(),
            PurchaseOrderAsQuoted = new ExtractedInvoiceField<string?>(),
            FeesTotal = Total(ResolutionFieldNames.FeesTotal),
            ExpensesTotal = Total(ResolutionFieldNames.ExpensesTotal),
            AdjustmentsTotal = Total(ResolutionFieldNames.AdjustmentsTotal),
            TotalDue = new() { Value = DecimalOf(Read(ResolutionFieldNames.TotalDue)?.Value), Confidence = Read(ResolutionFieldNames.TotalDue)?.Confidence },
            Currency = TextField(BillingFieldNames.Currency),
        };
    }

    // ── Lines ──

    private static ExtractedInvoiceLine Line(Entity row, JsonObject extraction, InvoiceLineType type, int rowIndex, bool dateMissing)
    {
        var fields = extraction["fields"] as JsonObject ?? new JsonObject();
        var isFee = type == InvoiceLineType.Fee;
        var quantity = row.GetAttributeValue<decimal?>(InvoiceSubmissionPersister.LineQuantity);
        var rate = row.GetAttributeValue<Money>(InvoiceSubmissionPersister.LineRate)?.Value;
        var date = row.GetAttributeValue<DateTime?>(InvoiceSubmissionPersister.LineEventDate) is { } stamp ? DateOnly.FromDateTime(stamp) : (DateOnly?)null;

        return new ExtractedInvoiceLine
        {
            LineType = type,
            RowIndex = rowIndex,
            Date = Field(fields, ResolutionFieldNames.Date, dateMissing ? null : date, DateOf),
            TimekeeperAsBilled = Field(fields, ResolutionFieldNames.TimekeeperAsBilled, row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTimekeeper), s => s),
            TitleAsBilled = Field<string?>(fields, "TitleAsBilled", null, s => s),
            Hours = Field(fields, ResolutionFieldNames.Hours, isFee ? quantity : null, DecimalOf),
            Rate = Field(fields, ResolutionFieldNames.Rate, isFee ? rate : null, DecimalOf),
            Quantity = Field(fields, ResolutionFieldNames.Quantity, isFee ? null : quantity, DecimalOf),
            UnitCost = Field(fields, ResolutionFieldNames.UnitCost, isFee ? null : rate, DecimalOf),
            Amount = Field(fields, ResolutionFieldNames.Amount, row.GetAttributeValue<Money>(InvoiceTotalsService.LineAmount)?.Value, DecimalOf),
            Narrative = Field(fields, BillingFieldNames.Narrative, row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineDescription), s => s),
            TaskCodeAsBilled = Field(fields, BillingFieldNames.BilledTaskCode, row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTaskCodeAsBilled), s => s),
            ActivityCodeAsBilled = Field(fields, BillingFieldNames.BilledActivityCode, row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineActivityCodeAsBilled), s => s),
            ExpenseCodeAsBilled = Field(fields, BillingFieldNames.BilledExpenseCode, row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExpenseCodeAsBilled), s => s),
        };
    }

    /// <summary>One line field: the column's value, with the confidence, page, box, repair and returned value the extraction JSON kept.</summary>
    private static ExtractedInvoiceField<T> Field<T>(JsonObject fields, string name, T value, Func<string?, T> parse)
    {
        var json = fields[name] as JsonObject;
        var repair = Enum.TryParse<ExtractedValueRepair>(Text(json, "repair"), out var r) && Enum.IsDefined(r) ? r : ExtractedValueRepair.None;
        return new ExtractedInvoiceField<T>
        {
            Value = value,
            Confidence = DecimalNode(json?["confidence"]),
            Page = json?["page"] is JsonValue page && page.TryGetValue<int>(out var p) ? p : null,
            BoundingBox = json?["box"] is JsonObject box
                && box["page"] is JsonValue bp && bp.TryGetValue<int>(out var boxPage)
                && DecimalNode(box["x"]) is { } x && DecimalNode(box["y"]) is { } y && DecimalNode(box["w"]) is { } w && DecimalNode(box["h"]) is { } h
                    ? new ExtractedBoundingBox(boxPage, x, y, w, h)
                    : null,
            Repair = repair,
            ReturnedValue = repair == ExtractedValueRepair.None ? default : parse(Text(json, "returned")),
        };
    }

    // ── Helpers ──

    private static JsonObject? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Text(JsonObject? node, string member) =>
        node?[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static decimal? DecimalNode(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : null;

    private static DateOnly? DateOf(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static decimal? DecimalOf(string? text) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
}

/// <summary>One line's coding as intake stored it (its <c>sprk_extractionconfidence.coding</c> block plus its observation quotes).</summary>
/// <param name="Coded">The line's coding result, or null when intake did not code the invoice (<c>NotCoded</c>, D-126).</param>
internal sealed record StoredLineCoding(int Index, LineCodingResult? Coded)
{
    /// <summary>The line's stored coding; null when the block cannot be read back (a quote without its observation, or the reverse, included).</summary>
    internal static StoredLineCoding? Parse(int index, JsonObject? coding, IReadOnlyList<ExtractionProvenance.ObservationQuote> quotes)
    {
        var status = StoredSubmissionRead.Text(coding, "status");
        if (coding is null || status is null)
        {
            return null;
        }

        if (status == "NotCoded")
        {
            return quotes.Count == 0 ? new StoredLineCoding(index, null) : null;
        }

        if (!Enum.TryParse<LineCodingStatus>(status, out var lineStatus) || !Enum.IsDefined(lineStatus)
            || !Guid.TryParse(StoredSubmissionRead.Text(coding, "codeSetId"), out var setId)
            || !Enum.TryParse<CodeSetSource>(StoredSubmissionRead.Text(coding, "codeSetSource"), out var source) || !Enum.IsDefined(source)
            || StoredSubmissionRead.DecimalNode(coding["confidence"]) is not { } confidence)
        {
            return null;
        }

        var observations = new List<InvoiceLineObservation>();
        foreach (var observation in (coding["observations"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (!Enum.TryParse<InvoiceLineObservationType>(StoredSubmissionRead.Text(observation, "type"), out var type) || !Enum.IsDefined(type)
                || StoredSubmissionRead.DecimalNode(observation["confidence"]) is not { } observed
                || quotes.Where(q => q.Type == type).ToList() is not [var quote])
            {
                return null;
            }

            observations.Add(new InvoiceLineObservation { Type = type, Confidence = observed, QuotedText = quote.QuotedText });
        }

        if (observations.Count != quotes.Count)
        {
            return null;
        }

        var refused = new List<RefusedCode>();
        foreach (var entry in (coding["refused"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (!Enum.TryParse<BillingCodeKind>(StoredSubmissionRead.Text(entry, "kind"), out var kind) || !Enum.IsDefined(kind)
                || StoredSubmissionRead.Text(entry, "code") is not { } code
                || !Enum.TryParse<CodeRefusalReason>(StoredSubmissionRead.Text(entry, "reason"), out var reason) || !Enum.IsDefined(reason))
            {
                return null;
            }

            refused.Add(new RefusedCode(kind, code, reason));
        }

        return new StoredLineCoding(index, new LineCodingResult
        {
            LineKey = index.ToString(CultureInfo.InvariantCulture),
            Status = lineStatus,
            Task = Code(coding["task"] as JsonObject),
            Activity = Code(coding["activity"] as JsonObject),
            Expense = Code(coding["expense"] as JsonObject),
            Confidence = confidence,
            Observations = observations,
            RefusedCodes = refused,
            CodeSetId = setId,
            CodeSetName = StoredSubmissionRead.Text(coding, "codeSetName") ?? string.Empty,
            CodeSetSource = source,
        });
    }

    private static LineCodeValue Code(JsonObject? slot) => new(
        StoredSubmissionRead.Text(slot, "code"),
        StoredSubmissionRead.Text(slot, "firmCode"),
        slot?["mismatch"] is JsonValue mismatch && mismatch.TryGetValue<bool>(out var differs) && differs);
}

/// <summary>
/// The invoice's coding as intake stored it: every line coded (<see cref="Coded"/>), or none, with the coding hold intake recorded
/// (<see cref="HoldReasonCode"/> and its cause). A mix of coded and uncoded lines is not a state intake writes, so it is not rebuilt.
/// </summary>
internal sealed record StoredCoding(InvoiceCodingResult? Coded, string? HoldReasonCode, string? HoldCause, int QuoteCount)
{
    /// <summary>The stored coding of <paramref name="lines"/> (in index order), or null when it is not one of the two shapes intake writes.</summary>
    internal static StoredCoding? From(JsonObject root, IReadOnlyList<StoredLineCoding> lines)
    {
        var quoteCount = lines.Sum(l => l.Coded?.Observations.Count ?? 0);
        if (lines.All(l => l.Coded is null))
        {
            // D-126: intake could not code; its pipeline finding names the hold and its cause.
            var holds = (root["pipelineFindings"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(p => StoredSubmissionRead.Text(p, "reasonCode") is SubmissionReasonCodes.CodingNoCodeSetHold or SubmissionReasonCodes.CodingUnavailableHold)
                .ToList();
            if (holds is not [var hold] || StoredSubmissionRead.Text(hold["values"] as JsonObject, "cause") is not { Length: > 0 } cause)
            {
                return null;
            }

            return new StoredCoding(null, StoredSubmissionRead.Text(hold, "reasonCode"), cause, 0);
        }

        if (lines.Any(l => l.Coded is null)
            || root["codeSet"] is not JsonObject set
            || !Guid.TryParse(StoredSubmissionRead.Text(set, "id"), out var setId)
            || !Enum.TryParse<CodeSetSource>(StoredSubmissionRead.Text(set, "source"), out var source) || !Enum.IsDefined(source))
        {
            return null;
        }

        // The invoice-level set: its id, name and source as stored. Its code lists are not needed once the lines are coded (the guard ran at
        // intake), so they are left empty; a line whose set changes is coded through 047, which loads its set in full.
        var codeSet = new ResolvedCodeSet(setId, StoredSubmissionRead.Text(set, "name") ?? string.Empty, source, [], [], []);
        return new StoredCoding(
            new InvoiceCodingResult { CodeSet = codeSet, Lines = lines.Select(l => l.Coded!).ToList(), ChunkCount = 0 },
            null,
            null,
            quoteCount);
    }
}
