// AUTH — spaarke_get_matter_finance (spaarkeai-mcp-bff-api-r2 task 033; spec FR-11, NFR-05, NFR-07; ADR-015, ADR-028 A10).
// KEEP path: auth.
//
// THE CLAIM. A caller the REST summary route (GET /api/finance/matters/{matterId}/summary) refuses is refused by the tool with the SAME
// reasonCode, and no finance service is called; a caller the route admits gets the route's figures. The tool adds no check of its own: it
// calls FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync (task 008), so these cases prove the tool maps that method's answer
// faithfully, and that nothing model-visible carries what the route's body carries beyond the totals.
//
// WHAT RUNS. Each case calls GetMatterFinanceTool.RunAsync directly (as the SDK's handler does) over the REAL step-0 resolvers, the REAL
// AuthorizationService and OperationAccessRule, substituted only at the Dataverse seams (task 008's harness), then sends the same caller and
// rights to the REAL summary route (FinanceAuthHost) and compares the two reasonCodes. NOT HERE: the MCP wire (tools/list, the A10 token
// gate, the result size, telemetry through the SDK handler): GetMatterFinanceToolContractTests.
//
// ADR Path A (owner decision 2026-11-26, spec ADR Tensions): a caller with no systemuser row gets the route's internal_callers_only, NOT
// UserContextRequired. A caller without Read on the matter gets 403 insufficient_rights, as the route does, not a 404.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ModelContextProtocol.Protocol;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Spaarke.Core.Auth.Rules;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Finance;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.FinanceTool;

public sealed class GetMatterFinanceToolAuthTests
{
    private const string Matters = FinanceAuthorizationFilter.MatterEntitySet;

    /// <summary>Every way the route refuses the summary, each as a caller state and a rights state.</summary>
    public enum Refusal
    {
        FlaggedExternal,
        NoSystemUserRow,
        FlagReadFault,
        LookupFault,
        RightsWithoutRead,
        NoRights,
        NoRightsOnAFault,
        RightsQueryThrows,
        EmptyMatterId,
    }

    // ---- every refusal the route gives ----------------------------------------------------------------------------------------------

    [Theory(DisplayName = "Each caller the REST summary route refuses is an isError result with the route's reasonCode (403, fixed text), and no summary is read")]
    [InlineData(Refusal.FlaggedExternal, FinanceAuthorizationFilter.InternalCallersOnlyReasonCode)]
    [InlineData(Refusal.NoSystemUserRow, FinanceAuthorizationFilter.InternalCallersOnlyReasonCode)] // ADR Path A: not UserContextRequired
    [InlineData(Refusal.FlagReadFault, FinanceAuthorizationFilter.SystemFailureReasonCode)]
    [InlineData(Refusal.LookupFault, FinanceAuthorizationFilter.SystemFailureReasonCode)]
    [InlineData(Refusal.RightsWithoutRead, FinanceAuthorizationFilter.InsufficientRightsReasonCode)]
    [InlineData(Refusal.NoRights, FinanceAuthorizationFilter.InsufficientRightsReasonCode)] // a matter that is absent has no rights: the same answer
    [InlineData(Refusal.NoRightsOnAFault, FinanceAuthorizationFilter.InsufficientRightsReasonCode)]
    [InlineData(Refusal.RightsQueryThrows, FinanceAuthorizationFilter.SystemFailureReasonCode)]
    [InlineData(Refusal.EmptyMatterId, FinanceAuthorizationFilter.NoTargetReasonCode)]
    public async Task EveryRefusalTheRouteGives_IsRefusedByTheTool_WithTheSameReasonCode_AndNoServiceIsCalled(Refusal refusal, string expectedReasonCode)
    {
        var matterId = refusal == Refusal.EmptyMatterId ? Guid.Empty : Guid.NewGuid();
        var world = new ToolWorld(KindOf(refusal));
        Arrange(world.Access, refusal, matterId);

        var result = await world.CallAsync(matterId.ToString());

        result.IsError.Should().BeTrue();
        var body = result.StructuredContent!.Value;
        var toolReasonCode = body.GetProperty("reasonCode").GetString();
        toolReasonCode.Should().Be(expectedReasonCode);
        body.GetProperty("status").GetInt32().Should().Be(403, "every one of these is the route's 403");
        world.Summary.VerifyNoOtherCalls();

        // The route, same caller and rights: the two reasonCodes are equal (FR-11), and the route called no service either.
        var routeReasonCode = await RestReasonCodeAsync(KindOf(refusal), matterId, host => Arrange(host.Access, refusal, matterId));
        toolReasonCode.Should().Be(routeReasonCode, "FR-11: the tool is refused with the reasonCode the route's 403 carries");
    }

