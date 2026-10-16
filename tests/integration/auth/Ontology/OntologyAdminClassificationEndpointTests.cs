using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// Ontology platform R1 task 103 (D-22, FR-59, D-49, NFR-10): the Classification routes under
/// <c>/api/v1/ontology/admin/classification</c> through the REAL pipeline (authentication, rate limiting, the production handlers and
/// service). Only the caller-identity Dataverse client is substituted, by a small Dataverse that holds the triage categories, the
/// policies and their versions, answers the volume aggregate, records every write with WHO made it and the row version it was
/// conditional on, and refuses a Console User's write the way Dataverse does (403). The live read-only leg is
/// <c>tests/integration/Sprk.Bff.Api.IntegrationTests/Ontology/OntologyAdminClassificationLiveTests.cs</c>.
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path).</remarks>
public class OntologyAdminClassificationEndpointTests : IClassFixture<ClassificationTestFixture>
{
    private const string Root = "/api/v1/ontology/admin/classification/categories";

    private static readonly Guid Fee = Guid.Parse("8b62dd84-1fbc-f111-aaaf-3833c5e9614d");
    private static readonly Guid Scope = Guid.Parse("8d62dd84-1fbc-f111-aaaf-3833c5e9614d");
    private static readonly Guid Invoice = Guid.Parse("71310056-598b-f111-8077-7ced8ddc4cc6");
    private static readonly Guid Marketing = Guid.Parse("8b310056-598b-f111-8077-7ced8ddc4cc6");
    private static readonly Guid Missing = Guid.Parse("10300000-0000-4000-8000-0000000000ff");

    private readonly ClassificationTestFixture _f;

    public OntologyAdminClassificationEndpointTests(ClassificationTestFixture fixture)
    {
        _f = fixture;
        _f.Reset();
    }

