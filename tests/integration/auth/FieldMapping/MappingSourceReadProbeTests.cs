using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 003 (spec FR-04, NFR-01) — the Read-as-caller gate on a mapping source,
/// <see cref="MappingSourceReadProbe.ReadAsync"/>: a caller's source is read app-only only after the caller-rights probe
/// answers Read; every other answer (denied, missing, unauthorizable type, thrown probe, failed read, no token) is ONE
/// "no mapping" outcome with one warning, so mapping is neither an existence oracle nor a refusal. A system principal
/// reads app-only with no probe.
/// </summary>
/// <remarks>
/// Lives under the security-auth KEEP path (ADR-038 §1, <c>tests/integration/auth/**</c>) because each test exercises an
/// authorization decision through the production <see cref="CallerRecordAccessProbe"/> (its virtual seam substituted, as
/// every other probe test does); the app-only reader is the <see cref="IGenericEntityService"/> module boundary (Moq).
/// </remarks>
public class MappingSourceReadProbeTests
{
    private static readonly Guid Readable = Guid.Parse("00000003-1111-4000-8000-000000000001");
    private static readonly Guid Denied = Guid.Parse("00000003-1111-4000-8000-000000000002");
    private static readonly Guid Missing = Guid.Parse("00000003-1111-4000-8000-000000000003");
    private static readonly string[] Columns = ["sprk_name", "sprk_matternumber"];

    private readonly Mock<IGenericEntityService> _appOnly = new(MockBehavior.Strict);
    private readonly List<string> _events = new();

