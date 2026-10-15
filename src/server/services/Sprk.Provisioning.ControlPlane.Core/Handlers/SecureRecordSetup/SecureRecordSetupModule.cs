// -----------------------------------------------------------------------------
// SecureRecordSetupModule.cs
//
// T256 (H7b) — DI registration of the Secure Record setup handler and its one seam (the per-handler module pattern of
// AppConfigSeedModule / E2EAcceptanceModule, so the Worker's Program.cs gains one line). The keyed IProvisioningHandler
// forwarder is in HandlerDispatchRegistrationModule; HandlerRegistrationCompletenessTests resolves it from the real
// Worker composition root.
//
// Depends on what Program.cs already registers for H7: EnvVarValuesOptions (bound + validated at start — H7b signs in
// with the same identity and options) and the singleton WorkerDataverseCredentialFactory. No new option, no new app
// setting, no new secret.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

/// <summary>Registers <see cref="H7bSecureRecordSetupHandler"/> and <see cref="ISecureRecordSetupDataverse"/>.</summary>
public static class SecureRecordSetupModule
{
    /// <summary>Adds H7b's handler, its Dataverse seam and the seam's named HttpClient.</summary>
    public static IServiceCollection AddH7bSecureRecordSetupHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient(DataverseWebApiSecureRecordSetup.HttpClientName);
        services.AddScoped<ISecureRecordSetupDataverse, DataverseWebApiSecureRecordSetup>();
        services.AddScoped<H7bSecureRecordSetupHandler>();
        return services;
    }
}
