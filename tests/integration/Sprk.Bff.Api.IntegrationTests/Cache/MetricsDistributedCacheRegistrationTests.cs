using System.Diagnostics.Metrics;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Cache;

/// <summary>
/// FR-05 regression guard for <see cref="MetricsDistributedCache"/> registration and the
/// canonical single-Meter invariant from FR-02 (task 002).
/// </summary>
/// <remarks>
/// <para>
/// Two binding invariants verified post-DI:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       <c>IDistributedCache</c> resolves to <see cref="MetricsDistributedCache"/> wrapping
///       an inner cache (<see cref="MemoryDistributedCache"/> on the dev-fallback branch, or
///       a Redis-backed implementation when <c>Redis:Enabled=true</c>). Catches the R1
///       regression class where <c>CacheModule.DecorateDistributedCacheWithMetrics</c> is
///       silently dropped or the decorator is constructed without the inner cache reference.
///     </description>
///   </item>
///   <item>
///     <description>
///       Exactly ONE <see cref="Meter"/> named <c>Sprk.Bff.Api.Cache</c> exists at runtime.
///       Asserts task 002's outcome: the previous two-Meter shape (instance class in
///       <c>CacheMetrics</c> + static fields in <c>TenantCache</c>) is gone.
///     </description>
///   </item>
/// </list>
/// <para>
/// Fixture pattern: minimal <see cref="WebApplicationFactory{TEntryPoint}"/> with
/// <c>UseEnvironment("Development")</c> + <c>Redis:Enabled=false</c> +
/// <c>Redis:AllowInMemoryFallback=true</c> — trips CacheModule Branch (b) so the host can
/// build deterministically without a live Redis. Mirrors the R1 task 009 fixture-repair
/// pattern (<c>tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs</c>).
/// </para>
/// <para>
/// MeterListener approach (Test 2): the canonical <see cref="CacheMetrics.Meter"/> static
/// field is created at type-load time on the first reference to <see cref="CacheMetrics"/>.
/// To capture any rogue second Meter named the same, the listener is started BEFORE the
/// host is built (the host's call into <c>TelemetryModule.AddMeter</c> + any instrument
/// initialization runs during <c>BuildHost</c>). We then enumerate published instruments
/// and count distinct <see cref="Meter"/> instances whose <see cref="Meter.Name"/> matches.
/// </para>
/// </remarks>
[Trait("project", "spaarke-redis-cache-remediation-r2")]
[Trait("task", "005")]
[Trait("category", "integration")]
public sealed class MetricsDistributedCacheRegistrationTests
{
    [Fact]
    public void IDistributedCache_Resolves_To_MetricsDistributedCache_Wrapping_Inner()
    {
        // Arrange — build the full BFF DI graph via WAF with the R1 fixture-repair config.
        using var factory = new CacheRegistrationWebAppFactory();

        // Act — resolve IDistributedCache from the root provider (singleton-scoped per CacheModule).
        var cache = factory.Services.GetRequiredService<IDistributedCache>();

        // Assert (a) — runtime type IS the decorator (FR-05 binding).
        cache.Should().NotBeNull();
        cache.GetType().Name.Should().Be(
            nameof(MetricsDistributedCache),
            because: "CacheModule.DecorateDistributedCacheWithMetrics MUST wrap IDistributedCache with " +
                     "the MetricsDistributedCache decorator so cache.* Meter instruments fire on every call");

        // Assert (b) — the decorator's private `_inner` field references the underlying cache.
        // Reflection is justified here: the field is private + sealed by design (decorator
        // implementation detail), but the test must verify the wrapping topology (FR-05).
        var innerField = cache.GetType().GetField(
            "_inner",
            BindingFlags.NonPublic | BindingFlags.Instance);

        innerField.Should().NotBeNull(
            because: "MetricsDistributedCache MUST hold a private '_inner' IDistributedCache reference");

        var inner = innerField!.GetValue(cache) as IDistributedCache;
        inner.Should().NotBeNull(
            because: "the decorator MUST forward to a concrete inner IDistributedCache");

        // The inner type depends on which CacheModule branch fired. In this fixture the
        // dev-fallback branch (b) runs → MemoryDistributedCache. In a Redis-enabled host the
        // inner would be the StackExchange Redis-backed cache. Accept either to keep the
        // test branch-agnostic.
        inner!.GetType().Name.Should().BeOneOf(
            new[] { nameof(MemoryDistributedCache), "RedisCache" },
            because: "the decorator's inner cache should be MemoryDistributedCache (dev-fallback) " +
                     "or the StackExchange RedisCache (Redis-on) — never another decorator and " +
                     "never null");

        // Critical: the inner MUST NOT itself be a MetricsDistributedCache — that would mean
        // the decoration ran twice (double-counting metrics).
        inner.Should().NotBeOfType<MetricsDistributedCache>(
            because: "double-decoration would emit duplicate cache.hits/misses measurements per call");
    }