    [Theory(DisplayName = "A refusal's text is fixed: it names no matter id, caller id or rights detail")]
    [InlineData(Refusal.FlaggedExternal)]
    [InlineData(Refusal.NoRights)]
    [InlineData(Refusal.RightsQueryThrows)]
    [InlineData(Refusal.EmptyMatterId)]
    public async Task ARefusal_CarriesFixedText_NamingNoId(Refusal refusal)
    {
        var matterId = refusal == Refusal.EmptyMatterId ? Guid.Empty : Guid.Parse("0330aaaa-0000-4000-8000-000000000001");
        var world = new ToolWorld(KindOf(refusal));
        Arrange(world.Access, refusal, matterId);

        var result = await world.CallAsync(matterId.ToString());

        var raw = result.StructuredContent!.Value.GetRawText() + string.Join(' ', result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        raw.Should().NotContainEquivalentOf(matterId.ToString(), "ADR-015: a refusal names no id");
        raw.Should().NotContain(FinanceAuthzTestAuthHandler.CallerObjectId, "a refusal does not echo the caller's oid");
        raw.Should().NotContain("RetrievePrincipalAccess", "an exception's text never reaches the model");
    }

    // ---- allowed ---------------------------------------------------------------------------------------------------------------------

    [Theory(DisplayName = "An internal caller with Read on the matter gets the route's figures, and the one summary read is for that matter")]
    [InlineData(StepZeroCallerKind.FlagFalse)]
    [InlineData(StepZeroCallerKind.FlagBlank)]
    public async Task AnAllowedCaller_GetsTheRoutesFigures(StepZeroCallerKind kind)
    {
        var matterId = Guid.NewGuid();
        var dto = Summary(matterId);
        var world = new ToolWorld(kind);
        world.Access.Grant(Matters, matterId, AccessRights.Read);
        world.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await world.CallAsync(matterId.ToString());

        result.IsError.Should().NotBe(true);
        world.Summary.Verify(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>()), Times.Once);
        world.Access.Calls.Should().ContainSingle().Which.Should().Be(new AccessCall(AccessPath.Record, Matters, matterId, HasToken: true));

        await using var host = await FinanceAuthHost.StartAsync();
        host.StepZero.Kind = kind;
        host.Access.Grant(Matters, matterId, AccessRights.Read);
        host.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        using var rest = await host.SendAsync(SummaryRequest(matterId));
        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        var restBody = JsonNode.Parse(await rest.Content.ReadAsStringAsync())!;

        var tool = JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!;
        foreach (var figure in new[] { "matterId", "currentSpend", "budget", "budgetVariance", "velocityPct", "generatedAt" })
        {
            JsonNode.DeepEquals(tool[figure], restBody[figure]).Should().BeTrue($"'{figure}' is the route's figure: tool {tool[figure]} vs route {restBody[figure]}");
        }

        tool["activeSignals"]!.AsArray().Select(s => s!["signalType"]!.GetValue<string>())
            .Should().Equal(restBody["activeSignals"]!.AsArray().Select(s => s!["signalType"]!.GetValue<string>()));
    }

