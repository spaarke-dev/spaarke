using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Detectors;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Communication Service (ADR-010: feature module pattern).
/// Registers communication services and configuration.
/// </summary>
public static class CommunicationModule
{
    public static IServiceCollection AddCommunicationModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind CommunicationOptions from "Communication" section
        services.Configure<CommunicationOptions>(configuration.GetSection(CommunicationOptions.SectionName));

        // Auto-file kill-switch + threshold (ADR-018 / FR-11). Bound from "Communication:AutoFile" and
        // consumed via IOptionsMonitor so a flag/threshold flip takes effect WITHOUT redeploy.
        services.Configure<AutoFileOptions>(configuration.GetSection(AutoFileOptions.SectionName));

        // Semantic-match (rung 4 / FR-14) options. Bound from "Communication:SemanticMatch"; the Enabled
        // flag is an operational kill-switch for the semantic rung (no redeploy).
        services.Configure<SemanticMatchOptions>(configuration.GetSection(SemanticMatchOptions.SectionName));

        // AI extract+classify (rung 5 / FR-15) options. Bound from "Communication:AiClassification"; the
        // Enabled flag is an operational kill-switch for the AI-classify rung (no redeploy).
        services.Configure<AiClassificationOptions>(configuration.GetSection(AiClassificationOptions.SectionName));

        // Core services (singleton: all dependencies are singleton or options)
        services.AddSingleton<CommunicationAccountService>();
        services.AddSingleton<ApprovedSenderValidator>();
        services.AddSingleton<CommunicationService>();
        services.AddSingleton<EmlGenerationService>();
        services.AddSingleton<GraphMessageToEmlConverter>();
        services.AddSingleton<MailboxVerificationService>();

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

        // Association Engine (ADR-045 / FR-09/FR-10): the pure Graph→envelope boundary mapper, the
        // ordered rungs, and the envelope-only engine. All unconditional (consumed unconditionally by
        // the inbound processor per ADR-032; no feature gate). Rungs are registered as IAssociationRung
        // and the engine evaluates them by ascending Order — so registration order is cosmetic.
        services.AddSingleton<GraphMessageNormalizer>();
        services.AddSingleton<IAssociationRung, ExplicitReferenceRung>();      // rung 0 — explicit reference
        services.AddSingleton<IAssociationRung, ThreadContinuityRung>();       // rung 1 — thread continuity
        services.AddSingleton<IAssociationRung, ParticipantCorrelationRung>(); // rung 2 — participant correlation
        // rung 3 — structural detectors (NFR-04: adding a detector is a new IStructuralDetector
        // registration; the rung + engine are unchanged).
        services.AddSingleton<IStructuralDetector, CalendarInviteDetector>();
        services.AddSingleton<IStructuralDetector, ESignCompletionDetector>();
        services.AddSingleton<IStructuralDetector, InvoiceNumberDetector>();
        services.AddSingleton<IStructuralDetector, CourtEFilingDetector>();
        services.AddSingleton<IAssociationRung, StructuralDetectorRung>();     // rung 3 — structural detectors
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
        services.AddSingleton<AssociationStatusMapper>();
        services.AddSingleton<IncomingAssociationResolver>();
        services.AddSingleton<IncomingCommunicationProcessor>();

        // Direction-agnostic enrichment orchestrator (ADR-045 / FR-08). Invoked by BOTH the inbound
        // processor and the outbound send path so received and sent communications get identical
        // treatment. Registered UNCONDITIONALLY (consumed unconditionally by both callers per ADR-032;
        // no feature gate — no Null-Object peer required). Singleton mirrors IncomingCommunicationProcessor,
        // which already injects the scoped IPostUploadIndexingEnqueuer via the same pattern.
        services.AddSingleton<ICommunicationEnrichmentService, CommunicationEnrichmentService>();

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

        return services;
    }
}
