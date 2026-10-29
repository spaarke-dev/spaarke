// -----------------------------------------------------------------------------
// CustomerOwnerTeamsModule.cs
//
// #2429 (H7c) — DI registration of the customer owner teams handler (the per-handler module pattern of
// SecureRecordSetupModule, so the Worker's Program.cs gains one line). The keyed IProvisioningHandler forwarder is in
// HandlerDispatchRegistrationModule; HandlerRegistrationCompletenessTests resolves it from the real Worker composition root.
//
// H7c's only seam is H7b's ISecureRecordSetupDataverse (same environment, same identity), which AddH7bSecureRecordSetupHandler
// registers — call that first. No new option, no new app setting, no new secret.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams;

/// <summary>Registers <see cref="H7cCustomerOwnerTeamsHandler"/>.</summary>
public static class CustomerOwnerTeamsModule
{
    /// <summary>Adds H7c's handler (its Dataverse seam is H7b's — <c>AddH7bSecureRecordSetupHandler</c>).</summary>
    public static IServiceCollection AddH7cCustomerOwnerTeamsHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<H7cCustomerOwnerTeamsHandler>();
        return services;
    }
}
