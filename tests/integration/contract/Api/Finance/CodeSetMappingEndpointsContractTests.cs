using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Tests.Seam.Finance;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The two code-set mapping routes through the REAL <c>MapCodeSetMappingEndpoints</c>, <see cref="FinanceAuthorizationFilter"/>,
/// <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 048, FR-22, ADR-008, ADR-019), over the
/// production <see cref="CodeSetProposalService"/> and the in-memory Dataverse of <see cref="FakeCodingReferenceData"/>. Only the
/// embedding call is doubled. Proves 401, 403 (<c>ebilling.codeset.forbidden</c>), 503 (flag off, AI off), 404, 409, and that a
/// denied, disabled or unauthenticated request reaches Dataverse not at all.
/// </summary>
public sealed class CodeSetMappingEndpointsContractTests
{
    private const string Proposals = "/api/finance/code-set-mappings/proposals";
    private const string DefaultSetColumn = "sprk_defaultbillingcodeset";

    // The LIVE privilege names (spaarkedev1, 2026-10-16), spelled out here on purpose: a lower-case spelling is "not held" for everyone.
    private const string WriteMatterType = "prvWritesprk_MatterType_Ref";
    private const string WritePracticeArea = "prvWritesprk_PracticeArea_Ref";

    private static string ConfirmUrl(string table, Guid id) => $"/api/finance/code-set-mappings/{table}/{id}/confirm";

    private sealed class Host : IAsyncDisposable
    {
        /// <summary>The step-0 caller (task 099); internal unless a test changes <see cref="StatedCaller.Kind"/>.</summary>
        public StatedCaller StepZero { get; private set; } = null!;

        public readonly FakeCodingReferenceData Data = new();
        public readonly Mock<IInvoiceAi> Ai = new();
        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly FinAuth.RecordingPrivilegeProbe Probe = new();
        public Guid Litigation;
        public Guid Bankruptcy;
        public Guid Counseling;
        public Guid Activity;
        private WebApplication _app = null!;
        public HttpClient Client = null!;

