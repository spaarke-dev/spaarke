// -----------------------------------------------------------------------------
// DataverseWebApiReferenceDataSeederTests.cs
//
// ISS-039: a new stamp had no rows in sprk_eventtype_ref / sprk_agreementtype /
// sprk_triagecategory while product code hard-codes dev event-type row ids.
// DataverseWebApiReferenceDataSeeder creates the rows with their fixed primary
// keys (create-if-missing) and REPORTS drift, inactive rows and natural-key collisions
// on existing rows without ever writing them (owner decision A, data-setup-plan.md). ADR-038: a real HttpClient
// over a hand-rolled fake handler (no Mock<HttpMessageHandler>), parity with the
// sibling seeder tests.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiReferenceDataSeederTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";

    private const string IdA = "aaaaaaaa-0000-f011-8406-000000000001";
    private const string IdB = "bbbbbbbb-0000-f011-8406-000000000002";

    private const string SmallTableJson = """
        {
          "table": "sprk_thing",
          "entitySet": "sprk_things",
          "primaryKey": "sprk_thingid",
          "columns": ["sprk_cue", "sprk_enabled", "sprk_name", "sprk_note", "sprk_weight"],
          "rows": [
            { "sprk_thingid": "aaaaaaaa-0000-f011-8406-000000000001", "sprk_cue": "line one\nline two", "sprk_enabled": true,
              "sprk_name": "Alpha", "sprk_note": null, "sprk_weight": 1.5 },
            { "sprk_thingid": "bbbbbbbb-0000-f011-8406-000000000002", "sprk_cue": null, "sprk_enabled": false,
              "sprk_name": "Beta", "sprk_note": "n", "sprk_weight": 40 }
          ]
        }
        """;

    private static readonly IReadOnlyList<ReferenceTable> SmallTables = new[] { ReferenceDataSet.Parse(SmallTableJson) };

    [Fact]
    public async Task ISS039_MissingRows_AreCreated_WithTheirSourcePrimaryKeys_AndOnlyNonNullColumns()
    {
        var fake = new FakeDataverseHandler();
        var result = await BuildSeeder(fake, SmallTables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("2 created").And.Contain("0 unchanged").And.Contain("no drift");
        fake.Posts.Should().HaveCount(2);
        fake.Posts.Select(p => p.Uri.AbsolutePath).Should().OnlyContain(p => p == "/api/data/v9.2/sprk_things");

        var alpha = fake.Posts.Select(p => JsonDocument.Parse(p.Body).RootElement)
            .Single(e => e.GetProperty("sprk_thingid").GetString() == IdA);
        alpha.GetProperty("sprk_name").GetString().Should().Be("Alpha");
        alpha.GetProperty("sprk_cue").GetString().Should().Be("line one\nline two");
        alpha.GetProperty("sprk_enabled").GetBoolean().Should().BeTrue();
        alpha.GetProperty("sprk_weight").GetDecimal().Should().Be(1.5m);
        alpha.TryGetProperty("sprk_note", out _).Should().BeFalse("a null source value is not sent");

        var beta = fake.Posts.Select(p => JsonDocument.Parse(p.Body).RootElement)
            .Single(e => e.GetProperty("sprk_thingid").GetString() == IdB);
        beta.GetProperty("sprk_enabled").GetBoolean().Should().BeFalse("false is a value, not an absent one");
    }

    [Fact]
    public async Task ISS039_ExistingIdenticalRows_AreNotWritten()
    {
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_things"] =
        [
            // CRLF in Dataverse vs LF in source, 1.50 vs 1.5 and null vs "" must all compare equal.
            "{\"sprk_thingid\":\"" + IdA + "\",\"sprk_cue\":\"line one\\r\\nline two\",\"sprk_enabled\":true,\"sprk_name\":\"Alpha\",\"sprk_note\":\"\",\"sprk_weight\":1.50}",
            "{\"sprk_thingid\":\"" + IdB + "\",\"sprk_cue\":null,\"sprk_enabled\":false,\"sprk_name\":\"Beta\",\"sprk_note\":\"n\",\"sprk_weight\":40.0}",
        ];

        var result = await BuildSeeder(fake, SmallTables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("0 created").And.Contain("2 unchanged").And.Contain("no drift");
        fake.Methods.Should().OnlyContain(m => m == HttpMethod.Get, "an identical row needs no write of any kind");
    }

    [Fact]
    public async Task ISS039_ExistingDifferentRow_IsReportedAsDrift_AndNeverWritten()
    {
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_things"] =
        [
            // Alpha: a customer tuned the weight and renamed the row. Beta is absent -> created.
            "{\"sprk_thingid\":\"" + IdA + "\",\"sprk_cue\":\"line one\\nline two\",\"sprk_enabled\":true,\"sprk_name\":\"Alpha (tuned)\",\"sprk_note\":null,\"sprk_weight\":99}",
        ];

        var result = await BuildSeeder(fake, SmallTables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, "drift is reported, it does not fail the step");
        result.Diagnostic.Should().Contain("1 created").And.Contain("DRIFTED")
            .And.Contain($"sprk_thing/{IdA} 'Alpha'").And.Contain("sprk_name").And.Contain("sprk_weight")
            .And.Contain("left unchanged");
        fake.Methods.Should().NotContain(m => m == HttpMethod.Patch || m == HttpMethod.Put || m == HttpMethod.Delete,
            "an existing row is never updated");
        fake.Posts.Should().ContainSingle("only the absent row is created").Which.Body.Should().Contain(IdB);
        fake.Posts.Should().NotContain(p => p.Body.Contains(IdA), "the drifted row is not re-created either");
    }

    // ---- natural-key collisions (review finding 2) --------------------------------------------------------------

    [Fact]
    public async Task ISS039_ARowThatExistsUnderADifferentId_WithTheSameNaturalKey_IsReportedAsACollision_AndNotCreated()
    {
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_things"] =
        [
            // Same name as source Alpha, different id, different case and padding. Beta is absent and unrelated -> created.
            "{\"sprk_thingid\":\"cccccccc-0000-f011-8406-000000000003\",\"sprk_cue\":null,\"sprk_enabled\":true,\"sprk_name\":\"  ALPHA \",\"sprk_note\":null,\"sprk_weight\":1}",
        ];

        var result = await BuildSeeder(fake, SmallTables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, "a collision is reported, it does not fail the step");
        result.Diagnostic.Should().Contain("1 created").And.Contain("COLLIDED")
            .And.Contain("cccccccc-0000-f011-8406-000000000003").And.Contain(IdA).And.Contain("sprk_thing 'Alpha'");
        fake.Methods.Should().NotContain(m => m == HttpMethod.Patch || m == HttpMethod.Put || m == HttpMethod.Delete);
        fake.Posts.Should().ContainSingle("only Beta is created").Which.Body.Should().Contain(IdB);
        fake.Posts.Should().NotContain(p => p.Body.Contains(IdA), "creating Alpha would duplicate the natural key");
        result.Evidence!.Value.GetProperty("collisions").GetArrayLength().Should().Be(1);
        result.Evidence!.Value.GetProperty("drift").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ISS039_AnExistingRowWithADifferentNaturalKey_IsNotACollision_TheSourceRowIsCreated()
    {
        // Must-not-fire control: a row under another id whose key differs does not block the create.
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_things"] =
        [
            "{\"sprk_thingid\":\"cccccccc-0000-f011-8406-000000000003\",\"sprk_cue\":null,\"sprk_enabled\":true,\"sprk_name\":\"Gamma\",\"sprk_note\":null,\"sprk_weight\":1}",
        ];

        var result = await BuildSeeder(fake, SmallTables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("2 created").And.NotContain("COLLIDED");
        fake.Posts.Should().HaveCount(2);
    }

    [Fact]
    public async Task ISS039_AnEventTypeCodeHeldByAnotherId_IsACollision_OnTheRealEventTypeTable()
    {
        // sprk_eventtype_ref.sprk_eventtypecode is a Dataverse alternate key: a POST of the "Task" source id would be
        // refused (409). The seeder must see the other row and report it instead of failing the step.
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_eventtype_refs"] =
        [
            "{\"sprk_eventtype_refid\":\"dddddddd-0000-f011-8406-000000000004\",\"sprk_eventtypecode\":\"task\",\"sprk_name\":\"Task (stamp-made)\"}",
        ];

        var result = await BuildSeeder(fake, ReferenceDataSet.Tables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("COLLIDED").And.Contain("sprk_eventtypecode='TASK'")
            .And.Contain("dddddddd-0000-f011-8406-000000000004").And.Contain("124f5fc9-98ff-f011-8406-7c1e525abd8b");
        fake.Posts.Should().NotContain(p => p.Body.Contains("124f5fc9-98ff-f011-8406-7c1e525abd8b"));
        fake.Posts.Should().HaveCount(ReferenceDataSet.Tables.Sum(t => t.Rows.Count) - 1,
            "every other row of every table is still created");
    }

    [Fact]
    public async Task ISS039_AnAgreementTypeKeyHeldByAnotherId_IsACollision_OnTheRealAgreementTypeTable()
    {
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_agreementtypes"] =
        [
            "{\"sprk_agreementtypeid\":\"eeeeeeee-0000-f011-8406-000000000005\",\"sprk_key\":\"General\",\"sprk_name\":\"General agreement\"}",
        ];

        var result = await BuildSeeder(fake, ReferenceDataSet.Tables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("COLLIDED").And.Contain("sprk_key='GENERAL'")
            .And.Contain("eeeeeeee-0000-f011-8406-000000000005");
        fake.Posts.Should().HaveCount(ReferenceDataSet.Tables.Sum(t => t.Rows.Count) - 1);
    }

    // ---- inactive rows (review finding 3) ------------------------------------------------------------------------

    [Fact]
    public async Task ISS039_AnInactiveExistingRow_IsReportedAsDrift_NotUnchanged_AndNotReactivated()
    {
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_things"] =
        [
            // Alpha is identical to source but deactivated (statecode 1). Beta is identical and active.
            "{\"sprk_thingid\":\"" + IdA + "\",\"statecode\":1,\"sprk_cue\":\"line one\\nline two\",\"sprk_enabled\":true,\"sprk_name\":\"Alpha\",\"sprk_note\":null,\"sprk_weight\":1.5}",
            "{\"sprk_thingid\":\"" + IdB + "\",\"statecode\":0,\"sprk_cue\":null,\"sprk_enabled\":false,\"sprk_name\":\"Beta\",\"sprk_note\":\"n\",\"sprk_weight\":40}",
        ];

        var result = await BuildSeeder(fake, SmallTables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, "an inactive row is reported, it does not fail the step");
        result.Diagnostic.Should().Contain("1 unchanged").And.Contain("DRIFTED")
            .And.Contain($"sprk_thing/{IdA} 'Alpha'").And.Contain("INACTIVE").And.Contain("not reactivated");
        fake.Methods.Should().OnlyContain(m => m == HttpMethod.Get, "an inactive row is neither recreated nor reactivated");
    }

    [Fact]
    public async Task ISS039_TheExistingRowRead_SelectsStateCode_AndTheNaturalKeyColumn()
    {
        var fake = new FakeDataverseHandler();

        await BuildSeeder(fake, ReferenceDataSet.Tables).SeedAsync(Input(), CancellationToken.None);

        fake.Gets.Should().OnlyContain(u => u.Query.Contains("statecode"));
        fake.Gets.Single(u => u.AbsolutePath.EndsWith("sprk_eventtype_refs", StringComparison.Ordinal)).Query.Should().Contain("sprk_eventtypecode");
        fake.Gets.Single(u => u.AbsolutePath.EndsWith("sprk_agreementtypes", StringComparison.Ordinal)).Query.Should().Contain("sprk_key");
    }

    [Fact]
    public async Task ISS039_AFailedCreate_FailsTheScope_WithStatusAndBody_AndStopsAtTheFirstRefusal()
    {
        var fake = new FakeDataverseHandler { PostStatus = HttpStatusCode.BadRequest };

        var result = await BuildSeeder(fake, SmallTables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Failed);
        result.Diagnostic.Should().Contain("400").And.Contain("POST sprk_things")
            .And.Contain("A re-run reads the table again").And.NotContain("full retry is safe");
        fake.Posts.Should().HaveCount(1);
    }

    [Fact]
    public async Task ISS039_AFailedRead_FailsTheScope_AndWritesNothing()
    {
        var fake = new FakeDataverseHandler { GetStatus = HttpStatusCode.Forbidden };

        var result = await BuildSeeder(fake, SmallTables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Failed);
        result.Diagnostic.Should().Contain("403").And.Contain("GET sprk_things");
        fake.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task ISS039_ExistingRowsOnASecondPage_AreSeenAndNotRecreated()
    {
        var fake = new FakeDataverseHandler { PageSize = 1 };
        fake.Existing["sprk_things"] =
        [
            "{\"sprk_thingid\":\"" + IdA + "\",\"sprk_cue\":\"line one\\nline two\",\"sprk_enabled\":true,\"sprk_name\":\"Alpha\",\"sprk_note\":null,\"sprk_weight\":1.5}",
            "{\"sprk_thingid\":\"" + IdB + "\",\"sprk_cue\":null,\"sprk_enabled\":false,\"sprk_name\":\"Beta\",\"sprk_note\":\"n\",\"sprk_weight\":40}",
        ];

        var result = await BuildSeeder(fake, SmallTables).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("2 unchanged");
        fake.Posts.Should().BeEmpty();
    }

    [Fact]
    public void ISS039_EmbeddedReferenceData_LoadsEveryTable_WithUniqueFixedKeys()
    {
        var tables = ReferenceDataSet.Tables;

        tables.Select(t => t.Table).Should().Equal(ReferenceDataSet.TableNames);
        tables.Single(t => t.Table == "sprk_eventtype_ref").Rows.Should().HaveCount(17);
        tables.Single(t => t.Table == "sprk_agreementtype").Rows.Should().HaveCount(10);
        tables.Single(t => t.Table == "sprk_triagecategory").Rows.Should().HaveCount(10);
        foreach (var table in tables)
        {
            table.Rows.Select(r => r.Id).Should().OnlyHaveUniqueItems($"{table.Table} primary keys are the fixed ids code depends on");
            table.Rows.Should().OnlyContain(r => r.Id != Guid.Empty);
            table.EntitySet.Should().StartWith("sprk_");
        }

        // The "Task" event type: the id DailyBriefingCollector, PortfolioService and TaskActionCore hard-code.
        tables.Single(t => t.Table == "sprk_eventtype_ref").Rows
            .Should().Contain(r => r.Id == Guid.Parse("124f5fc9-98ff-f011-8406-7c1e525abd8b") && r.DisplayName == "Task");

        // Environment-specific measurements are not part of the seed.
        tables.Single(t => t.Table == "sprk_triagecategory").Columns
            .Should().NotContain(["sprk_measuredrecall", "sprk_labelledsetsize", "sprk_recallmeasuredon"]);

        // Natural keys: the two Dataverse alternate keys, and the display name for the table that has none.
        tables.Single(t => t.Table == "sprk_eventtype_ref").NaturalKey.Should().Be("sprk_eventtypecode");
        tables.Single(t => t.Table == "sprk_agreementtype").NaturalKey.Should().Be("sprk_key");
        tables.Single(t => t.Table == "sprk_triagecategory").NaturalKey.Should().Be("sprk_name");
    }

    [Fact]
    public async Task ISS039_EmbeddedData_SeededIntoAnEmptyEnvironment_CreatesEverySourceRow()
    {
        var fake = new FakeDataverseHandler();
        var seeder = BuildSeeder(fake, ReferenceDataSet.Tables);

        var result = await seeder.SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        fake.Posts.Should().HaveCount(ReferenceDataSet.Tables.Sum(t => t.Rows.Count));
        var task = fake.Posts.Select(p => JsonDocument.Parse(p.Body).RootElement)
            .Single(e => e.TryGetProperty("sprk_eventtype_refid", out var id) && id.GetString() == "124f5fc9-98ff-f011-8406-7c1e525abd8b");
        task.GetProperty("sprk_eventtypecode").GetString().Should().Be("TASK");
    }

    [Fact]
    public void ISS039_ReferenceDataSeeder_IsRegisteredInTheH12bSeederSet()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddH12bAppConfigSeedHandler(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var seeders = scope.ServiceProvider.GetServices<IAppConfigSeeder>().ToList();

        seeders.Select(s => s.ScopeName).Should().Contain(AppConfigSeedScopes.ReferenceData);
        seeders.Should().ContainSingle(s => s is DataverseWebApiReferenceDataSeeder);
    }

    private static AppConfigSeedInput Input() => new("acme", TenantId, EnvUrl);

    private static DataverseWebApiReferenceDataSeeder BuildSeeder(FakeDataverseHandler fake, IReadOnlyList<ReferenceTable> tables)
        => new(
            new HttpClient(fake),
            Options.Create(new AppConfigSeedOptions()),
            NullLogger<DataverseWebApiReferenceDataSeeder>.Instance,
            _ => new StaticTokenCredential(),
            () => tables);

    private sealed class StaticTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    /// <summary>An in-memory Dataverse: GET lists the configured rows of an entity set (optionally paged), POST records the body.</summary>
    private sealed class FakeDataverseHandler : HttpMessageHandler
    {
        public Dictionary<string, string[]> Existing { get; } = new(StringComparer.Ordinal);
        public List<HttpMethod> Methods { get; } = new();
        public List<Uri> Gets { get; } = new();
        public List<(Uri Uri, string Body)> Posts { get; } = new();
        public HttpStatusCode PostStatus { get; init; } = HttpStatusCode.NoContent;
        public HttpStatusCode GetStatus { get; init; } = HttpStatusCode.OK;
        public int PageSize { get; init; } = int.MaxValue;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            var entitySet = request.RequestUri!.AbsolutePath.Split('/')[^1];

            if (request.Method == HttpMethod.Get)
            {
                Gets.Add(request.RequestUri);
                if (GetStatus != HttpStatusCode.OK)
                {
                    return Json(GetStatus, "{\"error\":{\"message\":\"denied\"}}");
                }

                var rows = Existing.TryGetValue(entitySet, out var configured) ? configured : [];
                var skip = int.TryParse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["skip"], out var s) ? s : 0;
                var page = rows.Skip(skip).Take(PageSize).ToArray();
                var next = skip + page.Length < rows.Length
                    ? $",\"@odata.nextLink\":\"https://acme.crm.dynamics.com/api/data/v9.2/{entitySet}?skip={skip + page.Length}\""
                    : string.Empty;
                return Json(HttpStatusCode.OK, "{\"value\":[" + string.Join(",", page) + "]" + next + "}");
            }

            if (request.Method == HttpMethod.Post)
            {
                Posts.Add((request.RequestUri, await request.Content!.ReadAsStringAsync(cancellationToken)));
                return PostStatus == HttpStatusCode.NoContent
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : Json(PostStatus, "{\"error\":{\"message\":\"refused\"}}");
            }

            // PATCH / PUT / DELETE: recorded in Methods; answered 200 so a wrongly-writing seeder is caught by the assertion, not by a crash.
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
