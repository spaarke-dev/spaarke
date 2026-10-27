// -----------------------------------------------------------------------------
// DataverseWebApiRecordTypeSeederTests.cs
//
// T186 L54: a new stamp's sprk_recordtype_ref table is empty, so H12b's
// field-mapping seeder failed on its first profile. DataverseWebApiRecordTypeSeeder
// creates the platform rows first. ADR-038: real HttpClient over a hand-rolled
// fake handler (no Mock<HttpMessageHandler>), parity with the sibling seeder tests.
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

public sealed class DataverseWebApiRecordTypeSeederTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";

    [Fact]
    public async Task T186_L54_EmptyTable_CreatesEveryPlatformRecordType_WithItsColumns()
    {
        var fake = new FakeDataverseHandler(existing: new HashSet<string>());
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(new AppConfigSeedInput("acme", TenantId, EnvUrl), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok, result.Diagnostic);
        fake.PostBodies.Should().HaveCount(DataverseWebApiRecordTypeSeeder.SeedRecordTypes.Count);
        fake.PostUris.Should().OnlyContain(u => u.AbsolutePath == "/api/data/v9.2/sprk_recordtype_refs");

        var matter = fake.PostBodies.Select(b => JsonDocument.Parse(b).RootElement)
            .Single(e => e.GetProperty("sprk_recordlogicalname").GetString() == "sprk_matter");
        matter.GetProperty("sprk_recordtypename").GetString().Should().Be("Matter", "the table's primary (required) name column");
        matter.GetProperty("sprk_recordtypecode").GetString().Should().Be("MTR");
        matter.GetProperty("sprk_regardingfield").GetString().Should().Be("sprk_regardingmatter");
        matter.GetProperty("sprk_regardingrecordnumberfield").GetString().Should().Be("sprk_matternumber");

        var todo = fake.PostBodies.Select(b => JsonDocument.Parse(b).RootElement)
            .Single(e => e.GetProperty("sprk_recordlogicalname").GetString() == "sprk_todo");
        todo.TryGetProperty("sprk_regardingrecordnumberfield", out _).Should().BeFalse("To Do has no record-number field");
    }

    [Fact]
    public async Task T186_L54_RowsAlreadyPresent_AreSkipped_NeverRewritten()
    {
        var all = DataverseWebApiRecordTypeSeeder.SeedRecordTypes.Select(r => r.LogicalName).ToHashSet();
        var fake = new FakeDataverseHandler(existing: all);
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(new AppConfigSeedInput("acme", TenantId, EnvUrl), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok);
        fake.PostBodies.Should().BeEmpty();
        fake.Methods.Should().OnlyContain(m => m == HttpMethod.Get, "an existing row is never PATCHed");
    }

    [Fact]
    public async Task T186_L54_PostRefused_FailsWithTheStatusAndBody()
    {
        var fake = new FakeDataverseHandler(existing: new HashSet<string>()) { PostStatus = HttpStatusCode.BadRequest };
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(new AppConfigSeedInput("acme", TenantId, EnvUrl), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Failed);
        result.Diagnostic.Should().Contain("400").And.Contain("POST sprk_recordtype_refs").And.Contain("full retry is safe");
        fake.PostBodies.Should().HaveCount(1, "the seeder stops at the first refused row");
    }

    [Fact]
    public void T186_L54_EveryEntityTheFieldMappingSeederBinds_IsARecordTypeThisSeederCreates()
    {
        var seeded = DataverseWebApiRecordTypeSeeder.SeedRecordTypes.Select(r => r.LogicalName).ToHashSet();
        var bound = DataverseWebApiFieldMappingSeeder.SeedProfiles.Select(p => p.TargetEntityLogicalName).Append("sprk_matter");

        bound.Should().OnlyContain(n => seeded.Contains(n));

        // Must-fire control: an entity outside the seed set is caught by the same check.
        seeded.Contains("sprk_notaseededtype").Should().BeFalse();
        DataverseWebApiRecordTypeSeeder.SeedRecordTypes.Select(r => r.LogicalName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void T186_L54_RecordTypeSeederIsRegisteredBeforeFieldMapping()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddH12bAppConfigSeedHandler(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var order = scope.ServiceProvider.GetServices<IAppConfigSeeder>().Select(s => s.ScopeName).ToList();

        order.Should().Contain(AppConfigSeedScopes.RecordType);
        order.IndexOf(AppConfigSeedScopes.RecordType).Should().BeLessThan(order.IndexOf(AppConfigSeedScopes.FieldMapping));
    }

    private static DataverseWebApiRecordTypeSeeder BuildSeeder(FakeDataverseHandler fake)
        => new(
            new HttpClient(fake),
            Options.Create(new AppConfigSeedOptions()),
            NullLogger<DataverseWebApiRecordTypeSeeder>.Instance,
            _ => new StaticTokenCredential());

    private sealed class StaticTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class FakeDataverseHandler(ISet<string> existing) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = new();
        public List<Uri> PostUris { get; } = new();
        public List<string> PostBodies { get; } = new();
        public HttpStatusCode PostStatus { get; init; } = HttpStatusCode.Created;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            if (request.Method == HttpMethod.Get)
            {
                var filter = Uri.UnescapeDataString(request.RequestUri!.Query);
                var hit = existing.FirstOrDefault(n => filter.Contains($"'{n}'", StringComparison.Ordinal));
                var json = hit is null
                    ? "{\"value\":[]}"
                    : "{\"value\":[{\"sprk_recordtype_refid\":\"60000000-0000-0000-0000-000000000001\"}]}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Post)
            {
                PostUris.Add(request.RequestUri!);
                PostBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                var body = PostStatus == HttpStatusCode.Created
                    ? "{\"sprk_recordtype_refid\":\"70000000-0000-0000-0000-000000000001\"}"
                    : "{\"error\":{\"message\":\"refused\"}}";
                return new HttpResponseMessage(PostStatus) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
            throw new InvalidOperationException($"unexpected request: {request.Method} {request.RequestUri}");
        }
    }
}
