using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// <c>POST /api/v1/field-mappings/push</c> through the REAL <c>MapFieldMappingEndpoints</c>
/// (unified-access-control-r2 task 166, sweep finding S-67).
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> The route read the source's mapped fields app-only, queried up to 500 children
/// app-only, and PATCHed every one app-only — for any source id any signed-in caller named. It was latent only because
/// the child query wrapped <c>_…_value</c> twice and Dataverse answered 400; fixing that bug alone would have turned it
/// into a mass write over children of records the caller cannot see. The gate and the fix land together.</para>
/// <para><b>What is real and what is substituted.</b> The mapper, the handler, the mapping engine and the problem
/// shapes are production code. Substituted at module boundaries only: <see cref="CallerRecordAccessProbe"/> (its
/// virtual rights + WhoAmI seams), <see cref="IFieldMappingDataverseService"/>, <see cref="IImpersonatedCommunicationQuery"/>
/// (records the entity set, query string and impersonated user) and <see cref="IGenericEntityService"/> (the entity-set
/// lookup). No HTTP handler is mocked (ADR-038).</para>
/// </remarks>
public class FieldMappingPushAuthorizationContractTests
{
    private static readonly Guid MatterId = Guid.Parse("16616616-1111-4000-8000-000000000166");
    private static readonly Guid CallerSystemUserId = Guid.Parse("16616616-2222-4000-8000-000000000166");
    private static readonly Guid ChildA = Guid.Parse("16616616-3333-4000-8000-000000000166");
    private static readonly Guid ChildB = Guid.Parse("16616616-4444-4000-8000-000000000166");

    private static object PushBody(string sourceEntity = "sprk_matter", Guid? sourceId = null, string targetEntity = "sprk_event") =>
        new { sourceEntity, sourceRecordId = sourceId ?? MatterId, targetEntity };

    // =========================================================================================
    // The child query string — the double `_…_value` wrap must not come back
    // =========================================================================================

    [Fact]
    public void BuildChildRecordQuery_WrapsTheLookupExactlyOnce()
    {
        var query = FieldMappingEndpoints.BuildChildRecordQuery("sprk_regardingmatter", MatterId, "sprk_event", 501);

        query.Should().Be($"$filter=_sprk_regardingmatter_value eq {MatterId:D}&$select=sprk_eventid&$top=501");
        query.Should().NotContain("__sprk_", "the old path produced __sprk_regardingmatter_value_value and always 400ed");
    }

    // =========================================================================================
    // The source gate — one 404, and nothing read or written before it
    // =========================================================================================

    public static TheoryData<string> RefusedSourceShapes => new()
    {
        "no-read", "rights-without-read", "probe-throws", "no-token", "caller-unresolvable", "unmapped-source-type",
    };

    [Theory]
    [MemberData(nameof(RefusedSourceShapes))]
    public async Task Push_WhenTheSourceCannotBeAuthorizedAsTheCaller_IsTheUniform404_AndNothingIsReadOrWritten(string shape)
    {
        await using var host = await PushHost.StartAsync();
        host.ArrangeHappyPathData();
        switch (shape)
        {
            case "no-read": break; // nothing granted
            case "rights-without-read": host.Probe.Rights = AccessRights.Write | AccessRights.AppendTo; break;
            case "probe-throws": host.Probe.Rights = AccessRights.Read; host.Probe.Fault = new HttpRequestException("RetrievePrincipalAccess unavailable"); break;
            case "no-token": host.Probe.Rights = AccessRights.Read; break;
            case "caller-unresolvable": host.Probe.Rights = AccessRights.Read; host.Probe.SystemUserId = null; break;
            case "unmapped-source-type": host.Probe.Rights = AccessRights.Read; break;
        }

        var request = Authenticated(shape == "unmapped-source-type" ? PushBody("sprk_unmappedthing") : PushBody());
        if (shape == "no-token")
        {
            request.Headers.Authorization = null;
        }

        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        Normalize(body).Should().Be(Normalize(await UnknownSourceBodyAsync()),
            "an unreadable, unmapped or unidentifiable source answers exactly as a source that does not exist");
        body.Should().NotContain(MatterId.ToString());
        host.VerifyNothingReadOrWritten();
    }

