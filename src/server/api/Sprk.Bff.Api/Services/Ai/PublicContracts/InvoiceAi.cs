using System.Globalization;
using System.Text.Json;
using OpenAI.Chat;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.ContentUnderstanding;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IInvoiceAi"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Legacy members</b> (<see cref="GetPlaybookByNameAsync"/>, <see cref="GetStructuredCompletionAsync{T}"/>,
/// <see cref="GenerateEmbeddingAsync"/>): a thin wrapper over <see cref="IPlaybookService"/> +
/// <see cref="IOpenAiClient"/> per the ADR-007 facade pattern — zero behaviour change. The first two retire
/// with their Finance callers (ebilling-r1 tasks 042 and 050).
/// </para>
/// <para>
/// <b>Capability methods</b> (ebilling-r1 task 041, D-52): each resolves its Action through
/// <see cref="IActionResolver"/> with its <see cref="ConsumerTypes"/> key, runs it through
/// <see cref="IActionRunner"/> with the request serialized as the <see cref="OperandChannel.Input"/> operand
/// (the same sequence as <see cref="CommunicationTriageAi"/>), and parses the structured output into the
/// result DTO. Prompt, output schema and model tier come from the Action / Binding rows; none of them is
/// in this class. Unlike triage (best-effort, returns null) these methods fail closed with
/// <see cref="InvoiceAiException"/> (FR-46). Logs carry consumer type, reason code, exception type and
/// identifiers only (ADR-015).
/// </para>
/// </remarks>
public sealed class InvoiceAi : IInvoiceAi
{
    private readonly IPlaybookService _playbook;
    private readonly IOpenAiClient _openAi;
    private readonly IActionResolver _actionResolver;
    private readonly IActionRunner _actionRunner;
    private readonly ContentUnderstandingClient _contentUnderstanding;
    private readonly ILogger<InvoiceAi> _logger;

    public InvoiceAi(
        IPlaybookService playbook,
        IOpenAiClient openAi,
        IActionResolver actionResolver,
        IActionRunner actionRunner,
        ContentUnderstandingClient contentUnderstanding,
        ILogger<InvoiceAi> logger)
    {
        _playbook = playbook ?? throw new ArgumentNullException(nameof(playbook));
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
        _contentUnderstanding = contentUnderstanding ?? throw new ArgumentNullException(nameof(contentUnderstanding));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<PlaybookResponse> GetPlaybookByNameAsync(
        string playbookName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playbookName);
        return _playbook.GetByNameAsync(playbookName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T> GetStructuredCompletionAsync<T>(
        IEnumerable<ChatMessage> messages,
        BinaryData jsonSchema,
        string schemaName,
        string deploymentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(jsonSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName);

        return _openAi.GetStructuredCompletionAsync<T>(
            messages,
            jsonSchema,
            schemaName,
            deploymentName,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        string? model = null,
        int? dimensions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return _openAi.GenerateEmbeddingAsync(text, model, dimensions, cancellationToken);
    }

    // ── Capability methods ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<InvoiceClassifyResult> ClassifyAsync(
        InvoiceClassifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DocumentText);

        var input = ToElement(new { documentText = request.DocumentText });
        var output = await RunCapabilityAsync(
            ConsumerTypes.InvoiceClassify, input, request.TenantId, subjectId: null, cancellationToken).ConfigureAwait(false);
        return ParseOrThrow(ConsumerTypes.InvoiceClassify, output, subjectId: null, InvoiceAiOutputParser.ParseClassify);
    }

    /// <inheritdoc />
    public async Task<InvoiceCodeLinesResult> CodeLinesAsync(
        InvoiceCodeLinesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Lines);
        ArgumentNullException.ThrowIfNull(request.AllowedTaskCodes);
        ArgumentNullException.ThrowIfNull(request.AllowedActivityCodes);
        ArgumentNullException.ThrowIfNull(request.AllowedExpenseCodes);
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("At least one line is required.", nameof(request));
        }

