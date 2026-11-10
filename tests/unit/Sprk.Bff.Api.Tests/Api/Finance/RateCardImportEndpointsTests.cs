using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;
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
    // 116 fix round 2: the known version is the one the bad-query cases name, so the version rights check passes them on to the handler.
    internal static readonly Guid VersionId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ProcedureId = Guid.NewGuid();

    public static TheoryData<string, string> AllRoutes => new()
    {
        { "GET", Template },
        { "POST", $"{Import}?procedureVersionId={VersionId}&name=Card" },
        { "POST", $"/api/finance/ratecards/{CardId}/publish" },
    };

    internal sealed class Host : IAsyncDisposable
    {
        /// <summary>The step-0 caller (task 099); internal unless a test changes <see cref="StatedCaller.Kind"/>.</summary>
        public StatedCaller StepZero { get; private set; } = null!;

        public readonly Mock<RateCardImportService> Service;

        /// <summary>The Dataverse seam the Procedure-write filter reads the version's Procedure through (085).</summary>
        public readonly Mock<IGenericEntityService> Entities = new();
        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly FinAuth.RecordingPrivilegeProbe Probe = new();

        /// <summary>116 fix round 2: the file store the import filter asks the caller's rights on the version through.</summary>
        public readonly Sprk.Bff.Api.Tests.Finance.Files.StoredFileRig Files = new();
        private WebApplication _app = null!;
        public HttpClient Client = null!;

        /// <summary>116 fix round 1 (F4): the body throws the server's 413 when read, as Kestrel does past its body cap.</summary>
        public bool BodyOverTheServerCap { get; set; }

        public Host()
        {
            Service = new Mock<RateCardImportService>(
                MockBehavior.Strict,
                Mock.Of<IGenericEntityService>(), new Sprk.Bff.Api.Tests.Finance.Files.StoredFileRig().Service, Mock.Of<IRecordOwnershipResolver>(),
                Mock.Of<ILogger<RateCardImportService>>());
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
            builder.Services.AddSingleton(host.Files.Service);
            host.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection());
            builder.Services.AddSingleton(host.Entities.Object);
            if (admin)
            {
                host.GrantAdmin();
            }

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.Use(async (context, next) =>
            {
                if (host.BodyOverTheServerCap)
                {
                    context.Request.Body = new ProcedureEndpointsTests.OverTheCapBody();
                }

                await next(context);
            });
            host._app.MapRateCardImportEndpoints();
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        /// <summary>The stored version <paramref name="versionId"/> belongs to the Procedure <paramref name="procedureId"/>.</summary>
        public void VersionBelongsTo(Guid versionId, Guid procedureId) =>
            Entities.Setup(e => e.RetrieveMultipleAsync(
                    It.Is<QueryExpression>(q => q.EntityName == "sprk_procedureversion" && q.Criteria.Conditions.Any(c => c.Values.Contains(versionId))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection(new List<Entity>
                {
                    new("sprk_procedureversion", versionId) { ["sprk_procedure"] = new EntityReference("sprk_procedure", procedureId) },
                }));

        /// <summary>The stored card <paramref name="cardId"/> names the Procedure Version <paramref name="versionId"/> (none: a card with no version).</summary>
        public void CardBelongsTo(Guid cardId, Guid? versionId)
        {
            var card = new Entity("sprk_ratecard", cardId);
            if (versionId is { } v)
            {
                card["sprk_procedureversion"] = new EntityReference("sprk_procedureversion", v);
            }

            Entities.Setup(e => e.RetrieveMultipleAsync(
                    It.Is<QueryExpression>(q => q.EntityName == "sprk_ratecard" && q.Criteria.Conditions.Any(c => c.Values.Contains(cardId))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection(new List<Entity> { card }));
        }

        /// <summary>The whole chain a publish is checked through: card, its version, the version's Procedure.</summary>
        public void CardOfProcedure(Guid cardId, Guid versionId, Guid procedureId)
        {
            CardBelongsTo(cardId, versionId);
            VersionBelongsTo(versionId, procedureId);
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

            // 116 fix round 2: an admin of the version's Procedure, with Read and Write on the version itself.
            VersionBelongsTo(VersionId, ProcedureId);
            Access.Grant("sprk_procedures", ProcedureId, AccessRights.Read | AccessRights.Write);
            Files.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, AccessRights.Read | AccessRights.Write);
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
    [InlineData(Import + "?procedureVersionId=00000000-0000-0000-0000-0000000000a1&name=Card", RateCardImportEndpoints.CreateRateCardPrivilege)]
    [InlineData(Import + "?procedureVersionId=00000000-0000-0000-0000-0000000000a1&name=Card", RateCardImportEndpoints.CreateRatePrivilege)]
    [InlineData(Import + "?procedureVersionId=00000000-0000-0000-0000-0000000000a1&name=Card", RateCardImportEndpoints.CreateTimekeeperPrivilege)]
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
    public async Task Import_HandsTheWorkbookVersionNameAndCallerToTheService_AndReturns201()
    {
        await using var host = await Host.StartAsync();
        host.VersionBelongsTo(VersionId, ProcedureId);
        host.Access.Grant("sprk_procedures", ProcedureId, AccessRights.Read | AccessRights.Write);
        var bytes = new byte[] { 9, 8, 7, 6 };
        RateCardImportRequest? seen = null;
        byte[]? seenBytes = null;
        var sourceFile = Guid.NewGuid();
        var result = new RateCardImportResult(CardId, sourceFile, "Draft", 3, 2, 0);
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback((RateCardImportRequest r, byte[] b, CancellationToken _) => { seen = r; seenBytes = b; })
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.Done(result));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=%202026%20rates%20", content: Upload(bytes)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith($"/api/finance/ratecards/{CardId}");
        var body = await Json(response);
        body["rateCardId"]!.GetValue<Guid>().Should().Be(CardId);
        body["status"]!.GetValue<string>().Should().Be("Draft");
        body["sourceFileId"]!.GetValue<Guid>().Should().Be(sourceFile, "task 116: the workbook is the version's source file");
        seen!.ProcedureVersionId.Should().Be(VersionId);
        seen.FirmId.Should().BeNull("the firm comes from the Procedure");
        seen.Name.Should().Be("2026 rates");
        seen.CallerObjectId.Should().Be(Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId));
        seen.Caller.FindFirst("oid")!.Value.Should().Be(FinanceAuthzTestAuthHandler.CallerObjectId, "task 116: the file store asks as the caller");
        seen.CallerBearerToken.Should().NotBeNullOrEmpty();
        seenBytes.Should().Equal(bytes);
        host.Access.Calls.Should().Contain(c => c.Path == FinAuth.AccessPath.Record && c.Set == "sprk_procedures" && c.Id == ProcedureId && c.HasToken,
            "the Procedure's rights are asked of Dataverse as the caller");
    }

    [Fact]
    public async Task Import_ANamedFirm_IsHandedToTheService_ToCompareWithTheProceduresFirm()
    {
        await using var host = await Host.StartAsync();
        RateCardImportRequest? seen = null;
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback((RateCardImportRequest r, byte[] _, CancellationToken _) => seen = r)
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.Done(new RateCardImportResult(CardId, Guid.NewGuid(), "Draft", 1, 0, 0)));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&firmId={FirmId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        seen!.FirmId.Should().Be(FirmId);
    }

    [Fact(DisplayName = "085 must-fire: a caller without Write on the version's Procedure gets 403 insufficient_rights and never reaches the service")]
    public async Task Import_WithoutWriteOnTheVersionsProcedure_Returns403_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync();
        host.VersionBelongsTo(VersionId, ProcedureId);
        host.Access.Grant("sprk_procedures", ProcedureId, AccessRights.Read); // Read only

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Json(response);
        problem["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        problem["detail"]!.GetValue<string>().Should().Be("You need Write on this Procedure.");
        host.Access.Calls.Should().Contain(c => c.Path == FinAuth.AccessPath.Record && c.Set == "sprk_procedures" && c.Id == ProcedureId);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "085: an UNKNOWN rights answer on the Procedure is system_failure, not insufficient_rights")]
    public async Task Import_WhenTheProceduresRightsAreUnknown_IsSystemFailure()
    {
        await using var host = await Host.StartAsync();
        host.VersionBelongsTo(VersionId, ProcedureId);
        host.Access.Grant("sprk_procedures", ProcedureId, AccessRights.None); // 116 fix round 2: the host's admin default grants it
        host.Access.Fault("sprk_procedures", ProcedureId);

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "085: a fault reading the version's Procedure denies (fail closed) and never reaches the service")]
    public async Task Import_WhenTheVersionsProcedureCannotBeRead_Returns403_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync();
        host.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("simulated read fault"));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116 fix 2 (item 9) must-fire: an unknown version and a version the caller cannot read get the same 404 (status and body); no Procedure check, no service call")]
    public async Task Import_AnUnknownVersion_AndAnUnreadableOne_AnswerTheSame404()
    {
        await using var host = await Host.StartAsync();
        var unknownVersion = Guid.NewGuid();
        var unknown = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={unknownVersion}&name=Card", content: Upload(new byte[] { 1 })));

        host.Files.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, AccessRights.None);
        var hidden = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
        (await Json(unknown))["errorCode"]!.GetValue<string>().Should().Be(ProcedureService.CodeVersionNotFound);
        host.Files.Probe.Calls.Select(c => (c.Set, c.Id)).Should().Contain(
            new[] { (StoredFileSchema.ProcedureVersionEntitySet, unknownVersion), (StoredFileSchema.ProcedureVersionEntitySet, VersionId) },
            "the version rights are asked for an unknown id too");
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116 fix 2 (item 9) must-fire: Read without Write on the version is 403 before any answer about the workbook; the service is never reached")]
    public async Task Import_ReadWithoutWriteOnTheVersion_Is403_BeforeTheWorkbookIsChecked()
    {
        await using var host = await Host.StartAsync();
        host.Files.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, AccessRights.Read);

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Json(response);
        problem["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        problem["detail"]!.GetValue<string>().Should().Be("You need Write on this Procedure Version.");
        host.Files.Probe.Calls.Should().Contain(c => c.Set == StoredFileSchema.ProcedureVersionEntitySet && c.Id == VersionId && c.Token != null,
            "asked as the caller");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116 fix 2 (item 9): a readable version that is not usable for a card still reaches the service, whose 400 ratecard.procedure_version_invalid stands")]
    public async Task Import_AReadableVersionNotUsable_TheServiceRefuses400()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.Refused(400, RateCardImportService.CodeProcedureVersionInvalid, "not usable"));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ratecard.procedure_version_invalid");
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

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

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

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(RecordOwnerRefusal.NoOwnerSource);
    }

    [Fact]
    public async Task Import_FileStoreNotConfigured_Returns503WithItsCode()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.Refused(503, StoredFileRefusal.NotConfigured, "not configured"));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("stored_file.store_not_configured");
    }

    [Theory(DisplayName = "116 must-fire: a refused workbook write is answered as the version file route answers it, never a generic 500")]
    [InlineData(StoredFileRefusal.CallerNotInternal, 403, "reasonCode", FinanceAuthorizationFilter.SystemFailureReasonCode)]
    [InlineData(StoredFileRefusal.NoWriteOnParent, 403, "reasonCode", FinanceAuthorizationFilter.InsufficientRightsReasonCode)]
    [InlineData(StoredFileRefusal.OwnerUnresolved, 409, "errorCode", RecordOwnerRefusal.NoOwnerSource)]
    [InlineData(StoredFileRefusal.NotConfigured, 503, "errorCode", StoredFileRefusal.NotConfigured)]
    [InlineData(StoredFileRefusal.InvalidRequest, 400, "errorCode", "ratecard.import_invalid")]
    [InlineData("stored_file.something_else", 500, "errorCode", "ratecard.import_failed")]
    public async Task Import_ARefusedFileWrite_IsMappedAsTheVersionFileRouteMapsIt(string refusal, int status, string field, string code)
    {
        await using var host = await Host.StartAsync();
        var stored = refusal == StoredFileRefusal.OwnerUnresolved
            ? StoredFileCreateResult.Refused(refusal, RecordOwnerRefusal.NoOwnerSource)
            : StoredFileCreateResult.Refused(refusal);
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.FileRefused(stored));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[] { 1 })));

        ((int)response.StatusCode).Should().Be(status);
        var body = await Json(response);
        body[field]!.GetValue<string>().Should().Be(code);
        if (refusal == StoredFileRefusal.NoWriteOnParent)
        {
            body["detail"]!.GetValue<string>().Should().Be("You need Write on this Procedure Version.");
        }
    }

    [Theory]
    [InlineData("name=Card", "procedureVersionId")]                                                       // no version
    [InlineData("procedureVersionId=not-a-guid&name=Card", "procedureVersionId")]
    [InlineData("procedureVersionId=00000000-0000-0000-0000-0000000000a1&name=Card&firmId=not-a-guid", "firmId")]
    [InlineData("procedureVersionId=00000000-0000-0000-0000-0000000000a1", "name")]                       // no name
    [InlineData("procedureVersionId=00000000-0000-0000-0000-0000000000a1&name=Card&matterId=00000000-0000-0000-0000-000000000001", "matterId")]
    [InlineData("procedureVersionId=00000000-0000-0000-0000-0000000000a1&name=Card&projectId=00000000-0000-0000-0000-000000000002", "projectId")]
    [InlineData("procedureVersionId=00000000-0000-0000-0000-0000000000a1&name=Card&workAssignmentId=00000000-0000-0000-0000-000000000003", "workAssignmentId")]
    public async Task Import_BadQuery_Returns400_AndNeverReachesTheService(string query, string column)
    {
        await using var host = await Host.StartAsync();

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

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[(2 * 1024 * 1024)])));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ratecard.file_too_large");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Import_WithoutAMultipartFile_IsRefused_NonMultipart415_MissingOrEmptyFile400()
    {
        await using var host = await Host.StartAsync();

        var notMultipart = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: new StringContent("x")));
        var noFile = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card",
            content: new MultipartFormDataContent { { new StringContent("x"), "other" } }));
        var empty = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(Array.Empty<byte>())));

        notMultipart.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        noFile.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Service.VerifyNoOtherCalls();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Publish
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Publish: with the table privileges and Write on the card's Procedure the card is published (must-not-fire control)")]
    public async Task Publish_Returns200WithThePublishedCard()
    {
        await using var host = await Host.StartAsync();
        host.CardOfProcedure(CardId, VersionId, ProcedureId);
        host.Access.Grant("sprk_procedures", ProcedureId, AccessRights.Read | AccessRights.Write);
        host.Service.Setup(s => s.PublishAsync(CardId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateCardOutcome<RateCardPublishResult>.Done(new RateCardPublishResult(CardId, "Published", 2)));

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        body["status"]!.GetValue<string>().Should().Be("Published");
        body["timekeepersApproved"]!.GetValue<int>().Should().Be(2);
        host.Access.Calls.Should().Contain(c => c.Path == FinAuth.AccessPath.Record && c.Set == "sprk_procedures" && c.Id == ProcedureId && c.HasToken,
            "the Procedure's rights are asked of Dataverse as the caller");
        host.Service.Verify(s => s.PublishAsync(CardId, VersionId, It.IsAny<CancellationToken>()), Times.Once, "the version the check named is the one handed on");
    }

    [Fact(DisplayName = "Publish must-fire: table privileges but no Write on the card's Procedure gives 403 insufficient_rights and nothing is published")]
    public async Task Publish_WithoutWriteOnTheCardsProcedure_Returns403_AndNothingIsPublished()
    {
        await using var host = await Host.StartAsync();
        host.CardOfProcedure(CardId, VersionId, ProcedureId);
        host.Access.Grant("sprk_procedures", ProcedureId, AccessRights.Read); // Read only

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Json(response);
        problem["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        problem["detail"]!.GetValue<string>().Should().Be("You need Write on this Procedure.");
        host.Access.Calls.Should().Contain(c => c.Path == FinAuth.AccessPath.Record && c.Set == "sprk_procedures" && c.Id == ProcedureId);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Publish must-fire: a handler with no record of the Procedure check (no checked version in HttpContext.Items) answers 403 system_failure and calls no service")]
    public async Task Publish_WithNoRecordOfTheCheck_IsSystemFailure_AndCallsNoService()
    {
        await using var host = await Host.StartAsync();
        var http = new DefaultHttpContext { TraceIdentifier = "trace-1" }; // the filter never ran, so Items has no checked version

        var result = await RateCardImportEndpoints.PublishRateCard(CardId, http, host.Service.Object, default);

        var problem = result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Publish: an UNKNOWN rights answer on the card's Procedure is system_failure, not insufficient_rights")]
    public async Task Publish_WhenTheProceduresRightsAreUnknown_IsSystemFailure()
    {
        await using var host = await Host.StartAsync();
        host.CardOfProcedure(CardId, VersionId, ProcedureId);
        host.Access.Grant("sprk_procedures", ProcedureId, AccessRights.None); // 116 fix round 2: the host's admin default grants it
        host.Access.Fault("sprk_procedures", ProcedureId);

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Publish: a fault reading the card or its version denies (fail closed) with 403 system_failure and publishes nothing")]
    public async Task Publish_WhenTheCardsProcedureCannotBeRead_Returns403SystemFailure()
    {
        await using var host = await Host.StartAsync();
        host.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("simulated read fault"));

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Publish: a fault reading the version's Procedure (the card is found) denies with 403 system_failure")]
    public async Task Publish_WhenTheVersionCannotBeRead_Returns403SystemFailure()
    {
        await using var host = await Host.StartAsync();
        host.CardBelongsTo(CardId, VersionId);
        host.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_procedureversion"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("simulated read fault"));

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Publish must-fire: an unknown card is 403 (not 404), so the answer does not say whether a card exists")]
    public async Task Publish_OnAnUnknownCard_Returns403_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync(); // the default read finds nothing

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Publish must-fire: a card with no Procedure Version is 403 and never reaches the service")]
    public async Task Publish_OnACardWithNoVersion_Returns403_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync();
        host.CardBelongsTo(CardId, versionId: null);

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Publish must-fire: a version with no Procedure is 403 and never reaches the service")]
    public async Task Publish_OnAVersionWithNoProcedure_Returns403_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync();
        host.CardBelongsTo(CardId, Guid.NewGuid()); // a version unknown to the read (116 fix round 2: the host's VersionId now has a Procedure)

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Publish must-fire: the service refusing 409 ratecard.version_changed (the card moved to another version after the check) is 409 and publishes nothing")]
    public async Task Publish_WhenTheCardMovedToAnotherVersion_Returns409VersionChanged()
    {
        await using var host = await Host.StartAsync();
        host.CardOfProcedure(CardId, VersionId, ProcedureId);
        host.Access.Grant("sprk_procedures", ProcedureId, AccessRights.Read | AccessRights.Write);
        host.Service.Setup(s => s.PublishAsync(CardId, VersionId, It.IsAny<CancellationToken>())).ReturnsAsync(
            RateCardOutcome<RateCardPublishResult>.Refused(409, RateCardImportService.CodeVersionChanged, "moved"));

        var response = await host.Client.SendAsync(Req("POST", $"/api/finance/ratecards/{CardId}/publish"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ratecard.version_changed");
    }

    [Fact]
    public async Task Publish_Overlap_Returns400WithTheErrors_NotFound404_NotDraft409()
    {
        await using var host = await Host.StartAsync();
        var overlap = Guid.NewGuid();
        var missing = Guid.NewGuid(); // found by the check, then gone when the service reads it (a race)
        var published = Guid.NewGuid();
        host.VersionBelongsTo(VersionId, ProcedureId);
        host.Access.Grant("sprk_procedures", ProcedureId, AccessRights.Read | AccessRights.Write);
        foreach (var id in new[] { overlap, missing, published })
        {
            host.CardBelongsTo(id, VersionId);
        }

        host.Service.Setup(s => s.PublishAsync(overlap, VersionId, It.IsAny<CancellationToken>())).ReturnsAsync(
            RateCardOutcome<RateCardPublishResult>.Invalid(new[] { new RateCardImportError("Rates", null, "Effective From", "Rate 'B' overlaps rate 'A' for the same timekeeper.") }));
        host.Service.Setup(s => s.PublishAsync(missing, VersionId, It.IsAny<CancellationToken>())).ReturnsAsync(
            RateCardOutcome<RateCardPublishResult>.Refused(404, RateCardImportService.CodeNotFound, "The rate card was not found."));
        host.Service.Setup(s => s.PublishAsync(published, VersionId, It.IsAny<CancellationToken>())).ReturnsAsync(
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

    [Fact(DisplayName = "116 fix 1 (F4) must-fire: an import whose body the server stops at its cap (a chunked body) is 413 file_too_large, not 400")]
    public async Task Import_ABodyOverTheServerCap_Is413()
    {
        await using var host = await Host.StartAsync();
        host.BodyOverTheServerCap = true;

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[10])));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ratecard.file_too_large");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116 fix 1 (A1): a workbook refused because the version stopped being a Draft is 409 procedure_version.not_draft")]
    public async Task Import_AVersionNoLongerADraft_Is409NotDraft()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ImportAsync(It.IsAny<RateCardImportRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateCardOutcome<RateCardImportResult>.FileRefused(StoredFileCreateResult.Refused(StoredFileRefusal.ParentNoLongerAccepts)));

        var response = await host.Client.SendAsync(Req("POST", $"{Import}?procedureVersionId={VersionId}&name=Card", content: Upload(new byte[10])));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("procedure_version.not_draft");
    }
}