    private static async Task<string> UnknownSourceBodyAsync()
    {
        await using var host = await PushHost.StartAsync();
        var response = await host.SendAsync(Authenticated(PushBody(sourceId: Guid.NewGuid())));
        return await response.Content.ReadAsStringAsync();
    }

    // =========================================================================================
    // The authorized push — children read and written AS THE CALLER
    // =========================================================================================

    [Fact]
    public async Task Push_ForAReadableSource_QueriesAndWritesTheChildrenImpersonatingTheCaller()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData();

        var response = await host.SendAsync(Authenticated(PushBody()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["updatedCount"]!.GetValue<int>().Should().Be(2);

        host.ProbedSources.Should().Equal(new[] { ("sprk_matters", MatterId) }, "the SOURCE is asked as the caller, by its entity set");
        host.ChildQueries.Should().ContainSingle().Which.Should().Be(
            ("sprk_events", $"$filter=_sprk_regardingmatter_value eq {MatterId:D}&$select=sprk_eventid&$top=501", CallerSystemUserId));
        host.FieldMappings.Verify(f => f.UpdateRecordFieldsAsync(
            "sprk_event", ChildA, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), CallerSystemUserId), Times.Once);
        host.FieldMappings.Verify(f => f.UpdateRecordFieldsAsync(
            "sprk_event", ChildB, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), CallerSystemUserId), Times.Once);
        host.FieldMappings.Verify(f => f.UpdateRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null),
            Times.Never, "no app-only child write remains on this route");
        host.FieldMappings.Verify(f => f.QueryChildRecordIdsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "the app-only child query (and its double-prefix bug) is no longer on this path");
    }

    [Fact]
    public async Task Push_ChildrenTheCallerCannotSee_NeverEnterTheResult()
    {
        // Dataverse, impersonating the caller, returns only the children they may read. The route must report on
        // exactly that set — no count, error or field result for a hidden child.
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(visibleChildren: new[] { ChildA });

        var response = await host.SendAsync(Authenticated(PushBody()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        JsonNode.Parse(text)!["totalRecords"]!.GetValue<int>().Should().Be(1);
        text.Should().NotContain(ChildB.ToString());
        host.FieldMappings.Verify(f => f.UpdateRecordFieldsAsync(
            It.IsAny<string>(), ChildB, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Never);
    }

    // =========================================================================================
    // Task 166 r2: the SOURCE's mapped fields are read AS THE CALLER (field-level security applies)
    // =========================================================================================

    [Fact]
    public void BuildSourceRecordQuery_SelectsTheMappedFieldsOfExactlyTheAuthorizedRow()
    {
        var query = FieldMappingEndpoints.BuildSourceRecordQuery(
            "sprk_matter", MatterId, ["sprk_clientreference", "bad field')", "sprk_name"]);

        query.Should().Be(
            $"$select=sprk_clientreference,sprk_name,sprk_matterid&$filter=sprk_matterid eq {MatterId:D}&$top=1",
            "a rule field that is not a logical name is never interpolated into the caller's query");
    }

    [Fact]
    public async Task Push_ReadsTheSourceFieldsAsTheCaller_NeverAppOnly()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData();

        var response = await host.SendAsync(Authenticated(PushBody()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.SourceReads.Should().Equal(new[]
        {
            ("sprk_matters", (string?)$"$select=sprk_clientreference,sprk_matterid&$filter=sprk_matterid eq {MatterId:D}&$top=1",
             CallerSystemUserId),
        }, "the source row is read through the impersonated seam, as the caller");
        host.FieldMappings.Verify(f => f.RetrieveRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "no app-only source read remains on this route");
    }

    // A column under field-level security that the caller cannot read comes back NULL from an impersonated read. The
    // push must not copy it into children the caller CAN read (the app-only read used to return its real value).
    [Fact]
    public async Task Push_ASourceColumnTheCallerCannotReadUnderFieldSecurity_IsNeverCopiedIntoAChild()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(sourceValue: null);

        var response = await host.SendAsync(Authenticated(PushBody()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.FieldMappings.Verify(f => f.UpdateRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()),
            Times.Never, "a value the caller cannot read is never written anywhere");
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["skippedCount"]!.GetValue<int>().Should().Be(2);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("REF-166");
    }

    [Fact]
    public async Task Push_WhenTheCallersSourceReadReturnsNoRow_IsTheUniform404_AndNoChildIsReadOrWritten()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(sourceRowVisible: false);

        var response = await host.SendAsync(Authenticated(PushBody()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Normalize(await response.Content.ReadAsStringAsync()).Should().Be(Normalize(await UnknownSourceBodyAsync()));
        host.ChildQueries.Should().BeEmpty();
        host.FieldMappings.Verify(f => f.UpdateRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    // =========================================================================================
    // Task 166 r1 (owner round 21 item 3): the parent lookup comes from RELATIONSHIP METADATA
    // =========================================================================================

    [Fact]
    public async Task Push_ToATargetWhoseLookupIsNotTheRegardingConvention_FindsItFromMetadata_MatterToInvoice()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(targetEntity: "sprk_invoice", targetSet: "sprk_invoices", lookupsToSource: ["sprk_matter"]);

        var response = await host.SendAsync(Authenticated(PushBody(targetEntity: "sprk_invoice")));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the live 'Matter to Invoice (Attorney Matrix)' profile works: sprk_invoice names its matter in sprk_matter");
        host.MetadataQueries.Should().Equal(new[]
        {
            ("EntityDefinitions(LogicalName='sprk_invoice')/ManyToOneRelationships",
             (string?)"$select=ReferencingAttribute,ReferencedEntity", CallerSystemUserId),
        }, "the metadata is read through the same impersonated seam, as the caller");
        host.ChildQueries.Should().ContainSingle().Which.Should().Be(
            ("sprk_invoices", $"$filter=_sprk_matter_value eq {MatterId:D}&$select=sprk_invoiceid&$top=501", CallerSystemUserId));
    }

    [Fact]
    public async Task Push_ToTheConventionalTarget_ResolvesTheSameLookupFromMetadata()
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(lookupsToSource: ["sprk_regardingmatter"]);

        var response = await host.SendAsync(Authenticated(PushBody()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.ChildQueries.Should().ContainSingle().Which.Query.Should().StartWith("$filter=_sprk_regardingmatter_value eq ");
    }

    [Theory]
    [InlineData(0, FieldMappingEndpoints.ParentLookupMissingReasonCode)]
    [InlineData(2, FieldMappingEndpoints.ParentLookupAmbiguousReasonCode)]
    public async Task Push_WhenMetadataNamesNoneOrSeveralLookupsToTheSource_Is409_AndReadsAndWritesNoChild(
        int lookups, string reasonCode)
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData(lookupsToSource: lookups == 0 ? [] : ["sprk_regardingmatter", "sprk_secondmatter"]);

        var response = await host.SendAsync(Authenticated(PushBody()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be(reasonCode);
        host.ChildQueries.Should().BeEmpty("the route never guesses which lookup names the parent");
        host.FieldMappings.Verify(f => f.UpdateRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Theory]
    [InlineData("sprk_event')/x?$filter=('")]
    [InlineData("sprk event")]
    public async Task Push_WithANonLogicalNameEntity_Is400_AndNothingIsAsked(string targetEntity)
    {
        await using var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read;
        host.ArrangeHappyPathData();

        var response = await host.SendAsync(Authenticated(PushBody(targetEntity: targetEntity)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an entity name is interpolated into OData and metadata paths");
        host.ProbedSources.Should().BeEmpty();
        host.VerifyNothingReadOrWritten();
        host.MetadataQueries.Should().BeEmpty();
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private static HttpRequestMessage Authenticated(object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/field-mappings/push")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PushAuthzTestAuthHandler.CallerHeader, "present");
        return request;
    }

    private static string Normalize(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("traceId");
        node.Remove("correlationId");
        return node.ToJsonString();
    }

    /// <summary><see cref="CallerRecordAccessProbe"/> at its two virtual seams. Like the real probe, no caller token
    /// answers <see cref="AccessRights.None"/> and no systemuserid.</summary>
    internal sealed class SeamProbe : CallerRecordAccessProbe
    {
        private readonly List<(string, Guid)> _probed;

        public SeamProbe(List<(string, Guid)> probed)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
            => _probed = probed;

        public AccessRights Rights { get; set; } = AccessRights.None;

        public Guid? SystemUserId { get; set; } = CallerSystemUserId;

        public Exception? Fault { get; set; }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            _probed.Add((entitySet, recordId));
            if (Fault is not null)
            {
                return Task.FromException<AccessRights>(Fault);
            }

            return Task.FromResult(!string.IsNullOrEmpty(callerBearerToken) && recordId == MatterId ? Rights : AccessRights.None);
        }

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
            => Task.FromResult(string.IsNullOrEmpty(callerBearerToken) ? null : SystemUserId);
    }

    /// <summary>A minimal host over the REAL field-mapping mapper.</summary>
    internal sealed class PushHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public List<(string Set, Guid Id)> ProbedSources { get; } = new();

        public List<(string EntitySet, string? Query, Guid Caller)> ChildQueries { get; } = new();

        /// <summary>Every SOURCE-row read (task 166 r2), with the caller it impersonated.</summary>
        public List<(string EntitySet, string? Query, Guid Caller)> SourceReads { get; } = new();

        /// <summary>Every relationship-metadata read (task 166 r1), with the caller it impersonated.</summary>
        public List<(string Path, string? Query, Guid Caller)> MetadataQueries { get; } = new();

        public SeamProbe Probe { get; }

        public Mock<IFieldMappingDataverseService> FieldMappings { get; } = new(MockBehavior.Loose);

        public Mock<IImpersonatedCommunicationQuery> Impersonated { get; } = new(MockBehavior.Strict);

        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Loose);

        private PushHost() => Probe = new SeamProbe(ProbedSources);

        public static async Task<PushHost> StartAsync()
        {
            var host = new PushHost();
            await host.InitializeAsync();
            return host;
        }

        /// <summary>A one-rule matter→target profile, a source row, the target's lookups to sprk_matter in relationship
        /// metadata (task 166 r1; default: the one sprk_regardingmatter), and the children the CALLER can see.</summary>
        public void ArrangeHappyPathData(
            Guid[]? visibleChildren = null,
            string targetEntity = "sprk_event",
            string targetSet = "sprk_events",
            string[]? lookupsToSource = null,
            string? sourceValue = "REF-166",
            bool sourceRowVisible = true)
        {
            FieldMappings
                .Setup(f => f.GetFieldMappingProfileWithRulesAsync("sprk_matter", targetEntity, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FieldMappingProfileEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "Matter to " + targetEntity,
                    SourceEntity = "sprk_matter",
                    TargetEntity = targetEntity,
                    IsActive = true,
                    Rules =
                    [
                        new FieldMappingRuleEntity
                        {
                            Id = Guid.NewGuid(), Name = "copy-ref", SourceField = "sprk_clientreference", SourceFieldType = 0,
                            TargetField = "sprk_clientreference", TargetFieldType = 0, MappingType = 0, ExecutionOrder = 1, IsActive = true,
                        },
                    ],
                });
            Entities.Setup(e => e.GetEntitySetNameAsync(targetEntity, It.IsAny<CancellationToken>())).ReturnsAsync(targetSet);
            Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");

            // The SOURCE row, as the caller reads it (task 166 r2): a field-secured column the caller cannot read comes
            // back null; a source the caller cannot read comes back as no row.
            Impersonated
                .Setup(q => q.QueryAsync("sprk_matters", It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string set, string? query, Guid caller, CancellationToken _) =>
                {
                    SourceReads.Add((set, query, caller));
                    return sourceRowVisible
                        ? new List<Dictionary<string, JsonElement>>
                        {
                            new()
                            {
                                ["sprk_matterid"] = JsonSerializer.SerializeToElement(MatterId.ToString()),
                                ["sprk_clientreference"] = JsonSerializer.SerializeToElement(sourceValue),
                            },
                        }
                        : new List<Dictionary<string, JsonElement>>();
                });

            var children = visibleChildren ?? new[] { ChildA, ChildB };
            Impersonated
                .Setup(q => q.QueryAsync(
                    It.Is<string>(set => !set.StartsWith("EntityDefinitions", StringComparison.Ordinal) && set != "sprk_matters"),
                    It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string set, string? query, Guid caller, CancellationToken _) =>
                {
                    ChildQueries.Add((set, query, caller));
                    return children
                        .Select(id => new Dictionary<string, JsonElement> { [$"{targetEntity}id"] = JsonSerializer.SerializeToElement(id.ToString()) })
                        .ToList();
                });

            // Relationship metadata: the target's lookups to sprk_matter, plus an unrelated lookup that must be ignored.
            var relationships = (lookupsToSource ?? ["sprk_regardingmatter"])
                .Select(attribute => Relationship(attribute, "sprk_matter"))
                .Append(Relationship("sprk_assignedto", "contact"))
                .ToList();
            Impersonated
                .Setup(q => q.QueryAsync(
                    It.Is<string>(set => set.StartsWith("EntityDefinitions", StringComparison.Ordinal)),
                    It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, string? query, Guid caller, CancellationToken _) =>
                {
                    MetadataQueries.Add((path, query, caller));
                    return relationships;
                });
        }

        private static Dictionary<string, JsonElement> Relationship(string referencingAttribute, string referencedEntity) => new()
        {
            ["ReferencingAttribute"] = JsonSerializer.SerializeToElement(referencingAttribute),
            ["ReferencedEntity"] = JsonSerializer.SerializeToElement(referencedEntity),
        };

        public void VerifyNothingReadOrWritten()
        {
            FieldMappings.Verify(f => f.GetFieldMappingProfileWithRulesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            FieldMappings.Verify(f => f.RetrieveRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
            FieldMappings.Verify(f => f.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Never);
            ChildQueries.Should().BeEmpty("no child query — as the caller or app-only — before the source is authorized");
            SourceReads.Should().BeEmpty("no source read — as the caller or app-only — before the source is authorized");
        }

        private async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = PushAuthzTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = PushAuthzTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, PushAuthzTestAuthHandler>(PushAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));
            builder.Services.AddSingleton<CallerRecordAccessProbe>(Probe);
            builder.Services.AddSingleton(FieldMappings.Object);
            builder.Services.AddSingleton(Impersonated.Object);
            builder.Services.AddSingleton(Entities.Object);
            // Batch-4 integration (task 156): the push re-stamps each written child's descendants after its own write —
            // a module boundary here (no child in these tests has descendants); the restamp is not this file's subject.
            builder.Services.AddSingleton(
                new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper);
            // Batch-4 integration (task 158): a push that files a work assignment or project under a secure record secures
            // it; no record in these tests is secure (a module boundary here).
            builder.Services.AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure());

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapFieldMappingEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client!.SendAsync(request);

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}

/// <summary>Authenticates a request carrying <see cref="CallerHeader"/> as a caller with an Entra oid.</summary>
public sealed class PushAuthzTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PushAuthzTest";
    public const string CallerHeader = "X-Test-Caller";

    public PushAuthzTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(CallerHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(new[] { new Claim("oid", "6f0c1a52-0000-4000-8000-0000000f0166") }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