        var input = ToElement(new
        {
            lines = request.Lines.Select(l => new
            {
                lineKey = l.LineKey,
                date = l.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                lineType = l.LineType.ToString(),
                narrative = l.Narrative,
                hours = l.Hours,
                amount = l.Amount,
                billedTaskCode = l.BilledTaskCode,
                billedActivityCode = l.BilledActivityCode,
                billedExpenseCode = l.BilledExpenseCode,
            }),
            allowedTaskCodes = CodeOptions(request.AllowedTaskCodes),
            allowedActivityCodes = CodeOptions(request.AllowedActivityCodes),
            allowedExpenseCodes = CodeOptions(request.AllowedExpenseCodes),
        });

        var output = await RunCapabilityAsync(
            ConsumerTypes.InvoiceLineCode, input, request.TenantId, subjectId: null, cancellationToken).ConfigureAwait(false);
        return ParseOrThrow(ConsumerTypes.InvoiceLineCode, output, subjectId: null, InvoiceAiOutputParser.ParseCodeLines);
    }

    /// <inheritdoc />
    public async Task<InvoiceCodeSetMatchResult> MatchCodeSetAsync(
        InvoiceCodeSetMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReferenceName);
        ArgumentNullException.ThrowIfNull(request.Candidates);
        ArgumentNullException.ThrowIfNull(request.Examples);
        if (request.Candidates.Count == 0)
        {
            throw new ArgumentException("At least one candidate code set is required.", nameof(request));
        }

        var input = ToElement(new
        {
            reference = new { type = request.ReferenceType.ToString(), name = request.ReferenceName },
            candidates = request.Candidates.Select(c => new
            {
                codeSetId = c.CodeSetId,
                name = c.Name,
                body = c.Body,
                description = c.Description,
                matchHints = c.MatchHints,
                taskCodes = c.TaskCodes,
            }),
            examples = request.Examples.Select(e => new
            {
                referenceType = e.ReferenceType.ToString(),
                referenceName = e.ReferenceName,
                codeSetName = e.CodeSetName,
            }),
        });

        var output = await RunCapabilityAsync(
            ConsumerTypes.InvoiceCodeSetMatch, input, request.TenantId, subjectId: null, cancellationToken).ConfigureAwait(false);
        return ParseOrThrow(ConsumerTypes.InvoiceCodeSetMatch, output, subjectId: null, InvoiceAiOutputParser.ParseCodeSetMatch);
    }

    /// <inheritdoc />
    public async Task<BillingRuleDraftResult> DraftRulesAsync(
        BillingRuleDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DocumentText);
        ArgumentNullException.ThrowIfNull(request.RuleTypes);
        if (request.RuleTypes.Count == 0)
        {
            throw new ArgumentException("The rule-type catalogue must not be empty.", nameof(request));
        }

        var input = ToElement(new
        {
            documentText = request.DocumentText,
            ruleTypes = request.RuleTypes.Select(t => new
            {
                ruleType = t.RuleType,
                description = t.Description,
                parameterSchema = ParseParameterSchema(t),
            }),
        });

        var output = await RunCapabilityAsync(
            ConsumerTypes.BillingRuleDraft, input, request.TenantId, request.DocumentId, cancellationToken).ConfigureAwait(false);
        return ParseOrThrow(ConsumerTypes.BillingRuleDraft, output, request.DocumentId, InvoiceAiOutputParser.ParseDraftRules);
    }

    /// <inheritdoc />
    public async Task<InvoiceFirmNoticeDraft> DraftFirmNoticeAsync(
        InvoiceFirmNoticeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Findings);

        var input = ToElement(new
        {
            outcome = request.Outcome.ToString(),
            findings = request.Findings.Select(f => new
            {
                ruleSetName = f.RuleSetName,
                ruleName = f.RuleName,
                consequence = f.Consequence,
                issue = f.Issue,
            }),
        });

        var output = await RunCapabilityAsync(
            ConsumerTypes.InvoiceFirmNotice, input, request.TenantId, subjectId: null, cancellationToken).ConfigureAwait(false);
        return ParseOrThrow(ConsumerTypes.InvoiceFirmNotice, output, subjectId: null, InvoiceAiOutputParser.ParseFirmNotice);
    }

    // ── Invoice reading (ebilling-r1 task 043) ──────────────────────────────────

    /// <inheritdoc />
    public async Task<ExtractedInvoice> ReadInvoiceAsync(
        InvoiceReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_contentUnderstanding.IsConfigured)
        {
            throw new FeatureDisabledException(
                ReadNotConfiguredCode,
                "Invoice reading requires ContentUnderstanding:Endpoint (the environment's AIServices resource).");
        }

        if (request.Content.IsEmpty)
        {
            // Nothing to send: the service would refuse it as ContentEmpty.
            throw Fail(InvoiceAiException.ReadUnreadable, InvoiceAiException.ReadCapability, request.DocumentId, null,
                "The invoice document is empty.");
        }

        ContentUnderstandingAnalysis analysis;
        try
        {
            analysis = await _contentUnderstanding
                .AnalyzeDocumentAsync(request.Content, request.ContentType, request.DocumentId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ContentUnderstandingException ex)
        {
            var code = ex.Failure switch
            {
                ContentUnderstandingFailure.Unreadable => InvoiceAiException.ReadUnreadable,
                ContentUnderstandingFailure.PageLimit => InvoiceAiException.ReadPageLimit,
                _ => InvoiceAiException.ReadServiceFailed,
            };
            throw Fail(code, InvoiceAiException.ReadCapability, request.DocumentId, ex,
                $"Reading the invoice failed ({code}).");
        }

        ExtractedInvoice invoice;
        try
        {
            invoice = InvoiceExtractionCompletion.Complete(InvoiceAnalyzerResultMapper.ToInvoice(analysis));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The mapper is tolerant of missing and mistyped values; reaching here means the result itself is
            // malformed (for example a value outside the decimal range).
            throw Fail(InvoiceAiException.ReadServiceFailed, InvoiceAiException.ReadCapability, request.DocumentId, ex,
                "The invoice reader returned a result that could not be mapped.");
        }

        if (IsEmptyRead(invoice))
        {
            throw Fail(InvoiceAiException.ReadNoInvoice, InvoiceAiException.ReadCapability, request.DocumentId, null,
                "No invoice data was found in the document.");
        }

        _logger.LogInformation(
            "Invoice read: documentId={DocumentId} analyzer={AnalyzerId} apiVersion={ApiVersion} pages={PageCount} " +
            "lines={LineCount} repairedValues={RepairCount}",
            request.DocumentId, invoice.AnalyzerId, invoice.ApiVersion, invoice.PageCount, invoice.Lines.Count,
            CountRepairs(invoice));
        return invoice;
    }

    /// <summary>FeatureDisabledException code when <c>ContentUnderstanding:Endpoint</c> is empty.</summary>
    internal const string ReadNotConfiguredCode = "ai.invoice.read.not_configured";

    /// <summary>"No invoice": no invoice number, date or totals, and no rows (a blank page, a letter, a scan with no text).</summary>
    private static bool IsEmptyRead(ExtractedInvoice invoice) =>
        invoice.Lines.Count == 0
        && invoice.Header.FirmInvoiceNumber.Value is null
        && invoice.Header.InvoiceDate.Value is null
        && invoice.Header.TotalDue.Value is null
        && invoice.Header.FeesTotal.Value is null;

    private static int CountRepairs(ExtractedInvoice invoice) =>
        invoice.Lines.Sum(l =>
            (l.Hours.Repair != ExtractedValueRepair.None ? 1 : 0)
            + (l.Rate.Repair != ExtractedValueRepair.None ? 1 : 0)
            + (l.Amount.Repair != ExtractedValueRepair.None ? 1 : 0)
            + (l.Date.Repair != ExtractedValueRepair.None ? 1 : 0));

    /// <summary>
    /// Resolve the consumer type's Action and run it over <paramref name="input"/> on the
    /// <see cref="OperandChannel.Input"/> channel. Maps failures to <see cref="InvoiceAiException"/>;
    /// cancellation and the kill switch (<see cref="FeatureDisabledException"/>) propagate unchanged.
    /// </summary>
    private async Task<JsonElement> RunCapabilityAsync(
        string consumerType,
        JsonElement input,
        string? tenantId,
        Guid? subjectId,
        CancellationToken cancellationToken)
    {
        AnalysisAction action;
        try
        {
            action = await _actionResolver.ResolveAsync(consumerType, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPassThrough(ex, cancellationToken))
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // IActionResolver's documented contract: InvalidOperationException = no Action routed for the
            // consumer type, or the routed Action row does not exist. Fail closed — no hard-coded fallback.
            // The code says WHERE it failed, not that retrying is useless: the routing read swallows its own
            // exceptions and reports "no Action routed" (platform issue #1936), so a transient read failure
            // also lands here. Callers retry a bounded number of times before marking work Failed.
            throw Fail(InvoiceAiException.BindingMissing, consumerType, subjectId, ex,
                $"No Binding / Action is configured for consumer type '{consumerType}'.");
        }
        catch (Exception ex)
        {
            // Any other resolver failure (the Action row read itself failing, say) is not proof the Binding is
            // missing; report it as a failed run.
            throw Fail(InvoiceAiException.CompletionFailed, consumerType, subjectId, ex,
                $"Resolving the Action for consumer type '{consumerType}' failed.");
        }

        var boundInputs = new BoundInputs
        {
            Context = ContextEnvelopeReferenceProducer.Assemble(),
            Operand = new ResolvedOperand
            {
                Channel = OperandChannel.Input,
                Kind = OperandKind.PreResolved,
                Input = input,
            },
        };

        var runContext = new LinearRunContext
        {
            ConsumerType = consumerType,
            TenantId = tenantId,
        };

        try
        {
            return await _actionRunner.RunAsync(action, boundInputs, runContext, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPassThrough(ex, cancellationToken))
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Fail(InvoiceAiException.CompletionFailed, consumerType, subjectId, ex,
                $"The Action for consumer type '{consumerType}' failed to complete.");
        }
    }

    private T ParseOrThrow<T>(
        string consumerType,
        JsonElement output,
        Guid? subjectId,
        Func<JsonElement, T> parse)
    {
        try
        {
            return parse(output);
        }
        catch (InvoiceAiOutputException ex)
        {
            throw Fail(InvoiceAiException.OutputInvalid, consumerType, subjectId, innerException: null,
                $"The output for consumer type '{consumerType}' is invalid at '{ex.Path}'.", ex.Path);
        }
    }

    /// <summary>Caller cancellation and the AI kill switch are not capability failures — rethrow as-is.</summary>
    private static bool IsPassThrough(Exception ex, CancellationToken cancellationToken) =>
        ex is FeatureDisabledException
        || (ex is OperationCanceledException && cancellationToken.IsCancellationRequested);

    private InvoiceAiException Fail(
        string reasonCode,
        string consumerType,
        Guid? subjectId,
        Exception? innerException,
        string message,
        string? outputPath = null)
    {
        // ADR-015: identifiers and codes only. The inner exception is NOT logged here (its message could carry
        // model output); it travels on the thrown exception for the caller to handle.
        _logger.LogWarning(
            "Invoice AI capability failed: consumerType={ConsumerType} reasonCode={ReasonCode} subjectId={SubjectId} " +
            "cause={CauseType} outputPath={OutputPath}",
            consumerType, reasonCode, subjectId, innerException?.GetType().Name, outputPath);
        return new InvoiceAiException(reasonCode, consumerType, message, innerException);
    }

    private static JsonElement ToElement<T>(T payload) => JsonSerializer.SerializeToElement(payload);

    private static IEnumerable<object> CodeOptions(IReadOnlyList<InvoiceCodeOption> options) =>
        options.Select(o => new { code = o.Code, description = o.Description, guidance = o.Guidance });

    private static JsonElement ParseParameterSchema(BillingRuleTypeOption option)
    {
        try
        {
            using var doc = JsonDocument.Parse(option.ParameterSchemaJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    $"The parameter schema for rule type '{option.RuleType}' must be a JSON object.");
            }

            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                $"The parameter schema for rule type '{option.RuleType}' is not valid JSON.", ex);
        }
    }
}

