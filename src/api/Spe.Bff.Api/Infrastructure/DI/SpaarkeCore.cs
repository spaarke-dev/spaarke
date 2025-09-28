using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
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

public class RequestCache
{
    private readonly Dictionary<string, object> _cache = new();

    public T? Get<T>(string key) where T : class
    {
        return _cache.TryGetValue(key, out var value) ? value as T : null;
    }

    public void Set<T>(string key, T value) where T : class
    {
        _cache[key] = value;
    }
}