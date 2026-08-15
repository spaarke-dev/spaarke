using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Detectors;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Threads;
using Sprk.Bff.Api.Services.Communication.Tracking;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Communication Service (ADR-010: feature module pattern).
/// Registers communication services and configuration.
/// </summary>
/// <remarks>
/// ADR-010 / r3 task 026 (2026-08-14): the registration body is decomposed into cohesive private
/// helpers for reviewability. This is a BEHAVIOR-NEUTRAL split — the same services, lifetimes, and
/// order-of-effect as before (helpers are contiguous slices invoked in the original sequence; no new
/// abstraction/interface, no changed lifetime, no reordered registration). The connective-tissue
/// blocks (options, core services, inbound enrichment/producers, read models) stay inline in
/// <see cref="AddCommunicationModule"/> so the composition reads top-to-bottom as it always did.
/// </remarks>
public static class CommunicationModule
{
    public static IServiceCollection AddCommunicationModule(this IServiceCollection services, IConfiguration configuration)
    {
        // ── Options (kill-switches / thresholds; IOptionsMonitor — a flag/threshold flip takes effect WITHOUT redeploy) ──

        // Bind CommunicationOptions from "Communication" section
        services.Configure<CommunicationOptions>(configuration.GetSection(CommunicationOptions.SectionName));

        // Auto-file kill-switch + threshold (ADR-018 / FR-11). Bound from "Communication:AutoFile" and
        // consumed via IOptionsMonitor so a flag/threshold flip takes effect WITHOUT redeploy.
        services.Configure<AutoFileOptions>(configuration.GetSection(AutoFileOptions.SectionName));

        // Category→team reconciliation routing (ADR-018 / FR-E7, task 057). Bound from
        // "Communication:CategoryRouting"; operator adds/removes a mapping or flips routing off with NO redeploy.
        services.Configure<CategoryRoutingOptions>(configuration.GetSection(CategoryRoutingOptions.SectionName));

        // Tracking-footer config (ADR-018 / FR-A1). Bound from "Communication:TrackingFooter"; operator
        // flips enable / edits the disclosure template with NO redeploy. Carries only the Key Vault secret
        // NAME of the HMAC key, never the key (ADR-028 / NFR-07).
        services.Configure<TrackingFooterOptions>(configuration.GetSection(TrackingFooterOptions.SectionName));

        // Semantic-match (rung 4 / FR-14) options. Bound from "Communication:SemanticMatch"; the Enabled
        // flag is an operational kill-switch for the semantic rung (no redeploy).
        services.Configure<SemanticMatchOptions>(configuration.GetSection(SemanticMatchOptions.SectionName));

        // AI extract+classify (rung 5 / FR-15) options. Bound from "Communication:AiClassification"; the
        // Enabled flag is an operational kill-switch for the AI-classify rung (no redeploy).
        services.Configure<AiClassificationOptions>(configuration.GetSection(AiClassificationOptions.SectionName));

        // Record-name/number match (rung 3.5) options. Bound from "Communication:RecordNameMatch"; the Enabled
        // flag is an operational kill-switch (no redeploy). Deterministic exact-name matcher (email-r4 UAT).
        services.Configure<RecordNameMatchOptions>(configuration.GetSection(RecordNameMatchOptions.SectionName));

        // Contact-name match (rung 3.6) options. Bound from "Communication:ContactNameMatch"; the Enabled flag
        // is an operational kill-switch (no redeploy). Deterministic exact full-name→contact matcher, suggest-only
        // (email-r4 UAT R2 B1).
        services.Configure<ContactNameMatchOptions>(configuration.GetSection(ContactNameMatchOptions.SectionName));

        // Affinity / deterministic-learning rung (FR-A4) options. Bound from "Communication:Affinity"; the
        // Enabled flag is a per-tenant operational kill-switch (IOptionsMonitor — no redeploy, ADR-018). The
        // rung surfaces a learned SUGGEST-ONLY candidate from the per-tenant sprk_affinity store; it never
        // auto-files (excluded from the mapper's auto-file/deterministic-write sets).
        services.Configure<AffinityOptions>(configuration.GetSection(AffinityOptions.SectionName));

        // Attachment-text match signal (Phase 2) options. Bound from "Communication:AttachmentMatch"; the
        // Enabled flag is an operational kill-switch (no redeploy). The inbound processor extracts bounded
        // attachment text (ITextExtractor) into the envelope before association so records named only in an
        // attachment still match.
        services.Configure<AttachmentMatchOptions>(configuration.GetSection(AttachmentMatchOptions.SectionName));

        // ── Core services (singleton: all dependencies are singleton or options) ──
        services.AddSingleton<CommunicationAccountService>();
        services.AddSingleton<ApprovedSenderValidator>();
        services.AddSingleton<CommunicationService>();
        // ADR-010 testing-seam over CommunicationService.ReconstructEnvelopeAsync for the Job B apply path (task 031
        // citation re-verify). Pass-through to the singleton CommunicationService → singleton.
        services.AddSingleton<ICommunicationEnvelopeReader>(sp => sp.GetRequiredService<CommunicationService>());
        services.AddSingleton<EmlGenerationService>();
        services.AddSingleton<GraphMessageToEmlConverter>();
        // HTML-preserving REVERSE of GraphMessageToEmlConverter (email-communication-solution-r5 task 010 /
        // FR-07 / NFR-03). Pure transformation (no I/O) → singleton, mirroring the converter above; its
        // HtmlSanitizer is configured once in the ctor and only Sanitize() is called after (thread-safe).
        // Consumed by the GET /api/documents/{id}/eml-render endpoint (FileAccessEndpoints).
        services.AddSingleton<EmlToHtmlRenderer>();
        services.AddSingleton<MailboxVerificationService>();

        // Channel transport plane: sender/archiver/ingestor seams + ACS identity/thread planes + inbound ingress.
        AddChannelSenders(services, configuration);

        // Association Engine: the boundary mapper, the ordered rungs + structural detectors, the gates, and the
        // envelope-only engine + inbound processor.
        AddAssociationEngine(services);

        // FR-B3 (task 043): the user-upload ("Save to Spaarke") capture entry point — the email sibling of
        // IncomingCommunicationProcessor (Graph-webhook) and MessagingIngestor (ACS). Routes a hand-filed email
        // through the SAME Association Engine + enrichment so a saved email becomes an intelligence-bearing
        // sprk_communication, not merely a sprk_document archive. Registered UNCONDITIONALLY (ADR-010 concrete;
        // §10 unconditional DI) — all deps are singletons; OfficeService consumes it best-effort (optional ctor).
        services.AddSingleton<EmailUploadCaptureService>();

        // Direction-agnostic enrichment orchestrator (ADR-045 / FR-08). Invoked by BOTH the inbound
        // processor and the outbound send path so received and sent communications get identical
        // treatment. Registered UNCONDITIONALLY (consumed unconditionally by both callers per ADR-032;
        // no feature gate — no Null-Object peer required). Singleton mirrors IncomingCommunicationProcessor,
        // which already injects the scoped IPostUploadIndexingEnqueuer via the same pattern.
        services.AddSingleton<ICommunicationEnrichmentService, CommunicationEnrichmentService>();

        // communication_assessed producer seam (spaarke-notification-spine-r1 task 040 / FR-11). Enrichment
        // step 5 (RunAssessmentEmissionAsync) publishes the assessed signal through
        // ICommunicationAssessedProducer instead of only logging. Registered UNCONDITIONALLY (ADR-032) with
        // the interim log-only safe default (LoggingCommunicationAssessedProducer) — no outbox write, no
        // IEventRulesService.FireAsync (both out of scope for FR-11). Task 041 replaces this registration with
        // the real comms-policy-gate consumer behind the SAME seam; task 042 (downstream of that gate) writes
        // the kind=communication-assessed outbox row. Placement Justification (root §10/§11): the seam lives in
        // Services/Communication/ beside its sole emit point (the enrichment step), consumes nothing AI-internal
        // (ADR-013 clean), and is a genuine seam (≥2 implementations: this default + task 041's policy consumer).
        // NOTE (task 041): the log-only default below is REPLACED by RuleGatedAssessedConsumer (next block);
        // LoggingCommunicationAssessedProducer stays defined as the ADR-032 safe default / test double.

        // Comms policy gate (spaarke-notification-spine-r1 task 041 / FR-12). Owner chose a dedicated
        // sprk_communicationrule Dataverse table (Path B — notes/041-rule-store-decision.md).
        // CommunicationRuleGate reads that table and evaluates tenant/matter match + a confidence threshold
        // (per-rule sprk_confidencethreshold, falling back to CommsPolicyOptions.DefaultConfidenceThreshold),
        // flags privilege (ADR-015 — flagged, NEVER decided), and returns authorize/deny. RuleGatedAssessedConsumer
        // is the REAL consumer behind task 040's ICommunicationAssessedProducer seam — it REPLACES the interim
        // LoggingCommunicationAssessedProducer; the enrichment step-5 emit point is unchanged. No outbox write, no
        // IEventRulesService.FireAsync (RI action execution is task 042, downstream of authorize). Concrete
        // singletons (ADR-010); all deps (IGenericEntityService, IOptions) are singletons.
        services.Configure<CommsPolicyOptions>(configuration.GetSection(CommsPolicyOptions.SectionName));
        services.AddSingleton<CommunicationRuleGate>();

        // Comms-RI action orchestrator (spaarke-notification-spine-r1 task 042 / FR-13). Executes the RI action
        // path when CommunicationRuleGate AUTHORIZES: converges the Layer-A action seam (task 031 IActionSeam,
        // ADR-013 — creates a follow-up task, never a direct write), the Layer-B outbox (task 012, kind=
        // communication-assessed row BEFORE the ping), the Layer-C SignalR ping (task 020, best-effort), and the
        // ONE platform appnotification writer (NotificationService.CreateNotificationAsync — the Daily-Briefing
        // mirror). RuleGatedAssessedConsumer (registered next) delegates to it ONLY inside its authorize branch, so
        // a deny produces no side effect. Placement Justification (root §10/§11): sits in Services/Communication/
        // beside the gate + arrived producer, depends "up" into the Notifications spine + the ADR-013 IActionSeam
        // facade (never AI-internal types); ZERO new access logic, ZERO new Dataverse write path (the seam +
        // NotificationService are the only writers). Concrete singleton (ADR-010) — all deps are singletons:
        // IActionSeam + NotificationService (AnalysisServicesModule), OutboxService + SignalRDeliveryService
        // (NotificationsModule), IGenericEntityService. MUST be registered BEFORE RuleGatedAssessedConsumer, which
        // now takes it as a ctor dependency.
        services.AddSingleton<CommunicationRiActionService>();
        services.AddSingleton<ICommunicationAssessedProducer, RuleGatedAssessedConsumer>();

        // Direction-symmetric thread resolver + per-channel key strategies.
        AddThreadResolution(services);

        // Participant-index writer (messaging-communication-app-r2 task 050 / FR-08 / ADR-048). Writes the
        // queryable sprk_communicationparticipant junction at MESSAGE grain — one row per (message ×
        // person/address × role {From,To,Cc,Bcc}) — at BOTH persist points: outbound send (CommunicationService)
        // and inbound capture (email: IncomingCommunicationProcessor; chat: MessagingIngestor). Placement
        // Justification (root §10 / §11): lives inside Services/Communication/ behind the ADR-045 boundary;
        // REUSES the existing email→contact resolver (ICommunicationDataverseService.QueryContactByEmailAsync)
        // that ParticipantCorrelationRung uses — NO new resolver, NO new AI dependency, NO new NuGet (publish-size
        // delta ≈0). Registered UNCONDITIONALLY (ADR-010 / ADR-032 — all three call sites consume it best-effort;
        // no feature gate → no asymmetric registration, no Null-Object peer required). Best-effort / non-fatal +
        // idempotent: it never throws, so a junction-write failure never fails a send or drops a captured message
        // (NFR-02), and re-processing the same message writes no duplicate rows. Singleton — its deps
        // (ICommunicationDataverseService + IGenericEntityService) are singletons; mirrors the enrichment/thread
        // resolvers the same three call sites already inject.
        services.AddSingleton<CommunicationParticipantIndexer>();

        // Messaging attachment materialization (messaging-communication-app-r1 task 070 / FR-14). The net-new
        // messaging step that materializes a chat file: ACS/file → SPE (SpeFileStore facade, ADR-007) →
        // governed sprk_document (sprk_document.sprk_communication lookup) → sprk_communicationattachment
        // intersection, returning the reference the ACS message carries (SPE is the store; binary never on the
        // ACS message). Enforces CHAT-ATTACHMENT-POLICY.md (25 MB binary cap + MIME allow-list) BEFORE upload,
        // rejecting oversize/disallowed with RFC 7807 ProblemDetails (ADR-019). Storage SCHEMA is unchanged —
        // it reuses the SAME sprk_document/sprk_communicationattachment shape the email inbound path writes.
        // Registered UNCONDITIONALLY (ADR-010 / ADR-032 — no feature gate). Scoped to match the Scoped
        // ISpeFileOperations facade lifetime (the Singleton IGenericEntityService composes safely into a Scoped
        // consumer). Consumed by the messaging inbound/file-share wiring (task 031 / 060), not registered here.
        services.AddScoped<MessageAttachmentMaterializer>(); // task 070

        // BFF read-path internal-only / privilege enforcement (messaging-communication-app-r1 task 042, REWORKED to
        // the impersonation model 2026-07-16 / FR-08 / NFR-06). RECORD-LEVEL read access is now Dataverse's job:
        // task 050's thread-read + unread endpoints issue the sprk_communication query IMPERSONATED (MSCRMCallerID =
        // caller systemuserid, via DataverseWebApiService.RetrieveMultipleImpersonatedAsync), so Dataverse returns
        // exactly the rows the caller may see — honoring ownership, role depth, BU, teams, sharing, hierarchy — in
        // one query. This filter then applies, ON TOP of those already-scoped rows, only the two Spaarke business
        // rules impersonation does not cover: internal-only (D-05 — hide sprk_isinternalonly from non-internal
        // callers, default-deny on an unreadable flag) and privilege (ADR-015 — sprk_privilegeclassification rides
        // along as composed metadata, NEVER gates a read, NEVER calls AI). The filter is pure (no I/O / no Dataverse
        // / membership / grant / AI dependency), so it needs only its logger.
        //
        // This SUPERSEDES the task-042 hand-computed "MembershipResolver(anchor) ∪ overlay grants" union + the
        // point-forward privacy switch, which mis-modeled effective access for an app-only BFF and rebuilt platform
        // security (design §5 — leverage, don't rebuild). Accordingly the IThreadPrivateGrantProvider deny-all
        // registration is REMOVED (the read filter no longer depends on it; the type is retained for future task-043
        // private-direct-thread work). Discrete authz gates ("can user X open/post to thread Y") use Dataverse
        // RetrievePrincipalAccess (Web API, app-only) — the documented one-principal-one-record mechanism for 050/043.
        // Registered UNCONDITIONALLY (ADR-010 / ADR-032 — the endpoint that consumes it maps unconditionally).
        services.AddSingleton<ICommunicationAccessFilter, CommunicationAccessFilter>();

        // BFF thread-read + unread-count read model (messaging-communication-app-r1 task 050 / FR-11 / NFR-06/07).
        // The ~5s poll surface for the timeline (task 060): both GET endpoints issue the sprk_communication query
        // IMPERSONATED (MSCRMCallerID = caller systemuserid) so Dataverse enforces record-level access natively,
        // then apply the SAME task-042 ICommunicationAccessFilter (internal-only + privilege) on top — no second
        // filter. Registered UNCONDITIONALLY (ADR-010 / ADR-032 — the endpoints map unconditionally).
        //   • IImpersonatedCommunicationQuery — thin ADR-010 test-seam over DataverseWebApiService's impersonated
        //     read (the no-leak negative cases are non-negotiable, NFR-06; ADR-038 bans Mock<HttpMessageHandler>).
        //     Stateless pass-through over the singleton DataverseWebApiService → singleton.
        //   • CommunicationThreadReadService — SCOPED, because it consumes the Scoped ICallerSystemUserResolver
        //     (a captive scoped dependency inside the Singleton CommunicationService would be an anti-pattern).
        //   • ICallerSystemUserResolver — REUSED (§11) for oid→systemuserid; TryAdd so this module is self-contained
        //     even if the AI module (its usual registrant) is not composed, without clobbering that registration.
        services.AddSingleton<IImpersonatedCommunicationQuery, DataverseImpersonatedCommunicationQuery>();
        services.TryAddScoped<Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver,
                              Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolver>();
        services.AddScoped<CommunicationThreadReadService>();

        // FR-17 ranked-exceptions queue-feed (email-communication-intelligence-r1 task 032). SAME impersonated
        // read + shared ICommunicationAccessFilter as CommunicationThreadReadService above (no new access
        // mechanism) — composed with task 030's sprk_emailreviewlog Proposed-row store via the existing
        // IGenericEntityService seam. SCOPED for the same reason as CommunicationThreadReadService (consumes the
        // Scoped ICallerSystemUserResolver). Registered UNCONDITIONALLY (ADR-010/ADR-032 — the endpoint maps
        // unconditionally); read-only, r1 supplies the feed only (C-3), r5 builds no surface here.
        services.AddScoped<CommunicationQueueFeedService>();

        // Job B APPLY (email-communication-intelligence-r1 task 031 / FR-10). Applies a CONFIRMED pending proposal
        // (task 030's open sprk_emailreviewlog Proposed row) to the associated record via the blessed
        // IActionSeam.UpdateRecordAsync UNDER THE CONFIRMING USER'S MSCRMCallerID impersonation (owner Option 2,
        // 2026-07-29 — native modifiedby = the human; go-live prereq: BFF app user holds prvActOnBehalfOfAnotherUser),
        // re-validating the sprk_emailupdatefield allow-list + citation at apply time, and writing the append-only
        // Applied audit row. SCOPED (consumes the Scoped ICallerSystemUserResolver, same as the queue feed).
        // Registered UNCONDITIONALLY (ADR-010/ADR-032 — the apply endpoint maps unconditionally).
        services.AddScoped<ICommunicationProposalApplyService, CommunicationProposalApplyService>();

        // Job C APPLY (email-communication-intelligence-r2 task 034 / FR-D5, backs FR-E5). Sibling of the Job B apply
        // above: creates the sprk_event (type=task) a CONFIRMED create-task proposal describes via the blessed
        // IActionSeam.CreateTaskAsync, PATCHes the human-supplied FR-E5 fields (status/completed-date/base-date/
        // final-due-date) UNDER THE CONFIRMING USER'S MSCRMCallerID impersonation, and writes ONE append-only Applied
        // audit row (Path B — facade unchanged per ADR-013). SCOPED (consumes the Scoped ICallerSystemUserResolver,
        // same as the Job B apply + queue feed). Registered UNCONDITIONALLY (ADR-010/ADR-032 — the apply endpoint maps
        // unconditionally).
        services.AddScoped<ICommunicationCreateTaskApplyService, CommunicationCreateTaskApplyService>();

        // Layer-C fan-out targeting (spaarke-notification-spine-r1 task 023 / FR-08 / NFR-07). Given a persisted
        // sprk_communication + its thread, returns the systemuserids eligible to receive a Layer-C ping (task 024's
        // producer loops them into SignalRDeliveryService.PingUserAsync). Placement Justification (root §10 / §11):
        // its dependencies are ALL Communication-flavored (ICommunicationAccessFilter + IThreadPrivateGrantProvider +
        // the sprk_communicationparticipant junction via IGenericEntityService), so ADR-010's feature-module home is
        // HERE, not NotificationsModule (which owns the SignalR delivery leg + identity resolver). ZERO new access
        // logic: it COMPOSES the two existing access primitives + the junction read-only (design §5 — leverage,
        // don't rebuild). Singleton — all deps are singletons; mirrors the participant indexer/thread resolver.
        //
        // IThreadPrivateGrantProvider deny-all null-object (ADR-032): the task-042 read-path rework REMOVED the
        // deny-all registration (the impersonation-based read filter no longer depends on it — see the task-042
        // comment above), so the seam is currently UNREGISTERED. The fan-out service DOES consume it (private-thread
        // gate), and it is reached from the unconditionally-mapped negotiate/producer path, so the null-object MUST
        // be registered unconditionally for the consumer to resolve (ADR-032 asymmetric-registration rule; CLAUDE.md
        // §10 bullet 6). TryAdd so the FUTURE Dataverse-backed provider (task-043 private-direct work) wins if it
        // registers first — this only supplies the fail-closed default (private-thread fan-out is EMPTY until then).
        services.TryAddSingleton<IThreadPrivateGrantProvider, DenyAllThreadPrivateGrantProvider>();
        services.AddSingleton<CommunicationFanOutTargetingService>();

        // The single, spine-owned communication-arrived producer (spaarke-notification-spine-r1 task 024 /
        // FR-09 / NFR-05). Emits the Layer-C refresh signal at PERSISTENCE for every sprk_communication write —
        // inbound capture (email + messaging) + outbound send (email + messaging), identically — so messaging-r3
        // task 045 consumes ONE spine event instead of wiring its own producer (Owner Clarification). Injected as
        // an OPTIONAL trailing ctor param into the three persist orchestrators (IncomingCommunicationProcessor,
        // MessagingIngestor, CommunicationService), each of which calls it AFTER its participant-index step (the
        // point at which the fan-out junction, thread lookup, and regarding are populated — NOT the raw CreateAsync;
        // see the producer's remarks + notes/024). Placement Justification (root §10 / §11): sits in
        // Services/Communication/ beside its fan-out dependency (task 023) and depends "up" into the Notifications
        // spine infra (OutboxService + SignalRDeliveryService) — the correct direction; ZERO new access logic, ZERO
        // AI dependency (ADR-013 clean). Registered UNCONDITIONALLY (ADR-010 / ADR-032 — non-fatal producer consumed
        // best-effort by all three call sites; no feature gate). Singleton — all deps (IGenericEntityService,
        // CommunicationFanOutTargetingService, OutboxService, SignalRDeliveryService) are singletons, matching the
        // three singleton orchestrators it is injected into (no captive-scope anti-pattern).
        services.AddSingleton<CommunicationArrivedProducer>();

        // Job handlers + background (hosted) services for the dedicated communication queue + Graph webhook lifecycle.
        AddCommunicationHostedServices(services);

        // Membership derivation + ACS reconcile + Direct 1:1 thread access mechanics.
        AddMembershipReconciliation(services, configuration);

        return services;
    }