/// <summary>Raised by <see cref="InvoiceAiOutputParser"/>; carries only the JSON path that failed.</summary>
internal sealed class InvoiceAiOutputException(string path) : Exception($"Invalid output at '{path}'.")
{
    public string Path { get; } = path;
}

/// <summary>
/// Strict parsers from an Action's structured output to the <see cref="IInvoiceAi"/> result DTOs. A missing
/// required member, a wrong JSON kind, an unknown enum name or a confidence outside 0–1 throws
/// <see cref="InvoiceAiOutputException"/> — never a default (FR-46). The property names here are the
/// contract the Action output schemas (042 / 047 / 054 / 075 / 131) must match.
/// </summary>
internal static class InvoiceAiOutputParser
{
    public static InvoiceClassifyResult ParseClassify(JsonElement output)
    {
        var root = RequireObject(output, "$");
        return new InvoiceClassifyResult
        {
            Classification = RequireEnum<InvoiceClassification>(root, "classification", "$"),
            Confidence = RequireConfidence(root, "confidence", "$"),
            Hints = ParseHints(root),
            Reasoning = OptionalString(root, "reasoning", "$"),
        };
    }

    public static InvoiceCodeLinesResult ParseCodeLines(JsonElement output)
    {
        var root = RequireObject(output, "$");
        var lines = new List<InvoiceLineCoding>();
        var i = 0;
        foreach (var item in RequireArray(root, "lines", "$"))
        {
            var path = $"$.lines[{i++}]";
            var line = RequireObject(item, path);
            var observations = new List<InvoiceLineObservation>();
            var j = 0;
            foreach (var obsItem in RequireArray(line, "observations", path))
            {
                var obsPath = $"{path}.observations[{j++}]";
                var obs = RequireObject(obsItem, obsPath);
                observations.Add(new InvoiceLineObservation
                {
                    Type = RequireEnum<InvoiceLineObservationType>(obs, "type", obsPath),
                    Confidence = RequireConfidence(obs, "confidence", obsPath),
                    // A blank quote is evidence-free, not malformed: pass it through as "" so LineCodingGuard drops and counts it,
                    // instead of failing the whole chunk (and with it the invoice's coding) over one placeholder observation.
                    QuotedText = OptionalString(obs, "quotedText", obsPath) ?? string.Empty,
                });
            }

            lines.Add(new InvoiceLineCoding
            {
                LineKey = RequireString(line, "lineKey", path),
                TaskCode = OptionalString(line, "taskCode", path),
                ActivityCode = OptionalString(line, "activityCode", path),
                ExpenseCode = OptionalString(line, "expenseCode", path),
                Confidence = RequireConfidence(line, "confidence", path),
                Observations = observations,
            });
        }

        return new InvoiceCodeLinesResult { Lines = lines };
    }

