using System.Text.Json;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Classification;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IAgreementAi"/> (spaarke-intake-pipeline-r1 task 004, FR-02).
/// </summary>
/// <remarks>
/// <para>
/// Each method resolves its Action through <see cref="IActionResolver"/> with its <see cref="ConsumerTypes"/> key, runs it through
/// <see cref="IActionRunner"/> with the request serialized as the <see cref="OperandChannel.Input"/> operand (the sequence
/// <see cref="InvoiceAi"/> uses), and parses the structured output. Prompt, output schema and model tier come from the Action and
/// Binding rows; none of them is here. Failures are typed (<see cref="AgreementAiException"/>); cancellation and the kill switch
/// (<see cref="FeatureDisabledException"/>) pass through unchanged. Logs carry consumer type, reason code, exception type, ids,
/// JSON paths and counts only, never document, clause or plan text (ADR-015).
/// </para>
/// <para>
/// <b>Classify</b> injects the live <c>sprk_agreementtype</c> registry into the Action's prompt
/// (<see cref="AgreementTypeRegistryPromptAssembler"/>) before the run, because the classifier's valid keys are prompt data, and
/// refuses a candidate whose key is not in that registry. <b>Edit plan</b> sends the draft's deterministic segmentation (the closed list of <c>para_key</c> values the model may anchor on, not
/// free text) and the pinned NDA standard (<see cref="PinnedNdaStandard"/>), and hands the model's edits to
/// <see cref="AgreementEditPlanAssembler"/>, which refuses any edit whose anchors do not match the segmentation exactly (no fuzzy matching
/// anywhere), then completes and validates the plan. Neither the engines nor the Console's review flow are involved.
/// </para>
/// <para>
/// The resolve-and-run helper repeats <see cref="InvoiceAi"/>'s private one on purpose: the eBilling facade is frozen until UAT, and
/// a shared helper is worth extracting when a third Action-backed facade needs it (known limit, task 004 notes).
/// </para>
/// </remarks>
public sealed class AgreementAi : IAgreementAi
{
    private readonly IActionResolver _actionResolver;
    private readonly IActionRunner _actionRunner;
    private readonly IAgreementTypeRegistryReader _registryReader;
    private readonly AgreementTypeRegistryPromptAssembler _registryAssembler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgreementAi> _logger;

    public AgreementAi(
        IActionResolver actionResolver,
        IActionRunner actionRunner,
        IAgreementTypeRegistryReader registryReader,
        AgreementTypeRegistryPromptAssembler registryAssembler,
        TimeProvider timeProvider,
        ILogger<AgreementAi> logger)
    {
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
        _registryReader = registryReader ?? throw new ArgumentNullException(nameof(registryReader));
        _registryAssembler = registryAssembler ?? throw new ArgumentNullException(nameof(registryAssembler));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AgreementClassifyResult> ClassifyAsync(
        AgreementClassifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DocumentText);

        const string consumerType = ConsumerTypes.AgreementClassify;
        var action = await ResolveAsync(consumerType, request.SubjectId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<AgreementTypeRow> registry;
        try
        {
            registry = await _registryReader.GetRowsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPassThrough(ex, cancellationToken))
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Fail(AgreementAiException.CompletionFailed, consumerType, request.SubjectId, ex,
                "Reading the agreement type registry failed.");
        }

        if (registry.Count == 0)
        {
            // Classifying against an empty closed set can only return a non-agreement or an invented key.
            throw Fail(AgreementAiException.CompletionFailed, consumerType, request.SubjectId, innerException: null,
                "The agreement type registry has no active rows.");
        }

        var materialized = _registryAssembler.Materialize(action, registry);
        if (ReferenceEquals(materialized, action))
        {
            // The assembler leaves an Action without the placeholder untouched: the model would classify with no key set.
            throw Fail(AgreementAiException.CompletionFailed, consumerType, request.SubjectId, innerException: null,
                "The deployed agreement-classify prompt has no registry placeholder.");
        }

        var input = ToElement(new { documentText = request.DocumentText });
        var output = await RunAsync(materialized, consumerType, input, request.TenantId, request.SubjectId, cancellationToken)
            .ConfigureAwait(false);

        AgreementClassifyResult result;
        try
        {
            result = ParseClassify(output, registry);
        }
        catch (AgreementOutputRefusal ex)
        {
            throw Fail(ex.ReasonCode, consumerType, request.SubjectId, innerException: null,
                $"The output for consumer type '{consumerType}' is invalid at '{ex.Path}'.", ex.Path);
        }

        _logger.LogInformation(
            "Agreement classified: subjectId={SubjectId} isAgreement={IsAgreement} candidates={CandidateCount} composite={Composite}",
            request.SubjectId, result.IsAgreement, result.Candidates.Count, result.Composite);
        return result;
    }

