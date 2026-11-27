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

    // ---- ISS-045: the 17 grid configurations product code hard-codes by dev row id -------------------------------------

    /// <summary>The ids External Access widgets, the BFF external-grant allow-list, the workspace widgets and the pages hard-code.</summary>
    private static readonly string[] HardCodedGridConfigurationIds =
    [
        "113ad380-9e63-f111-ab0c-70a8a53ec687", "1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6", "3019a06e-9b5e-f111-ab0c-7c1e521545d7",
        "3af4102c-1092-f111-b8dc-7ced8ddc4a05", "3ff4102c-1092-f111-b8dc-7ced8ddc4a05", "403e5d37-cb94-f111-b8db-00224835447a",
        "42f4102c-1092-f111-b8dc-7ced8ddc4a05", "5294c28a-f078-f111-ab0e-7ced8ddc4a05", "583a2a33-1092-f111-b8dc-7ced8ddc4a05",
        "61711823-1092-f111-b8dc-7ced8ddc4a05", "97ee98e7-7a63-f111-ab0c-70a8a53ec687", "9c5b0ee7-7a63-f111-ab0c-000d3a4d8152",
        "ac05e4f1-8d85-f111-8075-7c1e5268570d", "d021827b-9b5e-f111-ab0c-7c1e521545d7", "e15c2b93-a05f-f111-a825-70a8a59455f4",
        "e1826c4c-9575-f111-ab0e-7ced8ddc4a05", "e7c8126a-968b-f111-8077-7ced8ddc4a05",
    ];

    [Fact]
    public void ISS045_EmbeddedGridConfigurations_AreExactlyTheHardCodedIds_EachWithAUsableConfig()
    {
        var grid = ReferenceDataSet.Tables.Single(t => t.Table == "sprk_gridconfiguration");

        grid.PrimaryKey.Should().Be("sprk_gridconfigurationid");
        grid.EntitySet.Should().Be("sprk_gridconfigurations");
        grid.Rows.Select(r => r.Id).Should().BeEquivalentTo(HardCodedGridConfigurationIds.Select(Guid.Parse));
        grid.Columns.Should().Contain(["sprk_name", "sprk_entitylogicalname", "sprk_configjson"]);
        grid.Columns.Should().NotContain(c => c.StartsWith("created", StringComparison.Ordinal) || c.StartsWith("modified", StringComparison.Ordinal)
                                              || c.Contains("owner", StringComparison.Ordinal) || c == "versionnumber",
            "no audit or ownership column travels to a stamp");

        foreach (var row in grid.Rows)
        {
            // sprk_configjson is the shared <DataGrid configId=...> document: it must parse and name its source.
            using var config = JsonDocument.Parse(row.Values["sprk_configjson"].GetString()!);
            config.RootElement.TryGetProperty("source", out _).Should().BeTrue($"'{row.DisplayName}' carries a grid source");
            row.Values["sprk_entitylogicalname"].GetString().Should().StartWith("sprk_");
        }
    }

    [Fact]
    public async Task ISS045_GridConfigurations_SeededIntoAnEmptyEnvironment_AreCreatedByTheirFixedIds()
    {
        var fake = new FakeDataverseHandler();
        var seeder = BuildSeeder(fake, ReferenceDataSet.Tables);

        var result = await seeder.SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        var posted = fake.Posts.Where(p => p.Uri.AbsolutePath == "/api/data/v9.2/sprk_gridconfigurations")
            .Select(p => JsonDocument.Parse(p.Body).RootElement)
            .ToList();
        posted.Select(e => e.GetProperty("sprk_gridconfigurationid").GetString())
            .Should().BeEquivalentTo(HardCodedGridConfigurationIds, "product code opens each row by this id, so the row is created with it");
        posted.Select(e => e.GetProperty("sprk_configjson").GetString()).Should().OnlyContain(json => !string.IsNullOrEmpty(json));
    }

    [Fact]
    public async Task ISS045_GridConfigurations_AlreadyPresentUnderTheirIds_AreNotWritten()
    {
        var grid = ReferenceDataSet.Tables.Single(t => t.Table == "sprk_gridconfiguration");
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_gridconfigurations"] = grid.Rows.Select(r =>
        {
            var row = r.Values.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            row[grid.PrimaryKey] = r.Id.ToString("D");
            return JsonSerializer.Serialize(row);
        }).ToArray();

        var result = await BuildSeeder(fake, [grid]).SeedAsync(Input(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("0 created").And.Contain("17 unchanged");
        fake.Posts.Should().BeEmpty();
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

    // ---- data-setup plan D4: the starter taxonomy pack (create-if-missing by natural key, never an update) --------------------

    private const string PackName = "legal-standard";
    private const string WidgetIdA = "11111111-0000-4000-8000-00000000000a";
    private const string WidgetIdB = "11111111-0000-4000-8000-00000000000b";
    private const string WidgetIdC = "11111111-0000-4000-8000-00000000000c";

    /// <summary>A code + name table (like the matter-type tables) and a name-only one (like project types).</summary>
    private const string WidgetPackJson = """
        {
          "table": "sprk_widget_ref",
          "entitySet": "sprk_widget_refs",
          "primaryKey": "sprk_widget_refid",
          "match": "naturalKey",
          "naturalKey": "sprk_widgetcode",
          "nameColumn": "sprk_widgetname",
          "columns": ["sprk_widgetcode", "sprk_widgetname"],
          "rows": [
            { "sprk_widget_refid": "11111111-0000-4000-8000-00000000000a", "sprk_widgetcode": "AAA", "sprk_widgetname": "Alpha" },
            { "sprk_widget_refid": "11111111-0000-4000-8000-00000000000b", "sprk_widgetcode": "BBB", "sprk_widgetname": "Beta" },
            { "sprk_widget_refid": "11111111-0000-4000-8000-00000000000c", "sprk_widgetcode": "CCC", "sprk_widgetname": "Gamma" }
          ]
        }
        """;

    private const string GadgetPackJson = """
        {
          "table": "sprk_gadget_ref",
          "entitySet": "sprk_gadget_refs",
          "primaryKey": "sprk_gadget_refid",
          "match": "naturalKey",
          "nameColumn": "sprk_name",
          "columns": ["sprk_name"],
          "rows": [
            { "sprk_gadget_refid": "22222222-0000-4000-8000-00000000000a", "sprk_name": "Litigation" },
            { "sprk_gadget_refid": "22222222-0000-4000-8000-00000000000b", "sprk_name": "Due Diligence" }
          ]
        }
        """;

    private static IReadOnlyList<ReferenceTable> WidgetPack { get; } = [ReferenceDataSet.Parse(WidgetPackJson), ReferenceDataSet.Parse(GadgetPackJson)];

    private static Func<string, IReadOnlyList<ReferenceTable>> PackLookup(IReadOnlyList<ReferenceTable> pack)
        => name => name == PackName ? pack : name == "none" ? [] : ReferenceDataSet.Pack(name);

    [Fact]
    public async Task D4_AbsentPackRows_AreCreated_WithThePacksIds_AndOnlyNonNullColumns()
    {
        var fake = new FakeDataverseHandler();

        var result = await BuildSeeder(fake, [], PackLookup(WidgetPack)).SeedAsync(PackInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain($"starter taxonomy pack '{PackName}'")
            .And.Contain("5 row(s) across 2 table(s)").And.Contain("5 created").And.Contain("0 already present");
        fake.Posts.Should().HaveCount(5);
        fake.Posts.Select(p => p.Uri.AbsolutePath).Distinct().Should()
            .BeEquivalentTo("/api/data/v9.2/sprk_widget_refs", "/api/data/v9.2/sprk_gadget_refs");
        var alpha = fake.Posts.Select(p => JsonDocument.Parse(p.Body).RootElement)
            .Single(e => e.TryGetProperty("sprk_widget_refid", out var id) && id.GetString() == WidgetIdA);
        alpha.GetProperty("sprk_widgetcode").GetString().Should().Be("AAA");
        alpha.GetProperty("sprk_widgetname").GetString().Should().Be("Alpha");
        result.Evidence!.Value.GetProperty("pack").GetProperty("created").GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task D4_AnExistingRow_MatchedByCode_ByName_OrById_IsNotCreated_AndNeverWritten()
    {
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_widget_refs"] =
        [
            // A: the customer's own id and name, the pack's code (padded, other case) -> present by CODE.
            "{\"sprk_widget_refid\":\"99999999-0000-4000-8000-000000000001\",\"sprk_widgetcode\":\" aaa \",\"sprk_widgetname\":\"Alpha (ours)\",\"statecode\":0}",
            // B: no code, same name as the pack's Beta in other case -> present by NAME.
            "{\"sprk_widget_refid\":\"99999999-0000-4000-8000-000000000002\",\"sprk_widgetcode\":null,\"sprk_widgetname\":\"BETA\",\"statecode\":0}",
            // C: the pack's own id, renamed and re-coded by the customer -> present by ID.
            "{\"sprk_widget_refid\":\"" + WidgetIdC + "\",\"sprk_widgetcode\":\"MINE\",\"sprk_widgetname\":\"Ours\",\"statecode\":0}",
        ];
        fake.Existing["sprk_gadget_refs"] = ["{\"sprk_gadget_refid\":\"99999999-0000-4000-8000-000000000003\",\"sprk_name\":\"  litigation \",\"statecode\":0}"];

        var result = await BuildSeeder(fake, [], PackLookup(WidgetPack)).SeedAsync(PackInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("4 already present").And.Contain("1 created");
        fake.Posts.Should().ContainSingle("only 'Due Diligence' is absent").Which.Body.Should().Contain("Due Diligence");
        fake.Methods.Should().NotContain(m => m == HttpMethod.Patch || m == HttpMethod.Put || m == HttpMethod.Delete,
            "the customer owns a pack row once it exists: it is never updated");
    }

    [Fact]
    public async Task D4_AnInactiveMatchingRow_CountsAsPresent_NotCreated_AndNotReported()
    {
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_widget_refs"] =
        [
            "{\"sprk_widget_refid\":\"" + WidgetIdA + "\",\"sprk_widgetcode\":\"AAA\",\"sprk_widgetname\":\"Alpha\",\"statecode\":1}",
            "{\"sprk_widget_refid\":\"" + WidgetIdB + "\",\"sprk_widgetcode\":\"BBB\",\"sprk_widgetname\":\"Beta\",\"statecode\":1}",
            "{\"sprk_widget_refid\":\"" + WidgetIdC + "\",\"sprk_widgetcode\":\"CCC\",\"sprk_widgetname\":\"Gamma\",\"statecode\":1}",
        ];
        fake.Existing["sprk_gadget_refs"] =
        [
            "{\"sprk_gadget_refid\":\"22222222-0000-4000-8000-00000000000a\",\"sprk_name\":\"Litigation\",\"statecode\":1}",
            "{\"sprk_gadget_refid\":\"22222222-0000-4000-8000-00000000000b\",\"sprk_name\":\"Due Diligence\",\"statecode\":1}",
        ];

        var result = await BuildSeeder(fake, [], PackLookup(WidgetPack)).SeedAsync(PackInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("5 already present").And.Contain("0 created")
            .And.NotContain("INACTIVE").And.NotContain("DIFFER", "a retired row is the customer's decision, not a finding");
        fake.Methods.Should().OnlyContain(m => m == HttpMethod.Get);
    }

    [Fact]
    public async Task D4_APresentRowThatDiffersFromThePack_IsReportedInTheDiagnosticOnly()
    {
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_widget_refs"] =
        [
            // Beta: present by name but the customer gave it another code (like dev's code-less contact types).
            "{\"sprk_widget_refid\":\"99999999-0000-4000-8000-000000000002\",\"sprk_widgetcode\":null,\"sprk_widgetname\":\"Beta\",\"statecode\":0}",
        ];

        var result = await BuildSeeder(fake, [], PackLookup(WidgetPack)).SeedAsync(PackInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, "a difference is reported, it does not fail the step");
        result.Diagnostic.Should().Contain("1 present row(s) DIFFER from the pack").And.Contain("sprk_widget_ref 'Beta' differs in sprk_widgetcode")
            .And.Contain("report only");
        fake.Posts.Should().NotContain(p => p.Body.Contains("Beta"), "Beta is present, so it is not created again");
        fake.Methods.Should().NotContain(m => m == HttpMethod.Patch || m == HttpMethod.Put || m == HttpMethod.Delete);
        result.Evidence!.Value.GetProperty("pack").GetProperty("differs").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task D4_PackNone_SeedsNothing_AndReadsNothingOfThePack()
    {
        var fake = new FakeDataverseHandler();

        var result = await BuildSeeder(fake, [], PackLookup(WidgetPack)).SeedAsync(PackInput("none"), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("starter taxonomy pack 'none': nothing seeded");
        fake.Methods.Should().BeEmpty("`none` means the customer brings its own taxonomy: no read, no write");
    }

    [Fact]
    public async Task D4_ThePackIsSeeded_AlongsideTier1_AndTier1WordingIsUnchanged()
    {
        var fake = new FakeDataverseHandler();

        var result = await BuildSeeder(fake, SmallTables, PackLookup(WidgetPack)).SeedAsync(PackInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        result.Diagnostic.Should().Contain("2 source row(s) across 1 table(s): 2 created")
            .And.Contain("5 row(s) across 2 table(s)", "the pack is counted apart from the Tier 1 tables");
        fake.Posts.Should().HaveCount(7);
    }

    [Fact]
    public async Task D4_AnUnknownPack_FailsTheStep_BeforeAnyWrite()
    {
        var fake = new FakeDataverseHandler();
        var seeder = new DataverseWebApiReferenceDataSeeder(
            new HttpClient(fake), Options.Create(new AppConfigSeedOptions()), NullLogger<DataverseWebApiReferenceDataSeeder>.Instance,
            _ => new StaticTokenCredential(), () => SmallTables);

        var result = await seeder.SeedAsync(PackInput("enterprise-plus"), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Failed);
        result.Diagnostic.Should().Contain("Unknown reference-data pack 'enterprise-plus'");
        fake.Methods.Should().BeEmpty();
    }

    [Fact]
    public async Task D4_ARefusedPackCreate_FailsTheStep_NamingThePack_AndAReRunSkipsTheRowsAlreadyCreated()
    {
        var failing = new FakeDataverseHandler { PostStatus = HttpStatusCode.BadRequest };

        var failed = await BuildSeeder(failing, [], PackLookup(WidgetPack)).SeedAsync(PackInput(), CancellationToken.None);

        failed.Status.Should().Be(AppConfigSeederStatus.Failed);
        failed.Diagnostic.Should().Contain($"starter taxonomy pack '{PackName}'").And.Contain("POST sprk_widget_refs");

        // The resume: Alpha now exists (created before the failure), the rest are absent.
        var resumed = new FakeDataverseHandler();
        resumed.Existing["sprk_widget_refs"] = ["{\"sprk_widget_refid\":\"" + WidgetIdA + "\",\"sprk_widgetcode\":\"AAA\",\"sprk_widgetname\":\"Alpha\",\"statecode\":0}"];
        var result = await BuildSeeder(resumed, [], PackLookup(WidgetPack)).SeedAsync(PackInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        resumed.Posts.Should().HaveCount(4).And.NotContain(p => p.Body.Contains(WidgetIdA));
    }

    [Fact]
    public void D4_ThePackTableParser_RejectsAFileWhoseMatchRuleIsUnknownOrWhoseNameColumnIsMissing()
    {
        var unknownMatch = WidgetPackJson.Replace("\"match\": \"naturalKey\"", "\"match\": \"fuzzy\"", StringComparison.Ordinal);
        var noNameColumn = WidgetPackJson.Replace("\"nameColumn\": \"sprk_widgetname\",", string.Empty, StringComparison.Ordinal);
        var nameNotAColumn = WidgetPackJson.Replace("\"nameColumn\": \"sprk_widgetname\"", "\"nameColumn\": \"sprk_nope\"", StringComparison.Ordinal);

        foreach (var json in new[] { unknownMatch, noNameColumn, nameNotAColumn })
        {
            var parse = () => ReferenceDataSet.Parse(json);
            parse.Should().Throw<InvalidOperationException>();
        }

        ReferenceDataSet.Parse(SmallTableJson).Match.Should().Be(ReferenceMatch.FixedId, "a Tier 1 file carries no match rule and keeps fixed-id semantics");
    }

    // ---- the real legal-standard pack ------------------------------------------------------------------------------

    [Fact]
    public void D4_TheEmbeddedLegalStandardPack_LoadsItsTables_WithUniqueIdsCodesAndNames()
    {
        var tables = ReferenceDataSet.Pack(PackName);

        tables.Select(t => t.Table).Should().Equal(ReferenceDataSet.PackTableNames[PackName]);
        // D5: the 8 UTBMS code sets and their 161 codes come first, then the seven taxonomy tables.
        tables.Select(t => t.Rows.Count).Should().Equal(8, 161, 14, 16, 14, 8, 18, 9, 8);
        foreach (var table in tables)
        {
            table.Match.Should().Be(ReferenceMatch.NaturalKey, table.Table);
            table.NameColumn.Should().NotBeNull(table.Table);
            table.Rows.Select(r => r.Id).Should().OnlyHaveUniqueItems($"{table.Table} ids").And.NotContain(Guid.Empty);
            string?[] names = table.Rows.Select(r => r.Values[table.NameColumn!].GetString()?.Trim().ToUpperInvariant()).ToArray();
            names.Should().OnlyHaveUniqueItems($"{table.Table} names").And.NotContainNulls();
            if (table.NaturalKey is not null)
            {
                string?[] codes = table.Rows.Select(r => r.Values[table.NaturalKey].GetString()?.Trim().ToUpperInvariant()).ToArray();
                codes.Should().OnlyHaveUniqueItems($"{table.Table} codes").And.NotContainNulls();
            }
        }

        tables.Single(t => t.Table == "sprk_projecttype_ref").NaturalKey.Should().BeNull("project types have no code column");
        ReferenceDataSet.Pack("none").Should().BeEmpty();
    }

    [Fact]
    public async Task D4_TheLegalStandardPack_OnAnEnvironmentHoldingDevsRows_KeepsDevsCodesAndNames_AndCreatesTheRest()
    {
        // spaarkedev1's own rows (2026-11-24): the dev values keep their codes and names exactly, so the pack matches them.
        var fake = new FakeDataverseHandler();
        fake.Existing["sprk_mattertype_refs"] =
        [
            "{\"sprk_mattertype_refid\":\"11aed095-30da-f011-8406-7ced8d1dc988\",\"sprk_mattertypecode\":\"LITG\",\"sprk_mattertypename\":\"Litigation\",\"statecode\":0}",
            "{\"sprk_mattertype_refid\":\"46c35aa2-30da-f011-8406-7ced8d1dc988\",\"sprk_mattertypecode\":\"PAT\",\"sprk_mattertypename\":\"Patent\",\"statecode\":0}",
        ];
        fake.Existing["sprk_contacttype_refs"] =
        [
            // dev's contact types have no code: matched by name, reported as differing in the code.
            "{\"sprk_contacttype_refid\":\"a59066e4-8f96-f111-b8db-0022482fb5a7\",\"sprk_contacttypecode\":null,\"sprk_contacttypename\":\"Outside Counsel\",\"statecode\":0}",
        ];
        fake.Existing["sprk_projecttype_refs"] =
        [
            "{\"sprk_projecttype_refid\":\"0ed9d8ac-b018-f111-8343-7ced8d1dc988\",\"sprk_name\":\"Litigation\",\"statecode\":0}",
        ];

        var result = await BuildSeeder(fake, [], ReferenceDataSet.Pack).SeedAsync(PackInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        fake.Posts.Should().HaveCount(87 + 8 + 161 - 4, "the 87 taxonomy rows and the 169 D5 billing rows, less the four dev rows that are present");
        var created = fake.Posts.Select(p => p.Body).ToList();
        created.Should().NotContain(b => b.Contains("\"LITG\"") || b.Contains("\"PAT\""));
        created.Should().Contain(b => b.Contains("\"CORPMA\""));
        result.Diagnostic.Should().Contain("sprk_contacttype_ref 'Outside Counsel' differs in sprk_contacttypecode");
        fake.Methods.Should().NotContain(m => m == HttpMethod.Patch || m == HttpMethod.Put || m == HttpMethod.Delete);
    }

    private static AppConfigSeedInput Input() => new("acme", TenantId, EnvUrl);

    private static AppConfigSeedInput PackInput(string pack = PackName) => new("acme", TenantId, EnvUrl, pack);

    private static DataverseWebApiReferenceDataSeeder BuildSeeder(
        FakeDataverseHandler fake,
        IReadOnlyList<ReferenceTable> tables,
        Func<string, IReadOnlyList<ReferenceTable>>? packTables = null)
        => new(
            new HttpClient(fake),
            Options.Create(new AppConfigSeedOptions()),
            NullLogger<DataverseWebApiReferenceDataSeeder>.Instance,
            _ => new StaticTokenCredential(),
            () => tables,
            packTables);

    internal sealed class StaticTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    /// <summary>An in-memory Dataverse: GET lists the configured rows of an entity set (optionally paged), POST records the body.</summary>
    internal sealed class FakeDataverseHandler : HttpMessageHandler
    {
        public Dictionary<string, string[]> Existing { get; } = new(StringComparer.Ordinal);
        public List<HttpMethod> Methods { get; } = new();
        public List<Uri> Gets { get; } = new();
        public List<(Uri Uri, string Body)> Posts { get; } = new();
        public HttpStatusCode PostStatus { get; init; } = HttpStatusCode.NoContent;
        public HttpStatusCode GetStatus { get; init; } = HttpStatusCode.OK;
        public int PageSize { get; init; } = int.MaxValue;

        /// <summary>When true a successful POST adds the row to <see cref="Existing"/>, so a second run sees what the first created.</summary>
        public bool PersistPosts { get; init; }

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
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Posts.Add((request.RequestUri, body));
                if (PersistPosts && PostStatus == HttpStatusCode.NoContent)
                {
                    Existing[entitySet] = (Existing.TryGetValue(entitySet, out var current) ? current : Array.Empty<string>())
                        .Append(StoredRow(body)).ToArray();
                }

                return PostStatus == HttpStatusCode.NoContent
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : Json(PostStatus, "{\"error\":{\"message\":\"refused\"}}");
            }

            // PATCH / PUT / DELETE: recorded in Methods; answered 200 so a wrongly-writing seeder is caught by the assertion, not by a crash.
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        /// <summary>
        /// What Dataverse would return for a row created by <paramref name="postBody"/>: the body's columns, <c>statecode</c> 0, and each
        /// <c>Nav@odata.bind</c> turned into the <c>_nav_value</c> lookup column (the Web API names it by the lowercase logical name).
        /// </summary>
        private static string StoredRow(string postBody)
        {
            var row = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in JsonDocument.Parse(postBody).RootElement.EnumerateObject())
            {
                const string bind = "@odata.bind";
                if (property.Name.EndsWith(bind, StringComparison.Ordinal))
                {
                    var target = property.Value.GetString()!;
                    var id = target[(target.IndexOf('(', StringComparison.Ordinal) + 1)..^1];
                    row[$"_{property.Name[..^bind.Length].ToLowerInvariant()}_value"] = JsonSerializer.SerializeToElement(id);
                }
                else
                {
                    row[property.Name] = property.Value.Clone();
                }
            }

            row["statecode"] = JsonSerializer.SerializeToElement(0);
            return JsonSerializer.Serialize(row);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
