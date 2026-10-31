// -----------------------------------------------------------------------------
// ArmResourceNameAvailabilityProbeTests.cs
//
// T186 L23 — ArmResourceNameAvailabilityProbe through the REAL ARM SDK on a fake
// HTTP transport (parity with ArmSubscriptionReadinessProbeTests). The live
// defect: a resumed H2a found its own storage account 'taken' and failed with
// resource-name-taken. A name checkNameAvailability reports unavailable is now
// read in the stamp's resource group; 200 → ours (proceed), 404 → a real
// collision. ADR-038 path #1.
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmResourceNameAvailabilityProbeTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroup = "rg-spaarke-acme-prod";
    private const string StorageName = "sprkacmeprodsa";
    private const string ServiceBusName = "spaarke-acme-prod-sbus";

    private static readonly string StorageId =
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.Storage/storageAccounts/{StorageName}";
    private static readonly string ServiceBusId =
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.ServiceBus/namespaces/{ServiceBusName}";

    private static ResourceNameAvailabilityRequest Request(params ResourceNameCheckEntry[] names) =>
        new(SubscriptionId, names, ResourceGroup);

    private static ArmResourceNameAvailabilityProbe NewProbe(FakeArmHttpMessageHandler handler) =>
        new(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmResourceNameAvailabilityProbe>.Instance);

    private static HttpResponseMessage Respond(
        HttpRequestMessage request, bool storageAvailable, HttpStatusCode? ownGet, bool serviceBusAvailable = true)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/providers/Microsoft.Storage/checkNameAvailability", StringComparison.OrdinalIgnoreCase))
        {
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, storageAvailable
                ? """{"nameAvailable":true}"""
                : """{"nameAvailable":false,"reason":"AlreadyExists","message":"The storage account named sprkacmeprodsa is already taken."}""");
        }
        if (path.EndsWith("/providers/Microsoft.ServiceBus/CheckNameAvailability", StringComparison.OrdinalIgnoreCase))
        {
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, serviceBusAvailable
                ? """{"nameAvailable":true}"""
                : """{"nameAvailable":false,"reason":"NameInUse","message":"The namespace name is already in use."}""");
        }
        if (path.Equals(StorageId, StringComparison.OrdinalIgnoreCase) || path.Equals(ServiceBusId, StringComparison.OrdinalIgnoreCase))
        {
            if (ownGet != HttpStatusCode.OK)
            {
                return ArmSdkTestFakes.JsonResponse(ownGet ?? HttpStatusCode.NotFound,
                    ArmSdkTestFakes.ArmErrorBody("ResourceNotFound", "not found"));
            }
            var type = path.Contains("Microsoft.Storage", StringComparison.OrdinalIgnoreCase)
                ? "Microsoft.Storage/storageAccounts"
                : "Microsoft.ServiceBus/namespaces";
            var name = path.Split('/')[^1];
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                $$"""{"id":"{{path}}","name":"{{name}}","type":"{{type}}","location":"westus2"}""");
        }
        throw new InvalidOperationException("unexpected request: " + path);
    }

    [Fact]
    public async Task StorageNameTakenByThisStamp_IsNotACollision()
    {
        var handler = ArmSdkTestFakes.NewHandler(r => Respond(r, storageAvailable: false, ownGet: HttpStatusCode.OK));

        var result = await NewProbe(handler).CheckAvailabilityAsync(
            Request(new ResourceNameCheckEntry(ResourceNameKind.StorageAccount, StorageName)), CancellationToken.None);

        result.Should().BeOfType<ResourceNameAvailabilityResult.AllAvailable>(
            "a re-run of H2a finds its own storage account taken — that is the stamp, not a collision (T186 L23)");
        handler.RequestedUris.Should().Contain(u => u.AbsolutePath.Equals(StorageId, StringComparison.OrdinalIgnoreCase),
            "ownership is decided by reading the account in the stamp's resource group");
    }

    [Fact]
    public async Task StorageNameTakenElsewhere_IsAConflict()
    {
        var handler = ArmSdkTestFakes.NewHandler(r => Respond(r, storageAvailable: false, ownGet: HttpStatusCode.NotFound));

        var result = await NewProbe(handler).CheckAvailabilityAsync(
            Request(new ResourceNameCheckEntry(ResourceNameKind.StorageAccount, StorageName)), CancellationToken.None);

        var conflict = result.Should().BeOfType<ResourceNameAvailabilityResult.Conflict>().Subject;
        conflict.Kind.Should().Be(ResourceNameKind.StorageAccount);
        conflict.ConflictingName.Should().Be(StorageName);
        conflict.Reason.Should().Contain("AlreadyExists");
    }

    [Fact]
    public async Task ServiceBusNameTakenByThisStamp_IsNotACollision()
    {
        var handler = ArmSdkTestFakes.NewHandler(r =>
            Respond(r, storageAvailable: true, ownGet: HttpStatusCode.OK, serviceBusAvailable: false));

        var result = await NewProbe(handler).CheckAvailabilityAsync(
            Request(
                new ResourceNameCheckEntry(ResourceNameKind.StorageAccount, StorageName),
                new ResourceNameCheckEntry(ResourceNameKind.ServiceBusNamespace, ServiceBusName)),
            CancellationToken.None);

        result.Should().BeOfType<ResourceNameAvailabilityResult.AllAvailable>();
        handler.RequestedUris.Should().Contain(u => u.AbsolutePath.Equals(ServiceBusId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OwnershipReadFailsOtherThan404_IsReportedAsAConflict()
    {
        var handler = ArmSdkTestFakes.NewHandler(r => Respond(r, storageAvailable: false, ownGet: HttpStatusCode.Forbidden));

        var result = await NewProbe(handler).CheckAvailabilityAsync(
            Request(new ResourceNameCheckEntry(ResourceNameKind.StorageAccount, StorageName)), CancellationToken.None);

        result.Should().BeOfType<ResourceNameAvailabilityResult.Conflict>()
            .Which.Reason.Should().Contain("403", "an unreadable resource is never assumed to be ours");
    }

    [Fact]
    public async Task AvailableName_IsNotReadInTheResourceGroup()
    {
        var handler = ArmSdkTestFakes.NewHandler(r => Respond(r, storageAvailable: true, ownGet: null));

        var result = await NewProbe(handler).CheckAvailabilityAsync(
            Request(new ResourceNameCheckEntry(ResourceNameKind.StorageAccount, StorageName)), CancellationToken.None);

        result.Should().BeOfType<ResourceNameAvailabilityResult.AllAvailable>();
        handler.RequestedUris.Should().NotContain(u => u.AbsolutePath.Contains("/resourceGroups/", StringComparison.OrdinalIgnoreCase));
    }
}