    /// <summary>
    /// Channel transport plane (ADR-045 rule 4 / NFR-04): the ICommunicationChannelSender /
    /// ICommunicationArchiver / ICommunicationChannelIngestor seams (Email + Messaging), the ACS identity +
    /// thread planes the messaging sender depends on, and the inbound ACS ingress/normalizer/job-handler.
    /// Behavior-neutral extraction (r3 task 026) — same registrations, lifetimes, and order as the original
    /// inline block.
    /// </summary>
    private static void AddChannelSenders(IServiceCollection services, IConfiguration configuration)
    {
        // Channel seams (ADR-045 rule 4 / NFR-04). Email is the ONLY R4 implementation of each seam.
        // Registered UNCONDITIONALLY (ADR-010 / ADR-032 — the dispatcher + CommunicationService consume
        // them unconditionally; no feature gate). Adding a future channel (Teams/Slack/SMS) is purely
        // additive: register a new ICommunicationChannelSender + ICommunicationArchiver keyed to its own
        // CommunicationType here — the dispatcher resolves it by type with NO change to the dispatch site,
        // the Association Engine, the enrichment service, the regarding model, or the review UI.
        services.AddSingleton<ICommunicationChannelSender, EmailChannelSender>();
        services.AddSingleton<ICommunicationArchiver, EmailArchiver>();
        services.AddSingleton<CommunicationChannelDispatcher>();

        // ACS identity plane (FR-03 / task 010): server-side ACS identity creation + `communicationUserId`
        // ↔ Dataverse mapping + `chat`-scoped token minting. Registered UNCONDITIONALLY (ADR-010 / ADR-032)
        // via the ACS-owned extension; the CommunicationIdentityClient is built from the DI-injected central
        // TokenCredential (ADR-028 / NFR-05 — no inline credential, no connection-string key). ACS types stay
        // inside Services/Communication/ (ADR-045). Consumed by the messaging transport (011/020/051).
        services.AddAcsIdentityPlane(configuration);

        // ACS thread + membership plane (FR-15 / task 011): server-side chat-thread create (with 30-day
        // auto-delete retention set at create time) + idempotent, batch-friendly Add/Remove participant
        // ops. Registered UNCONDITIONALLY (ADR-010 / ADR-032) via the ACS-owned extension; the ChatClient is
        // built from a chat token rooted in the DI-injected central TokenCredential (ADR-028 / NFR-05 — no
        // inline credential, no connection-string key). Lazy client construction requires no live ACS
        // resource at startup. ACS types stay inside Services/Communication/ (ADR-045). Consumed by the
        // messaging sender (020), membership reconcile (041), and outbound send (051).
        services.AddAcsThreadPlane();

        // Messaging (ACS Chat) channel seam (messaging-communication-app-r1 task 020 / FR-01). The SECOND
        // implementation of each ADR-045 seam, keyed to CommunicationType.Message — proving "add a channel
        // by additive keyed registration alone" (NFR-04): the dispatcher's ToDictionary(SupportedType) picks
        // these up with NO change to the dispatch site in CommunicationService, the Association Engine, the
        // enrichment service, the regarding model, or the review UI. Registered UNCONDITIONALLY (ADR-010 /
        // ADR-032 — the dispatcher consumes them unconditionally; no feature gate). MessagingChannelSender
        // transmits server-side over ACS Chat using the task-010/011 identity + thread planes registered
        // above, returning the ACS message id as ProviderMessageId (the echo-dedup key, FR-04);
        // MessagingArchiver emits a chat-transcript artifact archived to SPE via the same
        // ICommunicationArchiver → SpeFileStore flow as email's .eml (ADR-007). Placed after the ACS planes
        // because the sender depends on IAcsIdentityService + IAcsThreadService.
        services.AddSingleton<ICommunicationChannelSender, MessagingChannelSender>();
        services.AddSingleton<ICommunicationArchiver, MessagingArchiver>();

        // Inbound ingestor seam (messaging-communication-app-r1 task 021 / FR-02). The NET-NEW inbound leg of
        // the ADR-045 channel seams — the counterpart to the sender/archiver legs above, completing the seam
        // triad ADR-045 STATED but left unbuilt (R4 shipped outbound only; inbound stayed concrete + email-only
        // via IncomingCommunicationProcessor). MessagingIngestor is the FIRST implementation, keyed to
        // CommunicationType.Message: it persists an already-normalized inbound ACS message (task 031's normalizer
        // supplies the NormalizedMessage) as a sprk_communication (Direction=Incoming) record via the canonical
        // IGenericEntityService, then invokes the SAME ICommunicationEnrichmentService the email inbound path
        // uses (best-effort, NFR-02) — so inbound capture is channel-agnostic, not forked per channel. Registered
        // UNCONDITIONALLY (ADR-010 / ADR-032 — the dispatcher's ToDictionary(SupportedType) resolver consumes
        // ingestors unconditionally; no feature gate). Email inbound stays on IncomingCommunicationProcessor and
        // is UNCHANGED by this task (no email ingestor registered → ResolveIngestor(Email) throws
        // CHANNEL_NOT_SUPPORTED by design). The Event Grid ingress (task 030) + Service Bus job + idempotent
        // dedupe + DLQ (task 031) call INTO this seam's IngestAsync and are NOT registered here.
        services.AddSingleton<ICommunicationChannelIngestor, MessagingIngestor>();

        // ACS Event Grid inbound ingress (messaging-communication-app-r1 task 030 / FR-02). The transport
        // boundary that turns ACS chat events (delivered by the per-boundary Event Grid subscription task 012
        // provisions) into IncomingMessaging jobs on the EXISTING Services/Jobs Service Bus contract — it does
        // NOT create a second queue/pipeline (ADR-004/036 / root §10). Registered UNCONDITIONALLY (ADR-010 /
        // ADR-032): the ingress ENDPOINT maps unconditionally (AcsEventGridEndpoints), so its service must
        // register unconditionally too. The endpoint is thin — validate the Event Grid subscription-validation
        // handshake + topic-origin allow-list (+ optional shared secret), enqueue, return fast; task 031's job
        // handler consumes IncomingMessagingJobPayload and owns normalize + persist + dedupe + DLQ. AllowedTopics
        // is the fail-closed origin control (SECURITY BOUNDARY — an unvalidated/spoofed payload never enqueues).
        services.Configure<AcsEventGridIngressOptions>(configuration.GetSection(AcsEventGridIngressOptions.SectionName));
        services.AddSingleton<AcsEventGridIngressService>();

        // Inbound ACS-event normalizer (messaging-communication-app-r1 task 031 / FR-02). The ACS analog of
        // GraphMessageNormalizer: maps an ACS chat Event Grid event → NormalizedMessage exactly ONCE at the
        // pipeline boundary (ADR-045) — no Azure.Communication.* type leaks downstream (it reads raw JSON, not
        // ACS SDK types). Pure mapper, no I/O → singleton (ADR-010), mirroring GraphMessageNormalizer's
        // registration below. Consumed by IncomingMessagingJobHandler.
        services.AddSingleton<AcsEventNormalizer>();

        // Inbound ACS-message job handler (messaging-communication-app-r1 task 031 / FR-02, FR-04, NFR-03). The
        // ACS analog of IncomingCommunicationJobHandler: consumes the IncomingMessaging job task 030 enqueues,
        // normalizes it (AcsEventNormalizer), and persists it idempotently via the task-021 MessagingIngestor
        // seam (resolved through CommunicationChannelDispatcher.ResolveIngestor(Message)). Idempotent dedupe on
        // the ACS message id via the EXISTING IIdempotencyService — Event Grid at-least-once redelivery, genuine
        // duplicates, and our own outbound echo (task 051) all collapse to exactly one persist. On repeated
        // failure it returns JobStatus.Poisoned so CommunicationJobProcessor dead-letters the job (ADR-004/036 —
        // never silently dropped). Registered as a concrete SCOPED type (mirrors IncomingCommunicationJobHandler)
        // for direct resolution by CommunicationJobProcessor — NO second queue/DLQ/idempotency mechanism (root §10).
        services.AddScoped<IncomingMessagingJobHandler>();
    }

