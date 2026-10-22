using System.Security.Claims;
using Microsoft.Extensions.Configuration;
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

    /// <summary>
    /// The create-owner policy of a test binder whose test is not about who owns a created contact (the CIAM plane never creates;
    /// the member-test, binding and invite tests assert the binding, not the owner). Named for what it is: a binder that binds
    /// no owner. Tests of the owner use <see cref="OwnerAwareBinder"/> or the real DI registration.
    /// </summary>
    public static ContactCreateOwner NoOwnerUnderTest { get; } =
        ContactCreateOwner.NoneForForeignEnvironment("test binder: contact ownership is not under test");

    public static ContactIdentityBinder Binder(
        InMemoryContactIdentityStore store, BindingLogCapture<ContactIdentityBinder>? log = null, params Guid[] tenants)
        => new(store, Tenants(tenants.Length == 0 ? new[] { CustomerTenant } : tenants), new FakeTimeProvider(Now),
            log ?? new BindingLogCapture<ContactIdentityBinder>(), NoOwnerUnderTest);

    /// <summary>
    /// The configuration a <c>ContactIdentityBinderFactory</c> reads for this BFF's own environment (<c>Dataverse:ServiceUrl</c>),
    /// which <c>CreateBinder</c> refuses (ISS-005, #1673 review K2). Empty by default: a test that is not about that refusal
    /// targets whatever environment it names.
    /// </summary>
    public static IConfiguration FactoryConfiguration(string? ownServiceUrl = null)
        => new ConfigurationBuilder().AddInMemoryCollection(
            ownServiceUrl is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = ownServiceUrl }).Build();

    /// <summary>
    /// The DI-registered (own-environment) binder of a <c>RegistrationDataverseService</c> whose test never links a contact
    /// in the own environment: an empty in-memory store, so any use of it would show.
    /// </summary>
    public static ContactIdentityBinder InertOwnEnvironmentBinder() => Binder(new InMemoryContactIdentityStore());

    /// <summary>
    /// A binder over THIS BFF's own environment as DI builds it (ISS-005, #1673): its owner policy is
    /// <c>ContactCreateOwner.FromCustomerBusinessUnit</c>, so a Workforce-plane create is owned by the default owner team of
    /// <paramref name="customerBusinessUnitId"/>, or refused when that is unset.
    /// </summary>
    public static ContactIdentityBinder OwnerAwareBinder(
        InMemoryContactIdentityStore store, Guid? customerBusinessUnitId, IDictionary<string, string?>? configuration = null)
        => new(store, Tenants(CustomerTenant), new FakeTimeProvider(Now), new BindingLogCapture<ContactIdentityBinder>(),
            ContactCreateOwner.FromCustomerBusinessUnit(
                new StaticOptionsMonitor<ExternalAccessOptions>(new ExternalAccessOptions { CustomerBusinessUnitId = customerBusinessUnitId }),
                new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()).Build()));

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
