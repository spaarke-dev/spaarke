using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using static Sprk.Bff.Api.Services.Ai.Chat.PlaybookDispatcher;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Hybrid LLM intent reranker (chat-routing-redesign-r1 FR-46, task 111R).
/// Uses Azure OpenAI gpt-4o-mini with structured output (JSON schema) to
/// pick the best 3 candidates from a top-5 input, applies the FR-46
/// graceful-degrade rules on timeout / parse failure / LLM error, and emits
/// ADR-015 tier-1 telemetry (counts + latency only).
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary (ADR-013)</b>: lives in <c>Services/Ai/Chat/</c>; NOT part
/// of the <c>Services/Ai/PublicContracts/</c> facade.
/// </para>
/// <para>
/// <b>ADR-015 tier-1</b>: the prompt body contains the verbatim user
/// message, attachment metadata (filename / contentType / textLength
/// only), and candidate metadata (PlaybookId / PlaybookName / Confidence).
/// File text content, file binary, and embedding vectors are NEVER
/// included. Log lines emit counts + latency only — no user message text,
/// no LLM response text.
/// </para>
/// </remarks>
public sealed class IntentRerankerService : IIntentRerankerService
{
    private readonly IChatClient _chatClient;
    private readonly IOptions<IntentRerankerOptions> _options;
    private readonly ILogger<IntentRerankerService> _logger;

