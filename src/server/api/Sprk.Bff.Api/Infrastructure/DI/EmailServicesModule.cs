namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for email-to-document conversion services (ADR-010).
/// </summary>
public static class EmailServicesModule
{
    /// <summary>
    /// Adds email processing stats, telemetry, email-to-EML converter,
    /// association service, attachment processor, and filter service.
    /// </summary>
    public static IServiceCollection AddEmailServicesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Email processing stats - in-memory stats readable via API
        services.AddSingleton<Sprk.Bff.Api.Services.Email.EmailProcessingStatsService>();

        // Communication telemetry
        services.AddSingleton<Sprk.Bff.Api.Telemetry.CommunicationTelemetry>(sp =>
            new Sprk.Bff.Api.Telemetry.CommunicationTelemetry(sp.GetService<Sprk.Bff.Api.Services.Email.EmailProcessingStatsService>()));

        // Document telemetry
        services.AddSingleton<Sprk.Bff.Api.Telemetry.DocumentTelemetry>();

        // RAG telemetry
        services.AddSingleton<Sprk.Bff.Api.Telemetry.RagTelemetry>();

        // Email Processing configuration
        services.Configure<Sprk.Bff.Api.Configuration.EmailProcessingOptions>(
            configuration.GetSection(Sprk.Bff.Api.Configuration.EmailProcessingOptions.SectionName));

        // Email-to-EML converter
        services.AddHttpClient<Sprk.Bff.Api.Services.Email.EmailToEmlConverter>();
        services.AddScoped<Sprk.Bff.Api.Services.Email.IEmailToEmlConverter>(sp =>
            sp.GetRequiredService<Sprk.Bff.Api.Services.Email.EmailToEmlConverter>());

        // Email association service
        services.AddScoped<Sprk.Bff.Api.Services.Email.IEmailAssociationService,
            Sprk.Bff.Api.Services.Email.EmailAssociationService>();

        // Email attachment processor
        services.AddScoped<Sprk.Bff.Api.Services.Email.IEmailAttachmentProcessor,
            Sprk.Bff.Api.Services.Email.EmailAttachmentProcessor>();

        // Attachment filter service
        services.AddScoped<Sprk.Bff.Api.Services.Email.AttachmentFilterService>();

        // HttpClient for email polling backup service
        services.AddHttpClient("DataversePolling")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        Console.WriteLine("\u2713 Email-to-Document conversion services registered");

        return services;
    }
}