    [Fact]
    public void Exactly_One_Meter_Named_Sprk_Bff_Api_Cache_Exists()
    {
        // Arrange — start the MeterListener BEFORE the host builds so any Meter instances
        // created during DI graph construction (TelemetryModule.AddMeter + first reference
        // to CacheMetrics static class) publish their instruments to our listener.
        var observedMeters = new HashSet<Meter>(ReferenceEqualityComparer.Instance);

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == CacheMetrics.MeterName)
                {
                    lock (observedMeters)
                    {
                        observedMeters.Add(instrument.Meter);
                    }
                }
            }
        };
        listener.Start();

        // Force the canonical CacheMetrics static class to load. Touching any field triggers
        // the static constructor → the single Meter instance is created and its instruments
        // are published to the listener.
        _ = CacheMetrics.Meter;
        _ = CacheMetrics.HitsCounter;
        _ = CacheMetrics.MissesCounter;
        _ = CacheMetrics.FailuresCounter;
        _ = CacheMetrics.HitsByResourceCounter;
        _ = CacheMetrics.MissesByResourceCounter;
        _ = CacheMetrics.CallDurationHistogram;
        _ = CacheMetrics.LatencyHistogram;

        // Build the full BFF DI graph — if any other code path creates a second
        // Meter("Sprk.Bff.Api.Cache") instance, the listener observes its publication.
        using var factory = new CacheRegistrationWebAppFactory();
        _ = factory.Services.GetRequiredService<IDistributedCache>();
        _ = factory.Services.GetRequiredService<ITenantCache>();

        // Act — done in Arrange. Snapshot the set.
        Meter[] meters;
        lock (observedMeters)
        {
            meters = observedMeters.ToArray();
        }

        // Assert — exactly ONE Meter instance with name "Sprk.Bff.Api.Cache" exists.
        // FR-02 binding: the canonical static CacheMetrics class is the single owner.
        meters.Should().HaveCount(
            1,
            because: "FR-02 binding: exactly one Meter('Sprk.Bff.Api.Cache') instance exists at runtime; " +
                     "the previous shape (instance class CacheMetrics + static Meter in TenantCache) was " +
                     "collapsed to a single canonical static class in task 002");

        meters[0].Name.Should().Be(CacheMetrics.MeterName);
        meters[0].Should().BeSameAs(
            CacheMetrics.Meter,
            because: "the single Meter instance must be the canonical CacheMetrics.Meter static field");
    }

    /// <summary>
    /// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> sized to build the full BFF DI graph
    /// with no network IO. Mirrors the configuration surface of
    /// <c>tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs</c> (R1 task 009 fixture repair).
    /// </summary>
    private sealed class CacheRegistrationWebAppFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // ── ConnectionStrings ─────────────────────────────────────
                    ["ConnectionStrings:ServiceBus"] =
                        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==",

                    // ── CORS ──────────────────────────────────────────────────
                    ["Cors:AllowedOrigins:0"] = "https://localhost:5173",

                    // ── Azure AD / UAMI ───────────────────────────────────────
                    ["UAMI_CLIENT_ID"] = "test-client-id",
                    ["TENANT_ID"] = "test-tenant-id",
                    ["API_APP_ID"] = "test-app-id",
                    ["API_CLIENT_SECRET"] = "test-secret",
                    ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                    ["AzureAd:TenantId"] = "test-tenant-id",
                    ["AzureAd:ClientId"] = "test-app-id",
                    ["AzureAd:Audience"] = "api://test-app-id",

                    // ── Graph / Dataverse ─────────────────────────────────────
                    ["Graph:TenantId"] = "test-tenant-id",
                    ["Graph:ClientId"] = "test-client-id",
                    ["Graph:ClientSecret"] = "test-client-secret",
                    ["Graph:UseManagedIdentity"] = "false",
                    ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                    ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                    ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                    ["Dataverse:ClientId"] = "test-client-id",
                    ["Dataverse:ClientSecret"] = "test-client-secret",
                    ["Dataverse:TenantId"] = "test-tenant-id",
                    ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",

                    // ── ServiceBus ────────────────────────────────────────────
                    ["ServiceBus:ConnectionString"] =
                        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==",
                    ["ServiceBus:QueueName"] = "sdap-jobs",

                    // ── Redis: trip CacheModule Branch (b) (dev-fallback). The new test
                    //          asserts decorator wraps MemoryDistributedCache OR a Redis cache
                    //          type — either branch is acceptable. Dev-fallback is the
                    //          deterministic, network-free option for CI. ──────────────
                    ["Redis:Enabled"] = "false",
                    ["Redis:AllowInMemoryFallback"] = "true",

                    // ── Document Intelligence / Analysis / AI Search ──────────
                    ["DocumentIntelligence:Enabled"] = "true",
                    ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                    ["DocumentIntelligence:OpenAiKey"] = "test-key",
                    ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                    ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                    ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                    ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                    ["Analysis:Enabled"] = "true",
                    ["Analysis:UseStubResolver"] = "true",
                    ["ModelSelector:DefaultModel"] = "gpt-4o",
                    ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                    ["AzureOpenAI:ChatModelName"] = "gpt-4o",

                    // ── SpeAdmin / KeyVault ───────────────────────────────────
                    ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",

                    // ── CosmosPersistence (raw config read in Program.cs) ─────
                    ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                    ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",

                    // ── Office rate limit + Resilience options ────────────────
                    ["OfficeRateLimit:Enabled"] = "false",
                    ["AiSearchResilience:MaxRetryAttempts"] = "3",
                    ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                    ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                    ["GraphResilience:MaxRetryAttempts"] = "3",
                    ["GraphResilience:RetryDelay"] = "00:00:01",
                    ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                    ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",

                    // ── AgentService (DataAnnotations validation) ─────────────
                    ["AgentService:Enabled"] = "false",
                    ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                    ["AgentService:AgentId"] = "test-agent-id",
                    ["AgentService:MaxConcurrency"] = "4",
                    ["AgentService:ThreadCacheExpiryMinutes"] = "60",
                });
            });

            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // R1 task 009 / R2 task 005 fixture pattern: CacheModule Branch (b) requires
            // IsDevelopment(). UseEnvironment("Development") + AllowInMemoryFallback=true
            // is the deterministic, network-free path.
            builder.UseEnvironment("Development");
            builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = false;
                options.ValidateOnBuild = false;
            });

            builder.ConfigureTestServices(services =>
            {
                // Remove hosted services that depend on external systems not configured here.
                // Same pattern as CustomWebAppFactory; we never start the request pipeline,
                // only resolve IDistributedCache + ITenantCache from the DI root.
                services.RemoveAll<IHostedService>();
            });
        }
    }
}

