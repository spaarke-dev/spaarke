using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Api.Insights;

/// <summary>
/// Public-facing tenant-user endpoint for the Spaarke Insights Engine (D-P15, task 061).
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 — this file imports
/// <see cref="IInsightsAi"/> (the only Zone-A surface Zone B may consume) plus the
/// Zone B POCOs (<see cref="InsightArtifact"/>, <see cref="DeclineResponse"/>,
/// <see cref="InsightAskRequest"/>, <see cref="InsightsAgentRequest"/>,
/// <see cref="InsightsAgentResult"/>). The §3.5.4 forbidden-imports grep is asserted
/// against <c>Api/Insights/</c> + <c>Models/Insights/</c> before merge.
/// </para>
/// <para>
/// <b>Endpoints</b>:
/// <list type="bullet">
///   <item><c>POST /api/insights/ask</c> — synthesize an Insights-mode answer
///   (Inference) or return a structured <see cref="DeclineResponse"/> per D-49.</item>
/// </list>
/// </para>
/// <para>
/// <b>Auth model</b>: <see cref="Microsoft.AspNetCore.Builder.AuthorizationEndpointConventionBuilderExtensions.RequireAuthorization{TBuilder}(TBuilder)"/>
/// only — any authenticated tenant user. The handler reads <c>tid</c> and <c>oid</c>
/// claims from <see cref="HttpContext.User"/>. Missing <c>tid</c> → 401 ProblemDetails
/// (token is invalid for Insights Engine purposes); missing <c>oid</c> → 401
/// ProblemDetails. There is NO role gate — Insights synthesis is a tenant-user
/// capability (per D-P15 task POML "regular tenant user, not admin role"). There IS a
/// record gate (unified-access-control-r2 task 163; owner round 16 items 1 and 3): the route
/// filter runs only a playbook registered as an insights-ask Binding, applies the SHARED
/// playbook-parameter policy, and authorizes the subject matter AS THE CALLER — Read for a
/// playbook that cannot write, Write for one that can (matter-health-single persists to
/// <c>sprk_matter.sprk_performancesummary</c>) — plus each record parameter. A denied, absent
/// or unverifiable matter gets the uniform 404.
/// </para>
/// <para>
/// <b>Zone B note</b>: besides <see cref="IInsightsAi"/>, this file uses two non-engine surfaces
/// for that gate: <c>Api.Filters</c> (the route-filter precedent) and the pure
/// <see cref="PlaybookParameterPolicy"/> class (no AI client, no engine — the §3.5.4 grep list
/// is unaffected).
/// </para>
/// <para>
/// <b>Rate limit</b>: <c>ai-context</c> policy (60 requests/minute sliding window per
/// caller <c>oid</c>) per ADR-016. Matches the task POML target of "60 req/min per
/// caller" and is semantically appropriate — Insights synthesis is read-heavy context
/// resolution. Exceeded limit returns 429 ProblemDetails with <c>Retry-After</c>
/// header (centralised in <c>RateLimitingModule.OnRejected</c>).
/// </para>
/// <para>
/// <b>Wire response shape</b>: both success and decline return 200 OK with body
/// <see cref="InsightAskResponse"/>. Decline is NOT an error — the playbook executed
/// successfully and produced a structured insufficient-evidence response per D-49.
/// ADR-019 ProblemDetails is reserved for true failures (400 validation, 401/403
/// auth, 429 rate limit, 500 internal error).
/// </para>
/// </remarks>
public static class InsightEndpoints
{
    /// <summary>
    /// Registers the <c>POST /api/insights/ask</c> route on the supplied endpoint
    /// route builder. Called from
    /// <c>EndpointMappingExtensions.MapDomainEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapInsightsAskEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/insights")
            .RequireAuthorization()
            .RequireRateLimiting("ai-context")
            .WithTags("Insights");

