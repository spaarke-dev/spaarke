using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
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
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The three rate card routes through the REAL <c>MapRateCardImportEndpoints</c>, <see cref="FinanceAuthorizationFilter"/>,
/// <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 078, FR-29, ADR-008, ADR-019): 401
/// unauthenticated, 403 for a caller without the table privileges, the stable disabled code when the flag is off, and the HTTP shape of
/// each outcome. The service is substituted at its own seam (its behaviour is <see cref="RateCardImportServiceTests"/>).
/// </summary>
public sealed class RateCardImportEndpointsTests
{
    private const string Template = "/api/finance/ratecards/import-template";
    internal const string Import = "/api/finance/ratecards/import";
    internal static readonly Guid FirmId = Guid.NewGuid();
    private static readonly Guid CardId = Guid.NewGuid();

    public static TheoryData<string, string> AllRoutes => new()
    {
        { "GET", Template },
        { "POST", $"{Import}?firmId={FirmId}&name=Card" },
        { "POST", $"/api/finance/ratecards/{CardId}/publish" },
    };

    internal sealed class Host : IAsyncDisposable
    {
        /// <summary>The step-0 caller (task 099); internal unless a test changes <see cref="StatedCaller.Kind"/>.</summary>
        public StatedCaller StepZero { get; private set; } = null!;

        public readonly Mock<RateCardImportService> Service;
        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly FinAuth.RecordingPrivilegeProbe Probe = new();
        private WebApplication _app = null!;
        public HttpClient Client = null!;

        public Host()
        {
            var gcf = Mock.Of<IGraphClientFactory>();
            var spe = new SpeFileStore(
                new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()));
            Service = new Mock<RateCardImportService>(
                MockBehavior.Strict,
                Mock.Of<IGenericEntityService>(), Mock.Of<IDocumentDataverseService>(), spe, Mock.Of<IRecordOwnershipResolver>(),
                Mock.Of<IOptionsMonitor<FinanceOptions>>(), Mock.Of<ILogger<RateCardImportService>>());
        }

