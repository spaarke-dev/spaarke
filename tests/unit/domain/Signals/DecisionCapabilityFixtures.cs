using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 013: builds the REAL <see cref="DecisionCapability"/> the worklist, decision-plan and suppression routes
/// now call, from the collaborators a test scripts. A collaborator the test leaves out is one its path never reaches: an object with no
/// state (<see cref="Unused{T}"/>), so a call into it fails the test loudly instead of passing quietly.
/// </summary>
internal static class DecisionCapabilityFixtures
{
    public const string TenantId = "tenant-013";

    public static readonly Guid CallerOid = Guid.Parse("01300000-0000-4000-8000-000000000001");
    public static readonly Guid SignalId = Guid.Parse("01300000-0000-4000-8000-0000000000a1");
    public static readonly Guid VersionId = Guid.Parse("01300000-0000-4000-8000-0000000000a2");
    public static readonly Guid CoreTypeId = Guid.Parse("01300000-0000-4000-8000-0000000000a3");
    public static readonly Guid MatterId = Guid.Parse("01300000-0000-4000-8000-0000000000a4");

    /// <summary>A Decide-lane plan whose only action needs no further caller read (no inquiry, budget or To Do check).</summary>
    public const string ApproveVariancePlan = """{"actions":["approve-variance"],"nextSteps":[]}""";

    /// <summary>A request as the routes see it after <c>RequireAuthorization</c>: an authenticated caller with a tenant and an object id.</summary>
    public static HttpContext SignedIn(string traceIdentifier = "trace-013", Guid? oid = null) =>
        new DefaultHttpContext
        {
            TraceIdentifier = traceIdentifier,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", TenantId), new Claim("oid", (oid ?? CallerOid).ToString("D"))],
                authenticationType: "Test")),
        };

    public static DecisionCapability Capability(
        SignalCoreRecordAccess? access = null,
        DecisionPlanService? plans = null,
        RuleBodyDescriber? describer = null,
        SignalSuppressionService? suppression = null,
        IDataverseUserClient? caller = null,
        SignalReadService? signalRead = null,
        DecisionPlanReadStamp? planReadStamp = null,
        DecisionParameterSources? parameterSources = null,
        ILogger<SignalSuppressionService>? suppressionLogger = null,
        DecisionCommitService? commits = null) =>
        new(
            signalRead ?? Unused<SignalReadService>(),
            access ?? Unused<SignalCoreRecordAccess>(),
            plans ?? Unused<DecisionPlanService>(),
            describer ?? Unused<RuleBodyDescriber>(),
            suppression ?? Unused<SignalSuppressionService>(),
            caller ?? new Mock<IDataverseUserClient>(MockBehavior.Strict).Object,
            suppressionLogger ?? NullLogger<SignalSuppressionService>.Instance,
            commits ?? Unused<DecisionCommitService>(),
            planReadStamp,
            parameterSources);

    /// <summary>
    /// A Decide-lane Signal on a matter, at the module boundaries: the caller's Dataverse client (strict, so an unscripted read fails)
    /// reads the Signal and, when <paramref name="matterReadable"/>, the matter; the BFF's own reader resolves the core type and the
    /// version's plan. The access check and the plan resolver are the real services.
    /// </summary>
    public static (SignalCoreRecordAccess Access, DecisionPlanService Plans, Mock<IDataverseUserClient> Users, Mock<IGenericEntityService> Entities)
        DecideSignalOnAMatter(bool matterReadable = true, string plan = ApproveVariancePlan)
    {
        var users = new Mock<IDataverseUserClient>(MockBehavior.Strict);
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(new Dictionary<string, object>
            {
                ["sprk_lane"] = (int)DecisionLane.Decide,
                ["_sprk_policyversion_value"] = VersionId,
                ["_sprk_corerecordtype_value"] = CoreTypeId,
                ["sprk_corerecordid"] = MatterId.ToString(),
            }));
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_matters(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterReadable
                ? Ok(new { createdon = "2026-11-07T00:00:00Z" })
                : DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "not readable"));

        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", CoreTypeId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", CoreTypeId) { ["sprk_recordlogicalname"] = "sprk_matter" });
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
        entities.Setup(e => e.RetrieveAsync("sprk_policyversion", VersionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_policyversion", VersionId) { ["sprk_decisionplan"] = plan });

        var access = new SignalCoreRecordAccess(
            users.Object, entities.Object, new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object, NullLogger<SignalCoreRecordAccess>.Instance);
        return (access, new DecisionPlanService(entities.Object, NullLogger<DecisionPlanService>.Instance), users, entities);
    }

    /// <summary>The rule-body describer; these fixtures store no rule body, so the plan is served without a description.</summary>
    public static RuleBodyDescriber Describer()
    {
        var schema = new RuleBodySchemaValidator();
        return new RuleBodyDescriber(
            new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance),
            new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance);
    }

    private static DataverseUserResponse Ok(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));

    /// <summary>A collaborator this test's path never calls: no constructor ran, so any call into it throws.</summary>
    public static T Unused<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