        // Authorization (unified-access-control-r2 task 163, sweep finding #17; owner round 16 items 1 and 3,
        // round 25 item 3): before IInsightsAi or the playbook cache is reached, the route filter resolves the
        // REGISTERED playbook, applies the shared playbook-parameter policy, and asks Dataverse AS THE CALLER for
        // Read on the subject matter — Write when a node that can write reaches it (e.g. matter-health-single's
        // UpdateRecord onto sprk_matter.sprk_performancesummary; "Read suffices only for non-persisting playbooks")
        // — and for each record parameter's right. Any denial, an absent matter and a fault are the identical
        // uniform 404.
        group.MapPost("/ask", Ask)
            .AddInsightsAskAuthorizationFilter()
            .WithName("AskInsights")
            .WithSummary("Synthesize an Insights-mode answer or return a structured decline (D-P15)")
            .WithDescription(
                "Accepts {question, subject, parameters} and routes through IInsightsAi.AnswerQuestionAsync. " +
                "Returns 200 OK with an Inference InsightArtifact on success, OR 200 OK with a structured " +
                "DeclineResponse (D-49) when evidence is insufficient. Both branches share the " +
                "InsightAskResponse envelope shape ({artifact, decline}). Observability headers " +
                "X-Insights-Cache and X-Insights-Elapsed-Ms reflect the D-P13 cache outcome and " +
                "orchestrator-measured wall time. Per SPEC §3.5 Zone B placement — the endpoint " +
                "consumes IInsightsAi only.")
            .Produces<InsightAskResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// What the <c>/ask</c> route filter decided, published for the handler: the ONE playbook it authorized the run
    /// of (the handler runs exactly this one — no second resolution that could pick another), and whether it
    /// established the caller's Write on the subject (<see cref="InsightsAgentRequest.SubjectWriteAuthorized"/>).
    /// </summary>
    internal sealed record AuthorizedAskRun(Guid PlaybookId, bool SubjectWriteAuthorized);

    /// <summary>The <see cref="HttpContext.Items"/> key under which the route filter publishes <see cref="AuthorizedAskRun"/>.</summary>
    private const string AuthorizedAskRunItemKey = "Sprk.InsightEndpoints.AuthorizedAskRun";

    /// <summary>
    /// The route filter of <c>POST /api/insights/ask</c> (unified-access-control-r2 task 163; owner round 16 items 1
    /// and 3, round 25 item 3). In order, so every 400 is returned before any rights query:
    /// <list type="number">
    ///   <item>no caller oid → 401 (the declaration filter's own body);</item>
    ///   <item>body, <c>question</c>, <c>subject</c> and the <c>matter:{guid}</c> subject → the handler's 400s;</item>
    ///   <item><c>parameters</c> → task 164's SHARED playbook-parameter policy
    ///   (<see cref="PlaybookParameterPolicy.Evaluate"/>; its 400 is <see cref="PlaybookAuthorizationFilter.ParameterRejected"/>,
    ///   the same body <c>/execute</c> and <c>/run-playbook</c> answer). Not forked: server-owned keys
    ///   (<c>userId</c>, <c>tenantId</c>, <c>run.*</c>, …), non-GUID record ids, mistyped tuning keys, a GUID on a text
    ///   key and every undeclared key are refused;</item>
    ///   <item><c>question</c> → the playbook, only through an enabled insights-ask Binding (a canonical name by its exact
    ///   consumer code; a raw GUID only when that Binding binds it) — otherwise the "not registered" 400;</item>
    ///   <item>the rights, AS THE CALLER, through <see cref="PlaybookAuthorizationFilter.BuildSubjectRunChecksAsync"/> and
    ///   the ONE per-route evaluator (<see cref="FinanceAuthorizationFilter"/>): Read on <c>sprk_matters(subject)</c> —
    ///   Write when a node that can write reaches the subject (through <c>{{matterId}}</c>), or the playbook has no
    ///   nodes — and each record parameter's right by the same rule. Every denial, an absent matter and a fault are
    ///   the identical uniform 404.</item>
    /// </list>
    /// The decision is published as <see cref="AuthorizedAskRun"/>; the handler refuses to run without it.
    /// </summary>
    internal static RouteHandlerBuilder AddInsightsAskAuthorizationFilter(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(AuthorizeAskAsync);

    private static async ValueTask<object?> AuthorizeAskAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;
        var logger = services.GetService<ILogger<InsightAskRequest>>();
        var ct = httpContext.RequestAborted;

        // 1. Identity first — the same 401 the declaration filter answers; it does not depend on any record.
        var callerOid = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(callerOid))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // 2. The handler's own input 400s, before any Dataverse call.
        var request = context.Arguments.OfType<InsightAskRequest>().FirstOrDefault();
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest("'question' is required and cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return BadRequest("'subject' is required and cannot be empty.");
        }

