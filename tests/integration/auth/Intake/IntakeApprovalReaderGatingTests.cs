using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Intake;

/// <summary>
/// spaarke-intake-pipeline-r1 task 017: the intake approval Signal (and so its plan, its commit and its Decision Record, which are
/// read through the same check) is visible only to a caller who can read the request itself. The REAL
/// <see cref="SignalCoreRecordAccess"/> decides: the caller reads the Signal, then the Signal's core record, here the service request,
/// as themselves; any refusal or an unknown core table is the uniform NotFound. No code was added for this: the test pins that
/// <c>sprk_servicerequest</c> is gated by the caller's own read like every other core type.
/// </summary>
/// <remarks>Maintain-class (ADR-038, security path). An external caller holding a grant on a related record (a matter, say) still
/// reads the request as themselves, so their case is the "request not readable" row below.</remarks>
public sealed class IntakeApprovalReaderGatingTests
{
    private static readonly Guid SignalId = Guid.Parse("01700000-0000-4000-8000-0000000005a1");
    private static readonly Guid VersionId = Guid.Parse("01700000-0000-4000-8000-0000000005a2");
    private static readonly Guid CoreTypeId = Guid.Parse("01700000-0000-4000-8000-0000000005a3");
    private static readonly Guid RequestId = Guid.Parse("01700000-0000-4000-8000-0000000005a4");

    private static SignalCoreRecordAccess Access(bool signalReadable = true, int? requestStatus = 200, string coreTable = "sprk_servicerequest")
    {
        var users = new Mock<IDataverseUserClient>(MockBehavior.Strict);
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(signalReadable
                ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new Dictionary<string, object>
                {
                    ["sprk_lane"] = (int)DecisionLane.Decide,
                    ["_sprk_policyversion_value"] = VersionId,
                    ["_sprk_corerecordtype_value"] = CoreTypeId,
                    ["sprk_corerecordid"] = RequestId.ToString(),
                }))
                : DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "not readable"));
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_servicerequests(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(requestStatus == 200
                ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { createdon = "2026-11-28T00:00:00Z" }))
                : DataverseUserResponse.Fail(requestStatus ?? 404, requestStatus == 403 ? "Forbidden" : DataverseUserClientErrorCodes.NotFound, "not readable"));

        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", CoreTypeId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", CoreTypeId) { ["sprk_recordlogicalname"] = coreTable });
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_servicerequest", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_servicerequests");

        return new SignalCoreRecordAccess(users.Object, entities.Object, new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object,
            NullLogger<SignalCoreRecordAccess>.Instance);
    }

    [Fact]
    public async Task ACallerWhoReadsTheRequest_SeesItsSignal_WithTheRequestAsItsCore()
    {
        CoreAncestorResolver.CoreRecordEntities.Should().Contain("sprk_servicerequest", "a service request is an access-control core type");

        var decision = await Access().AuthorizeAsync(SignalId, CancellationToken.None);

        decision.Outcome.Should().Be(SignalAccessOutcome.Allowed);
        decision.CoreTable.Should().Be("sprk_servicerequest");
        decision.CoreId.Should().Be(RequestId);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    public async Task ACallerWhoCannotReadTheRequest_GetsTheSameNotFoundAsAnUnknownSignal(int status)
    {
        var denied = await Access(requestStatus: status).AuthorizeAsync(SignalId, CancellationToken.None);
        var unknown = await Access(signalReadable: false).AuthorizeAsync(SignalId, CancellationToken.None);

        denied.Should().Be(SignalAccessDecision.NotFound);
        unknown.Should().Be(SignalAccessDecision.NotFound);
    }

    [Fact]
    public async Task ACoreTableThatIsNotACoreType_FailsClosed()
    {
        var decision = await Access(coreTable: "sprk_widget").AuthorizeAsync(SignalId, CancellationToken.None);

        decision.Should().Be(SignalAccessDecision.NotFound);
    }
}
