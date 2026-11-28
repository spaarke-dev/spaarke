using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarke-intake-pipeline-r1 task 017: the intake approval policy evaluated by the REAL job, evaluator and writer over the
/// <see cref="Harness"/> seams. A request awaiting approval raises exactly one Decide-lane Signal about the request itself (its own core
/// record), owned by the team the ownership resolver answers; a second pass (the trigger redelivered, or the nightly backstop) raises no
/// second Signal.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). The harness's fake does not apply option-set conditions, so the
/// stage filter itself (<c>sprk_intakestage eq 100000003</c>, which an inquiry's empty stage never meets) is pinned on the compiled
/// FetchXML in <c>IntakeApprovalPolicyDefinitionTests</c>.</remarks>
public partial class SignalReevaluationJobTests
{
    private static readonly Guid IntakeRequestId = Guid.Parse("01700000-0000-4000-8000-0000000000e1");
    private const string IntakePolicyCode = "POL-INTAKE-APPROVAL";
    private const string IntakeBody = """{"type":"Existence","subject":"sprk_servicerequest","when":{"sprk_intakestage":100000003},"all":[]}""";

    [Fact]
    public async Task Run_IntakeApprovalPolicy_RaisesOneDecideSignalAboutTheRequest_AndASecondPassRaisesNoSecond_017()
    {
        var h = new Harness();
        h.Sysadmin.AddPolicy(IntakePolicyCode, SignalWriter.LaneDecide, true, IntakeBody, "This intake request is awaiting approval.");
        h.Sysadmin.SubjectRows["sprk_servicerequest"] = new List<Entity>
        {
            new("sprk_servicerequest", IntakeRequestId)
            {
                ["sprk_servicerequestid"] = IntakeRequestId,
                ["sprk_intakestage"] = new OptionSetValue(100000003),
            },
        };

        var first = await h.RunAsync();

        first.Success.Should().BeTrue();
        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<OptionSetValue>("sprk_lane").Value.Should().Be(SignalWriter.LaneDecide);
        created.GetAttributeValue<EntityReference>("sprk_regardingservicerequest").Id.Should().Be(IntakeRequestId);
        created.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(IntakeRequestId), "the request is its own core record");
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId));
        created.GetAttributeValue<string>("sprk_dedupekey").Should().Be($"{IntakePolicyCode}|sprk_servicerequest|{Lower(IntakeRequestId)}|1");

        h.Org.ResetCounts();
        var second = await h.RunAsync();

        second.Success.Should().BeTrue();
        h.Org.CreateCalls.Should().Be(0, "the open Signal is found by its dedupe key; no second Signal");
    }
}
