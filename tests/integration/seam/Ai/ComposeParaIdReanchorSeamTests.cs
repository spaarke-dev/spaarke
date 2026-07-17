// spaarkeai-compose-r3 task 012 (FR-11) — paraId-primary re-anchoring vertical-slice seam test (KEEP
// path: tests/integration/seam/** — end-to-end across the E2 paraId substrate with production types;
// a service-unit ≠ working slice, ADR-038 §E-40 / tests/CLAUDE.md / project CLAUDE.md NFR-06).
//
// WHAT THIS PROVES THROUGH THE WIRE (NFR-06 Definition-of-Done for task 012):
//   The reanchor route drives the REAL AnnotationReanchorService over the REAL HTTP pipeline. A prior
//   anchor carrying a w14:paraId re-anchors BY THAT paraId to the exact original paragraph — even when
//   its textPattern would fuzzy-match a DIFFERENT paragraph — proving paraId is the PRIMARY anchor
//   through the wire, not just in a static unit call. The negative-contrast anchor (paraId absent from
//   the document, as after an external Word edit regenerated ids — Open-XML-SDK #925) re-anchors via
//   the RETAINED textPattern+Levenshtein fuzzy matcher, proving the fallback is wired and not deleted
//   (design §5.2). Only the external SPE download boundary (ISpeFileOperations) is doubled per ADR-038;
//   the scoring engine + the ExtractParaIds id-map extraction are REAL.
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; the
// AnnotationReanchorService under test is REAL; the mock lives ONLY at the SPE facade boundary.
// Assertions are HTTP-observable (the banded summary the client renders).

using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// FR-11 paraId-primary re-anchoring seam tests over the REAL app (WebApplicationFactory&lt;Program&gt;)
/// with the REAL <see cref="AnnotationReanchorService"/>; only the external SPE download boundary is
/// doubled. See the file header for the slice this closes.
/// </summary>
public sealed class ComposeParaIdReanchorSeamTests : IClassFixture<ComposeParaIdReanchorSeamFixture>
{
    private readonly ComposeParaIdReanchorSeamFixture _fx;

    public ComposeParaIdReanchorSeamTests(ComposeParaIdReanchorSeamFixture fx) => _fx = fx;

    private const string TenantId = "tenant-paraid-reanchor-001";

    // The reloaded document's paragraphs — each stamped with an explicit w14:paraId below.
    private static readonly string[] Paragraphs =
    {
        "This Master Services Agreement is entered into between the parties in the signature block.",
        "Indemnification is capped at fees paid by Client in the twelve months preceding the claim.",
        "Each Party shall perform its obligations in a professional and workmanlike manner.",
        "Termination requires thirty (30) days written notice to the other Party's contact.",
    };

    private static readonly string[] ParaIds = { "0000000A", "0000000B", "0000000C", "0000000D" };

