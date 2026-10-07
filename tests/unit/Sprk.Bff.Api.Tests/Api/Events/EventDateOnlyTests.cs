// spaarke-ontology-platform-r1 task 098 (2026-10-05) — sprk_event's six date columns are calendar dates.
//
// Live (spaarkedev1): the columns were Format DateOnly / Behavior UserLocal, so Dataverse stored instants and the
// model-driven app showed 2026-10-02T00:00:00Z as 10/1 to every user west of UTC. They are now Behavior DateOnly
// (docs/data-model/sprk_event-date-columns.md): the Web API returns "yyyy-MM-dd" and REFUSES a timestamp on write
// (HTTP 400 "Cannot convert the literal '2026-10-02T00:00:00.000Z' to the expected type 'Edm.Date'", probed live).
//
// What was wrong in the BFF:
//   - DataverseWebApiService.MapToEventEntity read them with DateTime.Parse — a "…Z" value became the SERVER's local
//     time (10/1 20:00 on an Eastern machine) — and EventDto carried a timestamp clients read as an instant;
//   - only three of the six columns were read at all;
//   - POST /events/{id}/complete wrote DateTime.UtcNow's date: a 23:49 Eastern completion on June 3 was stored as
//     June 4 (live event a0cb27af, hand-corrected during the conversion).
//
// The route-level read tests execute the real GET handler. Run against the pre-098 code (2026-10-05, handler reached
// there without changing it) they FAIL: dueDate came back "2026-10-01T20:00:00-04:00" for a stored
// 2026-10-02T00:00:00Z on an Eastern machine, and finalDueDate / approvedDate / meetingDate did not exist.

using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Xunit;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;

namespace Sprk.Bff.Api.Tests.Api.Events;

[Trait("status", "task-098-ontology-r1")]
public class EventDateOnlyTests
{
    private static readonly Guid EventId = Guid.Parse("98098098-0000-0000-0000-000000000098");
    private static readonly Guid CallerSystemUserId = Guid.Parse("1d02f31c-1872-f011-b4cb-7c1e52671ad0");

    private static readonly string[] SixColumns =
    [
        "sprk_basedate", "sprk_duedate", "sprk_finalduedate", "sprk_completeddate", "sprk_approveddate", "sprk_meetingdate",
    ];

    private static readonly string[] SixDtoProperties =
        ["baseDate", "dueDate", "finalDueDate", "completedDate", "approvedDate", "meetingDate"];

    // ── Read path: GET /api/v1/events/{id} returns each of the six as the calendar date ──────────────────────

    [Theory]
    [InlineData("2026-10-02")]           // Behavior DateOnly — spaarkedev1 since 2026-10-05
    [InlineData("2026-10-02T00:00:00Z")] // UserLocal, written as a date string (an environment not yet converted)
    [InlineData("2026-10-02T04:00:00Z")] // UserLocal, an MDA entry at Eastern midnight (not yet converted)
    public async Task GetEvent_ReturnsAllSixDateColumns_AsTheCalendarDate_yyyyMMdd(string wire)
    {
        var json = await GetEventJson(Row(SixColumns.ToDictionary(c => c, _ => wire)));

        foreach (var property in SixDtoProperties)
        {
            json.TryGetProperty(property, out var value).Should().BeTrue($"EventDto exposes {property}");
            value.GetString().Should().Be("2026-10-02",
                $"{property} is a calendar date — no time, no zone, never moved to the server's local day");
        }
    }