    [Fact(DisplayName = "A matter with no financial data is an isError not-found (the route's 404), not an empty success")]
    public async Task ANullSummary_IsANotFoundError()
    {
        var matterId = Guid.NewGuid();
        var world = new ToolWorld(StepZeroCallerKind.FlagFalse);
        world.Access.Grant(Matters, matterId, AccessRights.Read);
        world.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>())).ReturnsAsync((FinanceSummaryDto?)null);

        var result = await world.CallAsync(matterId.ToString());

        result.IsError.Should().BeTrue();
        result.StructuredContent!.Value.GetProperty("reasonCode").GetString().Should().Be(GetMatterFinanceTool.NotFoundReasonCode);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().Should().Be(404);
    }

    // ---- no caller -----------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "No request context is UserContextRequired (401), with nothing read")]
    public async Task NoRequestContext_IsUserContextRequired_AndNothingIsRead()
    {
        var summary = new Mock<IFinanceSummaryService>(MockBehavior.Strict);

        var result = await GetMatterFinanceTool.RunAsync(Args(Guid.NewGuid().ToString()), null, summary.Object, NullLogger.Instance, CancellationToken.None);

        AssertUserContextRequired(result);
        summary.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "An unauthenticated principal, or an authenticated one with no Entra oid, is UserContextRequired with no step-0 lookup, no rights question and no summary read")]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task NoSignedInCaller_IsUserContextRequired_AndNothingIsRead(bool authenticated, bool withOid)
    {
        var matterId = Guid.NewGuid();
        var world = new ToolWorld(StepZeroCallerKind.FlagFalse);
        world.Access.Grant(Matters, matterId, AccessRights.Read);
        var claims = withOid ? new[] { new Claim("oid", FinanceAuthzTestAuthHandler.CallerObjectId) } : Array.Empty<Claim>();

        var result = await world.CallAsync(matterId.ToString(), new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null)));

        AssertUserContextRequired(result);
        world.Caller.Queries.Should().BeEmpty("no systemuser lookup runs without a signed-in caller");
        world.Access.Calls.Should().BeEmpty("no rights question runs without a signed-in caller");
        world.Summary.VerifyNoOtherCalls();
    }

    // ---- arguments -----------------------------------------------------------------------------------------------------------------

    [Theory(DisplayName = "A call that is not exactly one matterId string that is a GUID is an isError argument error, and reads nothing")]
    [InlineData("none")]
    [InlineData("extra")]
    [InlineData("number")]
    [InlineData("blank")]
    [InlineData("not-a-guid")]
    public async Task ABadArgument_IsAnIsErrorResult_AndReadsNothing(string kind)
    {
        var world = new ToolWorld(StepZeroCallerKind.FlagFalse);
        IDictionary<string, JsonElement>? args = kind switch
        {
            "none" => null,
            "extra" => Args(Guid.NewGuid().ToString(), ("amount", "1")),
            "number" => new Dictionary<string, JsonElement> { ["matterId"] = JsonSerializer.SerializeToElement(5) },
            "blank" => Args("  "),
            _ => Args("not-a-guid"),
        };

        var result = await world.RunAsync(args);

        result.IsError.Should().BeTrue();
        result.StructuredContent!.Value.GetProperty("status").GetInt32().Should().Be(400);
        world.Caller.Queries.Should().BeEmpty();
        world.Access.Calls.Should().BeEmpty();
        world.Summary.VerifyNoOtherCalls();
    }

    // ---- service faults ------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "A summary fault is an isError result (the route's 500), its text never reaches the model, and the log carries the exception type only")]
    public async Task AServiceFault_IsAnIsErrorResult_LeakingNoExceptionText()
    {
        var matterId = Guid.NewGuid();
        var world = new ToolWorld(StepZeroCallerKind.FlagFalse);
        world.Access.Grant(Matters, matterId, AccessRights.Read);
        world.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse-exploded-spend-4,821,330.17"));
        var logger = new ListLogger();

        var result = await world.CallAsync(matterId.ToString(), logger: logger);

        result.IsError.Should().BeTrue();
        result.StructuredContent!.Value.GetProperty("reasonCode").GetString().Should().Be(GetMatterFinanceTool.UnavailableReasonCode);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().Should().Be(500);
        result.StructuredContent!.Value.GetRawText().Should().NotContain("dataverse-exploded");
        logger.Lines.Should().NotBeEmpty().And.OnlyContain(l => !l.Contains("dataverse-exploded") && !l.Contains("4,821,330"));
        logger.Lines.Should().Contain(l => l.Contains(nameof(InvalidOperationException)));
    }

    [Fact(DisplayName = "A call the caller abandoned is not a fault: cancellation propagates")]
    public async Task ACancelledCall_Propagates()
    {
        var matterId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var world = new ToolWorld(StepZeroCallerKind.FlagFalse);
        world.Access.Grant(Matters, matterId, AccessRights.Read);
        world.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((_, _) =>
            {
                cts.Cancel();
                return Task.FromException<FinanceSummaryDto?>(new OperationCanceledException(cts.Token));
            });

        var act = () => world.CallAsync(matterId.ToString(), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- must not leak ---------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "MUST NOT LEAK: nothing the route's body carries beyond the totals reaches the model (signal message, invoice rows, vendor, invoice numbers)")]
    public async Task TheModelVisibleResult_CarriesTotalsOnly()
    {
        var matterId = Guid.NewGuid();
        var vendorId = Guid.Parse("0330bbbb-0000-4000-8000-0000000000aa");
        var dto = Summary(matterId) with
        {
            ActiveSignals =
            [
                new SpendSignalDto { SignalType = "BudgetWarning", Severity = "Warning", Message = "SECRET-SIGNAL-NARRATIVE Acme v. Globex budget at 91%", GeneratedAt = DateTime.UtcNow },
            ],
            RecentInvoices =
            [
                new RecentInvoiceDto { InvoiceId = Guid.NewGuid(), InvoiceNumber = "INV-SECRET-0042", TotalAmount = 9999.99m, VendorOrgId = vendorId, Status = 1 },
            ],
        };
        var world = new ToolWorld(StepZeroCallerKind.FlagFalse);
        world.Access.Grant(Matters, matterId, AccessRights.Read);
        world.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await world.CallAsync(matterId.ToString());

        result.IsError.Should().NotBe(true);
        var raw = result.StructuredContent!.Value.GetRawText() + "\n" + string.Join('\n', result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        raw.Should().NotContain("SECRET-SIGNAL-NARRATIVE").And.NotContain("INV-SECRET-0042").And.NotContain(vendorId.ToString())
            .And.NotContain("9999.99").And.NotContainEquivalentOf("recentInvoices").And.NotContainEquivalentOf("message");
        result.StructuredContent!.Value.GetProperty("activeSignals").EnumerateArray().Single().GetProperty("signalType").GetString().Should().Be("BudgetWarning");
    }

    [Fact(DisplayName = "MUST NOT LEAK: the signals list is bounded")]
    public async Task TheSignalsList_IsBounded()
    {
        var matterId = Guid.NewGuid();
        var dto = Summary(matterId) with
        {
            ActiveSignals = Enumerable.Range(0, 500).Select(_ => new SpendSignalDto { SignalType = "VelocitySpike", Severity = "Info", Message = "m" }).ToList(),
        };
        var world = new ToolWorld(StepZeroCallerKind.FlagFalse);
        world.Access.Grant(Matters, matterId, AccessRights.Read);
        world.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await world.CallAsync(matterId.ToString());

        result.StructuredContent!.Value.GetProperty("activeSignals").GetArrayLength().Should().Be(GetMatterFinanceTool.MaxSignals);
        result.StructuredContent!.Value.GetRawText().Length.Should().BeLessThan(64 * 1024, "NFR-02");
    }

    // ---- EB-D-194 ---------------------------------------------------------------------------------------------------------------------

    // An ALLOWLIST, not a list of banned words: the Finance MCP folder may name three types of Services.Finance (the summary service, its
    // DTO and its signal row), so every other Services.Finance type, and any Invoice* name, fails, however the invoice workflow is spelled.
    // Comments are not scanned: a remark that names a type is not a reference to it.

    private static readonly string[] AllowedFinanceServiceTypes = ["IFinanceSummaryService", "FinanceSummaryDto", "SpendSignalDto"];

    [Fact(DisplayName = "EB-D-194: each source file of the Finance MCP folder references only the summary service, its DTO and its signal row from Services.Finance")]
    public void TheFinanceFolder_ReferencesOnlyTheAllowedFinanceTypes()
    {
        var folder = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Api", "Mcp", "LegalOperations", "Finance");
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
        files.Should().NotBeEmpty("the scan must see the tool");
        var financeTypes = FinanceServiceTypes();
        financeTypes.Should().Contain(["InvoiceLineDecisionService", "InvoiceRejectReturnService", "InvoiceApproveRecord", "IFinanceSummaryService"],
            "the list of Services.Finance types is read from the folder and must include the types the invoice workflow lives in");

        foreach (var file in files)
        {
            FinanceFolderViolations(File.ReadAllText(file), financeTypes)
                .Should().BeEmpty($"EB-D-194: {Path.GetFileName(file)} may reference only {string.Join(", ", AllowedFinanceServiceTypes)} from Services.Finance");
        }
    }

    [Theory(DisplayName = "EB-D-194 must-fire control: the matcher catches an invoice-workflow type in a seeded source")]
    [InlineData("InvoiceLineDecisionService")]
    [InlineData("InvoiceRejectReturnService")]
    [InlineData("InvoiceDecisionOutcome")]
    [InlineData("InvoiceApproveRecord")]
    [InlineData("InvoiceCorrectionService")]
    [InlineData("InvoiceAdjustmentService")]
    [InlineData("IInvoiceReviewService")]
    [InlineData("InvoiceNotInTheFolderYet")] // an Invoice* name is refused even when no such type exists today
    public void TheMatcher_CatchesAnInvoiceWorkflowType(string typeName)
    {
        var seeded = $"using Sprk.Bff.Api.Services.Finance;\ninternal sealed class T {{ private readonly {typeName} _x; }}";

        FinanceFolderViolations(seeded, FinanceServiceTypes()).Should().Contain(typeName);
    }

    [Theory(DisplayName = "EB-D-194 must-fire control: any other Services.Finance type, qualified or not, and a sub-namespace import are caught")]
    [InlineData("internal sealed class T { private readonly FinanceSummaryService _x; }", "FinanceSummaryService")]
    [InlineData("internal sealed class T { private readonly Sprk.Bff.Api.Services.Finance.BillingDecisionRecorder _x; }", "BillingDecisionRecorder")]
    [InlineData("using Sprk.Bff.Api.Services.Finance.Procedures;", "Services.Finance.Procedures")]
    public void TheMatcher_CatchesAnyOtherFinanceServiceReference(string seeded, string expected) =>
        FinanceFolderViolations(seeded, FinanceServiceTypes()).Should().Contain(expected);

    [Fact(DisplayName = "EB-D-194 must-not-fire control: the allowed types, and a comment that names a forbidden one, are not caught")]
    public void TheMatcher_DoesNotCatchTheAllowedTypes_OrAComment()
    {
        var seeded = """
            using Sprk.Bff.Api.Services.Finance;
            // InvoiceLineDecisionService is not used here.
            /// <summary>Not <c>InvoiceRejectReturnService</c>.</summary>
            internal sealed class T
            {
                private readonly IFinanceSummaryService _service;
                private FinanceSummaryDto? _dto;
                private SpendSignalDto? _signal;
                private readonly string _s = "ok";
            }
            """;

        FinanceFolderViolations(seeded, FinanceServiceTypes()).Should().BeEmpty();
        // A "//" inside a string literal is not a comment: what follows it is still source text.
        FinanceFolderViolations("var s = \"// InvoiceApproveRecord\";", FinanceServiceTypes()).Should().Equal("InvoiceApproveRecord");
    }

    /// <summary>Every top-level type declared in <c>Services/Finance</c> (file-scoped namespaces put them at column 0), by name.</summary>
    private static HashSet<string> FinanceServiceTypes()
    {
        var folder = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Finance");
        var declaration = new System.Text.RegularExpressions.Regex(
            @"^(?:(?:public|internal|sealed|static|abstract|partial|readonly|file)\s+)*(?:class|record(?:\s+struct)?|interface|enum|struct)\s+([A-Za-z_]\w*)",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            foreach (System.Text.RegularExpressions.Match m in declaration.Matches(File.ReadAllText(file)))
            {
                names.Add(m.Groups[1].Value);
            }
        }

        return names;
    }

    /// <summary>
    /// The names in <paramref name="source"/> (comments removed) that the Finance MCP folder may not use: an <c>Invoice*</c> name, a
    /// <c>Services.Finance</c> sub-namespace, or a top-level type of <c>Services/Finance</c> other than <see cref="AllowedFinanceServiceTypes"/>.
    /// </summary>
    private static List<string> FinanceFolderViolations(string source, HashSet<string> financeServiceTypes)
    {
        // A string literal is kept whole (so a "//" inside it is not a comment); a comment is dropped.
        var code = System.Text.RegularExpressions.Regex.Replace(
            source, @"""(?:[^""\\\r\n]|\\.)*""|//[^\r\n]*|/\*.*?\*/",
            m => m.Value.StartsWith('"') ? m.Value : " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(code, @"\b[A-Za-z_]\w*\b"))
        {
            var id = m.Value;
            if (AllowedFinanceServiceTypes.Contains(id, StringComparer.Ordinal))
            {
                continue;
            }

            if (financeServiceTypes.Contains(id) || System.Text.RegularExpressions.Regex.IsMatch(id, "^I?Invoice"))
            {
                found.Add(id);
            }
        }

        // A sub-namespace import ("Services.Finance.Procedures"); a qualified type name is already reported as the type above.
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(code, @"Services\.Finance\.([A-Za-z_]\w*)"))
        {
            if (!financeServiceTypes.Contains(m.Groups[1].Value))
            {
                found.Add(m.Value);
            }
        }

        return found.ToList();
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------

    private static StepZeroCallerKind KindOf(Refusal refusal) => refusal switch
    {
        Refusal.FlaggedExternal => StepZeroCallerKind.FlagTrue,
        Refusal.NoSystemUserRow => StepZeroCallerKind.NoSystemUserRow,
        Refusal.FlagReadFault => StepZeroCallerKind.FlagReadFault,
        Refusal.LookupFault => StepZeroCallerKind.LookupFault,
        _ => StepZeroCallerKind.FlagFalse,
    };

    /// <summary>The rights state of a case. A step-0 case grants Read, so only step 0 can refuse.</summary>
    private static void Arrange(RecordingAccessDataSource access, Refusal refusal, Guid matterId)
    {
        switch (refusal)
        {
            case Refusal.RightsWithoutRead:
                access.Grant(Matters, matterId, AccessRights.AppendTo);
                break;
            case Refusal.NoRightsOnAFault:
                access.Grant(Matters, matterId, AccessRights.AppendTo);
                access.Fault(Matters, matterId);
                break;
            case Refusal.RightsQueryThrows:
                access.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess unavailable");
                break;
            case Refusal.NoRights:
            case Refusal.EmptyMatterId:
                break;
            default:
                access.Grant(Matters, matterId, AccessRights.Read);
                break;
        }
    }

    private static FinanceSummaryDto Summary(Guid matterId) => new()
    {
        MatterId = matterId,
        CurrentSpend = 81250.50m,
        Budget = 100000m,
        BudgetVariance = 18749.50m,
        VelocityPct = 12.5m,
        GeneratedAt = new DateTime(2026, 11, 20, 8, 30, 0, DateTimeKind.Utc),
        ActiveSignals = [new SpendSignalDto { SignalType = "BudgetWarning", Severity = "Warning", Message = "m", GeneratedAt = new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc) }],
    };

    private static void AssertUserContextRequired(CallToolResult result)
    {
        result.IsError.Should().BeTrue();
        var body = result.StructuredContent!.Value;
        body.GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        body.GetProperty("status").GetInt32().Should().Be(401);
    }

    private static IDictionary<string, JsonElement> Args(string matterId, (string Name, string Value)? extra = null)
    {
        var args = new Dictionary<string, JsonElement> { ["matterId"] = JsonSerializer.SerializeToElement(matterId) };
        if (extra is { } e)
        {
            args[e.Name] = JsonSerializer.SerializeToElement(e.Value);
        }

        return args;
    }

    /// <summary>The <c>reasonCode</c> the REAL summary route returns for the same caller and rights (a 403; no summary service is called).</summary>
    private static async Task<string?> RestReasonCodeAsync(StepZeroCallerKind kind, Guid matterId, Action<FinanceAuthHost> arrange)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        host.StepZero.Kind = kind;
        arrange(host);

        using var response = await host.SendAsync(SummaryRequest(matterId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyNoServiceWasInvoked();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]?.GetValue<string>();
    }

    private static HttpRequestMessage SummaryRequest(Guid matterId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/finance/matters/{matterId}/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        return request;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// The tool's world: a request context as the SDK's handler gets it (the caller's principal and bearer token; request services carrying the
    /// REAL step-0 resolvers, <see cref="AuthorizationService"/> and <see cref="OperationAccessRule"/> over the recording rights source) and a
    /// strict summary service, so a call the tool should not make fails the case.
    /// </summary>
    private sealed class ToolWorld
    {
        private readonly IServiceProvider _services;

        public ToolWorld(StepZeroCallerKind kind)
        {
            var services = new ServiceCollection().AddLogging();
            Caller = StatedCaller.Register(services, kind);
            services.AddSingleton<IAccessDataSource>(Access);
            services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            services.AddScoped<AuthorizationService>();
            services.AddSingleton<CallerRecordAccessProbe>(new RecordingPrivilegeProbe());
            _services = services.BuildServiceProvider().CreateScope().ServiceProvider;
        }

        public StatedCaller Caller { get; }

        public RecordingAccessDataSource Access { get; } = new();

        public Mock<IFinanceSummaryService> Summary { get; } = new(MockBehavior.Strict);

        public Task<CallToolResult> CallAsync(string matterId, ClaimsPrincipal? principal = null, ILogger? logger = null, CancellationToken ct = default) =>
            RunAsync(Args(matterId), principal, logger, ct);

        public Task<CallToolResult> RunAsync(
            IDictionary<string, JsonElement>? arguments, ClaimsPrincipal? principal = null, ILogger? logger = null, CancellationToken ct = default)
        {
            var http = new DefaultHttpContext
            {
                RequestServices = _services,
                User = principal ?? new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", FinanceAuthzTestAuthHandler.CallerObjectId) }, "Test")),
            };
            http.Request.Headers.Authorization = "Bearer caller-token";
            return GetMatterFinanceTool.RunAsync(arguments, http, Summary.Object, logger ?? NullLogger.Instance, ct);
        }
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " EX:" + exception.GetType().Name + exception.Message));
    }
}