        public static async Task<Host> StartAsync(bool ebillingEnabled = true, int maxMb = 25, bool admin = true)
        {
            var host = new Host();
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
            host.StepZero = StatedCaller.Register(builder.Services); // task 099: every /api/finance route runs step 0
            builder.Services.AddOptions<EbillingOptions>().Configure(o =>
            {
                o.Enabled = ebillingEnabled;
                o.MaxSubmissionFileSizeMb = maxMb;
            });
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);
            builder.Services.AddSingleton(host.Service.Object);
            if (admin)
            {
                host.GrantAdmin();
            }

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapRateCardImportEndpoints();
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        public void GrantAdmin()
        {
            foreach (var p in new[]
            {
                RateCardImportEndpoints.CreateRateCardPrivilege, RateCardImportEndpoints.CreateRatePrivilege,
                RateCardImportEndpoints.CreateTimekeeperPrivilege, RateCardImportEndpoints.WriteRateCardPrivilege,
                RateCardImportEndpoints.WriteTimekeeperPrivilege,
            })
            {
                Probe.Hold(p);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    internal static HttpRequestMessage Req(string method, string url, bool authenticated = true, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = content };
        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        }

        return request;
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string partName = "file")
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), partName, "rates.xlsx");
        return content;
    }

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    // ═══════════════════════════════════════════════════════════════════════════
    // 401 / 403 / disabled — every route
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_Unauthenticated_Returns401_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req(method, url, authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_CallerWithoutTheTablePrivileges_Returns403_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync(admin: false);

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" ? Upload(new byte[] { 1 }) : null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientPrivilegeReasonCode);
        host.Probe.Calls.Should().NotBeEmpty("the route asked Dataverse, as the caller, what they hold");
        host.Service.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> AllRoutesRefused => StatedCaller.Cross(AllRoutes, StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> AllRoutesInternal => StatedCaller.Cross(AllRoutes, StatedCaller.InternalKinds);

    [Theory(DisplayName = "Task 099 must-fire: every /api/finance/ratecards route refuses a flagged-external, row-less or unreadable caller with 403, before any privilege question or service call")]
    [MemberData(nameof(AllRoutesRefused))]
    public async Task EveryRoute_StepZero_RefusesBeforeAnyCheckOrServiceCall(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" ? Upload(new byte[] { 1 }) : null));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Probe.Calls.Should().BeEmpty();
        host.Service.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "Task 099 must-not-fire: on every /api/finance/ratecards route a flag that reads false or blank reaches the privilege check as before")]
    [MemberData(nameof(AllRoutesInternal))]
    public async Task EveryRoute_StepZero_AdmitsAnInternalCaller(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(admin: false);
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" ? Upload(new byte[] { 1 }) : null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientPrivilegeReasonCode);
        host.Probe.Calls.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(Import + "?firmId=00000000-0000-0000-0000-0000000000f1&name=Card", RateCardImportEndpoints.CreateRateCardPrivilege)]
    [InlineData(Import + "?firmId=00000000-0000-0000-0000-0000000000f1&name=Card", RateCardImportEndpoints.CreateRatePrivilege)]
    [InlineData(Import + "?firmId=00000000-0000-0000-0000-0000000000f1&name=Card", RateCardImportEndpoints.CreateTimekeeperPrivilege)]
    public async Task Import_NeedsEveryPrivilegeOfTheThreeTablesItWrites(string url, string missing)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(missing);

        var response = await host.Client.SendAsync(Req("POST", url, content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(RateCardImportEndpoints.WriteRateCardPrivilege)]
    [InlineData(RateCardImportEndpoints.WriteTimekeeperPrivilege)]
    public async Task Publish_NeedsWriteOnTheCardAndOnTheTimekeeper(string missing)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(missing);

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_Returns503WithTheDisabledCode_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" ? Upload(new byte[] { 1 }) : null));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_StillDeniesANonAdmin_TheFlagNeverReplacesAuthorization(string method, string url)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false, admin: false);

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" ? Upload(new byte[] { 1 }) : null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Template
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Template_ReturnsTheWorkbookAsAnXlsxFile()
    {
        await using var host = await Host.StartAsync();
        var bytes = new byte[] { 0x50, 0x4B, 3, 4 };
        host.Service.Setup(s => s.BuildTemplate()).Returns(bytes);

        var response = await host.Client.SendAsync(Req("GET", Template));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(RateCardImportEndpoints.XlsxContentType);
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("rate-card-import-template.xlsx");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Import
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_HandsTheWorkbookFirmNameCoreRecordAndCallerToTheService_AndReturns201()
    {
        await using var host = await Host.StartAsync();
        var matterId = Guid.NewGuid();
        host.Access.Grant("sprk_matters", matterId, AccessRights.Read);
        var bytes = new byte[] { 9, 8, 7, 6 };
        RateCardImportRequest? seen = null;
        byte[]? seenBytes = null;
        var result = new RateCardImportResult(CardId, Guid.NewGuid(), "Draft", 3, 2, 0);
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback((RateCardImportRequest r, byte[] b, CancellationToken _) => { seen = r; seenBytes = b; })
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.Done(result));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?firmId={FirmId}&name=%202026%20rates%20&matterId={matterId}", content: Upload(bytes)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith($"/api/finance/ratecards/{CardId}");
        var body = await Json(response);
        body["rateCardId"]!.GetValue<Guid>().Should().Be(CardId);
        body["status"]!.GetValue<string>().Should().Be("Draft");
        seen!.FirmId.Should().Be(FirmId);
        seen.Name.Should().Be("2026 rates");
        seen.Core.Should().Be(new RateCardCoreRecord("sprk_matter", matterId));
        seen.CallerObjectId.Should().Be(Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId));
        seenBytes.Should().Equal(bytes);
    }

    [Fact]
    public async Task Import_ANamedCoreRecordTheCallerCannotRead_Returns403_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync();
        var matterId = Guid.NewGuid(); // no Read granted

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?firmId={FirmId}&name=Card&matterId={matterId}", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().Contain(c => c.Set == "sprk_matters" && c.Id == matterId);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Import_InvalidRows_Return400ProblemDetails_WithTheStableCodeAndEveryRowError()
    {
        await using var host = await Host.StartAsync();
        var errors = new[]
        {
            new RateCardImportError("Rates", 4, "Approved Rate", "Approved Rate may not be higher than Rack Rate."),
            new RateCardImportError("Timekeepers", 7, "Name", "Name is required."),
            new RateCardImportError(null, null, "firmId", "The firm was not found."),
        };
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.Invalid(errors));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?firmId={FirmId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("ratecard.import_invalid");
        var wire = body["errors"]!.AsArray();
        wire.Should().HaveCount(3);
        wire[0]!["sheet"]!.GetValue<string>().Should().Be("Rates");
        wire[0]!["row"]!.GetValue<int>().Should().Be(4);
        wire[0]!["column"]!.GetValue<string>().Should().Be("Approved Rate");
        wire[0]!["message"]!.GetValue<string>().Should().Contain("higher than Rack");
        wire[1]!["row"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public async Task Import_OwnerRefused_Returns409WithTheOwnerReasonCode()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.OwnerRefused(RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team")));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?firmId={FirmId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(RecordOwnerRefusal.NoOwnerSource);
    }

    [Fact]
    public async Task Import_ContainerNotConfigured_Returns503WithItsCode()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.Refused(503, RateCardImportService.CodeContainerNotConfigured, "not configured"));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?firmId={FirmId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ratecard.container_not_configured");
    }

    [Theory]
    [InlineData("name=Card", "firmId")]                                                    // no firm
    [InlineData("firmId=not-a-guid&name=Card", "firmId")]
    [InlineData("firmId=00000000-0000-0000-0000-0000000000f1", "name")]                    // no name
    [InlineData("firmId=00000000-0000-0000-0000-0000000000f1&name=Card&matterId=nope", "matterId")]
    [InlineData("firmId=00000000-0000-0000-0000-0000000000f1&name=Card&matterId=00000000-0000-0000-0000-000000000001&projectId=00000000-0000-0000-0000-000000000002", "coreRecord")]
    public async Task Import_BadQuery_Returns400_AndNeverReachesTheService(string query, string column)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant("sprk_matters", Guid.Parse("00000000-0000-0000-0000-000000000001"), AccessRights.Read);
        host.Access.Grant("sprk_projects", Guid.Parse("00000000-0000-0000-0000-000000000002"), AccessRights.Read);

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?{query}", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("ratecard.import_invalid");
        body["errors"]!.AsArray().Select(e => e!["column"]!.GetValue<string>()).Should().Contain(column);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Import_AFileOverTheConfiguredLimit_Returns413_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync(maxMb: 1);

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?firmId={FirmId}&name=Card", content: Upload(new byte[(2 * 1024 * 1024)])));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ratecard.file_too_large");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Import_WithoutAMultipartFile_IsRefused_NonMultipart415_MissingOrEmptyFile400()
    {
        await using var host = await Host.StartAsync();

        var notMultipart = await host.Client.SendAsync(Req("POST", $"{Import}?firmId={FirmId}&name=Card", content: new StringContent("x")));
        var noFile = await host.Client.SendAsync(Req("POST", $"{Import}?firmId={FirmId}&name=Card",
            content: new MultipartFormDataContent { { new StringContent("x"), "other" } }));
        var empty = await host.Client.SendAsync(Req("POST", $"{Import}?firmId={FirmId}&name=Card", content: Upload(Array.Empty<byte>())));

        notMultipart.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        noFile.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Service.VerifyNoOtherCalls();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Publish
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Publish_Returns200WithThePublishedCard()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.PublishAsync(CardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateCardOutcome<RateCardPublishResult>.Done(new RateCardPublishResult(CardId, "Published", 2)));

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        body["status"]!.GetValue<string>().Should().Be("Published");
        body["timekeepersApproved"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task Publish_Overlap_Returns400WithTheErrors_NotFound404_NotDraft409()
    {
        await using var host = await Host.StartAsync();
        var overlap = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var published = Guid.NewGuid();
        host.Service.Setup(s => s.PublishAsync(overlap, It.IsAny<CancellationToken>())).ReturnsAsync(
            RateCardOutcome<RateCardPublishResult>.Invalid(new[] { new RateCardImportError("Rates", null, "Effective From", "Rate 'B' overlaps rate 'A' for the same timekeeper.") }));
        host.Service.Setup(s => s.PublishAsync(missing, It.IsAny<CancellationToken>())).ReturnsAsync(
            RateCardOutcome<RateCardPublishResult>.Refused(404, RateCardImportService.CodeNotFound, "The rate card was not found."));
        host.Service.Setup(s => s.PublishAsync(published, It.IsAny<CancellationToken>())).ReturnsAsync(
            RateCardOutcome<RateCardPublishResult>.Refused(409, RateCardImportService.CodeNotDraft, "Only a Draft rate card can be published."));

        var r400 = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{overlap}/publish"));
        var r404 = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{missing}/publish"));
        var r409 = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{published}/publish"));

        r400.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await Json(r400);
        body["errorCode"]!.GetValue<string>().Should().Be("ratecard.publish_invalid");
        body["errors"]!.AsArray().Single()!["message"]!.GetValue<string>().Should().Contain("overlaps");
        r404.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(r404))["errorCode"]!.GetValue<string>().Should().Be("ratecard.not_found");
        r409.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(r409))["errorCode"]!.GetValue<string>().Should().Be("ratecard.not_draft");
    }
}
