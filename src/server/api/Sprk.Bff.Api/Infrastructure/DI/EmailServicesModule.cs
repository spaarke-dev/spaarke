namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for the email attachment/EML infrastructure shared by two LIVE
/// consumers: the Office Add-in upload-finalization worker
/// (<see cref="Sprk.Bff.Api.Workers.Office.UploadFinalizationWorker"/>, wired separately in
/// <see cref="Sprk.Bff.Api.Workers.Office.OfficeWorkersModule"/>) and the Graph-based inbound
/// communication pipeline (<see cref="Sprk.Bff.Api.Services.Communication.IncomingCommunicationProcessor"/>,
/// which calls <c>IEmailAttachmentProcessor.ShouldFilterAttachment</c>).
/// </summary>
/// <remarks>
/// The legacy OOB-`email`-activity subsystem previously registered here
/// (<c>EmailProcessingStatsService</c>, <c>IEmailAssociationService</c>/<c>EmailAssociationService</c>
/// \u2014 which built its own <c>ConfidentialClientApplication</c>, an ADR-028 drift point \u2014 and the
/// unused "DataversePolling" HttpClient) was retired by email-communication-solution-r4 task 007
/// (DEC-2/FR-07). <c>EmailToEmlConverter</c>, <c>AttachmentFilterService</c>, and
/// <c>EmailAttachmentProcessor</c> are KEPT \u2014 they are live dependencies of the two consumers
/// above, not remnants of the retired subsystem. See ADR-045.
/// </remarks>
public static class EmailServicesModule
{
    /// <summary>
    /// Adds communication telemetry, email-to-EML converter, attachment processor, and
    /// filter service (shared infra \u2014 see class remarks for why these survive OOB-email retirement).
    /// </summary>
    public static IServiceCollection AddEmailServicesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Communication telemetry
        services.AddSingleton<Sprk.Bff.Api.Telemetry.CommunicationTelemetry>();

        // Document telemetry
        services.AddSingleton<Sprk.Bff.Api.Telemetry.DocumentTelemetry>();

        // RAG telemetry
        services.AddSingleton<Sprk.Bff.Api.Telemetry.RagTelemetry>();

        // Email Processing configuration.
        // task 061 fail-fast sweep: bound under the canonical AddOptions chain with ValidateOnStart.
        // Behavior-neutral — the options class carries NO DataAnnotations. WebhookSigningKey is a nullable
        // secret whose "required-when-webhook-enabled" semantics stay at the webhook use-site (a fail-closed
        // 401 when unset): the class defaults Enabled=true / EnableWebhook=true, so a bare [Required] or an
        // Enabled→key IValidateOptions with ValidateOnStart would fail every test-host boot (no fixture seeds
        // the "EmailProcessing" section) — that would break valid config, not just misconfig. See task-061 notes.
        services.AddOptions<Sprk.Bff.Api.Configuration.EmailProcessingOptions>()
            .Bind(configuration.GetSection(Sprk.Bff.Api.Configuration.EmailProcessingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Email-to-EML converter \u2014 live dependency of Workers/Office/UploadFinalizationWorker.
        services.AddHttpClient<Sprk.Bff.Api.Services.Email.EmailToEmlConverter>();
        services.AddScoped<Sprk.Bff.Api.Services.Email.IEmailToEmlConverter>(sp =>
            sp.GetRequiredService<Sprk.Bff.Api.Services.Email.EmailToEmlConverter>());

        // Email attachment processor \u2014 live dependency of
        // Services/Communication/IncomingCommunicationProcessor (ShouldFilterAttachment).
        services.AddScoped<Sprk.Bff.Api.Services.Email.IEmailAttachmentProcessor,
            Sprk.Bff.Api.Services.Email.EmailAttachmentProcessor>();

        // Attachment filter service \u2014 live dependency of Workers/Office/UploadFinalizationWorker.
        services.AddScoped<Sprk.Bff.Api.Services.Email.AttachmentFilterService>();

        // D9-01 (config-deployment): removed Console.WriteLine startup echo that bypassed the
        // ILogger/OTel pipeline (pre-host-build registration context; the registrations are the
        // source of truth).

        return services;
    }
}
