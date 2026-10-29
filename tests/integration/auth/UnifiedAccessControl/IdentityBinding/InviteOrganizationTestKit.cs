using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// Slice B2 — builders for the invite's organisation lookup (<see cref="InviteOrganizationResolver"/>): a discovery endpoint
/// stood in for at the HTTP boundary by a scripted handler (no network; a concrete fake, not a mocked handler), and a
/// recording <see cref="ITenantCache"/> that keeps values as JSON, as Redis would.
/// </summary>
public static class InviteOrganizationTestKit
{
    /// <summary>The CIAM tenant the kit configures (<c>Ciam:TenantId</c>).</summary>
    public static readonly Guid CiamTenant = Guid.Parse("33333333-cccc-4ccc-8ccc-333333333333");

    /// <summary>
    /// A resolver for a deployment with no customer tenants: every invite is a portal-account invite and the discovery
    /// endpoint must never be called (the handler fails the test if it is).
    /// </summary>
    public static InviteOrganizationResolver PortalOnly()
        => Resolver(new DiscoveryHandler(), new RecordingTenantCache(), Array.Empty<Guid>());

    /// <summary>A resolver over <paramref name="handler"/> and <paramref name="cache"/> for <paramref name="customerTenants"/>.</summary>
    public static InviteOrganizationResolver Resolver(
        DiscoveryHandler handler, RecordingTenantCache cache, IEnumerable<Guid> customerTenants,
        TimeSpan? timeout = null, ILogger<InviteOrganizationResolver>? logger = null)
        => new(new SingleClientFactory(handler), cache, IdentityBindingTestKit.Tenants(customerTenants.ToArray()),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Ciam:TenantId"] = CiamTenant.ToString("D") })
                .Build(),
            logger ?? NullLogger<InviteOrganizationResolver>.Instance,
            timeout ?? InviteOrganizationResolver.DefaultLookupTimeout);

    /// <summary>The discovery body Entra answers for a known domain.</summary>
    public static string DiscoveryBody(Guid tenant)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["token_endpoint"] = $"https://login.microsoftonline.com/{tenant:D}/oauth2/v2.0/token",
            ["issuer"] = $"https://login.microsoftonline.com/{tenant:D}/v2.0",
            ["authorization_endpoint"] = $"https://login.microsoftonline.com/{tenant:D}/oauth2/v2.0/authorize",
        });

    /// <summary>The 400 body Entra answers for a domain no tenant owns (AADSTS90002).</summary>
    public const string TenantNotFoundBody =
        "{\"error\":\"invalid_tenant\",\"error_description\":\"AADSTS90002: Tenant 'unknown.example' not found. Check to make "
        + "sure you have the correct tenant ID and are signing into the correct cloud.\",\"error_codes\":[90002]}";

    /// <summary>
    /// The discovery endpoint, scripted per call: each response is a status and a body, or an exception to throw. Records
    /// every request. With nothing scripted, a call fails the test.
    /// </summary>
    public sealed class DiscoveryHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();

        /// <summary>Every request sent, in order.</summary>
        public List<HttpRequestMessage> Requests { get; } = new();

        /// <summary>Answers the next call with <paramref name="status"/> and <paramref name="body"/>.</summary>
        public DiscoveryHandler Answer(HttpStatusCode status, string body)
        {
            _script.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }));
            return this;
        }

        /// <summary>Answers the next call with <paramref name="response"/> as built.</summary>
        public DiscoveryHandler Answer(Func<HttpResponseMessage> response)
        {
            _script.Enqueue((_, _) => Task.FromResult(response()));
            return this;
        }

        /// <summary>The next call throws <paramref name="exception"/>.</summary>
        public DiscoveryHandler Throw(Exception exception)
        {
            _script.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));
            return this;
        }

        /// <summary>The next call never answers: it waits until its token is cancelled (a timeout).</summary>
        public DiscoveryHandler Hang()
        {
            _script.Enqueue(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("unreachable");
            });
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            if (!_script.TryDequeue(out var next))
            {
                throw new InvalidOperationException($"The discovery endpoint was called unexpectedly: {request.RequestUri}");
            }

            return next(request, ct);
        }
    }

    /// <summary>An <see cref="IHttpClientFactory"/> handing out clients over one handler, recording the names asked for.</summary>
    public sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<string> Names { get; } = new();

        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    /// <summary>An <see cref="ITenantCache"/> that keeps each value as JSON and records each write's key and TTL.</summary>
    public sealed class RecordingTenantCache : ITenantCache
    {
        private readonly ConcurrentDictionary<string, string> _values = new();

        /// <summary>Every write: (tenant, resource, id, version, JSON, TTL).</summary>
        public List<(string Tenant, string Resource, string Id, int Version, string Json, TimeSpan? Ttl)> Writes { get; } = new();

        /// <summary>When set, every read and write throws it (a cache outage).</summary>
        public Exception? Fault { get; set; }

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default",
            CancellationToken ct = default)
        {
            if (Fault is not null) throw Fault;
            return Task.FromResult(_values.TryGetValue(Key(tenantId, resource, id, version), out var json)
                ? JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : default);
        }

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            if (Fault is not null) throw Fault;
            var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            _values[Key(tenantId, resource, id, version)] = json;
            Writes.Add((tenantId, resource, id, version, json, ttl));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default",
            CancellationToken ct = default)
        {
            _values.TryRemove(Key(tenantId, resource, id, version), out _);
            return Task.CompletedTask;
        }

        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version,
            Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default",
            CancellationToken ct = default)
            => throw new NotSupportedException("The organisation lookup reads and writes; it never uses GetOrCreateAsync.");

        private static string Key(string tenant, string resource, string id, int version) => $"{tenant}|{resource}|{id}|{version}";
    }
}
