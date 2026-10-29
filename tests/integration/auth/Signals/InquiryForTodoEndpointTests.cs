using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Signals;

/// <summary>
/// Ontology platform R1 task 154 (D-111): <c>GET /api/v1/inquiries/for-todo/{todoId}</c> through the REAL pipeline:
/// authentication, then <c>RecordRouteAccessAuthorizationFilter</c> asking the caller's Read on the To Do (the probe double answers
/// per record, as Dataverse does), then the PRODUCTION <see cref="InquiryDispositionService.FindForTodoAsync"/>, whose every read
/// is AS THE CALLER. The caller-identity client is a small in-memory Dataverse: each row is returned only to a caller who may read
/// it, and list filters are evaluated, so what comes back is admitted by the caller's own read, not by the test.
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path).</remarks>
public class InquiryForTodoEndpointTests : IClassFixture<InquiryForTodoTestFixture>
{
    private static readonly Guid Matter = Guid.Parse("15415400-0000-4000-8000-0000000000a1");
    private static readonly Guid Inquiry = Guid.Parse("15415400-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherInquiry = Guid.Parse("15415400-0000-4000-8000-0000000000b2");
    private static readonly Guid ClosedInquiry = Guid.Parse("15415400-0000-4000-8000-0000000000b3");
    private static readonly Guid OrdinaryTodo = Guid.Parse("15415400-0000-4000-8000-0000000000c1");
    private static readonly Guid Older = Guid.Parse("15415400-0000-4000-8000-0000000000d1");
    private static readonly Guid Newer = Guid.Parse("15415400-0000-4000-8000-0000000000d2");
    private static readonly Guid Undated = Guid.Parse("15415400-0000-4000-8000-0000000000d3");
    private static readonly Guid Outgoing = Guid.Parse("15415400-0000-4000-8000-0000000000d4");
    private static readonly Guid OnAnother = Guid.Parse("15415400-0000-4000-8000-0000000000d5");

    private static readonly Guid InquiryTodo = InquiryDispositionService.TodoIdFor(Inquiry);

    private readonly InquiryForTodoTestFixture _fixture;
    private DataverseWorld W => _fixture.World;

    public InquiryForTodoEndpointTests(InquiryForTodoTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static string Route(Guid todoId) => $"/api/v1/inquiries/for-todo/{todoId:D}";

    private static string Hint(Guid serviceRequestId) => $"{InquiryReplyTodoCreator.NotesKey}{serviceRequestId:D}";

    /// <summary>The inquiry's To Do (readable, with its hint), the inquiry and two siblings on its matter, and its communications.</summary>
    private void TheInquiryWorld(string? notes)
    {
        _fixture.Rights[("sprk_todos", InquiryTodo)] = AccessRights.Read;
        W.Todos[InquiryTodo] = (notes, Matter);
        W.ServiceRequests[Inquiry] = (Matter, BudgetInquiryExecutor.DirectionOutbound, 0);
        W.ServiceRequests[OtherInquiry] = (Matter, BudgetInquiryExecutor.DirectionOutbound, 0);
        W.ServiceRequests[ClosedInquiry] = (Matter, BudgetInquiryExecutor.DirectionOutbound, 1);
        W.Communications.Add((Older, Inquiry, InquiryDispositionService.CommunicationIncoming, "RE: budget (first)", "2026-10-02T09:00:00Z"));
        W.Communications.Add((Undated, Inquiry, InquiryDispositionService.CommunicationIncoming, "RE: budget (undated)", null));
        W.Communications.Add((Newer, Inquiry, InquiryDispositionService.CommunicationIncoming, "RE: budget (second)", "2026-10-05T09:00:00Z"));
        W.Communications.Add((Outgoing, Inquiry, 100000001, "Budget inquiry", "2026-10-01T09:00:00Z"));
        W.Communications.Add((OnAnother, OtherInquiry, InquiryDispositionService.CommunicationIncoming, "RE: other", "2026-10-06T09:00:00Z"));
    }

    private async Task<HttpResponseMessage> GetAsync(Guid todoId) => await _fixture.CreateAuthenticatedClient().GetAsync(Route(todoId));

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static List<Guid> ReplyIds(JsonElement body) =>
        body.GetProperty("replies").EnumerateArray().Select(r => r.GetProperty("communicationId").GetGuid()).ToList();

    // ── Found ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AValidHint_Returns200_WithTheInquiry_AndItsIncomingRepliesNewestFirst()
    {
        TheInquiryWorld(Hint(Inquiry));

        var response = await GetAsync(InquiryTodo);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("serviceRequestId").GetGuid().Should().Be(Inquiry);
        ReplyIds(body).Should().Equal([Newer, Older, Undated], "newest first; an undated reply last; outgoing and other inquiries' mail never");
        var first = body.GetProperty("replies")[0];
        first.GetProperty("subject").GetString().Should().Be("RE: budget (second)");
        first.GetProperty("receivedOn").GetDateTimeOffset().Should().Be(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        body.GetProperty("replies")[2].GetProperty("receivedOn").ValueKind.Should().Be(JsonValueKind.Null);
        _fixture.ProbedTargets.Should().Contain(("sprk_todos", InquiryTodo), "the route filter asks Read on the To Do itself");
        W.Requests.Should().NotContain(r => r.StartsWith("sprk_servicerequests?", StringComparison.Ordinal), "a valid hint needs no list");
    }

    [Theory]
    [InlineData(null)]                         // no notes
    [InlineData("Record the outcome")]         // notes with no hint
    [InlineData("serviceRequestId=not-a-guid")] // a malformed hint
    public async Task NoUsableHint_FallsBackToTheMattersOpenOutboundInquiries_AndPicksTheOneThatDerivesTheTodosId(string? notes)
    {
        TheInquiryWorld(notes);

        var response = await GetAsync(InquiryTodo);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("serviceRequestId").GetGuid().Should().Be(Inquiry);
        ReplyIds(body).Should().Equal([Newer, Older, Undated]);
    }

    [Fact]
    public async Task AHintNamingAnotherInquiry_IsNotTrusted_TheFallbackFindsTheTodosOwnInquiry()
    {
        TheInquiryWorld(Hint(OtherInquiry));

        var body = await BodyOf(await GetAsync(InquiryTodo));

        body.GetProperty("serviceRequestId").GetGuid().Should().Be(Inquiry, "the hint is accepted only when it derives this To Do's id");
        W.Requests.Should().NotContain(r => r.StartsWith($"sprk_servicerequests({OtherInquiry:D})", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReplyTheCallerCannotRead_IsNotReturned()
    {
        TheInquiryWorld(Hint(Inquiry));
        W.CallerCannotRead.Add(Newer);

        var body = await BodyOf(await GetAsync(InquiryTodo));

        ReplyIds(body).Should().Equal([Older, Undated]);
    }

    // ── Not found: one answer, whatever the reason ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnOrdinaryTodo_NoInquiryDerivesItsId_Is404()
    {
        TheInquiryWorld(Hint(Inquiry));
        _fixture.Rights[("sprk_todos", OrdinaryTodo)] = AccessRights.Read;
        W.Todos[OrdinaryTodo] = (null, Matter);

        (await GetAsync(OrdinaryTodo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ATodoOnNoMatter_WithNoUsableHint_Is404()
    {
        _fixture.Rights[("sprk_todos", InquiryTodo)] = AccessRights.Read;
        W.Todos[InquiryTodo] = (null, null);

        (await GetAsync(InquiryTodo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ACallerWithoutReadOnTheTodo_Is404_AndNothingIsRead()
    {
        TheInquiryWorld(Hint(Inquiry));
        _fixture.Rights.Clear();

        var response = await GetAsync(InquiryTodo);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        W.Requests.Should().BeEmpty("the filter refuses before the handler runs");
    }

    [Fact]
    public async Task WhenTheCallersOwnReadOfTheTodoIsRefused_ItIs404_EvenThoughTheFilterAllowed()
    {
        TheInquiryWorld(Hint(Inquiry));
        W.CallerCannotRead.Add(InquiryTodo);

        var response = await GetAsync(InquiryTodo);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(Inquiry.ToString("D"));
    }

    [Theory]
    [InlineData(false)] // Dataverse answers 404
    [InlineData(true)]  // Dataverse answers 403, its usual answer for a row the caller cannot read (uac-r2 #1982 test nit)
    public async Task ACallerWhoCannotReadTheHintedInquiry_Is404_NeverItsIdOrReplies(bool forbidden)
    {
        // The hint names a real inquiry, but every returned value must be admitted by the caller's own read.
        TheInquiryWorld(Hint(Inquiry));
        W.CallerCannotRead.Add(Inquiry);
        W.CannotReadIsForbidden = forbidden;

        var response = await GetAsync(InquiryTodo);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(Inquiry.ToString("D")).And.NotContain("RE: budget");
    }

    [Fact]
    public async Task AValidHintNamingAClosedInquiry_Is404_TheHintedPathAppliesTheFallbacksOpenTest()
    {
        // Adversarial pass 1 F1: once the disposition is recorded, the To Do is an ordinary To Do; the picker has nothing to record.
        TheInquiryWorld(null);
        var closedTodo = InquiryDispositionService.TodoIdFor(ClosedInquiry);
        _fixture.Rights[("sprk_todos", closedTodo)] = AccessRights.Read;
        W.Todos[closedTodo] = (Hint(ClosedInquiry), Matter);

        var response = await GetAsync(closedTodo);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(ClosedInquiry.ToString("D"));
        W.Requests.Should().Contain($"sprk_servicerequests({ClosedInquiry:D})?$select=statecode,sprk_direction");
    }

    [Fact]
    public async Task AValidHintNamingAnInboundServiceRequest_Is404()
    {
        TheInquiryWorld(Hint(Inquiry));
        W.ServiceRequests[Inquiry] = (Matter, 100000000, 0);

        (await GetAsync(InquiryTodo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AHintBelowATypedNote_IsStillTheHint_NoListIsRead()
    {
        // Adversarial pass 1 F2: every serviceRequestId= occurrence is a candidate; the derived id decides.
        TheInquiryWorld($"Spoke to the firm\n{Hint(OtherInquiry)}\n{Hint(Inquiry)}");

        var body = await BodyOf(await GetAsync(InquiryTodo));

        body.GetProperty("serviceRequestId").GetGuid().Should().Be(Inquiry);
        W.Requests.Should().NotContain(r => r.StartsWith("sprk_servicerequests?", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)] // the list leaves the row out
    [InlineData(true)]  // the list is refused with 403 (no read on the table; uac-r2 #1982 test nit)
    public async Task ACallerWhoCannotReadTheInquiry_OnTheFallback_Is404(bool forbidden)
    {
        TheInquiryWorld(null);
        W.CallerCannotRead.Add(Inquiry);
        W.CannotReadIsForbidden = forbidden;

        (await GetAsync(InquiryTodo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── 401 and faults ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoToken_Is401()
    {
        TheInquiryWorld(Hint(Inquiry));

        var response = await _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync(Route(InquiryTodo));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        W.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_todos(", null)]
    [InlineData("sprk_servicerequests(", "hint")]
    [InlineData("sprk_servicerequests?", null)]
    [InlineData("sprk_communications?", "hint")]
    public async Task ADataverseFault_Is503_WithTheReasonCode_NeverA404OrAShorterList(string failingRead, string? hint)
    {
        TheInquiryWorld(hint is null ? null : Hint(Inquiry));
        W.FailReadsStartingWith = failingRead;

        var response = await GetAsync(InquiryTodo);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("reasonCode").GetString().Should().Be("inquiry.unavailable");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("RE: budget");
    }

    [Theory]
    [InlineData("sprk_todos(", null)]
    [InlineData("sprk_servicerequests(", "hint")]
    [InlineData("sprk_servicerequests?", null)]
    [InlineData("sprk_communications?", "hint")]
    public async Task ADataverseTimeout_Is503_Never500(string throwingRead, string? hint)
    {
        // Adversarial pass 1 F3: HttpClient's timeout is a TaskCanceledException while the request's own token is not cancelled.
        TheInquiryWorld(hint is null ? null : Hint(Inquiry));
        W.ThrowReadsStartingWith = throwingRead;

        var response = await GetAsync(InquiryTodo);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("reasonCode").GetString().Should().Be("inquiry.unavailable");
    }

    [Fact]
    public async Task AFurtherPageOfReplies_Is503_NeverTheFirstPageAsTheList()
    {
        TheInquiryWorld(Hint(Inquiry));
        W.RepliesHaveAnotherPage = true;

        var response = await GetAsync(InquiryTodo);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ── The caller-identity Dataverse ─────────────────────────────────────────────────────────────────────────────

    public sealed class DataverseWorld
    {
        public ConcurrentDictionary<Guid, (string? Notes, Guid? Matter)> Todos { get; } = new();
        public ConcurrentDictionary<Guid, (Guid Matter, int Direction, int State)> ServiceRequests { get; } = new();
        public List<(Guid Id, Guid ServiceRequest, int Direction, string Subject, string? ReceivedOn)> Communications { get; } = new();
        public HashSet<Guid> CallerCannotRead { get; } = new();
        public string? FailReadsStartingWith { get; set; }

        /// <summary>An unreadable service request answers 403 (single read, or the whole matter list) instead of 404 / a missing row.</summary>
        public bool CannotReadIsForbidden { get; set; }

        /// <summary>Reads starting with this THROW (adversarial pass 1 F3: DataverseUserClient lets a timeout through).</summary>
        public string? ThrowReadsStartingWith { get; set; }
        public bool RepliesHaveAnotherPage { get; set; }
        public ConcurrentQueue<string> Requests { get; } = new();

        public DataverseUserResponse Answer(string path)
        {
            Requests.Enqueue(path);
            if (ThrowReadsStartingWith is { } throwing && path.StartsWith(throwing, StringComparison.Ordinal))
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
            if (FailReadsStartingWith is { } failing && path.StartsWith(failing, StringComparison.Ordinal))
                return DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom");

            if (path.StartsWith("sprk_todos(", StringComparison.Ordinal))
            {
                var id = IdIn(path);
                if (!Todos.TryGetValue(id, out var todo) || CallerCannotRead.Contains(id))
                    return NotFound();
                return Ok(new Dictionary<string, object?>
                {
                    ["sprk_notes"] = todo.Notes,
                    ["_sprk_regardingmatter_value"] = todo.Matter?.ToString("D"),
                });
            }

            if (path.StartsWith("sprk_servicerequests(", StringComparison.Ordinal))
            {
                var id = IdIn(path);
                if (CannotReadIsForbidden && CallerCannotRead.Contains(id))
                    return Forbidden();
                return ServiceRequests.TryGetValue(id, out var request) && !CallerCannotRead.Contains(id)
                    ? Ok(new Dictionary<string, object?> { ["statecode"] = request.State, ["sprk_direction"] = request.Direction })
                    : NotFound();
            }

            if (path.StartsWith("sprk_servicerequests?", StringComparison.Ordinal))
            {
                var matter = Guid.Parse(Match(path, @"_sprk_regardingmatter_value eq ([0-9a-f-]{36})"));
                var direction = int.Parse(Match(path, @"sprk_direction eq (\d+)"));
                var state = int.Parse(Match(path, @"statecode eq (\d+)"));
                if (CannotReadIsForbidden && ServiceRequests.Keys.Any(CallerCannotRead.Contains))
                    return Forbidden();
                var rows = ServiceRequests
                    .Where(kv => kv.Value.Matter == matter && kv.Value.Direction == direction && kv.Value.State == state && !CallerCannotRead.Contains(kv.Key))
                    .Select(kv => new Dictionary<string, object?> { ["sprk_servicerequestid"] = kv.Key.ToString("D") });
                return Ok(new Dictionary<string, object?> { ["value"] = rows.ToList() });
            }

            if (path.StartsWith("sprk_communications?", StringComparison.Ordinal))
            {
                var request = Guid.Parse(Match(path, @"_sprk_regardingservicerequest_value eq ([0-9a-f-]{36})"));
                var direction = int.Parse(Match(path, @"sprk_direction eq (\d+)"));
                var rows = Communications
                    .Where(c => c.ServiceRequest == request && c.Direction == direction && !CallerCannotRead.Contains(c.Id))
                    .Select(c => new Dictionary<string, object?>
                    {
                        ["sprk_communicationid"] = c.Id.ToString("D"),
                        ["sprk_subject"] = c.Subject,
                        ["sprk_receiveddate"] = c.ReceivedOn,
                    });
                var body = new Dictionary<string, object?> { ["value"] = rows.ToList() };
                if (RepliesHaveAnotherPage)
                    body["@odata.nextLink"] = "https://test.crm.dynamics.com/api/data/v9.2/sprk_communications?$skiptoken=2";
                return Ok(body);
            }

            return NotFound();
        }

        private static Guid IdIn(string path) => Guid.Parse(path[(path.IndexOf('(') + 1)..path.IndexOf(')')]);

        private static string Match(string path, string pattern) => Regex.Match(Uri.UnescapeDataString(path), pattern).Groups[1].Value;

        private static DataverseUserResponse Forbidden() => DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "no");

        private static DataverseUserResponse NotFound() => DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no");

        private static DataverseUserResponse Ok(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));
    }
}

public sealed class InquiryForTodoTestFixture : WorkspaceTestFixture
{
    internal InquiryForTodoEndpointTests.DataverseWorld World { get; private set; } = new();

    public ConcurrentDictionary<(string EntitySet, Guid RecordId), AccessRights> Rights { get; } = new();

    public ConcurrentBag<(string EntitySet, Guid RecordId)> ProbedTargets { get; } = new();

    public void Reset()
    {
        World = new InquiryForTodoEndpointTests.DataverseWorld();
        Rights.Clear();
        ProbedTargets.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new RightsProbe(this));

            // The PRODUCTION InquiryDispositionService registration; only its caller-identity boundary is substituted.
            services.RemoveAll<IDataverseUserClient>();
            services.AddScoped<IDataverseUserClient>(_ => new WorldClient(this));
        });
    }

    public new HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return client;
    }

    private sealed class WorldClient(InquiryForTodoTestFixture fixture) : IDataverseUserClient
    {
        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(fixture.World.Answer(relativePath));
            }
            catch (Exception ex)
            {
                return Task.FromException<DataverseUserResponse>(ex);
            }
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The lookup never writes.");

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The lookup never writes.");

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The lookup never writes.");

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The lookup never writes.");
    }

    private sealed class RightsProbe : CallerRecordAccessProbe
    {
        private readonly InquiryForTodoTestFixture _fixture;

        public RightsProbe(InquiryForTodoTestFixture fixture)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
            => _fixture = fixture;

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            _fixture.ProbedTargets.Add((entitySet, recordId));
            if (string.IsNullOrWhiteSpace(callerBearerToken))
                return Task.FromResult(AccessRights.None);

            return Task.FromResult(_fixture.Rights.TryGetValue((entitySet, recordId), out var r) ? r : AccessRights.None);
        }
    }
}
