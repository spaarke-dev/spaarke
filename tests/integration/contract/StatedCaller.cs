using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>What the caller's systemuser lookup (procedure-and-file-model section 5.3) finds for the test caller.</summary>
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
/// THE one stated caller for every test host and bare <c>HttpContext</c> in this assembly: the REAL
/// <see cref="CallerSystemUserResolver"/> and the REAL <see cref="SystemUserIdentityResolver"/>, substituted only at the
/// Dataverse seam (and a cache that stores nothing, so <see cref="Kind"/> can change between requests). Finance step 0
/// (ebilling-r1 task 099) and the external document narrowing (unified-access-control-r2 task 188) both ask these two
/// resolvers, so one statement of the caller answers both: a host never states the caller twice, and a second
/// registration can never replace the first. Every <c>/api/finance</c> route runs step 0, so a Finance host without
/// this registration refuses every caller (fail closed).
/// </summary>
/// <remarks>
/// The real resolver only resolves a GUID oid. A host whose token oid is a GUID (<c>FinanceAuthzTestAuthHandler.CallerObjectId</c>)
/// is asked under that oid; a host whose oid is not (the shared document fixtures' <c>test-user-…</c>) registers with
/// <c>normaliseOid: true</c>, and the real resolver is then asked with the oid claim replaced by <see cref="NormalisedOid"/>
/// (<see cref="GuidOidCallerResolver"/>; production oids are GUIDs).
/// </remarks>
public sealed class StatedCaller
{
    /// <summary>The systemuser the test caller's oid resolves to.</summary>
    public static readonly Guid SystemUserId = Guid.Parse("5e0c1a52-0000-4000-8000-000000000099");

    /// <summary>The GUID oid a host registered with <c>normaliseOid: true</c> is asked under.</summary>
    public static readonly Guid NormalisedOid = Guid.Parse("18800000-0000-0000-0000-00000000000a");

    private const string IsExternalColumn = "sprk_isexternal";

    /// <summary>The oid the lookup must ask for; only it resolves.</summary>
    public Guid ExpectedOid { get; set; } = Guid.Parse(Sprk.Bff.Api.Tests.Api.Finance.FinanceAuthzTestAuthHandler.CallerObjectId);

    /// <summary>What the lookup finds; settable between requests. Ignored while <see cref="KindSource"/> is set.</summary>
    public StepZeroCallerKind Kind { get; set; }

    /// <summary>When set, what the lookup finds is read from it on every query (a host stating the caller per request).</summary>
    public Func<StepZeroCallerKind>? KindSource { get; set; }

    /// <summary>Every lookup Dataverse query, in order (oid lookup, then the flag read).</summary>
    public ConcurrentQueue<QueryExpression> Queries { get; } = new();

    private StepZeroCallerKind CurrentKind => KindSource?.Invoke() ?? Kind;

    /// <summary>
    /// Registers the two resolvers (replacing any registered before), stated as <paramref name="kind"/>, or as
    /// <paramref name="kindSource"/> says per query when given.
    /// </summary>
    public static StatedCaller Register(
        IServiceCollection services,
        StepZeroCallerKind kind = StepZeroCallerKind.FlagFalse,
        bool normaliseOid = false,
        Func<StepZeroCallerKind>? kindSource = null)
    {
        var caller = new StatedCaller { Kind = kind, KindSource = kindSource };
        if (normaliseOid)
        {
            caller.ExpectedOid = NormalisedOid;
        }

        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) => caller.Answer(q));

        services.RemoveAll<ISystemUserIdentityResolver>();
        services.AddSingleton<ISystemUserIdentityResolver>(new SystemUserIdentityResolver(
            dataverse.Object, new NoStoreCache(), NullLogger<SystemUserIdentityResolver>.Instance));
        services.RemoveAll<ICallerSystemUserResolver>();
        services.AddScoped<ICallerSystemUserResolver>(sp =>
        {
            ICallerSystemUserResolver real = new CallerSystemUserResolver(
                sp.GetRequiredService<ISystemUserIdentityResolver>(), NullLogger<CallerSystemUserResolver>.Instance);
            return normaliseOid ? new GuidOidCallerResolver(real, NormalisedOid) : real;
        });
        return caller;
    }

    /// <summary>The lookup answer for a narrowing test's <see cref="NarrowingCallerMode"/>.</summary>
    public static StepZeroCallerKind For(NarrowingCallerMode mode) => mode switch
    {
        NarrowingCallerMode.Internal => StepZeroCallerKind.FlagFalse,
        NarrowingCallerMode.External => StepZeroCallerKind.FlagTrue,
        NarrowingCallerMode.LookupFault => StepZeroCallerKind.LookupFault,
        NarrowingCallerMode.ExternalFlagFault => StepZeroCallerKind.FlagReadFault,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

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
            throw new InvalidOperationException($"the caller lookup queried an unexpected table '{q.EntityName}'");
        }

        var kind = CurrentKind;
        if (q.ColumnSet.Columns.Contains(IsExternalColumn))
        {
            // The flag read must ask for THE CALLER's systemuser (the one the oid lookup returned): a read for any other
            // systemuserid finds no row (and so refuses, fail closed). Without this, a production bug that passed the wrong
            // id (the oid, Guid.Empty) would still get the caller's row and every must-not-fire test would stay green.
            var asksForTheCallersRow = q.Criteria.Conditions.Any(c =>
                c.AttributeName == "systemuserid" && c.Operator == ConditionOperator.Equal && c.Values.Count == 1 && c.Values[0] is Guid id && id == SystemUserId);
            if (!asksForTheCallersRow)
            {
                return Task.FromResult(new EntityCollection());
            }

            if (kind == StepZeroCallerKind.FlagReadFault)
            {
                return Task.FromException<EntityCollection>(new InvalidOperationException("sprk_isexternal read failed"));
            }

            var row = new Entity("systemuser", SystemUserId);
            if (kind != StepZeroCallerKind.FlagBlank)
            {
                row[IsExternalColumn] = kind == StepZeroCallerKind.FlagTrue;
            }

            return Task.FromResult(new EntityCollection([row]));
        }

        // The lookup must ask for THE CALLER's oid: a query for any other oid finds no row (and so refuses).
        var asksForTheCaller = q.Criteria.Conditions.Any(c =>
            c.AttributeName == "azureactivedirectoryobjectid" && c.Values.Count == 1 && c.Values[0] is Guid oid && oid == ExpectedOid);

        return kind switch
        {
            StepZeroCallerKind.LookupFault => Task.FromException<EntityCollection>(new InvalidOperationException("systemuser lookup failed")),
            StepZeroCallerKind.NoSystemUserRow => Task.FromResult(new EntityCollection()),
            _ when !asksForTheCaller => Task.FromResult(new EntityCollection()),
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

/// <summary>
/// The real <see cref="CallerSystemUserResolver"/>, asked with the principal's oid claim replaced by a fixed GUID (the shared
/// document fixtures' oid is not a GUID; production oids are).
/// </summary>
internal sealed class GuidOidCallerResolver(ICallerSystemUserResolver inner, Guid oid) : ICallerSystemUserResolver
{
    private const string OidClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public Task<CallerSystemUserResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct) =>
        inner.ResolveAsync(
            caller is null
                ? null
                : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(OidClaim, oid.ToString()) }, "Test")),
            ct);
}