    /// <summary>
    /// Association Engine (ADR-045 / FR-09/FR-10): the pure Graph→envelope boundary mapper, the ordered
    /// IAssociationRung set (13 rungs) + IStructuralDetector set (4), the affinity learning loop, the
    /// confidence→status gates, the tracking-token signer, and the envelope-only engine + inbound processor.
    /// Rungs are evaluated by ascending Order, so registration order is cosmetic. Behavior-neutral extraction
    /// (r3 task 026) — same registrations, lifetimes, and order as the original inline block.
    /// </summary>
    private static void AddAssociationEngine(IServiceCollection services)
    {
        // Association Engine (ADR-045 / FR-09/FR-10): the pure Graph→envelope boundary mapper, the
        // ordered rungs, and the envelope-only engine. All unconditional (consumed unconditionally by
        // the inbound processor per ADR-032; no feature gate). Rungs are registered as IAssociationRung
        // and the engine evaluates them by ascending Order — so registration order is cosmetic.
        services.AddSingleton<GraphMessageNormalizer>();
        services.AddSingleton<IAssociationRung, ExplicitReferenceRung>();      // rung 0 — explicit reference
        // rung 0 — identifier reverse-lookup (FR-01). SAME Kind/Order as ExplicitReferenceRung: a well-formed
        // explicit identifier IS a rung-0 explicit reference. Extends matter-only identifier matching to all 7
        // core types by VALUE-based reverse lookup against Dataverse, catalog-driven off sprk_recordtype_ref
        // (no numbering scheme in code; onboarding a tenant needs only catalog config). Bare-numeric tokens
        // emit sub-threshold (never auto-file alone); multi-entity tokens are capped (never a guessed auto-file).
        // Registered unconditionally (mirrors the other deterministic rungs; ADR-010).
        services.AddSingleton<IAssociationRung, IdentifierReverseLookupRung>(); // rung 0 — identifier reverse-lookup
        // rung 0 (tier) — recipient-alias (FR-A2). Parses To/Cc/Bcc for a per-record intake address
        // (matter-{ref}@) and resolves it to a matter — a deliberate routing instruction, so it is
        // auto-file-eligible like an explicit reference (AssociationStatusMapper.IsAutoFileEligible). Reads
        // NormalizedMessage.Bcc (mapped at the Graph boundary); Bcc-only delivery associates deterministically.
        // Registered unconditionally (mirrors the other deterministic rungs; ADR-010).
        services.AddSingleton<IAssociationRung, RecipientAliasRung>();         // rung 0 — recipient-alias
        // rung 0 (tier) — tracking-token reader (FR-A1 / task 013). Reads the HMAC-signed footer token task 012
        // stamps on outbound Spaarke communications, VERIFIES the signature via ITrackingTokenSigner (010) BEFORE
        // trusting the bound record (ADR-028 verify-before-trust / NFR-07), and emits a signed-valid match at 1.0
        // (auto-file-eligible) or a bare/edited textual reference at 0.65 (corroborating). Reuses
        // Kind=ExplicitReference so NO AssociationStatusMapper change is required (a verified, Spaarke-minted token
        // IS the strongest explicit reference; the mapper collapses same-kind matches per target to their MAX).
        // Reads only the envelope (BodyText/BodyHtml incl. quoted history) + RegardingFieldMap + the signer — no
        // Dataverse, no AI (ADR-013). Best-effort/non-fatal (NFR-04): a forged/absent/deleted footer degrades to
        // no-match. Registered unconditionally (mirrors the other deterministic rungs; ADR-010).
        services.AddSingleton<IAssociationRung, TrackingTokenRung>();          // rung 0 — tracking-token reader
        services.AddSingleton<IAssociationRung, ThreadContinuityRung>();       // rung 1 — thread continuity
        services.AddSingleton<IAssociationRung, ParticipantCorrelationRung>(); // rung 2 — participant correlation
        // rung 3 — structural detectors (NFR-04: adding a detector is a new IStructuralDetector
        // registration; the rung + engine are unchanged).
        services.AddSingleton<IStructuralDetector, CalendarInviteDetector>();
        services.AddSingleton<IStructuralDetector, ESignCompletionDetector>();
        services.AddSingleton<IStructuralDetector, InvoiceNumberDetector>();
        services.AddSingleton<IStructuralDetector, CourtEFilingDetector>();
        services.AddSingleton<IAssociationRung, StructuralDetectorRung>();     // rung 3 — structural detectors
        // rung 3.5 — deterministic record-NAME/number match (email-r4 UAT 2026-07-17). Runs in the
        // deterministic pass; retrieves candidates from the records index (keyword ranking) then VERIFIES an
        // exact name/number appearance in the email. Surfaces every verified type (matter/project/invoice) for
        // review; NEVER auto-files (mapper excludes it from auto-file eligibility). Consumes IRecordMatchingAi
        // (ADR-013) from a per-evaluation scope. Registered unconditionally; self-gated by Communication:RecordNameMatch:Enabled.
        services.AddSingleton<IAssociationRung, RecordNameMatchRung>();        // rung 3.5 — record-name/number match
        // rung 3.6 — deterministic CONTACT-NAME match (email-r4 UAT R2 B1, 2026-07-20). Extracts Title-Case
        // full-name phrases from subject/body/attachment and resolves each by EXACT fullname→contact lookup
        // (ICommunicationDataverseService.QueryContactsByFullNameAsync — contacts are NOT in the records index).
        // SUGGEST-ONLY: emits sprk_regardingperson (a mapper FALLBACK field) at a Suggested-band confidence and
        // is NOT auto-file-eligible, so it never auto-files. Registered unconditionally; self-gated by
        // Communication:ContactNameMatch:Enabled.
        services.AddSingleton<IAssociationRung, ContactNameMatchRung>();       // rung 3.6 — contact-name match
        // rung 3.7 — attachment→document association (email-communication-intelligence-r1 061 UAT / F1). Matches
        // an incoming attachment to an existing sprk_document (by sprk_filename today; by the AI-populated
        // sprk_globalsearchextender content field as that project lands) and surfaces the document's OWN
        // matter/project/invoice links as candidates. SUGGEST-ONLY: RungKind.DocumentAssociation is not in the
        // mapper's auto-file-eligible/deterministic-write sets, so it can only add review candidates, never
        // auto-file. Takes the singleton IGenericEntityService (no captive dependency). Registered unconditionally (ADR-010).
        services.AddSingleton<IAssociationRung, AttachmentDocumentAssociationRung>(); // rung 3.7 — attachment→document
        // Affinity / deterministic learning loop (FR-A4). Reads the per-tenant sprk_affinity store (human
        // confirmation frequencies: sender / sender-domain / subject-keyword / participant-set → record) and
        // surfaces the highest-frequency record for an untagged message's signals as an explainable candidate
        // citing the confirmation count. SUGGEST-ONLY: RungKind.Affinity is excluded from the mapper's
        // auto-file/deterministic-write sets, so it can only add a review candidate, never auto-file. Deterministic
        // frequency counting only (no AI/ML — ADR-013). The store is an ADR-040 Path A exception (distinct from the
        // ADR-040 session ledger + ADR-048 participant index). Registered unconditionally (ADR-010); self-gated by
        // Communication:Affinity:Enabled (per-tenant). AffinityStore takes the singleton IGenericEntityService.
        services.AddSingleton<AffinityStore>();
        services.AddSingleton<IAssociationRung, AffinityRung>();               // rung 3 (tier) — affinity learning
        // FR-A4 R-1: the human-confirmation→affinity write orchestration behind POST /{id}/confirm-affinity.
        // Singleton (its deps — ICommunicationEnvelopeReader, AffinityStore, IOptionsMonitor — are all singletons).
        services.AddSingleton<AffinityConfirmationRecorder>();
        // rung 4 — semantic record match (FR-14). AI-tier: the engine evaluates it only when the
        // deterministic pass did not auto-file, and the mapper caps it to Suggested (never auto-files).
        // Consumes the IRecordMatchingAi facade (ADR-013) from a per-evaluation scope (facade is scoped,
        // this rung is a singleton). Registered unconditionally; self-gated by Communication:SemanticMatch:Enabled.
        services.AddSingleton<IAssociationRung, SemanticMatchRung>();          // rung 4 — semantic match
        // rung 5 — AI extract + classify (FR-15). AI-tier: the engine evaluates it only when the
        // deterministic pass did not auto-file, and it emits metadata-only signals (no target) — so it never
        // auto-files and never forces a regarding write; its output is the W5 triage substrate (task 053).
        // Consumes the ICommunicationClassificationAi facade (ADR-013) from a per-evaluation scope (facade is
        // scoped, this rung is a singleton). Registered unconditionally; self-gated by Communication:AiClassification:Enabled.
        services.AddSingleton<IAssociationRung, AiClassificationRung>();       // rung 5 — AI extract + classify
        // Confidence→status ladder + auto-file gate (FR-11 / ADR-018). Both unconditional (ADR-010):
        // the gate is pure config resolution and the mapper is pure decision logic; no feature gate.
        services.AddSingleton<AutoFileGate>();

        // Category→team routing gate (ADR-018 / FR-E7, task 057) — pure config resolution, registered
        // unconditionally (ADR-010); consumed by CommunicationEnrichmentService at triage time.
        services.AddSingleton<CategoryRoutingGate>();
        // Tracking-footer resolver (FR-A1 / ADR-018) — unconditional (ADR-010); pure config resolution.
        services.AddSingleton<TrackingFooterGate>();
        // Tracking-token HMAC signer (FR-A1 / NFR-07 / task 010). Registered UNCONDITIONALLY (ADR-010; the
        // FEATURE gate lives in 011's TrackingFooterOptions.Enabled, not the registration). Placement: lives in
        // Services/Communication/Tracking beside its sole callers — the send path (012) + TrackingTokenRung
        // (013) — per §10 BFF hygiene. Injects the CENTRAL TokenCredential (Program.cs) to reach Key Vault; no
        // credential is new-ed here (ADR-028). Singleton: holds the TTL key cache (thread-safe).
        services.AddSingleton<ITrackingTokenSigner, TrackingTokenSigner>();
        services.AddSingleton<AssociationStatusMapper>();
        services.AddSingleton<IncomingAssociationResolver>();
        services.AddSingleton<IncomingCommunicationProcessor>();
    }

