// CONTRACT — spaarke_get_matter_finance on POST /mcp/legal-operations (spaarkeai-mcp-bff-api-r2 task 033; spec FR-11, NFR-02, NFR-05; ADR-015, ADR-028 A10).
// KEEP path: endpoint-contract.
//
// WHAT RUNS: the REAL BFF host (Program, through McpProgramHostFactory: production AddMcpModule, FinanceModule, the A10 route filter, the
// per-tool policy, the finance route's own filters and AuthorizationService) over module-boundary doubles only: the systemuser lookup
// (StatedCaller), the rights source (IAccessDataSource), the privilege probe and IFinanceSummaryService. The tool and
// GET /api/finance/matters/{matterId}/summary are both called over HTTP with the SAME token on the SAME host, so "same reasonCode" and
// "same figures" compare two real answers (the replay of task 008's route-vs-method test, here for the tool).
// NOT HERE: every refusal case of the shared method (GetMatterFinanceToolAuthTests, MatterFinanceReadSharedMethodTests); the MCP protocol
// suite (the mcp-conformance CI job).

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Finance;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Api.Finance;
using Sprk.Bff.Api.Tests.Api.Mcp.Portfolio;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Mcp.FinanceTool;

public sealed class GetMatterFinanceToolContractTests : IClassFixture<FinanceProgramHostFactory>
{
    private static readonly Guid Oid = Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId);

    private readonly FinanceProgramHostFactory _factory;

    public GetMatterFinanceToolContractTests(FinanceProgramHostFactory factory) => _factory = factory;

    // ---- tools/list -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_GetMatterFinance_IsReadOnly_Idempotent_ModelVisible_WithNoWidget_AndAClosedSchemaRequiringMatterId()
    {
        using var client = _factory.CreateClient();

        var tools = await McpCall.ListToolsAsync(client, McpProgramHostFactory.MintToken(Oid));

        var tool = tools[GetMatterFinanceTool.ToolName];
        tool.GetProperty("title").GetString().Should().Be("Get a matter's finance position");
        var description = tool.GetProperty("description").GetString()!;
        description.Length.Should().BeLessThanOrEqualTo(1024);
        description.Should().Contain("Use for:").And.Contain("Not for:");
        var annotations = tool.GetProperty("annotations");
        annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
        annotations.GetProperty("idempotentHint").GetBoolean().Should().BeTrue();
        (tool.TryGetProperty("_meta", out var meta) && meta.TryGetProperty("ui", out _)).Should().BeFalse("no widget: model-visible by default");

        var schema = tool.GetProperty("inputSchema");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal("matterId");
        schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).Should().Equal("matterId");
    }

    // ---- allowed: the tool's figures are the route's -----------------------------------------------------------------------------------

    [Fact]
    public async Task GetMatterFinance_ForAnAllowedCaller_ReturnsTheRoutesFigures_AsTotalsOnly_UnderTheSizeLimit()
    {
        using var client = _factory.CreateClient();
        var token = McpProgramHostFactory.MintToken(Oid);
        var matterId = Guid.NewGuid();
        _factory.Arrange(StepZeroCallerKind.FlagFalse);
        _factory.Access.Grant(FinanceAuthorizationFilter.MatterEntitySet, matterId, AccessRights.Read);
        var dto = new FinanceSummaryDto
        {
            MatterId = matterId,
            CurrentSpend = 81250.50m,
            Budget = 100000m,
            BudgetVariance = 18749.50m,
            VelocityPct = 12.5m,
            GeneratedAt = new DateTime(2026, 11, 20, 8, 30, 0, DateTimeKind.Utc),
            ActiveSignals = Enumerable.Range(0, 300).Select(i => new SpendSignalDto { SignalType = "BudgetWarning", Severity = "Warning", Message = "SECRET narrative " + i }).ToList(),
            RecentInvoices = [new RecentInvoiceDto { InvoiceId = Guid.NewGuid(), InvoiceNumber = "INV-SECRET-0042", TotalAmount = 9999.99m }],
        };
        _factory.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var tool = await McpCall.CallAsync(client, GetMatterFinanceTool.ToolName, new { matterId }, token);
        using var rest = await client.SendAsync(Rest(matterId, token));
        var restBody = JsonDocument.Parse(await rest.Content.ReadAsStringAsync()).RootElement;

        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        tool.TryGetProperty("isError", out var isError).Should().BeFalse("a success result carries no isError (got " + isError + ")");
        var structured = tool.GetProperty("structuredContent");
        foreach (var figure in new[] { "matterId", "currentSpend", "budget", "budgetVariance", "velocityPct", "generatedAt" })
        {
            structured.GetProperty(figure).GetRawText().Should().Be(restBody.GetProperty(figure).GetRawText(), $"FR-11: '{figure}' is the route's figure");
        }

        structured.GetProperty("currentSpend").GetDecimal().Should().Be(81250.50m);
        var raw = tool.GetRawText();
        raw.Should().NotContain("SECRET").And.NotContain("INV-SECRET-0042", "ADR-015: totals only");
        Encoding.UTF8.GetByteCount(raw).Should().BeLessThan(64 * 1024, "NFR-02: 300 signals in, a bounded result out");
        tool.GetProperty("content").EnumerateArray().Single().GetProperty("text").GetString().Should().Contain("81,250.50").And.Contain("100,000.00");
    }

    // ---- review F1: the budget sentence says what the figures say -----------------------------------------------------------------------

    // The service reports a matter with no budget record as Budget 0 and BudgetVariance 0 (FinanceSummaryService), so a variance of 0 beside a
    // budget of 0 is "no budget", never "under budget". Spend and budget are strings because a decimal is not an attribute argument.
    [Theory]
    [InlineData("5000", "0", "0", "No budget is recorded for this matter.", "Budget variance")]
    [InlineData("120000", "100000", "-20000", "Budget variance: -20,000.00 (over budget)", "under budget")]
    [InlineData("100000", "100000", "0", "Budget variance: 0.00 (on budget)", "under budget")]
    [InlineData("81250.50", "100000", "18749.50", "Budget variance: 18,749.50 (under budget)", "over budget")]
    public async Task GetMatterFinance_BudgetSentence_FollowsTheFigures_AndStructuredContentStaysRaw(
        string spend, string budget, string variance, string expectedLine, string forbiddenText)
    {
        using var client = _factory.CreateClient();
        var token = McpProgramHostFactory.MintToken(Oid);
        var matterId = Guid.NewGuid();
        _factory.Arrange(StepZeroCallerKind.FlagFalse);
        _factory.Access.Grant(FinanceAuthorizationFilter.MatterEntitySet, matterId, AccessRights.Read);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _factory.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>())).ReturnsAsync(new FinanceSummaryDto
        {
            MatterId = matterId,
            CurrentSpend = decimal.Parse(spend, inv),
            Budget = decimal.Parse(budget, inv),
            BudgetVariance = decimal.Parse(variance, inv),
        });

        var tool = await McpCall.CallAsync(client, GetMatterFinanceTool.ToolName, new { matterId }, token);
        using var rest = await client.SendAsync(Rest(matterId, token));
        var restBody = JsonDocument.Parse(await rest.Content.ReadAsStringAsync()).RootElement;

        var text = tool.GetProperty("content").EnumerateArray().Single().GetProperty("text").GetString()!;
        text.Should().Contain(expectedLine);
        text.Should().NotContainEquivalentOf(forbiddenText);
        var structured = tool.GetProperty("structuredContent");
        foreach (var figure in new[] { "currentSpend", "budget", "budgetVariance" })
        {
            structured.GetProperty(figure).GetRawText().Should().Be(restBody.GetProperty(figure).GetRawText(), $"'{figure}' stays the route's raw figure");
        }

        structured.GetRawText().Should().NotContainEquivalentOf("under budget").And.NotContainEquivalentOf("over budget").And.NotContainEquivalentOf("on budget",
            "the label is words for the text, not a field of the data");
    }

    // ---- review F2: signal type, severity and time are the route's, per signal ------------------------------------------------------------

    [Fact]
    public async Task GetMatterFinance_ActiveSignals_CarryTheRoutesTypeSeverityAndTime_PerSignal()
    {
        using var client = _factory.CreateClient();
        var token = McpProgramHostFactory.MintToken(Oid);
        var matterId = Guid.NewGuid();
        _factory.Arrange(StepZeroCallerKind.FlagFalse);
        _factory.Access.Grant(FinanceAuthorizationFilter.MatterEntitySet, matterId, AccessRights.Read);
        _factory.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>())).ReturnsAsync(new FinanceSummaryDto
        {
            MatterId = matterId,
            CurrentSpend = 10m,
            Budget = 100m,
            BudgetVariance = 90m,
            ActiveSignals =
            [
                new SpendSignalDto { SignalType = "BudgetWarning", Severity = "Warning", Message = "SECRET one", GeneratedAt = new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc) },
                new SpendSignalDto { SignalType = "VelocitySpike", Severity = "Critical", Message = "SECRET two", GeneratedAt = new DateTime(2026, 11, 18, 13, 45, 10, DateTimeKind.Utc) },
            ],
        });

        var tool = await McpCall.CallAsync(client, GetMatterFinanceTool.ToolName, new { matterId }, token);
        using var rest = await client.SendAsync(Rest(matterId, token));
        var restBody = JsonDocument.Parse(await rest.Content.ReadAsStringAsync()).RootElement;

        var toolSignals = tool.GetProperty("structuredContent").GetProperty("activeSignals").EnumerateArray().ToList();
        var restSignals = restBody.GetProperty("activeSignals").EnumerateArray().ToList();
        toolSignals.Should().HaveCount(2).And.HaveCount(restSignals.Count);
        for (var i = 0; i < restSignals.Count; i++)
        {
            foreach (var field in new[] { "signalType", "severity", "generatedAt" })
            {
                toolSignals[i].GetProperty(field).GetRawText().Should().Be(restSignals[i].GetProperty(field).GetRawText(), $"signal {i}: '{field}' is the route's");
            }
        }

        toolSignals[0].GetProperty("severity").GetString().Should().Be("Warning");
        toolSignals[1].GetProperty("severity").GetString().Should().Be("Critical");
        toolSignals[1].GetProperty("signalType").GetString().Should().Be("VelocitySpike");
        tool.GetRawText().Should().NotContain("SECRET");
    }

    // ---- refused: the tool's reasonCode is the route's, on the wire ---------------------------------------------------------------------

    [Theory]
    [InlineData(StepZeroCallerKind.FlagTrue, false, FinanceAuthorizationFilter.InternalCallersOnlyReasonCode)]
    [InlineData(StepZeroCallerKind.NoSystemUserRow, false, FinanceAuthorizationFilter.InternalCallersOnlyReasonCode)] // ADR Path A
    [InlineData(StepZeroCallerKind.LookupFault, false, FinanceAuthorizationFilter.SystemFailureReasonCode)]
    [InlineData(StepZeroCallerKind.FlagReadFault, false, FinanceAuthorizationFilter.SystemFailureReasonCode)]
    [InlineData(StepZeroCallerKind.FlagFalse, false, FinanceAuthorizationFilter.InsufficientRightsReasonCode)] // no Read on the matter
    [InlineData(StepZeroCallerKind.FlagFalse, true, FinanceAuthorizationFilter.InsufficientRightsReasonCode)] // rights, but not Read
    public async Task GetMatterFinance_ForACallerTheRouteRefuses_IsRefusedWithTheSameReasonCode_AndNoSummaryIsRead(
        StepZeroCallerKind kind, bool rightsWithoutRead, string expectedReasonCode)
    {
        using var client = _factory.CreateClient();
        var token = McpProgramHostFactory.MintToken(Oid);
        var matterId = Guid.NewGuid();
        _factory.Arrange(kind);
        if (rightsWithoutRead)
        {
            _factory.Access.Grant(FinanceAuthorizationFilter.MatterEntitySet, matterId, AccessRights.AppendTo);
        }

        var tool = await McpCall.CallAsync(client, GetMatterFinanceTool.ToolName, new { matterId }, token);
        using var rest = await client.SendAsync(Rest(matterId, token));
        var restReasonCode = JsonDocument.Parse(await rest.Content.ReadAsStringAsync()).RootElement.GetProperty("reasonCode").GetString();

        rest.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        tool.GetProperty("isError").GetBoolean().Should().BeTrue();
        var error = tool.GetProperty("structuredContent");
        error.GetProperty("reasonCode").GetString().Should().Be(expectedReasonCode).And.Be(restReasonCode, "FR-11: the route's own reasonCode");
        error.GetProperty("status").GetInt32().Should().Be(403);
        error.TryGetProperty("correlationId", out _).Should().BeTrue("a refusal carries the support reference");
        _factory.Summary.Verify(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- the A10 token gate (K1) -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("scope-missing")]
    // "copilot-audience": the 401 comes from JwtBearer token validation (the audience is not one this host accepts), not from the A10 check; the
    // case proves the same outcome (401, nothing looked up or read), not that the A10 rule fired. "scope-missing" and "app-only" are the A10 rule's.
    [InlineData("copilot-audience")]
    [InlineData("app-only")]
    public async Task AnA10RefusedToken_IsRefused401_BeforeTheTool_SoNothingIsLookedUpOrRead(string kind)
    {
        using var client = _factory.CreateClient();
        var matterId = Guid.NewGuid();
        _factory.Arrange(StepZeroCallerKind.FlagFalse);
        _factory.Access.Grant(FinanceAuthorizationFilter.MatterEntitySet, matterId, AccessRights.Read);
        var lookups = _factory.Lookups;
        var rightsQuestions = _factory.Access.Calls.Count;

        using var request = new HttpRequestMessage(HttpMethod.Post, Sprk.Bff.Api.Api.Mcp.McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = GetMatterFinanceTool.ToolName, arguments = new { matterId } } }),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintRefused(kind));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync());
        _factory.Lookups.Should().Be(lookups, "the shared method never ran: no step-0 lookup");
        _factory.Access.Calls.Count.Should().Be(rightsQuestions, "no rights question");
        _factory.Summary.Verify(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- telemetry (NFR-05) -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetMatterFinance_WritesOneTelemetryLine_WithToolOutcomeAndCorrelation_AndNoAmountOrIdentity()
    {
        var logs = new PortfolioProgramHostFactory.CapturedLogs();
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs)));
        using var client = factory.CreateClient();
        var token = McpProgramHostFactory.MintToken(Oid);
        var allowed = Guid.NewGuid();
        var refused = Guid.NewGuid();
        _factory.Arrange(StepZeroCallerKind.FlagFalse);
        _factory.Access.Grant(FinanceAuthorizationFilter.MatterEntitySet, allowed, AccessRights.Read);
        _factory.Summary.Setup(s => s.GetSummaryAsync(allowed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinanceSummaryDto { MatterId = allowed, CurrentSpend = 81250.50m, Budget = 100000m, BudgetVariance = 18749.50m });

        await McpCall.CallAsync(client, GetMatterFinanceTool.ToolName, new { matterId = allowed }, token);
        await McpCall.CallAsync(client, GetMatterFinanceTool.ToolName, new { matterId = refused }, token);

        var lines = logs.Messages.Where(m => m.Contains("MCP tool spaarke_get_matter_finance completed", StringComparison.Ordinal)).ToList();
        lines.Should().HaveCount(2);
        lines.Should().ContainSingle(l => l.Contains("outcome ok", StringComparison.Ordinal));
        lines.Should().ContainSingle(l => l.Contains("outcome " + FinanceAuthorizationFilter.InsufficientRightsReasonCode, StringComparison.Ordinal));
        lines.Should().OnlyContain(l => l.Contains("correlation", StringComparison.Ordinal) && l.Contains(" ms", StringComparison.Ordinal));
        logs.Messages.Where(m => m.Contains("MCP tool", StringComparison.Ordinal)).Should()
            .OnlyContain(m => !m.Contains("81250") && !m.Contains("81,250") && !m.Contains("100000") && !m.Contains(Oid.ToString()));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------

    private static HttpRequestMessage Rest(Guid matterId, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/finance/matters/{matterId}/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>A token the A10 rule refuses, otherwise valid for this host: signed, in the Spaarke tenant, for a user.</summary>
    private static string MintRefused(string kind)
    {
        var claims = new Dictionary<string, object> { ["oid"] = Oid.ToString(), ["tid"] = McpProgramHostFactory.TenantId };
        var audience = McpProgramHostFactory.BffClientId;
        switch (kind)
        {
            case "scope-missing":
                claims["scp"] = "Files.Read";
                break;
            case "copilot-audience":
                claims["scp"] = "user_impersonation";
                audience = "99999999-8888-4777-8666-555555555555";
                break;
            default: // an app-only token: a role, no delegated scope, idtyp app
                claims["roles"] = "Finance.Read.All";
                claims["idtyp"] = "app";
                break;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = McpProgramHostFactory.Issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(McpProgramHostFactory.SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = claims,
        });
    }
}

/// <summary>
/// TESTS ONLY (task 033). The real BFF host with the module-boundary doubles the finance route's decision reads: the caller's systemuser
/// lookup (<see cref="StatedCaller"/>), the rights source, the privilege probe and the summary service. The step-0 resolvers, the
/// <see cref="AuthorizationService"/>, <c>OperationAccessRule</c>, the route's filters and the tool are production code.
/// </summary>
public sealed class FinanceProgramHostFactory : McpProgramHostFactory
{
    // WithWebHostBuilder builds a second host from this factory's configuration, so ConfigureSeededServices can run more than once: the
    // caller's state is one field every registered lookup reads, and the lookups are counted across all of them.
    private readonly List<StatedCaller> _callers = new();
    private StepZeroCallerKind _kind = StepZeroCallerKind.FlagFalse;

    internal RecordingAccessDataSource Access { get; } = new();

    internal Mock<IFinanceSummaryService> Summary { get; } = new(MockBehavior.Strict);

    /// <summary>How many step-0 lookups (the real resolvers' Dataverse seam) any host built from this factory has made.</summary>
    internal int Lookups
    {
        get
        {
            lock (_callers)
            {
                return _callers.Sum(c => c.Queries.Count);
            }
        }
    }

    /// <summary>States the caller's systemuser row for the next requests. Tests in this class run one at a time.</summary>
    internal void Arrange(StepZeroCallerKind kind) => _kind = kind;

    protected override void ConfigureSeededServices(IServiceCollection services)
    {
        var caller = StatedCaller.Register(services, kindSource: () => _kind);
        lock (_callers)
        {
            _callers.Add(caller);
        }

        services.RemoveAll<IAccessDataSource>();
        services.AddSingleton<IAccessDataSource>(Access);
        services.RemoveAll<CallerRecordAccessProbe>();
        services.AddSingleton<CallerRecordAccessProbe>(new RecordingPrivilegeProbe());
        services.RemoveAll<IFinanceSummaryService>();
        services.AddSingleton(Summary.Object);
    }
}
