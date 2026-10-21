using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;

/// <summary>What the step-0 lookup (procedure-and-file-model section 5.3) finds for the test caller.</summary>
public enum StepZeroCallerKind
{
    /// <summary>A systemuser row with <c>sprk_isexternal</c> false — internal.</summary>
    FlagFalse,

    /// <summary>A systemuser row with <c>sprk_isexternal</c> blank — internal (owner round 67).</summary>
    FlagBlank,

    /// <summary>A systemuser row with <c>sprk_isexternal</c> true — refused.</summary>
    FlagTrue,

    /// <summary>No active systemuser row for the token's oid — refused.</summary>
    NoSystemUserRow,

    /// <summary>The <c>sprk_isexternal</c> read faults — refused.</summary>
    FlagReadFault,

    /// <summary>The oid → systemuser lookup faults — refused.</summary>
    LookupFault,
}

/// <summary>
/// Registers the step-0 caller resolution for a Finance test host (ebilling-r1 task 099): the REAL
/// <see cref="CallerSystemUserResolver"/> and the REAL <see cref="SystemUserIdentityResolver"/>, substituted only at the
/// Dataverse seam (and a cache that stores nothing, so <see cref="Kind"/> can change between requests). Every
/// <c>/api/finance</c> route runs step 0, so a host without this registration refuses every caller (fail closed).
/// The caller's <c>oid</c> must be a GUID (the real resolver's rule): <c>FinanceAuthzTestAuthHandler.CallerObjectId</c> is.
/// </summary>
public sealed class FinanceStepZeroCaller
{
    /// <summary>The systemuser the test caller's oid resolves to.</summary>
    public static readonly Guid SystemUserId = Guid.Parse("5e0c1a52-0000-4000-8000-000000000099");

    private const string IsExternalColumn = "sprk_isexternal";

    /// <summary>What the lookup finds; settable between requests.</summary>
    public StepZeroCallerKind Kind { get; set; }

    /// <summary>Every step-0 Dataverse query, in order (oid lookup, then the flag read).</summary>
    public ConcurrentQueue<QueryExpression> Queries { get; } = new();

    public static FinanceStepZeroCaller Register(IServiceCollection services, StepZeroCallerKind kind = StepZeroCallerKind.FlagFalse)
    {
        var caller = new FinanceStepZeroCaller { Kind = kind };
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) => caller.Answer(q));

        services.AddSingleton<ISystemUserIdentityResolver>(new SystemUserIdentityResolver(
            dataverse.Object, new NoStoreCache(), NullLogger<SystemUserIdentityResolver>.Instance));
        services.AddScoped<ICallerSystemUserResolver>(sp => new CallerSystemUserResolver(
            sp.GetRequiredService<ISystemUserIdentityResolver>(), NullLogger<CallerSystemUserResolver>.Instance));
        return caller;
    }

    /// <summary>True for the kinds step 0 refuses.</summary>
    public static bool Refuses(StepZeroCallerKind kind) =>
        kind is not (StepZeroCallerKind.FlagFalse or StepZeroCallerKind.FlagBlank);

    /// <summary>The four kinds step 0 must refuse (must-fire).</summary>
    public static readonly StepZeroCallerKind[] RefusedKinds =
    [
        StepZeroCallerKind.FlagTrue, StepZeroCallerKind.NoSystemUserRow, StepZeroCallerKind.FlagReadFault, StepZeroCallerKind.LookupFault,
    ];

    /// <summary>The two kinds step 0 must admit (must-not-fire).</summary>
    public static readonly StepZeroCallerKind[] InternalKinds = [StepZeroCallerKind.FlagFalse, StepZeroCallerKind.FlagBlank];

    /// <summary>Every row of <paramref name="rows"/> once per kind, the kind appended as the last argument.</summary>
    public static IEnumerable<object[]> Cross(IEnumerable<object[]> rows, IEnumerable<StepZeroCallerKind> kinds) =>
        rows.SelectMany(row => kinds.Select(kind => row.Append(kind).ToArray())).ToList();

    /// <summary>
    /// Asserts <paramref name="response"/> is step 0's refusal for <paramref name="kind"/> in the route's own shape: the uniform
    /// 404 on a UniformNotFound route, otherwise 403 with <c>internal_callers_only</c> (flag true or no row) or
    /// <c>system_failure</c> (a fault), and that the step-0 lookup ran.
    /// </summary>
    public async Task AssertRefusedAsync(HttpResponseMessage response, StepZeroCallerKind kind, bool uniformNotFound)
    {
        Xunit.Assert.True(Refuses(kind), $"{kind} is not a refused kind");
        Xunit.Assert.False(Queries.IsEmpty, "the refusal must come from the step-0 lookup");
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var reasonCode = body.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
        if (uniformNotFound)
        {
            Xunit.Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            Xunit.Assert.Equal(Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter.RecordUnavailableReasonCode, reasonCode);
            return;
        }

        Xunit.Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Xunit.Assert.Equal(
            kind is StepZeroCallerKind.FlagTrue or StepZeroCallerKind.NoSystemUserRow
                ? Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter.InternalCallersOnlyReasonCode
                : Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter.SystemFailureReasonCode,
            reasonCode);
    }

    private Task<EntityCollection> Answer(QueryExpression q)
    {
        Queries.Enqueue(q);
        if (q.EntityName != "systemuser")
        {
            throw new InvalidOperationException($"step 0 queried an unexpected table '{q.EntityName}'");
        }

        if (q.ColumnSet.Columns.Contains(IsExternalColumn))
        {
            if (Kind == StepZeroCallerKind.FlagReadFault)
            {
                return Task.FromException<EntityCollection>(new InvalidOperationException("sprk_isexternal read failed"));
            }

            var row = new Entity("systemuser", SystemUserId);
            if (Kind != StepZeroCallerKind.FlagBlank)
            {
                row[IsExternalColumn] = Kind == StepZeroCallerKind.FlagTrue;
            }

            return Task.FromResult(new EntityCollection([row]));
        }

        return Kind switch
        {
            StepZeroCallerKind.LookupFault => Task.FromException<EntityCollection>(new InvalidOperationException("systemuser lookup failed")),
            StepZeroCallerKind.NoSystemUserRow => Task.FromResult(new EntityCollection()),
            _ => Task.FromResult(new EntityCollection([new Entity("systemuser", SystemUserId)])),
        };
    }

    private sealed class NoStoreCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }
}