    /// <summary>
    /// JSON-schema document constraining the LLM output to the expected
    /// shape: <c>{ "top3": [{ "playbookId": uuid, "reason": string }, ...] }</c>
    /// with <c>minItems=1, maxItems=3, additionalProperties=false</c>.
    /// </summary>
    /// <remarks>
    /// Inlined as a static field rather than a separate file because it is
    /// service-private and the schema mirrors the
    /// <see cref="RerankSchemaResponse"/> deserialization shape immediately
    /// below. Keeping them adjacent makes drift between the two
    /// immediately visible at review time.
    /// </remarks>
    private static readonly JsonElement RerankResponseSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "top3": {
              "type": "array",
              "minItems": 1,
              "maxItems": 3,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "playbookId": { "type": "string" },
                  "reason":     { "type": "string" }
                },
                "required": ["playbookId", "reason"]
              }
            }
          },
          "required": ["top3"]
        }
        """).RootElement;

    private const string SchemaName = "IntentRerankerResult";
    private const string SchemaDescription =
        "Top-3 reranked playbook candidates selected from the input top-5 list.";

    /// <summary>
    /// JSON deserialization options for the rerank LLM response. Case
    /// insensitive to be defensive against gpt-4o-mini case drift.
    /// </summary>
    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Per-candidate-reason max length. Defensive cap to avoid unbounded
    /// LLM output bloating downstream telemetry / UI payloads.
    /// </summary>
    private const int MaxReasonLength = 240;

    /// <summary>
    /// Initialises a new instance of <see cref="IntentRerankerService"/>.
    /// </summary>
    /// <param name="chatClient">
    /// The Azure OpenAI <see cref="IChatClient"/> registered by
    /// <c>AiModule.AddChatClient</c> with UseFunctionInvocation. Note: the
    /// rerank LLM call uses no tools — UseFunctionInvocation is harmless
    /// here. A future refinement may bind a separate keyed client to
    /// <see cref="IntentRerankerOptions.ModelDeploymentName"/>.
    /// </param>
    /// <param name="options">Typed FR-46 rerank options (ADR-018).</param>
    /// <param name="logger">Logger instance.</param>
    public IntentRerankerService(
        IChatClient chatClient,
        IOptions<IntentRerankerOptions> options,
        ILogger<IntentRerankerService> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IntentRerankerResult> RerankAsync(
        IntentRerankerInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sw = Stopwatch.StartNew();
        var opts = _options.Value;

        // === Step 1: empty input → no-LLM short-circuit ===
        if (input.Top5Candidates.Count == 0)
        {
            sw.Stop();
            _logger.LogInformation(
                "IntentRerankerService: no input candidates — returning empty top-3 (latencyMs={LatencyMs})",
                sw.ElapsedMilliseconds);
            return new IntentRerankerResult(
                Top3: Array.Empty<RankedPlaybookCandidate>(),
                RerankInvoked: true,
                Reason: "no-input-candidates",
                LatencyMs: sw.Elapsed);
        }

        // === Step 2: compose linked CTS with FR-46 hard timeout ===
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(opts.TimeoutMs));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        // === Step 3: build the metadata-only prompt (ADR-015 tier-1) ===
        var messages = BuildPrompt(input);

        var chatOptions = new ChatOptions
        {
            Temperature = (float)opts.Temperature,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                RerankResponseSchema,
                schemaName: SchemaName,
                schemaDescription: SchemaDescription),
        };

        // === Step 4: invoke the LLM with try/catch graceful-degrade ===
        ChatResponse? response;
        try
        {
            response = await _chatClient
                .GetResponseAsync(messages, chatOptions, linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The CALLER may have cancelled too — either case graceful-degrades to the
            // same `timeout-graceful-degrade` reason per FR-46. The conditional that
            // previously distinguished the two cases collapsed to a single value during
            // the FR-46 wording sweep; the inline ternary was retained as dead code and
            // simplified during task 147 code review (2026-06-25).
            sw.Stop();
            const string reason = "timeout-graceful-degrade";
            _logger.LogWarning(
                "IntentRerankerService: rerank timed out after {LatencyMs}ms (budget={BudgetMs}ms, " +
                "candidateCount={CandidateCount}) — falling back to top-3-by-confidence",
                sw.ElapsedMilliseconds, opts.TimeoutMs, input.Top5Candidates.Count);
            return BuildFallback(input, reason, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "IntentRerankerService: rerank LLM call failed after {LatencyMs}ms " +
                "(candidateCount={CandidateCount}) — falling back to top-3-by-confidence",
                sw.ElapsedMilliseconds, input.Top5Candidates.Count);
            return BuildFallback(input, "llm-error-graceful-degrade", sw.Elapsed);
        }

        // === Step 5: parse + validate the LLM response ===
        var responseText = response?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(responseText))
        {
            sw.Stop();
            _logger.LogWarning(
                "IntentRerankerService: rerank LLM returned empty response (latencyMs={LatencyMs}, " +
                "candidateCount={CandidateCount}) — falling back to top-3-by-confidence",
                sw.ElapsedMilliseconds, input.Top5Candidates.Count);
            return BuildFallback(input, "parse-error-graceful-degrade", sw.Elapsed);
        }

        RerankSchemaResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RerankSchemaResponse>(responseText, LlmJsonOptions);
        }
        catch (JsonException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "IntentRerankerService: rerank LLM response failed JSON parse (latencyMs={LatencyMs}, " +
                "candidateCount={CandidateCount}) — falling back to top-3-by-confidence",
                sw.ElapsedMilliseconds, input.Top5Candidates.Count);
            return BuildFallback(input, "parse-error-graceful-degrade", sw.Elapsed);
        }

        if (parsed is null || parsed.Top3 is null || parsed.Top3.Count == 0)
        {
            sw.Stop();
            _logger.LogWarning(
                "IntentRerankerService: rerank LLM response missing top3 (latencyMs={LatencyMs}, " +
                "candidateCount={CandidateCount}) — falling back to top-3-by-confidence",
                sw.ElapsedMilliseconds, input.Top5Candidates.Count);
            return BuildFallback(input, "parse-error-graceful-degrade", sw.Elapsed);
        }

        // === Step 6: lookup returned playbookIds in the input top-5 ===
        //
        // Build a case-insensitive lookup map from the input candidates so the
        // LLM can return playbookIds with any GUID-case formatting. Drop any
        // playbookId the LLM returned that does not appear in the input list
        // (LLM hallucination defence).
        var inputById = new Dictionary<string, PlaybookCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in input.Top5Candidates)
        {
            // First-wins on duplicates (defensive — input top-5 should never
            // contain duplicate IDs, but coding it this way is harmless).
            inputById.TryAdd(c.PlaybookId, c);
        }

        var ranked = new List<RankedPlaybookCandidate>(capacity: parsed.Top3.Count);
        int droppedCount = 0;
        foreach (var entry in parsed.Top3)
        {
            if (string.IsNullOrWhiteSpace(entry.PlaybookId) ||
                !inputById.TryGetValue(entry.PlaybookId, out var candidate))
            {
                droppedCount++;
                continue;
            }

            var reasonText = TruncateReason(entry.Reason);
            ranked.Add(new RankedPlaybookCandidate(candidate, reasonText));
        }

        if (droppedCount > 0)
        {
            // ADR-015 tier-1: log the count of dropped entries, NOT their content.
            _logger.LogWarning(
                "IntentRerankerService: dropped {DroppedCount} LLM-returned playbookId(s) not in input top-5 " +
                "(candidateCount={CandidateCount})",
                droppedCount, input.Top5Candidates.Count);
        }

        // If the LLM hallucinated everything, fall back rather than return empty.
        if (ranked.Count == 0)
        {
            sw.Stop();
            _logger.LogWarning(
                "IntentRerankerService: all LLM-returned playbookIds were unknown (latencyMs={LatencyMs}, " +
                "candidateCount={CandidateCount}) — falling back to top-3-by-confidence",
                sw.ElapsedMilliseconds, input.Top5Candidates.Count);
            return BuildFallback(input, "parse-error-graceful-degrade", sw.Elapsed);
        }

        // === Step 7: cap to 3 + classify reason ===
        var truncated = ranked.Count > 3;
        if (ranked.Count > 3)
        {
            ranked = ranked.Take(3).ToList();
        }

        var resultReason = truncated
            ? "llm-rerank-truncated"
            : (parsed.Top3.Count < 3 || ranked.Count < 3)
                ? "llm-rerank-partial"
                : "llm-rerank-from-5";

        sw.Stop();

        // === Step 8: tier-1 telemetry — counts + latency only ===
        _logger.LogInformation(
            "IntentRerankerService: rerank complete (candidateCount={CandidateCount}, " +
            "returnedCount={ReturnedCount}, latencyMs={LatencyMs}, rerankInvoked=true, reason={Reason})",
            input.Top5Candidates.Count, ranked.Count, sw.ElapsedMilliseconds, resultReason);

        return new IntentRerankerResult(
            Top3: ranked,
            RerankInvoked: true,
            Reason: resultReason,
            LatencyMs: sw.Elapsed);
    }

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the rerank prompt — metadata-only per ADR-015 tier-1.
    /// </summary>
    /// <remarks>
    /// Composition: system prompt describing the rerank task + user prompt
    /// containing the user message (verbatim — user's own input is
    /// permitted), the attachment metadata block, and the candidate list.
    /// No file body, no binary data, no embedding vectors.
    /// </remarks>
    private static List<ChatMessage> BuildPrompt(IntentRerankerInput input)
    {
        const string systemPrompt = """
            You are a playbook reranker. Given a user message, attachment metadata
            (filename, content-type, character-count only — no file content), and a
            list of 5 candidate playbooks (each with playbookId, name, and
            confidence score), select the 3 candidates that best match the user's
            intent. Return JSON only conforming to the supplied schema.

            For each chosen candidate provide a short (<240 char) reason explaining
            the match. The playbookId in each returned entry MUST be one of the
            playbookIds from the supplied candidate list — do not invent IDs.
            Return at most 3 candidates; return fewer only when fewer than 3 are
            plausibly relevant.
            """;

        var userPrompt = new StringBuilder(capacity: 2048);
        userPrompt.Append("User message: \"").Append(input.UserMessage).Append("\"\n\n");

        userPrompt.Append("Attachments (").Append(input.AttachmentMetadata.Count).Append("):\n");
        if (input.AttachmentMetadata.Count == 0)
        {
            userPrompt.Append("  (none)\n");
        }
        else
        {
            for (var i = 0; i < input.AttachmentMetadata.Count; i++)
            {
                var a = input.AttachmentMetadata[i];
                userPrompt.Append("  ").Append(i + 1).Append(". filename=\"").Append(a.Filename)
                    .Append("\" contentType=\"").Append(a.ContentType)
                    .Append("\" textLength=").Append(a.TextLength).Append('\n');
            }
        }

        userPrompt.Append("\nCandidates (").Append(input.Top5Candidates.Count).Append("):\n");
        for (var i = 0; i < input.Top5Candidates.Count; i++)
        {
            var c = input.Top5Candidates[i];
            userPrompt.Append("  ").Append(i + 1)
                .Append(". playbookId=\"").Append(c.PlaybookId)
                .Append("\" name=\"").Append(c.PlaybookName)
                .Append("\" confidence=").Append(c.Confidence.ToString("F3"))
                .Append('\n');
        }

        return new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt.ToString()),
        };
    }

    /// <summary>
    /// Constructs the graceful-degrade fallback result by taking the input
    /// top-3 by confidence. Reason field on each candidate is the literal
    /// <c>"fallback"</c> string — no LLM reason is available.
    /// </summary>
    private static IntentRerankerResult BuildFallback(
        IntentRerankerInput input,
        string reason,
        TimeSpan latency)
    {
        // Input top-5 may already be ordered by confidence DESC from 113R,
        // but sort defensively so a future caller passing an unsorted list
        // still gets correct degraded output.
        var fallback = input.Top5Candidates
            .OrderByDescending(c => c.Confidence)
            .Take(3)
            .Select(c => new RankedPlaybookCandidate(c, "fallback"))
            .ToList();

        return new IntentRerankerResult(
            Top3: fallback,
            RerankInvoked: true,
            Reason: reason,
            LatencyMs: latency);
    }

    /// <summary>Truncates the LLM reason text to <see cref="MaxReasonLength"/> characters.</summary>
    private static string TruncateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        return reason.Length <= MaxReasonLength
            ? reason
            : reason[..MaxReasonLength];
    }

    /// <summary>
    /// Deserialization shape for the gpt-4o-mini JSON-schema response.
    /// Mirrors <see cref="RerankResponseSchema"/> — drift between the two
    /// would surface as a parse failure and trigger graceful-degrade.
    /// </summary>
    private sealed class RerankSchemaResponse
    {
        public List<RerankSchemaEntry>? Top3 { get; set; }
    }

    private sealed class RerankSchemaEntry
    {
        public string? PlaybookId { get; set; }
        public string? Reason { get; set; }
    }
}
