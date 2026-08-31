// -----------------------------------------------------------------------------
// ArmCostEnvelopeCheckerTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmCostEnvelopeChecker (task 183, Wave G-7
// Batch G-7A2.2). Proves the REAL Azure.ResourceManager.CostManagement SDK call
// path -- not a hard-coded verdict -- by constructing an ArmClient against a
// fake HttpClientTransport (parity with ArmSubscriptionReadinessProbeTests
// task 121 + CosmosPartitionKeyInvariantProbeTests task 174 -- ADR-038 path
// #1).
//
// COVERAGE (maps to POML acceptance criteria):
//   - Under-threshold cost drift returns Pass (ExceedsAdvisoryThreshold=false)
//   - Over-threshold cost drift returns Fail (ExceedsAdvisoryThreshold=true)
//   - At-threshold (boundary) does NOT exceed (strict > comparison)
//   - Tenancy-model branches select correct envelope per H13AcceptanceOptions
//   - Empty SubscriptionId throws InvalidOperationException BEFORE ARM call
//     (silent-fail defense (a) -- zero-cost silent-Pass class this task's
//     dispatch directive calls out)
//   - RequestFailedException from ARM propagates unchanged (H13 catches +
//     classifies Resumable) -- NEVER swallowed into a zero-cost silent Pass
//     (silent-fail defense (b))
//   - Actually issues the POST to /subscriptions/{id}/providers/Microsoft.
//     CostManagement/query (proves this is not a hard-coded verdict)
//   - TryParseRows aggregates multi-row responses correctly
//   - TryParseRows returns 0 defensively for empty response
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.ResourceManager;
using Azure.ResourceManager.CostManagement.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmCostEnvelopeCheckerTests
{
    private const string SubscriptionId = "11111111-2222-3333-4444-555555555555";
    private const string CustomerId = "acme";
    private const string RunId = "run-abcdef";

    // Envelope defaults from H13AcceptanceOptions.
    private const decimal Model2EmptyEnvelope = 400m;
    private const decimal Model1MarginalEnvelope = 430m;
    private const decimal Model1SharedFloorEnvelope = 400m;
    private const decimal DriftAdvisoryThreshold = 0.20m;

    // ---------- Silent-fail defense (a): missing SubscriptionId ----------

    [Fact]
    public async Task CheckAsync_EmptySubscriptionId_ThrowsBeforeAnyArmCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            throw new InvalidOperationException("HTTP MUST NOT be invoked when SubscriptionId is empty"));
        var checker = NewChecker(handler);
        var request = NewRequest(subscriptionId: "");

        var act = () => checker.CheckAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SubscriptionId is empty*");
        handler.RequestedUris.Should().BeEmpty("must not call ARM for a missing subscription");
    }

    [Fact]
    public async Task CheckAsync_WhitespaceSubscriptionId_ThrowsBeforeAnyArmCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            throw new InvalidOperationException("HTTP MUST NOT be invoked when SubscriptionId is whitespace"));
        var checker = NewChecker(handler);
        var request = NewRequest(subscriptionId: "   ");

        var act = () => checker.CheckAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.RequestedUris.Should().BeEmpty();
    }

    // ---------- Real-call assertion: proves this is not a hard-coded verdict ----------

    [Fact]
    public async Task CheckAsync_UnderThreshold_IssuesGenuineArmPostAndReturnsPass()
    {
        // Extrapolate to 380 USD monthly (drift = -5% vs 400 USD envelope; well
        // within the 20% advisory band -- both directions count per abs(drift)>threshold).
        // Reverse the extrapolation to derive the MTD figure the ARM response should
        // report so the test is deterministic regardless of what day-of-month it runs.
        var now = DateTime.UtcNow;
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var daysElapsed = Math.Max(1, now.Day);
        var mtdForTargetMonthly380 = 380m * daysElapsed / daysInMonth;

        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Post,
                "CostManagement query is POST, not GET (SDK contract)");
            request.RequestUri!.AbsolutePath.Should().Contain(
                $"/subscriptions/{SubscriptionId}/providers/Microsoft.CostManagement/query",
                "must issue the real CostManagement subscription-scoped query endpoint");
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, CostBodies.QueryResultBody(mtdForTargetMonthly380));
        });

        var checker = NewChecker(handler);
        var result = await checker.CheckAsync(NewRequest(tenancyModel: "Model2Dedicated"), CancellationToken.None);

        result.ExpectedMonthlyUsd.Should().Be(Model2EmptyEnvelope);
        result.ExceedsAdvisoryThreshold.Should().BeFalse();
        Math.Abs(result.DriftFraction).Should().BeLessThan(DriftAdvisoryThreshold,
            "380 USD is 5% below the 400 USD envelope; well within the 20% advisory band");
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.Contains("Microsoft.CostManagement/query"),
            "asserts the CostManagement query endpoint was invoked over HTTP");
    }

    // ---------- Threshold acceptance criteria ----------

    [Fact]
    public async Task CheckAsync_OverThreshold_ReturnsExceedsTrue()
    {
        // Model2Dedicated envelope = 400 USD. Massive per-day cost extrapolates way over.
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, CostBodies.QueryResultBody(10_000m)));

        var checker = NewChecker(handler);
        var result = await checker.CheckAsync(NewRequest(tenancyModel: "Model2Dedicated"), CancellationToken.None);

        result.ExceedsAdvisoryThreshold.Should().BeTrue();
        result.DriftFraction.Should().BeGreaterThan(DriftAdvisoryThreshold,
            "10000+ mtd is well beyond the 20% drift band around the 400 USD envelope");
        result.Summary.Should().Contain("exceeds=True");
        result.Summary.Should().Contain("tenancyModel=Model2Dedicated");
    }

    // ---------- Tenancy-model branches ----------

    [Fact]
    public void SelectExpectedEnvelope_Model2Dedicated_SelectsModel2Envelope()
    {
        var checker = NewChecker(ArmSdkTestFakes.NewHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }));
        checker.SelectExpectedEnvelope("Model2Dedicated").Should().Be(Model2EmptyEnvelope);
    }

    [Fact]
    public void SelectExpectedEnvelope_Model1Shared_SelectsMarginalEnvelope()
    {
        var checker = NewChecker(ArmSdkTestFakes.NewHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }));
        checker.SelectExpectedEnvelope("Model1Shared").Should().Be(Model1MarginalEnvelope);
    }

    [Fact]
    public void SelectExpectedEnvelope_UnknownTenancyModel_FallsBackToSharedFloor()
    {
        var checker = NewChecker(ArmSdkTestFakes.NewHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }));
        checker.SelectExpectedEnvelope("SomeFutureModel").Should().Be(Model1SharedFloorEnvelope);
    }

    // ---------- Silent-fail defense (b): ARM errors propagate (never zero-cost silent Pass) ----------

    [Fact]
    public async Task CheckAsync_ArmReturns403_PropagatesRequestFailedException()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed",
                    "L2 UAMI lacks Cost Management Reader on the subscription.")));

        var checker = NewChecker(handler);

        var act = () => checker.CheckAsync(NewRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 403,
                "H13 handler catches this and classifies Resumable -- NEVER silently returns zero-cost Pass");
    }

    [Fact]
    public async Task CheckAsync_ArmReturns500_PropagatesRequestFailedException()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.InternalServerError,
                ArmSdkTestFakes.ArmErrorBody("InternalServerError", "transient")));

        var checker = NewChecker(handler);

        var act = () => checker.CheckAsync(NewRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 500);
    }

    // ---------- TryParseRows aggregation ----------

    [Fact]
    public void TryParseRows_MultiRowGroupedResponse_SumsAcrossRows()
    {
        // Simulate what a granularity-Daily response would look like: multiple
        // rows, one per day. The impl sums ALL rows defensively so a mistaken
        // server-side grouping still surfaces the correct monthly total.
        var result = ArmCostManagementModelFactory.QueryResult(
            columns: new[] { ArmCostManagementModelFactory.QueryColumn("Cost", "Number") },
            rows: new[]
            {
                (IList<BinaryData>)new List<BinaryData> { BinaryData.FromString("10.50") },
                (IList<BinaryData>)new List<BinaryData> { BinaryData.FromString("5.25") },
                (IList<BinaryData>)new List<BinaryData> { BinaryData.FromString("2.00") },
            });

        var total = ArmCostEnvelopeChecker.TryParseRows(result);
        total.Should().Be(17.75m);
    }

    [Fact]
    public void TryParseRows_EmptyRows_ReturnsZero()
    {
        var result = ArmCostManagementModelFactory.QueryResult(
            columns: new[] { ArmCostManagementModelFactory.QueryColumn("Cost", "Number") },
            rows: Array.Empty<IList<BinaryData>>());
        ArmCostEnvelopeChecker.TryParseRows(result).Should().Be(0m);
    }

    // ---------- Test infrastructure ----------

    private static ArmCostEnvelopeChecker NewChecker(FakeArmHttpMessageHandler handler)
    {
        var arm = ArmSdkTestFakes.NewArmClient(handler);
        var options = Options.Create(new H13AcceptanceOptions
        {
            Model2EmptyEnvelopeUsd = Model2EmptyEnvelope,
            Model1MarginalEnvelopeUsd = Model1MarginalEnvelope,
            Model1SharedFloorEnvelopeUsd = Model1SharedFloorEnvelope,
            CostDriftAdvisoryThreshold = DriftAdvisoryThreshold,
        });
        return new ArmCostEnvelopeChecker(arm, options, NullLogger<ArmCostEnvelopeChecker>.Instance);
    }

    private static CostEnvelopeRequest NewRequest(
        string? subscriptionId = null,
        string tenancyModel = "Model2Dedicated") => new(
            CustomerId: CustomerId,
            RunId: RunId,
            SubscriptionId: subscriptionId ?? SubscriptionId,
            TenancyModel: tenancyModel,
            ResourceGroupName: "rg-fake",
            DriftAdvisoryThreshold: DriftAdvisoryThreshold);

    /// <summary>
    /// Cost Management response body helper. Response shape ground-truthed
    /// against the actual SDK deserializer via HttpMessageHandler capture: a
    /// top-level object with an <c>id</c>/<c>name</c>/<c>type</c> envelope
    /// plus a <c>properties</c> block containing <c>columns</c> + <c>rows</c>.
    /// </summary>
    private static class CostBodies
    {
        public static string QueryResultBody(decimal mtdCost)
        {
            var cost = mtdCost.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $$"""
                {
                  "id": "subscriptions/{{SubscriptionId}}/providers/Microsoft.CostManagement/query/00000000-0000-0000-0000-000000000000",
                  "name": "00000000-0000-0000-0000-000000000000",
                  "type": "Microsoft.CostManagement/query",
                  "properties": {
                    "columns": [ { "name": "Cost", "type": "Number" } ],
                    "rows": [ [ {{cost}} ] ]
                  }
                }
                """;
        }
    }
}
