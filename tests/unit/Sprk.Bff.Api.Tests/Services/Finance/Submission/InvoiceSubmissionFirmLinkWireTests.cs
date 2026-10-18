using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// The firm-to-record link (ebilling-r1 D-106, refined by D-110) read through the REAL <see cref="ExternalParticipationService"/> over a real HTTP
/// transport, driven through the REAL <see cref="InvoiceSubmissionService.SubmitAsync"/> on both channels.
/// </summary>
/// <remarks>
/// <para>
/// The sibling <see cref="InvoiceSubmissionServiceTests"/> double the record-to-organization read, which proves the service's decision but not
/// what the wire does. The two claims here are about the wire: (1) <c>sprk_assignedlawfirm1</c> and <c>sprk_assignedlawfirm2</c> are the ONLY
/// source of "linked" - a firm that holds an org-level grant on the record and is in neither slot is refused; (2) a slot that cannot be read
/// is never an empty slot - a lookup property absent from the response, a failed read, a record that did not come back, each fails
/// CLOSED (503), while an explicit <c>null</c> (what Dataverse returns for an empty lookup; probed live 2026-10-17) is a slot that holds no
/// firm and never matches. The reader is app-only; nothing here depends on column-security behaviour.
/// </para>
/// <para>
/// The transport is an in-memory ASP.NET Core server (<c>UseTestServer</c>) standing in for the Dataverse Web API - the replacement ADR-038 names for
/// <c>Mock&lt;HttpMessageHandler&gt;</c> (ban B1) - so the JSON the real service deserializes is exactly what a Dataverse row looks like.
/// </para>
/// </remarks>
public sealed class InvoiceSubmissionFirmLinkWireTests
{
    private const string FakeServiceUrl = "https://fake-dataverse.crm.dynamics.com";

    /// <summary>A lookup property that is absent from the response.</summary>
    private const string Omitted = "<omitted>";

    private static readonly Guid FirmId = InvoiceSubmissionServiceTests.FirmId;
    private static readonly Guid MatterId = InvoiceSubmissionServiceTests.MatterId;
    private static readonly Guid OtherFirmId = InvoiceSubmissionServiceTests.OtherFirmId;

    public static TheoryData<InvoiceSubmissionChannel> Channels => new() { InvoiceSubmissionChannel.Spa, InvoiceSubmissionChannel.Internal };

