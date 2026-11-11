using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The deterministic half of rule drafting (ebilling-r1 task 075, FR-27): the closed list of rule types the model is offered, how a model
/// proposal becomes a flat rule body plus wording, and how a long document is split. Nothing here calls a model or writes a row; the
/// proposals it returns are still only proposals: <see cref="RuleSetService.AddDraftsAsync"/> validates each with
/// <see cref="PolicyVersionValidator.ValidateForSave"/> (Billing domain) before it stores it.
/// </summary>
/// <remarks>
/// <para><b>Why a template and a headline are written here.</b> The Action returns parameters, a consequence and the clause, not message
/// wording (the wording is firm-facing and fixed by the rule type, so a model never writes text a firm reads, FR-50 posture). Each type
/// has one neutral sentence that names only the fact fields the type's handler supplies
/// (<see cref="BillingRuleCatalogue.TemplateFactFields"/>); the administrator edits it in the review.</para>
/// <para><b>Closed.</b> Every member is a switch over <see cref="RuleType"/> with a throwing default (D-27); the offered types are
/// <see cref="BillingRuleCatalogue.Types"/>.</para>
/// </remarks>
internal static class BillingRuleDraftTranslator
{
    /// <summary>The largest piece of a document sent to the model in one call.</summary>
    internal const int ChunkChars = 24_000;

    /// <summary>The most pieces one run reads; text after them is reported, not read.</summary>
    internal const int MaxChunks = 8;

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // The closed list sent to the model
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The eleven billing types in catalogue order, each with its description and its parameter schema (without the <c>type</c> member).
    /// The description ends with the consequences the type may carry (<see cref="AllowedConsequencesSentence"/>), so the drafting prompt
    /// never suggests one the catalogue refuses (073 review F1-b).
    /// </summary>
    internal static IReadOnlyList<BillingRuleTypeOption> RuleTypeOptions() =>
        BillingRuleCatalogue.Types
            .Select(t => new BillingRuleTypeOption(t.ToString(), Description(t) + " " + AllowedConsequencesSentence(t), ParameterSchemaJson(t)))
            .ToList();

    /// <summary>
    /// The sentence the drafting prompt reads for a type's consequences, from <see cref="BillingRuleCatalogue.AllowedConsequences"/>, plus the
    /// Duplicate narrowing (Reject only with <c>firmInvoiceNumber</c> among the match fields; 073 review, owner question 3).
    /// </summary>
    internal static string AllowedConsequencesSentence(RuleType ruleType)
    {
        var allowed = string.Join(", ", BillingRuleCatalogue.AllowedConsequences(ruleType).OrderBy(c => (int)c).Select(c => c.ToString()));
        var sentence = $"Allowed consequences: {allowed}.";
        return ruleType == RuleType.Duplicate
            ? sentence + " Reject only when matchFields include firmInvoiceNumber; a rule that compares lines only uses Flag."
            : sentence;
    }

    /// <summary>The embedded body schema of a type with the members the application adds itself (<c>type</c>) and the long prose removed.</summary>
    internal static string ParameterSchemaJson(RuleType ruleType)
    {
        var schema = JsonNode.Parse(BillingRuleCatalogue.SchemaText(ruleType))!.AsObject();
        foreach (var name in new[] { "$schema", "$id", "title", "description" })
        {
            schema.Remove(name);
        }

        if (schema["properties"] is JsonObject properties)
        {
            properties.Remove("type");
        }

        if (schema["required"] is JsonArray required)
        {
            for (var i = required.Count - 1; i >= 0; i--)
            {
                if (required[i]?.GetValue<string>() == "type")
                {
                    required.RemoveAt(i);
                }
            }

            if (required.Count == 0)
            {
                schema.Remove("required");
            }
        }

        return schema.ToJsonString();
    }