    private async Task<(HttpResponseMessage Response, JsonElement Body)> Send(HttpMethod method, string route, object? body = null, bool authenticated = true)
    {
        using var client = _f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "oid-103");
        }

        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response, json);
    }

    /// <summary>A rule body whose filter names the given JSON value under sprk_triagecategory (the shape task 004 seeded).</summary>
    internal static string Named(string json) => "{\"all\":[{\"filter\":{\"sprk_triagecategory\":" + json + "}}]}";

    private static string Reason(JsonElement body) => body.GetProperty("reasonCode").GetString()!;

    private static JsonElement CategoryIn(JsonElement list, Guid id) =>
        list.GetProperty("categories").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);

    // ── Criterion 1: the list ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheList_ShowsGuidanceOnOffReadersVolumeAndRecall_ForEachCategory()
    {
        var (response, list) = await Send(HttpMethod.Get, Root);

        response.StatusCode.Should().Be(HttpStatusCode.OK, list.ToString());
        list.GetProperty("categories").GetArrayLength().Should().Be(4);
        list.GetProperty("volumeWindowDays").GetInt32().Should().Be(30);
        list.GetProperty("volumeAvailable").GetBoolean().Should().BeTrue();
        list.GetProperty("referencesAvailable").GetBoolean().Should().BeTrue();
        list.GetProperty("recallFloor").GetProperty("percent").GetInt32().Should().Be(80);
        list.GetProperty("recallFloor").GetProperty("minItems").GetInt32().Should().Be(50);

        var fee = CategoryIn(list, Fee);
        fee.GetProperty("name").GetString().Should().Be("Fee / rate change");
        fee.GetProperty("guidance").GetString().Should().Be("A change to rates or fees.");
        fee.GetProperty("enabled").GetBoolean().Should().BeTrue();
        fee.GetProperty("volume30Days").GetInt32().Should().Be(1);
        var readBy = fee.GetProperty("readBy").EnumerateArray().ToList();
        readBy.Select(r => r.GetProperty("policyCode").GetString()).Should().OnlyContain(c => c == "POL-COMMIT-BUDGET");
        readBy.Select(r => (r.GetProperty("versionNumber").GetInt32(), r.GetProperty("versionState").GetString()))
            .Should().BeEquivalentTo(new[] { (2, "InForce"), (1, "Earlier") });

        CategoryIn(list, Invoice).GetProperty("readBy").GetArrayLength().Should().Be(0);
        CategoryIn(list, Invoice).GetProperty("volume30Days").GetInt32().Should().Be(0, "no communications in the window is zero, not unknown");
    }

    [Fact]
    public async Task TheList_ShowsAMeasuredCategorysRecallSetSizeAndDate_AgainstTheFloor_AndNotMeasuredOtherwise()
    {
        var (_, list) = await Send(HttpMethod.Get, Root);

        var fee = CategoryIn(list, Fee).GetProperty("recall");
        fee.GetProperty("status").GetString().Should().Be("MeetsFloor");
        fee.GetProperty("recall").GetDecimal().Should().Be(88.89m);
        fee.GetProperty("labelledSetSize").GetInt32().Should().Be(92);
        fee.GetProperty("measuredOn").GetString().Should().Be("2026-10-10");

        var invoice = CategoryIn(list, Invoice).GetProperty("recall");
        invoice.GetProperty("status").GetString().Should().Be("NotMeasured");
        invoice.GetProperty("recall").ValueKind.Should().Be(JsonValueKind.Null, "never shown as zero");
    }

    [Fact]
    public async Task TheList_CarriesNoClassifierConfidenceAnywhere()
    {
        var (_, list) = await Send(HttpMethod.Get, Root);

        list.GetRawText().Should().NotContainEquivalentOf("confidence", "no classifier confidence exists (#5)");
    }

    [Fact]
    public async Task WhenThePoliciesCannotBeRead_TheListSaysSo_AndDoesNotClaimNoRuleReadsACategory()
    {
        _f.Faults.Add("versions");

        var (response, list) = await Send(HttpMethod.Get, Root);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        list.GetProperty("referencesAvailable").GetBoolean().Should().BeFalse();
        CategoryIn(list, Fee).GetProperty("readBy").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task WhenTheVolumeCannotBeRead_EveryVolumeIsNullNotZero()
    {
        _f.Faults.Add("volume");

        var (_, list) = await Send(HttpMethod.Get, Root);

        list.GetProperty("volumeAvailable").GetBoolean().Should().BeFalse();
        CategoryIn(list, Fee).GetProperty("volume30Days").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheList_IsAFaultNotAnEmptyList_WhenTheCategoriesCannotBeRead()
    {
        _f.Faults.Add("categories");

        var (response, body) = await Send(HttpMethod.Get, Root);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        Reason(body).Should().Be("ontology.admin.classification.unavailable");
    }

    // ── Criterion 2: create (saves Off) and edit (as the caller) ──────────────────────────────────────────────────

    [Fact]
    public async Task ANewCategory_IsCreatedOff_EvenWhenTheBodyAsksForOn_AndAsTheCaller()
    {
        var (response, row) = await Send(HttpMethod.Post, Root, new { name = "  Settlement  ", guidance = "An offer to settle.", enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created, row.ToString());
        row.GetProperty("name").GetString().Should().Be("Settlement", "the name is trimmed");
        row.GetProperty("enabled").GetBoolean().Should().BeFalse();
        var write = _f.Writes.Single();
        write.Method.Should().Be("POST");
        write.Caller.Should().Be(ClassificationTestFixture.Who.Admin);
        using var sent = JsonDocument.Parse(write.Body);
        sent.RootElement.GetProperty("sprk_enabled").GetBoolean().Should().BeFalse();
        sent.RootElement.GetProperty("sprk_name").GetString().Should().Be("Settlement");
        sent.RootElement.GetProperty("sprk_classifierguidance").GetString().Should().Be("An offer to settle.");
        response.Headers.Location!.ToString().Should().EndWith($"/categories/{row.GetProperty("id").GetGuid():D}");
    }

    [Theory]
    [InlineData(null, "name")]
    [InlineData("", "name")]
    [InlineData("   ", "name")]
    [InlineData("Line\nbreak", "name")]
    public async Task CreateRefusesABadName_AndWritesNothing(string? name, string field)
    {
        var (response, body) = await Send(HttpMethod.Post, Root, new { name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body.ToString());
        body.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRefusesATooLongNameOrGuidance_AndWritesNothing()
    {
        var (longName, _) = await Send(HttpMethod.Post, Root, new { name = new string('x', 101) });
        var (longGuidance, _) = await Send(HttpMethod.Post, Root, new { name = "Ok", guidance = new string('g', 2001) });

        longName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        longGuidance.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRefusesATakenName_IgnoringCase()
    {
        var (response, body) = await Send(HttpMethod.Post, Root, new { name = "FEE / RATE CHANGE" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Reason(body).Should().Be("ontology.admin.classification.name_taken");
        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task EditingGuidance_IsSavedAsTheCaller_ConditionalOnTheRowVersion_AndWritesOnlyThatField()
    {
        var version = _f.Categories[Invoice].Version;

        var (response, row) = await Send(HttpMethod.Patch, $"{Root}/{Invoice:D}", new { guidance = "Bills and amounts due.", version });

        response.StatusCode.Should().Be(HttpStatusCode.OK, row.ToString());
        row.GetProperty("guidance").GetString().Should().Be("Bills and amounts due.");
        var write = _f.Writes.Single();
        write.Method.Should().Be("PATCH");
        write.Caller.Should().Be(ClassificationTestFixture.Who.Admin);
        write.ExpectedVersion.Should().Be(version);
        using var sent = JsonDocument.Parse(write.Body);
        sent.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("sprk_classifierguidance");
        _f.Categories[Invoice].Guidance.Should().Be("Bills and amounts due.");
    }

    [Fact]
    public async Task AnEditThatChangesNothing_WritesNothing()
    {
        var (response, _) = await Send(HttpMethod.Patch, $"{Root}/{Invoice:D}", new { name = "Invoice / Billing", guidance = "Invoices." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _f.Writes.Should().BeEmpty();
    }

    // ── Criterion 3: rename blocked while a rule reads the category ───────────────────────────────────────────────

    [Fact]
    public async Task ARenameIsRefused_WhileAnInForceRuleReadsTheCategory_NamingTheRule()
    {
        var (response, body) = await Send(HttpMethod.Patch, $"{Root}/{Fee:D}", new { name = "Fee change" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body.ToString());
        Reason(body).Should().Be("ontology.admin.classification.rename_blocked");
        body.GetProperty("detail").GetString().Should().Contain("POL-COMMIT-BUDGET");
        body.GetProperty("blockedBy").EnumerateArray().Select(b => b.GetProperty("policyCode").GetString())
            .Should().OnlyContain(c => c == "POL-COMMIT-BUDGET");
        _f.Writes.Should().BeEmpty("a refused rename writes nothing");
        _f.Categories[Fee].Name.Should().Be("Fee / rate change");
    }

    [Theory]
    [InlineData("draft")]      // a saved draft version, not yet published
    [InlineData("earlier")]    // a superseded version
    public async Task ARenameIsRefused_WhenOnlyADraftOrAnEarlierVersionReadsTheCategory(string kind)
    {
        _f.Policies[Guid.Parse("10300000-0000-4000-8000-000000000101")] = new("POL-ONLY-" + kind, "Only " + kind, Enabled: false, CurrentVersion: null);
        _f.Versions[Guid.Parse("10300000-0000-4000-8000-000000000201")] = new(
            Guid.Parse("10300000-0000-4000-8000-000000000101"), 1, Named($"[\"{Marketing}\"]"),
            InforceTo: kind == "earlier" ? "2026-09-01T00:00:00Z" : null);

        var (response, body) = await Send(HttpMethod.Patch, $"{Root}/{Marketing:D}", new { name = "Noise" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body.ToString());
        Reason(body).Should().Be("ontology.admin.classification.rename_blocked");
        body.GetProperty("blockedBy")[0].GetProperty("versionState").GetString().Should().Be(kind == "draft" ? "Draft" : "Earlier");
        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ARenameIsRefused_WhenAPolicyOfAnotherDomainReadsTheCategory()
    {
        // No domain filter anywhere: a billing-domain policy (the ebilling project's) that reads a category blocks its rename like any other.
        _f.Policies[Guid.Parse("10300000-0000-4000-8000-000000000102")] = new("POL-BILLING-LEDES", "Billing rule", Enabled: true,
            CurrentVersion: Guid.Parse("10300000-0000-4000-8000-000000000202"), Domain: 100000000);
        _f.Versions[Guid.Parse("10300000-0000-4000-8000-000000000202")] = new(
            Guid.Parse("10300000-0000-4000-8000-000000000102"), 1, Named($"\"{Invoice}\""), InforceTo: null);

        var (response, body) = await Send(HttpMethod.Patch, $"{Root}/{Invoice:D}", new { name = "Billing" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        body.GetProperty("detail").GetString().Should().Contain("POL-BILLING-LEDES");
        _f.Writes.Should().BeEmpty();
        _f.Requests.Should().NotContain(r => r.Contains("sprk_domain", StringComparison.Ordinal), "the policy read has no domain filter");
    }

    [Theory]
    [InlineData("upper")]
    [InlineData("n-format")]
    [InlineData("operator-object")]
    [InlineData("damaged-json")]
    public async Task ARenameIsRefused_HoweverTheRuleBodyWritesTheId(string shape)
    {
        var id = Marketing;
        var body = shape switch
        {
            "upper" => Named($"[\"{id.ToString("D").ToUpperInvariant()}\"]"),
            "n-format" => Named($"[\"{id:N}\"]"),
            "operator-object" => Named($"{{\"<>\":\"{id}\"}}"),
            _ => Named($"[\"{id}\"]")[..^3], // truncated: not valid JSON
        };
        _f.Policies[Guid.Parse("10300000-0000-4000-8000-000000000103")] = new("POL-SHAPE", null, Enabled: true, CurrentVersion: null);
        _f.Versions[Guid.Parse("10300000-0000-4000-8000-000000000203")] = new(Guid.Parse("10300000-0000-4000-8000-000000000103"), 1, body, InforceTo: null);

        var (response, _) = await Send(HttpMethod.Patch, $"{Root}/{Marketing:D}", new { name = "Noise" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ACaseOnlyRename_CountsAsARename()
    {
        var (response, _) = await Send(HttpMethod.Patch, $"{Root}/{Fee:D}", new { name = "fee / rate change" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _f.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("policies")]
    [InlineData("versions")]
    public async Task ARename_IsRefusedAsAFault_WhenTheRulesCannotBeRead_NeverAllowedUnchecked(string failing)
    {
        _f.Faults.Add(failing);

        var (response, body) = await Send(HttpMethod.Patch, $"{Root}/{Marketing:D}", new { name = "Noise" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body.ToString());
        Reason(body).Should().Be("ontology.admin.classification.unavailable");
        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnreferencedCategory_CanBeRenamed_AsTheCaller()
    {
        var (response, row) = await Send(HttpMethod.Patch, $"{Root}/{Marketing:D}", new { name = "Noise / Marketing" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, row.ToString());
        row.GetProperty("name").GetString().Should().Be("Noise / Marketing");
        var write = _f.Writes.Single();
        write.Caller.Should().Be(ClassificationTestFixture.Who.Admin);
        using var sent = JsonDocument.Parse(write.Body);
        sent.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("sprk_name");
    }

    [Fact]
    public async Task ARenameToATakenName_IsRefused()
    {
        var (response, body) = await Send(HttpMethod.Patch, $"{Root}/{Marketing:D}", new { name = "invoice / billing" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Reason(body).Should().Be("ontology.admin.classification.name_taken");
        _f.Writes.Should().BeEmpty();
    }

    // ── Concurrency ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEditFromAnOutOfDateForm_IsStale_AndWritesNothing()
    {
        var (response, body) = await Send(HttpMethod.Patch, $"{Root}/{Invoice:D}", new { guidance = "x", version = _f.Categories[Invoice].Version - 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Reason(body).Should().Be("ontology.admin.classification.stale");
        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEditRacingAConcurrentChange_IsStale_NothingOverwritten()
    {
        _f.BeforeWrite = () => _f.Categories[Invoice].Version += 1;

        var (response, body) = await Send(HttpMethod.Patch, $"{Root}/{Invoice:D}", new { guidance = "mine" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body.ToString());
        Reason(body).Should().Be("ontology.admin.classification.stale");
        _f.Categories[Invoice].Guidance.Should().Be("Invoices.");
    }

    // ── On/Off ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TurningACategoryOn_WritesOnlyTheFlag_AsTheCaller_ConditionalOnTheVersion()
    {
        _f.Categories[Marketing].Enabled = false;
        var version = _f.Categories[Marketing].Version;

        var (response, row) = await Send(HttpMethod.Put, $"{Root}/{Marketing:D}/enabled", new { enabled = true, version });

        response.StatusCode.Should().Be(HttpStatusCode.OK, row.ToString());
        row.GetProperty("enabled").GetBoolean().Should().BeTrue();
        var write = _f.Writes.Single();
        write.Caller.Should().Be(ClassificationTestFixture.Who.Admin);
        write.ExpectedVersion.Should().Be(version);
        using var sent = JsonDocument.Parse(write.Body);
        sent.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("sprk_enabled");
    }

    [Fact]
    public async Task TurningACategoryToItsCurrentState_WritesNothing()
    {
        var (response, _) = await Send(HttpMethod.Put, $"{Root}/{Marketing:D}/enabled", new { enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task OnOff_RefusesABodyWithoutAFlag()
    {
        var (response, _) = await Send(HttpMethod.Put, $"{Root}/{Marketing:D}/enabled", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _f.Writes.Should().BeEmpty();
    }

    // ── Criterion 4: a Console User, an unresolved caller, no token ───────────────────────────────────────────────

    public static TheoryData<string, string, object?> WriteRoutes => new()
    {
        { "POST", Root, new { name = "Settlement" } },
        { "PATCH", $"{Root}/{Invoice:D}", new { guidance = "Changed." } },
        { "PUT", $"{Root}/{Invoice:D}/enabled", new { enabled = false } },
    };

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task AConsoleUser_IsRefusedByEveryWriteRoute_AndNothingIsWritten(string method, string route, object? body)
    {
        _f.Caller = ClassificationTestFixture.Who.ConsoleUser;
        var before = JsonSerializer.Serialize(_f.Categories);

        var (response, problem) = await Send(new HttpMethod(method), route, body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, problem.ToString());
        Reason(problem).Should().Be("ontology.admin.classification.not_permitted");
        _f.Writes.Should().BeEmpty();
        JsonSerializer.Serialize(_f.Categories).Should().Be(before);
    }

    [Theory]
    [InlineData("DATAVERSE_USER_CONTEXT_REQUIRED", 0)]
    [InlineData("DATAVERSE_OBO_EXCHANGE_FAILED", 0)]
    [InlineData("DATAVERSE_OBO_NOT_CONFIGURED", 0)]
    [InlineData("DATAVERSE_ACCESS_DENIED", 401)]
    public async Task AnUnresolvedCaller_IsTheSingle403_OnEveryRoute(string error, int status)
    {
        _f.EveryCallFails = (error, status);

        var routes = new (HttpMethod Method, string Route, object? Body)[]
        {
            (HttpMethod.Get, Root, null),
            (HttpMethod.Post, Root, new { name = "Settlement" }),
            (HttpMethod.Patch, $"{Root}/{Invoice:D}", new { guidance = "x" }),
            (HttpMethod.Put, $"{Root}/{Invoice:D}/enabled", new { enabled = false }),
        };
        foreach (var (method, route, body) in routes)
        {
            var (response, problem) = await Send(method, route, body);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {route}");
            Reason(problem).Should().Be("sdap.access.deny.caller_unresolved", $"{method} {route}");
        }

        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAToken_EveryRouteIs401_AndDataverseIsNeverAsked()
    {
        var routes = new (HttpMethod Method, string Route, object? Body)[]
        {
            (HttpMethod.Get, Root, null),
            (HttpMethod.Post, Root, new { name = "Settlement" }),
            (HttpMethod.Patch, $"{Root}/{Invoice:D}", new { guidance = "x" }),
            (HttpMethod.Put, $"{Root}/{Invoice:D}/enabled", new { enabled = false }),
        };
        foreach (var (method, route, body) in routes)
        {
            var (response, _) = await Send(method, route, body, authenticated: false);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {route}");
        }

        _f.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownCategory_IsTheUniform404_OnEveryRowRoute()
    {
        var (edit, editProblem) = await Send(HttpMethod.Patch, $"{Root}/{Missing:D}", new { guidance = "x" });
        var (flag, _) = await Send(HttpMethod.Put, $"{Root}/{Missing:D}/enabled", new { enabled = true });

        edit.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Reason(editProblem).Should().Be("sdap.access.deny.record_unavailable");
        flag.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _f.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ADataverseFault_OnAWrite_Is503_NotA500()
    {
        _f.Faults.Add("write");

        var (response, body) = await Send(HttpMethod.Patch, $"{Root}/{Invoice:D}", new { guidance = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body.ToString());
        Reason(body).Should().Be("ontology.admin.classification.unavailable");
    }
}

/// <summary>The BFF with only the caller-identity client and the inbound token substituted.</summary>
public sealed class ClassificationTestFixture : WorkspaceTestFixture
{
    public enum Who { Admin, ConsoleUser }

    public sealed class Category
    {
        public required Guid Id { get; init; }
        public required string Name { get; set; }
        public string? Guidance { get; set; }
        public bool Enabled { get; set; } = true;
        public long Version { get; set; } = 1000;
        public decimal? Recall { get; init; }
        public int? SetSize { get; init; }
        public string? MeasuredOn { get; init; }
    }

    public sealed record PolicyRow(string Code, string? Name, bool Enabled, Guid? CurrentVersion, int? Domain = null);

    public sealed record VersionRow(Guid Policy, int Number, string Body, string? InforceTo);

    public sealed record WriteRecord(string Method, string Path, string Body, Who Caller, long? ExpectedVersion);

    public Who Caller { get; set; } = Who.Admin;

    public Dictionary<Guid, Category> Categories { get; } = new();

    public Dictionary<Guid, PolicyRow> Policies { get; } = new();

    public Dictionary<Guid, VersionRow> Versions { get; } = new();

    /// <summary>"categories", "policies", "versions", "volume" (reads) or "write": the call answers a Dataverse 500.</summary>
    public HashSet<string> Faults { get; } = new();

    /// <summary>Every call answers this (error code, HTTP status): the caller cannot be named to Dataverse.</summary>
    public (string Error, int Status)? EveryCallFails { get; set; }

    /// <summary>Runs after the BFF's read and before its write: a concurrent writer.</summary>
    public Action? BeforeWrite { get; set; }

    public ConcurrentQueue<string> Requests { get; } = new();

    private readonly ConcurrentQueue<WriteRecord> _writes = new();

    public IReadOnlyCollection<WriteRecord> Writes => _writes;

    public void Reset()
    {
        Caller = Who.Admin;
        Faults.Clear();
        EveryCallFails = null;
        BeforeWrite = null;
        Requests.Clear();
        _writes.Clear();
        Categories.Clear();
        Policies.Clear();
        Versions.Clear();

        var fee = Guid.Parse("8b62dd84-1fbc-f111-aaaf-3833c5e9614d");
        var scope = Guid.Parse("8d62dd84-1fbc-f111-aaaf-3833c5e9614d");
        var invoice = Guid.Parse("71310056-598b-f111-8077-7ced8ddc4cc6");
        var marketing = Guid.Parse("8b310056-598b-f111-8077-7ced8ddc4cc6");
        Categories[fee] = new Category { Id = fee, Name = "Fee / rate change", Guidance = "A change to rates or fees.", Recall = 88.89m, SetSize = 92, MeasuredOn = "2026-10-10" };
        Categories[scope] = new Category { Id = scope, Name = "Scope / budget change", Guidance = "A change to scope.", Recall = 89.29m, SetSize = 92, MeasuredOn = "2026-10-10" };
        Categories[invoice] = new Category { Id = invoice, Name = "Invoice / Billing", Guidance = "Invoices." };
        Categories[marketing] = new Category { Id = marketing, Name = "Marketing / Noise", Guidance = "Newsletters." };

        // POL-COMMIT-BUDGET as it stands on spaarkedev1: v1 superseded, v2 in force; both name Fee and Scope by id.
        var policy = Guid.Parse("4d204810-61bf-f111-aaaf-0022482913fc");
        var v1 = Guid.Parse("42b3e716-61bf-f111-aaaf-0022482913fc");
        var v2 = Guid.Parse("e51906ea-62c2-f111-a05a-7c1e520a989f");
        var body = OntologyAdminClassificationEndpointTests.Named($"[\"{fee}\",\"{scope}\"]");
        Policies[policy] = new("POL-COMMIT-BUDGET", "Budget variance", Enabled: true, CurrentVersion: v2);
        Versions[v1] = new(policy, 1, body, InforceTo: "2026-10-07T12:00:00Z");
        Versions[v2] = new(policy, 2, body, InforceTo: null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataverseUserClient>();
            services.AddScoped<IDataverseUserClient>(_ => new World(this));

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, CallerTokenHandler>(CallerTokenHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = CallerTokenHandler.SchemeName;
                o.DefaultChallengeScheme = CallerTokenHandler.SchemeName;
            });
        });
    }

    private sealed class CallerTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "OntologyClassificationCaller";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new("oid", header["Bearer ".Length..]), new("tid", WorkspaceTestConstants.TestTenantId) };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    /// <summary>A Dataverse that holds the tables the Classification routes touch and enforces the write privilege per caller.</summary>
    private sealed class World(ClassificationTestFixture f) : IDataverseUserClient
    {
        private static DataverseUserResponse Fail(int status, string code) => DataverseUserResponse.Fail(status, code, code);

        private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

        private DataverseUserResponse? Common(string what)
        {
            f.Requests.Enqueue(what);
            if (f.EveryCallFails is { } all)
            {
                return Fail(all.Status, all.Error);
            }

            return null;
        }

        private static object Row(Category c) => new Dictionary<string, object?>
        {
            ["sprk_triagecategoryid"] = c.Id.ToString("D"),
            ["sprk_name"] = c.Name,
            ["sprk_classifierguidance"] = c.Guidance,
            ["sprk_enabled"] = c.Enabled,
            ["versionnumber"] = c.Version,
            ["statecode"] = 0,
            ["sprk_measuredrecall"] = c.Recall,
            ["sprk_labelledsetsize"] = c.SetSize,
            ["sprk_recallmeasuredon"] = c.MeasuredOn,
        };

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            if (Common("GET " + relativePath) is { } failed)
            {
                return Task.FromResult(failed);
            }

            if (relativePath.StartsWith("sprk_triagecategories?", StringComparison.Ordinal))
            {
                return Task.FromResult(f.Faults.Contains("categories")
                    ? Fail(500, DataverseUserClientErrorCodes.ServiceError)
                    : DataverseUserResponse.Ok(200, Json(new { value = f.Categories.Values.OrderBy(c => c.Name, StringComparer.Ordinal).Select(Row) })));
            }

            if (relativePath.StartsWith("sprk_triagecategories(", StringComparison.Ordinal))
            {
                var id = Guid.Parse(relativePath["sprk_triagecategories(".Length..relativePath.IndexOf(')')]);
                return Task.FromResult(f.Categories.TryGetValue(id, out var category)
                    ? DataverseUserResponse.Ok(200, Json(Row(category)))
                    : Fail(404, DataverseUserClientErrorCodes.NotFound));
            }

            if (relativePath.StartsWith("sprk_policies?", StringComparison.Ordinal))
            {
                return Task.FromResult(f.Faults.Contains("policies")
                    ? Fail(500, DataverseUserClientErrorCodes.ServiceError)
                    : DataverseUserResponse.Ok(200, Json(new
                    {
                        value = f.Policies.Select(p => new Dictionary<string, object?>
                        {
                            ["sprk_policyid"] = p.Key.ToString("D"),
                            ["sprk_policycode"] = p.Value.Code,
                            ["sprk_name"] = p.Value.Name,
                            ["sprk_enabled"] = p.Value.Enabled,
                            ["_sprk_currentversion_value"] = p.Value.CurrentVersion?.ToString("D"),
                            ["sprk_domain"] = p.Value.Domain,
                        }),
                    })));
            }

            if (relativePath.StartsWith("sprk_policyversions?", StringComparison.Ordinal))
            {
                return Task.FromResult(f.Faults.Contains("versions")
                    ? Fail(500, DataverseUserClientErrorCodes.ServiceError)
                    : DataverseUserResponse.Ok(200, Json(new
                    {
                        value = f.Versions.Select(v => new Dictionary<string, object?>
                        {
                            ["sprk_policyversionid"] = v.Key.ToString("D"),
                            ["sprk_versionnumber"] = v.Value.Number,
                            ["sprk_rulebody"] = v.Value.Body,
                            ["sprk_inforceto"] = v.Value.InforceTo,
                            ["_sprk_policy_value"] = v.Value.Policy.ToString("D"),
                        }),
                    })));
            }

            if (relativePath.StartsWith("sprk_communications?fetchXml=", StringComparison.Ordinal))
            {
                var fetch = Uri.UnescapeDataString(relativePath["sprk_communications?fetchXml=".Length..]);
                fetch.Should().Contain("aggregate='true'").And.Contain("last-x-days").And.Contain("value='30'");
                if (f.Faults.Contains("volume"))
                {
                    return Task.FromResult(Fail(500, DataverseUserClientErrorCodes.ServiceError));
                }

                // The shape measured live on spaarkedev1: a row without 'cat' for unclassified communications, then one per category.
                return Task.FromResult(DataverseUserResponse.Ok(200, Json(new
                {
                    value = new object[]
                    {
                        new { n = 2 },
                        new { n = 1, cat = "8b62dd84-1fbc-f111-aaaf-3833c5e9614d" },
                        new { n = 6, cat = "8d62dd84-1fbc-f111-aaaf-3833c5e9614d" },
                    },
                })));
            }

            throw new InvalidOperationException($"The Classification routes asked something unexpected: {relativePath}");
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            PostAsync(absoluteApiPath, jsonBody, preferRepresentation: false, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
        {
            if (Common("POST " + absoluteApiPath) is { } failed)
            {
                return Task.FromResult(failed);
            }

            absoluteApiPath.Should().Be("/api/data/v9.2/sprk_triagecategories");
            preferRepresentation.Should().BeTrue("the route needs the created row's id");
            if (f.Caller != Who.Admin)
            {
                return Task.FromResult(Fail(403, DataverseUserClientErrorCodes.AccessDenied));
            }

            if (f.Faults.Contains("write"))
            {
                return Task.FromResult(Fail(500, DataverseUserClientErrorCodes.ServiceError));
            }

            using var doc = JsonDocument.Parse(jsonBody);
            var created = new Category
            {
                Id = Guid.NewGuid(),
                Name = doc.RootElement.GetProperty("sprk_name").GetString()!,
                Guidance = doc.RootElement.TryGetProperty("sprk_classifierguidance", out var g) ? g.GetString() : null,
                Enabled = doc.RootElement.GetProperty("sprk_enabled").GetBoolean(),
            };
            f.Categories[created.Id] = created;
            f._writes.Enqueue(new WriteRecord("POST", absoluteApiPath, jsonBody, f.Caller, null));
            return Task.FromResult(DataverseUserResponse.Ok(201, Json(Row(created))));
        }

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A Classification write must be version-conditional (If-Match on the row version), never If-Match: *.");

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken)
        {
            if (Common("PATCH " + relativePath) is { } failed)
            {
                return Task.FromResult(failed);
            }

            f.BeforeWrite?.Invoke();
            var id = Guid.Parse(relativePath["sprk_triagecategories(".Length..relativePath.IndexOf(')')]);
            if (f.Caller != Who.Admin)
            {
                return Task.FromResult(Fail(403, DataverseUserClientErrorCodes.AccessDenied));
            }

            if (f.Faults.Contains("write"))
            {
                return Task.FromResult(Fail(500, DataverseUserClientErrorCodes.ServiceError));
            }

            if (!f.Categories.TryGetValue(id, out var category))
            {
                return Task.FromResult(Fail(404, DataverseUserClientErrorCodes.NotFound));
            }

            if (category.Version != expectedVersion)
            {
                return Task.FromResult(Fail(412, DataverseUserClientErrorCodes.BadRequest));
            }

            using var doc = JsonDocument.Parse(jsonBody);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "sprk_name": category.Name = property.Value.GetString()!; break;
                    case "sprk_classifierguidance": category.Guidance = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString(); break;
                    case "sprk_enabled": category.Enabled = property.Value.GetBoolean(); break;
                    default: throw new InvalidOperationException($"The route wrote a column it should not: {property.Name}");
                }
            }

            category.Version += 1;
            f._writes.Enqueue(new WriteRecord("PATCH", relativePath, jsonBody, f.Caller, expectedVersion));
            return Task.FromResult(DataverseUserResponse.Ok(204, null));
        }

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The Classification area never deletes.");
    }
}
