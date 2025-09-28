using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Core.Cache;
using Spaarke.Dataverse;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class SpaarkeCore
{
    public static IServiceCollection AddSpaarkeCore(this IServiceCollection services)
    {
        // Don't add authorization here since it's already in Program.cs

        // SDAP Authorization services
        services.AddScoped<Spaarke.Core.Auth.AuthorizationService>();
        services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();

        // Authorization rules (registered in order of execution)
        services.AddScoped<IAuthorizationRule, ExplicitDenyRule>();
        services.AddScoped<IAuthorizationRule, ExplicitGrantRule>();
        services.AddScoped<IAuthorizationRule, TeamMembershipRule>();

        // Request cache for per-request memoization
        services.AddScoped<RequestCache>();

        return services;
    }
}