    // ── linked: either slot, the other slot empty or somebody else ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task A_firm_in_the_first_assigned_slot_is_linked(InvoiceSubmissionChannel channel)
    {
        await using var world = await World.StartAsync();
        world.Dataverse.Matter(Lookup(FirmId), "null");

        var outcome = await world.SubmitAsync(channel);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task A_firm_in_the_second_assigned_slot_is_linked(InvoiceSubmissionChannel channel)
    {
        await using var world = await World.StartAsync();
        world.Dataverse.Matter(Lookup(OtherFirmId), Lookup(FirmId));

        var outcome = await world.SubmitAsync(channel);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    // ── not linked ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task A_firm_that_holds_only_an_org_level_grant_on_the_record_is_refused_and_no_grant_is_read(InvoiceSubmissionChannel channel)
    {
        // The record's assigned law firms are somebody else's. The chosen firm may well hold an org-level grant on it (the fake would serve one),
        // but D-110 says only the assigned slots link: the grants are never asked about.
        await using var world = await World.StartAsync();
        world.Dataverse.Matter(Lookup(OtherFirmId), "null");
        world.Dataverse.OrgGrantOnTheMatter = FirmId;

        var outcome = await world.SubmitAsync(channel);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 403 && o.ErrorCode == InvoiceSubmissionErrors.FirmNotLinked);
        world.Dataverse.Requests.Select(r => r.Collection).Should().OnlyContain(c => c == "sprk_matters", "no grant, membership or share table is consulted");
        world.Harness.Writes.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task Two_empty_slots_link_no_firm_a_null_never_matches(InvoiceSubmissionChannel channel)
    {
        await using var world = await World.StartAsync();
        world.Dataverse.Matter("null", "null");

        var outcome = await world.SubmitAsync(channel);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 403 && o.ErrorCode == InvoiceSubmissionErrors.FirmNotLinked);
        world.Harness.Writes.Should().Be(0);
    }

    // ── cannot be read: fail closed ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task A_first_slot_the_row_does_not_carry_fails_closed_even_when_the_second_slot_names_the_firm(InvoiceSubmissionChannel channel)
    {
        // The firm sits in slot 2, so it IS linked on what was read. But slot 1 was not read, and "the first slot is unreadable" must never be
        // answered with "the first slot is empty": the whole record is unverifiable.
        await using var world = await World.StartAsync();
        world.Dataverse.Matter(Omitted, Lookup(FirmId));

        var outcome = await world.SubmitAsync(channel);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == InvoiceSubmissionErrors.FirmUnverifiable);
        world.Harness.Writes.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task A_second_slot_the_row_does_not_carry_fails_closed_even_when_the_first_slot_names_the_firm(InvoiceSubmissionChannel channel)
    {
        await using var world = await World.StartAsync();
        world.Dataverse.Matter(Lookup(FirmId), Omitted);

        var outcome = await world.SubmitAsync(channel);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == InvoiceSubmissionErrors.FirmUnverifiable);
        world.Harness.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa, Omitted, "null")]
    [InlineData(InvoiceSubmissionChannel.Spa, "null", Omitted)]
    [InlineData(InvoiceSubmissionChannel.Spa, Omitted, Omitted)]
    [InlineData(InvoiceSubmissionChannel.Internal, Omitted, "null")]
    [InlineData(InvoiceSubmissionChannel.Internal, "null", Omitted)]
    [InlineData(InvoiceSubmissionChannel.Internal, Omitted, Omitted)]
    public async Task An_omitted_slot_beside_an_empty_one_is_unverifiable_not_unlinked(InvoiceSubmissionChannel channel, string first, string second)
    {
        // The case the omission rule exists for: an unread column beside an empty one would otherwise read as "no firm assigned" -> 403, which is
        // an answer about the record we do not have.
        await using var world = await World.StartAsync();
        world.Dataverse.Matter(first, second);

        var outcome = await world.SubmitAsync(channel);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == InvoiceSubmissionErrors.FirmUnverifiable);
        world.Harness.Writes.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task A_record_that_exists_but_does_not_come_back_from_the_link_read_is_unverifiable(InvoiceSubmissionChannel channel)
    {
        // The service read the record's own fields (it exists), yet the link read returned no row for it: that is not an answer about the record's
        // firms, so the submission fails closed.
        await using var world = await World.StartAsync();
        world.Dataverse.NoRows = true;

        var outcome = await world.SubmitAsync(channel);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == InvoiceSubmissionErrors.FirmUnverifiable);
        world.Dataverse.Requests.Should().ContainSingle("the record was read first, then its links");
        world.Harness.Writes.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task A_record_that_cannot_be_found_is_422_not_503_and_its_links_are_never_asked_for(InvoiceSubmissionChannel channel)
    {
        // A deleted record is a row the link reader is not given, which it cannot tell from a fault. The submission therefore reads the record's own
        // fields first: a missing record is its 422, so 503 "try again shortly" means only a real fault.
        await using var world = await World.StartAsync();
        world.Dataverse.NoRows = true; // what the reader would see for the deleted record
        world.Harness.Matter = null;

        var outcome = await world.SubmitAsync(channel);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 422 && o.ErrorCode == InvoiceSubmissionErrors.CoreRecordUnavailable);
        world.Dataverse.Requests.Should().BeEmpty("the missing record is refused before its links are read");
        world.Harness.Writes.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task A_failed_read_is_unverifiable(InvoiceSubmissionChannel channel)
    {
        await using var world = await World.StartAsync();
        world.Dataverse.Fault = HttpStatusCode.InternalServerError;

        var outcome = await world.SubmitAsync(channel);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == InvoiceSubmissionErrors.FirmUnverifiable);
        world.Harness.Writes.Should().Be(0);
    }

    // ── what is asked ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_read_asks_for_both_assigned_firm_slots_of_exactly_this_record_and_nothing_else()
    {
        await using var world = await World.StartAsync();
        world.Dataverse.Matter(Lookup(FirmId), "null");

        await world.SubmitAsync(InvoiceSubmissionChannel.Internal);

        var request = world.Dataverse.Requests.Should().ContainSingle().Subject;
        request.Collection.Should().Be("sprk_matters");
        request.Select.Should().Contain("_sprk_assignedlawfirm1_value").And.Contain("_sprk_assignedlawfirm2_value");
        request.Filter.Should().Contain($"sprk_matterid eq {MatterId}");
        request.Filter.Should().NotContain("or", "one record, one id");
    }

    [Theory]
    [InlineData(InvoiceCoreRecordKind.Project, "sprk_projects", "sprk_projectid")]
    [InlineData(InvoiceCoreRecordKind.WorkAssignment, "sprk_workassignments", "sprk_workassignmentid")]
    public async Task A_project_or_work_assignment_is_checked_against_its_own_table(InvoiceCoreRecordKind kind, string collection, string idAttribute)
    {
        var recordId = Guid.NewGuid();
        await using var world = await World.StartAsync();
        world.Dataverse.Row(collection, idAttribute, recordId, Lookup(OtherFirmId), "null");

        var outcome = await world.SubmitAsync(
            InvoiceSubmissionChannel.Internal, request => request with { CoreRecord = new InvoiceCoreRecord(kind, recordId) });

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 403 && o.ErrorCode == InvoiceSubmissionErrors.FirmNotLinked);
        world.Dataverse.Requests.Should().ContainSingle().Which.Collection.Should().Be(collection);
        world.Dataverse.Requests[0].Filter.Should().Contain($"{idAttribute} eq {recordId}");
    }

    private static string Lookup(Guid id) => $"\"{id}\"";

    // ── the world: real participation service + real submission service over the fake transport ──────────────────────────────────────────────

    private sealed class World : IAsyncDisposable
    {
        private readonly InvoiceSubmissionService _service;

        private World(FakeDataverse dataverse, InvoiceSubmissionServiceTests.Harness harness)
        {
            Dataverse = dataverse;
            Harness = harness;
            _service = harness.Build();
        }

        public FakeDataverse Dataverse { get; }

        public InvoiceSubmissionServiceTests.Harness Harness { get; }

        public static async Task<World> StartAsync()
        {
            var dataverse = await FakeDataverse.StartAsync();
            var harness = new InvoiceSubmissionServiceTests.Harness
            {
                Participations = new ExternalParticipationService(
                    dataverse.CreateClient(),
                    Mock.Of<ITenantCache>(),
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = FakeServiceUrl })
                        .Build(),
                    new StaticTokenCredential(),
                    Mock.Of<IHttpContextAccessor>(),
                    NullLogger<ExternalParticipationService>.Instance,
                    filing: null),
            };
            return new World(dataverse, harness);
        }

        public Task<InvoiceSubmissionOutcome> SubmitAsync(
            InvoiceSubmissionChannel channel, Func<InvoiceSubmissionRequest, InvoiceSubmissionRequest>? shape = null)
        {
            var request = InvoiceSubmissionServiceTests.On(channel);
            return _service.SubmitAsync(shape is null ? request : shape(request), CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Dataverse.DisposeAsync();
    }

    private sealed record SeenRequest(string Collection, string Filter, string Select);

    /// <summary>The Dataverse Web API as far as the link read goes: one row-set read per collection, answering raw JSON.</summary>
    private sealed class FakeDataverse : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly List<SeenRequest> _requests = new();
        private readonly Dictionary<string, string> _rows = new();
        private readonly object _gate = new();

        private FakeDataverse(WebApplication app) => _app = app;

        public IReadOnlyList<SeenRequest> Requests
        {
            get { lock (_gate) { return _requests.ToList(); } }
        }

        /// <summary>Non-null: every request answers with this status.</summary>
        public HttpStatusCode? Fault { get; set; }

        /// <summary>True: the query succeeds and returns no row.</summary>
        public bool NoRows { get; set; }

        /// <summary>A firm that holds an org-level grant on the test matter. The fake serves it if (and only if) the grant table is asked for.</summary>
        public Guid? OrgGrantOnTheMatter { get; set; }

        public static async Task<FakeDataverse> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var app = builder.Build();
            var fake = new FakeDataverse(app);
            app.MapGet("/api/data/v9.2/{collection}", (HttpContext context, string collection) => fake.HandleAsync(context, collection));
            await app.StartAsync();
            return fake;
        }

        public HttpClient CreateClient() => _app.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        /// <summary>The test matter's row: each slot a raw JSON value (<c>"guid"</c>, <c>null</c>) or <see cref="Omitted"/> for a property absent from the response.</summary>
        public void Matter(string slot1, string slot2) => Row("sprk_matters", "sprk_matterid", MatterId, slot1, slot2);

        public void Row(string collection, string idAttribute, Guid id, string slot1, string slot2)
        {
            var json = new StringBuilder("{").Append($"\"{idAttribute}\":\"{id}\"");
            if (slot1 != Omitted)
            {
                json.Append($",\"_sprk_assignedlawfirm1_value\":{slot1}");
            }

            if (slot2 != Omitted)
            {
                json.Append($",\"_sprk_assignedlawfirm2_value\":{slot2}");
            }

            lock (_gate) { _rows[collection] = json.Append('}').ToString(); }
        }

        private async Task HandleAsync(HttpContext context, string collection)
        {
            lock (_gate)
            {
                _requests.Add(new SeenRequest(collection, context.Request.Query["$filter"].ToString(), context.Request.Query["$select"].ToString()));
            }

            if (Fault is { } status)
            {
                context.Response.StatusCode = (int)status;
                return;
            }

            string row;
            lock (_gate) { _rows.TryGetValue(collection, out row!); }

            var value = collection == "sprk_externalrecordaccesses" && OrgGrantOnTheMatter is { } org
                ? $"[{{\"_sprk_organization_value\":\"{org}\",\"_sprk_matter_value\":\"{MatterId}\"}}]"
                : NoRows || row is null ? "[]" : $"[{row}]";

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"value\":{value}}}");
        }
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(Token);
    }
}
