// ISS-010 part D1 (#3184): the per-request RecordOwnerLookupCache — one read per request of the business-unit and team
// configuration rows a decision commit's creates ask again and again (R1 the Secure Record business unit by name, R2 the Secure
// Record owner team, R4 a unit's default owner team). The binding conditions (uac-r2 and field-mapping-r1, 2026-11-26) are
// pinned here: a request keeps only a successful, non-empty answer; a fault, a cancellation, "not found" and a refusal are
// removed so the next read goes to the wire; concurrent first reads issue one call; two requests share nothing; R1/R2 are keyed
// on the configured VALUE; a request's instance keeps nothing a pass keeps besides R1, R2 and R4. A pass keeps R1 and R4 exactly
// as before D1: every answer that did not fault, and only on the questions that took a pass's cache before D1 (a parent's and a
// record's, never the acting user's). R3 (can a team be the Secure owner) and R7 (the acting user's row) are not cached.

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

    /// <summary>Review F2, through the resolver, in an environment with no Secure Record business unit (supported): a pass
    /// asks the unit once for all its filed rows, as before D1 (the parent's question took the pass's cache); the acting user's
    /// question takes no pass cache, so a pass asks it per row there, as before D1. A request asks it per question either way
    /// (not-found is never kept per request).</summary>
    [Fact]
    public async Task APass_WithNoSecureUnit_ReadsTheUnitOnceForFiledRows_AndPerRowOnTheActingUsersPath()
    {
        const int Rows = 3;
        var matter = Guid.Parse("d1d1d1d1-0000-4000-8000-0000000000b1");

        async Task<(int UnitReads, Guid? Owner)> Ask(RecordOwnerLookupCache? cache, RecordOwnershipContext question)
        {
            var directory = new FakeDirectory()
                .WithUser(FakeDirectory.CallerUserId, FakeDirectory.CallerOid, FakeDirectory.GeneralBu)
                .WithBusinessUnit(FakeDirectory.GeneralBu, "Spaarke")
                .WithBusinessUnit(FakeDirectory.ChildBu, "Spaarke Business Unit 1", parent: FakeDirectory.GeneralBu)
                .WithTeam(FakeDirectory.GeneralTeam, FakeDirectory.GeneralBu, isDefault: true, teamType: 0, "Spaarke")
                .WithTeam(FakeDirectory.ChildTeam, FakeDirectory.ChildBu, isDefault: true, teamType: 0, "Spaarke Business Unit 1")
                .WithRecord("sprk_matter", matter, FakeDirectory.ChildBu, owningTeam: FakeDirectory.ChildTeam);
            var resolver = directory.Resolver();
            RecordOwnerResolution? answer = null;
            for (var i = 0; i < Rows; i++)
                answer = await resolver.ResolveOwnerAsync(question with { LookupCache = cache }, CancellationToken.None);
            return (directory.QueriedEntities.Count(e => e == "businessunit"), answer!.OwningTeamId);
        }

        var filed = new RecordOwnershipContext { TargetEntityLogicalName = "sprk_matter", TargetRecordId = matter };
        var unfiled = new RecordOwnershipContext { CallerObjectId = FakeDirectory.CallerOid };

        (await Ask(new RecordOwnerLookupCache(), filed)).Should().Be((1, FakeDirectory.ChildTeam), "a pass: the missing unit once for its filed rows");
        (await Ask(RequestCache(), filed)).Should().Be((Rows, FakeDirectory.ChildTeam), "a request reads not-found again");
        (await Ask(null, filed)).Should().Be((Rows, FakeDirectory.ChildTeam));

        (await Ask(new RecordOwnerLookupCache(), unfiled)).Should().Be((Rows, FakeDirectory.GeneralTeam),
            "the acting user's question took no pass cache before D1, so a pass reads per row there");
        (await Ask(RequestCache(), unfiled)).Should().Be((Rows, FakeDirectory.GeneralTeam), "a request reads not-found again");
        (await Ask(null, unfiled)).Should().Be((Rows, FakeDirectory.GeneralTeam));
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

    /// <summary>Review round 2 (F2, F3): the acting user's question. With a request's cache it reads R1 and R4 once per request;
    /// with a pass's cache or none it reads them per question, exactly as before D1 (the cascade's root-owner step and the
    /// unfiled-communication creator hand it a pass's cache). The user's own row is read per question in every case (R7 is not
    /// cached). The answers are the same.</summary>
    [Fact]
    public async Task TheActingUsersQuestion_ReadsR1AndR4OncePerRequest_ButPerQuestionWithAPassCacheOrNone()
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

        request.Should().Be((2, 1, 1, FakeDirectory.GeneralTeam), "R1 and R4 once per request; the user's row per question");
        pass.Should().Be((2, 2, 2, FakeDirectory.GeneralTeam), "a pass: the acting user's question reads as before D1");
        none.Should().Be((2, 2, 2, FakeDirectory.GeneralTeam), "no cache: every question reads, exactly as before");
    }

    /// <summary>A request's instance keeps nothing a pass keeps besides R1, R2 and R4: the restricted-content walk is read per question.</summary>
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

    /// <summary>Review round 2 (F3, CLAUDE.md §11): the route asks the owner question by system user id, so an object-id key for
    /// the acting user's row (R7) never fired; it is not cached. Re-keying it on the system user id needs the owners' sign-off.</summary>
    [Fact]
    public void R7_IsNotCached_TheCacheHasNoActingUserEntry()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        typeof(RecordOwnerLookupCache).GetMembers(All).Select(m => m.Name)
            .Should().NotContain(n => n.Contains("ActingUser", StringComparison.Ordinal), "the cache keeps no acting user's row");
        typeof(RecordOwnerLookupCache).GetNestedTypes(All).Select(t => t.Name)
            .Should().NotContain(n => n.Contains("ActingUser", StringComparison.Ordinal));
        typeof(RecordOwnershipResolver).GetMethods(All).Select(m => m.Name)
            .Should().NotContain("ReadActingUserAsync", "the user's row is read inline, as on master");
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