        if (!TryParseMatterSubject(request.Subject, out var subjectMatterId, out var subjectError))
        {
            return BadRequest(subjectError);
        }

        // 3. The shared playbook-parameter policy (task 164) — syntax only, no rights query, so never an oracle.
        var parameterEvaluation = PlaybookParameterPolicy.Evaluate(request.Parameters);
        if (!parameterEvaluation.IsValid)
        {
            logger?.LogWarning(
                "[INSIGHTS-ASK] parameter {Key} refused by the shared playbook-parameter policy ({Reason}) for caller {CallerOid}",
                parameterEvaluation.RejectedKey, parameterEvaluation.Reason, callerOid);
            return PlaybookAuthorizationFilter.ParameterRejected(httpContext, parameterEvaluation);
        }

        // 4. The playbook — registered insights-ask Bindings only.
        var playbookId = await ResolveRegisteredPlaybookAsync(
            request.Question, services.GetRequiredService<IConsumerRoutingService>(), ct);
        if (playbookId is not { } authorizedPlaybookId)
        {
            return UnregisteredQuestion(request.Question);
        }

        // 5. The rights, as the caller — fail closed on any fault (ADR-003).
        IReadOnlyList<FinanceAuthorizationCheck> checks;
        try
        {
            if (!EntityAccessFilter.TryResolveEntitySet("matter", out var matterEntitySet))
            {
                throw new InvalidOperationException("The shared entity-set map has no 'matter' entry.");
            }

            checks = await PlaybookAuthorizationFilter.BuildSubjectRunChecksAsync(
                services, authorizedPlaybookId, matterEntitySet, subjectMatterId, MatterSubjectParameter,
                parameterEvaluation.RecordParameters, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "[INSIGHTS-ASK] the run's rights could not be decided for playbook {PlaybookId} and caller {CallerOid}; denying (fail closed)",
                authorizedPlaybookId, callerOid);
            return FinanceAuthorizationFilter.UniformRecordNotFound(httpContext);
        }

        httpContext.Items[AuthorizedAskRunItemKey] = new AuthorizedAskRun(
            authorizedPlaybookId,
            SubjectWriteAuthorized: string.Equals(checks[0].Operation, "write", StringComparison.Ordinal));

        var evaluator = new FinanceAuthorizationFilter(
            services.GetRequiredService<AuthorizationService>(),
            _ => FinanceAuthorizationTargets.Authorize(checks.ToArray()),
            FinanceDenial.UniformNotFound,
            services.GetService<CallerRecordAccessProbe>());