    /// <summary>
    /// Direction-symmetric thread resolver (messaging-communication-app-r1 task 040 / FR-06) + the per-channel
    /// IThreadKeyStrategy instances it resolves via ToDictionary(SupportedType). Behavior-neutral extraction
    /// (r3 task 026) — same registrations, lifetimes, and order as the original inline block.
    /// </summary>
    private static void AddThreadResolution(IServiceCollection services)
    {
        // Direction-symmetric thread resolver (messaging-communication-app-r1 task 040 / FR-06). The thread
        // analog of the enrichment orchestrator above: find-or-create a sprk_communicationthread and stamp
        // the sprk_communicationthread lookup, invoked from BOTH the inbound capture path (email:
        // IncomingCommunicationProcessor; chat: MessagingIngestor) AND the outbound send path
        // (CommunicationService) for ALL channels (ADR-045 rule 3). Registered UNCONDITIONALLY (ADR-010 /
        // ADR-032 — the resolver + its per-channel key strategies are consumed unconditionally by all three
        // call sites; no feature gate). Per-channel key extraction sits behind CommunicationType-keyed
        // IThreadKeyStrategy instances resolved via ToDictionary(SupportedType), mirroring the dispatcher —
        // so adding a future channel's thread key is a purely additive registration (NFR-04). Best-effort /
        // non-fatal (NFR-02): a resolve/create failure never fails send or capture. Thread anchor REUSES the
        // ADR-024 regarding family (no second regarding mechanism).
        services.AddSingleton<IThreadKeyStrategy, EmailThreadKeyStrategy>();
        services.AddSingleton<IThreadKeyStrategy, MessagingThreadKeyStrategy>();
        services.AddSingleton<IThreadResolver, ThreadResolver>();
    }

