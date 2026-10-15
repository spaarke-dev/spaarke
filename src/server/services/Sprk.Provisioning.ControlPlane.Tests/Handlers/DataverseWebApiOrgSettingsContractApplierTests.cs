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
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
        handler.Requests.Should().HaveCount(2);

        var get = handler.Requests[0];
        get.Method.Should().Be(HttpMethod.Get);
        get.Uri.AbsolutePath.Should().Be("/api/data/v9.2/organizations");
        Uri.UnescapeDataString(get.Uri.Query).Should().Contain("$select=organizationid,maxuploadfilesize");
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
            ? Json(HttpStatusCode.OK, OrganizationJson(5242880))
            : Json(HttpStatusCode.Forbidden, """{"error":{"code":"0x80040220","message":"missing prvWriteOrganization"}}"""));

        var outcome = await NewApplier(handler).ApplyAsync(NewRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("PATCH").And.Contain("prvWriteOrganization").And.Contain("maxuploadfilesize=25600000");
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

    private static DataverseWebApiOrgSettingsContractApplier NewApplier(FakeHandler handler, bool throwingCredential = false)
        => new(
            new HttpClient(handler),
            NullLogger<DataverseWebApiOrgSettingsContractApplier>.Instance,
            (_, _, _) => throwingCredential ? new ThrowingCredential() : new FakeCredential());

    private static Func<HttpRequestMessage, HttpResponseMessage> OrganizationRow(long maxUpload)
        => request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, OrganizationJson(maxUpload))
            : new HttpResponseMessage(HttpStatusCode.NoContent);

    private static string OrganizationJson(long maxUpload)
        => $$"""{"value":[{"organizationid":"{{OrganizationId}}","maxuploadfilesize":{{maxUpload}}}]}""";

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

    private sealed class ThrowingCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential chain failure");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential chain failure");
    }
}
