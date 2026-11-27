// ISS-010 part D1 (#3184): the per-request RecordOwnerLookupCache — one read per request of the business-unit and team
// configuration rows a decision commit's creates ask again and again (R1 the Secure Record business unit by name, R2 the Secure
// Record owner team, R4 a unit's default owner team, R7 the acting user's unit by Entra object id). The binding conditions
// (uac-r2 and field-mapping-r1, 2026-11-26) are pinned here: a request keeps only a successful, non-empty answer; a fault, a
// cancellation, "not found" and a refusal are removed so the next read goes to the wire; concurrent first reads issue one call;
// two requests share nothing; R1/R2 are keyed on the configured VALUE; R7 is kept per request only; a request's instance keeps
// nothing a pass keeps besides R1, R2, R4 and R7. A pass keeps R1 and R4 exactly as before D1 (every answer that did not fault).

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

[Trait("status", "iss-010-d1")]
public class RecordOwnerLookupCacheTests
{
    private static readonly Guid Unit = Guid.Parse("d1d1d1d1-0000-4000-8000-000000000001");
    private static readonly Guid Team = Guid.Parse("d1d1d1d1-0000-4000-8000-000000000002");

    private static RecordOwnerLookupCache RequestCache() => RecordOwnerLookupCache.ForRequest(new DefaultHttpContext())!;