    /// <summary>
    /// Job handlers + background (hosted) services: the dedicated sdap-communication queue processor, the
    /// delta-query reconciliation backstop, the Graph webhook subscription manager, the missed-webhook polling
    /// backup, and the daily send-count reset. Hosted services start in registration order; this helper keeps
    /// their original relative order. Behavior-neutral extraction (r3 task 026) — same registrations, lifetimes,
    /// and order as the original inline block.
    /// </summary>
    private static void AddCommunicationHostedServices(IServiceCollection services)
    {
        // Job handler: processes incoming email notifications from Graph webhooks (Task 072)
        // Extracts message details from Graph, creates sprk_communication record, handles attachments.
        // JobType: "IncomingCommunication" — processed by dedicated CommunicationJobProcessor (not shared queue).
        // Registered as concrete type for direct resolution (no GetServices<IJobHandler> enumeration).
        services.AddScoped<IncomingCommunicationJobHandler>();

        // Background service: dedicated processor for the sdap-communication queue
        // Isolates email job processing from the shared sdap-jobs queue to prevent cross-domain failures.
        services.AddHostedService<CommunicationJobProcessor>();

        // Delta-query reconciliation backstop (FR-24). The Graph seam + the reconciliation service are
        // registered UNCONDITIONALLY (consumed unconditionally by the hosted service AND by
        // GraphSubscriptionManager's `missed`-lifecycle path per ADR-032 — no feature gate). The
        // reconciliation service is registered as a singleton AND as a hosted service via the same
        // instance so the on-demand lifecycle trigger and the periodic timer share one object.
        // GraphMailFolderDeltaReader is a concrete singleton (ADR-010 — no interface; virtual method
        // provides the test seam).
        services.AddSingleton<GraphMailFolderDeltaReader>();
        services.AddSingleton<MailboxDeltaReconciliationService>();
        services.AddHostedService(sp => sp.GetRequiredService<MailboxDeltaReconciliationService>());

        // Background service: manages Graph webhook subscriptions + lifecycle notifications for inbound
        // email monitoring (ADR-001, FR-24). Registered as a singleton AND as a hosted service via the
        // same instance so the incoming-webhook endpoint can invoke HandleLifecycleNotificationAsync
        // on the running manager.
        services.AddSingleton<GraphSubscriptionManager>();
        services.AddHostedService(sp => sp.GetRequiredService<GraphSubscriptionManager>());

        // Background service: backup polling for missed webhooks (ADR-001)
        services.AddHostedService<InboundPollingBackupService>();

        // Background service: reset daily send counts at midnight UTC (ADR-001)
        services.AddHostedService<DailySendCountResetService>();
    }

