// -----------------------------------------------------------------------------
// ArmSlotSwapperTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmSlotSwapper (task 132, Wave G-3). Proves
// the REAL Azure.ResourceManager.AppService SDK swap call path
// (GetWebSiteSlotAsync + WebSiteSlotResource.SwapSlotAsync as an awaited LRO)
// via a fake HttpClientTransport — parity with ArmAppServiceIdentityPatcherTests.cs
// (task 125, the T1-PATCH sibling this class's swap call pairs with). This is
// also the ROLLBACK-PATH COMPLETENESS PROOF at the collaborator/SDK-shape
// layer (task 132 dispatch directive #5): T4 below proves TWO consecutive
// identical swap invocations (the exact shape H9BffDeployHandler's rollback
// re-swap performs) both genuinely reach ARM over HTTP. ADR-038 path #1.
//
// GROUND-TRUTHED SDK SHAPES (verified via reflection against the installed
// Azure.ResourceManager.AppService 1.5.0 package BEFORE writing this file):
//   - WebSiteResource.GetWebSiteSlotAsync(string, ct) -> Task<Response<WebSiteSlotResource>>
//   - WebSiteSlotResource.SwapSlotAsync(WaitUntil, CsmSlotEntity, ct) -> Task<ArmOperation>
//   - CsmSlotEntity(string targetSlot, bool preserveVnet) — positional ctor.
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmSlotSwapperTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "spaarke-bff-acme";
    private const string SourceSlotName = "staging";
    private const string TargetSlotName = "production";

    private static SlotSwapRequest NewRequest() => new(
        SubscriptionId: SubscriptionId,
        ResourceGroupName: ResourceGroupName,
        AppServiceName: AppServiceName,
        SourceSlotName: SourceSlotName,
        TargetSlotName: TargetSlotName);

    private static string SlotBody() => $$"""
        {
          "id": "/subscriptions/{{SubscriptionId}}/resourceGroups/{{ResourceGroupName}}/providers/Microsoft.Web/sites/{{AppServiceName}}/slots/{{SourceSlotName}}",
          "name": "{{AppServiceName}}/{{SourceSlotName}}",
          "location": "westus2",
          "properties": {}
        }
        """;

    // ---------- T1 successful swap — request reaches ARM over HTTP ----------

    [Fact]
    public async Task SwapAsync_Success_LooksUpSourceSlotThenSwapsOverHttp_ReturnsSuccess()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/" + SourceSlotName) && request.Method == HttpMethod.Get)
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SlotBody());
            }
            // The SwapSlotAsync POST — Azure Core treats a 200 (not 202)
            // initial response as an already-complete LRO (parity with task
            // 123's ArmDeploymentRunnerTests fake convention for
            // WaitUntil.Completed calls).
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, "{}");
        });

        var swapper = new ArmSlotSwapper(
            ArmSdkTestFakes.NewArmClient(handler),
            Options.Create(new BffDeployOptions()),
            NullLogger<ArmSlotSwapper>.Instance);

        var result = await swapper.SwapAsync(NewRequest(), CancellationToken.None);

        result.Should().BeOfType<SlotSwapResult.Success>();

        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.EndsWith("/slots/" + SourceSlotName),
            "the source-slot lookup (GetWebSiteSlotAsync) must genuinely reach ARM");
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.StartsWith($"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroupName}/providers/Microsoft.Web/sites/{AppServiceName}/slots/{SourceSlotName}/", StringComparison.Ordinal),
            "the SwapSlotAsync action call (a sub-path under the slot resource, e.g. .../slots/staging/slotsswap) must genuinely reach ARM — not just the lookup");
    }

    // ---------- T2 ARM error on swap — domain Failure, not throw ----------

    [Fact]
    public async Task SwapAsync_ArmRejectsSwap_ReturnsFailure_DoesNotThrow()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SlotBody());
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "The client does not have authorization to perform action Microsoft.Web/sites/slots/swap/action."));
        });

        var swapper = new ArmSlotSwapper(
            ArmSdkTestFakes.NewArmClient(handler),
            Options.Create(new BffDeployOptions()),
            NullLogger<ArmSlotSwapper>.Instance);

        var result = await swapper.SwapAsync(NewRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<SlotSwapResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("AuthorizationFailed");
        failure.Diagnostic.Should().Contain(SourceSlotName);
        failure.Diagnostic.Should().Contain(TargetSlotName);
    }

    // ---------- T3 argument guard ----------

    [Fact]
    public async Task SwapAsync_MissingAppServiceName_ThrowsWithoutCallingArm()
    {
        var swapper = new ArmSlotSwapper(
            ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call ARM"))),
            Options.Create(new BffDeployOptions()),
            NullLogger<ArmSlotSwapper>.Instance);

        var input = new SlotSwapRequest(SubscriptionId, ResourceGroupName, string.Empty, SourceSlotName, TargetSlotName);

        var act = async () => await swapper.SwapAsync(input, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---------- T4 rollback-path completeness: TWO identical invocations both reach ARM ----------

    [Fact]
    public async Task SwapAsync_InvokedTwiceWithIdenticalRequest_BothCallsIndependentlyReachArm()
    {
        var swapCallCount = 0;
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SlotBody());
            }
            swapCallCount++;
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, "{}");
        });

        var swapper = new ArmSlotSwapper(
            ArmSdkTestFakes.NewArmClient(handler),
            Options.Create(new BffDeployOptions()),
            NullLogger<ArmSlotSwapper>.Instance);

        var request = NewRequest();
        var first = await swapper.SwapAsync(request, CancellationToken.None);
        var second = await swapper.SwapAsync(request, CancellationToken.None);

        first.Should().BeOfType<SlotSwapResult.Success>();
        second.Should().BeOfType<SlotSwapResult.Success>();
        swapCallCount.Should().Be(2,
            "H9's rollback re-swap re-invokes SwapAsync with the IDENTICAL request — this proves BOTH " +
            "invocations independently reach ARM (no caching/memoization silently turning the rollback into a no-op)");
    }
}