    /// <summary>A read that counts its calls and answers each from <paramref name="answer"/> (by call number, from 1).</summary>
    private sealed class Wire<T>(Func<int, Task<T>> answer)
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<T> Read() => answer(Interlocked.Increment(ref _calls));
    }

    // ── The request scope ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneRequest_HasOneInstance_AndTwoRequestsShareNothing()
    {
        var first = new DefaultHttpContext();
        var second = new DefaultHttpContext();
        var a = RecordOwnerLookupCache.ForRequest(first)!;

        RecordOwnerLookupCache.ForRequest(first).Should().BeSameAs(a, "every create of one request shares its instance");
        RecordOwnerLookupCache.ForRequest(second).Should().NotBeSameAs(a, "another request gets its own");
        a.RequestScoped.Should().BeTrue();
        new RecordOwnerLookupCache().RequestScoped.Should().BeFalse("a pass's cache is not a request's");

        var wire = new Wire<(Guid?, bool)>(_ => Task.FromResult<(Guid?, bool)>((Unit, false)));
        await a.SecureBusinessUnitAsync("Secure Record", wire.Read);
        await a.SecureBusinessUnitAsync("Secure Record", wire.Read);
        await RecordOwnerLookupCache.ForRequest(second)!.SecureBusinessUnitAsync("Secure Record", wire.Read);
        wire.Calls.Should().Be(2, "once per request: the second request reads its own answer");
    }

    [Fact]
    public void NoRequest_OrAnEndedOne_GetsNoCache()
    {
        RecordOwnerLookupCache.ForRequest(null).Should().BeNull();
        var ended = new DefaultHttpContext { Items = new EndedItems() };
        RecordOwnerLookupCache.ForRequest(ended).Should().BeNull("an ended request reads every answer, as before");
    }

    // ── What is kept ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task R1_AFoundUnit_IsReadOnce_ButNotFoundAndAmbiguousAreReadAgain()
    {
        var cache = RequestCache();
        var found = new Wire<(Guid?, bool)>(_ => Task.FromResult<(Guid?, bool)>((Unit, false)));
        (await cache.SecureBusinessUnitAsync("Secure Record", found.Read)).Should().Be(((Guid?)Unit, false));
        (await cache.SecureBusinessUnitAsync("Secure Record", found.Read)).Should().Be(((Guid?)Unit, false));
        found.Calls.Should().Be(1);

        var missing = new Wire<(Guid?, bool)>(_ => Task.FromResult<(Guid?, bool)>((null, false)));
        await cache.SecureBusinessUnitAsync("Missing", missing.Read);
        await cache.SecureBusinessUnitAsync("Missing", missing.Read);
        missing.Calls.Should().Be(2, "not found is never kept: a misconfigured name refuses the same way on every create");

        var ambiguous = new Wire<(Guid?, bool)>(_ => Task.FromResult<(Guid?, bool)>((null, true)));
        await cache.SecureBusinessUnitAsync("Twice", ambiguous.Read);
        await cache.SecureBusinessUnitAsync("Twice", ambiguous.Read);
        ambiguous.Calls.Should().Be(2, "an ambiguous unit is a refusal, never kept");
    }

    [Fact]
    public async Task R1_IsKeyedOnTheConfiguredName_SoAChangedNameIsReadAgain()
    {
        var cache = RequestCache();
        var wire = new Wire<(Guid?, bool)>(_ => Task.FromResult<(Guid?, bool)>((Unit, false)));

        await cache.SecureBusinessUnitAsync("Secure Record", wire.Read);
        await cache.SecureBusinessUnitAsync("Secure Records (renamed)", wire.Read);

        wire.Calls.Should().Be(2, "uac-r2 (b): a reloaded name is never served the old unit");
    }

    [Fact]
    public async Task R2_AFoundTeam_IsReadOnce_ButNoneAndARefusalAreReadAgain_AndTheKeyIsBothNames()
    {
        var cache = RequestCache();
        var found = new Wire<(Guid?, string?)>(_ => Task.FromResult<(Guid?, string?)>((Team, null)));
        await cache.SecureOwnerTeamAsync("Secure Record", "Secure Record Owners", found.Read);
        await cache.SecureOwnerTeamAsync("Secure Record", "Secure Record Owners", found.Read);
        found.Calls.Should().Be(1);
        await cache.SecureOwnerTeamAsync("Secure Record", "Other Owners", found.Read);
        await cache.SecureOwnerTeamAsync("Other Unit", "Secure Record Owners", found.Read);
        found.Calls.Should().Be(3, "the configured unit and team names are the key");

        var none = new Wire<(Guid?, string?)>(_ => Task.FromResult<(Guid?, string?)>((null, null)));
        await cache.SecureOwnerTeamAsync("x", "y", none.Read);
        await cache.SecureOwnerTeamAsync("x", "y", none.Read);
        none.Calls.Should().Be(2);

        var refused = new Wire<(Guid?, string?)>(_ => Task.FromResult<(Guid?, string?)>((null, "two teams")));
        await cache.SecureOwnerTeamAsync("x", "z", refused.Read);
        await cache.SecureOwnerTeamAsync("x", "z", refused.Read);
        refused.Calls.Should().Be(2);
    }

    [Fact]
    public async Task R4_AnOwnedTeam_IsReadOnce_ButARefusalIsReadAgain()
    {
        var cache = RequestCache();
        var owned = new Wire<RecordOwnerResolution>(_ => Task.FromResult(RecordOwnerResolution.Owned(Team)));
        await cache.DefaultOwnerTeamAsync(Unit, owned.Read);
        await cache.DefaultOwnerTeamAsync(Unit, owned.Read);
        owned.Calls.Should().Be(1);

        var other = Guid.NewGuid();
        var refused = new Wire<RecordOwnerResolution>(_ =>
            Task.FromResult(RecordOwnerResolution.Refused(RecordOwnerRefusal.NoDefaultOwnerTeam, "no default team")));
        (await cache.DefaultOwnerTeamAsync(other, refused.Read)).IsRefused.Should().BeTrue();
        (await cache.DefaultOwnerTeamAsync(other, refused.Read)).IsRefused.Should().BeTrue();
        refused.Calls.Should().Be(2);
    }

    [Fact]
    public async Task R7_OneEnabledUserWithAUnit_IsReadOnce_ButNoneOrTwoAreReadAgain()
    {
        var cache = RequestCache();
        var oid = Guid.NewGuid();
        var one = new Wire<RecordOwnerLookupCache.ActingUserRow>(_ => Task.FromResult(new RecordOwnerLookupCache.ActingUserRow(1, Unit, false)));
        await cache.ActingUserAsync(oid, one.Read);
        await cache.ActingUserAsync(oid, one.Read);
        one.Calls.Should().Be(1);

        foreach (var row in new[]
                 {
                     new RecordOwnerLookupCache.ActingUserRow(0, null, null),
                     new RecordOwnerLookupCache.ActingUserRow(2, Unit, false),
                     new RecordOwnerLookupCache.ActingUserRow(1, null, false),
                 })
        {
            var key = Guid.NewGuid();
            var wire = new Wire<RecordOwnerLookupCache.ActingUserRow>(_ => Task.FromResult(row));
            await cache.ActingUserAsync(key, wire.Read);
            await cache.ActingUserAsync(key, wire.Read);
            wire.Calls.Should().Be(2, $"{row} is not a single user with a unit");
        }
    }

    // ── A pass keeps what it kept before D1 (review F2) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task APass_KeepsNotFoundAndAmbiguousR1_AndARefusedR4_AsBefore_ARequestReadsThemAgain()
    {
        foreach (var (cache, expected) in new[] { (new RecordOwnerLookupCache(), 1), (RequestCache(), 2) })
        {
            var missing = new Wire<(Guid?, bool)>(_ => Task.FromResult<(Guid?, bool)>((null, false)));
            var ambiguous = new Wire<(Guid?, bool)>(_ => Task.FromResult<(Guid?, bool)>((null, true)));
            var refused = new Wire<RecordOwnerResolution>(_ =>
                Task.FromResult(RecordOwnerResolution.Refused(RecordOwnerRefusal.NoDefaultOwnerTeam, "no default team")));
            for (var i = 0; i < 2; i++)
            {
                (await cache.SecureBusinessUnitAsync("Missing", missing.Read)).Id.Should().BeNull();
                (await cache.SecureBusinessUnitAsync("Twice", ambiguous.Read)).Ambiguous.Should().BeTrue();
                (await cache.DefaultOwnerTeamAsync(Unit, refused.Read)).IsRefused.Should().BeTrue();
            }

            (missing.Calls, ambiguous.Calls, refused.Calls).Should().Be((expected, expected, expected),
                cache.RequestScoped ? "a request reads not-found and refusals again" : "a pass keeps every answer that did not fault");
        }
    }

    [Fact]
    public async Task APass_SeededWithNoSecureUnit_ReadsItNever_ARequestIsNotSeededWithNotFound()
    {
        var pass = new RecordOwnerLookupCache();
        pass.SeedSecureBusinessUnit("Secure Record", (null, false));
        var wire = new Wire<(Guid?, bool)>(_ => Task.FromResult<(Guid?, bool)>((Unit, false)));
        (await pass.SecureBusinessUnitAsync("Secure Record", wire.Read)).Id.Should().BeNull("the reconciler's seed, as before");
        wire.Calls.Should().Be(0, "a pass seeded with no Secure Record unit never reads it (UAC task 194)");

        var request = RequestCache();
        request.SeedSecureBusinessUnit("Secure Record", (null, false));
        (await request.SecureBusinessUnitAsync("Secure Record", wire.Read)).Id.Should().Be(Unit);
        wire.Calls.Should().Be(1, "a request is never seeded with not-found");
    }

    [Fact]
    public async Task APass_FaultIsStillNotKept()
    {
        var pass = new RecordOwnerLookupCache();
        var wire = new Wire<(Guid?, bool)>(call => call == 1
            ? Task.FromException<(Guid?, bool)>(new InvalidOperationException("Dataverse 503"))
            : Task.FromResult<(Guid?, bool)>((null, false)));

        var first = () => pass.SecureBusinessUnitAsync("Secure Record", wire.Read);
        await first.Should().ThrowAsync<InvalidOperationException>();
        await pass.SecureBusinessUnitAsync("Secure Record", wire.Read);
        await pass.SecureBusinessUnitAsync("Secure Record", wire.Read);
        wire.Calls.Should().Be(2, "a fault is asked again; the answer after it is kept for the pass");
    }

    /// <summary>Review F2, through the resolver: in an environment with no Secure Record business unit (supported), a pass asks
    /// the unit once for all its rows, as before D1; a request asks it per question (not-found is never kept per request).</summary>
    [Fact]
    public async Task APass_WithNoSecureUnit_ReadsTheUnitOnce_NotOncePerRow()
    {
        const int Rows = 3;
        var question = new RecordOwnershipContext { CallerObjectId = FakeDirectory.CallerOid };

        async Task<(int UnitReads, Guid? Owner)> Ask(RecordOwnerLookupCache? cache)
        {
            var directory = new FakeDirectory()
                .WithUser(FakeDirectory.CallerUserId, FakeDirectory.CallerOid, FakeDirectory.GeneralBu)
                .WithBusinessUnit(FakeDirectory.GeneralBu, "Spaarke")
                .WithTeam(FakeDirectory.GeneralTeam, FakeDirectory.GeneralBu, isDefault: true, teamType: 0, "Spaarke");
            var resolver = directory.Resolver();
            RecordOwnerResolution? answer = null;
            for (var i = 0; i < Rows; i++)
                answer = await resolver.ResolveOwnerAsync(question with { LookupCache = cache }, CancellationToken.None);
            return (directory.QueriedEntities.Count(e => e == "businessunit"), answer!.OwningTeamId);
        }

        var pass = await Ask(new RecordOwnerLookupCache());
        var request = await Ask(RequestCache());
        var none = await Ask(null);

        pass.Should().Be((1, FakeDirectory.GeneralTeam), "a pass: the missing Secure Record unit is read once");
        request.Should().Be((Rows, FakeDirectory.GeneralTeam), "a request reads not-found again on every question");
        none.Should().Be((Rows, FakeDirectory.GeneralTeam));
    }

    // ── Faults, cancellation and concurrency ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFaultedFirstRead_IsNotKept_TheSecondGoesToTheWire_AndSucceeds()
    {
        var cache = RequestCache();
        var wire = new Wire<(Guid?, bool)>(call => call == 1
            ? Task.FromException<(Guid?, bool)>(new InvalidOperationException("Dataverse 503"))
            : Task.FromResult<(Guid?, bool)>((Unit, false)));

        var first = () => cache.SecureBusinessUnitAsync("Secure Record", wire.Read);
        await first.Should().ThrowAsync<InvalidOperationException>();
        (await cache.SecureBusinessUnitAsync("Secure Record", wire.Read)).Id.Should().Be(Unit);
        await cache.SecureBusinessUnitAsync("Secure Record", wire.Read);

        wire.Calls.Should().Be(2, "the fault is removed, the success is kept");
    }

    [Fact]
    public async Task ACancelledFirstRead_IsNotKept()
    {
        var cache = RequestCache();
        var wire = new Wire<RecordOwnerResolution>(call => call == 1
            ? Task.FromCanceled<RecordOwnerResolution>(new CancellationToken(canceled: true))
            : Task.FromResult(RecordOwnerResolution.Owned(Team)));

        var first = () => cache.DefaultOwnerTeamAsync(Unit, wire.Read);
        await first.Should().ThrowAsync<OperationCanceledException>();
        (await cache.DefaultOwnerTeamAsync(Unit, wire.Read)).OwningTeamId.Should().Be(Team);
        wire.Calls.Should().Be(2);
    }

    [Fact]
    public async Task TwoConcurrentFirstReads_IssueOneCall_AndWhenItThrows_TheNextReadGoesToTheWire()
    {
        var cache = RequestCache();
        var gate = new TaskCompletionSource<(Guid?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new Wire<(Guid?, string?)>(call => call == 1 ? gate.Task : Task.FromResult<(Guid?, string?)>((Team, null)));

        var a = cache.SecureOwnerTeamAsync("Secure Record", "Owners", wire.Read);
        var b = cache.SecureOwnerTeamAsync("Secure Record", "Owners", wire.Read);
        wire.Calls.Should().Be(1, "the second first read shares the first one's call");
        gate.SetException(new InvalidOperationException("Dataverse 503"));

        await ((Func<Task>)(() => a)).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => b)).Should().ThrowAsync<InvalidOperationException>("both readers see the one fault");
        (await cache.SecureOwnerTeamAsync("Secure Record", "Owners", wire.Read)).TeamId.Should().Be(Team);
        wire.Calls.Should().Be(2, "the faulted entry was removed, so the next read went to the wire");
    }

    [Fact]
    public async Task ManyConcurrentFirstReads_IssueOneCall()
    {
        var cache = RequestCache();
        var gate = new TaskCompletionSource<RecordOwnerResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new Wire<RecordOwnerResolution>(_ => gate.Task);

        var readers = Enumerable.Range(0, 16).Select(_ => Task.Run(() => cache.DefaultOwnerTeamAsync(Unit, wire.Read))).ToArray();
        await Task.Delay(50);
        gate.SetResult(RecordOwnerResolution.Owned(Team));
        var answers = await Task.WhenAll(readers);

        answers.Should().OnlyContain(a => a.OwningTeamId == Team);
        wire.Calls.Should().Be(1);
    }

    // ── Through the real readers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>R7 is the request's only: one unfiled question per create, the acting user read once per request, and per
    /// question (as before) with a pass's cache or none. The answers are the same.</summary>
    [Fact]
    public async Task R7_TheActingUsersRow_IsReadOncePerRequest_ButEveryTimeWithAPassCacheOrNone()
    {
        var question = new RecordOwnershipContext { CallerObjectId = FakeDirectory.CallerOid };

        async Task<(int UserReads, int UnitReads, int TeamReads, Guid? Owner)> Twice(RecordOwnerLookupCache? cache)
        {
            var directory = FakeDirectory.Standard();
            var resolver = directory.Resolver();
            var first = await resolver.ResolveOwnerAsync(question with { LookupCache = cache }, CancellationToken.None);
            var second = await resolver.ResolveOwnerAsync(question with { LookupCache = cache }, CancellationToken.None);
            second.Should().BeEquivalentTo(first);
            return (directory.QueriedEntities.Count(e => e == "systemuser"), directory.QueriedEntities.Count(e => e == "businessunit"),
                directory.QueriedEntities.Count(e => e == "team"), first.OwningTeamId);
        }

        var request = await Twice(RequestCache());
        var pass = await Twice(new RecordOwnerLookupCache());
        var none = await Twice(null);

        request.Should().Be((1, 1, 1, FakeDirectory.GeneralTeam), "R7, R1 and R4 once per request");
        pass.UserReads.Should().Be(2, "uac-r2 Q4: the acting user's row is kept per request only, never per pass");
        none.Should().Be((2, 2, 2, FakeDirectory.GeneralTeam), "no cache: every question reads, exactly as before");
        pass.Owner.Should().Be(none.Owner);
    }

    /// <summary>A request's instance keeps nothing a pass keeps besides R1, R2, R4 and R7: the restricted-content walk is read per question.</summary>
    [Fact]
    public async Task ARequestCache_DoesNotKeepTheRestrictedContentWalk_APassCacheDoes()
    {
        var matter = Guid.Parse("d1d1d1d1-0000-4000-8000-0000000000a1");
        var policy = RecordOwnershipContext.ForChild(new Entity("sprk_policy", Guid.NewGuid())
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", matter),
        });

        async Task<int> WalkReads(RecordOwnerLookupCache cache)
        {
            var directory = FakeDirectory.Standard().WithRecord("sprk_matter", matter, FakeDirectory.ChildBu, owningTeam: FakeDirectory.ChildTeam);
            var resolver = directory.Resolver();
            (await resolver.ResolveOwnerAsync(policy with { LookupCache = cache }, CancellationToken.None)).OwningTeamId
                .Should().Be(FakeDirectory.RestrictedContentTeam);
            (await resolver.ResolveOwnerAsync(policy with { LookupCache = cache }, CancellationToken.None)).OwningTeamId
                .Should().Be(FakeDirectory.RestrictedContentTeam);
            return directory.BusinessUnitIdReads;
        }

        (await WalkReads(new RecordOwnerLookupCache())).Should().Be(2, "a pass walks once");
        (await WalkReads(RequestCache())).Should().Be(4, "a request walks per question, as with no cache");
    }

    /// <summary>Review F4 (CLAUDE.md §11): "can this team be the Secure owner" (R3) has no production caller with a request, so
    /// it is not cached: no R3 reader takes a <see cref="RecordOwnerLookupCache"/>, and the cache has no R3 entry.</summary>
    [Fact]
    public void R3_IsNotCached_NoReaderTakesTheCache()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var readers = typeof(Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer).GetMethods(All)
            .Where(m => m.Name is "CannotBeSecureOwnerTeamAsync" or "IsSecureOwnerTeamAsync")
            .ToList();

        readers.Should().NotBeEmpty("the R3 readers exist; the guard has its signal");
        readers.SelectMany(m => m.GetParameters()).Should().NotContain(p => p.ParameterType == typeof(RecordOwnerLookupCache),
            "no production caller passes a cache to the R3 readers");
        typeof(RecordOwnerLookupCache).GetMethods(All).Should().NotContain(m => m.Name.Contains("CannotBeSecureOwner"),
            "the cache keeps no R3 answer");
    }

    /// <summary>An ended request's <c>Items</c>: every access throws, as a disposed <c>HttpContext</c>'s does.</summary>
    private sealed class EndedItems : Dictionary<object, object?>, IDictionary<object, object?>
    {
        bool IDictionary<object, object?>.TryGetValue(object key, out object? value) =>
            throw new ObjectDisposedException("HttpContext");

        object? IDictionary<object, object?>.this[object key]
        {
            get => throw new ObjectDisposedException("HttpContext");
            set => throw new ObjectDisposedException("HttpContext");
        }
    }
}
