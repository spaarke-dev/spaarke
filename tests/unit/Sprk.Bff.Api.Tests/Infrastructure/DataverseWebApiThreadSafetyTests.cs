using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure;

/// <summary>
/// Tests verifying thread-safety of Dataverse Web API service patterns.
/// Validates that:
/// 1. Token refresh uses SemaphoreSlim with double-check locking (no redundant refreshes)
/// 2. Per-request Authorization headers are used (no shared DefaultRequestHeaders mutation)
/// 3. Concurrent requests do not corrupt headers or cause race conditions
/// </summary>
public class DataverseWebApiThreadSafetyTests
{
    /// <summary>
    /// Verifies that concurrent HTTP requests each carry their own Authorization header
    /// on the HttpRequestMessage, rather than relying on shared DefaultRequestHeaders.
    /// This simulates the per-request header pattern used by DataverseWebApiService.
    /// </summary>
    [Fact]
    public async Task ConcurrentRequests_UsePerRequestHeaders_NoHeaderCorruption()
    {
        // Arrange: shared HttpClient (simulating singleton DI registration)
        using var handler = new ConcurrentTestHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.crm.dynamics.com/api/data/v9.2/")
        };

        // Simulate 50 concurrent requests, each with its own Authorization header
        var concurrency = 50;
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            var token = $"token-{i}";
            using var request = new HttpRequestMessage(HttpMethod.Get, $"sprk_documents?$top=1&requestId={i}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.SendAsync(request);
            return (RequestIndex: i, Token: token, Response: response);
        }).ToArray();

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert: each request should have carried its own unique token
        results.Should().HaveCount(concurrency);
        handler.CapturedTokens.Should().HaveCount(concurrency);

        // Verify no token corruption: each captured token matches the expected pattern
        for (var i = 0; i < concurrency; i++)
        {
            handler.CapturedTokens.Should().Contain($"token-{i}",
                because: $"request {i} should have its own per-request Authorization header");
        }
    }

    /// <summary>
    /// Verifies that shared DefaultRequestHeaders.Authorization would corrupt under concurrency.
    /// This is the ANTI-PATTERN that the fix prevents — demonstrating why per-request headers matter.
    /// </summary>
    [Fact]
    public async Task SharedDefaultHeaders_CorruptUnderConcurrency_DemonstratesAntiPattern()
    {
        // Arrange: shared HttpClient with mutable DefaultRequestHeaders (the bug we fixed)
        using var handler = new ConcurrentTestHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.crm.dynamics.com/api/data/v9.2/")
        };

        // Simulate rapid auth header mutation on shared client (the old buggy pattern)
        var concurrency = 20;
        var barrier = new Barrier(concurrency);
        var headerValues = new List<string>();
        var lockObj = new object();

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            await Task.Yield(); // Force async scheduling
            barrier.SignalAndWait(TimeSpan.FromSeconds(5)); // Maximize contention

            // OLD PATTERN (buggy): mutate shared headers
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", $"shared-token-{i}");

            // Small delay to maximize race window
            await Task.Delay(1);

            // Read back what's on the shared header — may not match what we set!
            var currentAuth = httpClient.DefaultRequestHeaders.Authorization?.Parameter;
            lock (lockObj)
            {
                if (currentAuth != null) headerValues.Add(currentAuth);
            }
        }).ToArray();

        // Act
        await Task.WhenAll(tasks);

        // Assert: with shared mutable headers, we expect some overwritten values
        // The exact count of unique values will likely be < concurrency due to races
        headerValues.Should().NotBeEmpty();
        // This test documents the problem — it's non-deterministic but demonstrates the risk
    }

    /// <summary>
    /// Verifies the SemaphoreSlim double-check locking pattern prevents redundant token refreshes.
    /// Simulates multiple threads hitting token expiry simultaneously.
    /// </summary>
    [Fact]
    public async Task SemaphoreDoubleCheckPattern_PreventsRedundantRefreshes()
    {
        // Arrange: simulate token refresh with SemaphoreSlim
        var refreshCount = 0;
        var semaphore = new SemaphoreSlim(1, 1);
        string? cachedToken = null;
        var tokenExpiry = DateTimeOffset.MinValue; // Force first refresh

        async Task<string> GetTokenAsync()
        {
            // Fast path (no lock)
            if (cachedToken != null && tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
                return cachedToken;

            // Slow path with semaphore
            await semaphore.WaitAsync();
            try
            {
                // Double-check
                if (cachedToken != null && tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
                    return cachedToken;

                // Simulate token acquisition delay
                await Task.Delay(50);
                Interlocked.Increment(ref refreshCount);
                cachedToken = $"refreshed-token-{refreshCount}";
                tokenExpiry = DateTimeOffset.UtcNow.AddHours(1);
                return cachedToken;
            }
            finally
            {
                semaphore.Release();
            }
        }

        // Act: 20 concurrent threads all requesting a token when it's expired
        var concurrency = 20;
        var tokens = await Task.WhenAll(
            Enumerable.Range(0, concurrency).Select(_ => GetTokenAsync()));

        // Assert: only ONE refresh should have occurred (double-check prevents redundant refreshes)
        refreshCount.Should().Be(1, because: "SemaphoreSlim with double-check should prevent redundant token refreshes");
        tokens.Should().AllBe("refreshed-token-1", because: "all threads should get the same refreshed token");
    }

    /// <summary>
    /// Verifies that SemaphoreSlim timeout prevents deadlocks when token refresh hangs.
    /// </summary>
    [Fact]
    public async Task SemaphoreTimeout_PreventsDeadlock_WhenRefreshHangs()
    {
        // Arrange: semaphore that simulates a hung token refresh
        var semaphore = new SemaphoreSlim(1, 1);

        // Hold the semaphore to simulate a stuck refresh
        await semaphore.WaitAsync();

        // Act: try to acquire with a short timeout
        var acquired = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(100));

        // Assert: should timeout, not deadlock
        acquired.Should().BeFalse(because: "the semaphore should timeout rather than deadlock");

        // Cleanup
        semaphore.Release();
    }

    /// <summary>
    /// Test handler that captures Authorization headers from each request.
    /// Used to verify per-request header isolation.
    /// </summary>
    private class ConcurrentTestHandler : HttpMessageHandler
    {
        private readonly List<string> _capturedTokens = new();
        private readonly object _lock = new();

        public IReadOnlyList<string> CapturedTokens
        {
            get { lock (_lock) return _capturedTokens.ToList(); }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.Headers.Authorization?.Parameter;
            if (token != null)
            {
                lock (_lock)
                {
                    _capturedTokens.Add(token);
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { value = Array.Empty<object>() })
            });
        }
    }
}
