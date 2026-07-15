using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Communication;
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

        // Core services (singleton: all dependencies are singleton or options)
        services.AddSingleton<CommunicationAccountService>();
        services.AddSingleton<ApprovedSenderValidator>();
        services.AddSingleton<CommunicationService>();
        services.AddSingleton<EmlGenerationService>();
        services.AddSingleton<GraphMessageToEmlConverter>();
        services.AddSingleton<MailboxVerificationService>();

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
