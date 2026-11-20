using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// GitHub #2851 (D-18): <c>HttpContext.Items</c> is created lazily and without synchronization, so two concurrent FIRST touches
/// on a fresh request can each get their own dictionary (a lost memo) or corrupt the feature collection (a read turned into
/// "unknown", a 503). <see cref="RequestAccessMemo.For"/> must therefore first run single-threaded, before any fan-out: the
/// caller-principal strategies create the memo before their three concurrent compositions. <see cref="CallerWhoAmIMemo"/>,
/// asked from fan-outs it does not own, makes its own first touch single-threaded instead.
/// </summary>
public sealed class RequestAccessMemoFirstTouchTests
{
    private const int Repetitions = 50;
    private const int WorkersPerComposition = 11; // 3 compositions x 11 = 33 concurrent first touches per repetition

    private static readonly Guid Contact = Guid.Parse("28510000-0000-4000-8000-0000000000c1");
    private static readonly Guid SystemUser = Guid.Parse("28510000-0000-4000-8000-0000000000c2");
    private const string Oid = "28510000-0000-4000-8000-0000000000c3";
    private const string Tenant = "28510000-0000-4000-8000-0000000000c4";
    private const string Email = "first-touch@customer.example";

    public static TheoryData<string> Planes => new() { "workforce-systemuser", "workforce-contact", "ciam" };

    // ── The entry creates the memo before the fan-out ─────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task TheStrategy_CreatesTheRequestMemo_BeforeItStartsAnyComposition(string plane)
    {
        var http = NewRequest();
        var memoPresentAtStart = new ConcurrentQueue<(string Entity, bool Present)>();

        var resolution = await ResolveAsync(plane, http, entity =>
        {
            // Called synchronously by the strategy as it starts each composition: nothing here touches Items first.
            memoPresentAtStart.Enqueue((entity, PeekMemo(http) is not null));
            return Task.FromResult(Set(entity));
        });

        resolution.IsResolved.Should().BeTrue();
        memoPresentAtStart.Select(c => c.Entity).Should().BeEquivalentTo("sprk_project", "sprk_matter", "sprk_workassignment");
        memoPresentAtStart.Should().OnlyContain(c => c.Present,
            "the strategy creates the request's memo single-threaded BEFORE the three concurrent compositions start (#2851)");
    }

    // ── Concurrent first touches, through the real entry ─────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task ConcurrentFirstTouches_InsideTheThreeCompositions_ShareOneMemo_AndNeverThrow(string plane)
    {
        for (var repetition = 0; repetition < Repetitions; repetition++)
        {
            var http = NewRequest();
            using var barrier = new Barrier(3 * WorkersPerComposition);
            var seen = new ConcurrentBag<RequestAccessMemo?>();
            var faults = new ConcurrentBag<Exception>();

            // Each composition fans out the way the real ones do (concurrent reads that each reach RequestAccessMemo.For),
            // on dedicated threads released together, so the first touches of Items really are simultaneous.
            Task<AccessibleRecordSet> FanOut(string entity)
            {
                var workers = Enumerable.Range(0, WorkersPerComposition).Select(_ => Task.Factory.StartNew(
                    () =>
                    {
                        try
                        {
                            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                            seen.Add(RequestAccessMemo.For(http));
                        }
                        catch (Exception ex)
                        {
                            faults.Add(ex);
                        }
                    },
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                return Task.WhenAll(workers).ContinueWith(_ => Set(entity), TaskScheduler.Default);
            }

            var resolution = await ResolveAsync(plane, http, FanOut);

            resolution.IsResolved.Should().BeTrue();
            faults.Should().BeEmpty($"no first touch of Items may fault (repetition {repetition})");
            seen.Should().HaveCount(3 * WorkersPerComposition);
            seen.Distinct().Should().ContainSingle($"every read of one request shares ONE memo (repetition {repetition})")
                .Which.Should().NotBeNull().And.BeSameAs(PeekMemo(http), "and it is the one the request keeps");
        }
    }

    // ── RequestAccessMemo.For: its own first touch is single-threaded (#2845 F-R2) ──────────────────

    [Fact]
    public async Task RequestAccessMemoFor_ConcurrentFirstCalls_OnAFreshRequestWithNoPreCreate_ShareOneMemo_AndNeverThrow()
    {
        const int threads = 32;
        const int repetitions = 200;

        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            var http = NewRequest(); // nothing has touched Items, and nothing pre-creates the memo
            using var barrier = new Barrier(threads);
            var seen = new ConcurrentBag<RequestAccessMemo?>();
            var faults = new ConcurrentBag<Exception>();

            var workers = Enumerable.Range(0, threads).Select(_ => Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                        seen.Add(RequestAccessMemo.For(http));
                    }
                    catch (Exception ex)
                    {
                        faults.Add(ex);
                    }
                },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            await Task.WhenAll(workers);

            faults.Should().BeEmpty($"no first touch of Items may fault (repetition {repetition})");
            seen.Should().HaveCount(threads);
            seen.Distinct().Should().ContainSingle($"every caller gets the ONE memo of the request (repetition {repetition})");
            PeekMemo(http).Should().BeSameAs(seen.First(), "the memo every caller got is the one the request keeps");
        }
    }