    [Fact]
    public async Task Reanchor_PriorAnchorWithParaId_ReanchorsByParaIdOverContent_ThroughTheWire()
    {
        const string driveId = "b!paraid-reanchor-001";
        const string documentSpeId = "spe-item-paraid-001";
        _fx.ResetBoundaries();
        _fx.SetupDownload(BuildDocxWithParaIds(Paragraphs, ParaIds));

        using var client = _fx.CreateAuthenticatedClient();

        // Two prior anchors:
        //   • by-paraid: paraId 0000000D (paragraph 3) but textPattern byte-identical to paragraph 0 —
        //     paraId-primary MUST land it at 3, NOT at the content match 0.
        //   • by-fuzzy: paraId DEADBEEF (absent — external Word edit case) + textPattern matching
        //     paragraph 1 — the retained fuzzy matcher MUST re-anchor it at 1.
        var body = new
        {
            driveId,
            tenantId = TenantId,
            priorAnchors = new[]
            {
                new { id = "by-paraid", type = "comment", textPattern = Paragraphs[0], paragraphHint = 0, paraId = "0000000D" },
                new { id = "by-fuzzy", type = "comment", textPattern = Paragraphs[1], paragraphHint = 1, paraId = "DEADBEEF" },
            },
        };

        var response = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/reanchor-annotations", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var wire = await response.Content.ReadFromJsonAsync<ReanchorWire>();
        wire!.Summary.Total.Should().Be(2);

        var byParaId = wire.Summary.Annotations.Single(a => a.Id == "by-paraid");
        byParaId.MatchedParagraphIndex.Should().Be(3,
            "the anchor's paraId (0000000D) identifies paragraph 3 — paraId beats the textPattern content match at 0, through the wire");
        byParaId.Band.Should().Be("auto");
        byParaId.Confidence.Should().Be(1.0, "an exact paraId hit is a definitive re-anchor");

        var byFuzzy = wire.Summary.Annotations.Single(a => a.Id == "by-fuzzy");
        byFuzzy.MatchedParagraphIndex.Should().Be(1,
            "the paraId (DEADBEEF) is absent from the document, so the RETAINED fuzzy matcher re-anchors by content at paragraph 1");
        byFuzzy.Band.Should().Be("auto");
    }

    /// <summary>Builds a valid DOCX whose paragraphs carry the supplied w14:paraIds.</summary>
    private static byte[] BuildDocxWithParaIds(IReadOnlyList<string> paragraphs, IReadOnlyList<string> paraIds)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            for (var i = 0; i < paragraphs.Count; i++)
            {
                var p = new Paragraph(new Run(new Text(paragraphs[i]) { Space = SpaceProcessingModeValues.Preserve }))
                {
                    ParagraphId = new HexBinaryValue(paraIds[i]),
                };
                body.AppendChild(p);
            }
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    // Minimal wire mirrors (deserialization only).
    private sealed record ReanchorWire(string DocumentSpeId, ReanchorSummaryWire Summary);
    private sealed record ReanchorSummaryWire(int Total, int AutoCount, int ReviewCount, int OrphanCount, IReadOnlyList<ReanchorAnnWire> Annotations);
    private sealed record ReanchorAnnWire(string Id, string Band, double Confidence, int MatchedParagraphIndex);
}

/// <summary>
/// In-process BFF fixture keeping the REAL <see cref="AnnotationReanchorService"/> (constructed by the
/// reanchor endpoint over the injected in-memory <c>IDistributedCache</c>) and replacing only the
/// external SPE download boundary with a Moq. Config-key set mirrors <c>ComposeWordShuttlePollFixture</c>
/// (bff-extensions.md §F.2 Fixture-Config-FIRST).
/// </summary>
public sealed class ComposeParaIdReanchorSeamFixture : WebApplicationFactory<Program>
{
    public Mock<ISpeFileOperations> SpeMock { get; } = new(MockBehavior.Loose);

    public void ResetBoundaries() => SpeMock.Reset();

    /// <summary>Arranges the SPE download boundary to return <paramref name="docxBytes"/>.</summary>
    public void SetupDownload(byte[] docxBytes)
    {
        SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(docxBytes.ToArray()));
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ParaIdReanchorFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ParaIdReanchorFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ParaIdReanchorFakeAuthHandler>(
                ParaIdReanchorFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = ParaIdReanchorFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ParaIdReanchorFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // KEEP the real AnnotationReanchorService (constructed by the endpoint over the injected
            // in-memory cache); double ONLY the external SPE download boundary.
            services.RemoveAll<ISpeFileOperations>();
            services.AddSingleton(SpeMock.Object);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>Fake auth handler authenticating any request carrying an <c>Authorization</c> header, with
/// the <c>tid</c> + <c>oid</c> claims the compose group reads.</summary>
internal sealed class ParaIdReanchorFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ParaIdReanchorFakeAuth";

    public ParaIdReanchorFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var oid = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new("oid", oid),
            new("tid", "tenant-paraid-reanchor-001"),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"ParaId Reanchor Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