    private static string Description(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => "Every line's timekeeper must be approved for the firm. Use it when the text requires timekeepers to be approved or pre-approved. It has no parameters.",
        RuleType.ApprovedRate => "A line's billed rate may be at most the approved or agreed rate for its timekeeper or classification, plus an optional tolerance in percent.",
        RuleType.TimeIncrement => "Hours on a line must be a multiple of a fixed increment, for example 0.1 of an hour.",
        RuleType.DailyHoursCap => "One timekeeper may bill at most a stated number of hours per day on an invoice.",
        RuleType.NonBillableActivity => "Lines carrying certain activity or task codes are not billable.",
        RuleType.ExpenseRule => "Certain expense codes are not reimbursable, and/or one expense item may not be over a maximum amount. At least one of the two parameters is needed.",
        RuleType.InvoiceAge => "A line may not be dated more than a stated number of days before the invoice date.",
        RuleType.CoreRecordWindow => "Each line must be dated within the open and close dates of the matter or project, allowing a stated number of grace days.",
        RuleType.Duplicate => "An invoice or line may not duplicate an earlier one from the same firm; the parameter names the fields that must match.",
        RuleType.RequiredData => "Each invoice and line must carry certain data fields, for example the firm's invoice number or the task code.",
        RuleType.AiObservation => "A line may not show a billing-practice observation (block billing or vague description) at or above a stated confidence.",
        RuleType.AddedFee => "An added fee (a late fee, an administrative fee, interest or another added fee) billed on expense lines may total at most a cap: an amount, or a percent of the invoice's other lines. Only allowance 'cap' with the fee's expense codes can be checked; a clause that forbids an added fee or allows it only after a number of days goes to notConverted, and so does a rate charged on an overdue balance over time (for example '1.5% per month'), which is not a percent of the other lines.",
        RuleType.Discount => "A fee line's billed rate must reflect an agreed discount off the timekeeper's standard (rack) rate, in percent, with an optional tolerance in percent. It flags a fee line whose rate is above the discounted rate plus the tolerance; a discount given at invoice level is not checked.",
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Proposal to drafted rule
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The fixed firm-facing wording of a type: a headline and a message template naming only its allowed fact fields.</summary>
    internal static (string Headline, string Template) Wording(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => (
            "Timekeeper not approved",
            "{{line.timekeeperName}} is not an approved timekeeper for this firm on {{line.date}}."),
        RuleType.ApprovedRate => (
            "Billed rate above the approved rate",
            "{{line.timekeeperName}} billed {{line.rate}} on {{line.date}}; the approved rate is {{approvedRate}} (tolerance {{rule.tolerancePercent}} percent)."),
        RuleType.TimeIncrement => (
            "Hours not in the required increment",
            "{{line.hours}} hours billed by {{line.timekeeperName}} on {{line.date}} is not a multiple of {{rule.incrementHours}} hours."),
        RuleType.DailyHoursCap => (
            "Daily hours limit exceeded",
            "{{line.timekeeperName}} billed {{dailyHours}} hours on {{line.date}}; the limit is {{rule.maxHoursPerDay}} hours per day."),
        RuleType.NonBillableActivity => (
            "Non-billable activity billed",
            "The activity code {{line.activityCode}} (task code {{line.taskCode}}) billed by {{line.timekeeperName}} on {{line.date}} is not billable."),
        RuleType.ExpenseRule => (
            "Expense not allowed under the guidelines",
            "The expense {{line.expenseCode}} of {{line.amount}} on {{line.date}} is not allowed under the billing guidelines."),
        RuleType.InvoiceAge => (
            "Line dated too long before the invoice",
            "The line dated {{line.date}} is more than {{rule.maxAgeDays}} days before the invoice date {{invoice.date}}."),
        RuleType.CoreRecordWindow => (
            "Line outside the matter dates",
            "The line dated {{line.date}} is outside the open date {{coreRecord.openDate}} and close date {{coreRecord.closeDate}} of the matter (grace {{rule.graceDays}} days)."),
        RuleType.Duplicate => (
            "Possible duplicate billing",
            "Invoice {{invoice.firmInvoiceNumber}}, the line dated {{line.date}} for {{line.timekeeperName}}, duplicates {{duplicateOf.trackingNumber}}."),
        RuleType.RequiredData => (
            "Required data missing",
            "The required data {{missingField}} is missing."),
        RuleType.AiObservation => (
            "Billing practice concern",
            "The line dated {{line.date}} shows {{observation}} (confidence {{observationConfidence}}; the rule applies from {{rule.minConfidence}})."),
        RuleType.AddedFee => (
            "Added fees over the agreed limit",
            "The {{rule.feeKind}} charges matched by their expense codes total {{feeTotal}}, more than the agreed limit of {{feeLimit}}."),
        RuleType.Discount => (
            "Rate above the agreed discounted rate",
            "{{line.timekeeperName}} billed {{line.rate}} on {{line.date}}, above the limit of {{discountLimit}}: the agreed discount of {{rule.discountPercent}} percent off the standard rate of {{rackRate}} gives {{discountedRate}}."),
        // D-125: never offered or authored; every SubmissionIntegrity reason has its own message, so this is only the closed switch's answer.
        RuleType.SubmissionIntegrity => (
            "Submission check",
            "The submission has a note for review."),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>
    /// Turns the proposals of one or more drafting calls into drafted rules and the proposals that could not even be shaped into a body.
    /// A repeated rule (same type, body and clause) is kept once. The result is NOT validated against the rule body schema here.
    /// When <paramref name="sourceText"/> is given, a proposal whose clause quote is not in that text (ignoring white space, case and
    /// typographic quote and dash characters) is refused: a "verbatim" quote the model made up must never reach the review page or,
    /// once published, a firm.
    /// </summary>
    internal static (List<DraftedRule> Rules, List<NotConvertedClause> Refused) Translate(
        IEnumerable<BillingRuleProposal> proposals, string? sourceText = null)
    {
        var rules = new List<DraftedRule>();
        var refused = new List<NotConvertedClause>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalisedSource = sourceText is null ? null : NormaliseForQuoteCheck(sourceText);
        foreach (var proposal in proposals)
        {
            if (!TryParseType(proposal.RuleType, out var ruleType))
            {
                refused.Add(new NotConvertedClause(
                    proposal.ClauseQuote, proposal.ClauseLocation, "Not stored: the drafted rule named a rule type that is not in the offered list."));
                continue;
            }

            if (normalisedSource is not null && !QuoteIsInText(proposal.ClauseQuote, normalisedSource))
            {
                refused.Add(new NotConvertedClause(
                    proposal.ClauseQuote, proposal.ClauseLocation, "Not stored: the clause quote was not found in the document."));
                continue;
            }

            string body;
            try
            {
                body = BuildBody(ruleType, proposal.ParametersJson);
            }
            catch (JsonException)
            {
                refused.Add(new NotConvertedClause(
                    proposal.ClauseQuote, proposal.ClauseLocation, "Not stored: the drafted rule's parameters were not a JSON object."));
                continue;
            }

            if (!seen.Add($"{ruleType}\n{body}\n{proposal.ClauseQuote}"))
            {
                continue;
            }

            var (headline, template) = Wording(ruleType);
            rules.Add(new DraftedRule(
                ruleType.ToString(), body, template, headline, proposal.Consequence, proposal.ClauseQuote, proposal.ClauseLocation));
        }

        return (rules, refused);
    }

    private static bool QuoteIsInText(string? quote, string normalisedSource)
    {
        if (string.IsNullOrWhiteSpace(quote))
        {
            return false;
        }

        return normalisedSource.Contains(NormaliseForQuoteCheck(quote), StringComparison.Ordinal);
    }

    /// <summary>White space collapsed to one space, lower case, typographic quotes and dashes reduced to their plain forms.</summary>
    internal static string NormaliseForQuoteCheck(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var raw in text)
        {
            if (char.IsWhiteSpace(raw) || raw == ' ')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            var c = raw switch
            {
                '‘' or '’' or '‛' => '\'',
                '“' or '”' or '‟' => '"',
                '‐' or '‑' or '‒' or '–' or '—' or '−' => '-',
                _ => char.ToLowerInvariant(raw),
            };
            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Exact catalogue name only (ordinal): never a number, a comma list or another case, which the model must not be able to smuggle in.</summary>
    internal static bool TryParseType(string? text, out RuleType ruleType)
    {
        foreach (var candidate in BillingRuleCatalogue.Types)
        {
            if (string.Equals(text, candidate.ToString(), StringComparison.Ordinal))
            {
                ruleType = candidate;
                return true;
            }
        }

        ruleType = default;
        return false;
    }

    /// <summary>The flat rule body: <c>{"type":"…", params}</c> from the parameters object with its null members (the Action's "not used") removed.</summary>
    /// <exception cref="JsonException">The parameters are not a JSON object.</exception>
    internal static string BuildBody(RuleType ruleType, string parametersJson)
    {
        var parameters = JsonNode.Parse(parametersJson) as JsonObject ?? throw new JsonException("The parameters are not a JSON object.");
        var body = new JsonObject { ["type"] = ruleType.ToString() };
        foreach (var member in parameters)
        {
            // The application sets "type"; a null member is the Action's "not used".
            if (member.Value is not null && member.Key != "type")
            {
                body[member.Key] = member.Value.DeepClone();
            }
        }

        return body.ToJsonString();
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Long documents
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits text into pieces of at most <paramref name="maxChars"/> at paragraph breaks (a single longer paragraph is cut at the limit),
    /// returning at most <paramref name="maxChunks"/> pieces. <paramref name="unreadFrom"/> is the index of the first character NOT in
    /// any returned piece, or -1 when all of the text is.
    /// </summary>
    internal static IReadOnlyList<string> Split(string text, int maxChars, int maxChunks, out int unreadFrom)
    {
        var chunks = new List<string>();
        var start = 0;
        while (start < text.Length && chunks.Count < maxChunks)
        {
            var end = Math.Min(start + maxChars, text.Length);
            if (end < text.Length)
            {
                var paragraph = text.LastIndexOf("\n\n", end - 1, end - start, StringComparison.Ordinal);
                var line = paragraph > start ? -1 : text.LastIndexOf('\n', end - 1, end - start);
                if (paragraph > start)
                {
                    end = paragraph + 2;
                }
                else if (line > start)
                {
                    end = line + 1;
                }
            }

            var piece = text[start..end];
            if (!string.IsNullOrWhiteSpace(piece))
            {
                chunks.Add(piece);
            }

            start = end;
        }

        unreadFrom = start < text.Length ? start : -1;
        return chunks;
    }
}