    /// <summary>
    /// Membership derivation + ACS reconcile (task 041, FR-07) and Direct 1:1 thread access mechanics
    /// (task 043). ACS thread membership is a reconciled PROJECTION of Dataverse-derived access, never a
    /// second ACL. Includes the Lazy&lt;IThreadMembershipDerivationService&gt; that breaks the genuine
    /// 3-node DI cycle (task 052) and the reconcile job on the existing shared sdap-jobs queue + the
    /// periodic sweep hosted service (self-gated). Behavior-neutral extraction (r3 task 026) — same
    /// registrations, lifetimes, and order as the original inline block.
    /// </summary>
    private static void AddMembershipReconciliation(IServiceCollection services, IConfiguration configuration)
    {
        // ────────────────────────────────────────────────────────────────────────────────────────────
        // task 041 — Membership derivation + ACS reconcile (FR-07). ACS thread membership is a reconciled
        // PROJECTION of Dataverse-derived access, NEVER a second ACL. Placement Justification (root §10 /
        // §11): all pieces live inside Services/Communication/ behind the ADR-045 boundary; the reconcile
        // reuses the EXISTING ADR-004 Service Bus job contract (no second queue/DLQ) and ADR-034 membership
        // discovery (no new authorization engine). Registered UNCONDITIONALLY (ADR-010); the sweep is
        // self-gated by an options flag at runtime (not a DI gate → no asymmetric registration).
        services.TryAddSingleton(TimeProvider.System); // task 041 (idempotent — matches other modules)
        services.Configure<MembershipReconcileOptions>(configuration.GetSection(MembershipReconcileOptions.SectionName)); // task 041
        // task 043 — Direct 1:1 thread access mechanics (find-or-create, explicit two-party read, per-message
        // GrantAccess). POA-based (owner ∪ POA share), NOT a new grant table (owner decision 2026-07-16,
        // notes/access-model-decision.md). IDataverseAccessGrantService is the ADR-010 testing seam over the
        // concrete DataverseWebApiService's GrantAccess/POA primitives (mirrors IImpersonatedCommunicationQuery
        // below — the same singleton, a second thin seam over it). GrantMessageAccessAsync (the per-message
        // grant hook) was GENERALIZED by task 052 (FR-11) to ALSO grant Open/record-anchored threads — see
        // that task's comment below for the DI-cycle note this required.
        services.AddSingleton<IDataverseAccessGrantService, DataverseAccessGrantService>(); // task 043
        // task 052 — Lazy<IThreadMembershipDerivationService> breaks a genuine 3-node DI cycle:
        // DirectThreadAccessService → IThreadMembershipDerivationService → IThreadExplicitParticipantReader
        // (DirectThreadExplicitParticipantReader) → IDirectThreadAccessService. All three are singletons;
        // deferring resolution to first use (inside GrantMessageAccessAsync, well after DirectThreadAccessService
        // itself is constructed and cached) lets the container satisfy the cycle without changing any of the
        // three services' shapes.
        services.AddSingleton(sp => new Lazy<IThreadMembershipDerivationService>(
            () => sp.GetRequiredService<IThreadMembershipDerivationService>()));
        services.AddSingleton<IDirectThreadAccessService, DirectThreadAccessService>(); // task 043
        // task 041/043 — explicit participant/grant reader. Task 041 registered the ADR-032 Null-Object default
        // (no explicit grants); task 043 replaces it with the REAL Direct-topology reader (owner ∪ POA share).
        // For a Direct thread it returns the two participants; for any non-Direct thread it returns EMPTY, so
        // Open/record-anchored threads are unaffected. Task 042's private-overlay-grant reader is a FUTURE
        // registration behind this SAME seam — the derivation + reconcile pick it up with no code change.
        services.TryAddSingleton<IThreadExplicitParticipantReader, DirectThreadExplicitParticipantReader>(); // task 043
        // task 041 — shared authorized-set contract: authorized = reverse-ADR-034(anchor) ∪ explicit grants.
        // Task 042's read-filter computes the SAME set; both consume this service so they agree by construction.
        services.AddSingleton<IThreadMembershipDerivationService, ThreadMembershipDerivationService>(); // task 041
        // task 041 — audit sink: one entry per ACS membership change (FR-07). Default = structured logging;
        // a durable sink can replace it behind this seam with no reconcile change.
        services.AddSingleton<IMembershipReconcileAuditSink, LoggingMembershipReconcileAuditSink>(); // task 041
        // task 041 — the reconcile core (desired = derived set → ACS MRIs; current = ACS participants;
        // Add(desired\current), Remove(current\desired); projection-never-exceeds guard; audit per change).
        services.AddSingleton<IMembershipReconciler, MembershipReconciler>(); // task 041
        // task 041 — event-driven + sweep enqueue entry point (best-effort, NFR-02). Consumed by 042/043 for
        // event-driven reconcile and by the sweep below.
        services.AddSingleton<MembershipReconcileEnqueuer>(); // task 041
        // task 041 — reconcile job on the EXISTING shared sdap-jobs queue (ServiceBusJobProcessor resolves it
        // by JobType via GetServices<IJobHandler>()). Reuses ADR-004/036 idempotency/retry/DLQ.
        services.AddSingleton<IJobHandler, MembershipReconcileJob>(); // task 041
        // task 041 — periodic eventual-consistency sweep (design §8.4). Self-gated by
        // Communication:MembershipReconcile:SweepEnabled (default off — event-driven is the primary path).
        services.AddHostedService<MembershipReconcileSweepService>(); // task 041
        // ────────────────────────────────────────────────────────────────────────────────────────────
    }
}