        public static async Task<Host> StartAsync(bool ebillingEnabled = true, bool admin = true)
        {
            var host = new Host();
            host.Seed();

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
            builder.Services.AddOptions<EbillingOptions>().Configure(o => o.Enabled = ebillingEnabled);
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);
            builder.Services.AddScoped(_ => new CodeSetProposalService(host.Data.Entities.Object, host.Data.Records.Object, host.Ai.Object, NullLogger<CodeSetProposalService>.Instance));
            if (admin)
            {
                host.Probe.Hold(WriteMatterType);
                host.Probe.Hold(WritePracticeArea);
            }

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapCodeSetMappingEndpoints();
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        private void Seed()
        {
            Litigation = Set("UTBMS Litigation", "ABA", "L110", [1f, 0f, 0f]);
            Bankruptcy = Set("UTBMS Bankruptcy", "ABA", "B110", [0f, 1f, 0f]);
            Counseling = Set("UTBMS Counseling", "ABA", "C100", [0f, 0f, 1f]);
            Activity = Data.AddSet("UTBMS Activity", body: "ABA");
            Data.AddCode(Activity, "A101", BillingCodeKind.Activity);
            Vectors["Litigation"] = [0.9f, 0.1f, 0f];
            Vectors["Bankruptcy"] = [0.1f, 0.9f, 0f];
            Vectors["Appellate"] = [1f, 0f, 0f];
            Ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string text, string? _, int? _, CancellationToken _) =>
                    new ReadOnlyMemory<float>(Vectors.TryGetValue(text.Split('\n')[0], out var v) ? v : throw new InvalidOperationException($"No vector for '{text}'.")));
            // The AI review step (task 054): by default it agrees with the top-ranked candidate; tests override it.
            Ai.Setup(a => a.MatchCodeSetAsync(It.IsAny<InvoiceCodeSetMatchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((InvoiceCodeSetMatchRequest r, CancellationToken _) => new InvoiceCodeSetMatchResult
                {
                    CodeSetId = r.Candidates[0].CodeSetId.ToString(),
                    Confidence = 0.9m,
                    Reason = $"{r.ReferenceName} work is covered by {r.Candidates[0].Name}.",
                });
        }

        public readonly Dictionary<string, float[]> Vectors = new(StringComparer.Ordinal);

        private Guid Set(string name, string body, string code, float[] vector)
        {
            var id = Data.AddSet(name, body: body);
            Data.AddCode(id, code, BillingCodeKind.Task);
            Vectors[$"{name} ({body})"] = vector;
            return id;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static HttpRequestMessage Req(string method, string url, bool authenticated = true, object? body = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        }

        return request;
    }

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    public static TheoryData<string, string, bool> AllRoutes => new()
    {
        { "GET", Proposals, false },
        { "POST", ConfirmUrl("mattertype", Guid.NewGuid()), true },
        { "POST", ConfirmUrl("practicearea", Guid.NewGuid()), true },
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // 401 / 403 / disabled — every route
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_Unauthenticated_Returns401_AndNeverReachesDataverse(string method, string url, bool hasBody)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req(method, url, authenticated: false, body: hasBody ? new { codeSetId = host.Litigation } : null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Data.QueryCount.Should().Be(0);
        host.Data.Updates.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_CallerWithoutThePrivilege_Returns403WithTheStableCode_AndNothingIsReadOrWritten(string method, string url, bool hasBody)
    {
        await using var host = await Host.StartAsync(admin: false);
        host.Data.AddMatterType(null, "Litigation");

        var response = await host.Client.SendAsync(Req(method, url, body: hasBody ? new { codeSetId = host.Litigation } : null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("ebilling.codeset.forbidden");
        body["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientPrivilegeReasonCode);
        host.Probe.Calls.Should().NotBeEmpty("the route asked Dataverse, as the caller, what they hold");
        host.Data.QueryCount.Should().Be(0);
        host.Data.Updates.Should().BeEmpty();
    }

    public static IEnumerable<object[]> AllRoutesRefused => StatedCaller.Cross(AllRoutes, StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> AllRoutesInternal => StatedCaller.Cross(AllRoutes, StatedCaller.InternalKinds);

    [Theory(DisplayName = "Task 099 must-fire: every code-set route refuses a flagged-external, row-less or unreadable caller before any privilege question or read")]
    [MemberData(nameof(AllRoutesRefused))]
    public async Task EveryRoute_StepZero_RefusesBeforeAnyCheckOrRead(string method, string url, bool hasBody, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, body: hasBody ? new { codeSetId = host.Litigation } : null));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Probe.Calls.Should().BeEmpty();
        host.Data.QueryCount.Should().Be(0);
        host.Data.Updates.Should().BeEmpty();
    }

    [Theory(DisplayName = "Task 099 must-not-fire: on every code-set route a flag that reads false or blank reaches the privilege check as before")]
    [MemberData(nameof(AllRoutesInternal))]
    public async Task EveryRoute_StepZero_AdmitsAnInternalCaller(string method, string url, bool hasBody, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(admin: false);
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, body: hasBody ? new { codeSetId = host.Litigation } : null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientPrivilegeReasonCode);
        host.Probe.Calls.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(WriteMatterType)]
    [InlineData(WritePracticeArea)]
    public async Task Proposals_NeedWriteOnBothTables(string missing)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(missing);

        var response = await host.Client.SendAsync(Req("GET", Proposals));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Data.QueryCount.Should().Be(0);
    }

    [Fact]
    public async Task Confirm_NeedsWriteOnTheTableTheRouteNames_NotOnTheOtherOne()
    {
        await using var host = await Host.StartAsync(admin: false);
        host.Probe.Hold(WriteMatterType); // may map matter types only
        var matterType = host.Data.AddMatterType(null, "Litigation");
        var area = host.Data.AddPracticeArea(null, "Appellate");

        var forArea = await host.Client.SendAsync(Req("POST", ConfirmUrl("practicearea", area), body: new { codeSetId = host.Litigation }));
        var forMatterType = await host.Client.SendAsync(Req("POST", ConfirmUrl("mattertype", matterType), body: new { codeSetId = host.Litigation }));

        forArea.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forMatterType.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Data.Updates.Should().ContainSingle().Which.Id.Should().Be(matterType);
        host.Probe.Calls.Select(c => c.Privilege).Should().Equal(WritePracticeArea, WriteMatterType);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_Returns503WithTheDisabledCode_AndNeverReachesDataverse(string method, string url, bool hasBody)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.Client.SendAsync(Req(method, url, body: hasBody ? new { codeSetId = host.Litigation } : null));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Data.QueryCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_StillDeniesANonAdmin_TheFlagNeverReplacesAuthorization(string method, string url, bool hasBody)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false, admin: false);

        var response = await host.Client.SendAsync(Req(method, url, body: hasBody ? new { codeSetId = host.Litigation } : null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET proposals
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Proposals_ListEveryUnmappedRowWithASetScoreAndRationale_ExcludeMappedRows_AndWriteNothing()
    {
        await using var host = await Host.StartAsync();
        var litigationType = host.Data.AddMatterType(null, "Litigation");
        host.Data.AddMatterType(host.Counseling, "Mapped already");
        var bankruptcyType = host.Data.AddMatterType(null, "Bankruptcy");
        var appellate = host.Data.AddPracticeArea(null, "Appellate");

        var response = await host.Client.SendAsync(Req("GET", Proposals));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposals = (await Json(response))["proposals"]!.AsArray();
        proposals.Select(p => p!["referenceId"]!.GetValue<Guid>()).Should().Equal(bankruptcyType, litigationType, appellate);
        var first = proposals[1]!;
        first["table"]!.GetValue<string>().Should().Be("mattertype");
        first["referenceName"]!.GetValue<string>().Should().Be("Litigation");
        first["proposed"]!["codeSetId"]!.GetValue<Guid>().Should().Be(host.Litigation);
        first["proposed"]!["name"]!.GetValue<string>().Should().Be("UTBMS Litigation");
        first["proposed"]!["score"]!.GetValue<decimal>().Should().BeGreaterThan(0.9m);
        first["runnerUp"]!["codeSetId"]!.GetValue<Guid>().Should().NotBe(host.Litigation);
        first["rationale"]!.GetValue<string>().Should().Contain("UTBMS Litigation");
        proposals[2]!["table"]!.GetValue<string>().Should().Be("practicearea");
        host.Data.Updates.Should().BeEmpty();
        host.Data.Entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Proposals_WhenTheAiIsOff_Returns503WithTheFeatureDisabledCode()
    {
        await using var host = await Host.StartAsync();
        host.Data.AddMatterType(null, "Litigation");
        host.Ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException("ai.invoice.disabled", "AI is off"));

        var response = await host.Client.SendAsync(Req("GET", Proposals));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ai.invoice.disabled");
        host.Data.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Proposals_WithNothingUnmapped_ReturnsAnEmptyList()
    {
        await using var host = await Host.StartAsync();
        host.Data.AddMatterType(host.Litigation, "Litigation");

        var response = await host.Client.SendAsync(Req("GET", Proposals));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response))["proposals"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Proposals_ReportCoveragePerTable_AndANoneFitsRowCarriesNoConfirmableSet()
    {
        await using var host = await Host.StartAsync();
        host.Data.AddMatterType(host.Litigation, "Mapped already");
        var litigationType = host.Data.AddMatterType(null, "Litigation");
        var appellate = host.Data.AddPracticeArea(null, "Appellate");
        host.Ai.Setup(a => a.MatchCodeSetAsync(It.Is<InvoiceCodeSetMatchRequest>(r => r.ReferenceName == "Appellate"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceCodeSetMatchResult { CodeSetId = "none", Confidence = 0.8m, Reason = "No candidate covers it." });

        var response = await host.Client.SendAsync(Req("GET", Proposals));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        var coverage = body["coverage"]!;
        Count(coverage["matterTypes"]!).Should().Be((2, 1, 1, 0, 0, 0));
        Count(coverage["practiceAreas"]!).Should().Be((1, 0, 0, 1, 0, 0));

        var proposals = body["proposals"]!.AsArray();
        var reviewed = proposals.Single(p => p!["referenceId"]!.GetValue<Guid>() == litigationType)!;
        reviewed["outcome"]!.GetValue<string>().Should().Be("Proposed");
        reviewed["reviewed"]!.GetValue<bool>().Should().BeTrue();
        reviewed["confidence"]!.GetValue<decimal>().Should().Be(0.9m);

        var noneFits = proposals.Single(p => p!["referenceId"]!.GetValue<Guid>() == appellate)!;
        noneFits["outcome"]!.GetValue<string>().Should().Be("NoneFits", "the outcome is a stable string, not an enum number");
        noneFits["proposed"].Should().BeNull("a none-fits row has nothing an admin could confirm by mistake");
        noneFits["runnerUp"].Should().BeNull();
        noneFits["closest"]!["codeSetId"]!.GetValue<Guid>().Should().Be(host.Litigation);
        noneFits["rationale"]!.GetValue<string>().Should().Contain("by hand");
        host.Data.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Proposals_WhenTheReviewStepFails_StillListEveryRow_MarkedUnreviewed()
    {
        await using var host = await Host.StartAsync();
        host.Data.AddMatterType(null, "Litigation");
        host.Data.AddPracticeArea(null, "Appellate");
        host.Ai.Setup(a => a.MatchCodeSetAsync(It.IsAny<InvoiceCodeSetMatchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvoiceAiException(InvoiceAiException.CompletionFailed, ConsumerTypes.InvoiceCodeSetMatch, "model down"));

        var response = await host.Client.SendAsync(Req("GET", Proposals));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the review step never fails the list");
        var body = await Json(response);
        var proposals = body["proposals"]!.AsArray();
        proposals.Should().HaveCount(2);
        proposals.Should().OnlyContain(p => p!["reviewed"]!.GetValue<bool>() == false && p["outcome"]!.GetValue<string>() == "Proposed");
        proposals.Should().OnlyContain(p => p!["confidence"] == null);
        proposals.Select(p => p!["proposed"]!["codeSetId"]!.GetValue<Guid>()).Should().Equal(host.Litigation, host.Litigation);
        Count(body["coverage"]!["matterTypes"]!).Should().Be((1, 0, 1, 0, 1, 0));
    }

    private static (int Active, int Mapped, int Proposed, int NoneFits, int Unreviewed, int Failed) Count(JsonNode node) => (
        node["active"]!.GetValue<int>(),
        node["mapped"]!.GetValue<int>(),
        node["proposed"]!.GetValue<int>(),
        node["noneFits"]!.GetValue<int>(),
        node["unreviewed"]!.GetValue<int>(),
        node["failed"]!.GetValue<int>());

    // ═══════════════════════════════════════════════════════════════════════════
    // POST confirm
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Confirm_ByAnAdmin_SetsTheLookupOnExactlyThatRow_AndReturns200()
    {
        await using var host = await Host.StartAsync();
        var target = host.Data.AddMatterType(null, "Litigation");
        var sibling = host.Data.AddMatterType(null, "Bankruptcy");

        var response = await host.Client.SendAsync(Req("POST", ConfirmUrl("mattertype", target), body: new { codeSetId = host.Litigation }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        body["table"]!.GetValue<string>().Should().Be("mattertype");
        body["referenceId"]!.GetValue<Guid>().Should().Be(target);
        body["codeSetId"]!.GetValue<Guid>().Should().Be(host.Litigation);
        var update = host.Data.Updates.Should().ContainSingle().Subject;
        (update.Entity, update.Id).Should().Be(("sprk_mattertype_ref", target));
        update.Fields.Keys.Should().Equal(DefaultSetColumn);
        host.Data.Find("sprk_mattertype_ref", target)!.GetAttributeValue<EntityReference>(DefaultSetColumn).Id.Should().Be(host.Litigation);
        host.Data.Find("sprk_mattertype_ref", sibling)!.Contains(DefaultSetColumn).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_AnAlreadyMappedRow_Returns409AlreadyMapped_AndKeepsTheExistingMapping()
    {
        await using var host = await Host.StartAsync();
        var row = host.Data.AddPracticeArea(host.Counseling, "Appellate");

        var response = await host.Client.SendAsync(Req("POST", ConfirmUrl("practicearea", row), body: new { codeSetId = host.Litigation }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.codeset.already_mapped");
        host.Data.Updates.Should().BeEmpty();
        host.Data.Find("sprk_practicearea_ref", row)!.GetAttributeValue<EntityReference>(DefaultSetColumn).Id.Should().Be(host.Counseling);
    }

    [Fact]
    public async Task Confirm_WhenAnUnmappedRowKeepsChangingUnderTheWrite_Returns409ConcurrentChange_NotAlreadyMapped()
    {
        await using var host = await Host.StartAsync();
        var row = host.Data.AddMatterType(null, "Litigation");

        // Every re-read of the row is followed by an unrelated edit (a rename), so each conditional write meets a newer version. The row
        // never gains a mapping: "already has a default code set" would be false (task 054 fix-round-1 K4).
        host.Data.AfterQuery = query =>
        {
            if (query.EntityName == "sprk_mattertype_ref" && query.TopCount == 1)
            {
                host.Data.Apply("sprk_mattertype_ref", row, new Dictionary<string, object> { ["sprk_mattertypename"] = "Litigation" });
            }
        };

        var response = await host.Client.SendAsync(Req("POST", ConfirmUrl("mattertype", row), body: new { codeSetId = host.Litigation }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("ebilling.codeset.concurrent_change");
        body["detail"]!.GetValue<string>().Should().NotContain("already has a default code set").And.Contain("Try again");
        host.Data.Find("sprk_mattertype_ref", row)!.Contains(DefaultSetColumn).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_AnUnknownReferenceRow_Returns404NotFound()
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req("POST", ConfirmUrl("mattertype", Guid.NewGuid()), body: new { codeSetId = host.Litigation }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.codeset.not_found");
        host.Data.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_AnUnknownOrUnusableCodeSet_Returns404NotFound_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        var row = host.Data.AddMatterType(null, "Litigation");

        foreach (var codeSetId in new[] { Guid.NewGuid(), Guid.Empty, host.Activity })
        {
            var response = await host.Client.SendAsync(Req("POST", ConfirmUrl("mattertype", row), body: new { codeSetId }));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound, because: codeSetId.ToString());
            (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.codeset.not_found");
        }

        host.Data.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_AnUnknownTableSegment_Returns404_BeforeAnyRightsQuery()
    {
        await using var host = await Host.StartAsync(admin: false);

        var response = await host.Client.SendAsync(Req("POST", ConfirmUrl("matters", Guid.NewGuid()), body: new { codeSetId = host.Litigation }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.codeset.not_found");
        host.Probe.Calls.Should().BeEmpty();
        host.Data.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_WithoutABody_Returns400_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        var row = host.Data.AddMatterType(null, "Litigation");

        var response = await host.Client.SendAsync(Req("POST", ConfirmUrl("mattertype", row)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Data.Updates.Should().BeEmpty();
    }
}