    [Fact]
    public async Task GetEvent_AnEmptyDateColumn_IsNull()
    {
        var json = await GetEventJson(Row(new Dictionary<string, string> { ["sprk_duedate"] = "2026-10-02" }));

        json.GetProperty("dueDate").GetString().Should().Be("2026-10-02");
        json.GetProperty("completedDate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ReadUrls_SelectAllSixDateColumns()
    {
        foreach (var column in SixColumns)
            DataverseWebApiService.EventGetSelect.Should().Contain(column);
    }

    // ── DataverseDateOnly: the one parser / formatter ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-10-02", 2026, 10, 2)]
    [InlineData("2026-10-02T00:00:00Z", 2026, 10, 2)]
    [InlineData("2026-10-02T23:30:00-05:00", 2026, 10, 2)] // the leading ten characters, never a converted instant
    [InlineData("2026-06-04T03:49:16Z", 2026, 6, 4)]       // what the UTC conversion keeps for an unconverted value
    public void Parse_ReadsBothWireShapes_AsTheLeadingDate(string text, int y, int m, int d) =>
        DataverseDateOnly.Parse(text).Should().Be(new DateOnly(y, m, d));

    [Theory]
    [InlineData("10/02/2026")]
    [InlineData("2026-10-02 00:00")]
    [InlineData("2026-1-2")]
    [InlineData("2026-13-01")]
    [InlineData("")]
    public void Parse_RefusesAnythingElse(string text)
    {
        DataverseDateOnly.TryParse(text, out _).Should().BeFalse();
        var act = () => DataverseDateOnly.Parse(text);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Writes_AreInvariant_yyyyMMdd_EvenUnderANonGregorianCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // th-TH formats "yyyy" in the Buddhist era (2569) — a culture-sensitive ToString("yyyy-MM-dd") writes a date
            // Dataverse reads as 543 years in the future.
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            DataverseWebApiService.BuildUpdateEventStatusPayload(EventStatusCode.Completed, new DateOnly(2026, 10, 5))
                ["sprk_completeddate"].Should().Be("2026-10-05");
            DataverseWebApiService.BuildCreateEventPayload(new DataverseCreateEventRequest
            {
                Name = "Filing",
                DueDate = new DateOnly(2026, 10, 20),
                BaseDate = new DateOnly(2026, 10, 1),
            }).Should().Contain("sprk_duedate", "2026-10-20").And.Contain("sprk_basedate", "2026-10-01");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ── API request contract: a calendar date, not a timestamp ──────────────────────────────────────────────

    [Fact]
    public void CreateRequest_DueDate_IsACalendarDate_AndATimestampIsRefused()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        JsonSerializer.Deserialize<ApiCreateEventRequest>("""{ "subject": "Filing", "dueDate": "2026-10-02" }""", web)!
            .DueDate.Should().Be(new DateOnly(2026, 10, 2));

        var act = () => JsonSerializer.Deserialize<ApiCreateEventRequest>(
            """{ "subject": "Filing", "dueDate": "2026-10-02T00:00:00Z" }""", web);
        act.Should().Throw<JsonException>("Dataverse refuses a timestamp for a Date Only column, and converting one would " +
                                          "pick a time zone for the caller");
    }

    // ── Completion date: today in the completing user's own time zone ───────────────────────────────────────

    [Theory]
    [InlineData("2026-06-04T03:49:16Z", "2026-06-03")] // live event a0cb27af: 23:49 EDT on June 3
    [InlineData("2026-01-15T04:30:00Z", "2026-01-14")] // 23:30 EST (standard time)
    [InlineData("2026-10-05T14:00:00Z", "2026-10-05")]
    public void LocalDate_IsTheDateInTheZone(string utc, string expected) =>
        EventCompletionDate.LocalDate(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture), Eastern())
            .Should().Be(DateOnly.Parse(expected, CultureInfo.InvariantCulture));

    [Fact]
    public void LocalDate_WithoutAZone_IsTheUtcDate() =>
        EventCompletionDate.LocalDate(DateTimeOffset.Parse("2026-06-04T03:49:16Z", CultureInfo.InvariantCulture), null)
            .Should().Be(new DateOnly(2026, 6, 4));

    [Fact]
    public async Task Complete_WritesTheCallersLocalDate_FromTheirDataverseTimeZone()
    {
        var dv = OpenEvent();
        var entities = UserSettings(timeZoneCode: 35, standardName: "Eastern Standard Time");

        var result = await Complete(dv, Resolved(), entities.Object, "2026-06-04T03:49:16Z");

        StatusOf(result).Should().Be(StatusCodes.Status200OK);
        dv.Verify(d => d.UpdateEventStatusAsync(EventId, EventStatusCode.Completed, new DateOnly(2026, 6, 3), It.IsAny<CancellationToken>()),
            Times.Once, "23:49 on June 3 in the user's zone is June 3 — the UTC date (June 4) was the defect");
    }

    [Fact]
    public async Task Complete_WhenTheCallerCannotBeResolved_FallsBackToTheUtcDate_AndSaysSo()
    {
        var dv = OpenEvent();
        var log = new CapturingLogger();
        var resolver = new Mock<ICallerSystemUserResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-oid-claim"));

        var result = await Complete(dv, resolver.Object, Mock.Of<IGenericEntityService>(), "2026-06-04T03:49:16Z", log);

        StatusOf(result).Should().Be(StatusCodes.Status200OK);
        dv.Verify(d => d.UpdateEventStatusAsync(EventId, EventStatusCode.Completed, new DateOnly(2026, 6, 4), It.IsAny<CancellationToken>()),
            Times.Once);
        log.Warnings.Should().Contain(m => m.Contains("UTC") && m.Contains("caller-no-oid-claim"));
    }

    [Theory]
    [InlineData(null, "Eastern Standard Time", "no-timezonecode")]
    [InlineData(9035, "Not A Real Zone", "unknown-timezonecode-9035")] // a code no other test caches (zones are cached per process)
    public async Task Complete_WhenTheZoneIsMissingOrUnknown_FallsBackToTheUtcDate(int? code, string standardName, string reason)
    {
        var dv = OpenEvent();
        var log = new CapturingLogger();
        var entities = new Mock<IGenericEntityService>();
        var settings = new Entity("usersettings", CallerSystemUserId);
        if (code is { } c)
            settings["timezonecode"] = c;
        entities.Setup(e => e.RetrieveAsync("usersettings", CallerSystemUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection([new Entity("timezonedefinition") { ["standardname"] = standardName }]));

        var result = await Complete(dv, Resolved(), entities.Object, "2026-06-04T03:49:16Z", log);

        StatusOf(result).Should().Be(StatusCodes.Status200OK);
        dv.Verify(d => d.UpdateEventStatusAsync(EventId, EventStatusCode.Completed, new DateOnly(2026, 6, 4), It.IsAny<CancellationToken>()),
            Times.Once);
        log.Warnings.Should().ContainSingle(m => m.Contains(reason));
    }

    [Fact]
    public async Task Complete_WhenTheTimeZoneReadFails_StillCompletes_OnTheUtcDate()
    {
        var dv = OpenEvent();
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("usersettings", CallerSystemUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("privilege"));

        var result = await Complete(dv, Resolved(), entities.Object, "2026-06-04T03:49:16Z");

        StatusOf(result).Should().Be(StatusCodes.Status200OK, "a completion never fails because its date could not be localised");
        dv.Verify(d => d.UpdateEventStatusAsync(EventId, EventStatusCode.Completed, new DateOnly(2026, 6, 4), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── External SPA create: only a calendar date reaches Dataverse ─────────────────────────────────────────

    [Theory]
    [InlineData("2026-10-20")]
    [InlineData("2026-10-20T00:00:00.000Z")] // what earlier SPA builds sent: toISOString() of the picked day's UTC midnight
    public void ExternalCreateEventBody_WritesTheDueDateAs_yyyyMMdd(string sent) =>
        ExternalDataService.BuildEventCreatePayload(Guid.NewGuid(), new CreateExternalEventRequest { SprkName = "Kick-off", SprkDuedate = sent }, Guid.NewGuid())
            ["sprk_duedate"].Should().Be("2026-10-20", "Dataverse answers 400 to a timestamp for a Date Only column");

    // ── Schema of record ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DataModelDoc_RecordsDateOnlyBehaviour_ForAllSix()
    {
        var doc = File.ReadAllLines(Path.Combine(RepoRoot(), "docs", "data-model", "sprk_event-related-tables.md"));

        foreach (var column in SixColumns)
            doc.Should().ContainSingle(l => l.Contains($"| sprk_event ") && l.Contains($" {column} ") && l.Contains("Behavior: DateOnly"),
                $"{column} is Behavior DateOnly in spaarkedev1 since 2026-10-05 (task 098)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, JsonElement> Row(IReadOnlyDictionary<string, string> dates)
    {
        var extra = string.Join(", ", dates.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\""));
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            $$"""{ "sprk_eventid": "{{EventId}}", "sprk_eventname": "Filing", "statecode": 0, "statuscode": 659490001, {{extra}} }""")!;
    }

    /// <summary>GET /api/v1/events/{id} through its real (private) handler, serialised as the API serialises it.</summary>
    private static async Task<JsonElement> GetEventJson(Dictionary<string, JsonElement> row)
    {
        var entity = DataverseWebApiService.MapToEventEntity(row);
        var dv = new Mock<IEventDataverseService>();
        dv.Setup(d => d.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var result = await EventEndpoints.GetEventByIdAsync(EventId, new DefaultHttpContext(), dv.Object, NullLogger<Program>.Instance, CancellationToken.None);
        var value = result.Should().BeAssignableTo<IValueHttpResult>().Subject.Value;
        return JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static Mock<IEventDataverseService> OpenEvent()
    {
        var dv = new Mock<IEventDataverseService>(MockBehavior.Loose);
        dv.Setup(d => d.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventEntity { Id = EventId, Name = "zz-098 fixture", StatusCode = EventStatusCode.Open });
        return dv;
    }

    private static ICallerSystemUserResolver Resolved()
    {
        var resolver = new Mock<ICallerSystemUserResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        return resolver.Object;
    }

    private static Mock<IGenericEntityService> UserSettings(int timeZoneCode, string standardName)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("usersettings", CallerSystemUserId, It.Is<string[]>(c => c.Contains("timezonecode")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("usersettings", CallerSystemUserId) { ["timezonecode"] = timeZoneCode });
        entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "timezonedefinition"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection([new Entity("timezonedefinition") { ["standardname"] = standardName }]));
        return entities;
    }

    private static Task<IResult> Complete(
        Mock<IEventDataverseService> dv, ICallerSystemUserResolver resolver, IGenericEntityService entities, string utcNow,
        ILogger<Program>? log = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        return EventEndpoints.CompleteEventAsync(EventId, http, dv.Object,
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), resolver, entities,
            log ?? NullLogger<Program>.Instance, default);
    }

    private static TimeZoneInfo Eastern() => TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private static int StatusOf(IResult result) => result switch
    {
        IStatusCodeHttpResult { StatusCode: { } code } => code,
        _ => throw new InvalidOperationException($"Unexpected result {result.GetType().Name}"),
    };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }

    private sealed class CapturingLogger : ILogger<Program>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
