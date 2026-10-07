using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>Shared builders for task 141's identity-binding tests.</summary>
public static class IdentityBindingTestKit
{
    /// <summary>A customer workforce tenant ("T").</summary>
    public static readonly Guid CustomerTenant = Guid.Parse("11111111-aaaa-4aaa-8aaa-111111111111");

    /// <summary>The tenant the app registration lives in under Model 1 ("S") — NOT a customer tenant.</summary>
    public static readonly Guid SpaarkeTenant = Guid.Parse("22222222-bbbb-4bbb-8bbb-222222222222");

    public static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    public static IOptionsMonitor<WorkforceIdentityOptions> Tenants(params Guid[] tenants)
        => new StaticOptionsMonitor<WorkforceIdentityOptions>(new WorkforceIdentityOptions
        {
            CustomerTenantIds = tenants.Select(t => t.ToString("D")).ToList(),
        });

    public static ContactIdentityBinder Binder(
        InMemoryContactIdentityStore store, BindingLogCapture<ContactIdentityBinder>? log = null, params Guid[] tenants)
        => new(store, Tenants(tenants.Length == 0 ? new[] { CustomerTenant } : tenants), new FakeTimeProvider(Now),
            log ?? new BindingLogCapture<ContactIdentityBinder>());

    /// <summary>A delegated (user) workforce token. Every claim is optional so a test can drop exactly one.</summary>
    public static ClaimsPrincipal WorkforceUser(
        Guid oid, Guid? tid = null, string? acct = "0", string? email = "person@customer.example",
        string? givenName = "Pat", string? familyName = "Example", bool delegated = true)
    {
        var claims = new List<Claim> { new("oid", oid.ToString("D")) };
        if (tid is { } t) claims.Add(new Claim("tid", t.ToString("D")));
        if (acct is not null) claims.Add(new Claim("acct", acct));
        if (email is not null) claims.Add(new Claim("email", email));
        if (givenName is not null) claims.Add(new Claim("given_name", givenName));
        if (familyName is not null) claims.Add(new Claim("family_name", familyName));
        if (delegated) claims.Add(new Claim("scp", "user_impersonation"));
        // A user token's sub is a pairwise id, never the oid.
        claims.Add(new Claim("sub", "pairwise-" + oid.ToString("N")));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestWorkforce"));
    }

    /// <summary>
    /// An APP-ONLY token as Entra issues it to this BFF: no scp, sub == oid, and NO idtyp (an optional claim
    /// this repo's registrations do not configure). <c>CallerIdentity.FromPrincipal</c> classifies it
    /// Application from sub == oid.
    /// </summary>
    public static ClaimsPrincipal AppOnlyToken(Guid servicePrincipalOid, Guid tid, string? acct = "0")
    {
        var claims = new List<Claim>
        {
            new("oid", servicePrincipalOid.ToString("D")),
            new("sub", servicePrincipalOid.ToString("D")),
            new("tid", tid.ToString("D")),
            new("appid", Guid.NewGuid().ToString("D")),
        };
        if (acct is not null) claims.Add(new Claim("acct", acct));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestWorkforce"));
    }

    public static CollisionFlag Flag(IdentityCollisionReason reason, Guid? oid, IdentityPlaneMarker plane = IdentityPlaneMarker.Workforce)
        => new(Now.AddDays(-1), oid, plane, reason);

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

/// <summary>Records formatted log lines (with level) — what an operator would read in App Insights.</summary>
public sealed class BindingLogCapture<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IEnumerable<string> Messages => Entries.Select(e => e.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
