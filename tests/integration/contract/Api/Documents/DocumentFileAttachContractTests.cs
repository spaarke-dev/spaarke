// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`
//   - Path:     `tests/integration/contract/Api/Documents/**`
//   - Justification: POST /api/v1/documents/{id}/file is the ONE way a client gives a document its file (unified-access-
//     control-r2 task 166 f1; owner round 21 item 1 (i), ADR-002 WP-3): the client creates the row WITHOUT a pointer and
//     the BFF stamps it after verifying it. Only the REAL route (its DocumentAuthorizationFilter("write") and handler)
//     proves the order — Write as the caller first, then the relocator's checks, then the app-only stamp.
//
// Doubles are module boundaries only (ADR-038 §4): IAccessDataSource (the caller's rights), the REAL AuthorizationService
// and OperationAccessRule, the REAL relocator and resolver over the document-pointer world, and SpeFileStore at its
// virtual methods. Services the mapped siblings need but these requests never reach are throwing factories.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;
using Xunit;
using Relocation = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;

namespace Sprk.Bff.Api.Tests.Api.Documents;

public class DocumentFileAttachContractTests
{
    private static string Url => $"/api/v1/documents/{Relocation.DocumentId}/file";

    private static object Body(string? drive = Relocation.CustomerA1Container, string? item = Relocation.Item)
        => new { driveId = drive, itemId = item };

    [Fact]
    public async Task Attach_ByTheCreatorWithWrite_OfTheirOwnUploadInTheDerivedContainer_Is200_AndStampsThePointer()
    {
        await using var host = await AttachHost.StartAsync(AccessRights.Read | AccessRights.Write);

        var response = await host.PostAsync(Url, Body());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["driveId"]!.GetValue<string>().Should().Be(Relocation.CustomerA1Container);
        body["itemId"]!.GetValue<string>().Should().Be(Relocation.Item);
        body["alreadyAttached"]!.GetValue<bool>().Should().BeFalse();
        host.World.Updates.Should().ContainSingle().Which.Fields["sprk_graphitemid"].Should().Be(Relocation.Item);
    }

    [Theory]
    [InlineData(AccessRights.None)]
    [InlineData(AccessRights.Read)]
    public async Task Attach_WithoutWriteOnTheDocument_Is403_AndNothingIsReadOrStamped(AccessRights rights)
    {
        await using var host = await AttachHost.StartAsync(rights);

        var response = await host.PostAsync(Url, Body());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.Updates.Should().BeEmpty();
        host.Rig.Spe.Verify(s => s.GetItemCreatorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the caller's Write decides first — the relocator never runs for a caller who may not write the row");
    }

    [Fact]
    public async Task Attach_AFileOutsideTheDerivedContainer_Is409_WithTheAttachReasonCode()
    {
        await using var host = await AttachHost.StartAsync(AccessRights.Read | AccessRights.Write);

        var response = await host.PostAsync(Url, Body(drive: Relocation.CustomerBContainer));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem["errorCode"]!.GetValue<string>().Should().Be(DataverseDocumentsEndpoints.AttachRefusedCode);
        problem["reasonCode"]!.GetValue<string>().Should().Be("WrongContainer");
        host.World.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_AFileSomeoneElseUploaded_Is403()
    {
        await using var host = await AttachHost.StartAsync(AccessRights.Read | AccessRights.Write, world =>
            world.Items[(Relocation.CustomerA1Container, Relocation.Item)] =
                new SpeItemCreator("payroll.xlsx", Relocation.OtherPersonObjectId.ToString("D"), null));

        var response = await host.PostAsync(Url, Body());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be("NotTheUploader");
        host.World.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, Relocation.Item)]
    [InlineData(Relocation.CustomerA1Container, "")]
    public async Task Attach_WithoutTheUploadedFilesIds_Is400(string? drive, string? item)
    {
        await using var host = await AttachHost.StartAsync(AccessRights.Read | AccessRights.Write);

        var response = await host.PostAsync(Url, Body(drive, item));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.World.Updates.Should().BeEmpty();
    }

    /// <summary>A host over the REAL documents mapper, with the caller holding <c>rights</c> on the document.</summary>
    private sealed class AttachHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        private AttachHost(AccessRights rights, Action<TestRecordContainerResolver.DocumentPointerWorld>? arrange)
        {
            World = Relocation.Environment();
            World.Rows[("sprk_document", Relocation.DocumentId)] = Relocation.Doc();
            arrange?.Invoke(World);
            Rig = new Relocation.Rig(World);
            Rights = rights;
        }

        public TestRecordContainerResolver.DocumentPointerWorld World { get; }
        public Relocation.Rig Rig { get; }
        public AccessRights Rights { get; }

        public static async Task<AttachHost> StartAsync(
            AccessRights rights, Action<TestRecordContainerResolver.DocumentPointerWorld>? arrange = null)
        {
            var host = new AttachHost(rights, arrange);
            await host.InitializeAsync();
            return host;
        }

        private async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = AttachTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = AttachTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, AttachTestAuthHandler>(AttachTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

            // The REAL authorization stack over the caller's rights on the document.
            var access = new Mock<IAccessDataSource>(MockBehavior.Strict);
            access.Setup(a => a.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string userId, string resourceId, string? _, CancellationToken _) =>
                    new AccessSnapshot { UserId = userId, ResourceId = resourceId, AccessRights = Rights });
            builder.Services.AddSingleton(access.Object);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();

            builder.Services.AddSingleton(Rig.Relocator);
            builder.Services.AddSingleton(Rig.Resolver);

            // Parameters of the mapped siblings — never reached by an attach request. A stray call throws.
            builder.Services.AddSingleton<SpeFileStore>(_ => throw new NotSupportedException("not reached by the attach route"));
            builder.Services.AddSingleton<DocumentTelemetry>(_ => throw new NotSupportedException("not reached by the attach route"));
            builder.Services.AddSingleton(Mock.Of<IDocumentDataverseService>(MockBehavior.Strict));
            builder.Services.AddSingleton(Mock.Of<IMembershipEventPublisher>(MockBehavior.Strict));
            builder.Services.AddSingleton(Mock.Of<IRecordOwnershipResolver>(MockBehavior.Strict));
            builder.Services.AddSingleton(Mock.Of<IGenericEntityService>(MockBehavior.Strict));

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapDataverseDocumentsEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> PostAsync(string url, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(AttachTestAuthHandler.CallerHeader, "present");
            return _client!.SendAsync(request);
        }

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

/// <summary>Authenticates a request carrying <see cref="CallerHeader"/> as the document world's creator.</summary>
public sealed class AttachTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DocumentAttachTest";
    public const string CallerHeader = "X-Test-Caller";

    public AttachTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(CallerHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            new[] { new Claim("oid", TestRecordContainerResolver.PointerWorldCreatorObjectId.ToString("D")) }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
