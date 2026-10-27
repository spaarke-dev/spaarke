// -----------------------------------------------------------------------------
// DataverseWebApiOrgSettingsContractApplierTests.cs
//
// Task 253 (G38) — unit tests for the H6 org-settings applier that replaced
// PacOrgSettingsContractApplier (`pac org update-settings`). ADR-038 path #1:
// a real HttpClient over a hand-rolled fake HttpMessageHandler (NOT
// Mock<HttpMessageHandler>, banned per testing.md) and a fake credential.
//
// IN SCOPE: the F14 contract on the organization row — one GET, one PATCH of
// only the settings below target (as a JSON number for an integer column,
// with If-Match: * so it never creates a row), no PATCH when satisfied, a
// higher value never lowered; every refusal (token, GET, PATCH, unparseable
// row, a non-column name) is a Failure that says nothing was applied.
// #2027: org auditing (isauditenabled=true, auditretentionperiodv2=-1 Forever)
// is part of the contract; a -1 target is met only by -1; and the row is read
// back after the PATCH, so a setting Dataverse silently ignores fails the step.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiOrgSettingsContractApplierTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";
    private const string OrganizationId = "5d4c3b2a-1111-2222-3333-444455556666";

    [Fact]
    public async Task ApplyAsync_FreshEnvironmentAt5Mb_PatchesTheOrganizationRowTo25Mb_AsANumber()
    {
        var handler = new FakeHandler(OrganizationRow(maxUpload: 5242880));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>()
            .Which.AppliedOrAlreadyCorrect["maxuploadfilesize"].Should().Be("25600000");
        handler.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Get, HttpMethod.Patch, HttpMethod.Get);

        var get = handler.Requests[0];
        get.Method.Should().Be(HttpMethod.Get);
        get.Uri.AbsolutePath.Should().Be("/api/data/v9.2/organizations");
        Uri.UnescapeDataString(get.Uri.Query).Should()
            .Contain("$select=organizationid,auditretentionperiodv2,isauditenabled,maxuploadfilesize");
        get.Authorization.Should().Be("Bearer fake-dataverse-token");

        var patch = handler.Requests[1];
        patch.Method.Should().Be(HttpMethod.Patch);
        patch.Uri.AbsolutePath.Should().Be($"/api/data/v9.2/organizations({OrganizationId})");
        patch.IfMatch.Should().Be("*", "the PATCH updates the existing row and must never create one");
        using var body = JsonDocument.Parse(patch.Body!);
        body.RootElement.GetProperty("maxuploadfilesize").ValueKind.Should().Be(JsonValueKind.Number,
            "maxuploadfilesize is an integer column");
        body.RootElement.GetProperty("maxuploadfilesize").GetInt64().Should().Be(25600000);
        body.RootElement.EnumerateObject().Should().ContainSingle("only the settings below target are sent");
    }

    [Theory]
    [InlineData(25600000)]
    [InlineData(31457280)]   // an environment already above target is never lowered
    public async Task ApplyAsync_AlreadyAtOrAboveTarget_NoPatch(long current)
    {
        var handler = new FakeHandler(OrganizationRow(maxUpload: current));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>()
            .Which.AppliedOrAlreadyCorrect["maxuploadfilesize"].Should().Be(current.ToString(System.Globalization.CultureInfo.InvariantCulture));
        handler.Requests.Should().ContainSingle("only the GET — nothing to write");
    }

    // ---------- #2027: org auditing ----------

    [Fact]
    public void Manifest_TurnsOrgAuditingOn_AndKeepsItForever()
    {
        StaticOrgSettingsContractManifest.DefaultOrgSettings.Should().Contain("isauditenabled", "true")
            .And.Contain("auditretentionperiodv2", "-1")
            .And.Contain("maxuploadfilesize", "25600000");
    }

    [Fact]
    public async Task ApplyAsync_FreshEnvironmentWithAuditingOff_TurnsItOnForever_TypedAndReadBack()
    {
        var handler = new FakeHandler(OrganizationRow(maxUpload: 25600000, auditEnabled: "false", retention: "null"));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        var applied = outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>().Which.AppliedOrAlreadyCorrect;
        applied["isauditenabled"].Should().Be("true");
        applied["auditretentionperiodv2"].Should().Be("-1");
        handler.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Get, HttpMethod.Patch, HttpMethod.Get);
        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        body.RootElement.GetProperty("isauditenabled").ValueKind.Should().Be(JsonValueKind.True);
        body.RootElement.GetProperty("auditretentionperiodv2").GetInt64().Should().Be(-1);
        body.RootElement.TryGetProperty("maxuploadfilesize", out _).Should().BeFalse("25 MB is already satisfied");
    }

    [Theory]
    [InlineData("30")]      // a finite retention is below Forever — must be raised, not kept as "30 >= -1"
    [InlineData("2555")]
    public async Task ApplyAsync_FiniteAuditRetention_IsRaisedToForever(string retention)
    {
        var handler = new FakeHandler(OrganizationRow(maxUpload: 25600000, retention: retention));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>()
            .Which.AppliedOrAlreadyCorrect["auditretentionperiodv2"].Should().Be("-1");
        using var body = JsonDocument.Parse(handler.Requests.Single(r => r.Method == HttpMethod.Patch).Body!);
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("auditretentionperiodv2");
    }

    [Fact]
    public async Task ApplyAsync_PatchAnswered204ButTheRowDidNotChange_FailsNamingTheSetting()
    {
        // Dataverse returns 204 for a PATCH of a column it ignores: only the read-back can tell.
        var handler = new FakeHandler(OrganizationRow(
            maxUpload: 25600000, auditEnabled: "false", retention: "-1", patchTakes: false));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("did not take")
            .And.Contain("isauditenabled (target true, reads back false)");
        handler.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Get, HttpMethod.Patch, HttpMethod.Get);
    }

    [Fact]
    public async Task ApplyAsync_ReadBackRefused_Fails_NeverReportsTheSettingApplied()
    {
        var gets = 0;
        var row = OrganizationRow(maxUpload: 5242880);
        var handler = new FakeHandler(request => request.Method == HttpMethod.Get && ++gets == 2
            ? Json(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"busy"}}""")
            : row(request));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("reading it back failed").And.Contain("503");
    }

    [Theory]
    [InlineData("-1", "-1", true)]
    [InlineData("30", "-1", false)]
    [InlineData("2555", "-1", false)]
    [InlineData("-1", "25600000", true)]          // unlimited meets any finite target
    [InlineData("31457280", "25600000", true)]
    [InlineData("5242880", "25600000", false)]
    [InlineData("true", "true", true)]
    [InlineData("false", "true", false)]
    public void SettingAlreadyAtOrAboveTarget_TreatsMinusOneAsUnlimited(string current, string target, bool expected)
    {
        DataverseWebApiOrgSettingsContractApplier.SettingAlreadyAtOrAboveTarget(current, target).Should().Be(expected);
    }

    [Fact]
    public async Task ApplyAsync_GetRefused_FailsAndPatchesNothing()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Forbidden,
            """{"error":{"code":"0x80040220","message":"Principal user is missing prvReadOrganization privilege"}}"""));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("403").And.Contain("prvReadOrganization").And.Contain("Nothing was applied");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ApplyAsync_PatchRefused_FailsNamingTheSettingNotApplied()
    {
        var handler = new FakeHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, OrganizationJson(5242880, "true", "-1"))
            : Json(HttpStatusCode.Forbidden, """{"error":{"code":"0x80040220","message":"missing prvWriteOrganization"}}"""));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("PATCH").And.Contain("prvWriteOrganization").And.Contain("maxuploadfilesize=25600000");
    }

    // ---------- T186 audit: first-use propagation (Entra FIC / Dataverse application user) ----------

    private static WorkerCredentialSelectionOptions Propagation(TimeSpan budget) => new()
    {
        EntraPropagationTimeout = budget,
        EntraPropagationRetryInterval = TimeSpan.FromMilliseconds(5),
        DataverseAppUserPropagationTimeout = budget,
        DataverseAppUserPropagationRetryInterval = TimeSpan.FromMilliseconds(5),
    };

    [Fact]
    public async Task ApplyAsync_FirstTokenExchangeHitsEntraPropagation_IsWaitedOut_ThenApplies()
    {
        var credential = new FlakyCredential(failures: 2, "AADSTS70025: The client has no configured federated identity credentials.");
        var handler = new FakeHandler(OrganizationRow(maxUpload: 31457280));

        var outcome = await NewApplier(handler, propagation: Propagation(TimeSpan.FromSeconds(10)), credential: credential)
            .ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>();
        credential.Calls.Should().Be(3);
    }

    [Fact]
    public async Task ApplyAsync_UnrelatedAadstsCode_IsNotRetried_FailsWithNoHttpCall()
    {
        var credential = new FlakyCredential(failures: int.MaxValue, "AADSTS7000215: Invalid client secret provided.");
        var handler = new FakeHandler(OrganizationRow(maxUpload: 5242880));

        var outcome = await NewApplier(handler, propagation: Propagation(TimeSpan.FromSeconds(10)), credential: credential)
            .ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("Token acquisition").And.Contain("AADSTS7000215");
        credential.Calls.Should().Be(1);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_FirstGetRefusedWhileTheAppUserPropagates_IsWaitedOut_ThenApplies()
    {
        var gets = 0;
        var row = OrganizationRow(maxUpload: 5242880);
        var handler = new FakeHandler(request => request.Method == HttpMethod.Get && ++gets <= 2
            ? Json(HttpStatusCode.Forbidden, """{"error":{"message":"principal not found"}}""")
            : row(request));

        var outcome = await NewApplier(handler, propagation: Propagation(TimeSpan.FromSeconds(10)))
            .ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>();
        gets.Should().Be(4, "two refused GETs, the read, then the read-back");
    }

    [Fact]
    public async Task ApplyAsync_FirstGetForbiddenAfterTheBudget_KeepsTheFailureAndPatchesNothing()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Forbidden,
            """{"error":{"message":"Principal user is missing prvReadOrganization privilege"}}"""));

        var outcome = await NewApplier(handler, propagation: Propagation(TimeSpan.FromMilliseconds(80)))
            .ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("403").And.Contain("prvReadOrganization").And.Contain("Nothing was applied");
        handler.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get).And.HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task ApplyAsync_AColumnNameWithATrailingNewline_FailsWithNoHttpCall()
    {
        // #2253 fix round 3 (K): `$` matched before a final "\n"; `\z` does not, so the name never reaches $select.
        var handler = new FakeHandler(OrganizationRow(maxUpload: 5242880));
        var request = NewRequest() with
        {
            OrgSettings = new Dictionary<string, string> { ["maxuploadfilesize\n"] = "25600000" },
        };

        var outcome = await NewApplier(handler).ApplyAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("not an organization column name");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_TokenAcquisitionFails_FailsWithNoHttpCall()
    {
        var handler = new FakeHandler(OrganizationRow(maxUpload: 5242880));

        var outcome = await NewApplier(handler, throwingCredential: true).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("Token acquisition").And.Contain("Nothing was applied");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_ResponseHasNoOrganizationRow_FailsWithoutPatching()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{"value":[]}"""));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("exactly one row");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ApplyAsync_ContractNamesANonColumn_FailsBeforeAnyCall()
    {
        var handler = new FakeHandler(OrganizationRow(maxUpload: 5242880));
        var request = NewRequest() with
        {
            OrgSettings = new Dictionary<string, string> { ["maxuploadfilesize,name"] = "1" },
        };

        var outcome = await NewApplier(handler).ApplyAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("not an organization column name");
        handler.Requests.Should().BeEmpty("a bad name never reaches $select or a PATCH body");
    }

    [Theory]
    [InlineData("25600000", "maxuploadfilesize", 25600000L, JsonValueKind.Number)]
    [InlineData("true", "isauditenabled", null, JsonValueKind.True)]
    [InlineData("en-US", "someculturesetting", null, JsonValueKind.String)]
    public void BuildPatchBody_WritesEachValueAsItsJsonType(string value, string name, long? number, JsonValueKind kind)
    {
        var body = DataverseWebApiOrgSettingsContractApplier.BuildPatchBody(new Dictionary<string, string> { [name] = value });

        using var document = JsonDocument.Parse(body);
        var element = document.RootElement.GetProperty(name);
        element.ValueKind.Should().Be(kind);
        if (number is not null)
        {
            element.GetInt64().Should().Be(number);
        }
    }

    // ---------- helpers ----------

    private static OrgSettingsContractApplyRequest NewRequest() => new(
        TenantId: TenantId,
        ClientId: ClientId,
        ClientSecret: null,
        TargetDataverseUrl: EnvUrl,
        OrgSettings: StaticOrgSettingsContractManifest.DefaultOrgSettings);

    private static DataverseWebApiOrgSettingsContractApplier NewApplier(
        FakeHandler handler, bool throwingCredential = false, WorkerCredentialSelectionOptions? propagation = null,
        TokenCredential? credential = null)
        => new(
            new HttpClient(handler),
            NullLogger<DataverseWebApiOrgSettingsContractApplier>.Instance,
            (_, _, _) => credential ?? (throwingCredential ? new ThrowingCredential() : new FakeCredential()),
            // T186 audit: the propagation waits are OFF unless a test opts in.
            propagation ?? new WorkerCredentialSelectionOptions
            {
                EntraPropagationTimeout = TimeSpan.Zero,
                DataverseAppUserPropagationTimeout = TimeSpan.Zero,
            });

    /// <summary>
    /// A stateful organization row: GET returns it, PATCH merges its body into it (unless <paramref name="patchTakes"/>
    /// is false — Dataverse ignoring the column while still answering 204). Audit defaults are already satisfied so a
    /// test about one setting sees only that setting move. Values are raw JSON (<c>"null"</c> for a null column).
    /// </summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> OrganizationRow(
        long maxUpload, string auditEnabled = "true", string retention = "-1", bool patchTakes = true)
    {
        var state = Columns(maxUpload, auditEnabled, retention);
        return request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, OrganizationJson(state));
            }
            if (patchTakes)
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                foreach (var property in body.RootElement.EnumerateObject())
                {
                    state[property.Name] = property.Value.GetRawText();
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        };
    }

    private static Dictionary<string, string> Columns(long maxUpload, string auditEnabled, string retention) => new(StringComparer.Ordinal)
    {
        ["maxuploadfilesize"] = maxUpload.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["isauditenabled"] = auditEnabled,
        ["auditretentionperiodv2"] = retention,
    };

    private static string OrganizationJson(long maxUpload, string auditEnabled, string retention)
        => OrganizationJson(Columns(maxUpload, auditEnabled, retention));

    private static string OrganizationJson(IReadOnlyDictionary<string, string> columns)
    {
        var fields = string.Join(",", columns.Select(kv => "\"" + kv.Key + "\":" + kv.Value));
        return "{\"value\":[{\"organizationid\":\"" + OrganizationId + "\"," + fields + "}]}";
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record Captured(HttpMethod Method, Uri Uri, string? Authorization, string? IfMatch, string? Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<Captured> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Captured(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("If-Match", out var ifMatch) ? ifMatch.Single() : null,
                body));
            return _respond(request);
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-dataverse-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class FlakyCredential(int failures, string message) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            return Calls <= failures
                ? throw new Azure.Identity.AuthenticationFailedException(message)
                : new AccessToken("fake-dataverse-token", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class ThrowingCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential chain failure");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential chain failure");
    }
}