    /// <inheritdoc />
    public async Task<AgreementEditPlan> BuildEditPlanAsync(
        AgreementEditPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SegmentationJson);

        // A malformed segmentation is a caller bug (ArgumentException, no document text in the message), checked before any Action runs.
        var segmentation = AgreementSegmentation.Parse(request.SegmentationJson);

        const string consumerType = ConsumerTypes.AgreementEditPlan;
        var action = await ResolveAsync(consumerType, request.SubjectId, cancellationToken).ConfigureAwait(false);

        var input = ToElement(new
        {
            has_tracked_changes = segmentation.HasTrackedChanges,
            has_comments = segmentation.HasComments,
            has_hidden_text = segmentation.HasHiddenText,
            sections = segmentation.Sections.Select(s => new { index = s.Index, number = s.Number, heading = s.Heading, para_key = s.ParaKey, text = s.Text }),
            standard = new
            {
                name = PinnedNdaStandard.Name,
                version = PinnedNdaStandard.Version,
                scope = PinnedNdaStandard.Scope,
            },
            topics = PinnedNdaStandard.Topics.Select(t => new { id = t.Id, position = t.Position }),
        });

        var output = await RunAsync(action, consumerType, input, request.TenantId, request.SubjectId, cancellationToken)
            .ConfigureAwait(false);

        AgreementEditPlan plan;
        try
        {
            plan = AgreementEditPlanAssembler.Assemble(output, segmentation, request, request.ReviewedAt ?? _timeProvider.GetUtcNow());
        }
        catch (AgreementOutputRefusal ex)
        {
            throw Fail(ex.ReasonCode, consumerType, request.SubjectId, innerException: null,
                $"The edit plan for consumer type '{consumerType}' was refused at '{ex.Path}'.", ex.Path);
        }