    // ── CallerWhoAmIMemo: its own first touch is single-threaded ─────────────────────────────────────

    [Fact]
    public async Task CallerWhoAmIMemo_ConcurrentFirstRemembers_OnAFreshRequest_LoseNoAnswer()
    {
        const int threads = 32;
        const string environment = "https://first-touch.crm.dynamics.com";

        for (var repetition = 0; repetition < Repetitions; repetition++)
        {
            var http = NewRequest();
            using var barrier = new Barrier(threads);
            var ids = Enumerable.Range(0, threads).Select(_ => Guid.NewGuid()).ToArray();
            var faults = new ConcurrentBag<Exception>();

            var workers = Enumerable.Range(0, threads).Select(i => Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        using var body = JsonDocument.Parse($"{{\"UserId\":\"{ids[i]:D}\"}}");
                        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                        CallerWhoAmIMemo.Remember(http, environment, $"token-{i}", body.RootElement);
                    }
                    catch (Exception ex)
                    {
                        faults.Add(ex);
                    }
                },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            await Task.WhenAll(workers);

            faults.Should().BeEmpty($"no first touch of Items may fault (repetition {repetition})");
            for (var i = 0; i < threads; i++)
            {
                CallerWhoAmIMemo.TryGetUserId(http, environment, $"token-{i}", out var userId)
                    .Should().BeTrue($"answer {i} was remembered and must not be lost to a second Items (repetition {repetition})");
                userId.Should().Be(ids[i]);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A fresh request whose <c>Items</c> nothing has touched yet (DefaultHttpContext creates it on first use).</summary>
    private static DefaultHttpContext NewRequest() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", Oid), new Claim("email", Email) }, "Bearer")),
    };

    /// <summary>The request's memo if one exists, read single-threaded (the test's own read never creates one).</summary>
    private static RequestAccessMemo? PeekMemo(HttpContext http) =>
        http.Items.TryGetValue(RequestAccessMemo.HttpContextItemsKey, out var held) ? held as RequestAccessMemo : null;

    private static AccessibleRecordSet Set(string entity) => new()
    {
        PrincipalKind = WorkforcePrincipalKind.ContactOnly,
        EntityType = entity,
        Rights = new Dictionary<Guid, AccessRights>(),
        Sources = new AccessibleRecordSetSources(false, true, false),
    };

    /// <summary>
    /// Resolves the caller through the real strategy of <paramref name="plane"/>, whose three compositions are
    /// <paramref name="compose"/> (called as each composition starts).
    /// </summary>
    private static Task<CallerPrincipalResolution> ResolveAsync(
        string plane, HttpContext http, Func<string, Task<AccessibleRecordSet>> compose)
    {
        var sets = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);

        if (plane == "ciam")
        {
            sets.Setup(s => s.ComposeForCiamContactAsync(Contact, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((Guid _, string entity, CancellationToken _) => compose(entity));
            var store = new InMemoryContactIdentityStore();
            store.AddContact(Contact, email: Email, oid: Oid, plane: IdentityPlaneMarker.External);
            return new CiamContactPrincipalStrategy(
                    IdentityBindingTestKit.Binder(store), sets.Object, Mock.Of<ILogger<CiamContactPrincipalStrategy>>())
                .ResolveAsync(http, CancellationToken.None);
        }

        var resolution = plane == "workforce-contact"
            ? WorkforcePrincipalResolution.ForContact(Contact, Oid, Tenant)
            : WorkforcePrincipalResolution.ForSystemUser(SystemUser, Contact, Oid, Tenant, Email);
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>())).ReturnsAsync(resolution);
        sets.Setup(s => s.ComposeAsync(resolution.Principal!, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((WorkforcePrincipal _, string entity, CancellationToken _) => compose(entity));
        return new WorkforcePrincipalStrategy(resolver.Object, sets.Object, Mock.Of<ILogger<WorkforcePrincipalStrategy>>())
            .ResolveAsync(http, CancellationToken.None);
    }
}