        return await evaluator.InvokeAsync(context, next);
    }

    /// <summary>
    /// The registered playbook a <c>question</c> names, or <c>null</c>. Two inputs are accepted, and both read the ONE
    /// routing surface (enabled <c>sprk_playbookconsumer</c> Binding rows of consumer type insights-ask, FR-P3-01 /
    /// ADR-039): a canonical name, resolved by its EXACT consumer code (a default-row fallback means "not
    /// registered"); or a raw playbook GUID, accepted only when the Binding that targets it is an insights-ask one
    /// (task 163 — a raw GUID used to run ANY playbook).
    /// </summary>
    private static async Task<Guid?> ResolveRegisteredPlaybookAsync(
        string question, IConsumerRoutingService consumerRouting, CancellationToken ct)
    {
        if (Guid.TryParse(question, out var rawPlaybookId) && rawPlaybookId != Guid.Empty)
        {
            var boundAs = await consumerRouting.GetBindingByPlaybookIdAsync(rawPlaybookId, cancellationToken: ct);
            return boundAs is not null
                && string.Equals(boundAs.ConsumerType, ConsumerTypes.InsightsAsk, StringComparison.OrdinalIgnoreCase)
                    ? rawPlaybookId
                    : null;
        }

        var binding = await consumerRouting.ResolveBindingAsync(
            ConsumerTypes.InsightsAsk, consumerCode: question, cancellationToken: ct);

        // Null/empty ConsumerCode is treated as "default" by the resolution algorithm — normalize before comparing so a
        // null-code default row behaves identically to a literal "default" row (same as AssistantToolCallHandler).
        return binding is not null
            && string.Equals(
                string.IsNullOrWhiteSpace(binding.ConsumerCode) ? "default" : binding.ConsumerCode,
                question,
                StringComparison.OrdinalIgnoreCase)
            && binding.PlaybookId is { } boundPlaybookId
            && boundPlaybookId != Guid.Empty
                ? boundPlaybookId
                : null;
    }

    /// <summary>The 400 for a <c>question</c> that names no registered insights-ask playbook.</summary>
    private static IResult UnregisteredQuestion(string question) =>
        BadRequest(
            "'question' must be either a valid playbook Guid id OR a canonical name " +
            "registered as an enabled sprk_playbookconsumer row (consumerType " +
            $"'{ConsumerTypes.InsightsAsk}', sprk_consumercode = the canonical name). " +
            $"Received: '{question}'.");

    /// <summary>
    /// Parses the Phase 1 <c>matter:{guid}</c> subject. The error strings for a wrong scheme and an empty id are
    /// the ones the handler has always returned; a non-GUID id is new with task 163 (it used to reach the
    /// facade unparsed).
    /// </summary>
    private static bool TryParseMatterSubject(string subject, out Guid matterId, out string error)
    {
        matterId = Guid.Empty;
        error = string.Empty;

        if (!subject.StartsWith(MatterSubjectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = $"'subject' must begin with '{MatterSubjectPrefix}' in Phase 1 " +
                    "(e.g., 'matter:{id}'). Other schemes are not yet supported.";
            return false;
        }

        var raw = subject.Substring(MatterSubjectPrefix.Length).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            error = $"'subject' is missing an identifier after '{MatterSubjectPrefix}'.";
            return false;
        }

        if (!Guid.TryParse(raw, out matterId) || matterId == Guid.Empty)
        {
            error = $"'subject' must be '{MatterSubjectPrefix}' followed by a matter id (a non-empty GUID).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// HTTP response header carrying the D-P13 cache outcome
    /// (<c>true</c> on hit, <c>false</c> on miss).
    /// </summary>
    private const string CacheHeader = "X-Insights-Cache";

    /// <summary>
    /// HTTP response header carrying the orchestrator-measured wall time in milliseconds.
    /// </summary>
    private const string ElapsedHeader = "X-Insights-Elapsed-Ms";

    /// <summary>
    /// Phase 1 accepted subject scheme — only <c>matter:</c> is supported per task POML
    /// guidance. Other schemes return 400 ProblemDetails.
    /// </summary>
    private const string MatterSubjectPrefix = "matter:";

    /// <summary>
    /// The playbook parameter a <c>matter:{id}</c> subject is bound to — the Insights orchestrator sets
    /// <c>matterId</c> from the subject (<c>InsightsOrchestrator.EnrichParametersFromSubject</c>), so the nodes reach the
    /// subject through it, and the route asks for Write on the subject when a node that can write references it.
    /// </summary>
    private const string MatterSubjectParameter = "matterId";

    /// <summary>
    /// Default <c>topic</c> dimension for the InsightSummaryCard widget invocation
    /// telemetry path. r1 Matter Health is the only registered topic in
    /// <c>sprk_aitopicregistry</c> (per task 050 <see cref="InsightWidgetsTelemetry"/>
    /// ValidTopics enum + project spec FR-04). When future topics ship, this default
    /// becomes a per-request dimension derived from the topic registry — for r1 a single
    /// constant is sufficient and cardinality-safe.
    /// </summary>
    private const string DefaultTopic = "matter-health";

    /// <summary>
    /// Default <c>mode</c> dimension for the r1 widget invocation path. Matter Health
    /// ships as single-subject mode only; multi / cohort are framework-shaped for r2+.
    /// </summary>
    private const string DefaultMode = "single";

    /// <summary>
    /// POST /api/insights/ask
    /// </summary>
    private static async Task<IResult> Ask(
        [FromBody] InsightAskRequest? request,
        HttpContext httpContext,
        IInsightsAi insightsAi,
        InsightWidgetsTelemetry widgetTelemetry,
        ILogger<InsightAskRequest> logger,
        CancellationToken ct)
    {
        // ---------------------------------------------------------------
        // Validation — ADR-019 ProblemDetails for every error
        // ---------------------------------------------------------------
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest("'question' is required and cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return BadRequest("'subject' is required and cannot be empty.");
        }

        // Wave E2 (FR-05) forceMode plumbing. The /ask endpoint IS the canonical playbook
        // dispatcher — caller-declared "playbook" intent is consistent; "rag" intent is a
        // wrong-endpoint mismatch and gets rejected with 400 ProblemDetails so the caller
        // (or the future E3 Assistant) can re-dispatch to /api/insights/search. Null = no
        // override = normal playbook behavior. The classifier itself is not invoked from
        // this endpoint in E2; the field exists for forward-compat with E3 Spaarke Assistant.
        if (!string.IsNullOrWhiteSpace(request.ForceMode))
        {
            if (string.Equals(request.ForceMode, "rag", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    "'forceMode' is 'rag' but this endpoint serves the playbook path. " +
                    "Use POST /api/insights/search for RAG dispatch.");
            }
            if (!string.Equals(request.ForceMode, "playbook", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    $"'forceMode' must be either 'playbook' or 'rag' (received: '{request.ForceMode}'). " +
                    "Omit the field to use the default intent-classifier dispatch (Wave E3+).");
            }
        }

        // Phase 1 subject contract: matter:{guid}. The route filter has already parsed and authorized it.
        if (!TryParseMatterSubject(request.Subject, out var subjectMatterId, out var subjectError))
        {
            return BadRequest(subjectError);
        }

        // Task 163: run EXACTLY the playbook the route filter resolved and authorized (an enabled insights-ask
        // Binding; parameters through the shared policy; the caller's rights on the subject and every record
        // parameter decided as the caller). No second resolution here — one that could pick a different playbook
        // than the one the rights were decided for. Without the filter's decision nothing runs (fail closed).
        if (httpContext.Items.TryGetValue(AuthorizedAskRunItemKey, out var decided) is false
            || decided is not AuthorizedAskRun authorizedRun)
        {
            logger.LogError(
                "[INSIGHTS-ASK] no route-filter authorization decision on the request; denying (fail closed)");
            return FinanceAuthorizationFilter.UniformRecordNotFound(httpContext);
        }

        var playbookId = authorizedRun.PlaybookId;

        // ---------------------------------------------------------------
        // Auth context — derive tenantId + caller oid from claims.
        // ---------------------------------------------------------------
        // 'tid' is the Entra ID tenant claim (mapped from 'http://schemas.microsoft.com/identity/claims/tenantid'
        // by Microsoft Identity Web's default claim mapping settings; we check both for robustness).
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // The auth pipeline normally rejects tokens without 'tid', but we guard explicitly
            // so a malformed token reaching this handler produces a clean 401 ProblemDetails
            // rather than an opaque 500 when IInsightsAi rejects an empty TenantId.
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Tenant identity ('tid' claim) not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        var callerOid = CallerResolution.ResolveObjectId(httpContext.User);

        if (string.IsNullOrWhiteSpace(callerOid))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity ('oid' claim) not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // ---------------------------------------------------------------
        // AccessibleScopeHash (Phase 1) — derived from tenant + caller.
        // ---------------------------------------------------------------
        // Per DEP-3 (decisions.md): within-tenant access trimming source is open. Under D-52
        // single-tenant, tenant scope dominates; Phase 1.5 will refine when matter-scope
        // claims arrive on the principal. Hashing tenant + oid gives a stable cache-key
        // discriminator that invalidates per-caller — acceptable for Phase 1 acceptance.
        var accessibleScopeHash = ComputeAccessibleScopeHash(tenantId, callerOid);

        // ---------------------------------------------------------------
        // Delegate to IInsightsAi facade — Zone B → Zone A boundary crossing.
        // ---------------------------------------------------------------
        var facadeRequest = new InsightsAgentRequest(
            Question: playbookId,
            // Task 163: the CANONICAL form of the authorized id, not the raw string — so what the playbook
            // and the cache key see is exactly the record the route filter asked Dataverse about, whatever
            // GUID spelling (braces, case, no dashes) the caller sent.
            Subject: $"{MatterSubjectPrefix}{subjectMatterId}",
            Parameters: request.Parameters,
            TenantId: tenantId,
            AccessibleScopeHash: accessibleScopeHash)
        {
            // Task 163: the subject right the route filter established as the caller. When it is not Write, the facade
            // refuses a run that can write (the playbook gained a writing node after the filter decided).
            SubjectWriteAuthorized = authorizedRun.SubjectWriteAuthorized,
        };

        // ---------------------------------------------------------------
        // Widget telemetry (NFR-06 / task 051):
        //   - Activity span carries high-cardinality dims (subject GUID, correlationId,
        //     tenantId) per ADR-014/015 cardinality discipline (subject is span-only).
        //   - Stopwatch measures end-to-end (cache lookup + playbook + serialisation) and
        //     is the value recorded on the duration histogram. result.ProcessingTimeMs
        //     is orchestrator-internal and continues to surface on the X-Insights-Elapsed-Ms
        //     header for backward compatibility.
        //   - RecordInvocation emits the counter + histogram with the BOUNDED dim set
        //     (topic, mode, outcome, cacheHit, tenant.id). The outcome dim is set on each
        //     of the four exit paths (kill_switched / failed / success+cache_hit / success).
        // ---------------------------------------------------------------
        using var widgetActivity = widgetTelemetry.StartActivity(
            operationName: "InsightSummaryCard.Invoke",
            tenantId: tenantId,
            subject: request.Subject,
            correlationId: httpContext.TraceIdentifier);

        var widgetStopwatch = Stopwatch.StartNew();

        InsightsAgentResult result;
        try
        {
            result = await insightsAi.AnswerQuestionAsync(facadeRequest, ct);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled or request aborted — no widget event emitted (cancellation
            // is not an invocation outcome). Propagate so Kestrel records it accurately
            // rather than returning a synthetic 500.
            throw;
        }
        catch (FeatureDisabledException ex)
        {
            // ADR-018/032 kill-switch path. Record telemetry BEFORE returning 503 so the
            // kill_switched outcome shows up in App Insights for ops dashboards (spec NFR-06
            // outcome enum + task 050 RecordInvocation outcome dimension).
            widgetStopwatch.Stop();
            widgetTelemetry.RecordInvocation(
                topic: DefaultTopic,
                mode: DefaultMode,
                outcome: "kill_switched",
                cacheHit: false,
                durationMs: widgetStopwatch.Elapsed.TotalMilliseconds,
                tenantId: tenantId);

            logger.LogDebug(
                "[INSIGHTS-ASK] AI feature disabled. ErrorCode={ErrorCode} TenantId={TenantId} Subject={Subject}",
                ex.ErrorCode, tenantId, request.Subject);

            return ex.AsFeatureDisabled503();
        }
        catch (SdapProblemException ex) when (ex.Code == InsightsAgentRequest.SubjectWriteRequiredCode)
        {
            // Task 163: the facade's run guard refused a run that can write — the caller's Write on the subject was
            // not established. Nothing ran. The route's deny shape: the uniform 404 (no id, no reason).
            logger.LogWarning(
                "[INSIGHTS-ASK] run refused: playbook {PlaybookId} can write and the caller's Write on the subject was not established. TenantId={TenantId}",
                playbookId, tenantId);
            return FinanceAuthorizationFilter.UniformRecordNotFound(httpContext);
        }
        catch (ArgumentException ex)
        {
            // The facade contract throws ArgumentException for validation faults that
            // slipped past our pre-checks. NOT recorded as a widget invocation because
            // the playbook never ran (treat as 400 validation, not an invocation outcome).
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Per ADR-019: never leak document content / prompts / model output. Generic
            // 500 with correlation id; details are in the structured log.
            // Telemetry: emit failed outcome BEFORE returning so dashboards can track
            // invocation failure rate (spec NFR-06 outcome enum).
            widgetStopwatch.Stop();
            widgetTelemetry.RecordInvocation(
                topic: DefaultTopic,
                mode: DefaultMode,
                outcome: "failed",
                cacheHit: false,
                durationMs: widgetStopwatch.Elapsed.TotalMilliseconds,
                tenantId: tenantId);

            logger.LogError(ex,
                "[INSIGHTS-ASK] AnswerQuestionAsync failed for playbook {PlaybookId} subject {Subject} tenant {TenantId} caller {CallerOid}",
                playbookId, request.Subject, tenantId, callerOid);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to produce insight. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "INSIGHTS_INTERNAL_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        widgetStopwatch.Stop();

        // Successful playbook execution (Artifact OR Decline both count as success — the
        // playbook produced a structured result). cacheHit is a separate dimension; when
        // true, the spec NFR-06 outcome enum prefers "cache_hit" over "success" so cache
        // hit-rate dashboards work without joining two dimensions.
        widgetTelemetry.RecordInvocation(
            topic: DefaultTopic,
            mode: DefaultMode,
            outcome: result.CacheHit ? "cache_hit" : "success",
            cacheHit: result.CacheHit,
            durationMs: widgetStopwatch.Elapsed.TotalMilliseconds,
            tenantId: tenantId);

        // ---------------------------------------------------------------
        // Observability headers — set BEFORE writing the response body.
        // ---------------------------------------------------------------
        httpContext.Response.Headers[CacheHeader] = result.CacheHit ? "true" : "false";
        httpContext.Response.Headers[ElapsedHeader] = result.ProcessingTimeMs.ToString();

        // ---------------------------------------------------------------
        // Response — 200 OK for both branches; envelope discriminates.
        // ---------------------------------------------------------------
        if (result.Artifact is not null)
        {
            logger.LogInformation(
                "[INSIGHTS-ASK] Success playbook {PlaybookId} subject {Subject} tenant {TenantId} cacheHit={CacheHit} elapsedMs={ElapsedMs}",
                playbookId, request.Subject, tenantId, result.CacheHit, result.ProcessingTimeMs);

            return Results.Ok(new InsightAskResponse(Artifact: result.Artifact, Decline: null));
        }

        if (result.Decline is not null)
        {
            logger.LogInformation(
                "[INSIGHTS-ASK] Declined playbook {PlaybookId} subject {Subject} tenant {TenantId} reason={Reason} cacheHit={CacheHit} elapsedMs={ElapsedMs}",
                playbookId, request.Subject, tenantId, result.Decline.Reason, result.CacheHit, result.ProcessingTimeMs);

            return Results.Ok(new InsightAskResponse(Artifact: null, Decline: result.Decline));
        }

        // Defensive: IInsightsAi contract guarantees exactly one of Artifact/Decline is
        // populated. If both null, the facade impl is broken — surface as 500 so we don't
        // hide a contract violation behind an empty envelope.
        logger.LogError(
            "[INSIGHTS-ASK] IInsightsAi returned a result with neither Artifact nor Decline (contract violation). " +
            "playbook={PlaybookId} subject={Subject} tenant={TenantId}",
            playbookId, request.Subject, tenantId);

        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            detail: "Facade returned an empty result. See server logs.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = "INSIGHTS_FACADE_EMPTY_RESULT",
                ["correlationId"] = httpContext.TraceIdentifier
            });
    }

    /// <summary>
    /// 400 ProblemDetails helper to keep the handler readable.
    /// </summary>
    private static IResult BadRequest(string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

    /// <summary>
    /// Compute the Phase 1 <c>AccessibleScopeHash</c> from the tenant + caller oid.
    /// Returns a lowercase hex SHA-256 (64 chars). Stable for a given (tenant, caller)
    /// pair; collisions are negligible at the cache-key cardinality we expect.
    /// </summary>
    /// <remarks>
    /// Phase 1.5 will replace this with a proper accessible-scope projection (matter set,
    /// practice area set, etc.) once those claims arrive on the principal or are
    /// queryable from a unified access-control service (DEP-3 resolution).
    /// </remarks>
    private static string ComputeAccessibleScopeHash(string tenantId, string callerOid)
    {
        var input = $"tid:{tenantId}|oid:{callerOid}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
