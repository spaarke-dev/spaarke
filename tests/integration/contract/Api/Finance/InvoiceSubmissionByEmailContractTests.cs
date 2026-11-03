using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using W = Sprk.Bff.Api.Tests.Services.Finance.Submission.FromEmailWorld;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The wire contract of <c>GET /api/finance/invoice-submissions?internetMessageId=...</c> (ebilling-r1 task 095a; contract section 11 of
/// <c>projects/ebilling-r1/notes/095-from-email-contract.md</c>): the latest attempt of an email that the caller can read. The REAL route,
/// filters in their order (step 0, the Finance filter, the kill switch), the real <see cref="InvoiceSubmissionService"/> view mapping and
/// the real <see cref="AuthorizationService"/>; doubles only at Dataverse (the .eml and attempt queries, the submitter's name).
/// </summary>
/// <remarks>Maintain-class (ADR-038): every field name, status and code here is a client contract (word-add-in-r1).</remarks>
public sealed class InvoiceSubmissionByEmailContractTests : IAsyncLifetime
{
    private static readonly Guid Eml = Guid.Parse("095a0000-0000-4000-8000-0000000000e1");
    private static readonly Guid OtherEml = Guid.Parse("095a0000-0000-4000-8000-0000000000e2");
    private static readonly Guid Person = Guid.Parse("095a0000-0000-4000-8000-0000000000a1");
    private static readonly Guid ServiceIdentity = Guid.Parse("095a0000-0000-4000-8000-0000000000a2");
    private const string MessageId = "<inv-1001@firm.example>";
    private const string ServiceIdentityName = "# mi-bff-api-dev";
    private static readonly DateTime Day = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly W _world = new();
    private readonly RecordingAccessDataSource _access = new();
    private readonly RecordingPrivilegeProbe _probe = new();
    private readonly HashSet<Guid> _throwFor = new();
    private readonly Dictionary<Guid, (string MessageId, bool Archive)> _emls = new();
    private readonly List<Entity> _attempts = new();
    private readonly Dictionary<Guid, string?> _people = new();
    private readonly List<QueryExpression> _queries = new();
    private readonly List<string> _doubleErrors = new();
    private readonly Mock<IGenericEntityService> _entities = new();
    private Exception? _emlQueryFault;
    private Exception? _attemptQueryFault;
    private Exception? _personFault;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public StatedCaller StepZero { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _probe.Hold(InvoiceSubmissionEndpoints.ReadSubmissionPrivilege);
        _people[Person] = "Pat Partner";
        _people[ServiceIdentity] = ServiceIdentityName;

        _entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) =>
            {
                lock (_queries)
                {
                    _queries.Add(q);
                }

                try
                {
                    if (q.EntityName == "sprk_document")
                    {
                        return _emlQueryFault is { } fault ? Task.FromException<EntityCollection>(fault) : Task.FromResult(AnswerEmlQuery(q));
                    }

                    if (q.EntityName == InvoiceSubmissionService.SubmissionEntity)
                    {
                        return _attemptQueryFault is { } fault ? Task.FromException<EntityCollection>(fault) : Task.FromResult(AnswerAttemptQuery(q));
                    }

                    return Task.FromException<EntityCollection>(new InvalidOperationException("unexpected query " + q.EntityName));
                }
                catch (NotSupportedException ex)
                {
                    // The double does not understand a condition, order or join the route sent: the test fails (in DisposeAsync, so even a
                    // test that expects a 503 cannot pass on it) rather than answer from a query the double did not read (095a review F3).
                    _doubleErrors.Add(ex.Message);
                    return Task.FromException<EntityCollection>(ex);
                }
            });
        _entities.Setup(e => e.RetrieveAsync("systemuser", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string[] _, CancellationToken _) =>
            {
                if (_personFault is { } fault)
                {
                    return Task.FromException<Entity>(fault);
                }

                return _people.TryGetValue(id, out var name)
                    ? Task.FromResult(new Entity("systemuser", id) { ["fullname"] = name })
                    : Task.FromException<Entity>(W.NotFound());
            });

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = FinanceAuthzTestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = FinanceAuthzTestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, FinanceAuthzTestAuthHandler>(FinanceAuthzTestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        StepZero = StatedCaller.Register(builder.Services);
        builder.Services.AddRateLimiter(opt => opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

        builder.Services.AddSingleton<IAccessDataSource>(new ThrowingAccess(_access, _throwFor));
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddSingleton<ITenantCache>(new InMemoryTenantCache());
        builder.Services.AddSingleton<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>(_probe);

        builder.Services.AddSingleton(_world.Intake.Monitor);
        builder.Services.AddSingleton(_world.Intake.Build());
        builder.Services.AddSingleton(_world.Intake.Participations);
        builder.Services.AddSingleton(_world.Build());
        builder.Services.AddSingleton(_entities.Object);

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapInvoiceSubmissionEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _doubleErrors.Should().BeEmpty("the Dataverse double answers only queries it understands (a condition, order or join it does not read would make a test pass on nothing)");
    }

    /// <summary>
    /// The .eml query as Dataverse answers it: EVERY condition the route sent is applied (case-insensitive string match for <c>In</c> on the
    /// Message-ID, the archive flag only when the route asks for it), so a condition the route drops changes the answer; a condition, nested
    /// filter, join or order the double does not read is <see cref="NotSupportedException"/>.
    /// </summary>
    private EntityCollection AnswerEmlQuery(QueryExpression q)
    {
        RequireFlat(q);
        if (q.Orders.Count > 0)
        {
            throw new NotSupportedException("an order on the .eml query");
        }

        IEnumerable<KeyValuePair<Guid, (string MessageId, bool Archive)>> rows = _emls;
        foreach (var condition in q.Criteria.Conditions.ToList())
        {
            var values = condition.Values.Cast<object>().ToList();
            rows = (condition.AttributeName, condition.Operator) switch
            {
                ("sprk_emailmessageid", ConditionOperator.In) =>
                    rows.Where(d => values.OfType<string>().Any(k => string.Equals(k, d.Value.MessageId, StringComparison.OrdinalIgnoreCase))).ToList(),
                ("sprk_isemailarchive", ConditionOperator.Equal) =>
                    rows.Where(d => values.Count == 1 && values[0] is bool wanted && d.Value.Archive == wanted).ToList(),
                _ => throw new NotSupportedException($"the condition {condition.AttributeName} {condition.Operator} on the .eml query"),
            };
        }

        return new EntityCollection(rows.Select(d => new Entity("sprk_document", d.Key)).Take(q.TopCount ?? int.MaxValue).ToList());
    }

    /// <summary>
    /// The attempt query as Dataverse answers it: the link condition applied, then the query's own orders IN ORDER (createdon, then the
    /// submission id), then <c>TopCount</c>. With no order the rows come back in the order they were stored, so an order the route drops
    /// changes which attempt answers (095a review F3).
    /// </summary>
    private EntityCollection AnswerAttemptQuery(QueryExpression q)
    {
        RequireFlat(q);
        IEnumerable<Entity> rows = _attempts;
        foreach (var condition in q.Criteria.Conditions.ToList())
        {
            var values = condition.Values.Cast<object>().ToList();
            rows = (condition.AttributeName, condition.Operator) switch
            {
                (InvoiceSubmissionService.SourceEmailDocumentColumn, ConditionOperator.In) =>
                    rows.Where(r => r.GetAttributeValue<EntityReference>(InvoiceSubmissionService.SourceEmailDocumentColumn) is { } link
                        && values.OfType<Guid>().Contains(link.Id)).ToList(),
                _ => throw new NotSupportedException($"the condition {condition.AttributeName} {condition.Operator} on the attempt query"),
            };
        }

        IOrderedEnumerable<Entity>? ordered = null;
        foreach (var order in q.Orders)
        {
            Func<Entity, object> key = order.AttributeName switch
            {
                "createdon" => r => r.GetAttributeValue<DateTime>("createdon"),
                TrackingNumberAllocator.SubmissionIdColumn => r => r.Id,
                _ => throw new NotSupportedException($"an order on {order.AttributeName} on the attempt query"),
            };
            var descending = order.OrderType == OrderType.Descending;
            if (ordered is null)
            {
                ordered = descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
            }
            else
            {
                ordered = descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
            }
        }

        return new EntityCollection((ordered ?? rows).Take(q.TopCount ?? int.MaxValue).ToList());
    }

    private static void RequireFlat(QueryExpression q)
    {
        if (q.Criteria.Filters.Count > 0 || q.LinkEntities.Count > 0 || q.Criteria.FilterOperator != LogicalOperator.And)
        {
            throw new NotSupportedException("a nested filter, an OR filter or a join");
        }
    }

    private sealed class ThrowingAccess(IAccessDataSource inner, HashSet<Guid> throwFor) : IAccessDataSource
    {
        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            inner.GetUserAccessAsync(userId, resourceId, userAccessToken, ct);

        public Task<AccessSnapshot> GetRecordAccessAsync(string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default) =>
            throwFor.Contains(recordId)
                ? Task.FromException<AccessSnapshot>(new TimeoutException("rights read faulted"))
                : inner.GetRecordAccessAsync(userId, entitySetName, recordId, userAccessToken, ct);
    }

    // ── Helpers ──

    private Task<HttpResponseMessage> GetAsync(string? query = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/finance/invoice-submissions" + (query ?? "?internetMessageId=" + Uri.EscapeDataString(MessageId)));
        request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string Normalise(string body) => Regex.Replace(body, "\"correlationId\":\"[^\"]*\"", "\"correlationId\":\"x\"");

    /// <summary>
    /// The .eml of <see cref="MessageId"/> (readable unless said otherwise). <paramref name="archive"/> false seeds a document of the same
    /// Message-ID that is NOT the email archive (<c>sprk_isemailarchive</c> false): the route must never consider it.
    /// </summary>
    private void SeedEml(Guid? id = null, string? messageId = null, bool readable = true, bool archive = true)
    {
        var emlId = id ?? Eml;
        _emls[emlId] = (messageId ?? MessageId, archive);
        _access.Grant(FinanceAuthorizationFilter.DocumentEntitySet, emlId, readable ? AccessRights.Read : AccessRights.None);
    }

    /// <summary>
    /// An attempt naming <paramref name="eml"/>, readable unless said otherwise; <paramref name="ageMinutes"/> older than the base day.
    /// <paramref name="id"/> fixes the submission id (the tie-break order).
    /// </summary>
    private Entity SeedAttempt(
        string tracking, InvoiceSubmissionStatus status, int ageMinutes = 0, Guid? eml = null, bool readable = true, Guid? person = null,
        Guid? invoice = null, Guid? correctionOf = null, string? reason = null, Guid? id = null)
    {
        var attemptId = id ?? Guid.NewGuid();
        var row = new Entity(InvoiceSubmissionService.SubmissionEntity, attemptId)
        {
            ["sprk_processingstatus"] = new OptionSetValue((int)status),
            [TrackingNumberAllocator.TrackingNumberColumn] = tracking,
            [TrackingNumberAllocator.FirmColumn] = new EntityReference("sprk_organization", W.FirmId),
            [TrackingNumberAllocator.FirmInvoiceNumberColumn] = "1001",
            ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", W.MatterId),
            ["createdon"] = Day.AddMinutes(-ageMinutes),
            [InvoiceSubmissionService.SourceEmailDocumentColumn] = new EntityReference("sprk_document", eml ?? Eml),
            // The service identity is what createdby holds; the route must never read it.
            ["createdby"] = new EntityReference("systemuser", ServiceIdentity),
        };
        if (person is { } p || person is null)
        {
            row[Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.Column] = new EntityReference("systemuser", person ?? Person);
        }

        if (invoice is { } inv)
        {
            row["sprk_invoice"] = new EntityReference("sprk_invoice", inv);
        }

        if (correctionOf is { } earlier)
        {
            row["sprk_resubmissionof"] = new EntityReference(InvoiceSubmissionService.SubmissionEntity, earlier);
        }

        if (reason is not null)
        {
            row["sprk_outcomereasoncode"] = reason;
        }

        _attempts.Add(row);
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, attemptId, readable ? AccessRights.Read : AccessRights.None);
        return row;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 200
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "095a: an email with one Accepted attempt the caller can read: 200 with its tracking number, Accepted, the invoice id, submittedOn (UTC ISO) and submittedBy")]
    public async Task OneAcceptedAttempt_Is200_WithTheBanner()
    {
        var invoice = Guid.NewGuid();
        SeedEml();
        var row = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted, invoice: invoice);

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body.GetProperty("submissionId").GetGuid().Should().Be(row.Id);
        body.GetProperty("trackingNumber").GetString().Should().Be("1001.1");
        body.GetProperty("status").GetString().Should().Be("Accepted");
        body.GetProperty("invoiceId").GetGuid().Should().Be(invoice);
        body.GetProperty("submittedOn").GetString().Should().Be("2026-11-03T10:00:00Z");
        body.GetProperty("submittedBy").EnumerateObject().Select(p => p.Name).Should().Equal("name");
        body.GetProperty("submittedBy").GetProperty("name").GetString().Should().Be("Pat Partner");
        body.GetProperty("isCorrection").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "095a: a Failed attempt followed by an Accepted one: 200 with the Accepted, latest one")]
    public async Task FailedThenAccepted_ReturnsTheLatest()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Failed, ageMinutes: 10, reason: "ai.invoice.read.not_configured");
        var accepted = SeedAttempt("1001.2", InvoiceSubmissionStatus.Accepted, ageMinutes: 1, invoice: Guid.NewGuid());

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("submissionId").GetGuid().Should().Be(accepted.Id);
        body.GetProperty("trackingNumber").GetString().Should().Be("1001.2");
        body.GetProperty("status").GetString().Should().Be("Accepted");
    }

    [Fact(DisplayName = "095a: an email whose latest attempt is Failed: 200 with the Failed attempt and its reason code")]
    public async Task LatestIsFailed_ReturnsTheFailedAttempt_WithItsReason()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted, ageMinutes: 10, invoice: Guid.NewGuid());
        var failed = SeedAttempt("1001.2", InvoiceSubmissionStatus.Failed, ageMinutes: 1, reason: "ebilling.submission.source_changed");

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body.GetProperty("submissionId").GetGuid().Should().Be(failed.Id);
        body.GetProperty("status").GetString().Should().Be("Failed");
        body.GetProperty("outcomeReasonCode").GetString().Should().Be("ebilling.submission.source_changed");
        body.TryGetProperty("invoiceId", out _).Should().BeFalse("a Failed attempt has no invoice");
    }

    [Theory(DisplayName = "095a: a still-running attempt (Received, Processing): 200 with that status, so the pane resumes polling by submissionId")]
    [InlineData(InvoiceSubmissionStatus.Received, "Received")]
    [InlineData(InvoiceSubmissionStatus.Processing, "Processing")]
    public async Task RunningAttempt_Is200_WithItsStatus(InvoiceSubmissionStatus status, string expected)
    {
        SeedEml();
        var row = SeedAttempt("1001.1", status);

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("submissionId").GetGuid().Should().Be(row.Id);
        body.GetProperty("status").GetString().Should().Be(expected);
    }

    [Fact(DisplayName = "095a: a correction attempt (it names the submission it replaces): isCorrection true; a new attempt: false")]
    public async Task CorrectionAttempt_IsCorrectionTrue()
    {
        SeedEml();
        SeedAttempt("1001.2", InvoiceSubmissionStatus.Received, correctionOf: Guid.NewGuid());

        (await BodyAsync(await GetAsync())).GetProperty("isCorrection").GetBoolean().Should().BeTrue();
    }

    [Theory(DisplayName = "095a: the Message-ID with and without angle brackets, and differing only in case, finds the same attempt")]
    [InlineData("<inv-1001@firm.example>")]
    [InlineData("inv-1001@firm.example")]
    [InlineData("<INV-1001@FIRM.example>")]
    [InlineData("INV-1001@Firm.Example")]
    [InlineData("  <inv-1001@firm.example>  ")]
    public async Task MessageIdSpellings_FindTheSameAttempt(string sent)
    {
        SeedEml();
        var row = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);

        var response = await GetAsync("?internetMessageId=" + Uri.EscapeDataString(sent));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyAsync(response)).GetProperty("submissionId").GetGuid().Should().Be(row.Id);
    }

    [Fact(DisplayName = "095a: an .eml stored without brackets is found by the bracketed Message-ID Outlook sends (both spellings are asked)")]
    public async Task StoredWithoutBrackets_IsFoundByTheBracketedSpelling()
    {
        SeedEml(messageId: "inv-1001@firm.example");
        var row = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("submissionId").GetGuid().Should().Be(row.Id);
        var keys = _queries.First(q => q.EntityName == "sprk_document").Criteria.Conditions
            .Single(c => c.AttributeName == "sprk_emailmessageid").Values.Cast<string>();
        keys.Should().BeEquivalentTo("inv-1001@firm.example", "<inv-1001@firm.example>");
    }

    [Fact(DisplayName = "095a: attempts of two .eml documents of one email (two savers): the newest readable attempt across both answers")]
    public async Task TwoEmls_TheNewestReadableAttemptAcrossBoth()
    {
        SeedEml();
        SeedEml(OtherEml);
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Failed, ageMinutes: 20, eml: Eml);
        var newest = SeedAttempt("1001.2", InvoiceSubmissionStatus.Accepted, ageMinutes: 2, eml: OtherEml);

        (await BodyAsync(await GetAsync())).GetProperty("submissionId").GetGuid().Should().Be(newest.Id);
    }

    [Fact(DisplayName = "095a F3: a document of the same Message-ID that is not the email archive is never considered, however new its attempt (seed-proof: the sprk_isemailarchive condition)")]
    public async Task NonArchiveDocumentOfTheSameMessageId_IsIgnored()
    {
        SeedEml();
        SeedEml(OtherEml, archive: false);
        var mine = SeedAttempt("1001.1", InvoiceSubmissionStatus.Failed, ageMinutes: 10, eml: Eml);
        SeedAttempt("1001.2", InvoiceSubmissionStatus.Accepted, ageMinutes: 1, eml: OtherEml);

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("submissionId").GetGuid().Should().Be(mine.Id, "the newer attempt names a document that is not the email archive");
        _queries.First(q => q.EntityName == "sprk_document").Criteria.Conditions
            .Should().Contain(c => c.AttributeName == "sprk_isemailarchive" && c.Operator == ConditionOperator.Equal && true.Equals(c.Values.Single()));
        _queries.Single(q => q.EntityName == InvoiceSubmissionService.SubmissionEntity).Criteria.Conditions
            .Single(c => c.AttributeName == InvoiceSubmissionService.SourceEmailDocumentColumn).Values.Cast<Guid>()
            .Should().BeEquivalentTo(new[] { Eml }, "only the archive's id is asked for");
    }

    [Fact(DisplayName = "095a F3: an email whose only document of that Message-ID is not the email archive: the uniform 404")]
    public async Task OnlyANonArchiveDocument_IsTheUniform404()
    {
        SeedEml(archive: false);
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);

        (await GetAsync()).StatusCode.Should().Be(HttpStatusCode.NotFound);
        _queries.Should().NotContain(q => q.EntityName == InvoiceSubmissionService.SubmissionEntity);
    }

    [Fact(DisplayName = "095a F3: the attempts are asked newest first (createdon descending, then the submission id descending) (seed-proof: the AddOrder calls)")]
    public async Task TheAttemptQuery_IsOrderedNewestFirst_ThenBySubmissionId()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Failed, ageMinutes: 10);

        await GetAsync();

        _queries.Single(q => q.EntityName == InvoiceSubmissionService.SubmissionEntity).Orders
            .Select(o => (o.AttributeName, o.OrderType))
            .Should().Equal(("createdon", OrderType.Descending), (TrackingNumberAllocator.SubmissionIdColumn, OrderType.Descending));
    }

    [Theory(DisplayName = "095a F3: two attempts created in the same instant: the one with the higher submission id answers, whichever was stored first (the tie-break)")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameInstant_TheHigherSubmissionIdAnswers(bool lowStoredFirst)
    {
        var low = Guid.Parse("095a0000-0000-4000-8000-0000000000b1");
        var high = Guid.Parse("095a0000-0000-4000-8000-0000000000b2");
        SeedEml();
        if (lowStoredFirst)
        {
            SeedAttempt("1001.1", InvoiceSubmissionStatus.Failed, id: low);
            SeedAttempt("1001.2", InvoiceSubmissionStatus.Accepted, id: high);
        }
        else
        {
            SeedAttempt("1001.2", InvoiceSubmissionStatus.Accepted, id: high);
            SeedAttempt("1001.1", InvoiceSubmissionStatus.Failed, id: low);
        }

        (await BodyAsync(await GetAsync())).GetProperty("submissionId").GetGuid().Should().Be(high);
    }

    [Fact(DisplayName = "095a: a submission with no person recorded: submittedBy null; the service identity (createdby) is never named")]
    public async Task NoPerson_IsNull_NeverTheServiceIdentity()
    {
        SeedEml();
        var row = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        row.Attributes.Remove(Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.Column);

        var response = await GetAsync();

        var text = await response.Content.ReadAsStringAsync();
        (await BodyAsync(response)).GetProperty("submittedBy").ValueKind.Should().Be(JsonValueKind.Null);
        text.Should().NotContain(ServiceIdentityName).And.NotContain(ServiceIdentity.ToString()).And.NotContain("createdby");
    }

    [Fact(DisplayName = "095a: a person who has no name: submittedBy null (a name only, never an id or an address)")]
    public async Task PersonWithNoName_IsNull_AndTheIdIsNeverReturned()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _people[Person] = "  ";

        var response = await GetAsync();

        (await BodyAsync(response)).GetProperty("submittedBy").ValueKind.Should().Be(JsonValueKind.Null);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(Person.ToString());
    }

    [Fact(DisplayName = "095a: the body carries every field GET /invoice-submissions/{id} serializes (same names), and never the source-email link")]
    public async Task Body_IsTheStatusBody_PlusTheNewFields_AndNeverTheSourceLink()
    {
        SeedEml();
        var row = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted, invoice: Guid.NewGuid());
        row["sprk_ponumber"] = "PO-1";
        row["sprk_receivedon"] = Day;
        row["sprk_document"] = new EntityReference("sprk_document", Guid.NewGuid());
        var viewNames = JsonSerializer.SerializeToElement(InvoiceSubmissionService.ToView(row), JsonSerializerOptions.Web)
            .EnumerateObject().Select(p => p.Name).ToList();

        var response = await GetAsync();

        var text = await response.Content.ReadAsStringAsync();
        var names = (await BodyAsync(response)).EnumerateObject().Select(p => p.Name).ToList();
        names.Should().Contain(viewNames, "the by-email body is the status body plus the new fields");
        names.Should().Contain(["submittedOn", "submittedBy", "isCorrection", "invoiceId"]);
        text.ToLowerInvariant().Should().NotContain("sourceemail").And.NotContain(Eml.ToString());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The uniform 404
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<string> NoMatchBodyAsync()
    {
        var response = await GetAsync("?internetMessageId=" + Uri.EscapeDataString("<nobody-sent-this@firm.example>"));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        return Normalise(await response.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "095a: no .eml for the email, and an .eml with no attempt: the uniform 404")]
    public async Task NoEml_AndNoAttempt_AreTheUniform404()
    {
        var noEml = await GetAsync();
        noEml.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await noEml.Content.ReadAsStringAsync()).Should().Contain("sdap.access.deny.record_unavailable");

        SeedEml();
        var noAttempt = await GetAsync();
        noAttempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Normalise(await noAttempt.Content.ReadAsStringAsync()).Should().Be(await NoMatchBodyAsync());
    }

    [Fact(DisplayName = "095a (seed-proof, the Read check on the attempt): an attempt the caller cannot read, with no readable one: the no-match 404, byte for byte, naming nothing")]
    public async Task UnreadableAttempt_IsTheNoMatch404_ByteForByte()
    {
        SeedEml();
        var hidden = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted, readable: false);

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var text = await response.Content.ReadAsStringAsync();
        Normalise(text).Should().Be(await NoMatchBodyAsync());
        text.Should().NotContain(hidden.Id.ToString()).And.NotContain("1001.1").And.NotContain("Pat Partner");
    }

    [Fact(DisplayName = "095a: an unreadable newest attempt is skipped, never returned: the older readable one answers")]
    public async Task UnreadableNewest_IsSkipped_TheOlderReadableAnswers()
    {
        SeedEml();
        SeedAttempt("1001.2", InvoiceSubmissionStatus.Accepted, ageMinutes: 1, readable: false);
        var older = SeedAttempt("1001.1", InvoiceSubmissionStatus.Failed, ageMinutes: 10);

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyAsync(response)).GetProperty("submissionId").GetGuid().Should().Be(older.Id);
    }

    [Fact(DisplayName = "095a: an .eml the caller cannot read: the no-match 404, byte for byte, and its attempts are not even asked for")]
    public async Task UnreadableEml_IsTheNoMatch404_ByteForByte()
    {
        SeedEml(readable: false);
        var row = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var text = await response.Content.ReadAsStringAsync();
        Normalise(text).Should().Be(await NoMatchBodyAsync());
        text.Should().NotContain(row.Id.ToString()).And.NotContain("1001.1");
        _queries.Should().NotContain(q => q.EntityName == InvoiceSubmissionService.SubmissionEntity, "nothing of an unreadable .eml is read");
    }

    [Fact(DisplayName = "095a: a readable .eml beside an unreadable one: only the readable one's attempts are considered")]
    public async Task OneUnreadableEml_OnlyTheReadableOnesAttemptsAreConsidered()
    {
        SeedEml();
        SeedEml(OtherEml, readable: false);
        SeedAttempt("1001.2", InvoiceSubmissionStatus.Accepted, ageMinutes: 1, eml: OtherEml);
        var mine = SeedAttempt("1001.1", InvoiceSubmissionStatus.Failed, ageMinutes: 10, eml: Eml);

        (await BodyAsync(await GetAsync())).GetProperty("submissionId").GetGuid().Should().Be(mine.Id);
    }

    [Theory(DisplayName = "095a: an empty, missing, repeated, over-long or malformed internetMessageId: the identical 404, and Dataverse is not asked")]
    [InlineData("")]
    [InlineData("?internetMessageId=")]
    [InlineData("?internetMessageId=%20%20")]
    [InlineData("?internetMessageId=not-a-message-id")]
    [InlineData("?internetMessageId=AAMkAGI2THVSAAA%3D")]
    [InlineData("?internetMessageId=%3Ca%40b%3E&internetMessageId=%3Cc%40d%3E")]
    [InlineData("?internetMessageId=%3C%3E%40x")]
    public async Task MalformedMessageId_IsTheUniform404(string query)
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);

        var response = await GetAsync(query);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Normalise(await response.Content.ReadAsStringAsync()).Should().Be(await NoMatchBodyAsync());
        _queries.Should().NotContain(q => q.EntityName == "sprk_document" && q.Criteria.Conditions.Any(c => c.Operator == ConditionOperator.In
            && c.Values.OfType<string>().Any(v => v.Contains("not-a-message-id"))));
    }

    [Theory(DisplayName = "095a F1: a step-0 refusal that is a decision (an external caller, a caller with no systemuser row) is the uniform 404, and nothing is read")]
    [InlineData(StepZeroCallerKind.FlagTrue)]
    [InlineData(StepZeroCallerKind.NoSystemUserRow)]
    public async Task Step0Refusal_IsTheUniform404(StepZeroCallerKind kind)
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        var noMatch = await NoMatchBodyAsync(); // the handler's no-match body, asked as the default internal caller BEFORE step 0 refuses
        StepZero.Kind = kind;

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Normalise(await response.Content.ReadAsStringAsync()).Should().Be(noMatch);
        noMatch.Should().Contain("sdap.access.deny.record_unavailable");
        _queries.Should().BeEmpty();
        _probe.Calls.Should().BeEmpty("step 0 refuses before the privilege is asked");
    }

    [Fact(DisplayName = "095a F1: a caller without the table-level Read on submissions (Dataverse answered, the privilege is not held): the uniform 404, and nothing is read")]
    public async Task NoTableRead_IsTheUniform404()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _probe.Release(InvoiceSubmissionEndpoints.ReadSubmissionPrivilege);

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sdap.access.deny.record_unavailable");
        _queries.Should().BeEmpty();
        _probe.Calls.Should().ContainSingle(c => c.Privilege == InvoiceSubmissionEndpoints.ReadSubmissionPrivilege && c.HasToken,
            "the three-state question is asked with the caller's token");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 503: never "not submitted" when the route could not decide
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task Is503Async(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response)).GetProperty("errorCode").GetString().Should().Be("ebilling.submission.record_unavailable");
    }

    [Fact(DisplayName = "095a: a Dataverse fault reading the .eml documents is 503, not the 404")]
    public async Task EmlQueryFault_Is503()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _emlQueryFault = new TimeoutException();

        await Is503Async(await GetAsync());
    }

    [Fact(DisplayName = "095a: a Dataverse fault reading the attempts is 503, not the 404")]
    public async Task AttemptQueryFault_Is503()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _attemptQueryFault = new TimeoutException();

        await Is503Async(await GetAsync());
    }

    [Fact(DisplayName = "095a F1: a fault answering the caller's table-level Read (OBO, WhoAmI or privilege-read fault) is 503, never the no-match 404, and nothing is read")]
    public async Task PrivilegeProbeFault_Is503_NotTheNoMatch404()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _probe.FaultFor.Add(InvoiceSubmissionEndpoints.ReadSubmissionPrivilege);

        await Is503Async(await GetAsync());
        _queries.Should().BeEmpty("a caller whose privilege could not be read is answered before anything is read");

        // Control (must not fire): the same caller once the probe answers is a plain yes.
        _probe.FaultFor.Clear();
        (await GetAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "095a F1: a privilege probe that throws is 503, never the no-match 404")]
    public async Task PrivilegeProbeThrows_Is503()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _probe.ThrowOnEveryCall = new TimeoutException("WhoAmI timed out");

        await Is503Async(await GetAsync());
        _queries.Should().BeEmpty();
    }

    [Theory(DisplayName = "095a F1: a step-0 fault (the sprk_isexternal flag read or the systemuser lookup faults) is 503, never the no-match 404, and nothing is read")]
    [InlineData(StepZeroCallerKind.FlagReadFault)]
    [InlineData(StepZeroCallerKind.LookupFault)]
    public async Task Step0Fault_Is503_NotTheNoMatch404(StepZeroCallerKind kind)
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        StepZero.Kind = kind;

        await Is503Async(await GetAsync());
        _queries.Should().BeEmpty();
        _probe.Calls.Should().BeEmpty("step 0 faults before the privilege is asked");

        // Control (must not fire): the same caller once the lookup answers (internal) is a plain yes.
        StepZero.Kind = StepZeroCallerKind.FlagFalse;
        (await GetAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "095a F3: more than 50 .eml documents of one Message-ID: 503 (cannot decide), never a partial answer; exactly 50 still decides")]
    public async Task MoreThanFiftyEmls_Is503_ExactlyFiftyDecides()
    {
        var ids = Enumerable.Range(0, InvoiceSubmissionEndpoints.MaxEmlCandidates + 1).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            SeedEml(id);
        }

        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted, eml: ids[0]);

        await Is503Async(await GetAsync());
        _queries.Should().NotContain(q => q.EntityName == InvoiceSubmissionService.SubmissionEntity, "no attempt is read once the .eml set is over the cap");

        // Control (must not fire): exactly the cap decides.
        _emls.Remove(ids[^1]);
        (await GetAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "095a F3: none of the newest 50 attempts readable and more exist: 503 (cannot decide); exactly 50 unreadable is the no-match 404; a readable newest of 51 answers")]
    public async Task MoreThanFiftyAttempts_NoneReadable_Is503_Controls()
    {
        SeedEml();
        var cap = InvoiceSubmissionEndpoints.MaxAttemptCandidates;
        for (var i = 0; i <= cap; i++)
        {
            SeedAttempt("1001." + (i + 1), InvoiceSubmissionStatus.Failed, ageMinutes: i, readable: false);
        }

        await Is503Async(await GetAsync());

        // Control (must not fire): exactly the cap, none readable, is a decision: nothing readable, the no-match 404.
        _attempts.RemoveAt(_attempts.Count - 1);
        (await GetAsync()).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Control (must not fire): over the cap, but the newest is readable: it answers.
        var newest = SeedAttempt("1001.0", InvoiceSubmissionStatus.Accepted, ageMinutes: -1);
        (await BodyAsync(await GetAsync())).GetProperty("submissionId").GetGuid().Should().Be(newest.Id);
    }

    [Fact(DisplayName = "095a: a fault reading the caller's rights on the .eml (thrown) is 503, not the 404")]
    public async Task RightsReadFaultOnTheEml_Is503()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _throwFor.Add(Eml);

        await Is503Async(await GetAsync());
    }

    [Fact(DisplayName = "095a: a fault reading the caller's rights on an attempt (thrown) is 503, not the 404")]
    public async Task RightsReadFaultOnTheAttempt_Is503()
    {
        SeedEml();
        var row = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _throwFor.Add(row.Id);

        await Is503Async(await GetAsync());
    }

    [Fact(DisplayName = "095a: a rights snapshot that is no complete answer (Faulted, no Read) is 503, never a 'no'; a Faulted snapshot that does carry Read is a yes")]
    public async Task FaultedRightsSnapshot_Is503_NotADenial()
    {
        SeedEml();
        var row = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted, readable: false);
        _access.Fault(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, row.Id);

        await Is503Async(await GetAsync());

        // Control (must not fire): the same snapshot with Read is a plain yes.
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, row.Id, AccessRights.Read);
        (await GetAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "095a: a fault reading the submitter's name is 503, never an answer without it")]
    public async Task SubmitterNameFault_Is503()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _personFault = new TimeoutException();

        await Is503Async(await GetAsync());
    }

    [Fact(DisplayName = "095a: a submitter that no longer exists: submittedBy null, the rest of the answer intact")]
    public async Task SubmitterGone_IsNull_NotAFault()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted, person: Guid.NewGuid());

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyAsync(response)).GetProperty("submittedBy").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "095a: with Ebilling:Enabled false the route answers 503 ebilling.disabled")]
    public async Task KillSwitch_EbillingOff_Is503EbillingDisabled()
    {
        SeedEml();
        SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);
        _world.Intake.Options.Enabled = false;

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response)).GetProperty("errorCode").GetString().Should().Be("ebilling.disabled");
    }

    [Fact(DisplayName = "095a: rights are asked as the caller (with the token) for the .eml and for each attempt")]
    public async Task RightsAreAskedAsTheCaller()
    {
        SeedEml();
        var row = SeedAttempt("1001.1", InvoiceSubmissionStatus.Accepted);

        await GetAsync();

        _access.Calls.Should().Contain(c => c.Set == FinanceAuthorizationFilter.DocumentEntitySet && c.Id == Eml && c.HasToken);
        _access.Calls.Should().Contain(c => c.Set == FinanceAuthorizationFilter.InvoiceSubmissionEntitySet && c.Id == row.Id && c.HasToken);
    }
}
