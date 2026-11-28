using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.Intake;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Intake;
using Sprk.Bff.Api.Tests.Integration.DataMutation.Intake;
using Xunit;
using static Sprk.Bff.Api.Tests.Integration.DataMutation.Intake.IntakeApprovalTestBed;

namespace Sprk.Bff.Api.Tests.Api.Intake;

/// <summary>
/// The wire contract of <c>POST /api/v1/intake/requests/{requestId}/claim</c> (spaarke-intake-pipeline-r1 task 017): status codes,
/// the ProblemDetails <c>reasonCode</c>, and the uniform 404 whose body differs only by its correlation id. A minimal in-process host
/// maps ONLY <see cref="IntakeApprovalEndpoints.MapIntakeApprovalEndpoints"/> with a bearer-presence auth handler and a no-op
/// <c>dataverse-query</c> limiter; the service behind it is the REAL <see cref="IntakeApprovalService"/> over
/// <see cref="IntakeApprovalTestBed"/> (the filter and the handler both run it).
/// </summary>
/// <remarks>Maintain-class (ADR-038, contract).</remarks>
public sealed class IntakeApprovalContractTests
{
    private static string ClaimPath(Guid id) => $"/api/v1/intake/requests/{id}/claim";

    private static async Task<(WebApplication App, HttpClient Client)> HostAsync(IntakeApprovalTestBed bed, Caller? caller)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = IntakeFakeAuthHandler.SchemeName;
                o.DefaultChallengeScheme = IntakeFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, IntakeFakeAuthHandler>(IntakeFakeAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(opt =>
            opt.AddPolicy("dataverse-query", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("dataverse-query-test")));
        builder.Services.AddSingleton(bed.Service());
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapIntakeApprovalEndpoints();
        await app.StartAsync();

        var client = app.GetTestClient();
        if (caller is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.Token);
        return (app, client);
    }

    private static async Task<(HttpStatusCode Status, JsonObject? Body)> ClaimAsync(IntakeApprovalTestBed bed, Caller? caller, Guid? id = null)
    {
        var (app, client) = await HostAsync(bed, caller);
        await using (app)
        {
            var response = await client.PostAsync(ClaimPath(id ?? IntakeRunTestBed.RequestId), content: null);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, string.IsNullOrEmpty(text) ? null : JsonNode.Parse(text)!.AsObject());
        }
    }

    private static string WithoutCorrelation(JsonObject body)
    {
        var copy = body.DeepClone().AsObject();
        copy.Remove("correlationId");
        copy.Remove("traceId");
        return copy.ToJsonString();
    }

    [Fact]
    public async Task Claim_AsAnOwningTeamMember_Is200_AndSetsTheCallersOwnContact()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();

        var (status, body) = await ClaimAsync(bed, Alice);

        status.Should().Be(HttpStatusCode.OK);
        body!["requestId"]!.GetValue<Guid>().Should().Be(IntakeRunTestBed.RequestId);
        body["claimed"]!.GetValue<bool>().Should().BeTrue();
        bed.ClaimedBy().Should().Be(Alice.ContactId);
    }

    [Fact]
    public async Task Claim_WithNoToken_Is401_AndNothingIsWritten()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        var before = bed.Run.Dataverse.Writes.Count;

        var (status, _) = await ClaimAsync(bed, caller: null);

        status.Should().Be(HttpStatusCode.Unauthorized);
        bed.WritesSince(before).Should().BeEmpty();
    }

    [Fact]
    public async Task Claim_EveryRecordCheckFailure_IsOne404_WhoseBodyDiffersOnlyByCorrelationId()
    {
        var unknown = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        var (unknownStatus, unknownBody) = await ClaimAsync(unknown, Alice, Guid.Parse("01700000-0000-4000-8000-0000000000ee"));

        var readOnly = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        readOnly.Rights[Alice.Token] = Spaarke.Dataverse.AccessRights.Read;
        var before = readOnly.Run.Dataverse.Writes.Count;
        var (readOnlyStatus, readOnlyBody) = await ClaimAsync(readOnly, Alice);

        var fault = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        fault.RightsFault.Add(Alice.Token);
        var (faultStatus, faultBody) = await ClaimAsync(fault, Alice);

        new[] { unknownStatus, readOnlyStatus, faultStatus }.Should().OnlyContain(s => s == HttpStatusCode.NotFound);
        unknownBody!["reasonCode"]!.GetValue<string>().Should().Be(IntakeApprovalEndpoints.NotFoundCode);
        unknownBody["correlationId"].Should().NotBeNull();
        WithoutCorrelation(readOnlyBody!).Should().Be(WithoutCorrelation(unknownBody));
        WithoutCorrelation(faultBody!).Should().Be(WithoutCorrelation(unknownBody));
        readOnly.WritesSince(before).Should().BeEmpty();
    }

    [Fact]
    public async Task Claim_NotInTheOwningTeam_Is403()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.Teams[Alice.UserId] = [OtherTeamId];

        var (status, body) = await ClaimAsync(bed, Alice);

        status.Should().Be(HttpStatusCode.Forbidden);
        body!["reasonCode"]!.GetValue<string>().Should().Be(IntakeApprovalEndpoints.ForbiddenCode);
        bed.ClaimedBy().Should().BeNull();
    }

    [Fact]
    public async Task Claim_SomeoneElsesClaim_Is409AlreadyClaimed()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Bob.ContactId);

        var (status, body) = await ClaimAsync(bed, Alice);

        status.Should().Be(HttpStatusCode.Conflict);
        body!["reasonCode"]!.GetValue<string>().Should().Be(IntakeApprovalEndpoints.AlreadyClaimedCode);
        bed.ClaimedBy().Should().Be(Bob.ContactId);
    }

    [Fact]
    public async Task Claim_NotWaitingForApproval_Is409WrongState()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetRequestColumn(IntakeRunStore.RequestIntakeStage, new OptionSetValue(IntakeRequestStages.InReview));

        var (status, body) = await ClaimAsync(bed, Alice);

        status.Should().Be(HttpStatusCode.Conflict);
        body!["reasonCode"]!.GetValue<string>().Should().Be(IntakeApprovalEndpoints.WrongStateCode);
    }

    [Fact]
    public async Task Claim_HarnessSwitchedOff_Is503Disabled_AndNothingIsWritten()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.Run.Options.CurrentValue = new IntakePipelineOptions { Harnesses = { ["nda"] = false } };
        var before = bed.Run.Dataverse.Writes.Count;

        var (status, body) = await ClaimAsync(bed, Alice);

        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        body!["reasonCode"]!.GetValue<string>().Should().Be(IntakeApprovalEndpoints.DisabledCode);
        bed.WritesSince(before).Should().BeEmpty();
    }
}

/// <summary>Bearer-presence auth for the in-process host; the token itself is read by the service.</summary>
public sealed class IntakeFakeAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IntakeFakeAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim("oid", "01700000-0000-4000-8000-0000000000aa"), new Claim("tid", "01700000-0000-4000-8000-0000000000ab")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