        _logger.LogInformation(
            "Agreement edit plan built: subjectId={SubjectId} standardVersion={StandardVersion} edits={EditCount} topics={TopicCount} refused={RefusedCount} unplaceable={UnplaceableCount}",
            request.SubjectId, plan.StandardVersion, plan.EditCount, plan.TopicIds.Count, plan.RefusedEdits.Count, plan.UnplaceableFindings.Count);
        return plan;
    }

    // ── Output parsing ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strict parse of the classifier's closed contract <c>{isAgreement, candidates[{subDomainKey, confidence}], composite, reasoning}</c>.
    /// A missing member, a wrong JSON kind, a confidence outside 0 to 1, a key outside the registry, or a verdict that contradicts
    /// itself (a non-agreement with candidates, an agreement with none) is refused.
    /// </summary>
    private static AgreementClassifyResult ParseClassify(JsonElement output, IReadOnlyList<AgreementTypeRow> registry)
    {
        if (output.ValueKind != JsonValueKind.Object)
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, "$");
        }

        var isAgreement = RequireBool(output, "isAgreement");
        var composite = RequireBool(output, "composite");
        if (!output.TryGetProperty("candidates", out var candidatesElement) || candidatesElement.ValueKind != JsonValueKind.Array)
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, "$.candidates");
        }

        var reasoning = output.TryGetProperty("reasoning", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String
            ? ReadString(reasoningElement, "$.reasoning")
            : throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, "$.reasoning");

        var keys = registry.ToDictionary(r => r.Key, r => r.Key, StringComparer.OrdinalIgnoreCase);
        var candidates = new List<AgreementCandidate>();
        var i = 0;
        foreach (var item in candidatesElement.EnumerateArray())
        {
            var path = $"$.candidates[{i++}]";
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("subDomainKey", out var keyElement)
                || keyElement.ValueKind != JsonValueKind.String
                || !keys.TryGetValue(ReadString(keyElement, $"{path}.subDomainKey"), out var registryKey))
            {
                // Includes a key the model invented: the registry is the closed set.
                throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.subDomainKey");
            }

            if (!item.TryGetProperty("confidence", out var confidenceElement)
                || confidenceElement.ValueKind != JsonValueKind.Number
                || !confidenceElement.TryGetDouble(out var confidence)
                || confidence is < 0.0 or > 1.0)
            {
                throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.confidence");
            }

            candidates.Add(new AgreementCandidate(registryKey, confidence));
        }

        if (isAgreement ? candidates.Count == 0 : (candidates.Count > 0 || composite))
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, "$.candidates");
        }

        return new AgreementClassifyResult
        {
            IsAgreement = isAgreement,
            Candidates = candidates.OrderByDescending(c => c.Confidence).ToList(),
            Composite = composite,
            Reasoning = reasoning,
        };
    }

    /// <summary>
    /// A JSON string's value. An escaped lone surrogate (<c>\uD800</c>) is valid JSON that <see cref="JsonElement.GetString"/> cannot
    /// decode (it throws <see cref="InvalidOperationException"/>): that is <c>output_invalid</c> at this path, never an untyped error.
    /// </summary>
    private static string ReadString(JsonElement value, string path)
    {
        try
        {
            return value.GetString() ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, path);
        }
    }

    private static bool RequireBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"$.{name}");

    // ── Resolve and run ─────────────────────────────────────────────────────────────────────────

    private async Task<AnalysisAction> ResolveAsync(string consumerType, Guid? subjectId, CancellationToken cancellationToken)
    {
        try
        {
            return await _actionResolver.ResolveAsync(consumerType, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPassThrough(ex, cancellationToken))
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // IActionResolver's documented contract: InvalidOperationException = no Action routed for the consumer type, or the
            // routed Action row does not exist. Fail closed; there is no hard-coded fallback. The routing read also reports a
            // transient read failure this way (platform issue #1936), so callers retry a bounded number of times.
            throw Fail(AgreementAiException.BindingMissing, consumerType, subjectId, ex,
                $"No Binding / Action is configured for consumer type '{consumerType}'.");
        }
        catch (Exception ex)
        {
            throw Fail(AgreementAiException.CompletionFailed, consumerType, subjectId, ex,
                $"Resolving the Action for consumer type '{consumerType}' failed.");
        }
    }

    private async Task<JsonElement> RunAsync(
        AnalysisAction action,
        string consumerType,
        JsonElement input,
        string? tenantId,
        Guid? subjectId,
        CancellationToken cancellationToken)
    {
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
            throw Fail(AgreementAiException.CompletionFailed, consumerType, subjectId, ex,
                $"The Action for consumer type '{consumerType}' failed to complete.");
        }
    }

    /// <summary>Caller cancellation and the AI kill switch are not capability failures: rethrow as-is.</summary>
    private static bool IsPassThrough(Exception ex, CancellationToken cancellationToken) =>
        ex is FeatureDisabledException
        || (ex is OperationCanceledException && cancellationToken.IsCancellationRequested);

    private AgreementAiException Fail(
        string reasonCode,
        string consumerType,
        Guid? subjectId,
        Exception? innerException,
        string message,
        string? outputPath = null)
    {
        // ADR-015: identifiers, codes and JSON paths only. The inner exception is NOT logged (its message could carry model
        // output); it travels on the thrown exception for the caller to handle.
        _logger.LogWarning(
            "Agreement AI capability failed: consumerType={ConsumerType} reasonCode={ReasonCode} subjectId={SubjectId} " +
            "cause={CauseType} outputPath={OutputPath}",
            consumerType, reasonCode, subjectId, innerException?.GetType().Name, outputPath);
        return new AgreementAiException(reasonCode, consumerType, message, innerException);
    }

    private static JsonElement ToElement<T>(T payload) => JsonSerializer.SerializeToElement(payload);
}