    public MappingSourceReadProbeTests()
    {
        _appOnly
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
            {
                _events.Add($"read {table}({id})");
                return new Entity(table, id) { ["sprk_name"] = "Parent" };
            });
    }

    private Task<MappingSourceRead> Read(
        MappingSourceReader reader, string table, Guid id, IReadOnlyCollection<string>? columns = null, CancellationToken ct = default)
        => MappingSourceReadProbe.ReadAsync(reader, new MappingSource(table, id), columns ?? Columns, _appOnly.Object, NullLogger.Instance, ct);

    // ── Allowed ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_CallerHoldsRead_ReadsTheSourceOnlyAfterTheProbeAnswers()
    {
        var probe = new RightsProbe(AccessRights.Read, _events);

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token"), "sprk_matter", Readable);

        result.Readable.Should().BeTrue();
        result.Warning.Should().BeNull();
        result.Record!["sprk_name"].Should().Be("Parent");
        _events.Should().Equal($"probe sprk_matters({Readable})", $"read sprk_matter({Readable})");
    }

    [Fact]
    public async Task ReadAsync_CallerHoldsReadAndProfileNeedsNoColumns_IsReadableWithoutAnyRead()
    {
        var probe = new RightsProbe(AccessRights.Read, _events);

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token"), "sprk_matter", Readable, Array.Empty<string>());

        result.Should().Be(new MappingSourceRead(true, null, null));
        _events.Should().Equal($"probe sprk_matters({Readable})");
    }

    // ── Denied and its look-alikes: one outcome ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_CallerWithoutRead_GivesNoMappingAndOneWarningAndNeverReads()
    {
        var probe = new RightsProbe(AccessRights.Write | AccessRights.Append, _events);

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token"), "sprk_matter", Denied);

        result.Should().Be(new MappingSourceRead(false, null, MappingSourceReadProbe.SourceUnavailableWarning));
        _appOnly.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadAsync_DeniedEvenForADefaultOnlyProfile_GivesNoMapping()
    {
        var probe = new RightsProbe(AccessRights.None, _events);

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token"), "sprk_matter", Denied, Array.Empty<string>());

        result.Should().BeSameAs(MappingSourceRead.Unavailable);
    }

    [Fact]
    public async Task ReadAsync_MissingAndDeniedSources_GiveTheIdenticalOutcomeAfterTheSameWork()
    {
        var missingProbe = new WireProbe();
        var deniedProbe = new WireProbe();

        var missing = await Read(MappingSourceReader.ForCaller(missingProbe, "caller-token"), "sprk_matter", Missing);
        var denied = await Read(MappingSourceReader.ForCaller(deniedProbe, "caller-token"), "sprk_matter", Denied);

        missing.Should().BeSameAs(MappingSourceRead.Unavailable);
        denied.Should().BeSameAs(missing);
        denied.Warning.Should().Be(missing.Warning);
        // No timing oracle: a denied record is re-asked on the not-found schedule (task 064), so both did the same work.
        deniedProbe.Asks.Should().Be(missingProbe.Asks).And.BeGreaterThan(1);
        deniedProbe.Waits.Should().Equal(missingProbe.Waits);
        _appOnly.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadAsync_ProbeThrows_FailsClosedWithoutEchoingTheFault()
    {
        var probe = new ThrowingProbe();

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token"), "sprk_matter", Readable);

        result.Should().BeSameAs(MappingSourceRead.Unavailable);
        result.Warning.Should().NotContain("upstream-secret");
        _appOnly.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadAsync_SourceTypeWithNoEntitySet_GivesNoMappingWithoutAskingTheProbe()
    {
        var probe = new RightsProbe(AccessRights.Read, _events);

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token"), "sprk_unmappedtable", Readable);

        result.Should().BeSameAs(MappingSourceRead.Unavailable);
        _events.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_CallerWithNoToken_GivesNoMapping()
    {
        // The production probe, unsubstituted: no token answers None (fail closed), so nothing is read.
        var probe = new CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance);

        var result = await Read(MappingSourceReader.ForCaller(probe, callerBearerToken: null), "sprk_matter", Readable);

        result.Should().BeSameAs(MappingSourceRead.Unavailable);
        _appOnly.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadAsync_ReadFailsAfterTheProbeAllowed_GivesTheSameNoMappingOutcome()
    {
        _appOnly
            .Setup(e => e.RetrieveAsync("sprk_matter", Readable, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse said: upstream-secret"));
        var probe = new RightsProbe(AccessRights.Read, _events);

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token"), "sprk_matter", Readable);

        result.Should().BeSameAs(MappingSourceRead.Unavailable);
    }

    [Fact]
    public void SourceUnavailableWarning_NamesNoRecordTypeOrId()
    {
        var warning = MappingSourceRead.Unavailable.Warning!;

        warning.Should().Be(MappingSourceReadProbe.SourceUnavailableWarning);
        warning.Should().NotContainAny("sprk_", "matter", Denied.ToString(), Missing.ToString(), "exist", "found", "permission");
    }

    [Fact]
    public async Task ReadAsync_CancelledByTheCaller_PropagatesRatherThanReadingAsNoMapping()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var probe = new CancellingProbe();

        var act = () => Read(MappingSourceReader.ForCaller(probe, "caller-token"), "sprk_matter", Readable, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── System principal ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_SystemPrincipal_ReadsAppOnlyWithNoCallerProbe()
    {
        // A type a caller could not be evaluated on: the system path never asks, so it still reads.
        var result = await Read(MappingSourceReader.System, "sprk_unmappedtable", Readable);

        MappingSourceReader.System.IsSystem.Should().BeTrue();
        result.Readable.Should().BeTrue();
        result.Record!.LogicalName.Should().Be("sprk_unmappedtable");
        _events.Should().Equal($"read sprk_unmappedtable({Readable})");
    }

    // ── Task 043 (owner 2026-11-04): a Document is a mapping source ───────────────────────────────────────────────

    private const string Document = "sprk_document";

    /// <summary>The route's answer to the external document narrowing, recorded in the same journal as the probe and the read.</summary>
    private Func<Guid, CancellationToken, Task<bool>> Shows(bool shown) => (id, _) =>
    {
        _events.Add($"narrowing {id}");
        return Task.FromResult(shown);
    };

    [Fact]
    public async Task ReadAsync_ADocumentTheCallerCanRead_AndTheNarrowingShows_IsAskedOnTheDocumentSet_ThenRead()
    {
        var probe = new RightsProbe(AccessRights.Read, _events);

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token", Shows(true)), Document, Readable);

        result.Readable.Should().BeTrue();
        result.Warning.Should().BeNull();
        result.Record!.LogicalName.Should().Be(Document);
        _events.Should().Equal(
            $"probe sprk_documents({Readable})", $"narrowing {Readable}", $"read sprk_document({Readable})");
    }

    public static TheoryData<string> DocumentNotShown => new() { "hidden", "narrowing-faults" };

    [Theory]
    [MemberData(nameof(DocumentNotShown))]
    public async Task ReadAsync_ADocumentTheNarrowingDoesNotShow_IsTheOneUnavailable_AndIsNeverRead(string shape)
    {
        var probe = new RightsProbe(AccessRights.Read, _events);
        var decision = shape switch
        {
            "hidden" => Shows(false),
            "narrowing-faults" => (id, _) =>
            {
                _events.Add($"narrowing {id}");
                throw new HttpRequestException("narrowing read failed: upstream-secret");
            }
            ,
            _ => (Func<Guid, CancellationToken, Task<bool>>?)null,
        };

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token", decision), Document, Readable);

        result.Should().BeSameAs(MappingSourceRead.Unavailable);
        _events.Should().StartWith($"probe sprk_documents({Readable})", "the caller's Read is asked first");
        _events.Should().NotContain(e => e.StartsWith("read ", StringComparison.Ordinal), "the document is never read app-only");
        _appOnly.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadAsync_ADocumentOnARouteWithNoDocumentDecision_IsTheOneUnavailable_WithNothingAsked()
    {
        // Task 043 review (K1): a route that passes no document decision can never map from a document, so neither the
        // caller-rights probe nor the read runs. The answer depends only on the route and the table, never on the record:
        // a readable, a denied and a missing document all give the same result after the same (no) work.
        foreach (var id in new[] { Readable, Denied, Missing })
        {
            var probe = new RightsProbe(AccessRights.Read, _events);

            var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token"), Document, id);

            result.Should().BeSameAs(MappingSourceRead.Unavailable);
        }

        _events.Should().BeEmpty("no probe, no narrowing and no read");
        _appOnly.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadAsync_ADocumentTheNarrowingDoesNotShow_GivesNoMapping_EvenForADefaultOnlyProfile()
    {
        var probe = new RightsProbe(AccessRights.Read, _events);

        var result = await Read(
            MappingSourceReader.ForCaller(probe, "caller-token", Shows(false)), Document, Readable, Array.Empty<string>());

        result.Should().BeSameAs(MappingSourceRead.Unavailable);
    }

    [Fact]
    public async Task ReadAsync_ADocumentTheCallerCannotRead_IsUnavailable_BeforeTheNarrowingIsAsked()
    {
        var probe = new RightsProbe(AccessRights.Write | AccessRights.AppendTo, _events);

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token", Shows(true)), Document, Denied);

        result.Should().BeSameAs(MappingSourceRead.Unavailable);
        _events.Should().Equal($"probe sprk_documents({Denied})");
    }

    [Fact]
    public async Task ReadAsync_AMissingAndADeniedDocument_GiveTheIdenticalOutcomeAfterTheSameWork()
    {
        var missingProbe = new WireProbe();
        var deniedProbe = new WireProbe();

        var missing = await Read(MappingSourceReader.ForCaller(missingProbe, "caller-token", Shows(true)), Document, Missing);
        var denied = await Read(MappingSourceReader.ForCaller(deniedProbe, "caller-token", Shows(true)), Document, Denied);

        missing.Should().BeSameAs(MappingSourceRead.Unavailable);
        denied.Should().BeSameAs(missing);
        deniedProbe.Asks.Should().Be(missingProbe.Asks).And.BeGreaterThan(1);
        deniedProbe.Waits.Should().Equal(missingProbe.Waits);
        _events.Should().BeEmpty("neither is put to the narrowing nor read");
    }

    [Fact]
    public async Task ReadAsync_ADocumentWhoseProbeThrows_IsUnavailable_AndIsNeverRead()
    {
        var result = await Read(MappingSourceReader.ForCaller(new ThrowingProbe(), "caller-token", Shows(true)), Document, Readable);

        result.Should().BeSameAs(MappingSourceRead.Unavailable);
        _events.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_ANonDocumentSource_NeverAsksTheDocumentDecision()
    {
        var probe = new RightsProbe(AccessRights.Read, _events);

        var result = await Read(MappingSourceReader.ForCaller(probe, "caller-token", Shows(false)), "sprk_matter", Readable);

        result.Readable.Should().BeTrue("the narrowing is about documents; a matter is decided by the caller's Read alone");
        _events.Should().Equal($"probe sprk_matters({Readable})", $"read sprk_matter({Readable})");
    }

    // ── Task 043 AC7 (must not fire): every other table resolves and is decided exactly as before ─────────────────

    [Fact]
    public void TryResolveSourceEntitySet_AgreesWithTheAssociationMapOnEveryEntry_AndAddsOnlyTheDocument()
    {
        foreach (var type in Sprk.Bff.Api.Api.Filters.EntityAccessFilter.SupportedEntityTypes)
        {
            Sprk.Bff.Api.Api.Filters.EntityAccessFilter.TryResolveEntitySet(type, out var expected).Should().BeTrue();
            MappingSourceReadProbe.TryResolveSourceEntitySet(type, out var actual).Should().BeTrue(type);
            actual.Should().Be(expected, type);
        }

        MappingSourceReadProbe.TryResolveSourceEntitySet(" SPRK_Document ", out var documents).Should().BeTrue();
        documents.Should().Be(Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter.DocumentEntitySet);
        MappingSourceReadProbe.TryResolveSourceEntitySet("sprk_unmappedtable", out var none).Should().BeFalse();
        none.Should().BeEmpty();
        MappingSourceReadProbe.TryResolveSourceEntitySet("document", out _).Should().BeFalse("only the logical name, no alias");
        Sprk.Bff.Api.Api.Filters.EntityAccessFilter.TryResolveEntitySet(Document, out _).Should().BeFalse(
            "the association lockstep map itself is unchanged");
    }

    [Fact]
    public async Task CheckCallerReadAsync_TheQuickCreateDecision_StillFindsADocumentNotAuthorizable()
    {
        var probe = new RightsProbe(AccessRights.Read, _events);

        var quickCreate = await MappingSourceReadProbe.CheckCallerReadAsync(probe, "caller-token", Document, Readable, default);
        var unmapped = await MappingSourceReadProbe.CheckCallerReadOfSourceAsync(probe, "caller-token", "sprk_unmappedtable", Readable, default);

        quickCreate.Decision.Should().Be(CallerReadDecision.EntityTypeNotAuthorizable, "the quick-create refusals are unchanged");
        unmapped.Decision.Should().Be(CallerReadDecision.EntityTypeNotAuthorizable);
        _events.Should().BeEmpty();
    }

    // ── Probe doubles: the production probe with one virtual seam substituted ───────────────────────────────────

    private static IConfiguration OboConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AzureAd:TenantId"] = "00000000-0000-0000-0000-0000000000aa",
        ["AzureAd:ClientId"] = "00000000-0000-0000-0000-0000000000bb",
        ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com"
    }).Build();

    /// <summary>Answers fixed rights and records each question.</summary>
    private sealed class RightsProbe(AccessRights rights, List<string> events)
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            events.Add($"probe {entitySet}({recordId})");
            return Task.FromResult(rights);
        }
    }

    private sealed class ThrowingProbe()
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => throw new InvalidOperationException("RetrievePrincipalAccess said: upstream-secret");
    }

    private sealed class CancellingProbe()
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(AccessRights.Read);
        }
    }

    /// <summary>
    /// The production probe with only its wire answered from memory (Dataverse's real answers: 404 for an unknown record,
    /// 403 <c>0x80048306</c> for one the caller cannot see) and its waits recorded, never slept.
    /// </summary>
    private sealed class WireProbe : CallerRecordAccessProbe
    {
        private int _asks;

        public WireProbe()
            : base(
                new HttpClient(),
                OboConfiguration(),
                NullLogger<CallerRecordAccessProbe>.Instance,
                new OrderedCredentialClientProvider(
                    Options.Create(new CredentialSelectionOptions { Order = new List<string> { "ManagedIdentityFederated" } }),
                    new ConfigurationBuilder().Build(),
                    NullLogger<OrderedCredentialClientProvider>.Instance))
        {
        }

        public int Asks => Volatile.Read(ref _asks);

        private readonly ConcurrentQueue<TimeSpan> _waits = new();

        public IReadOnlyList<TimeSpan> Waits => _waits.ToList();

        protected override Task<string?> ExchangeForDataverseTokenAsync(string callerBearerToken, string context, CancellationToken ct)
            => Task.FromResult<string?>("dataverse-token");

        protected override Task<Guid?> ResolveCallerSystemUserIdAsync(string dataverseToken, CancellationToken ct)
            => Task.FromResult<Guid?>(Guid.Parse("00000003-1111-4000-8000-0000000000ff"));

        protected override Task<(HttpStatusCode Status, string Body)> SendPrincipalAccessRequestAsync(
            string url, string dataverseToken, CancellationToken ct)
        {
            Interlocked.Increment(ref _asks);
            (HttpStatusCode, string) answer =
                url.Contains(Missing.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? (HttpStatusCode.NotFound, """{"error":{"code":"0x80040217","message":"Does Not Exist"}}""")
                : url.Contains(Denied.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? (HttpStatusCode.Forbidden, """{"error":{"code":"0x80048306","message":"Principal user is missing access"}}""")
                : (HttpStatusCode.OK, """{"AccessRights":"ReadAccess"}""");
            return Task.FromResult(answer);
        }

        protected override Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            _waits.Enqueue(delay);
            return Task.CompletedTask;
        }
    }
}