namespace Sprk.Bff.Api.Tests.AccessControl
{
    /// <summary>Who the caller is, as the systemuser lookup answers (a narrowing test's vocabulary; <see cref="StatedCaller.For"/>).</summary>
    public enum NarrowingCallerMode
    {
        /// <summary>A resolved systemuser, not flagged external.</summary>
        Internal,

        /// <summary>A resolved systemuser flagged <c>sprk_isexternal</c>.</summary>
        External,

        /// <summary>The oid → systemuser lookup throws (the resolver reports <c>lookup-failed</c>).</summary>
        LookupFault,

        /// <summary>The systemuser resolves, but its external flag cannot be read.</summary>
        ExternalFlagFault,
    }
}

namespace Sprk.Bff.Api.Tests.TestInfrastructure
{
    /// <summary>
    /// unified-access-control-r2 task 188: states the caller (through <see cref="StatedCaller"/>, the one stated caller) and
    /// the app-only world THE external document narrowing (<c>InternalPlaneDocumentNarrowing</c>) reads, for a test host or a
    /// bare <c>DefaultHttpContext</c>. A host whose tests are about something else states
    /// <see cref="NarrowingCallerMode.Internal"/>: the narrowing then makes no call and no read, as for an internal user in
    /// production.
    /// </summary>
    internal static class StatedNarrowingCaller
    {
        /// <summary>Registers the stated caller and the world (both read per request, so a test may change them).</summary>
        public static IServiceCollection AddStatedNarrowingCaller(
            this IServiceCollection services, Func<NarrowingCallerMode> mode, Func<AppOnlyDocumentWorld> world)
        {
            StatedCaller.Register(services, normaliseOid: true, kindSource: () => StatedCaller.For(mode()));
            return services.AddNarrowingWorld(world);
        }

        /// <summary>
        /// Registers only the app-only world the narrowing reads, bound to the REAL predicate behind the real seam: for a
        /// host that states its caller with <see cref="StatedCaller.Register"/> itself.
        /// </summary>
        public static IServiceCollection AddNarrowingWorld(this IServiceCollection services, Func<AppOnlyDocumentWorld> world)
        {
            services.RemoveAll<InternalRouteDocumentNarrowing>();
            services.AddScoped<InternalRouteDocumentNarrowing>(_ => (ids, ct) => world().Narrowing(ids, ct));
            return services;
        }

        /// <summary>
        /// Request services for a bare <c>DefaultHttpContext</c> whose caller is a resolved INTERNAL systemuser: the narrowing
        /// makes no call and no read.
        /// </summary>
        public static IServiceProvider InternalCallerServices()
        {
            var world = new AppOnlyDocumentWorld();
            return new ServiceCollection().AddLogging()
                .AddStatedNarrowingCaller(() => NarrowingCallerMode.Internal, () => world)
                .BuildServiceProvider();
        }
    }
}