    public static InvoiceCodeSetMatchResult ParseCodeSetMatch(JsonElement output)
    {
        var root = RequireObject(output, "$");
        return new InvoiceCodeSetMatchResult
        {
            CodeSetId = RequireString(root, "codeSetId", "$").Trim(),
            Confidence = RequireConfidence(root, "confidence", "$"),
            Reason = RequireString(root, "reason", "$"),
        };
    }

    public static BillingRuleDraftResult ParseDraftRules(JsonElement output)
    {
        var root = RequireObject(output, "$");

        var proposals = new List<BillingRuleProposal>();
        var i = 0;
        foreach (var item in RequireArray(root, "proposals", "$"))
        {
            var path = $"$.proposals[{i++}]";
            var proposal = RequireObject(item, path);
            if (!proposal.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            {
                throw new InvoiceAiOutputException($"{path}.parameters");
            }

            proposals.Add(new BillingRuleProposal
            {
                RuleType = RequireString(proposal, "ruleType", path),
                ParametersJson = parameters.GetRawText(),
                Consequence = RequireString(proposal, "consequence", path),
                ClauseQuote = RequireString(proposal, "clauseQuote", path),
                ClauseLocation = OptionalString(proposal, "clauseLocation", path),
            });
        }

        var notConverted = new List<BillingRuleNotConverted>();
        i = 0;
        foreach (var item in RequireArray(root, "notConverted", "$"))
        {
            var path = $"$.notConverted[{i++}]";
            var entry = RequireObject(item, path);
            notConverted.Add(new BillingRuleNotConverted
            {
                Text = RequireString(entry, "text", path),
                Location = OptionalString(entry, "location", path),
                Reason = RequireString(entry, "reason", path),
            });
        }

        return new BillingRuleDraftResult { Proposals = proposals, NotConverted = notConverted };
    }

    public static InvoiceFirmNoticeDraft ParseFirmNotice(JsonElement output)
    {
        var root = RequireObject(output, "$");
        return new InvoiceFirmNoticeDraft
        {
            Subject = RequireString(root, "subject", "$"),
            Body = RequireString(root, "body", "$"),
        };
    }

    private static InvoiceClassifyHints? ParseHints(JsonElement root)
    {
        if (!root.TryGetProperty("hints", out var hints) || hints.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        const string path = "$.hints";
        RequireObject(hints, path);
        return new InvoiceClassifyHints
        {
            VendorName = OptionalString(hints, "vendorName", path),
            InvoiceNumber = OptionalString(hints, "invoiceNumber", path),
            InvoiceDate = OptionalString(hints, "invoiceDate", path),
            TotalAmount = OptionalDecimal(hints, "totalAmount", path),
            Currency = OptionalString(hints, "currency", path),
            MatterReference = OptionalString(hints, "matterReference", path),
        };
    }

    private static JsonElement RequireObject(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.Object ? element : throw new InvoiceAiOutputException(path);

    private static JsonElement.ArrayEnumerator RequireArray(JsonElement obj, string name, string path) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : throw new InvoiceAiOutputException($"{path}.{name}");

    private static string RequireString(JsonElement obj, string name, string path)
    {
        if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        throw new InvoiceAiOutputException($"{path}.{name}");
    }

    private static string? OptionalString(JsonElement obj, string name, string path)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvoiceAiOutputException($"{path}.{name}");
        }

        // An empty or whitespace-only optional value carries no information; surface it as absent (null), not "".
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static decimal? OptionalDecimal(JsonElement obj, string name, string path)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : throw new InvoiceAiOutputException($"{path}.{name}");
    }

    /// <summary>
    /// A confidence outside 0–1 is rejected rather than clamped: downstream rules compare it to a threshold
    /// (FR-26), so an out-of-range value would read as certainty.
    /// </summary>
    private static decimal RequireConfidence(JsonElement obj, string name, string path)
    {
        if (obj.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number)
            && number is >= 0m and <= 1m)
        {
            return number;
        }

        throw new InvoiceAiOutputException($"{path}.{name}");
    }

    /// <summary>Accepts only a defined member NAME (case-insensitive) — never a numeric string.</summary>
    private static TEnum RequireEnum<TEnum>(JsonElement obj, string name, string path)
        where TEnum : struct, Enum
    {
        if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            foreach (var member in Enum.GetValues<TEnum>())
            {
                if (string.Equals(member.ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    return member;
                }
            }
        }

        throw new InvoiceAiOutputException($"{path}.{name}");
    }
}
